using System;
using System.IO;
using SharpMetal.Metal;
using System.Threading;
using SharpGPU.Collections;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;

namespace SharpGPU
{
    internal sealed class MetalDevice : RHIDevice
    {
        public MTLDevice NativeDevice => m_NativeDevice;
        public MetalInstance MetalInstance => m_MetalInstance;
        internal bool SupportsMetal4Barriers => m_SupportsMetal4Barriers;
        internal bool SupportsMetal4 => m_SupportsMetal4;
        internal bool SupportsArgumentTable => m_SupportsArgumentTable;
        internal bool SupportsMetalML => m_Feature?.IsMLSupported == true;
        internal string? TimestampQueriesUnavailableReason => m_TimestampQueriesUnavailableReason;
        internal string? MetalMLUnavailableReason => m_MetalMLUnavailableReason;
        internal MTLTextureViewPool TextureViewPool => m_TextureViewPool;

        private readonly MTLDevice m_NativeDevice;
        private readonly MetalInstance m_MetalInstance;
        private readonly bool m_SupportsMetal4Barriers;
        private readonly bool m_SupportsMetal3;
        private readonly bool m_SupportsMetal4;
        private readonly bool m_SupportsArgumentTable;
        private string? m_TimestampQueriesUnavailableReason;
        private string? m_MetalMLUnavailableReason;
        private MTLTextureViewPool m_TextureViewPool;
        private int m_NextTextureViewIndex;

        private static readonly Selector s_RespondsToSelector = "respondsToSelector:";
        private static readonly Selector s_NewArgumentTableWithDescriptorError = "newArgumentTableWithDescriptor:error:";
        private static readonly Selector s_NewCompilerWithDescriptorError = "newCompilerWithDescriptor:error:";
        private static readonly Selector s_NewCounterHeapWithDescriptorError = "newCounterHeapWithDescriptor:error:";
        private static readonly Selector s_NewTensorWithDescriptorError = "newTensorWithDescriptor:error:";

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
            m_SupportsMetal3 = SafeSupportsFamily(MTLGPUFamily.Metal3);
            m_SupportsMetal4 = SafeSupportsFamily(MTLGPUFamily.Metal4);
            m_SupportsArgumentTable = m_SupportsMetal4 && SafeSupportsSelector(s_NewArgumentTableWithDescriptorError);
            m_SupportsMetal4Barriers = m_SupportsMetal4;

            if (!m_SupportsMetal4)
            {
                throw new NotSupportedException("Metal backend requires Metal 4 support.");
            }

            if (!m_SupportsArgumentTable)
            {
                throw new NotSupportedException("Metal backend requires MTL4 argument table support.");
            }

            BuildLimitAndFeature();
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
            CreateTextureViewPool();
        }

        public override RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index)
        {
            if (m_CommandQueueMap == null)
            {
                return null;
            }

            if (m_CommandQueueMap.TryGetValue(pipeline, out TArray<RHICommandQueue> queues))
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
            return new MetalSwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            return new MetalFence();
        }

        public override RHISemaphore CreateSemaphore()
        {
            return new MetalSemaphore(this);
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            return new MetalStorageQueue();
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            if ((descriptor.Type == ERHIQueryType.TimestampTransfer || descriptor.Type == ERHIQueryType.TimestampGenerice)
                && m_Feature?.IsTimestampQueriesSupported != true)
            {
                throw new NotSupportedException(m_TimestampQueriesUnavailableReason ?? "Metal timestamp queries require native MTL4CounterHeap support.");
            }

            if (descriptor.Type == ERHIQueryType.Statistics && m_Feature?.IsPipelineStatsQueriesSupported != true)
            {
                throw new NotSupportedException("Metal pipeline statistics queries require a device statistics counter set.");
            }

            return new MetalQuery(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            return new MetalHeap(this, descriptor);
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            return new MetalBuffer(this, descriptor);
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            return new MetalTexture(this, descriptor);
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
            return new MetalArgumentTableLayout(descriptor);
        }

        public override RHIArgumentTable CreateArgumentTable(in RHIArgumentTableDescriptor descriptor)
        {
            return new MetalArgumentTable(descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new MetalPipelineLayout(descriptor);
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
            // Metal 4 ML is function/shader-based, not operator-description-based: a MetalMLProgram
            // wraps a compiled MetalFunction, not an op sequence. The backend-neutral op-sequence
            // program builder (ADR-0028) is therefore implemented on DX12/DirectML in MVP3; a Metal
            // lowering that emits a Metal ML kernel per op (or a fused kernel) is the MVP4 Metal
            // backend's scope. Failing loudly keeps the contract honest instead of silently no-op'ing.
            throw new NotSupportedException(
                "Metal ML program construction from an op sequence is not implemented yet. " +
                "Metal 4 ML is function-based; the op-sequence -> Metal ML kernel lowering is scoped to the SharpNeural MVP4 Metal backend (RFC-0010). " +
                "TODO(UNVERIFIED): macOS ARM64 Metal 4 ML lowering.");
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override RHIPipelineLibrary CreatePipelineLibrary(in RHIPipelineLibraryDescriptor descriptor)
        {
            return new MetalPipelineLibrary(this, descriptor);
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

            m_Feature = new RHIDeviceFeature(
                isFlipProjection: true,
                isHDRPresentSupported: true,
                isUnifiedMemorySupported: m_NativeDevice.HasUnifiedMemory,
                isRootConstantSupport: true,
                isIndirectRootConstantSupport: false,
                isPixelShaderUAVSupported: true,
                isRasterizerOrderedSupported: m_NativeDevice.RasterOrderGroupsSupported,
                isAnisotropyTextureSupported: true,
                isDepthbufferFetchSupported: false,
                isFramebufferFetchSupported: false,
                isTimestampQueriesSupported: isTimestampSupported,
                isOcclusionQueriesSupported: true,
                isPipelineStatsQueriesSupported: isPipelineStatsSupported,
                isAtomicUInt64Supported: isMetal3,
                isWorkgraphSupported: false,
                isMeshShadingSupported: false,
                isDrawIndirectSupported: true,
                isDrawMultiIndirectSupported: true,
                isRaytracingSupported: isRayTracingSupported,
                isRaytracingInlineSupported: isRayTracingSupported,
                isVariableRateShadingSupported: false,
                isHiddenSurfaceRemovalSupported: false,
                isBarycentricCoordSupported: m_NativeDevice.SupportsShaderBarycentricCoordinates,
                isProgrammableSamplePositionSupported: m_NativeDevice.ProgrammableSamplePositionsSupported,
                isMLSupported: isMLSupported,
                matrixMajorons: ERHIMatrixMajorons.RowMajor,
                depthValueRange: ERHIDepthValueRange.ZeroToOne,
                multiviewStrategy: ERHIMultiviewStrategy.Unsupported,
                waveOperationStrategy: ERHIWaveOperationStrategy.Basic);
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

            unavailableReason = "Metal ML is disabled until SharpGPU provides a native Metal ML package program path; CPU and no-op fallbacks are not allowed.";
            return false;
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

        internal uint AllocateTextureViewIndex()
        {
            return (uint)Interlocked.Increment(ref m_NextTextureViewIndex) - 1;
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
            const ulong initialCapacity = 4096;

            MTLResourceViewPoolDescriptor poolDescriptor = MTLResourceViewPoolDescriptor.New();
            poolDescriptor.ResourceViewCount = initialCapacity;

            NSError error = default;
            m_TextureViewPool = m_NativeDevice.NewTextureViewPool(poolDescriptor, ref error);
            ObjectiveCRuntime.Release(poolDescriptor.NativePtr);

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

            if (m_NativeDevice.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDevice);
            }
        }
    }
}
