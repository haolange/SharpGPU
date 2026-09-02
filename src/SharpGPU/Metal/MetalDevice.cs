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
        internal bool SupportsNativeArgumentTable => m_SupportsNativeArgumentTable;
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
        private readonly bool m_SupportsNativeArgumentTable;
        private readonly bool m_SupportsPlacementSparse;
        private readonly MetalRasterCapabilities m_RasterCapabilities;
        private string? m_TimestampQueriesUnavailableReason;
        private string? m_MetalMLUnavailableReason;
        private MTLTextureViewPool m_TextureViewPool;
        private readonly MetalTextureViewIndexAllocator m_TextureViewIndices = new();
        // Metal 4 ML runtime objects (pipeline / binding table / intermediates heap) are retained
        // for the device lifetime so a future Metal4-native artifact route can reuse the encoder path.
        private readonly List<MTL4MachineLearningPipelineState> m_MetalMLPipelineStates = new List<MTL4MachineLearningPipelineState>();
        private readonly List<MTL4ArgumentTable> m_MetalMLNativeArgumentTables = new List<MTL4ArgumentTable>();
        private readonly List<MTLHeap> m_MetalMLIntermediatesHeaps = new List<MTLHeap>();
        // Managed owners keep MetalHeap finalizers from releasing native heaps mid-session.
        private readonly List<MetalHeap> m_MetalMLIntermediatesHeapOwners = new List<MetalHeap>();
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
            m_AdapterIdentity = new RHIAdapterIdentity(
                unchecked((long)m_NativeDevice.RegistryID),
                Guid.Empty);
            m_SupportsMetal3 = SafeSupportsFamily(MTLGPUFamily.Metal3);
            m_SupportsMetal4 = SafeSupportsFamily(MTLGPUFamily.Metal4);
            m_SupportsNativeArgumentTable = m_SupportsMetal4 && SafeSupportsSelector(s_NewArgumentTableWithDescriptorError);
            m_SupportsMetal4Barriers = m_SupportsMetal4;
            m_SupportsPlacementSparse =
                m_SupportsMetal4 &&
                SafeSupportsPlacementSparse();

            if (!m_SupportsMetal4)
            {
                throw new NotSupportedException("Metal backend requires Metal 4 support.");
            }

            if (!m_SupportsNativeArgumentTable)
            {
                throw new NotSupportedException("Metal backend requires MTL4 binding table support.");
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
            ThrowIfCommandQueueFailed();
            Capabilities.Storage.NativeGpuFileIo.Require("Metal storage queue creation");
            return new MetalStorageQueue(this);
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
                Capabilities.Memory.SparseTexture2D.Require(
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
            Capabilities.Memory.RequireSparseTexture(
                descriptor,
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
            Capabilities.Memory.RequireSparseTexture(
                descriptor,
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
            RejectUnpairedSamplerFeedbackTexture(in descriptor);
            return new MetalTexture(this, descriptor);
        }

        public override RHICapability QueryRasterAttachmentSupport(
            in RHIRasterAttachmentSupportQuery query)
        {
            ThrowIfDisposed();
            const string ProbeSource =
                "MTLDevice.supportsTextureSampleCount + " +
                "newTextureWithDescriptor runtime probe";

            if (query.IsInput && query.IsOutput &&
                Capabilities.Raster.FramebufferReadWrite.Tier ==
                    ERHICapabilityTier.Unavailable)
            {
                return Capabilities.Raster.FramebufferReadWrite;
            }
            if (query.IsInput && !query.IsOutput &&
                Capabilities.Raster.FramebufferLocalRead.Tier ==
                    ERHICapabilityTier.Unavailable)
            {
                return Capabilities.Raster.FramebufferLocalRead;
            }

            ulong sampleCount = checked((ulong)query.SampleCount);
            if (!m_NativeDevice.SupportsTextureSampleCount(sampleCount))
            {
                return RHICapability.Unavailable(
                    $"Metal does not support {sampleCount}x MSAA.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }
            if (query.Blend.BlendEnable &&
                !IsKnownMetalBlendableFormat(query.Format))
            {
                return RHICapability.Unavailable(
                    $"Metal format {query.Format} is not in SharpGPU's " +
                    "qualified hardware-blendable format set.",
                    ERHICapabilityProbeKind.BackendContract,
                    "SharpGPU Metal pixel-format capability table");
            }

            MTLPixelFormat pixelFormat;
            try
            {
                pixelFormat =
                    MetalUtility.ConvertToMetalPixelFormat(query.Format);
            }
            catch (ArgumentOutOfRangeException)
            {
                return RHICapability.Unavailable(
                    $"Metal has no pixel-format mapping for {query.Format}.",
                    ERHICapabilityProbeKind.BackendContract,
                    "SharpGPU Metal pixel-format lowering");
            }

            MTLTextureDescriptor descriptor =
                MTLTextureDescriptor.Texture2DDescriptor(
                    pixelFormat,
                    1,
                    1,
                    false);
            descriptor.TextureType = query.IsLayered
                ? sampleCount == 1
                    ? MTLTextureType.Type2DArray
                    : MTLTextureType.Type2DMultisampleArray
                : sampleCount == 1
                    ? MTLTextureType.Type2D
                    : MTLTextureType.Type2DMultisample;
            descriptor.ArrayLength = 1;
            descriptor.SampleCount = sampleCount;
            descriptor.Usage = MTLTextureUsage.RenderTarget;
            descriptor.StorageMode = MTLStorageMode.Private;
            MTLTexture texture = default;
            try
            {
                texture = m_NativeDevice.NewTexture(descriptor);
                if (texture.NativePtr == IntPtr.Zero)
                {
                    return RHICapability.Unavailable(
                        $"Metal rejected {query.Format} at {sampleCount}x " +
                        "for render-target attachment usage.",
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        ProbeSource);
                }
            }
            finally
            {
                if (texture.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(texture);
                }
                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor);
                }
            }

            return RHICapability.Available(
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeSpecialized,
                ERHICapabilityProbeKind.RuntimeObjectProbe,
                ProbeSource);
        }

        public override RHICapability QueryFormatSupport(
            in RHIFormatSupportQuery query)
        {
            ThrowIfDisposed();
            const string ProbeSource =
                "MTLDevice.supportsTextureSampleCount + newTextureWithDescriptor";

            if (query.Tiling == ERHITextureTiling.Linear)
            {
                return RHICapability.Unavailable(
                    "Metal does not expose a generic linear image tiling query for this dimension.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            if ((query.Usage & ERHITextureUsage.ResolveTarget) != 0 &&
                query.SampleCount != ERHISampleCount.None)
            {
                return RHICapability.Unavailable(
                    "Metal resolve destination queries require a single-sample count.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            ERHITextureUsage textureUsageBits =
                query.Usage & ~(ERHITextureUsage.CopySrc | ERHITextureUsage.CopyDst);
            if (textureUsageBits == ERHITextureUsage.ResolveTarget)
            {
                return RHICapability.Unavailable(
                    "Metal cannot exactly prove resolve support for this format combination.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            if ((query.Usage & ERHITextureUsage.DepthStencil) != 0 &&
                (RHIBarrierUtility.InferAspectMask(query.Format) &
                    (ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil)) == 0)
            {
                return RHICapability.Unavailable(
                    $"Metal DepthStencil usage requires a depth/stencil format, not {query.Format}.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            MTLPixelFormat pixelFormat;
            try
            {
                pixelFormat = MetalUtility.ConvertToMetalPixelFormat(query.Format);
            }
            catch (ArgumentOutOfRangeException)
            {
                return RHICapability.Unavailable(
                    $"Metal has no pixel-format mapping for {query.Format}.",
                    ERHICapabilityProbeKind.BackendContract,
                    "SharpGPU Metal pixel-format lowering");
            }

            ulong sampleCount = checked((ulong)query.SampleCount);
            if (!m_NativeDevice.SupportsTextureSampleCount(sampleCount))
            {
                return RHICapability.Unavailable(
                    $"Metal does not support {sampleCount}x MSAA.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }

            MTLTextureDescriptor descriptor = query.Dimension switch
            {
                ERHITextureDimension.Texture3D =>
                    MTLTextureDescriptor.Texture2DDescriptor(pixelFormat, 1, 1, false),
                _ => MTLTextureDescriptor.Texture2DDescriptor(pixelFormat, 1, 1, false),
            };
            descriptor.TextureType = query.Dimension switch
            {
                ERHITextureDimension.Texture3D => MTLTextureType.Type3D,
                ERHITextureDimension.TextureCube => MTLTextureType.Cube,
                ERHITextureDimension.TextureCubeArray => MTLTextureType.CubeArray,
                ERHITextureDimension.Texture2DArray =>
                    sampleCount == 1
                        ? MTLTextureType.Type2DArray
                        : MTLTextureType.Type2DMultisampleArray,
                ERHITextureDimension.Texture2DArrayMS => MTLTextureType.Type2DMultisampleArray,
                ERHITextureDimension.Texture2DMS => MTLTextureType.Type2DMultisample,
                _ => sampleCount == 1
                    ? MTLTextureType.Type2D
                    : MTLTextureType.Type2DMultisample,
            };
            descriptor.Width = 1;
            descriptor.Height = 1;
            descriptor.Depth = 1UL;
            descriptor.ArrayLength = 1;
            descriptor.SampleCount = sampleCount;
            descriptor.Usage = MTLTextureUsage.Unknown;
            if ((query.Usage & ERHITextureUsage.ShaderResource) != 0)
            {
                descriptor.Usage |= MTLTextureUsage.ShaderRead;
            }
            if ((query.Usage & ERHITextureUsage.UnorderedAccess) != 0)
            {
                descriptor.Usage |= MTLTextureUsage.ShaderWrite;
            }
            if ((query.Usage & (
                    ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.DepthStencil)) != 0)
            {
                descriptor.Usage |= MTLTextureUsage.RenderTarget;
            }
            if (descriptor.Usage == MTLTextureUsage.Unknown)
            {
                return RHICapability.Unavailable(
                    (query.Usage & ERHITextureUsage.ResolveTarget) != 0
                        ? "Metal cannot exactly prove resolve support for this format combination."
                        : "Metal cannot map the requested usage to MTLTextureUsage.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }
            descriptor.StorageMode = MTLStorageMode.Private;

            MTLTexture texture = default;
            try
            {
                texture = m_NativeDevice.NewTexture(descriptor);
                if (texture.NativePtr == IntPtr.Zero)
                {
                    return RHICapability.Unavailable(
                        $"Metal rejected {query.Format} at {sampleCount}x for the queried dimension/usage.",
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        ProbeSource);
                }
            }
            finally
            {
                if (texture.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(texture);
                }
                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor);
                }
            }

            ERHIFormatSupportOperation mask = ERHIFormatSupportOperation.None;
            if ((query.Usage & ERHITextureUsage.ShaderResource) != 0)
            {
                mask |= ERHIFormatSupportOperation.Sample;
            }
            if ((query.Usage & ERHITextureUsage.UnorderedAccess) != 0)
            {
                mask |= ERHIFormatSupportOperation.StorageStore;
            }
            if ((query.Usage & ERHITextureUsage.RenderTarget) != 0 &&
                RHIBarrierUtility.InferAspectMask(query.Format) ==
                    ERHITextureAspectMask.Color)
            {
                mask |= ERHIFormatSupportOperation.ColorAttachment;
                if (IsKnownMetalBlendableFormat(query.Format))
                {
                    mask |= ERHIFormatSupportOperation.Blend;
                }
            }
            if ((query.Usage & ERHITextureUsage.DepthStencil) != 0 &&
                (RHIBarrierUtility.InferAspectMask(query.Format) &
                    (ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil)) != 0)
            {
                mask |= ERHIFormatSupportOperation.DepthStencilAttachment;
            }

            if (mask == ERHIFormatSupportOperation.None)
            {
                return RHICapability.Unavailable(
                    "Metal texture creation succeeded but no requested operation could be proven.",
                    ERHICapabilityProbeKind.RuntimeObjectProbe,
                    ProbeSource);
            }

            return RHICapability.Available(
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeSpecialized,
                ERHICapabilityProbeKind.RuntimeObjectProbe,
                ProbeSource,
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.SupportedFormatOperationMask,
                        (ulong)mask)));
        }

        public override int QueryCooperativeMatrixConfigs(
            Span<RHICooperativeMatrixConfig> destination)
        {
            ThrowIfDisposed();
            return CopyCooperativeMatrixConfigs(
                ReadOnlySpan<RHICooperativeMatrixConfig>.Empty,
                destination);
        }

        public override RHIClockCalibration QueryClockCalibration(
            ERHIPipelineType queue,
            int queueIndex = 0)
        {
            ThrowIfDisposed();
            Capabilities.Synchronization.CalibratedTimestamps.Require(
                "Synchronization.CalibratedTimestamps");
            throw new NotSupportedException(
                "Synchronization.CalibratedTimestamps is unavailable: " +
                Capabilities.Synchronization.CalibratedTimestamps.UnavailableReason);
        }

        private static bool IsKnownMetalBlendableFormat(
            ERHIPixelFormat format) =>
            format is
                ERHIPixelFormat.R8_UNorm or
                ERHIPixelFormat.R8_SNorm or
                ERHIPixelFormat.R16_Float or
                ERHIPixelFormat.R8G8_UNorm or
                ERHIPixelFormat.R8G8_SNorm or
                ERHIPixelFormat.R16G16_Float or
                ERHIPixelFormat.R8G8B8A8_UNorm or
                ERHIPixelFormat.R8G8B8A8_UNorm_Srgb or
                ERHIPixelFormat.R8G8B8A8_SNorm or
                ERHIPixelFormat.B8G8R8A8_UNorm or
                ERHIPixelFormat.B8G8R8A8_UNorm_Srgb or
                ERHIPixelFormat.R10G10B10A2_UNorm or
                ERHIPixelFormat.R11G11B10_Float or
                ERHIPixelFormat.R16G16B16A16_Float;

        public override RHIRasterAttachmentShaderAbi
            QueryRasterAttachmentShaderAbi(
                in RHIRasterAttachmentShaderAbiDescriptor descriptor)
        {
            ThrowIfDisposed();
            MetalPipelineLayout pipelineLayout =
                descriptor.PipelineLayout as MetalPipelineLayout ??
                throw new ArgumentException(
                    "Metal attachment shader ABI requires a Metal pipeline layout.",
                    nameof(descriptor));
            if (!ReferenceEquals(pipelineLayout.Device, this))
            {
                throw new ArgumentException(
                    "Metal attachment shader ABI pipeline layout belongs to a different device.",
                    nameof(descriptor));
            }

            RHIAttachmentInterfaceSignature signature =
                descriptor.AttachmentInterface;
            List<RHIRasterAttachmentShaderBinding> bindings = new();
            for (int logicalAttachment = 0;
                 logicalAttachment < signature.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                int inputSlot =
                    RHIRasterAttachmentShaderAbiFactory.FindInputSlot(
                        in signature,
                        logicalAttachment);
                int outputLocation =
                    RHIRasterAttachmentShaderAbiFactory.FindOutputLocation(
                        in signature,
                        logicalAttachment);
                if (inputSlot < 0 && outputLocation < 0)
                {
                    continue;
                }
                bool readWrite = inputSlot >= 0 && outputLocation >= 0;
                RHIRawShaderBindingLocation input = default;
                RHIRawShaderBindingLocation output = default;
                if (inputSlot >= 0)
                {
                    int colorLocation = readWrite
                        ? outputLocation
                        : inputSlot;
                    input = new RHIRawShaderBindingLocation(
                        ERHIRawShaderBindingKind.ColorAttachment,
                        checked((uint)colorLocation));
                }
                if (outputLocation >= 0)
                {
                    output = new RHIRawShaderBindingLocation(
                        ERHIRawShaderBindingKind.ColorAttachment,
                        checked((uint)outputLocation));
                }
                bindings.Add(new RHIRasterAttachmentShaderBinding(
                    logicalAttachment,
                    inputSlot,
                    outputLocation,
                    input,
                    output));
            }
            return RHIRasterAttachmentShaderAbiFactory.Create(
                BackendType,
                in descriptor,
                bindings.ToArray());
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

        public override RHIBindingTableLayout CreateBindingTableLayout(in RHIBindingTableLayoutDescriptor descriptor)
        {
            return new MetalBindingTableLayout(this, descriptor);
        }

        public override RHIBindingTable CreateBindingTable(in RHIBindingTableDescriptor descriptor)
        {
            return new MetalBindingTable(this, descriptor);
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
            Capabilities.FunctionLibrary.NativeLibrary.Require("FunctionLibrary.NativeLibrary");
            return new MetalFunctionLibrary(this, descriptor);
        }

        public override RHIFunctionTable CreateFunctionTable()
        {
            ThrowIfDisposed();
            Capabilities.RayTracing.Pipeline.Require("Metal ray-tracing function tables");
            return new MetalFunctionTable(this);
        }

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            return new MetalComputePipeline(this, descriptor);
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.RayTracing.Pipeline.Require("Metal ray-tracing pipelines");
            return new MetalRaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            if (RHIRasterPipelineContract.RequestsMeshPath(in descriptor))
            {
                Capabilities.Mesh.MeshShader.Require("Metal mesh-shader pipelines");
                if (descriptor.PrimitiveAssembler.MeshletAssembler is { TaskFunction: not null })
                {
                    Capabilities.Mesh.TaskShader.Require("Metal task-shader pipelines");
                }
            }

            return new MetalRasterPipeline(this, descriptor);
        }

        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Metal machine-learning pipelines");
            if (descriptor.Binary.Format != ERHIMLBinaryFormat.MetalPackageV1)
            {
                throw new InvalidOperationException(
                    $"Metal ML pipeline requires {nameof(ERHIMLBinaryFormat.MetalPackageV1)} binary.");
            }

            return new MetalMLPipeline(this, descriptor);
        }

        public override RHIMLBindingTable CreateMLBindingTable(in RHIMLBindingTableDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Metal machine-learning binding tables");
            return new MetalMLBindingTable(this, descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Metal machine-learning tensors");
            return new MetalTensor(this, descriptor);
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.WorkGraph.Execution.Require("Metal work graphs");
            throw new NotSupportedException(
                "Metal Work Graph execution is not exposed by SharpGPU.");
        }

        public override RHIPipelineCache CreatePipelineCache()
        {
            Capabilities.PipelineCache.NativeCache.Require("Metal pipeline cache");
            throw new NotSupportedException(
                "Metal pipeline cache is not exposed by SharpGPU.");
        }
        public override RHIIndirectCommandLayout CreateIndirectCommandLayout(in RHIIndirectCommandLayoutDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new MetalIndirectCommandLayout(this, descriptor);
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

            string metalGpuFamilyName = ProbeMetalGpuFamilyName();
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
            string waveOperationsProbeSource =
                $"Metal SIMD-group width is 32 or 64; device-wide exact width is not queried (MTLGPUFamily.{metalGpuFamilyName})";
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

            RHICapability layoutIndirectUnavailable = RHICapability.Unavailable(
                "Metal layout-driven indirect execution requires the unimplemented stream-to-ICB lowering path.",
                ERHICapabilityProbeKind.BackendContract,
                "SharpGPU Metal layout-stream lowering");
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
                    framebufferReadWrite: Probe(
                        m_RasterCapabilities.FramebufferLocalRead,
                        "Metal programmable blending / [[color(n)]] input",
                        "The complete Metal framebuffer read/write mechanism is unavailable."),
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
                    variableRateShadingPerDraw: Probe(
                        false,
                        "SharpGPU Metal variable-rate rasterization lowering",
                        "Variable-rate shading is not exposed by the Metal backend.",
                        probeKind: ERHICapabilityProbeKind.BackendContract),
                    variableRateShadingPerPrimitive: Probe(
                        false,
                        "SharpGPU Metal variable-rate rasterization lowering",
                        "Variable-rate shading is not exposed by the Metal backend.",
                        probeKind: ERHICapabilityProbeKind.BackendContract),
                    variableRateShadingAttachment: Probe(
                        false,
                        "SharpGPU Metal variable-rate rasterization lowering",
                        "Variable-rate shading is not exposed by the Metal backend.",
                        probeKind: ERHICapabilityProbeKind.BackendContract),
                    variableRateShadingCombiners: Probe(
                        false,
                        "SharpGPU Metal variable-rate rasterization lowering",
                        "Variable-rate shading is not exposed by the Metal backend.",
                        probeKind: ERHICapabilityProbeKind.BackendContract),
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
                        rasterLimits),
                    samplerFeedback: RHICapability.Unavailable(
                        "Sampler feedback has no Metal equivalent; it is a DX12-only optional facet and is not framebuffer-local read or an attachment feedback loop.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sampler-feedback contract")),
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
                        m_SupportsNativeArgumentTable,
                        "MTL4ArgumentTable runtime object probe",
                        "Metal binding tables are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe,
                        limits: bindingLimits),
                    partiallyBoundDescriptors: Probe(
                        m_SupportsNativeArgumentTable,
                        "MTL4ArgumentTable nil-entry contract",
                        "Metal argument-table nil entries are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    updateAfterBindDescriptors: Probe(
                        false,
                        "SharpGPU external argument-table synchronization contract",
                        "Argument-table mutation while GPU work is pending is intentionally not exposed."),
                    nullDescriptors: Probe(
                        m_SupportsNativeArgumentTable,
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
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    calibratedTimestamps: RHICapability.Unavailable(
                        "SharpMetal MTLDevice.SampleTimestamps passes timestamps by value and cannot return a calibrated CPU/GPU pair.",
                        ERHICapabilityProbeKind.BackendContract,
                        "MTLDevice.sampleTimestamps:gpuTimestamp:")),
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
                    gpuVirtualAddress: Probe(
                        m_SupportsMetal4,
                        "MTLBuffer.gpuAddress",
                        "Metal buffer GPU addresses require a Metal 4 device.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
                    sparseBuffer: RHICapability.Unavailable(
                        "Metal sparse-buffer mapping is not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sparse-buffer contract"),
                    sparseTexture2D: Probe(
                        m_SupportsPlacementSparse,
                        "MTLDevice.supportsPlacementSparse + MTL4CommandQueue.updateTextureMappings",
                        "The Metal runtime does not expose placement sparse resources.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    sparseTexture3D: RHICapability.Unavailable(
                        "Metal placement-sparse 3D textures are not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sparse-texture contract"),
                    sparseMsaa: RHICapability.Unavailable(
                        "Metal placement-sparse MSAA textures are not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sparse-texture contract"),
                    sparseMipTail: RHICapability.Unavailable(
                        "Metal placement-sparse mip-tail binding is not implemented as a distinct native contract.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sparse-texture contract"),
                    sparseTileGeometry: Probe(
                        m_SupportsPlacementSparse,
                        "MTLDevice.sparseTileSizeInBytes + sparseTileSizeWithTextureType",
                        "Metal sparse tile geometry requires placement sparse.",
                        strategy: ERHICapabilityStrategy.NativeSpecialized,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe,
                        limits: ProbeMetalSparseTileSizeBytes()),
                    residency: RHICapability.Unavailable(
                        "MTLResidencySet does not provide the explicit completion-fence contract required by SharpGPU.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Metal explicit residency completion contract"),
                    budgetQuery: Probe(
                        m_NativeDevice.RecommendedMaxWorkingSetSize != 0,
                        "MTLDevice.recommendedMaxWorkingSetSize + currentAllocatedSize",
                        "Metal did not report a device working-set budget.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.RuntimeObjectProbe),
                    sparseAliasing: RHICapability.Unavailable(
                        "Metal sparse aliasing is not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal sparse-texture contract")),
                storage: MetalStorageQueue.CreateCapabilities(this),
                pipelineCache: new RHIPipelineCacheCapabilities(
                    nativeCache: RHICapability.Unavailable(
                        "ADR-0065: MTLBinaryArchive is URL-based and cannot satisfy the caller-owned in-memory blob contract. Temp-file and UrlArchive lowering are rejected.",
                        ERHICapabilityProbeKind.BackendContract,
                        "ADR-0065 Metal pipeline cache")),
                presentation: new RHIPresentationCapabilities(
                    swapChain: Probe(
                        m_SupportsMetal4,
                        "CAMetalLayer runtime on a Metal 4 device with queue-ordered acquire/present synchronization",
                        "Metal swapchain creation requires CAMetalLayer and Metal 4.",
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        probeKind: ERHICapabilityProbeKind.ApiVersion),
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
                    meshShader: RHICapability.Unavailable(
                        "Metal mesh shaders are not exposed by the current SharpGPU factory surface.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal factory surface"),
                    taskShader: RHICapability.Unavailable(
                        "Metal task/object shaders are not exposed by the current SharpGPU factory surface.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Metal factory surface")),
                machineLearning: new RHIMachineLearningCapabilities(
                    execution: isMLSupported
                        ? RHICapability.Available(
                            ERHICapabilityTier.Tier1,
                            ERHICapabilityStrategy.NativeSpecialized,
                            ERHICapabilityProbeKind.NativeFeatureQuery,
                            "MTL4MachineLearningCommandEncoder (Metal4-native)")
                        : RHICapability.Unavailable(
                            metalMLUnavailableReason ??
                            "Metal ML remains Unavailable until a Metal4-native program builder and stable multi-dispatch exist.",
                            ERHICapabilityProbeKind.BackendContract,
                            "SharpGPU Metal MTL4-only ML contract (ADR-0051)")),
                workGraph: RHIWorkGraphCapabilities.CreateUnavailable(
                    "Metal Work Graph execution is not exposed by SharpGPU.",
                    "SharpGPU Metal factory surface"),
                indirectCommandBuffer: new RHIIndirectCommandBufferCapabilities(layoutIndirectUnavailable, new RHIIndirectTokenCapabilities(layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable)),
                compute: new RHIComputeCapabilities(
                    ERHIWaveOperationStrategy.Basic,
                    waveOperations: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.BackendContract,
                        waveOperationsProbeSource,
                        computeLimits),
                    variableSubgroupSize: RHICapability.Unavailable(
                        "Metal has no device-wide variable SIMD-group width query; threadExecutionWidth is per-pipeline.",
                        ERHICapabilityProbeKind.BackendContract,
                        waveOperationsProbeSource),
                    cooperativeMatrix: RHICapability.Unavailable(
                        "Metal simdgroup_matrix device query is not present in SharpMetal.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpMetal MTLDevice")),
                functionLibrary: CreateMetalFunctionLibraryCapabilities(
                    isRayTracingSupported));
        }

        private static RHIFunctionLibraryCapabilities CreateMetalFunctionLibraryCapabilities(
            bool raytracingAvailable)
        {
            ulong reusable =
                (ulong)ERHIFunctionLibraryReusablePipelineClass.Raster |
                (ulong)ERHIFunctionLibraryReusablePipelineClass.Compute |
                (raytracingAvailable
                    ? (ulong)ERHIFunctionLibraryReusablePipelineClass.Raytracing
                    : 0UL);
            return RHIFunctionLibraryCapabilities.CreateNative(
                "MTLLibrary.NewFunction shared-library views",
                reusable,
                RHIFunctionLibraryCapabilities.PayloadKindBit(ERHIShaderPayloadKind.MetalLibrary) |
                    RHIFunctionLibraryCapabilities.PayloadKindBit(ERHIShaderPayloadKind.MslSource),
                rasterComputeUnavailableReason: null);
        }

        private RHICapabilityLimits ProbeMetalSparseTileSizeBytes()
        {
            if (!m_SupportsPlacementSparse)
            {
                return default;
            }

            ulong tileSizeBytes = m_NativeDevice.SparseTileSizeInBytes(
                MetalSparseMemoryUtility.SparsePageSize);
            if (tileSizeBytes == 0)
            {
                return default;
            }

            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.SparseStandardTileSizeBytes,
                    tileSizeBytes));
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
                unavailableReason = "Metal ML requires Metal 4 (MTL4MachineLearningPipelineState / CommandEncoder).";
                return false;
            }

            // ADR-0052 / TASK-20260726 W4: Binary-only public surface (MetalPackageV1) + MTL4 runtime.
            // Capability opens when Metal 4 is present; matching-host multi-dispatch is proven by
            // SharpGpuMetalQualified MetalMLStabilityProbeTests (CoreML → metal-package-builder fixtures).
            return true;
        }
        internal void RegisterMetalMLPipelineState(in MTL4MachineLearningPipelineState pipelineState)
        {
            if (pipelineState.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLPipelineStates.Add(pipelineState);
        }

        internal void RegisterMetalMLNativeArgumentTable(in MTL4ArgumentTable bindingTable)
        {
            if (bindingTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLNativeArgumentTables.Add(bindingTable);
        }

        internal void RegisterMetalMLIntermediatesHeap(MetalHeap heap)
        {
            if (heap == null || heap.NativeHeap.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_MetalMLIntermediatesHeapOwners.Add(heap);
            m_MetalMLIntermediatesHeaps.Add(heap.NativeHeap);
        }

        private void ReleaseMetalMLNativeObjects()
        {
            // Dispose managed owners first; MetalHeap.Release releases the native MTLHeap.
            for (int i = 0; i < m_MetalMLIntermediatesHeapOwners.Count; ++i)
            {
                m_MetalMLIntermediatesHeapOwners[i].Dispose();
            }

            m_MetalMLIntermediatesHeapOwners.Clear();
            m_MetalMLIntermediatesHeaps.Clear();

            for (int i = 0; i < m_MetalMLNativeArgumentTables.Count; ++i)
            {
                MTL4ArgumentTable bindingTable = m_MetalMLNativeArgumentTables[i];
                if (bindingTable.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(bindingTable);
                }
            }

            m_MetalMLNativeArgumentTables.Clear();

            for (int i = 0; i < m_MetalMLPipelineStates.Count; ++i)
            {
                MTL4MachineLearningPipelineState pipelineState = m_MetalMLPipelineStates[i];
                if (pipelineState.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pipelineState);
                }
            }

            m_MetalMLPipelineStates.Clear();
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
                encoderMappingSelector);
        }

        private string ProbeMetalGpuFamilyName()
        {
            if (SafeSupportsFamily(MTLGPUFamily.Apple10)) return nameof(MTLGPUFamily.Apple10);
            if (SafeSupportsFamily(MTLGPUFamily.Apple9)) return nameof(MTLGPUFamily.Apple9);
            if (SafeSupportsFamily(MTLGPUFamily.Apple8)) return nameof(MTLGPUFamily.Apple8);
            if (SafeSupportsFamily(MTLGPUFamily.Apple7)) return nameof(MTLGPUFamily.Apple7);
            if (SafeSupportsFamily(MTLGPUFamily.Apple6)) return nameof(MTLGPUFamily.Apple6);
            if (SafeSupportsFamily(MTLGPUFamily.Apple5)) return nameof(MTLGPUFamily.Apple5);
            if (SafeSupportsFamily(MTLGPUFamily.Apple4)) return nameof(MTLGPUFamily.Apple4);
            if (SafeSupportsFamily(MTLGPUFamily.Apple3)) return nameof(MTLGPUFamily.Apple3);
            if (SafeSupportsFamily(MTLGPUFamily.Apple2)) return nameof(MTLGPUFamily.Apple2);
            if (SafeSupportsFamily(MTLGPUFamily.Apple1)) return nameof(MTLGPUFamily.Apple1);
            if (SafeSupportsFamily(MTLGPUFamily.Mac2)) return nameof(MTLGPUFamily.Mac2);
            if (SafeSupportsFamily(MTLGPUFamily.Metal4)) return nameof(MTLGPUFamily.Metal4);
            if (SafeSupportsFamily(MTLGPUFamily.Metal3)) return nameof(MTLGPUFamily.Metal3);
            if (SafeSupportsFamily(MTLGPUFamily.Common3)) return nameof(MTLGPUFamily.Common3);
            return "Unknown";
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

            if (m_NativeDevice.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDevice);
            }
        }
    }
}
