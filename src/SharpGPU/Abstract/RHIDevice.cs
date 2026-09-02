using System;
using SharpGPU.Core;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SharpGPU
{
    public class RHIDeviceLimit
    {
        public readonly int UniformBufferAlignment;
        public readonly int UploadBufferAlignment;
        public readonly int UploadBufferTextureAlignment;
        public readonly int UploadBufferTextureRowAlignment;
        public readonly int MaxMSAACount;
        public readonly int MaxBoundTexture;
        public readonly int MinWavefrontSize;
        public readonly int MaxWavefrontSize;
        public readonly int MaxComputeThreads;
        public readonly int MaxGroupShareMemorySize;
        public readonly int MaxVertexInputBindings;
        public readonly int MaxColorAttachments;
        public readonly int MaxTexture2DSize;
        public readonly int MaxTextureCubeSize;

        internal RHIDeviceLimit(in int uniformBufferAlignment,
                                in int uploadBufferAlignment,
                                in int uploadBufferTextureAlignment,
                                in int uploadBufferTextureRowAlignment, 
                                in int maxMSAACount,
                                in int maxBoundTexture,
                                in int minWavefrontSize,
                                in int maxWavefrontSize,
                                in int maxComputeThreads,
                                in int maxGroupShareMemorySize,
                                in int maxVertexInputBindings,
                                in int maxColorAttachments,
                                in int maxTexture2DSize,
                                in int maxTextureCubeSize)
        {
            UniformBufferAlignment = uniformBufferAlignment;
            UploadBufferAlignment = uploadBufferAlignment;
            UploadBufferTextureAlignment = uploadBufferTextureAlignment;
            UploadBufferTextureRowAlignment = uploadBufferTextureRowAlignment;
            MaxMSAACount = maxMSAACount;
            MaxBoundTexture = maxBoundTexture;
            MinWavefrontSize = minWavefrontSize;
            MaxWavefrontSize = maxWavefrontSize;
            MaxComputeThreads = maxComputeThreads;
            MaxGroupShareMemorySize = maxGroupShareMemorySize;
            MaxVertexInputBindings = maxVertexInputBindings;
            MaxColorAttachments = maxColorAttachments;
            MaxTexture2DSize = maxTexture2DSize;
            MaxTextureCubeSize = maxTextureCubeSize;
        }
    }

    public struct RHIVendorId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
        public ERHIVendorType StringValue => (ERHIVendorType)IntValue;
    }

    public struct RHIDeviceId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
    }

    /// <summary>
    /// Describes one exact color attachment pipeline combination. Input plus
    /// output denotes framebuffer read/write; blend remains orthogonal.
    /// </summary>
    public readonly struct RHIRasterAttachmentSupportQuery
    {
        /// <summary>Gets the logical color format.</summary>
        public ERHIPixelFormat Format { get; }
        /// <summary>Gets the exact raster sample count.</summary>
        public ERHISampleCount SampleCount { get; }
        /// <summary>Gets whether the attachment is a shader input.</summary>
        public bool IsInput { get; }
        /// <summary>Gets whether the attachment is a shader output.</summary>
        public bool IsOutput { get; }
        /// <summary>Gets whether input access uses layered attachment shape.</summary>
        public bool IsLayered { get; }
        /// <summary>Gets the unchanged fixed-function blend state.</summary>
        public RHIBlendDescriptor Blend { get; }
        /// <summary>Gets the unchanged alpha-to-coverage state.</summary>
        public bool AlphaToCoverage { get; }

        /// <summary>Creates a validated exact attachment support query.</summary>
        public RHIRasterAttachmentSupportQuery(
            ERHIPixelFormat format,
            ERHISampleCount sampleCount,
            bool isInput,
            bool isOutput,
            in RHIBlendDescriptor blend,
            bool alphaToCoverage = false,
            bool isLayered = false)
        {
            if (!isInput && !isOutput)
            {
                throw new ArgumentException(
                    "A raster attachment support query must be an input, " +
                    "an output, or both.");
            }
            if (isLayered && !isInput)
            {
                throw new ArgumentException(
                    "Layered raster attachment access requires an input declaration.",
                    nameof(isLayered));
            }
            if (format == ERHIPixelFormat.Unknown ||
                RHIBarrierUtility.InferAspectMask(format) !=
                    ERHITextureAspectMask.Color)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Raster attachment support queries require a color format.");
            }
            if (sampleCount is not (
                    ERHISampleCount.None or
                    ERHISampleCount.Count2 or
                    ERHISampleCount.Count4 or
                    ERHISampleCount.Count8))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCount),
                    sampleCount,
                    "Raster attachment support queries require a concrete sample count.");
            }

            Format = format;
            SampleCount = sampleCount;
            IsInput = isInput;
            IsOutput = isOutput;
            IsLayered = isLayered;
            Blend = blend;
            AlphaToCoverage = alphaToCoverage;
        }
    }

    public enum ERHITextureTiling : byte
    {
        Optimal,
        Linear,
        Pending
    }

    [Flags]
    public enum ERHIFormatSupportOperation : ulong
    {
        None = 0,
        Sample = 1UL << 0,
        StorageLoad = 1UL << 1,
        StorageStore = 1UL << 2,
        Atomic = 1UL << 3,
        ColorAttachment = 1UL << 4,
        DepthStencilAttachment = 1UL << 5,
        Blend = 1UL << 6,
        ResolveSource = 1UL << 7,
        LinearFilter = 1UL << 8,
        VertexBuffer = 1UL << 9,
        IndexBuffer = 1UL << 10,
        ResolveDestination = 1UL << 11
    }

    /// <summary>
    /// Describes one exact format, usage, dimension, sample-count, and tiling
    /// combination. The returned operation mask is orthogonal: vertex and
    /// index bits report whether the format itself supports those buffer
    /// uses, even when the query names a texture dimension.
    /// </summary>
    public readonly struct RHIFormatSupportQuery
    {
        public ERHIPixelFormat Format { get; }
        public ERHITextureUsage Usage { get; }
        public ERHITextureDimension Dimension { get; }
        public ERHISampleCount SampleCount { get; }
        public ERHITextureTiling Tiling { get; }

        public RHIFormatSupportQuery(
            ERHIPixelFormat format,
            ERHITextureUsage usage,
            ERHITextureDimension dimension,
            ERHISampleCount sampleCount,
            ERHITextureTiling tiling)
        {
            if (format is ERHIPixelFormat.Unknown or ERHIPixelFormat.Pending ||
                !Enum.IsDefined(format))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Format support queries require a concrete pixel format.");
            }
            if (!IsKnownTextureUsage(usage))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(usage),
                    usage,
                    "Format support queries reject None, zero, or unknown texture usage.");
            }
            if (dimension == ERHITextureDimension.Pending ||
                !Enum.IsDefined(dimension))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimension),
                    dimension,
                    "Format support queries require a concrete texture dimension.");
            }
            if (sampleCount is not (
                    ERHISampleCount.None or
                    ERHISampleCount.Count2 or
                    ERHISampleCount.Count4 or
                    ERHISampleCount.Count8))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampleCount),
                    sampleCount,
                    "Format support queries require a concrete sample count.");
            }
            if (tiling == ERHITextureTiling.Pending ||
                !Enum.IsDefined(tiling))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tiling),
                    tiling,
                    "Format support queries require a concrete texture tiling.");
            }

            Format = format;
            Usage = usage;
            Dimension = dimension;
            SampleCount = sampleCount;
            Tiling = tiling;
        }

        internal static bool IsKnownTextureUsage(ERHITextureUsage usage)
        {
            const ERHITextureUsage knownBits =
                ERHITextureUsage.CopySrc |
                ERHITextureUsage.CopyDst |
                ERHITextureUsage.DepthStencil |
                ERHITextureUsage.RenderTarget |
                ERHITextureUsage.ResolveTarget |
                ERHITextureUsage.ShaderResource |
                ERHITextureUsage.UnorderedAccess;
            return usage != ERHITextureUsage.None &&
                (usage & ~knownBits) == 0;
        }
    }

    /// <summary>
    /// Describes one exact MSAA source and single-sample destination pair
    /// for a resolve query. Extent and layer equality stay encoder-time.
    /// </summary>
    public readonly struct RHIResolveSupportQuery
    {
        public ERHIPixelFormat SourceFormat { get; }
        public ERHITextureUsage SourceUsage { get; }
        public ERHITextureDimension SourceDimension { get; }
        public ERHISampleCount SourceSampleCount { get; }
        public ERHITextureTiling SourceTiling { get; }
        public ERHIPixelFormat DestinationFormat { get; }
        public ERHITextureUsage DestinationUsage { get; }
        public ERHITextureDimension DestinationDimension { get; }
        public ERHISampleCount DestinationSampleCount { get; }
        public ERHITextureTiling DestinationTiling { get; }
        public ERHITextureAspectMask Aspect { get; }
        public ERHIResolveMode ResolveMode { get; }

        public RHIResolveSupportQuery(
            ERHIPixelFormat sourceFormat,
            ERHITextureUsage sourceUsage,
            ERHITextureDimension sourceDimension,
            ERHISampleCount sourceSampleCount,
            ERHITextureTiling sourceTiling,
            ERHIPixelFormat destinationFormat,
            ERHITextureUsage destinationUsage,
            ERHITextureDimension destinationDimension,
            ERHISampleCount destinationSampleCount,
            ERHITextureTiling destinationTiling,
            ERHITextureAspectMask aspect,
            ERHIResolveMode resolveMode)
        {
            ValidateFormat(sourceFormat, nameof(sourceFormat));
            ValidateUsage(sourceUsage, nameof(sourceUsage));
            ValidateDimension(sourceDimension, nameof(sourceDimension));
            ValidateTiling(sourceTiling, nameof(sourceTiling));
            ValidateFormat(destinationFormat, nameof(destinationFormat));
            ValidateUsage(destinationUsage, nameof(destinationUsage));
            ValidateDimension(destinationDimension, nameof(destinationDimension));
            ValidateTiling(destinationTiling, nameof(destinationTiling));
            if (sourceSampleCount is not (
                    ERHISampleCount.Count2 or
                    ERHISampleCount.Count4 or
                    ERHISampleCount.Count8))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sourceSampleCount),
                    sourceSampleCount,
                    "Resolve support queries require an MSAA source sample count.");
            }

            if (destinationSampleCount != ERHISampleCount.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(destinationSampleCount),
                    destinationSampleCount,
                    "Resolve support queries require a single-sample destination.");
            }

            const ERHITextureAspectMask knownAspects =
                ERHITextureAspectMask.Color |
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
            if (aspect == ERHITextureAspectMask.None ||
                (aspect & ~knownAspects) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(aspect),
                    aspect,
                    "Resolve support queries require a concrete texture aspect.");
            }

            if (resolveMode == ERHIResolveMode.Pending ||
                !Enum.IsDefined(resolveMode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolveMode),
                    resolveMode,
                    "Resolve support queries require a concrete resolve mode.");
            }

            if (sourceFormat != destinationFormat)
            {
                throw new ArgumentException(
                    "Resolve support queries require the source and destination formats to match exactly.",
                    nameof(destinationFormat));
            }

            if ((destinationUsage & ERHITextureUsage.ResolveTarget) == 0)
            {
                throw new ArgumentException(
                    "Resolve support queries require DestinationUsage to include ResolveTarget.",
                    nameof(destinationUsage));
            }

            SourceFormat = sourceFormat;
            SourceUsage = sourceUsage;
            SourceDimension = sourceDimension;
            SourceSampleCount = sourceSampleCount;
            SourceTiling = sourceTiling;
            DestinationFormat = destinationFormat;
            DestinationUsage = destinationUsage;
            DestinationDimension = destinationDimension;
            DestinationSampleCount = destinationSampleCount;
            DestinationTiling = destinationTiling;
            Aspect = aspect;
            ResolveMode = resolveMode;
        }

        private static void ValidateFormat(
            ERHIPixelFormat format,
            string paramName)
        {
            if (format is ERHIPixelFormat.Unknown or ERHIPixelFormat.Pending ||
                !Enum.IsDefined(format))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    format,
                    "Resolve support queries require a concrete pixel format.");
            }
        }

        private static void ValidateUsage(
            ERHITextureUsage usage,
            string paramName)
        {
            if (!RHIFormatSupportQuery.IsKnownTextureUsage(usage))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    usage,
                    "Resolve support queries reject None, zero, or unknown texture usage.");
            }
        }

        private static void ValidateDimension(
            ERHITextureDimension dimension,
            string paramName)
        {
            if (dimension == ERHITextureDimension.Pending ||
                !Enum.IsDefined(dimension))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    dimension,
                    "Resolve support queries require a concrete texture dimension.");
            }
        }

        private static void ValidateTiling(
            ERHITextureTiling tiling,
            string paramName)
        {
            if (tiling == ERHITextureTiling.Pending ||
                !Enum.IsDefined(tiling))
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    tiling,
                    "Resolve support queries require a concrete texture tiling.");
            }
        }
    }

    public abstract class RHIDevice : Disposal
    {
        public string? Name => m_Name;
        public RHIVendorId VendorId => m_VendorId;
        public RHIDeviceId DeviceId => m_DeviceId;
        public string DriverVersion => m_DriverVersion;
        public ERHIDeviceType Type => m_Type;
        /// <summary>
        /// The backend kind this device belongs to (DirectX12 / Metal / Vulkan). Exposed on the
        /// abstract HAL so higher layers (e.g. SharpNeural's GPU backend) can branch on the active
        /// backend without taking a compile-time dependency on backend-specific types or performing
        /// runtime type checks. This is a HAL-level property, not part of the ML execution-layer
        /// surface frozen by ADR-0019.
        /// </summary>
        public abstract ERHIBackend BackendType { get; }
        public RHIDeviceLimit? Limit => m_Limit;
        public RHIDeviceCapabilities Capabilities => m_Capabilities;
        public RHIAdapterIdentity AdapterIdentity => m_AdapterIdentity;
        public int ComputeQueueCount => m_ComputeQueueCount;
        public int TransferQueueCount => m_TransferQueueCount;
        public int GraphicsQueueCount => m_GraphicsQueueCount;
        public ERHIDeviceState State =>
            (ERHIDeviceState)Volatile.Read(ref m_DeviceState);
        public RHIException? DeviceLossDiagnostic =>
            Volatile.Read(ref m_DeviceLossDiagnostic);

        protected string? m_Name;
        protected RHIVendorId m_VendorId;
        protected RHIDeviceId m_DeviceId;
        protected string m_DriverVersion = "Unknown";
        protected ERHIDeviceType m_Type;
        protected RHIDeviceLimit? m_Limit;
        protected RHIDeviceCapabilities m_Capabilities = RHIDeviceCapabilities.CreateUnprobed("RHIDevice subclass default");
        protected RHIAdapterIdentity m_AdapterIdentity;
        protected int m_ComputeQueueCount;
        protected int m_TransferQueueCount;
        protected int m_GraphicsQueueCount;
        protected Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>? m_CommandQueueMap;
        private readonly object m_DeviceStateLock = new();
        private int m_DeviceState =
            (int)ERHIDeviceState.Operational;
        private RHIException? m_DeviceLossDiagnostic;

        internal RHIException MarkDeviceLost(
            RHIException diagnostic)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            if (diagnostic.ErrorCode != ERHIErrorCode.DeviceLost)
            {
                throw new ArgumentException(
                    "A device-loss transition requires a DeviceLost diagnostic.",
                    nameof(diagnostic));
            }
            if (diagnostic.Backend != BackendType)
            {
                throw new ArgumentException(
                    "The device-loss diagnostic belongs to a different backend.",
                    nameof(diagnostic));
            }
            if (diagnostic.DeviceState is
                ERHIDeviceState.Unknown or
                ERHIDeviceState.Operational)
            {
                throw new ArgumentException(
                    "A device-loss diagnostic must carry Lost, Removed, or Reset state.",
                    nameof(diagnostic));
            }

            lock (m_DeviceStateLock)
            {
                if ((ERHIDeviceState)m_DeviceState !=
                    ERHIDeviceState.Operational)
                {
                    return m_DeviceLossDiagnostic ??
                        throw new InvalidOperationException(
                            "The device is unavailable without a loss diagnostic.");
                }

                m_DeviceLossDiagnostic = diagnostic;
                Volatile.Write(
                    ref m_DeviceState,
                    (int)diagnostic.DeviceState);
                return diagnostic;
            }
        }

        internal void ThrowIfDeviceUnavailable()
        {
            ThrowIfDisposed();
            ERHIDeviceState state = State;
            if (state == ERHIDeviceState.Operational)
            {
                return;
            }

            RHIException? diagnostic = DeviceLossDiagnostic;
            if (diagnostic == null)
            {
                throw new InvalidOperationException(
                    $"The {BackendType} device entered '{state}' without a diagnostic.");
            }

            throw diagnostic;
        }

        public abstract RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index);
        public abstract RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor);
        public abstract RHIFence CreateFence();
        public abstract RHISemaphore CreateSemaphore();
        public abstract RHIStorageQueue CreateStorageQueue();
        public abstract RHIQuery CreateQuery(in RHIQueryDescriptor descriptor);
        public abstract RHIHeap CreateHeap(in RHIHeapDescription descriptor);
        public virtual RHIResourceMemoryRequirements GetBufferMemoryRequirements(in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose exact buffer memory requirements.");
        }
        public virtual RHIResourceMemoryRequirements GetTextureMemoryRequirements(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose exact texture memory requirements.");
        }
        public abstract RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor);
        public virtual RHIBuffer CreatePlacedBuffer(
            RHIHeap heap,
            ulong heapOffset,
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose placed buffers.");
        }
        public abstract RHITexture CreateTexture(in RHITextureDescriptor descriptor);
        /// <summary>
        /// Creates an opaque sampler-feedback map paired with
        /// <see cref="RHISamplerFeedbackMapDescriptor.PairedTexture"/> at
        /// create time. The encoder cannot establish pairing.
        /// The returned <see cref="RHITexture"/> does not own the paired
        /// sampled texture; disposing the map does not dispose that texture.
        /// Destroy views first, then the map. The paired texture may outlive
        /// the map.
        /// Requires <see cref="RHIRasterCapabilities.SamplerFeedback"/>.
        /// </summary>
        public virtual RHITexture CreateSamplerFeedbackMap(
            in RHISamplerFeedbackMapDescriptor descriptor)
        {
            ThrowIfDisposed();
            ValidateSamplerFeedbackMapDescriptor(in descriptor);
            Capabilities.Raster.SamplerFeedback.Require("Raster.SamplerFeedback");
            throw new NotSupportedException(
                $"{BackendType} does not implement sampler-feedback map creation.");
        }
        /// <summary>
        /// Queries whether this device can express an exact attachment,
        /// sampling, and blend combination without semantic fallback.
        /// </summary>
        public abstract RHICapability QueryRasterAttachmentSupport(
            in RHIRasterAttachmentSupportQuery query);
        /// <summary>
        /// Queries the orthogonal operation mask for one exact format,
        /// usage, dimension, sample-count, and tiling combination.
        /// This does not replace <see cref="QueryRasterAttachmentSupport"/>;
        /// raster attachment plus blend combinations stay on that query.
        /// </summary>
        public abstract RHICapability QueryFormatSupport(
            in RHIFormatSupportQuery query);
        /// <summary>
        /// Queries whether this device can resolve one exact MSAA source
        /// into one exact single-sample destination. This does not replace
        /// <see cref="QueryRasterAttachmentSupport"/> or
        /// <see cref="QueryFormatSupport"/>.
        /// </summary>
        public abstract RHICapability QueryResolveSupport(
            in RHIResolveSupportQuery query);
        /// <summary>
        /// Returns a native GPU/CPU clock calibration for the selected queue.
        /// Requires <see cref="RHISynchronizationCapabilities.CalibratedTimestamps"/>.
        /// </summary>
        public abstract RHIClockCalibration QueryClockCalibration(
            ERHIPipelineType queue,
            int queueIndex = 0);
        /// <summary>
        /// Copies native cooperative-matrix configurations into
        /// <paramref name="destination"/>. Requires
        /// <see cref="RHIComputeCapabilities.CooperativeMatrix"/>.
        /// An empty destination returns 0. A destination that is too small
        /// returns the required count without writing.
        /// </summary>
        public virtual int QueryCooperativeMatrixConfigs(
            Span<RHICooperativeMatrixConfig> destination)
        {
            ThrowIfDisposed();
            return CopyCooperativeMatrixConfigs(
                ReadOnlySpan<RHICooperativeMatrixConfig>.Empty,
                destination);
        }

        protected int CopyCooperativeMatrixConfigs(
            ReadOnlySpan<RHICooperativeMatrixConfig> source,
            Span<RHICooperativeMatrixConfig> destination)
        {
            Capabilities.Compute.CooperativeMatrix.Require(
                "Compute.CooperativeMatrix");
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (destination.Length < source.Length)
            {
                return source.Length;
            }

            source.CopyTo(destination);
            return source.Length;
        }
        /// <summary>
        /// Queries the versioned target shader ABI for a logical attachment
        /// interface. SharpShader is not required to consume this contract.
        /// </summary>
        /// <summary>
        /// Validates create-time sampler-feedback pairing. Missing pairing
        /// throws before the capability gate so the encoder cannot be used to
        /// invent a pair.
        /// </summary>
        protected void ValidateSamplerFeedbackMapDescriptor(
            in RHISamplerFeedbackMapDescriptor descriptor)
        {
            if (descriptor.PairedTexture == null)
            {
                throw new ArgumentException(
                    "Sampler-feedback pairing must be established at create time. PairedTexture is required; the encoder cannot pair a map.",
                    nameof(descriptor));
            }

            RHITexture paired = descriptor.PairedTexture;
            RHITextureDescriptor pairedDescriptor = paired.Descriptor;
            if (paired.IsSamplerFeedbackMap)
            {
                throw new ArgumentException(
                    "A sampler-feedback map cannot be paired with another feedback map.",
                    nameof(descriptor));
            }

            if (pairedDescriptor.Dimension is not (
                    ERHITextureDimension.Texture2D or
                    ERHITextureDimension.Texture2DArray))
            {
                throw new ArgumentException(
                    "Sampler-feedback pairing requires a 2D or 2D-array sampled texture.",
                    nameof(descriptor));
            }

            if (pairedDescriptor.SampleCount != ERHISampleCount.None)
            {
                throw new ArgumentException(
                    "Sampler-feedback pairing does not accept a multisampled texture.",
                    nameof(descriptor));
            }

            if ((pairedDescriptor.UsageFlag & ERHITextureUsage.ShaderResource) == 0)
            {
                throw new ArgumentException(
                    "Sampler-feedback pairing requires a ShaderResource sampled texture.",
                    nameof(descriptor));
            }

            if (RHITexture.IsSamplerFeedbackOpaqueFormat(pairedDescriptor.Format))
            {
                throw new ArgumentException(
                    "Sampler-feedback pairing requires a sampled color texture, not an opaque feedback format.",
                    nameof(descriptor));
            }

            if (descriptor.Mode is ERHISamplerFeedbackMode.Pending ||
                !Enum.IsDefined(descriptor.Mode))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Mode,
                    "Sampler-feedback mode must be MinMip or MipRegionUsed.");
            }
        }

        protected static void RejectUnpairedSamplerFeedbackTexture(
            in RHITextureDescriptor descriptor)
        {
            if (RHITexture.IsSamplerFeedbackOpaqueFormat(descriptor.Format))
            {
                throw new ArgumentException(
                    "Sampler-feedback maps must be created with CreateSamplerFeedbackMap so pairing is established at create time.",
                    nameof(descriptor));
            }
        }

        protected static void ValidateTextureUsage(
            ERHITextureUsage usage,
            string parameterName)
        {
            if (!RHIFormatSupportQuery.IsKnownTextureUsage(usage))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    usage,
                    "Texture usage must be a non-zero combination of known ERHITextureUsage bits.");
            }
        }

        protected void ValidateQueryDescriptor(in RHIQueryDescriptor descriptor)
        {
            if (descriptor.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "A query descriptor must contain at least one slot.");
            }

            if (descriptor.Type == ERHIQueryType.Pending ||
                !Enum.IsDefined(descriptor.Type))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Type,
                    "CreateQuery does not accept Pending or unknown query types.");
            }

            if (descriptor.Type != ERHIQueryType.Statistics)
            {
                return;
            }

            if (descriptor.CounterMask == ERHIPipelineStatisticCounter.None)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.CounterMask,
                    "Statistics queries require a non-empty CounterMask.");
            }

            if (!Enum.IsDefined(descriptor.Domain))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Domain,
                    "Statistics queries require a concrete pipeline-statistics domain.");
            }

            ERHIPipelineStatisticCounter legalMask =
                LegalCountersForDomain(descriptor.Domain);
            if ((descriptor.CounterMask & ~legalMask) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.CounterMask,
                    $"Statistics domain {descriptor.Domain} does not accept the requested counter mask.");
            }

            RHICapability capability =
                Capabilities.Synchronization.PipelineStatisticsQueries;
            capability.Require("Synchronization.PipelineStatisticsQueries");
            if (!capability.Limits.TryGetValue(
                    ERHICapabilityLimitKind.SupportedPipelineStatisticsDomainMask,
                    out ulong domainMask) ||
                (domainMask & (1UL << (byte)descriptor.Domain)) == 0)
            {
                throw new NotSupportedException(
                    $"Pipeline statistics domain {descriptor.Domain} is unavailable on this device.");
            }

            ERHICapabilityLimitKind counterLimitKind =
                descriptor.Domain switch
                {
                    ERHIPipelineStatisticsDomain.Raster =>
                        ERHICapabilityLimitKind.RasterPipelineStatisticCounterMask,
                    ERHIPipelineStatisticsDomain.Compute =>
                        ERHICapabilityLimitKind.ComputePipelineStatisticCounterMask,
                    ERHIPipelineStatisticsDomain.RayTracing =>
                        ERHICapabilityLimitKind.RayTracingPipelineStatisticCounterMask,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        descriptor.Domain,
                        "Statistics queries require a concrete pipeline-statistics domain."),
                };
            if (!capability.Limits.TryGetValue(
                    counterLimitKind,
                    out ulong supportedCounters) ||
                ((ulong)descriptor.CounterMask & ~supportedCounters) != 0)
            {
                throw new NotSupportedException(
                    $"Pipeline statistics domain {descriptor.Domain} does not support the requested CounterMask.");
            }
        }

        internal static ERHIPipelineStatisticCounter LegalCountersForDomain(
            ERHIPipelineStatisticsDomain domain)
        {
            const ERHIPipelineStatisticCounter rasterCounters =
                ERHIPipelineStatisticCounter.InputAssemblyVertices |
                ERHIPipelineStatisticCounter.InputAssemblyPrimitives |
                ERHIPipelineStatisticCounter.VertexShaderInvocations |
                ERHIPipelineStatisticCounter.GeometryShaderInvocations |
                ERHIPipelineStatisticCounter.GeometryShaderPrimitives |
                ERHIPipelineStatisticCounter.ClipperInvocations |
                ERHIPipelineStatisticCounter.ClipperPrimitives |
                ERHIPipelineStatisticCounter.PixelShaderInvocations |
                ERHIPipelineStatisticCounter.HullShaderInvocations |
                ERHIPipelineStatisticCounter.DomainShaderInvocations |
                ERHIPipelineStatisticCounter.MeshShaderInvocations |
                ERHIPipelineStatisticCounter.TaskShaderInvocations |
                ERHIPipelineStatisticCounter.MeshShaderPrimitives;
            const ERHIPipelineStatisticCounter computeCounters =
                ERHIPipelineStatisticCounter.ComputeShaderInvocations;
            return domain switch
            {
                ERHIPipelineStatisticsDomain.Raster => rasterCounters,
                ERHIPipelineStatisticsDomain.Compute => computeCounters,
                ERHIPipelineStatisticsDomain.RayTracing =>
                    ERHIPipelineStatisticCounter.None,
                _ => ERHIPipelineStatisticCounter.None,
            };
        }

        internal static RHICapabilityLimits CreatePipelineStatisticsLimits(
            ERHIPipelineStatisticCounter rasterCounters,
            ERHIPipelineStatisticCounter computeCounters,
            ERHIPipelineStatisticCounter rayTracingCounters)
        {
            ulong domainMask = 0;
            if (rasterCounters != ERHIPipelineStatisticCounter.None)
            {
                domainMask |= 1UL << (byte)ERHIPipelineStatisticsDomain.Raster;
            }

            if (computeCounters != ERHIPipelineStatisticCounter.None)
            {
                domainMask |= 1UL << (byte)ERHIPipelineStatisticsDomain.Compute;
            }

            if (rayTracingCounters != ERHIPipelineStatisticCounter.None)
            {
                domainMask |= 1UL << (byte)ERHIPipelineStatisticsDomain.RayTracing;
            }

            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.SupportedPipelineStatisticsDomainMask,
                    domainMask),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.RasterPipelineStatisticCounterMask,
                    (ulong)rasterCounters),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.ComputePipelineStatisticCounterMask,
                    (ulong)computeCounters),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.RayTracingPipelineStatisticCounterMask,
                    (ulong)rayTracingCounters));
        }

        public abstract RHIRasterAttachmentShaderAbi
            QueryRasterAttachmentShaderAbi(
                in RHIRasterAttachmentShaderAbiDescriptor descriptor);
        public virtual RHITexture CreatePlacedTexture(
            RHIHeap heap,
            ulong heapOffset,
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose placed textures.");
        }
        public virtual RHISparseTextureMemoryRequirements GetSparseTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose sparse texture requirements.");
        }
        public virtual RHITexture CreateSparseTexture(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose sparse textures.");
        }
        public virtual RHIMemoryBudget QueryMemoryBudget(ERHIStorageMode storageMode)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose an exact native memory budget.");
        }
        public virtual void RequestResidency(in RHIResidencyRequestDescriptor descriptor)
        {
            ThrowIfDisposed();
            throw new NotSupportedException($"{BackendType} does not expose residency requests with explicit completion.");
        }
        public abstract RHISampler CreateSampler(in RHISamplerDescriptor descriptor);
        public abstract RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
        public abstract RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor);
        public abstract RHIBindingTableLayout CreateBindingTableLayout(in RHIBindingTableLayoutDescriptor descriptor);
        public abstract RHIBindingTable CreateBindingTable(in RHIBindingTableDescriptor descriptor);
        public abstract RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor);
        public abstract RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor);
        public abstract RHIFunctionLibrary CreateFunctionLibrary(in RHIFunctionLibraryDescriptor descriptor);
        public abstract RHIFunctionTable CreateFunctionTable();
        public abstract RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor);
        public abstract RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor);
        public abstract RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor);
        public abstract RHIPipelineCache CreatePipelineCache();
        public abstract RHIIndirectCommandLayout CreateIndirectCommandLayout(in RHIIndirectCommandLayoutDescriptor descriptor);
        public abstract RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor);
        public abstract RHIMLBindingTable CreateMLBindingTable(in RHIMLBindingTableDescriptor descriptor);
        public abstract RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor);
        public abstract RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor);

        public virtual bool TryToggleGpuCapture(string savedPath, string reason)
        {
            return false;
        }

        protected RHICommandQueue RequireCommandQueue(
            ERHIPipelineType queue,
            int queueIndex,
            string operation)
        {
            if (queue == ERHIPipelineType.Pending ||
                !Enum.IsDefined(queue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queue),
                    queue,
                    $"{operation} requires a concrete queue type.");
            }
            if (queueIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueIndex),
                    queueIndex,
                    $"{operation} queue index must not be negative.");
            }

            RHICommandQueue? commandQueue = GetCommandQueue(queue, queueIndex);
            if (commandQueue == null)
            {
                throw new InvalidOperationException(
                    $"{operation} target queue {queue}[{queueIndex}] does not exist.");
            }

            return commandQueue;
        }
    }
    #region Capabilities
    public enum ERHICapabilityTier : byte
    {
        Unavailable,
        Tier1,
        Tier2,
        Tier3,
        Tier4
    }

    public enum ERHICapabilityStrategy : byte
    {
        Unavailable,
        CoreApi,
        NativeExtension,
        NativeSpecialized,
        NativeLibrary
    }

    public enum ERHICapabilityProbeKind : byte
    {
        BackendContract,
        ApiVersion,
        NativeFeatureQuery,
        NativeExtensionQuery,
        RuntimeObjectProbe,
        RuntimeLibraryProbe
    }

    public enum ERHICapabilityLimitKind : byte
    {
        UniformBufferAlignment,
        UploadBufferAlignment,
        UploadTextureAlignment,
        UploadTextureRowAlignment,
        MaximumSampleCount,
        MaximumBoundTextures,
        MinimumWavefrontSize,
        MaximumWavefrontSize,
        MaximumComputeThreads,
        MaximumGroupSharedMemoryBytes,
        MaximumBindingTables,
        MaximumSamplerDescriptorsPerTable,
        MaximumSampledImageDescriptorsPerTable,
        MaximumStorageImageDescriptorsPerTable,
        MaximumUniformBufferDescriptorsPerTable,
        MaximumStorageBufferDescriptorsPerTable,
        MaximumVertexInputBindings,
        MaximumColorAttachments,
        MaximumTexture2DSize,
        MaximumTextureCubeSize,
        MemoryHeapCount,
        MaximumRootConstantBytes,
        RootConstantAlignmentBytes,
        SupportedRootConstantStageMask,
        ShadingRateAttachmentTileWidthMin,
        ShadingRateAttachmentTileHeightMin,
        ShadingRateAttachmentTileWidthMax,
        ShadingRateAttachmentTileHeightMax,
        // Bit mask of supported ERHIShadingRate values: 1UL << (byte)rate. Pending is never set.
        SupportedShadingRateMask,
        // Bit mask of supported ERHIShadingRateCombiner values: 1UL << (byte)combiner. Pending is never set.
        SupportedShadingRateCombinerMask,
        // Bit mask of supported ERHIFormatSupportOperation values. Pending is never set.
        SupportedFormatOperationMask,
        CalibratedTimestampMaxDeviation,
        GpuTimestampFrequency,
        SparseStandardTileWidth,
        SparseStandardTileHeight,
        SparseStandardTileDepth,
        SparseStandardTileSizeBytes,
        MeshMaxOutputVertices,
        MeshMaxOutputPrimitives,
        MeshMaxPayloadBytes,
        MeshMaxWorkGroupSizeX,
        MeshMaxWorkGroupSizeY,
        MeshMaxWorkGroupSizeZ,
        MeshMaxPerPrimitiveAttributes,
        /// <summary>
        /// Bit mask of <see cref="ERHIStageMask"/> values for wave / subgroup
        /// or cooperative-matrix stage scope. Pending is never set.
        /// </summary>
        WaveStageMask,
        CooperativeMatrixConfigCount,
        /// <summary>
        /// Bit mask of supported <see cref="ERHISamplerFeedbackMode"/> values:
        /// 1UL &lt;&lt; (byte)mode. Pending is never set.
        /// </summary>
        SupportedSamplerFeedbackModeMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHISamplerFeedbackOperation"/>
        /// values. Pending / None are never set as exclusive encodings.
        /// </summary>
        SupportedSamplerFeedbackOperationMask,
        MaximumStorageRequestBytes,
        MaximumStorageConcurrentRequests,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIStorageCompressionFormat"/>
        /// values that a storage queue can enqueue. <see cref="ERHIStorageCompressionFormat.None"/>
        /// is never set.
        /// </summary>
        SupportedStorageCompressionFormatMask,
        FunctionLibraryReusablePipelineClassMask,
        FunctionLibraryMaxEntryCount,
        FunctionLibrarySupportedPayloadKindMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIResolveMode"/> values:
        /// 1UL &lt;&lt; (byte)mode. Pending / None are never set as exclusive encodings.
        /// </summary>
        SupportedResolveModeMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIPipelineStatisticsDomain"/>
        /// values: 1UL &lt;&lt; (byte)domain.
        /// </summary>
        SupportedPipelineStatisticsDomainMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIPipelineStatisticCounter"/>
        /// values for the Raster domain.
        /// </summary>
        RasterPipelineStatisticCounterMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIPipelineStatisticCounter"/>
        /// values for the Compute domain.
        /// </summary>
        ComputePipelineStatisticCounterMask,
        /// <summary>
        /// Bit mask of supported <see cref="ERHIPipelineStatisticCounter"/>
        /// values for the RayTracing domain.
        /// </summary>
        RayTracingPipelineStatisticCounterMask
    }

    /// <summary>
    /// One native cooperative-matrix configuration. Values come from a device
    /// query; callers must not invent configurations.
    /// </summary>
    public readonly struct RHICooperativeMatrixConfig
    {
        public uint M { get; }
        public uint N { get; }
        public uint K { get; }
        public ERHICooperativeMatrixElementType AType { get; }
        public ERHICooperativeMatrixElementType BType { get; }
        public ERHICooperativeMatrixElementType CType { get; }
        public ERHICooperativeMatrixElementType ResultType { get; }
        public ERHICooperativeMatrixScope Scope { get; }
        public bool SaturatingAccumulation { get; }

        public RHICooperativeMatrixConfig(
            uint m,
            uint n,
            uint k,
            ERHICooperativeMatrixElementType aType,
            ERHICooperativeMatrixElementType bType,
            ERHICooperativeMatrixElementType cType,
            ERHICooperativeMatrixElementType resultType,
            ERHICooperativeMatrixScope scope,
            bool saturatingAccumulation)
        {
            if (!Enum.IsDefined(aType))
            {
                throw new ArgumentOutOfRangeException(nameof(aType), aType, "Unknown cooperative-matrix element type.");
            }
            if (!Enum.IsDefined(bType))
            {
                throw new ArgumentOutOfRangeException(nameof(bType), bType, "Unknown cooperative-matrix element type.");
            }
            if (!Enum.IsDefined(cType))
            {
                throw new ArgumentOutOfRangeException(nameof(cType), cType, "Unknown cooperative-matrix element type.");
            }
            if (!Enum.IsDefined(resultType))
            {
                throw new ArgumentOutOfRangeException(nameof(resultType), resultType, "Unknown cooperative-matrix element type.");
            }
            if (!Enum.IsDefined(scope))
            {
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown cooperative-matrix scope.");
            }

            M = m;
            N = n;
            K = k;
            AType = aType;
            BType = bType;
            CType = cType;
            ResultType = resultType;
            Scope = scope;
            SaturatingAccumulation = saturatingAccumulation;
        }
    }

    public enum ERHIProjectionStrategy : byte
    {
        Native,
        FlipY,
        Pending
    }

    public readonly struct RHICapabilityProvenance
    {
        public ERHICapabilityProbeKind Kind { get; }
        public string Source { get; }

        public RHICapabilityProvenance(
            ERHICapabilityProbeKind kind,
            string source)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown capability probe kind.");
            }
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("Capability probe source must not be empty.", nameof(source));
            }

            Kind = kind;
            Source = source;
        }
    }

    public readonly struct RHICapabilityLimit
    {
        public ERHICapabilityLimitKind Kind { get; }
        public ulong Value { get; }

        public RHICapabilityLimit(
            ERHICapabilityLimitKind kind,
            ulong value)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown capability limit kind.");
            }

            Kind = kind;
            Value = value;
        }
    }

    public readonly struct RHICapabilityLimits
    {
        public static RHICapabilityLimits Empty => default;

        private readonly RHICapabilityLimit[]? m_Values;

        public ReadOnlySpan<RHICapabilityLimit> Values => m_Values;

        public RHICapabilityLimits(params RHICapabilityLimit[] values)
        {
            ArgumentNullException.ThrowIfNull(values);
            m_Values = values.Length == 0
                ? null
                : (RHICapabilityLimit[])values.Clone();
        }

        public bool TryGetValue(
            ERHICapabilityLimitKind kind,
            out ulong value)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown capability limit kind.");
            }

            ReadOnlySpan<RHICapabilityLimit> values = Values;
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index].Kind == kind)
                {
                    value = values[index].Value;
                    return true;
                }
            }

            value = 0;
            return false;
        }
    }

    public readonly struct RHICapability
    {
        public ERHICapabilityTier Tier { get; }
        public ERHICapabilityStrategy Strategy { get; }
        public RHICapabilityLimits Limits { get; }
        public string? UnavailableReason { get; }
        public RHICapabilityProvenance Provenance { get; }

        private RHICapability(
            ERHICapabilityTier tier,
            ERHICapabilityStrategy strategy,
            RHICapabilityLimits limits,
            string? unavailableReason,
            RHICapabilityProvenance provenance)
        {
            if (!Enum.IsDefined(tier))
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown capability tier.");
            }
            if (!Enum.IsDefined(strategy))
            {
                throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown capability strategy.");
            }

            if (tier == ERHICapabilityTier.Unavailable)
            {
                if (strategy != ERHICapabilityStrategy.Unavailable)
                {
                    throw new ArgumentException("Unavailable capabilities must use the unavailable strategy.", nameof(strategy));
                }
                if (string.IsNullOrWhiteSpace(unavailableReason))
                {
                    throw new ArgumentException("Unavailable capabilities must provide a reason.", nameof(unavailableReason));
                }
            }
            else
            {
                if (strategy == ERHICapabilityStrategy.Unavailable)
                {
                    throw new ArgumentException("Available capabilities must name a native strategy.", nameof(strategy));
                }
                if (unavailableReason != null)
                {
                    throw new ArgumentException("Available capabilities cannot provide an unavailable reason.", nameof(unavailableReason));
                }
            }

            Tier = tier;
            Strategy = strategy;
            Limits = limits;
            UnavailableReason = unavailableReason;
            Provenance = provenance;
        }

        public static RHICapability Available(
            ERHICapabilityTier tier,
            ERHICapabilityStrategy strategy,
            ERHICapabilityProbeKind probeKind,
            string probeSource,
            RHICapabilityLimits limits = default)
        {
            if (tier == ERHICapabilityTier.Unavailable)
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "An available capability must use a non-zero tier.");
            }

            return new RHICapability(
                tier,
                strategy,
                limits,
                unavailableReason: null,
                new RHICapabilityProvenance(probeKind, probeSource));
        }

        public static RHICapability Unavailable(
            string reason,
            ERHICapabilityProbeKind probeKind,
            string probeSource)
        {
            return new RHICapability(
                ERHICapabilityTier.Unavailable,
                ERHICapabilityStrategy.Unavailable,
                RHICapabilityLimits.Empty,
                reason,
                new RHICapabilityProvenance(probeKind, probeSource));
        }

        internal static RHICapability FromProbe(
            bool available,
            ERHICapabilityTier availableTier,
            ERHICapabilityStrategy availableStrategy,
            ERHICapabilityProbeKind probeKind,
            string probeSource,
            string unavailableReason,
            RHICapabilityLimits limits = default)
        {
            return available
                ? Available(availableTier, availableStrategy, probeKind, probeSource, limits)
                : Unavailable(unavailableReason, probeKind, probeSource);
        }

        public void Require(string capabilityName)
        {
            if (string.IsNullOrWhiteSpace(capabilityName))
            {
                throw new ArgumentException("Capability name must not be empty.", nameof(capabilityName));
            }
            if (Tier == ERHICapabilityTier.Unavailable)
            {
                throw new NotSupportedException($"{capabilityName} is unavailable: {UnavailableReason}");
            }
        }
    }

    public sealed class RHIRasterCapabilities
    {
        public ERHIProjectionStrategy ProjectionStrategy { get; }
        public ERHIMatrixMajorOrder MatrixMajorOrder { get; }
        public ERHIDepthValueRange DepthValueRange { get; }
        public ERHIMultiviewStrategy MultiviewStrategy { get; }
        public RHICapability PixelShaderStorageWrites { get; }
        public RHICapability FramebufferReadWrite { get; }
        public RHICapability AnisotropicSampling { get; }
        public RHICapability DepthAttachmentRead { get; }
        public RHICapability FramebufferLocalRead { get; }
        public RHICapability DrawIndirect { get; }
        public RHICapability MultiDrawIndirect { get; }
        public RHICapability VariableRateShadingPerDraw { get; }
        public RHICapability VariableRateShadingPerPrimitive { get; }
        public RHICapability VariableRateShadingAttachment { get; }
        public RHICapability VariableRateShadingCombiners { get; }
        public RHICapability HiddenSurfaceRemoval { get; }
        public RHICapability BarycentricCoordinates { get; }
        public RHICapability ProgrammableSamplePositions { get; }
        public RHICapability NativeRenderPass { get; }
        /// <summary>
        /// DX12-only optional sampler-feedback facet. Not
        /// <see cref="FramebufferLocalRead"/> and not an attachment
        /// feedback loop. Vulkan and Metal stay Unavailable.
        /// </summary>
        public RHICapability SamplerFeedback { get; }

        public RHIRasterCapabilities(
            ERHIProjectionStrategy projectionStrategy,
            ERHIMatrixMajorOrder matrixMajorOrder,
            ERHIDepthValueRange depthValueRange,
            ERHIMultiviewStrategy multiviewStrategy,
            RHICapability pixelShaderStorageWrites,
            RHICapability framebufferReadWrite,
            RHICapability anisotropicSampling,
            RHICapability depthAttachmentRead,
            RHICapability framebufferLocalRead,
            RHICapability drawIndirect,
            RHICapability multiDrawIndirect,
            RHICapability variableRateShadingPerDraw,
            RHICapability variableRateShadingPerPrimitive,
            RHICapability variableRateShadingAttachment,
            RHICapability variableRateShadingCombiners,
            RHICapability hiddenSurfaceRemoval,
            RHICapability barycentricCoordinates,
            RHICapability programmableSamplePositions,
            RHICapability nativeRenderPass,
            RHICapability samplerFeedback)
        {
            ProjectionStrategy = projectionStrategy;
            MatrixMajorOrder = matrixMajorOrder;
            DepthValueRange = depthValueRange;
            MultiviewStrategy = multiviewStrategy;
            PixelShaderStorageWrites = pixelShaderStorageWrites;
            FramebufferReadWrite = framebufferReadWrite;
            AnisotropicSampling = anisotropicSampling;
            DepthAttachmentRead = depthAttachmentRead;
            FramebufferLocalRead = framebufferLocalRead;
            DrawIndirect = drawIndirect;
            MultiDrawIndirect = multiDrawIndirect;
            VariableRateShadingPerDraw = variableRateShadingPerDraw;
            VariableRateShadingPerPrimitive = variableRateShadingPerPrimitive;
            VariableRateShadingAttachment = variableRateShadingAttachment;
            VariableRateShadingCombiners = variableRateShadingCombiners;
            HiddenSurfaceRemoval = hiddenSurfaceRemoval;
            BarycentricCoordinates = barycentricCoordinates;
            ProgrammableSamplePositions = programmableSamplePositions;
            NativeRenderPass = nativeRenderPass;
            SamplerFeedback = samplerFeedback;
        }
    }

    public sealed class RHIBindingCapabilities
    {
        public RHICapability RootConstants { get; }
        public RHICapability IndirectRootConstants { get; }
        public RHICapability AtomicUInt64 { get; }
        public RHICapability DescriptorIndexing { get; }
        public RHICapability PartiallyBoundDescriptors { get; }
        public RHICapability UpdateAfterBindDescriptors { get; }
        public RHICapability NullDescriptors { get; }

        public RHIBindingCapabilities(
            RHICapability rootConstants,
            RHICapability indirectRootConstants,
            RHICapability atomicUInt64,
            RHICapability descriptorIndexing,
            RHICapability partiallyBoundDescriptors,
            RHICapability updateAfterBindDescriptors,
            RHICapability nullDescriptors)
        {
            RootConstants = rootConstants;
            IndirectRootConstants = indirectRootConstants;
            AtomicUInt64 = atomicUInt64;
            DescriptorIndexing = descriptorIndexing;
            PartiallyBoundDescriptors = partiallyBoundDescriptors;
            UpdateAfterBindDescriptors = updateAfterBindDescriptors;
            NullDescriptors = nullDescriptors;
        }
    }

    public sealed class RHISynchronizationCapabilities
    {
        public RHICapability TimestampQueries { get; }
        public RHICapability OcclusionQueries { get; }
        public RHICapability PipelineStatisticsQueries { get; }
        public RHICapability EnhancedBarriers { get; }
        public RHICapability CalibratedTimestamps { get; }

        public RHISynchronizationCapabilities(
            RHICapability timestampQueries,
            RHICapability occlusionQueries,
            RHICapability pipelineStatisticsQueries,
            RHICapability enhancedBarriers,
            RHICapability calibratedTimestamps)
        {
            TimestampQueries = timestampQueries;
            OcclusionQueries = occlusionQueries;
            PipelineStatisticsQueries = pipelineStatisticsQueries;
            EnhancedBarriers = enhancedBarriers;
            CalibratedTimestamps = calibratedTimestamps;
        }
    }

    public sealed class RHIMemoryCapabilities
    {
        public RHICapability UnifiedMemory { get; }
        public RHICapability PlacedResources { get; }
        public RHICapability GpuVirtualAddress { get; }
        public RHICapability SparseBuffer { get; }
        public RHICapability SparseTexture2D { get; }
        public RHICapability SparseTexture3D { get; }
        public RHICapability SparseMsaa { get; }
        public RHICapability SparseMipTail { get; }
        public RHICapability SparseTileGeometry { get; }
        public RHICapability Residency { get; }
        public RHICapability BudgetQuery { get; }
        public RHICapability SparseAliasing { get; }

        public RHIMemoryCapabilities(
            RHICapability unifiedMemory,
            RHICapability placedResources,
            RHICapability gpuVirtualAddress,
            RHICapability sparseBuffer,
            RHICapability sparseTexture2D,
            RHICapability sparseTexture3D,
            RHICapability sparseMsaa,
            RHICapability sparseMipTail,
            RHICapability sparseTileGeometry,
            RHICapability residency,
            RHICapability budgetQuery,
            RHICapability sparseAliasing)
        {
            UnifiedMemory = unifiedMemory;
            PlacedResources = placedResources;
            GpuVirtualAddress = gpuVirtualAddress;
            SparseBuffer = sparseBuffer;
            SparseTexture2D = sparseTexture2D;
            SparseTexture3D = sparseTexture3D;
            SparseMsaa = sparseMsaa;
            SparseMipTail = sparseMipTail;
            SparseTileGeometry = sparseTileGeometry;
            Residency = residency;
            BudgetQuery = budgetQuery;
            SparseAliasing = sparseAliasing;
        }

        internal RHICapability RequireSparseTexture(
            in RHITextureDescriptor descriptor,
            string operation)
        {
            bool isMsaa =
                descriptor.SampleCount != ERHISampleCount.None ||
                descriptor.Dimension is
                    ERHITextureDimension.Texture2DMS or
                    ERHITextureDimension.Texture2DArrayMS;
            if (isMsaa)
            {
                SparseMsaa.Require(operation);
                return SparseMsaa;
            }

            if (descriptor.Dimension == ERHITextureDimension.Texture3D)
            {
                SparseTexture3D.Require(operation);
                return SparseTexture3D;
            }

            SparseTexture2D.Require(operation);
            return SparseTexture2D;
        }

        internal void RequireSparseBind(
            in RHISparseBindDescriptor descriptor,
            string operation)
        {
            ReadOnlySpan<RHISparseTextureTileBinding> tiles =
                descriptor.TileBindings.Span;
            for (int i = 0; i < tiles.Length; ++i)
            {
                RequireSparseTexture(
                    tiles[i].Texture.Descriptor,
                    operation);
            }

            ReadOnlySpan<RHISparseTextureMipTailBinding> tails =
                descriptor.MipTailBindings.Span;
            if (tails.Length > 0)
            {
                SparseMipTail.Require(operation);
            }
            for (int i = 0; i < tails.Length; ++i)
            {
                RequireSparseTexture(
                    tails[i].Texture.Descriptor,
                    operation);
            }
        }
    }

    public sealed class RHIStorageCapabilities
    {
        public RHICapability NativeGpuFileIo { get; }
        public RHICapability GpuDecompression { get; }
        public RHICapability RequestCancellation { get; }
        public RHICapability IoPriority { get; }

        public RHIStorageCapabilities(
            RHICapability nativeGpuFileIo,
            RHICapability gpuDecompression,
            RHICapability requestCancellation,
            RHICapability ioPriority)
        {
            NativeGpuFileIo = nativeGpuFileIo;
            GpuDecompression = gpuDecompression;
            RequestCancellation = requestCancellation;
            IoPriority = ioPriority;
        }
    }

    public sealed class RHIPipelineCacheCapabilities
    {
        public RHICapability NativeCache { get; }

        public RHIPipelineCacheCapabilities(RHICapability nativeCache)
        {
            NativeCache = nativeCache;
        }
    }

    public sealed class RHIFunctionLibraryCapabilities
    {
        public RHICapability NativeLibrary { get; }
        public RHICapability RasterComputeReuse { get; }
        public ulong ReusablePipelineClasses { get; }
        public ulong MaxEntryCount { get; }
        public ulong SupportedPayloadKinds { get; }

        public RHIFunctionLibraryCapabilities(
            RHICapability nativeLibrary,
            RHICapability rasterComputeReuse,
            ulong reusablePipelineClasses,
            ulong maxEntryCount,
            ulong supportedPayloadKinds)
        {
            NativeLibrary = nativeLibrary;
            RasterComputeReuse = rasterComputeReuse;
            ReusablePipelineClasses = reusablePipelineClasses;
            MaxEntryCount = maxEntryCount;
            SupportedPayloadKinds = supportedPayloadKinds;
        }

        internal static RHIFunctionLibraryCapabilities CreateNative(
            string nativeLibrarySource,
            ulong reusablePipelineClasses,
            ulong supportedPayloadKinds,
            string? rasterComputeUnavailableReason)
        {
            bool rasterCompute =
                (reusablePipelineClasses &
                    ((ulong)ERHIFunctionLibraryReusablePipelineClass.Raster |
                     (ulong)ERHIFunctionLibraryReusablePipelineClass.Compute)) != 0;
            RHICapability nativeLibrary = RHICapability.Available(
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeLibrary,
                ERHICapabilityProbeKind.BackendContract,
                nativeLibrarySource,
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.FunctionLibraryReusablePipelineClassMask,
                        reusablePipelineClasses),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.FunctionLibraryMaxEntryCount,
                        0),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.FunctionLibrarySupportedPayloadKindMask,
                        supportedPayloadKinds)));
            RHICapability rasterComputeReuse = rasterCompute
                ? RHICapability.Available(
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeLibrary,
                    ERHICapabilityProbeKind.BackendContract,
                    nativeLibrarySource)
                : RHICapability.Unavailable(
                    rasterComputeUnavailableReason
                        ?? "Raster / compute function-library views are unavailable.",
                    ERHICapabilityProbeKind.BackendContract,
                    nativeLibrarySource);
            return new RHIFunctionLibraryCapabilities(
                nativeLibrary,
                rasterComputeReuse,
                reusablePipelineClasses,
                maxEntryCount: 0,
                supportedPayloadKinds);
        }

        internal static RHIFunctionLibraryCapabilities CreateUnavailable(
            string reason,
            string probeSource)
        {
            RHICapability unavailable = RHICapability.Unavailable(
                reason,
                ERHICapabilityProbeKind.BackendContract,
                probeSource);
            return new RHIFunctionLibraryCapabilities(
                unavailable,
                unavailable,
                reusablePipelineClasses: 0,
                maxEntryCount: 0,
                supportedPayloadKinds: 0);
        }

        internal static ulong PayloadKindBit(ERHIShaderPayloadKind kind)
        {
            if (kind == ERHIShaderPayloadKind.Pending || !Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Pending is not a legal function-library payload kind.");
            }

            return 1UL << (byte)kind;
        }

        internal bool SupportsPayloadKind(ERHIShaderPayloadKind kind)
        {
            if (kind == ERHIShaderPayloadKind.Pending || !Enum.IsDefined(kind))
            {
                return false;
            }

            return (SupportedPayloadKinds & PayloadKindBit(kind)) != 0;
        }

        internal ERHIFunctionLibraryReusablePipelineClass ClassifyFunctionType(
            ERHIFunctionType type)
        {
            return type switch
            {
                ERHIFunctionType.Vertex or
                ERHIFunctionType.Fragment or
                ERHIFunctionType.Task or
                ERHIFunctionType.Mesh =>
                    ERHIFunctionLibraryReusablePipelineClass.Raster,
                ERHIFunctionType.Compute =>
                    ERHIFunctionLibraryReusablePipelineClass.Compute,
                ERHIFunctionType.RayTracing =>
                    ERHIFunctionLibraryReusablePipelineClass.Raytracing,
                _ => ERHIFunctionLibraryReusablePipelineClass.None,
            };
        }

        internal void RequireReusableClass(
            ERHIFunctionLibraryReusablePipelineClass pipelineClass,
            string operation)
        {
            if (pipelineClass == ERHIFunctionLibraryReusablePipelineClass.Raster ||
                pipelineClass == ERHIFunctionLibraryReusablePipelineClass.Compute)
            {
                RasterComputeReuse.Require(operation);
            }

            if ((ReusablePipelineClasses & (ulong)pipelineClass) == 0)
            {
                throw new NotSupportedException(
                    $"{operation} is unavailable because reusable pipeline class {pipelineClass} is not set.");
            }
        }
    }

    public sealed class RHIPresentationCapabilities
    {
        public RHICapability SwapChain { get; }
        public RHICapability Hdr { get; }

        public RHIPresentationCapabilities(
            RHICapability swapChain,
            RHICapability hdr)
        {
            SwapChain = swapChain;
            Hdr = hdr;
        }
    }

    public sealed class RHIRayTracingCapabilities
    {
        public RHICapability Pipeline { get; }
        public RHICapability Inline { get; }

        public RHIRayTracingCapabilities(
            RHICapability pipeline,
            RHICapability inline)
        {
            Pipeline = pipeline;
            Inline = inline;
        }
    }

    public sealed class RHIMeshCapabilities
    {
        public RHICapability MeshShader { get; }
        public RHICapability TaskShader { get; }

        public RHIMeshCapabilities(RHICapability meshShader, RHICapability taskShader)
        {
            MeshShader = meshShader;
            TaskShader = taskShader;
        }
    }

    public sealed class RHIMachineLearningCapabilities
    {
        public RHICapability Execution { get; }

        public RHIMachineLearningCapabilities(RHICapability execution)
        {
            Execution = execution;
        }
    }

    public sealed class RHIWorkGraphCapabilities
    {
        public RHICapability Execution { get; }
        public RHICapability BroadcastNodes { get; }
        public RHICapability ThreadNodes { get; }
        public RHICapability Recursion { get; }
        public RHICapability MeshNodes { get; }
        public RHICapability GpuInput { get; }
        public RHICapability BackingMemory { get; }
        public RHICapability EntryRecords { get; }

        public RHIWorkGraphCapabilities(
            RHICapability execution,
            RHICapability broadcastNodes,
            RHICapability threadNodes,
            RHICapability recursion,
            RHICapability meshNodes,
            RHICapability gpuInput,
            RHICapability backingMemory,
            RHICapability entryRecords)
        {
            Execution = execution;
            BroadcastNodes = broadcastNodes;
            ThreadNodes = threadNodes;
            Recursion = recursion;
            MeshNodes = meshNodes;
            GpuInput = gpuInput;
            BackingMemory = backingMemory;
            EntryRecords = entryRecords;
        }

        internal static RHIWorkGraphCapabilities CreateUnavailable(
            string reason,
            string probeSource)
        {
            RHICapability unavailable = RHICapability.Unavailable(
                reason,
                ERHICapabilityProbeKind.BackendContract,
                probeSource);
            return new RHIWorkGraphCapabilities(
                unavailable,
                unavailable,
                unavailable,
                unavailable,
                unavailable,
                unavailable,
                unavailable,
                unavailable);
        }
    }

    public sealed class RHIIndirectTokenCapabilities
    {
        public RHICapability VertexBuffer { get; }
        public RHICapability IndexBuffer { get; }
        public RHICapability Draw { get; }
        public RHICapability DrawIndexed { get; }
        public RHICapability Dispatch { get; }
        public RHICapability DispatchMesh { get; }

        public RHIIndirectTokenCapabilities(
            RHICapability vertexBuffer,
            RHICapability indexBuffer,
            RHICapability draw,
            RHICapability drawIndexed,
            RHICapability dispatch,
            RHICapability dispatchMesh)
        {
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            Draw = draw;
            DrawIndexed = drawIndexed;
            Dispatch = dispatch;
            DispatchMesh = dispatchMesh;
        }

        internal static RHIIndirectTokenCapabilities FromExecution(
            RHICapability execution)
        {
            return new RHIIndirectTokenCapabilities(
                execution,
                execution,
                execution,
                execution,
                execution,
                execution);
        }
    }

    public sealed class RHIIndirectCommandBufferCapabilities
    {
        public RHICapability Execution { get; }
        public RHIIndirectTokenCapabilities Tokens { get; }

        public RHIIndirectCommandBufferCapabilities(RHICapability execution, RHIIndirectTokenCapabilities tokens)
        {
            Execution = execution;
            Tokens = tokens;
        }
    }

    public sealed class RHIComputeCapabilities
    {
        public ERHIWaveOperationStrategy WaveOperationStrategy { get; }
        public RHICapability WaveOperations { get; }
        public RHICapability VariableSubgroupSize { get; }
        public RHICapability CooperativeMatrix { get; }

        public RHIComputeCapabilities(
            ERHIWaveOperationStrategy waveOperationStrategy,
            RHICapability waveOperations,
            RHICapability variableSubgroupSize,
            RHICapability cooperativeMatrix)
        {
            WaveOperationStrategy = waveOperationStrategy;
            WaveOperations = waveOperations;
            VariableSubgroupSize = variableSubgroupSize;
            CooperativeMatrix = cooperativeMatrix;
        }
    }

    public sealed class RHIDeviceCapabilities
    {
        public RHIRasterCapabilities Raster { get; }
        public RHIBindingCapabilities Binding { get; }
        public RHISynchronizationCapabilities Synchronization { get; }
        public RHIMemoryCapabilities Memory { get; }
        public RHIStorageCapabilities Storage { get; }
        public RHIPipelineCacheCapabilities PipelineCache { get; }
        public RHIPresentationCapabilities Presentation { get; }
        public RHIRayTracingCapabilities RayTracing { get; }
        public RHIMeshCapabilities Mesh { get; }
        public RHIMachineLearningCapabilities MachineLearning { get; }
        public RHIWorkGraphCapabilities WorkGraph { get; }
        public RHIIndirectCommandBufferCapabilities IndirectCommandBuffer { get; }
        public RHIComputeCapabilities Compute { get; }
        public RHIFunctionLibraryCapabilities FunctionLibrary { get; }

        internal static RHIDeviceCapabilities CreateUnprobed(string probeSource)
        {
            RHICapability unavailable = RHICapability.Unavailable(
                "The RHIDevice subclass has not published a native capability probe.",
                ERHICapabilityProbeKind.BackendContract,
                probeSource);
            return new RHIDeviceCapabilities(
                new RHIRasterCapabilities(
                    projectionStrategy: ERHIProjectionStrategy.Pending,
                    matrixMajorOrder: ERHIMatrixMajorOrder.Pending,
                    depthValueRange: ERHIDepthValueRange.Pending,
                    multiviewStrategy: ERHIMultiviewStrategy.Pending,
                    pixelShaderStorageWrites: unavailable,
                    framebufferReadWrite: unavailable,
                    anisotropicSampling: unavailable,
                    depthAttachmentRead: unavailable,
                    framebufferLocalRead: unavailable,
                    drawIndirect: unavailable,
                    multiDrawIndirect: unavailable,
                    variableRateShadingPerDraw: unavailable,
                    variableRateShadingPerPrimitive: unavailable,
                    variableRateShadingAttachment: unavailable,
                    variableRateShadingCombiners: unavailable,
                    hiddenSurfaceRemoval: unavailable,
                    barycentricCoordinates: unavailable,
                    programmableSamplePositions: unavailable,
                    nativeRenderPass: unavailable,
                    samplerFeedback: unavailable),
                new RHIBindingCapabilities(
                    rootConstants: unavailable,
                    indirectRootConstants: unavailable,
                    atomicUInt64: unavailable,
                    descriptorIndexing: unavailable,
                    partiallyBoundDescriptors: unavailable,
                    updateAfterBindDescriptors: unavailable,
                    nullDescriptors: unavailable),
                new RHISynchronizationCapabilities(
                    timestampQueries: unavailable,
                    occlusionQueries: unavailable,
                    pipelineStatisticsQueries: unavailable,
                    enhancedBarriers: unavailable,
                    calibratedTimestamps: unavailable),
                new RHIMemoryCapabilities(
                    unifiedMemory: unavailable,
                    placedResources: unavailable,
                    gpuVirtualAddress: unavailable,
                    sparseBuffer: unavailable,
                    sparseTexture2D: unavailable,
                    sparseTexture3D: unavailable,
                    sparseMsaa: unavailable,
                    sparseMipTail: unavailable,
                    sparseTileGeometry: unavailable,
                    residency: unavailable,
                    budgetQuery: unavailable,
                    sparseAliasing: unavailable),
                new RHIStorageCapabilities(unavailable, unavailable, unavailable, unavailable),
                new RHIPipelineCacheCapabilities(unavailable),
                new RHIPresentationCapabilities(
                    swapChain: unavailable,
                    hdr: unavailable),
                new RHIRayTracingCapabilities(
                    pipeline: unavailable,
                    inline: unavailable),
                new RHIMeshCapabilities(unavailable, unavailable),
                new RHIMachineLearningCapabilities(unavailable),
                new RHIWorkGraphCapabilities(
                    execution: unavailable,
                    broadcastNodes: unavailable,
                    threadNodes: unavailable,
                    recursion: unavailable,
                    meshNodes: unavailable,
                    gpuInput: unavailable,
                    backingMemory: unavailable,
                    entryRecords: unavailable),
                new RHIIndirectCommandBufferCapabilities(
                    unavailable,
                    new RHIIndirectTokenCapabilities(
                        unavailable,
                        unavailable,
                        unavailable,
                        unavailable,
                        unavailable,
                        unavailable)),
                new RHIComputeCapabilities(
                    waveOperationStrategy: ERHIWaveOperationStrategy.Pending,
                    waveOperations: unavailable,
                    variableSubgroupSize: unavailable,
                    cooperativeMatrix: unavailable),
                RHIFunctionLibraryCapabilities.CreateUnavailable(
                    "The RHIDevice subclass has not published a native capability probe.",
                    probeSource));
        }

        public RHIDeviceCapabilities(
            RHIRasterCapabilities raster,
            RHIBindingCapabilities binding,
            RHISynchronizationCapabilities synchronization,
            RHIMemoryCapabilities memory,
            RHIStorageCapabilities storage,
            RHIPipelineCacheCapabilities pipelineCache,
            RHIPresentationCapabilities presentation,
            RHIRayTracingCapabilities rayTracing,
            RHIMeshCapabilities mesh,
            RHIMachineLearningCapabilities machineLearning,
            RHIWorkGraphCapabilities workGraph,
            RHIIndirectCommandBufferCapabilities indirectCommandBuffer,
            RHIComputeCapabilities compute,
            RHIFunctionLibraryCapabilities functionLibrary)
        {
            Raster = raster ?? throw new ArgumentNullException(nameof(raster));
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            Synchronization = synchronization ?? throw new ArgumentNullException(nameof(synchronization));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            PipelineCache = pipelineCache ?? throw new ArgumentNullException(nameof(pipelineCache));
            Presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            RayTracing = rayTracing ?? throw new ArgumentNullException(nameof(rayTracing));
            Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
            MachineLearning = machineLearning ?? throw new ArgumentNullException(nameof(machineLearning));
            WorkGraph = workGraph ?? throw new ArgumentNullException(nameof(workGraph));
            IndirectCommandBuffer = indirectCommandBuffer ?? throw new ArgumentNullException(nameof(indirectCommandBuffer));
            Compute = compute ?? throw new ArgumentNullException(nameof(compute));
            FunctionLibrary = functionLibrary ?? throw new ArgumentNullException(nameof(functionLibrary));
        }
    }
    #endregion
}
