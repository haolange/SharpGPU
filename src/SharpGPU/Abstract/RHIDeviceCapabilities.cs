using System;

namespace SharpGPU
{
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

    public enum ERHIPresentationMaintenanceStrategy : byte
    {
        Unavailable,
        PresentFence,
        QueueIdle
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
        MaximumArgumentTables,
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
        public RHICapability RasterOrderedAccess { get; }
        public RHICapability AnisotropicSampling { get; }
        public RHICapability DepthAttachmentRead { get; }
        public RHICapability FramebufferLocalRead { get; }
        public RHICapability SampledFeedback { get; }
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
            RHICapability rasterOrderedAccess,
            RHICapability anisotropicSampling,
            RHICapability depthAttachmentRead,
            RHICapability framebufferLocalRead,
            RHICapability sampledFeedback,
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
            RasterOrderedAccess = rasterOrderedAccess;
            AnisotropicSampling = anisotropicSampling;
            DepthAttachmentRead = depthAttachmentRead;
            FramebufferLocalRead = framebufferLocalRead;
            SampledFeedback = sampledFeedback;
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
        public RHICapability Residency { get; }
        public RHICapability BudgetQuery { get; }

        public RHIMemoryCapabilities(
            RHICapability unifiedMemory,
            RHICapability placedResources,
            RHICapability sparseBinding,
            RHICapability residency,
            RHICapability budgetQuery)
        {
            UnifiedMemory = unifiedMemory;
            PlacedResources = placedResources;
            SparseBinding = sparseBinding;
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
        public RHICapability AcquireSignal { get; }
        public RHICapability PresentWait { get; }
        public RHICapability PresentCompletion { get; }
        public RHICapability Maintenance { get; }
        public ERHIPresentationMaintenanceStrategy MaintenanceStrategy { get; }
        public RHICapability Hdr { get; }

        public RHIPresentationCapabilities(
            RHICapability swapChain,
            RHICapability acquireSignal,
            RHICapability presentWait,
            RHICapability presentCompletion,
            RHICapability maintenance,
            ERHIPresentationMaintenanceStrategy maintenanceStrategy,
            RHICapability hdr)
        {
            if (!Enum.IsDefined(maintenanceStrategy))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maintenanceStrategy),
                    maintenanceStrategy,
                    "Unknown presentation maintenance strategy.");
            }
            bool maintenanceAvailable =
                maintenance.Tier != ERHICapabilityTier.Unavailable;
            if (maintenanceAvailable ==
                (maintenanceStrategy ==
                    ERHIPresentationMaintenanceStrategy.Unavailable))
            {
                throw new ArgumentException(
                    maintenanceAvailable
                        ? "Available presentation maintenance must name its exact drain strategy."
                        : "Unavailable presentation maintenance cannot advertise a drain strategy.",
                    nameof(maintenanceStrategy));
            }
            if (maintenanceStrategy ==
                    ERHIPresentationMaintenanceStrategy.PresentFence &&
                presentCompletion.Tier ==
                    ERHICapabilityTier.Unavailable)
            {
                throw new ArgumentException(
                    "PresentFence maintenance requires present-completion fence support.",
                    nameof(presentCompletion));
            }

            SwapChain = swapChain;
            AcquireSignal = acquireSignal;
            PresentWait = presentWait;
            PresentCompletion = presentCompletion;
            Maintenance = maintenance;
            MaintenanceStrategy = maintenanceStrategy;
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
                    rasterOrderedAccess: unavailable,
                    anisotropicSampling: unavailable,
                    depthAttachmentRead: unavailable,
                    framebufferLocalRead: unavailable,
                    drawIndirect: unavailable,
                    sampledFeedback: unavailable,
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
                    residency: unavailable,
                    budgetQuery: unavailable),
                new RHIStorageCapabilities(unavailable),
                new RHIPipelineCacheCapabilities(unavailable),
                new RHIPresentationCapabilities(
                    swapChain: unavailable,
                    acquireSignal: unavailable,
                    presentWait: unavailable,
                    presentCompletion: unavailable,
                    maintenance: unavailable,
                    maintenanceStrategy:
                        ERHIPresentationMaintenanceStrategy.Unavailable,
                    hdr: unavailable),
                new RHIRayTracingCapabilities(
                    pipeline: unavailable,
                    inline: unavailable),
                new RHIMeshCapabilities(unavailable),
                new RHIMachineLearningCapabilities(unavailable),
                new RHIWorkGraphCapabilities(unavailable),
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
            Compute = compute ?? throw new ArgumentNullException(nameof(compute));
        }
    }
}
