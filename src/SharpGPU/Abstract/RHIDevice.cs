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
        /// Queries whether this device can express an exact attachment,
        /// sampling, and blend combination without semantic fallback.
        /// </summary>
        public abstract RHICapability QueryRasterAttachmentSupport(
            in RHIRasterAttachmentSupportQuery query);
        /// <summary>
        /// Queries the versioned target shader ABI for a logical attachment
        /// interface. SharpShader is not required to consume this contract.
        /// </summary>
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
    }
    #region Capabilities
    public enum ERHICapabilityTier : byte
    {
        Unavailable,
        Tier1,
        Tier2,
        Tier3
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
        SupportedRootConstantStageMask
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
        public RHICapability VariableRateShading { get; }
        public RHICapability HiddenSurfaceRemoval { get; }
        public RHICapability BarycentricCoordinates { get; }
        public RHICapability ProgrammableSamplePositions { get; }
        public RHICapability NativeRenderPass { get; }

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
            RHICapability variableRateShading,
            RHICapability hiddenSurfaceRemoval,
            RHICapability barycentricCoordinates,
            RHICapability programmableSamplePositions,
            RHICapability nativeRenderPass)
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
            VariableRateShading = variableRateShading;
            HiddenSurfaceRemoval = hiddenSurfaceRemoval;
            BarycentricCoordinates = barycentricCoordinates;
            ProgrammableSamplePositions = programmableSamplePositions;
            NativeRenderPass = nativeRenderPass;
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

        public RHISynchronizationCapabilities(
            RHICapability timestampQueries,
            RHICapability occlusionQueries,
            RHICapability pipelineStatisticsQueries,
            RHICapability enhancedBarriers)
        {
            TimestampQueries = timestampQueries;
            OcclusionQueries = occlusionQueries;
            PipelineStatisticsQueries = pipelineStatisticsQueries;
            EnhancedBarriers = enhancedBarriers;
        }
    }

    public sealed class RHIMemoryCapabilities
    {
        public RHICapability UnifiedMemory { get; }
        public RHICapability PlacedResources { get; }
        public RHICapability SparseBinding { get; }
        public RHICapability GpuVirtualAddress { get; }
        public RHICapability SparseBufferBinding { get; }
        public RHICapability Residency { get; }
        public RHICapability BudgetQuery { get; }

        public RHIMemoryCapabilities(
            RHICapability unifiedMemory,
            RHICapability placedResources,
            RHICapability sparseBinding,
            RHICapability gpuVirtualAddress,
            RHICapability sparseBufferBinding,
            RHICapability residency,
            RHICapability budgetQuery)
        {
            UnifiedMemory = unifiedMemory;
            PlacedResources = placedResources;
            SparseBinding = sparseBinding;
            GpuVirtualAddress = gpuVirtualAddress;
            SparseBufferBinding = sparseBufferBinding;
            Residency = residency;
            BudgetQuery = budgetQuery;
        }
    }

    public sealed class RHIStorageCapabilities
    {
        public RHICapability NativeGpuFileIo { get; }

        public RHIStorageCapabilities(RHICapability nativeGpuFileIo)
        {
            NativeGpuFileIo = nativeGpuFileIo;
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
        public RHICapability Shader { get; }

        public RHIMeshCapabilities(RHICapability shader)
        {
            Shader = shader;
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

        public RHIWorkGraphCapabilities(RHICapability execution)
        {
            Execution = execution;
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

        public RHIComputeCapabilities(
            ERHIWaveOperationStrategy waveOperationStrategy,
            RHICapability waveOperations)
        {
            WaveOperationStrategy = waveOperationStrategy;
            WaveOperations = waveOperations;
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
                    variableRateShading: unavailable,
                    hiddenSurfaceRemoval: unavailable,
                    barycentricCoordinates: unavailable,
                    programmableSamplePositions: unavailable,
                    nativeRenderPass: unavailable),
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
                    enhancedBarriers: unavailable),
                new RHIMemoryCapabilities(
                    unifiedMemory: unavailable,
                    placedResources: unavailable,
                    sparseBinding: unavailable,
                    gpuVirtualAddress: unavailable,
                    sparseBufferBinding: unavailable,
                    residency: unavailable,
                    budgetQuery: unavailable),
                new RHIStorageCapabilities(unavailable),
                new RHIPipelineCacheCapabilities(unavailable),
                new RHIPresentationCapabilities(
                    swapChain: unavailable,
                    hdr: unavailable),
                new RHIRayTracingCapabilities(
                    pipeline: unavailable,
                    inline: unavailable),
                new RHIMeshCapabilities(unavailable),
                new RHIMachineLearningCapabilities(unavailable),
                new RHIWorkGraphCapabilities(unavailable),
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
                    waveOperations: unavailable));
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
            RHIComputeCapabilities compute)
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
        }
    }
    #endregion
}
