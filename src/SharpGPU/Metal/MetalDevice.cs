using System;
using System.IO;
using SharpMetal.Metal;
using SharpGPU.Collections;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;
using System.Threading;

namespace SharpGPU
{
    internal sealed class MetalDevice : RHIDevice
    {
        public MTLDevice NativeDevice => m_NativeDevice;
        public MetalInstance MetalInstance => m_MetalInstance;
        public override ERHIBackend BackendType => ERHIBackend.Metal;
        internal bool SupportsMetal4Barriers => m_SupportsMetal4Barriers;
        internal bool SupportsMetal4 => m_SupportsMetal4;
        internal bool SupportsArgumentTable => m_SupportsArgumentTable;
        internal bool SupportsPlacementSparse => m_SupportsPlacementSparse;
        internal bool SupportsMetalML => Capabilities.MachineLearning.Execution.Tier != ERHICapabilityTier.Unavailable;
        internal MetalRasterCapabilities RasterCapabilities => m_RasterCapabilities;
        internal string? TimestampQueriesUnavailableReason => m_TimestampQueriesUnavailableReason;
        internal string? MetalMLUnavailableReason => m_MetalMLUnavailableReason;
        internal MTLTextureViewPool TextureViewPool => m_TextureViewPool;

        private readonly MTLDevice m_NativeDevice;
        private readonly MetalInstance m_MetalInstance;
        private readonly bool m_SupportsMetal4Barriers;
        private readonly bool m_SupportsMetal3;
        private readonly bool m_SupportsMetal4;
        private readonly bool m_SupportsArgumentTable;
        private readonly bool m_SupportsPlacementSparse;
        private readonly MetalRasterCapabilities m_RasterCapabilities;
        private string? m_TimestampQueriesUnavailableReason;
        private string? m_MetalMLUnavailableReason;
        private MTLTextureViewPool m_TextureViewPool;
        private readonly MetalTextureViewIndexAllocator m_TextureViewIndices = new();
        // Metal 4 ML package objects are retained for the device lifetime. Releasing argument
        // tables / intermediates heaps / pipeline libraries at per-program disposal time made
        // subsequent native ML dispatches observe zeroed outputs on macOS 26.5.
        private readonly List<string> m_MetalMLPackageDirectories = new List<string>();
        private readonly List<MTLLibrary> m_MetalMLLibraries = new List<MTLLibrary>();
        private readonly List<MTL4MachineLearningPipelineState> m_MetalMLPipelineStates = new List<MTL4MachineLearningPipelineState>();
        private readonly List<MTL4ArgumentTable> m_MetalMLArgumentTables = new List<MTL4ArgumentTable>();
        private readonly List<MTLHeap> m_MetalMLIntermediatesHeaps = new List<MTLHeap>();
        private RHIException? m_PendingCommandQueueFailure;

        private static readonly Selector s_RespondsToSelector = "respondsToSelector:";
        private static readonly Selector s_NewArgumentTableWithDescriptorError = "newArgumentTableWithDescriptor:error:";
        private static readonly Selector s_NewCompilerWithDescriptorError = "newCompilerWithDescriptor:error:";
        private static readonly Selector s_NewCounterHeapWithDescriptorError = "newCounterHeapWithDescriptor:error:";
        private static readonly Selector s_NewTensorWithDescriptorError = "newTensorWithDescriptor:error:";
        private static readonly Selector s_SupportsPlacementSparse = "supportsPlacementSparse";

        public MetalDevice(MetalInstance instance, in MTLDevice device, in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_MetalInstance = instance;
            m_NativeDevice = device;

            if (m_NativeDevice.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal device pointer is null.");
            }

            m_Name = m_NativeDevice.Name.ToString() ?? string.Empty;
            m_Type = m_NativeDevice.IsHeadless ? ERHIDeviceType.Software : ERHIDeviceType.Hardware;
            m_VendorId.IntValue = (uint)ERHIVendorType.Apple;
            m_DeviceId.IntValue = (uint)(m_NativeDevice.RegistryID & uint.MaxValue);
            m_DriverVersion = "Metal-" + Environment.OSVersion.Version.ToString();
            m_SupportsMetal3 = SafeSupportsFamily(MTLGPUFamily.Metal3);
            m_SupportsMetal4 = SafeSupportsFamily(MTLGPUFamily.Metal4);
            m_SupportsArgumentTable = m_SupportsMetal4 && SafeSupportsSelector(s_NewArgumentTableWithDescriptorError);
            m_SupportsMetal4Barriers = m_SupportsMetal4;
            m_SupportsPlacementSparse =
                m_SupportsMetal4 &&
                SafeSupportsPlacementSparse();

            if (!m_SupportsMetal4)
            {
                throw new NotSupportedException("Metal backend requires Metal 4 support.");
            }

            if (!m_SupportsArgumentTable)
            {
                throw new NotSupportedException("Metal backend requires MTL4 argument table support.");
            }

            m_RasterCapabilities = ProbeRasterCapabilities();
            BuildLimitAndFeature();
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
            CreateTextureViewPool();
        }

        internal void ReportCommandQueueFeedback(in NSError error)
        {
            if (error.NativePtr == IntPtr.Zero)
            {
                return;
            }

            long nativeCode = error.Code;
            MTL4CommandQueueError queueError =
                (MTL4CommandQueueError)nativeCode;
            (ERHIErrorCode errorCode, ERHIDeviceState deviceState) =
                queueError switch
                {
                    MTL4CommandQueueError.OutOfMemory =>
                        (ERHIErrorCode.OutOfMemory, ERHIDeviceState.Operational),
                    MTL4CommandQueueError.DeviceRemoved =>
                        (ERHIErrorCode.DeviceLost, ERHIDeviceState.Removed),
                    MTL4CommandQueueError.AccessRevoked =>
                        (ERHIErrorCode.DeviceLost, ERHIDeviceState.Lost),
                    MTL4CommandQueueError.Timeout or
                    MTL4CommandQueueError.NotPermitted or
                    MTL4CommandQueueError.Internal =>
                        (ERHIErrorCode.SubmissionFailed, ERHIDeviceState.Operational),
                    _ =>
                        (ERHIErrorCode.NativeFailure, ERHIDeviceState.Operational),
                };

            string nativeMessage;
            try
            {
                nativeMessage = error.LocalizedDescription.ToString();
            }
            catch
            {
                nativeMessage =
                    $"MTL4 command queue feedback reported '{queueError}'.";
            }
            if (string.IsNullOrWhiteSpace(nativeMessage))
            {
                nativeMessage =
                    $"MTL4 command queue feedback reported '{queueError}'.";
            }

            RHIException diagnostic = new(
                errorCode,
                ERHIBackend.Metal,
                nativeCode,
                nativeMessage,
                deviceState);
            if (errorCode == ERHIErrorCode.DeviceLost)
            {
                MarkDeviceLost(diagnostic);
                return;
            }

            _ = Interlocked.CompareExchange(
                ref m_PendingCommandQueueFailure,
                diagnostic,
                null);
        }

        internal void ThrowIfCommandQueueFailed()
        {
            ThrowIfDeviceUnavailable();
            RHIException? diagnostic = Interlocked.Exchange(
                ref m_PendingCommandQueueFailure,
                null);
            if (diagnostic != null)
            {
                throw diagnostic;
            }
        }

        public override RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index)
        {
            ThrowIfCommandQueueFailed();
            if (m_CommandQueueMap == null)
            {
                return null;
            }

            if (m_CommandQueueMap.TryGetValue(pipeline, out TArray<RHICommandQueue>? queues) && queues != null)
            {
                if ((uint)index < (uint)queues.length)
                {
                    return queues[index];
                }
            }

            return null;
        }

        public override RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            ThrowIfCommandQueueFailed();
            Capabilities.Presentation.SwapChain.Require(
                "Metal swapchain creation");
            return new MetalSwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            ThrowIfCommandQueueFailed();
            return new MetalFence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            ThrowIfCommandQueueFailed();
            return new MetalSemaphore(this);
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            throw new NotSupportedException(
                "Metal StorageQueue is unavailable because SharpGPU has no official native storage API for this backend.");
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            if ((descriptor.Type == ERHIQueryType.TimestampTransfer || descriptor.Type == ERHIQueryType.Timestamp)
                && Capabilities.Synchronization.TimestampQueries.Tier == ERHICapabilityTier.Unavailable)
            {
                throw new NotSupportedException(m_TimestampQueriesUnavailableReason ?? "Metal timestamp queries require native MTL4CounterHeap support.");
            }

            if (descriptor.Type == ERHIQueryType.Statistics && Capabilities.Synchronization.PipelineStatisticsQueries.Tier == ERHICapabilityTier.Unavailable)
            {
                throw new NotSupportedException("Metal pipeline statistics queries require a device statistics counter set.");
            }

            return new MetalQuery(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            ThrowIfDisposed();
            if (MetalSparseMemoryUtility.RequiresPlacementSparseCompatibility(
                    descriptor.Compatibility))
            {
                Capabilities.Memory.SparseBinding.Require(
                    "Metal placement-sparse heap creation");
            }
            else
            {
                Capabilities.Memory.PlacedResources.Require("Metal heap creation");
            }
            return new MetalHeap(this, descriptor);
        }

        public override RHIResourceMemoryRequirements GetBufferMemoryRequirements(
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Metal buffer memory requirements");
            MTLResourceOptions options = MetalMemoryUtility.GetBufferOptions(descriptor);
            MTLSizeAndAlign nativeRequirements =
                m_NativeDevice.HeapBufferSizeAndAlign((ulong)descriptor.ByteSize, options);
            return CreateMemoryRequirements(
                nativeRequirements,
                descriptor.StorageMode,
                ERHIMemoryResourceKind.Buffer);
        }

        public override RHIResourceMemoryRequirements GetTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Metal texture memory requirements");
            MTLTextureDescriptor nativeDescriptor =
                MetalMemoryUtility.BuildTextureDescriptor(descriptor);
            try
            {
                MTLSizeAndAlign nativeRequirements =
                    m_NativeDevice.HeapTextureSizeAndAlign(nativeDescriptor);
                return CreateMemoryRequirements(
                    nativeRequirements,
                    descriptor.StorageMode,
                    ERHIMemoryResourceKind.Texture);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }
        }

        public override RHISparseTextureMemoryRequirements
            GetSparseTextureMemoryRequirements(
                in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.SparseBinding.Require(
                "Metal sparse texture memory requirements");
            MTLTexture nativeTexture =
                MetalSparseMemoryUtility.CreateSparseTexture(
                    this,
                    descriptor);
            try
            {
                return MetalSparseMemoryUtility.QueryRequirements(
                    this,
                    descriptor,
                    nativeTexture);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeTexture.NativePtr);
            }
        }

        public override RHITexture CreateSparseTexture(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.SparseBinding.Require(
                "Metal sparse texture creation");
            return new MetalTexture(this, descriptor, createSparse: true);
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new MetalBuffer(this, descriptor);
        }

        public override RHIBuffer CreatePlacedBuffer(
            RHIHeap heap,
            ulong heapOffset,
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Metal placed buffer creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not MetalHeap metalHeap ||
                !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException(
                    "Placed buffer heap was created by a different backend or device.",
                    nameof(heap));
            }

            RHIResourceMemoryRequirements requirements =
                GetBufferMemoryRequirements(descriptor);
            RHIHeapPlacement placement =
                heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new MetalBuffer(
                    this,
                    descriptor,
                    metalHeap,
                    heapOffset,
                    placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new MetalTexture(this, descriptor);
        }

        public override RHITexture CreatePlacedTexture(
            RHIHeap heap,
            ulong heapOffset,
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Metal placed texture creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not MetalHeap metalHeap ||
                !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException(
                    "Placed texture heap was created by a different backend or device.",
                    nameof(heap));
            }

            RHIResourceMemoryRequirements requirements =
                GetTextureMemoryRequirements(descriptor);
            RHIHeapPlacement placement =
                heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new MetalTexture(
                    this,
                    descriptor,
                    metalHeap,
                    heapOffset,
                    placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        private RHIResourceMemoryRequirements CreateMemoryRequirements(
            in MTLSizeAndAlign nativeRequirements,
            ERHIStorageMode storageMode,
            ERHIMemoryResourceKind resourceKind)
        {
            if (nativeRequirements.size == 0 ||
                nativeRequirements.align == 0)
            {
                throw new ArgumentException(
                    "Metal rejected the resource descriptor while querying heap requirements.");
            }

            return new RHIResourceMemoryRequirements(
                this,
                nativeRequirements.size,
                nativeRequirements.align,
                storageMode,
                1UL,
                resourceKind);
        }

        public override RHIMemoryBudget QueryMemoryBudget(
            ERHIStorageMode storageMode)
        {
            ThrowIfDisposed();
            Capabilities.Memory.BudgetQuery.Require(
                "Metal memory budget query");
            if (!Enum.IsDefined(storageMode) ||
                storageMode == ERHIStorageMode.Pending)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storageMode),
                    storageMode,
                    "Unknown storage mode.");
            }

            bool deviceLocalDomain =
                storageMode is ERHIStorageMode.GPULocal or
                    ERHIStorageMode.Memoryless;
            if (!deviceLocalDomain && !m_NativeDevice.HasUnifiedMemory)
            {
                throw new NotSupportedException(
                    "Metal exposes only a device-wide recommended working-set budget; " +
                    "a discrete-memory device cannot report an exact host-visible segment budget.");
            }

            ulong budgetBytes = m_NativeDevice.RecommendedMaxWorkingSetSize;
            if (budgetBytes == 0)
            {
                throw new NotSupportedException(
                    "MTLDevice did not report a recommended maximum working-set size.");
            }

            return new RHIMemoryBudget(
                storageMode,
                budgetBytes,
                m_NativeDevice.CurrentAllocatedSize);
        }

        public override void RequestResidency(
            in RHIResidencyRequestDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException(
                "Metal residency sets do not provide the explicit fence-completion " +
                "contract required by RHIResidencyRequestDescriptor.");
        }

        public override RHISampler CreateSampler(in RHISamplerDescriptor descriptor)
        {
            return new MetalSampler(this, descriptor);
        }

        public override RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            return new MetalTopLevelAccelStruct(this, descriptor);
        }

        public override RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            return new MetalBottomLevelAccelStruct(this, descriptor);
        }

        public override RHIArgumentTableLayout CreateArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            return new MetalArgumentTableLayout(this, descriptor);
        }

        public override RHIArgumentTable CreateArgumentTable(in RHIArgumentTableDescriptor descriptor)
        {
            return new MetalArgumentTable(this, descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new MetalPipelineLayout(this, descriptor);
        }

        public override RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor)
        {
            return new MetalFunction(this, descriptor);
        }

        public override RHIFunctionLibrary CreateFunctionLibrary(in RHIFunctionLibraryDescriptor descriptor)
        {
            return new MetalFunctionLibrary(this, descriptor);
        }

        public override RHIFunctionTable CreateFunctionTable()
        {
            return new MetalFunctionTable();
        }

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            return new MetalComputePipeline(this, descriptor);
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            return new MetalRaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            return new MetalRasterPipeline(this, descriptor);
        }

        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            if (!SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalMLUnavailableReason ?? "Metal ML is not supported on this device.");
            }

            return new MetalMLPipeline(this, descriptor);
        }

        public override RHIMLBindingSet CreateMLBindingSet(in RHIMLBindingSetDescriptor descriptor)
        {
            if (!SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalMLUnavailableReason ?? "Metal ML is not supported on this device.");
            }

            return new MetalMLBindingSet(this, descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            if (!SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalMLUnavailableReason ?? "Metal ML tensors are not supported on this device.");
            }

            return new MetalTensor(this, descriptor);
        }

        public override RHIMLProgram CreateMLProgram(in RHIMLProgramDescriptor descriptor)
        {
            if (!SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalMLUnavailableReason ?? "Metal ML is not supported on this device.");
            }

            if (descriptor.Ops.Length > 1)
            {
                throw new NotSupportedException(
                    "Metal ML MPSGraph package lowering currently supports one RHI ML op per native artifact. " +
                    "Multi-op packages execute once but corrupt subsequent MTL4MachineLearningCommandEncoder dispatches on macOS 26.5; " +
                    "compute-kernel and CPU fallbacks are intentionally rejected.");
            }

            try
            {
                MetalMpsGraphPackage package = MetalMpsGraphPackageBuilder.Build(this, descriptor);
                RegisterMetalMLPackageDirectory(package.PackageDirectory);
                RegisterMetalMLLibrary(package.Library);
                return new MetalMLProgram(
                    descriptor.Name,
                    package.Library,
                    MetalMpsGraphPackageBuilder.EntryName,
                    package.PackageDirectory,
                    package.BindingInfos);
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Metal ML program construction failed for '{descriptor.Name}': {ex.Message}", ex);
            }
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override RHIPipelineCache CreatePipelineCache()
        {
            Capabilities.PipelineCache.NativeCache.Require("Metal pipeline cache");
            throw new InvalidOperationException("Metal pipeline-cache capability is available without a factory implementation.");
        }

        public override RHIComputeIndirectCommandBuffer CreateComputeIndirectCommandBuffer(in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            return new MetalComputeIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRayTracingIndirectCommandBuffer CreateRayTracingIndirectCommandBuffer(in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            return new MetalRayTracingIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRasterIndirectCommandBuffer CreateRasterIndirectCommandBuffer(in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            return new MetalRasterIndirectCommandBuffer(this, descriptor);
        }

        public override bool TryToggleGpuCapture(string savedPath, string reason)
        {
            MetalCommandQueue? graphicsQueue = GetCommandQueue(ERHIPipelineType.Graphics, 0) as MetalCommandQueue;
            if (graphicsQueue == null || graphicsQueue.NativeQueue4.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine("[MetalDevice] Capture skipped: graphics queue unavailable.");
                return false;
            }

            MTLCaptureManager manager = MTLCaptureManager.SharedCaptureManager();
            if (manager.NativePtr == IntPtr.Zero)
            {
                Console.WriteLine("[MetalDevice] Capture skipped: MTLCaptureManager unavailable.");
                return false;
            }

            if (manager.IsCapturing)
            {
                manager.StopCapture();
                Console.WriteLine("[MetalDevice] Metal capture stopped.");
                return true;
            }

            string captureDir = Path.Combine(savedPath, "Captures", "GPU");
            Directory.CreateDirectory(captureDir);
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string outputPath = Path.Combine(captureDir, $"metal-{timestamp}-{SanitizeFileName(reason)}.gputrace");

            MTLCaptureDescriptor descriptor = MTLCaptureDescriptor.New();
            NSError error = default;
            try
            {
                descriptor.CaptureObject = graphicsQueue.NativeQueue4.NativePtr;
                descriptor.Destination = MTLCaptureDestination.GPUTraceDocument;
                descriptor.OutputURL = NSURL.FileURLWithPath(new NSString(outputPath));
                bool started = manager.StartCapture(descriptor, ref error);
                if (!started)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    Console.WriteLine($"[MetalDevice] Metal capture start failed: {errorText}");
                    return false;
                }

                Console.WriteLine($"[MetalDevice] Metal capture started: {outputPath}");
                return true;
            }
            finally
            {
                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor.NativePtr);
                }
            }
        }

        private void BuildLimitAndFeature()
        {
            int maxTextureSize = 16384;
            int maxCubeTextureSize = 16384;

            m_Limit = new RHIDeviceLimit(
                uniformBufferAlignment: 256,
                uploadBufferAlignment: 4,
                uploadBufferTextureAlignment: 256,
                uploadBufferTextureRowAlignment: 256,
                maxMSAACount: 8,
                maxBoundTexture: 128,
                minWavefrontSize: 32,
                maxWavefrontSize: 64,
                maxComputeThreads: (int)Math.Max(64UL, m_NativeDevice.MaxThreadsPerThreadgroup.width * m_NativeDevice.MaxThreadsPerThreadgroup.height * m_NativeDevice.MaxThreadsPerThreadgroup.depth),
                maxGroupShareMemorySize: (int)m_NativeDevice.MaxThreadgroupMemoryLength,
                maxVertexInputBindings: 31,
                maxColorAttachments: 8,
                maxTexture2DSize: maxTextureSize,
                maxTextureCubeSize: maxCubeTextureSize);

            // MTL4 ray tracing requires hardware support. Apple M1/M2 expose software-emulated RT
            // capability flags but assert when building acceleration structures through MTL4.
            bool rawRayTracingCapability = m_NativeDevice.SupportsRaytracing && m_NativeDevice.SupportsRaytracingFromRender;
            string deviceName = m_Name ?? string.Empty;
            bool isAppleSilicon = deviceName.StartsWith("Apple M", StringComparison.OrdinalIgnoreCase);
            bool hasKnownHardwareRayTracing = !isAppleSilicon || IsAppleM3OrNewer(deviceName);
            bool isRayTracingSupported = rawRayTracingCapability && hasKnownHardwareRayTracing;
            if (rawRayTracingCapability && !isRayTracingSupported)
            {
                Console.WriteLine($"[MetalDevice] Ray tracing capability appears software-emulated on '{deviceName}'; disabling RT for MTL4 runtime stability.");
            }
            bool isMetal3 = m_SupportsMetal3;
            bool isTimestampSupported = TryProbeTimestampCounterHeap(out string? timestampUnavailableReason);
            m_TimestampQueriesUnavailableReason = timestampUnavailableReason;
            bool isPipelineStatsSupported = TryGetStatisticsCounterSet(m_NativeDevice, out _);
            bool isMLSupported = TryProbeMetalMLSupport(out string? metalMLUnavailableReason);
            m_MetalMLUnavailableReason = metalMLUnavailableReason;

            static RHICapability Probe(
                bool available,
                string source,
                string unavailableReason,
                ERHICapabilityTier tier = ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy strategy = ERHICapabilityStrategy.NativeSpecialized,
                ERHICapabilityProbeKind probeKind = ERHICapabilityProbeKind.NativeFeatureQuery,
                RHICapabilityLimits limits = default)
            {
                return RHICapability.FromProbe(
                    available,
                    tier,
                    strategy,
                    probeKind,
                    source,
                    unavailableReason,
                    limits);
            }

            RHICapabilityLimits rasterLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumSampleCount, 8),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumVertexInputBindings, 31),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumColorAttachments, 8),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTexture2DSize, (ulong)maxTextureSize),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTextureCubeSize, (ulong)maxCubeTextureSize));
            RHICapabilityLimits bindingLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.UniformBufferAlignment, 256),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumBoundTextures, 128));
            RHICapabilityLimits computeLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MinimumWavefrontSize, 32),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumWavefrontSize, 64),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumComputeThreads,
                    m_NativeDevice.MaxThreadsPerThreadgroup.width
                        * m_NativeDevice.MaxThreadsPerThreadgroup.height
                        * m_NativeDevice.MaxThreadsPerThreadgroup.depth),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumGroupSharedMemoryBytes,
                    m_NativeDevice.MaxThreadgroupMemoryLength));

            m_Capabilities = new RHIDeviceCapabilities(
                raster: new RHIRasterCapabilities(
                    ERHIProjectionStrategy.FlipY,
                    ERHIMatrixMajorOrder.RowMajor,
                    ERHIDepthValueRange.ZeroToOne,
                    ERHIMultiviewStrategy.Unsupported,
                    pixelShaderStorageWrites: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal 4 fragment-stage writable resources",
                        rasterLimits),
                    rasterOrderedAccess: Probe(
                        m_RasterCapabilities.RasterOrderGroups,
                        "MTLDevice.rasterOrderGroupsSupported",
                        "Raster order groups are unavailable."),
                    anisotropicSampling: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal sampler contract"),
                    depthAttachmentRead: Probe(
                        false,
                        "SharpGPU Metal attachment-read lowering",
                        "Depth attachment reads are not exposed by the current Metal lowering."),
                    framebufferLocalRead: Probe(
                        m_RasterCapabilities.FramebufferLocalRead,
                        "MTL4RenderPassDescriptor.supportColorAttachmentMapping + " +
                        "MTLLogicalToPhysicalColorAttachmentMap.setPhysicalIndex",
                        "The complete Metal framebuffer-local-read selector set is unavailable.",
                        limits: rasterLimits),
                    sampledFeedback: Probe(
                        false,
                        "SharpGPU Metal sampled-feedback lowering",
                        "Metal sampled feedback is unavailable until the backend provides exact texture-usage, hazard, and pipeline lowering.",
                        probeKind: ERHICapabilityProbeKind.BackendContract),
                    drawIndirect: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal indirect draw commands"),
                    multiDrawIndirect: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal indirect command buffers"),
                    variableRateShading: Probe(
                        false,
                        "SharpGPU Metal variable-rate rasterization lowering",
                        "Variable-rate shading is not exposed by the Metal backend."),
                    hiddenSurfaceRemoval: Probe(
                        false,
                        "SharpGPU Metal raster lowering",
                        "Hidden-surface removal is renderer policy and is not a Metal HAL capability."),
                    barycentricCoordinates: Probe(
                        m_NativeDevice.SupportsShaderBarycentricCoordinates,
                        "MTLDevice.supportsShaderBarycentricCoordinates",
                        "Shader barycentric coordinates are unavailable."),
                    programmableSamplePositions: Probe(
                        m_NativeDevice.ProgrammableSamplePositionsSupported,
                        "MTLDevice.programmableSamplePositionsSupported",
                        "Programmable sample positions are unavailable."),
                    nativeRenderPass: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "MTLRenderPassDescriptor",
                        rasterLimits)),
                binding: new RHIBindingCapabilities(
                    rootConstants: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal inline byte binding",
                        bindingLimits),
                    indirectRootConstants: Probe(
                        false,
                        "SharpGPU Metal indirect-command lowering",
                        "Indirect inline constants are not exposed."),
                    atomicUInt64: Probe(
                        isMetal3,
                        "Metal 3 64-bit atomic contract",
                        "64-bit shader atomics require Metal 3."),
                    descriptorIndexing: Probe(
                        m_SupportsArgumentTable,
                        "MTL4ArgumentTable runtime object probe",
                        "Metal argument tables are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe,
                        limits: bindingLimits),
                    partiallyBoundDescriptors: Probe(
                        m_SupportsArgumentTable,
                        "MTL4ArgumentTable nil-entry contract",
                        "Metal argument-table nil entries are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    updateAfterBindDescriptors: Probe(
                        false,
                        "SharpGPU external argument-table synchronization contract",
                        "Argument-table mutation while GPU work is pending is intentionally not exposed."),
                    nullDescriptors: Probe(
                        m_SupportsArgumentTable,
                        "MTL4ArgumentTable nil resource/sampler entries",
                        "Metal argument-table nil entries are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe)),
                synchronization: new RHISynchronizationCapabilities(
                    timestampQueries: Probe(
                        isTimestampSupported,
                        "MTL4CounterHeap creation",
                        timestampUnavailableReason ?? "Metal timestamp counter heaps are unavailable.",
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    occlusionQueries: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal visibility result queries"),
                    pipelineStatisticsQueries: Probe(
                        isPipelineStatsSupported,
                        "MTLDevice counterSets statistics probe",
                        "Metal pipeline statistics counter sets are unavailable.",
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    enhancedBarriers: Probe(
                        m_SupportsMetal4Barriers,
                        "Metal 4 barrier contract",
                        "Metal 4 barriers are unavailable.",
                        probeKind: ERHICapabilityProbeKind.ApiVersion)),
                memory: new RHIMemoryCapabilities(
                    unifiedMemory: Probe(
                        m_NativeDevice.HasUnifiedMemory,
                        "MTLDevice.hasUnifiedMemory",
                        "The Metal device does not use unified memory."),
                    placedResources: Probe(
                        m_SupportsMetal4,
                        "MTLDevice heapSizeAndAlign + MTLHeap placement resources",
                        "Metal placement heaps are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    sparseBinding: Probe(
                        m_SupportsPlacementSparse,
                        "MTLDevice.supportsPlacementSparse + MTL4CommandQueue.updateTextureMappings",
                        "The Metal runtime does not expose placement sparse resources.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    residency: RHICapability.Unavailable(
                        "MTLResidencySet does not provide the explicit completion-fence contract required by SharpGPU.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Metal explicit residency completion contract"),
                    budgetQuery: Probe(
                        m_NativeDevice.RecommendedMaxWorkingSetSize != 0,
                        "MTLDevice.recommendedMaxWorkingSetSize + currentAllocatedSize",
                        "Metal did not report a device working-set budget.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe)),
                storage: new RHIStorageCapabilities(
                    nativeGpuFileIo: RHICapability.Unavailable(
                        "Metal has no SharpGPU-supported official native GPU file-I/O queue.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Metal storage contract")),
                pipelineCache: new RHIPipelineCacheCapabilities(
                    nativeCache: RHICapability.Unavailable(
                        "MTLBinaryArchive is URL-based and cannot satisfy the caller-owned in-memory blob contract.",
                        ERHICapabilityProbeKind.BackendContract,
                        "MTLBinaryArchive import/export contract")),
                presentation: new RHIPresentationCapabilities(
                    swapChain: Probe(
                        m_SupportsMetal4,
                        "CAMetalLayer runtime on a Metal 4 device",
                        "Metal swapchain creation requires CAMetalLayer and Metal 4.",
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    acquireSignal: RHICapability.Unavailable(
                        "CAMetalLayer nextDrawable does not signal caller-owned RHI synchronization.",
                        ERHICapabilityProbeKind.BackendContract,
                        "CAMetalLayer acquisition contract"),
                    presentWait: RHICapability.Unavailable(
                        "CAMetalDrawable presentation does not consume caller-owned RHI semaphores.",
                        ERHICapabilityProbeKind.BackendContract,
                        "CAMetalDrawable presentation contract"),
                    presentCompletion: Probe(
                        m_SupportsMetal4,
                        "CAMetalDrawable addPresentedHandler runtime contract",
                        "Metal presentation completion callbacks require the Metal 4 runtime.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    maintenance: Probe(
                        m_SupportsMetal4,
                        "caller-drained CAMetalLayer replacement",
                        "Metal swapchain maintenance requires the Metal 4 runtime.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    maintenanceStrategy:
                        ERHIPresentationMaintenanceStrategy.PresentFence,
                    hdr: RHICapability.Unavailable(
                        "SharpGPU does not yet expose the CAMetalLayer colorspace and extended-dynamic-range contract required for HDR presentation.",
                        ERHICapabilityProbeKind.BackendContract,
                        "CAMetalLayer HDR presentation contract")),
                rayTracing: new RHIRayTracingCapabilities(
                    pipeline: Probe(
                        isRayTracingSupported,
                        "MTLDevice ray-tracing properties plus Apple hardware-family qualification",
                        "Hardware Metal ray tracing is unavailable."),
                    inline: Probe(
                        isRayTracingSupported,
                        "MTLDevice ray-tracing properties plus Apple hardware-family qualification",
                        "Inline Metal ray tracing is unavailable.")),
                mesh: new RHIMeshCapabilities(
                    shader: RHICapability.Unavailable(
                        "Metal mesh shaders are not exposed by the current SharpGPU factory surface.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal factory surface")),
                machineLearning: new RHIMachineLearningCapabilities(
                    execution: RHICapability.FromProbe(
                        isMLSupported,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.NativeSpecialized,
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        "Metal 4 ML compiler, tensor, and argument-table selectors",
                        metalMLUnavailableReason ?? "Metal 4 ML runtime objects are unavailable.")),
                workGraph: new RHIWorkGraphCapabilities(
                    execution: RHICapability.Unavailable(
                        "Metal Work Graph execution is not exposed by SharpGPU.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal factory surface")),
                compute: new RHIComputeCapabilities(
                    ERHIWaveOperationStrategy.Basic,
                    waveOperations: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Metal SIMD-group operations",
                        computeLimits)));
        }

        private bool TryProbeTimestampCounterHeap(out string? unavailableReason)
        {
            unavailableReason = null;

            if (!m_SupportsMetal4)
            {
                unavailableReason = "Metal timestamp queries require Metal 4.";
                return false;
            }

            if (!SafeSupportsSelector(s_NewCounterHeapWithDescriptorError))
            {
                unavailableReason = "Metal timestamp queries require newCounterHeapWithDescriptor:error:.";
                return false;
            }

            MTL4CounterHeapDescriptor descriptor = MTL4CounterHeapDescriptor.New();
            MTL4CounterHeap heap = default;
            NSError error = default;
            try
            {
                descriptor.Type = MTL4CounterHeapType.Timestamp;
                descriptor.Count = 1;
                heap = m_NativeDevice.NewCounterHeap(descriptor, ref error);
                if (heap.NativePtr == IntPtr.Zero)
                {
                    unavailableReason = error.NativePtr != IntPtr.Zero
                        ? $"Metal timestamp queries require MTL4CounterHeap support: {error.LocalizedDescription}."
                        : "Metal timestamp queries require MTL4CounterHeap support.";
                    return false;
                }

                ulong entrySize = m_NativeDevice.SizeOfCounterHeapEntry(MTL4CounterHeapType.Timestamp);
                ulong frequency = m_NativeDevice.QueryTimestampFrequency();
                if (entrySize == 0 || frequency == 0)
                {
                    unavailableReason = $"Metal timestamp queries require nonzero counter entry size and frequency. entrySize={entrySize}, frequency={frequency}.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                unavailableReason = $"Metal timestamp query probe failed: {ex.Message}";
                return false;
            }
            finally
            {
                if (heap.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(heap);
                }

                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor);
                }
            }
        }

        private bool TryProbeMetalMLSupport(out string? unavailableReason)
        {
            unavailableReason = null;

            if (!m_SupportsMetal4)
            {
                unavailableReason = "Metal ML requires Metal 4.";
                return false;
            }

            if (!m_SupportsArgumentTable || !SafeSupportsSelector(s_NewArgumentTableWithDescriptorError))
            {
                unavailableReason = "Metal ML requires native MTL4 argument table support.";
                return false;
            }

            if (!SafeSupportsSelector(s_NewCompilerWithDescriptorError))
            {
                unavailableReason = "Metal ML requires native MTL4 compiler support.";
                return false;
            }

            if (!SafeSupportsSelector(s_NewTensorWithDescriptorError))
            {
                unavailableReason = "Metal ML requires native MTLTensor creation support.";
                return false;
            }

            if (!MetalMpsGraphPackageBuilder.IsAvailable(out string? packageUnavailableReason))
            {
                unavailableReason = packageUnavailableReason ?? "Metal ML requires MPSGraph package serialization support.";
                return false;
            }

            unavailableReason =
                "Metal ML package artifacts are available, but the current MPSGraph-package route is disabled: " +
                "matrix/intermediates-heap packages can dispatch once and then corrupt subsequent MTL4MachineLearningCommandEncoder dispatches in the same process on macOS 26.5. " +
                "No compute-kernel or CPU fallback is allowed; keep Metal ML unsupported until a stable native artifact route is proven.";
            return false;
        }

        internal void RegisterMetalMLPipelineState(in MTL4MachineLearningPipelineState pipelineState)
        {
            if (pipelineState.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLPipelineStates.Add(pipelineState);
        }

        internal void RegisterMetalMLArgumentTable(in MTL4ArgumentTable argumentTable)
        {
            if (argumentTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLArgumentTables.Add(argumentTable);
        }

        internal void RegisterMetalMLIntermediatesHeap(in MTLHeap heap)
        {
            if (heap.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLIntermediatesHeaps.Add(heap);
        }

        private void RegisterMetalMLPackageDirectory(string packageDirectory)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory))
            {
                return;
            }

            m_MetalMLPackageDirectories.Add(packageDirectory);
        }

        private void RegisterMetalMLLibrary(in MTLLibrary library)
        {
            if (library.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLLibraries.Add(library);
        }

        private void ReleaseMetalMLNativeObjects()
        {
            for (int i = 0; i < m_MetalMLIntermediatesHeaps.Count; ++i)
            {
                MTLHeap heap = m_MetalMLIntermediatesHeaps[i];
                if (heap.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(heap);
                }
            }

            m_MetalMLIntermediatesHeaps.Clear();

            for (int i = 0; i < m_MetalMLArgumentTables.Count; ++i)
            {
                MTL4ArgumentTable argumentTable = m_MetalMLArgumentTables[i];
                if (argumentTable.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(argumentTable);
                }
            }

            m_MetalMLArgumentTables.Clear();

            for (int i = 0; i < m_MetalMLPipelineStates.Count; ++i)
            {
                MTL4MachineLearningPipelineState pipelineState = m_MetalMLPipelineStates[i];
                if (pipelineState.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pipelineState);
                }
            }

            m_MetalMLPipelineStates.Clear();

            for (int i = 0; i < m_MetalMLLibraries.Count; ++i)
            {
                MTLLibrary library = m_MetalMLLibraries[i];
                if (library.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(library);
                }
            }

            m_MetalMLLibraries.Clear();
        }

        private void DeleteMetalMLPackageDirectories()
        {
            for (int i = 0; i < m_MetalMLPackageDirectories.Count; ++i)
            {
                string packageDirectory = m_MetalMLPackageDirectories[i];
                try
                {
                    if (Directory.Exists(packageDirectory))
                    {
                        Directory.Delete(packageDirectory, recursive: true);
                    }
                }
                catch
                {
                }
            }

            m_MetalMLPackageDirectories.Clear();
        }

        internal static bool TryGetStatisticsCounterSet(MTLDevice device, out MTLCounterSet counterSet)
        {
            NSArray counterSets = device.CounterSets;
            for (uint i = 0; i < counterSets.Count; ++i)
            {
                MTLCounterSet candidate = new MTLCounterSet(counterSets[i]);
                string name = candidate.Name.ToString() ?? string.Empty;
                if (name.Contains("stat", StringComparison.OrdinalIgnoreCase))
                {
                    counterSet = candidate;
                    return true;
                }

                NSArray counters = candidate.Counters;
                for (uint counterIndex = 0; counterIndex < counters.Count; ++counterIndex)
                {
                    MTLCounter counter = new MTLCounter(counters[counterIndex]);
                    string counterName = counter.Name.ToString() ?? string.Empty;
                    if (counterName.Contains("vertex", StringComparison.OrdinalIgnoreCase)
                        || counterName.Contains("fragment", StringComparison.OrdinalIgnoreCase)
                        || counterName.Contains("primitive", StringComparison.OrdinalIgnoreCase))
                    {
                        counterSet = candidate;
                        return true;
                    }
                }
            }

            counterSet = default;
            return false;
        }

        private static bool IsAppleM3OrNewer(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                return false;
            }

            int markerIndex = deviceName.IndexOf("Apple M", StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            int generationStart = markerIndex + "Apple M".Length;
            int generationEnd = generationStart;
            while (generationEnd < deviceName.Length && char.IsDigit(deviceName[generationEnd]))
            {
                ++generationEnd;
            }

            if (generationEnd == generationStart)
            {
                return false;
            }

            if (!int.TryParse(deviceName.Substring(generationStart, generationEnd - generationStart), out int generation))
            {
                return false;
            }

            return generation >= 3;
        }

        private void CreateCommandQueues(in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_ComputeQueueCount = computeQueueCount;
            m_TransferQueueCount = transferQueueCount;
            m_GraphicsQueueCount = graphicsQueueCount;
            m_CommandQueueMap = new Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>(3);

            if (computeQueueCount > 0)
            {
                TArray<RHICommandQueue> computeQueues = new TArray<RHICommandQueue>(computeQueueCount);
                for (int i = 0; i < computeQueueCount; ++i)
                {
                    computeQueues.Add(new MetalCommandQueue(this, ERHIPipelineType.Compute));
                }

                m_CommandQueueMap.Add(ERHIPipelineType.Compute, computeQueues);
            }

            if (transferQueueCount > 0)
            {
                TArray<RHICommandQueue> transferQueues = new TArray<RHICommandQueue>(transferQueueCount);
                for (int i = 0; i < transferQueueCount; ++i)
                {
                    transferQueues.Add(new MetalCommandQueue(this, ERHIPipelineType.Transfer));
                }

                m_CommandQueueMap.Add(ERHIPipelineType.Transfer, transferQueues);
            }

            if (graphicsQueueCount > 0)
            {
                TArray<RHICommandQueue> graphicsQueues = new TArray<RHICommandQueue>(graphicsQueueCount);
                for (int i = 0; i < graphicsQueueCount; ++i)
                {
                    graphicsQueues.Add(new MetalCommandQueue(this, ERHIPipelineType.Graphics));
                }

                m_CommandQueueMap.Add(ERHIPipelineType.Graphics, graphicsQueues);
            }
        }

        private MetalRasterCapabilities ProbeRasterCapabilities()
        {
            bool passMappingSelector = false;
            bool mapEntrySelector = false;
            bool encoderMappingSelector = false;
            MTL4RenderPassDescriptor passDescriptor = default;
            MTLLogicalToPhysicalColorAttachmentMap map = default;
            MTL4CommandBuffer commandBuffer = default;
            MTL4CommandAllocator commandAllocator = default;
            MTL4RenderCommandEncoder renderEncoder = default;
            try
            {
                passDescriptor = MTL4RenderPassDescriptor.New();
                passMappingSelector =
                    passDescriptor.NativePtr != IntPtr.Zero &&
                    passDescriptor.SupportsColorAttachmentMapping;

                map = MTLLogicalToPhysicalColorAttachmentMap.New();
                mapEntrySelector =
                    map.NativePtr != IntPtr.Zero &&
                    map.SupportsPhysicalIndexMapping;

                if (passMappingSelector && mapEntrySelector)
                {
                    commandBuffer =
                        m_NativeDevice.NewMTL4CommandBuffer();
                    commandAllocator =
                        m_NativeDevice.NewMTL4CommandAllocator();
                    if (commandBuffer.NativePtr != IntPtr.Zero &&
                        commandAllocator.NativePtr != IntPtr.Zero)
                    {
                        commandBuffer.BeginCommandBuffer(
                            commandAllocator);
                        passDescriptor.SupportColorAttachmentMapping =
                            true;
                        passDescriptor.RenderTargetWidth = 1;
                        passDescriptor.RenderTargetHeight = 1;
                        passDescriptor.RenderTargetArrayLength = 1;
                        passDescriptor.DefaultRasterSampleCount = 1;
                        renderEncoder =
                            commandBuffer.RenderCommandEncoder(
                                passDescriptor);
                        encoderMappingSelector =
                            renderEncoder.NativePtr != IntPtr.Zero &&
                            renderEncoder
                                .SupportsColorAttachmentMapping;
                        if (renderEncoder.NativePtr != IntPtr.Zero)
                        {
                            new MTL4CommandEncoder(
                                renderEncoder.NativePtr)
                                .EndEncoding();
                        }
                        commandBuffer.EndCommandBuffer();
                    }
                }
            }
            catch
            {
                passMappingSelector = false;
                mapEntrySelector = false;
                encoderMappingSelector = false;
            }
            finally
            {
                if (commandBuffer.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(
                        commandBuffer.NativePtr);
                }
                if (commandAllocator.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(
                        commandAllocator.NativePtr);
                }
                if (map.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(map.NativePtr);
                }
                if (passDescriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(passDescriptor.NativePtr);
                }
            }

            return MetalRasterCapabilities.FromSelectorProbes(
                passMappingSelector,
                mapEntrySelector,
                encoderMappingSelector,
                m_NativeDevice.RasterOrderGroupsSupported);
        }

        private bool SafeSupportsFamily(in MTLGPUFamily family)
        {
            try
            {
                return m_NativeDevice.SupportsFamily(family);
            }
            catch
            {
                return false;
            }
        }

        private MTLArgumentBuffersTier SafeArgumentBuffersTier()
        {
            try
            {
                return m_NativeDevice.ArgumentBuffersSupport;
            }
            catch
            {
                return unchecked((MTLArgumentBuffersTier)ulong.MaxValue);
            }
        }

        private bool SafeSupportsPlacementSparse()
        {
            if (!SafeSupportsSelector(s_SupportsPlacementSparse))
            {
                return false;
            }

            try
            {
                return m_NativeDevice.SupportsPlacementSparse;
            }
            catch
            {
                return false;
            }
        }

        private bool SafeSupportsSelector(in Selector selector)
        {
            try
            {
                return ObjectiveCRuntime.bool_objc_msgSend(m_NativeDevice.NativePtr, s_RespondsToSelector, selector);
            }
            catch
            {
                return false;
            }
        }

        internal MetalTextureViewIndexLease AllocateTextureViewIndex()
        {
            return m_TextureViewIndices.Allocate();
        }

        internal void ReleaseTextureViewIndex(in MetalTextureViewIndexLease lease)
        {
            _ = m_TextureViewIndices.Release(lease);
        }

        internal void RemoveResidencyAllocation(in MTLAllocation allocation)
        {
            if (allocation.NativePtr == IntPtr.Zero || m_CommandQueueMap == null)
            {
                return;
            }

            foreach (KeyValuePair<ERHIPipelineType, TArray<RHICommandQueue>> pair in m_CommandQueueMap)
            {
                TArray<RHICommandQueue> queues = pair.Value;
                for (int i = 0; i < queues.length; ++i)
                {
                    if (queues[i] is MetalCommandQueue queue)
                    {
                        queue.RemoveResidencyAllocation(allocation);
                    }
                }
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "manual";
            }

            string sanitized = value;
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; ++i)
            {
                sanitized = sanitized.Replace(invalidChars[i], '-');
            }

            return string.IsNullOrWhiteSpace(sanitized) ? "manual" : sanitized;
        }

        private void CreateTextureViewPool()
        {
            MTLResourceViewPoolDescriptor poolDescriptor = MTLResourceViewPoolDescriptor.New();
            NSError error = default;
            try
            {
                poolDescriptor.ResourceViewCount = m_TextureViewIndices.Capacity;
                m_TextureViewPool = m_NativeDevice.NewTextureViewPool(poolDescriptor, ref error);
            }
            finally
            {
                ObjectiveCRuntime.Release(poolDescriptor.NativePtr);
            }

            if (m_TextureViewPool.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTLTextureViewPool: {errorText}");
            }
        }

        protected override void Release()
        {
            if (m_CommandQueueMap != null)
            {
                foreach (KeyValuePair<ERHIPipelineType, TArray<RHICommandQueue>> pair in m_CommandQueueMap)
                {
                    TArray<RHICommandQueue> queues = pair.Value;
                    for (int i = 0; i < queues.length; ++i)
                    {
                        queues[i]?.Dispose();
                    }
                }
            }

            if (m_TextureViewPool.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_TextureViewPool.NativePtr);
                m_TextureViewPool = default;
            }

            ReleaseMetalMLNativeObjects();
            DeleteMetalMLPackageDirectories();

            if (m_NativeDevice.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDevice);
            }
        }
    }
}
