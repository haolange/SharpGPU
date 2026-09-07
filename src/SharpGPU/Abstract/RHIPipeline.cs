using System;
using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using SharpGPU.Core;
using SharpMath;

namespace SharpGPU
{
    public struct RHIPipelineLayoutDescriptor
    {
        public bool bLocalSignature;
        public bool bUseVertexLayout;
        public uint PushConstantSize;
        public RHIBindingTableLayout[] BindingTableLayouts;
        //public RHIPipelineConstantLayout[] PipelineConstantLayouts;
        public Memory<RHIStaticSamplerDescriptor>? StaticSamplers;
    };

    public abstract class RHIPipelineLayout : Disposal
    {
        internal ReadOnlyMemory<byte> PipelineCacheIdentity => m_PipelineCacheIdentity;

        private byte[] m_PipelineCacheIdentity = Array.Empty<byte>();

        protected void InitializePipelineCacheIdentity(
            in RHIPipelineLayoutDescriptor descriptor)
        {
            if (m_PipelineCacheIdentity.Length != 0)
            {
                throw new InvalidOperationException("Pipeline layout cache identity is already initialized.");
            }
            m_PipelineCacheIdentity = RHIPipelineCacheKeyBuilder.CreatePipelineLayoutIdentity(descriptor);
        }
    }

    public struct RHIOutputStateDescriptor
    {
        public uint OutputCount;
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat ColorFormat0;
        public ERHIPixelFormat ColorFormat1;
        public ERHIPixelFormat ColorFormat2;
        public ERHIPixelFormat ColorFormat3;
        public ERHIPixelFormat ColorFormat4;
        public ERHIPixelFormat ColorFormat5;
        public ERHIPixelFormat ColorFormat6;
        public ERHIPixelFormat ColorFormat7;
        public ERHIPixelFormat DepthStencilFormat;
    }

    public struct RHIVertexElementDescriptor
    {
        public uint Slot;
        public uint Offset;
        public ERHISemanticType Type;
        public ERHISemanticFormat Format;
    }

    public struct RHIVertexLayoutDescriptor
    {
        public uint Index;
        public uint Stride;
        public uint StepRate;
        public ERHIVertexStepMode StepMode;
        public Memory<RHIVertexElementDescriptor> VertexElements;
    }

    public struct RHIBlendDescriptor
    {
        public bool BlendEnable;
        public ERHIBlendOp BlendOpColor;
        public ERHIBlendMode SrcBlendColor;
        public ERHIBlendMode DstBlendColor;
        public ERHIBlendOp BlendOpAlpha;
        public ERHIBlendMode SrcBlendAlpha;
        public ERHIBlendMode DstBlendAlpha;
        public ERHIColorWriteChannel ColorWriteChannel;
    }

    public struct RHIBlendStateDescriptor
    {
        public bool AlphaToCoverage;
        public bool IndependentBlend;
        public RHIBlendDescriptor BlendDescriptor0;
        public RHIBlendDescriptor BlendDescriptor1;
        public RHIBlendDescriptor BlendDescriptor2;
        public RHIBlendDescriptor BlendDescriptor3;
        public RHIBlendDescriptor BlendDescriptor4;
        public RHIBlendDescriptor BlendDescriptor5;
        public RHIBlendDescriptor BlendDescriptor6;
        public RHIBlendDescriptor BlendDescriptor7;
    }

    public struct RHIRasterizerStateDescriptor
    {
        public ERHIFillMode FillMode;
        public ERHICullMode CullMode;
        public bool DepthClipEnable;
        public bool ConservativeRaster;
        public bool AntialiasedLineEnable;
        public bool FrontCounterClockwise;
        public uint DepthBias;
        public float DepthBiasClamp;
        public float SlopeScaledDepthBias;
    }

    public struct RHIStencilStateDescriptor
    {
        public ERHIStencilOp StencilPassOp;
        public ERHIStencilOp StencilFailOp;
        public ERHIStencilOp StencilDepthFailOp;
        public ERHIComparisonMode ComparisonMode;
    }

    public struct RHIDepthStencilStateDescriptor
    {
        public bool DepthEnable;
        public bool DepthWriteMask;
        public bool StencilEnable;
        public byte StencilReadMask;
        public byte StencilWriteMask;
        public ERHIComparisonMode ComparisonMode;
        public RHIStencilStateDescriptor BackFace;
        public RHIStencilStateDescriptor FrontFace;
    }

    public struct RHIRenderStateDescriptor
    {
        public uint? SampleMask;
        public RHIBlendStateDescriptor BlendState;
        public RHIRasterizerStateDescriptor RasterizerState;
        public RHIDepthStencilStateDescriptor DepthStencilState;
    }

    public struct RHIComputePipelineDescriptor
    {
        public uint3 ThreadSize;
        public RHIFunction ComputeFunction;
        public RHIPipelineLayout PipelineLayout;
    }

    public struct RHIRayHitGroupDescriptor
    {
        public string Name;
        public ERHIHitGroupType Type;
        public RHIRayFunctionDescriptor? AnyHit;
        public RHIRayFunctionDescriptor? Intersect;
        public RHIRayFunctionDescriptor? ClosestHit;
    }

    public struct RHIRayGeneralGroupDescriptor
    {
        public string Name;
        public RHIRayFunctionDescriptor General;
    }

    public struct RHIRaytracingPipelineDescriptor
    {
        public uint3 ThreadSize;
        public uint MaxPayloadSize;
        public uint MaxAttributeSize;
        public uint MaxRecursionDepth;
        public RHIPipelineLayout PipelineLayout;
        public RHIFunctionLibrary FunctionLibrary;
        public RHIRayGeneralGroupDescriptor RayGeneration;
        public Memory<RHIRayHitGroupDescriptor> RayHitGroups;
        public Memory<RHIRayGeneralGroupDescriptor> RayMissGroups;
        public Memory<RHIRayGeneralGroupDescriptor> RayCallableGroups;
        public uint LocalDataStrideInBytes;
    }

    public struct RHIVertexAssemblerDescriptor
    {
        public RHIFunction VertexFunction;
        public Memory<RHIVertexLayoutDescriptor> VertexLayouts;

        public RHIVertexAssemblerDescriptor(RHIFunction vertexFunction, in Memory<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            VertexLayouts = vertexLayouts;
            VertexFunction = vertexFunction;
        }
    }

    public struct RHIMeshletAssemblerDescriptor
    {
        public RHIFunction? TaskFunction;
        public RHIFunction? MeshFunction;

        public RHIMeshletAssemblerDescriptor(RHIFunction? taskFunction, RHIFunction? meshFunction)
        {
            TaskFunction = taskFunction;
            MeshFunction = meshFunction;
        }
    }

    public struct RHIPrimitiveAssemblerDescriptor
    {
        public ERHIPrimitiveType PrimitiveType => (MeshletAssembler.HasValue ? ERHIPrimitiveType.Mesh : ERHIPrimitiveType.Vertex);

        public ERHIPrimitiveTopology PrimitiveTopology;
        public RHIVertexAssemblerDescriptor? VertexAssembler;
        public RHIMeshletAssemblerDescriptor? MeshletAssembler;
    }

    public struct RHIRasterPipelineDescriptor
    {
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat DepthFormat;
        public ERHIPixelFormat[] ColorFormats;
        public RHIAttachmentInterfaceSignature AttachmentInterface;
        /// <summary>
        /// Optional for output-only pipelines and required when the fragment
        /// shader consumes color attachment inputs.
        /// </summary>
        public RHIRasterAttachmentShaderAbiClaim? AttachmentShaderAbiClaim;
        public RHIRenderStateDescriptor RenderState;
        public RHIFunction? FragmentFunction;
        public RHIPipelineLayout? PipelineLayout;
        public RHIPrimitiveAssemblerDescriptor PrimitiveAssembler;
    }

    public abstract class RHIComputePipeline : Disposal
    {
        public RHIComputePipelineDescriptor Descriptor => m_Descriptor;


        protected RHIComputePipelineDescriptor m_Descriptor;
    }

    public abstract class RHIRaytracingPipeline : Disposal
    {
        public RHIRaytracingPipelineDescriptor Descriptor => m_Descriptor;


        protected RHIRaytracingPipelineDescriptor m_Descriptor;
    }

    public abstract class RHIRasterPipeline : Disposal
    {
        public RHIRasterPipelineDescriptor Descriptor
        {
            get
            {
                ThrowIfDisposed();
                RHIRasterPipelineDescriptor snapshot = m_Descriptor;
                snapshot.ColorFormats =
                    (ERHIPixelFormat[])m_Descriptor.ColorFormats.Clone();
                return snapshot;
            }
        }

        internal ref readonly RHIRasterPipelineDescriptor DescriptorInternal
        {
            get
            {
                ThrowIfDisposed();
                return ref m_Descriptor;
            }
        }

        protected RHIRasterPipelineDescriptor m_Descriptor;
    }

    internal static class RHIRasterPipelineContract
    {
        internal static RHIRasterPipelineDescriptor SnapshotAndValidate(
            in RHIRasterPipelineDescriptor descriptor)
        {
            RHIRasterPipelineDescriptor snapshot = descriptor;
            snapshot.SampleCount = ValidateSampleCount(
                descriptor.SampleCount,
                nameof(descriptor.SampleCount));
            snapshot.ColorFormats = descriptor.ColorFormats == null
                ? throw new ArgumentException("Raster pipeline ColorFormats cannot be null.", nameof(descriptor))
                : (ERHIPixelFormat[])descriptor.ColorFormats.Clone();
            if (snapshot.ColorFormats.Length > RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    snapshot.ColorFormats.Length,
                    $"Raster pipelines support at most {RHIAttachmentIndexArray.MaxAttachments} color attachments.");
            }
            for (int i = 0; i < snapshot.ColorFormats.Length; ++i)
            {
                if (snapshot.ColorFormats[i] == ERHIPixelFormat.Unknown ||
                    RHIBarrierUtility.InferAspectMask(snapshot.ColorFormats[i]) != ERHITextureAspectMask.Color)
                {
                    throw new ArgumentException(
                        $"Raster pipeline color format at index {i} is not a color-renderable format.",
                        nameof(descriptor));
                }
            }
            if (descriptor.DepthFormat != ERHIPixelFormat.Unknown &&
                (RHIBarrierUtility.InferAspectMask(descriptor.DepthFormat) & ERHITextureAspectMask.Depth) == 0)
            {
                throw new ArgumentException(
                    "Raster pipeline DepthFormat must be Unknown or a depth/depth-stencil format.",
                    nameof(descriptor));
            }

            bool usesDualSourceColor = UsesDualSourceBlend(
                descriptor.RenderState.BlendState,
                snapshot.ColorFormats.Length);
            snapshot.AttachmentInterface = descriptor.AttachmentInterface.NormalizeForPipeline(
                snapshot.ColorFormats.Length,
                usesDualSourceColor);
            ERHITextureAspectMask depthStencilAspects =
                descriptor.DepthFormat == ERHIPixelFormat.Unknown
                    ? ERHITextureAspectMask.None
                    : RHIBarrierUtility.InferAspectMask(descriptor.DepthFormat) &
                      (ERHITextureAspectMask.Depth |
                       ERHITextureAspectMask.Stencil);
            ValidateDepthStencilCompatibility(
                in snapshot,
                in snapshot.AttachmentInterface,
                depthStencilAspects,
                nameof(descriptor));
            ValidateAssemblerExclusive(in snapshot, nameof(descriptor));
            return snapshot;
        }

        internal static bool RequestsMeshPath(in RHIRasterPipelineDescriptor descriptor)
        {
            return descriptor.PrimitiveAssembler.MeshletAssembler.HasValue
                || descriptor.PrimitiveAssembler.PrimitiveType == ERHIPrimitiveType.Mesh;
        }

        private static void ValidateAssemblerExclusive(
            in RHIRasterPipelineDescriptor descriptor,
            string parameterName)
        {
            bool hasVertexAssembler = descriptor.PrimitiveAssembler.VertexAssembler.HasValue;
            bool hasMeshletAssembler = descriptor.PrimitiveAssembler.MeshletAssembler.HasValue;
            if (hasVertexAssembler && hasMeshletAssembler)
            {
                throw new ArgumentException(
                    "Raster pipelines cannot combine VertexAssembler and MeshletAssembler.",
                    parameterName);
            }

            if (hasMeshletAssembler &&
                descriptor.PrimitiveAssembler.MeshletAssembler.Value.MeshFunction == null)
            {
                throw new ArgumentException(
                    "Mesh pipelines require a mesh shader function.",
                    parameterName);
            }
        }

        internal static void ValidateDepthStencilCompatibility(
            in RHIRasterPipelineDescriptor descriptor,
            in RHIAttachmentInterfaceSignature attachmentInterface,
            ERHITextureAspectMask availableAspects,
            string parameterName)
        {
            const ERHITextureAspectMask knownDepthStencilAspects =
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
            if ((availableAspects & ~knownDepthStencilAspects) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    availableAspects,
                    "Available pipeline depth/stencil aspects contain unknown or color bits.");
            }

            ref readonly RHIDepthStencilStateDescriptor state =
                ref descriptor.RenderState.DepthStencilState;
            ValidateComparisonMode(
                state.ComparisonMode,
                "ComparisonMode",
                parameterName);
            ValidateStencilFace(
                in state.FrontFace,
                "FrontFace",
                parameterName);
            ValidateStencilFace(
                in state.BackFace,
                "BackFace",
                parameterName);

            bool hasDepth =
                (availableAspects & ERHITextureAspectMask.Depth) != 0;
            bool hasStencil =
                (availableAspects & ERHITextureAspectMask.Stencil) != 0;
            if (state.DepthEnable && !hasDepth)
            {
                throw new ArgumentException(
                    "DepthEnable requires an available depth aspect.",
                    parameterName);
            }
            if (state.StencilEnable && !hasStencil)
            {
                throw new ArgumentException(
                    "StencilEnable requires an available stencil aspect.",
                    parameterName);
            }

            ERHISubPassFlags flags = attachmentInterface.DepthStencilFlags;
            if ((flags & ERHISubPassFlags.ReadOnlyDepth) != 0)
            {
                if (!hasDepth)
                {
                    throw new ArgumentException(
                        "ReadOnlyDepth requires an available depth aspect.",
                        parameterName);
                }
                if (HasDepthWrites(in state))
                {
                    throw new ArgumentException(
                        "ReadOnlyDepth is incompatible with enabled depth writes.",
                        parameterName);
                }
            }
            if ((flags & ERHISubPassFlags.ReadOnlyStencil) != 0)
            {
                if (!hasStencil)
                {
                    throw new ArgumentException(
                        "ReadOnlyStencil requires an available stencil aspect.",
                        parameterName);
                }
                if (HasStencilWrites(in state))
                {
                    throw new ArgumentException(
                        "ReadOnlyStencil is incompatible with stencil operations that can modify enabled write-mask bits.",
                        parameterName);
                }
            }
        }

        internal static bool HasDepthWrites(
            in RHIDepthStencilStateDescriptor state) =>
            state.DepthEnable && state.DepthWriteMask;

        internal static bool HasStencilWrites(
            in RHIDepthStencilStateDescriptor state)
        {
            return state.StencilEnable &&
                state.StencilWriteMask != 0 &&
                (FaceCanModifyStencil(in state.FrontFace) ||
                 FaceCanModifyStencil(in state.BackFace));
        }

        private static bool FaceCanModifyStencil(
            in RHIStencilStateDescriptor face)
        {
            return face.StencilFailOp != ERHIStencilOp.Keep ||
                face.StencilDepthFailOp != ERHIStencilOp.Keep ||
                face.StencilPassOp != ERHIStencilOp.Keep;
        }

        private static void ValidateStencilFace(
            in RHIStencilStateDescriptor face,
            string faceName,
            string parameterName)
        {
            ValidateComparisonMode(
                face.ComparisonMode,
                $"{faceName}.ComparisonMode",
                parameterName);
            ValidateStencilOperation(face.StencilFailOp, $"{faceName}.StencilFailOp", parameterName);
            ValidateStencilOperation(face.StencilDepthFailOp, $"{faceName}.StencilDepthFailOp", parameterName);
            ValidateStencilOperation(face.StencilPassOp, $"{faceName}.StencilPassOp", parameterName);
        }

        private static void ValidateComparisonMode(
            ERHIComparisonMode comparisonMode,
            string comparisonName,
            string parameterName)
        {
            switch (comparisonMode)
            {
                case ERHIComparisonMode.Never:
                case ERHIComparisonMode.Less:
                case ERHIComparisonMode.Equal:
                case ERHIComparisonMode.LessEqual:
                case ERHIComparisonMode.Greater:
                case ERHIComparisonMode.NotEqual:
                case ERHIComparisonMode.GreaterEqual:
                case ERHIComparisonMode.Always:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        comparisonMode,
                        $"{comparisonName} is not a supported comparison mode.");
            }
        }

        private static void ValidateStencilOperation(
            ERHIStencilOp operation,
            string operationName,
            string parameterName)
        {
            switch (operation)
            {
                case ERHIStencilOp.Keep:
                case ERHIStencilOp.Zero:
                case ERHIStencilOp.Replace:
                case ERHIStencilOp.IncrementSaturation:
                case ERHIStencilOp.DecrementSaturation:
                case ERHIStencilOp.Invert:
                case ERHIStencilOp.Increment:
                case ERHIStencilOp.Decrement:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        operation,
                        $"{operationName} is not a supported stencil operation.");
            }
        }

        internal static ERHISampleCount ValidateSampleCount(
            ERHISampleCount sampleCount,
            string parameterName)
        {
            switch (sampleCount)
            {
                case ERHISampleCount.None:
                case ERHISampleCount.Count2:
                case ERHISampleCount.Count4:
                case ERHISampleCount.Count8:
                    return sampleCount;
                default:
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        sampleCount,
                        "Raster sample count must be 1, 2, 4, or 8.");
            }
        }

        internal static bool UsesDualSourceBlend(
            in RHIBlendStateDescriptor blendState,
            int colorAttachmentCount)
        {
            int descriptorCount = blendState.IndependentBlend
                ? colorAttachmentCount
                : Math.Min(colorAttachmentCount, 1);
            for (int i = 0; i < descriptorCount; ++i)
            {
                RHIBlendDescriptor blend = GetBlendDescriptor(in blendState, i);
                if (blend.BlendEnable &&
                    (IsSecondarySource(blend.SrcBlendColor) ||
                     IsSecondarySource(blend.DstBlendColor) ||
                     IsSecondarySource(blend.SrcBlendAlpha) ||
                     IsSecondarySource(blend.DstBlendAlpha)))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void ValidateAttachmentSupport(
            RHIDevice device,
            in RHIRasterPipelineDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            RHIAttachmentInterfaceSignature attachmentInterface =
                descriptor.AttachmentInterface;
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    attachmentInterface.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                bool isInput =
                    (attachmentInterface.ColorInputMask & bit) != 0;
                bool isOutput =
                    (attachmentInterface.ColorOutputMask & bit) != 0;
                if (!isInput && !isOutput)
                {
                    continue;
                }

                RHIBlendDescriptor blend = default;
                if (isOutput)
                {
                    int outputLocation =
                        FindOutputLocation(
                            in attachmentInterface,
                            logicalAttachment);
                    int blendIndex =
                        descriptor.RenderState.BlendState
                            .IndependentBlend
                            ? outputLocation
                            : 0;
                    blend = GetBlendDescriptor(
                        in descriptor.RenderState.BlendState,
                        blendIndex);
                }
                RHIRasterAttachmentSupportQuery query = new(
                    descriptor.ColorFormats[logicalAttachment],
                    descriptor.SampleCount,
                    isInput,
                    isOutput,
                    in blend,
                    alphaToCoverage: descriptor.RenderState.BlendState
                        .AlphaToCoverage,
                    isLayered:
                        (attachmentInterface.LayeredAccessMask & bit) != 0);
                RHICapability support =
                    device.QueryRasterAttachmentSupport(in query);
                if (support.Tier == ERHICapabilityTier.Unavailable)
                {
                    throw new NotSupportedException(
                        $"{device.BackendType} cannot express logical " +
                        $"color attachment {logicalAttachment} " +
                        $"(Input={isInput}, Output={isOutput}, " +
                        $"Layered={query.IsLayered}, " +
                        $"Format={query.Format}, Samples={query.SampleCount}, " +
                        $"Blend={query.Blend.BlendEnable}, " +
                        $"AlphaToCoverage={query.AlphaToCoverage}): " +
                        support.UnavailableReason);
                }
            }

            ValidateAttachmentShaderAbi(device, in descriptor);
        }

        internal static void ValidateAttachmentShaderAbi(
            RHIDevice device,
            in RHIRasterPipelineDescriptor descriptor)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                descriptor.AttachmentInterface;
            RHIRasterAttachmentShaderAbiClaim? claim =
                descriptor.AttachmentShaderAbiClaim;
            bool requiresClaim = attachmentInterface.ColorInputMask != 0;
            if (!requiresClaim && !claim.HasValue)
            {
                return;
            }
            if (descriptor.FragmentFunction == null)
            {
                throw new ArgumentException(
                    "A raster attachment input requires a fragment function.",
                    nameof(descriptor));
            }
            if (!claim.HasValue)
            {
                throw new InvalidOperationException(
                    "The raster pipeline does not claim the versioned SharpGPU attachment shader ABI required by its Inputs declaration.");
            }
            RHIPipelineLayout pipelineLayout =
                descriptor.PipelineLayout ??
                throw new ArgumentException(
                    "Raster attachment shader ABI validation requires a pipeline layout.",
                    nameof(descriptor));
            RHIRasterAttachmentShaderAbiDescriptor abiDescriptor = new()
            {
                PipelineLayout = pipelineLayout,
                SampleCount = descriptor.SampleCount,
                ColorFormats = descriptor.ColorFormats,
                AttachmentInterface = attachmentInterface,
            };
            RHIRasterAttachmentShaderAbi expected =
                device.QueryRasterAttachmentShaderAbi(in abiDescriptor);
            RHIRasterAttachmentShaderAbiClaim expectedClaim =
                expected.CreateClaim();
            if (claim.Value != expectedClaim)
            {
                throw new InvalidOperationException(
                    $"The raster pipeline attachment shader ABI claim " +
                    $"({claim.Value.Backend}/v{claim.Value.Revision}/" +
                    $"{claim.Value.ContractHash}) does not match the " +
                    $"pipeline contract ({expectedClaim.Backend}/" +
                    $"v{expectedClaim.Revision}/" +
                    $"{expectedClaim.ContractHash}).");
            }
        }

        private static int FindOutputLocation(
            in RHIAttachmentInterfaceSignature attachmentInterface,
            int logicalAttachment)
        {
            for (int outputLocation = 0;
                 outputLocation <
                    attachmentInterface.ColorOutputLocationCount;
                 ++outputLocation)
            {
                if (attachmentInterface
                        .GetColorOutputLogicalAttachment(
                            outputLocation) ==
                    logicalAttachment)
                {
                    return outputLocation;
                }
            }
            throw new InvalidOperationException(
                $"Logical attachment {logicalAttachment} is marked as an " +
                "output but has no output location.");
        }

        internal static RHIBlendDescriptor GetBlendDescriptor(
            in RHIBlendStateDescriptor blendState,
            int index)
        {
            return index switch
            {
                0 => blendState.BlendDescriptor0,
                1 => blendState.BlendDescriptor1,
                2 => blendState.BlendDescriptor2,
                3 => blendState.BlendDescriptor3,
                4 => blendState.BlendDescriptor4,
                5 => blendState.BlendDescriptor5,
                6 => blendState.BlendDescriptor6,
                7 => blendState.BlendDescriptor7,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }

        private static bool IsSecondarySource(ERHIBlendMode mode)
        {
            return mode == ERHIBlendMode.SecondarySourceColor ||
                mode == ERHIBlendMode.InverseSecondarySourceColor ||
                mode == ERHIBlendMode.SecondarySourceAlpha ||
                mode == ERHIBlendMode.InverseSecondarySourceAlpha;
        }
    }

    public struct RHIWorkGraphPipelineDescriptor
    {
        public string Name;
        public RHIFunctionLibrary FunctionLibrary;
        public RHIPipelineLayout PipelineLayout;
    }

    public readonly struct RHIWorkGraphMemoryRequirements
    {
        public readonly ulong MinSizeInBytes;
        public readonly ulong MaxSizeInBytes;
        public readonly uint SizeGranularityInBytes;

        public RHIWorkGraphMemoryRequirements(ulong minSizeInBytes, ulong maxSizeInBytes, uint sizeGranularityInBytes)
        {
            MinSizeInBytes = minSizeInBytes;
            MaxSizeInBytes = maxSizeInBytes;
            SizeGranularityInBytes = sizeGranularityInBytes;
        }
    }

    public abstract class RHIWorkGraphPipeline : Disposal
    {
        public RHIWorkGraphPipelineDescriptor Descriptor
        {
            get
            {
                ThrowIfDisposed();
                return m_Descriptor;
            }
        }

        public abstract RHIWorkGraphMemoryRequirements MemoryRequirements { get; }

        protected RHIWorkGraphPipelineDescriptor m_Descriptor;
    }
    #region MachineLearning
    public enum ERHIMLTensorBindingKind : byte
    {
        Input = 0,
        Output = 1,
        Pending = 255
    }

    public struct RHIMLTensorBindingInfo
    {
        public string Name;
        public uint Index;
        public ERHIMLTensorBindingKind Kind;
        public RHIMLTensorDescriptor Descriptor;
    }

    public abstract class RHIMLProgram : Disposal
    {
        public string? Name => m_Name;

        protected string? m_Name;
    }

    public struct RHIMLPipelineDescriptor
    {
        public string Name;
        public RHIMLBinary Binary;
    }

    public abstract class RHIMLPipeline : Disposal
    {
        public RHIMLPipelineDescriptor Descriptor => m_Descriptor;

        public ulong TemporaryResourceSize => m_TemporaryResourceSize;
        public ulong PersistentResourceSize => m_PersistentResourceSize;
        public uint InputCount => m_InputCount;
        public uint OutputCount => m_OutputCount;
        public ReadOnlyMemory<RHIMLTensorBindingInfo> BindingInfos => m_BindingInfos ?? ReadOnlyMemory<RHIMLTensorBindingInfo>.Empty;

        protected RHIMLPipelineDescriptor m_Descriptor;
        protected ulong m_TemporaryResourceSize;
        protected ulong m_PersistentResourceSize;
        protected uint m_InputCount;
        protected uint m_OutputCount;
        protected RHIMLTensorBindingInfo[]? m_BindingInfos;
    }

    public struct RHIMLBindingTableDescriptor
    {
        public RHIMLPipeline Pipeline;
        public Memory<RHITensor> Inputs;
        public Memory<RHITensor> Outputs;
        /// <summary>
        /// Additive view bindings. When non-empty, used instead of <see cref="Inputs"/> for that side.
        /// </summary>
        public Memory<RHITensorView> InputViews;
        /// <summary>
        /// Additive view bindings. When non-empty, used instead of <see cref="Outputs"/> for that side.
        /// </summary>
        public Memory<RHITensorView> OutputViews;
    }

    public abstract class RHIMLBindingTable : Disposal
    {
        public RHIMLPipeline? Pipeline => m_Pipeline;

        protected RHIMLPipeline? m_Pipeline;
    }

    internal static class RHIMLHelpers
    {
        internal static bool HasExplicitStrides(in RHIMLTensorDescriptor descriptor)
        {
            return descriptor.Strides is Memory<uint> strides && strides.Length > 0;
        }

        public static uint GetElementSize(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => 4,
                ERHIMLDataType.Float16 => 2,
                ERHIMLDataType.BFloat16 => 2,
                ERHIMLDataType.Int32 => 4,
                ERHIMLDataType.Int16 => 2,
                ERHIMLDataType.Int8 => 1,
                ERHIMLDataType.UInt32 => 4,
                ERHIMLDataType.UInt16 => 2,
                ERHIMLDataType.UInt8 => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), $"Unsupported ML data type '{dataType}'."),
            };
        }

        public static ulong CalculateElementCount(in RHIMLTensorDescriptor descriptor)
        {
            ulong elementCount = 1;
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            for (int i = 0; i < dimensions.Length; ++i)
            {
                elementCount *= dimensions[i];
            }

            return elementCount;
        }

        public static ulong CalculateMinimumByteLength(in RHIMLTensorDescriptor descriptor)
        {
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            if (dimensions.Length == 0)
            {
                return 0;
            }

            uint elementSize = GetElementSize(descriptor.DataType);
            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                ReadOnlySpan<uint> strides = explicitStrides.Span;
                if (strides.Length != dimensions.Length)
                {
                    throw new InvalidOperationException($"Tensor stride rank mismatch. dimensions={dimensions.Length}, strides={strides.Length}.");
                }

                ulong offsetInElements = 0;
                for (int i = 0; i < dimensions.Length; ++i)
                {
                    if (dimensions[i] == 0)
                    {
                        return 0;
                    }

                    offsetInElements += (ulong)(dimensions[i] - 1) * strides[i];
                }

                return (offsetInElements + 1) * elementSize;
            }

            return CalculateElementCount(descriptor) * elementSize;
        }

        public static uint[] GetEffectiveStrides(in RHIMLTensorDescriptor descriptor)
        {
            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                return explicitStrides.ToArray();
            }

            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            uint[] strides = new uint[dimensions.Length];
            ulong runningStride = 1;
            for (int i = dimensions.Length - 1; i >= 0; --i)
            {
                strides[i] = checked((uint)runningStride);
                runningStride *= dimensions[i];
            }

            return strides;
        }

        public static RHIMLTensorDescriptor CloneLayoutDescriptor(in RHIMLTensorDescriptor descriptor)
        {
            RHIMLTensorDescriptor clone = descriptor;
            clone.Dimensions = descriptor.Dimensions.ToArray();
            clone.Strides =
                descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0
                    ? explicitStrides.ToArray() : null;
            clone.BackingBuffer = null;
            clone.BackingBufferOffset = 0;
            return clone;
        }

        public static bool HasCompatibleLayout(in RHIMLTensorDescriptor expected, in RHIMLTensorDescriptor actual)
        {
            if (expected.DataType != actual.DataType)
            {
                return false;
            }

            ReadOnlySpan<uint> expectedDimensions = expected.Dimensions.Span;
            ReadOnlySpan<uint> actualDimensions = actual.Dimensions.Span;
            if (expectedDimensions.Length != actualDimensions.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedDimensions.Length; ++i)
            {
                if (expectedDimensions[i] != actualDimensions[i])
                {
                    return false;
                }
            }

            ReadOnlySpan<uint> expectedStrides = GetEffectiveStrides(expected);
            ReadOnlySpan<uint> actualStrides = GetEffectiveStrides(actual);
            if (expectedStrides.Length != actualStrides.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedStrides.Length; ++i)
            {
                if (expectedStrides[i] != actualStrides[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static string DescribeLayout(in RHIMLTensorDescriptor descriptor)
        {
            string dimensions = string.Join("x", descriptor.Dimensions.ToArray());
            string strides = string.Join(",", GetEffectiveStrides(descriptor));
            return $"{descriptor.DataType}[{dimensions}] strides=[{strides}]";
        }
    }
    #endregion

    #region MLBinaryHash
    internal static class RHIMLBinaryHash
    {
        internal static ulong ComputeContentHash(ReadOnlySpan<byte> payload)
        {
            const ulong offsetBasis = 0xCBF29CE484222325UL;
            const ulong prime = 0x100000001B3UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < payload.Length; ++i)
            {
                hash ^= payload[i];
                hash *= prime;
            }

            return hash;
        }
    }
    #endregion

    #region MLBinary
    public enum ERHIMLBinaryFormat : byte
    {
        Unknown = 0,
        DirectMLProgramV1 = 1,
        MetalPackageV1 = 2,
    }

    public struct RHIMLBinaryReflection
    {
        public string EntryName;
        public RHIMLTensorBindingInfo[] Bindings;
        public ulong IntermediateHeapSizeHint;
    }

    /// <summary>
    /// Opaque ML binary artifact consumed by <see cref="RHIDevice.CreateMLPipeline"/>.
    /// Public RHI ML surface is Binary ? Pipeline ? BindingTable ? Encoder only (ADR-0052).
    /// </summary>
    public sealed class RHIMLBinary
    {
        public ERHIMLBinaryFormat Format { get; }
        public ReadOnlyMemory<byte> Payload { get; }
        public ulong ContentHash { get; }
        public RHIMLBinaryReflection Reflection { get; }

        public RHIMLBinary(
            ERHIMLBinaryFormat format,
            ReadOnlyMemory<byte> payload,
            in RHIMLBinaryReflection reflection,
            ulong contentHash)
        {
            if (format == ERHIMLBinaryFormat.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(format), "ML binary format must be specified.");
            }

            if (payload.IsEmpty)
            {
                throw new ArgumentException("ML binary payload must not be empty.", nameof(payload));
            }

            Format = format;
            Payload = payload;
            Reflection = reflection;
            ContentHash = contentHash;
        }
    }
    #endregion

    #region PipelineCache
    public enum ERHIPipelineCacheImportStatus : byte
    {
        Empty = 0,
        Loaded = 1,
        Incompatible = 2,
        Corrupt = 3,
    }

    public readonly struct RHIPipelineCacheImportResult
    {
        public ERHIPipelineCacheImportStatus Status { get; }
        public string Reason { get; }
        public bool IsLoaded => Status == ERHIPipelineCacheImportStatus.Loaded;

        internal RHIPipelineCacheImportResult(
            in ERHIPipelineCacheImportStatus status,
            string reason)
        {
            Status = status;
            Reason = reason ?? string.Empty;
        }
    }

    public abstract class RHIPipelineCache : Disposal
    {
        private readonly RHIPipelineCacheIdentity m_Identity;

        internal RHIPipelineCache(RHIDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (device.IsDisposed)
            {
                throw new ObjectDisposedException(device.GetType().FullName);
            }

            m_Identity = RHIPipelineCacheIdentity.FromDevice(device);
        }

        public RHIPipelineCacheImportResult Import(in ReadOnlyMemory<byte> blob)
        {
            ThrowIfDisposed();

            if (blob.IsEmpty)
            {
                if (!TryReplaceNativePayload(ReadOnlySpan<byte>.Empty, out string reason))
                {
                    return new RHIPipelineCacheImportResult(
                        ERHIPipelineCacheImportStatus.Corrupt,
                        string.IsNullOrWhiteSpace(reason)
                            ? "The backend rejected an empty pipeline cache."
                            : reason);
                }

                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Empty,
                    "An empty cache was initialized.");
            }

            RHIPipelineCacheImportResult decodeResult = RHIPipelineCacheBlob.TryDecode(
                blob.Span,
                m_Identity,
                out byte[] nativePayload);
            if (decodeResult.Status != ERHIPipelineCacheImportStatus.Loaded)
            {
                return decodeResult;
            }

            if (!TryReplaceNativePayload(nativePayload, out string nativeReason))
            {
                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Corrupt,
                    string.IsNullOrWhiteSpace(nativeReason)
                        ? "The backend rejected the compatible native pipeline-cache payload."
                        : nativeReason);
            }

            return decodeResult;
        }

        public byte[] Export()
        {
            ThrowIfDisposed();
            byte[] nativePayload = ExportNativePayload();
            return RHIPipelineCacheBlob.Encode(m_Identity, nativePayload);
        }

        public abstract RHIComputePipeline CreateComputePipeline(
            in RHIComputePipelineDescriptor descriptor);

        public abstract RHIRasterPipeline CreateRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor);

        protected string BuildComputePipelineCacheKey(
            in RHIComputePipelineDescriptor descriptor)
        {
            return RHIPipelineCacheKeyBuilder.CreateComputeKey(descriptor, m_Identity);
        }

        protected string BuildRasterPipelineCacheKey(
            in RHIRasterPipelineDescriptor descriptor)
        {
            return RHIPipelineCacheKeyBuilder.CreateRasterKey(descriptor, m_Identity);
        }

        protected abstract bool TryReplaceNativePayload(
            ReadOnlySpan<byte> nativePayload,
            out string reason);

        protected abstract byte[] ExportNativePayload();
    }

    internal readonly struct RHIPipelineCacheIdentity
    {
        public const uint CurrentSchemaRevision = 3;
        // Revision 9 is an incompatible bump for the Motion public descriptor change.
        // Acceleration-structure descriptors are not part of the pipeline-cache key.
        public const uint CurrentPipelineAbiRevision = 9;

        public ERHIBackend Backend { get; }
        public uint VendorId { get; }
        public uint DeviceId { get; }
        public string DriverVersion { get; }

        public RHIPipelineCacheIdentity(
            in ERHIBackend backend,
            in uint vendorId,
            in uint deviceId,
            string driverVersion)
        {
            Backend = backend;
            VendorId = vendorId;
            DeviceId = deviceId;
            DriverVersion = driverVersion ?? string.Empty;
        }

        public static RHIPipelineCacheIdentity FromDevice(RHIDevice device)
        {
            return new RHIPipelineCacheIdentity(
                device.BackendType,
                device.VendorId.IntValue,
                device.DeviceId.IntValue,
                device.DriverVersion);
        }
    }

    internal static class RHIPipelineCacheBlob
    {
        private static readonly byte[] s_Magic = Encoding.ASCII.GetBytes("SGPUCACH");
        private const int DigestSize = 32;
        private const int MaxDriverVersionByteCount = 4096;

        public static byte[] Encode(
            in RHIPipelineCacheIdentity identity,
            ReadOnlySpan<byte> nativePayload)
        {
            byte[] payloadDigest = SHA256.HashData(nativePayload);
            byte[] driverBytes = Encoding.UTF8.GetBytes(identity.DriverVersion);
            if (driverBytes.Length > MaxDriverVersionByteCount)
            {
                throw new InvalidOperationException(
                    $"Driver identity is {driverBytes.Length} bytes, exceeding the {MaxDriverVersionByteCount}-byte pipeline-cache limit.");
            }

            using MemoryStream stream = new MemoryStream(
                checked(8 + 4 + 4 + 1 + 4 + 4 + 4 + driverBytes.Length + 4 + DigestSize + nativePayload.Length));
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(s_Magic);
            writer.Write(RHIPipelineCacheIdentity.CurrentSchemaRevision);
            writer.Write(RHIPipelineCacheIdentity.CurrentPipelineAbiRevision);
            writer.Write((byte)identity.Backend);
            writer.Write(identity.VendorId);
            writer.Write(identity.DeviceId);
            writer.Write(driverBytes.Length);
            writer.Write(driverBytes);
            writer.Write(nativePayload.Length);
            writer.Write(payloadDigest);
            writer.Write(nativePayload);
            writer.Flush();
            return stream.ToArray();
        }

        public static RHIPipelineCacheImportResult TryDecode(
            ReadOnlySpan<byte> blob,
            in RHIPipelineCacheIdentity expectedIdentity,
            out byte[] nativePayload)
        {
            nativePayload = Array.Empty<byte>();
            try
            {
                using MemoryStream stream = new MemoryStream(blob.ToArray(), writable: false);
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                byte[] magic = reader.ReadBytes(s_Magic.Length);
                if (magic.Length != s_Magic.Length || !magic.AsSpan().SequenceEqual(s_Magic))
                {
                    return Corrupt("Pipeline-cache magic is invalid.");
                }

                uint schemaRevision = reader.ReadUInt32();
                uint pipelineAbiRevision = reader.ReadUInt32();
                byte backendValue = reader.ReadByte();
                uint vendorId = reader.ReadUInt32();
                uint deviceId = reader.ReadUInt32();

                int driverByteCount = reader.ReadInt32();
                if (driverByteCount < 0 || driverByteCount > MaxDriverVersionByteCount)
                {
                    return Corrupt("Pipeline-cache driver identity length is invalid.");
                }

                byte[] driverBytes = reader.ReadBytes(driverByteCount);
                if (driverBytes.Length != driverByteCount)
                {
                    return Corrupt("Pipeline-cache driver identity is truncated.");
                }

                string driverVersion = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(driverBytes);

                int payloadLength = reader.ReadInt32();
                if (payloadLength < 0)
                {
                    return Corrupt("Pipeline-cache native payload length is invalid.");
                }

                byte[] expectedDigest = reader.ReadBytes(DigestSize);
                if (expectedDigest.Length != DigestSize)
                {
                    return Corrupt("Pipeline-cache payload digest is truncated.");
                }

                long remaining = stream.Length - stream.Position;
                if (remaining != payloadLength)
                {
                    return Corrupt(
                        $"Pipeline-cache native payload length is {payloadLength}, but {remaining} bytes remain.");
                }

                nativePayload = reader.ReadBytes(payloadLength);
                if (!CryptographicOperations.FixedTimeEquals(
                        expectedDigest,
                        SHA256.HashData(nativePayload)))
                {
                    nativePayload = Array.Empty<byte>();
                    return Corrupt("Pipeline-cache native payload checksum does not match.");
                }

                if (schemaRevision != RHIPipelineCacheIdentity.CurrentSchemaRevision)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache schema revision {schemaRevision} does not match {RHIPipelineCacheIdentity.CurrentSchemaRevision}.");
                }

                if (pipelineAbiRevision != RHIPipelineCacheIdentity.CurrentPipelineAbiRevision)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline ABI revision {pipelineAbiRevision} does not match {RHIPipelineCacheIdentity.CurrentPipelineAbiRevision}.");
                }

                if (!Enum.IsDefined(typeof(ERHIBackend), backendValue)
                    || (ERHIBackend)backendValue != expectedIdentity.Backend)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache backend {(ERHIBackend)backendValue} does not match {expectedIdentity.Backend}.");
                }

                if (vendorId != expectedIdentity.VendorId || deviceId != expectedIdentity.DeviceId)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache adapter {vendorId:X8}:{deviceId:X8} does not match {expectedIdentity.VendorId:X8}:{expectedIdentity.DeviceId:X8}.");
                }

                if (!string.Equals(
                        driverVersion,
                        expectedIdentity.DriverVersion,
                        StringComparison.Ordinal))
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible("Pipeline-cache driver identity does not match the active device.");
                }

                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Loaded,
                    "The compatible pipeline cache was loaded.");
            }
            catch (Exception exception) when (
                exception is EndOfStreamException
                or IOException
                or DecoderFallbackException
                or ArgumentException
                or OverflowException)
            {
                nativePayload = Array.Empty<byte>();
                return Corrupt($"Pipeline-cache blob is malformed: {exception.Message}");
            }
        }

        private static RHIPipelineCacheImportResult Corrupt(string reason)
        {
            return new RHIPipelineCacheImportResult(
                ERHIPipelineCacheImportStatus.Corrupt,
                reason);
        }

        private static RHIPipelineCacheImportResult Incompatible(string reason)
        {
            return new RHIPipelineCacheImportResult(
                ERHIPipelineCacheImportStatus.Incompatible,
                reason);
        }
    }

    internal static unsafe class RHIPipelineCacheKeyBuilder
    {
        public static byte[] CreatePipelineLayoutIdentity(
            in RHIPipelineLayoutDescriptor descriptor)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(descriptor.bLocalSignature);
            writer.Write(descriptor.bUseVertexLayout);
            writer.Write(descriptor.PushConstantSize);

            RHIBindingTableLayout[] layouts =
                descriptor.BindingTableLayouts ?? Array.Empty<RHIBindingTableLayout>();
            writer.Write(layouts.Length);
            for (int index = 0; index < layouts.Length; ++index)
            {
                WriteBindingTableLayout(writer, layouts[index], index);
            }

            if (descriptor.StaticSamplers.HasValue)
            {
                Span<RHIStaticSamplerDescriptor> staticSamplers =
                    descriptor.StaticSamplers.Value.Span;
                writer.Write(staticSamplers.Length);
                for (int samplerGroupIndex = 0;
                     samplerGroupIndex < staticSamplers.Length;
                     ++samplerGroupIndex)
                {
                    ref RHIStaticSamplerDescriptor samplerGroup =
                        ref staticSamplers[samplerGroupIndex];
                    writer.Write(samplerGroup.Index);
                    Span<RHIStaticSamplerElement> elements = samplerGroup.Elements.Span;
                    writer.Write(elements.Length);
                    for (int elementIndex = 0;
                         elementIndex < elements.Length;
                         ++elementIndex)
                    {
                        ref RHIStaticSamplerElement element = ref elements[elementIndex];
                        writer.Write(element.BindSlot);
                        WriteSampler(writer, element.SamplerDescriptor);
                    }
                }
            }
            else
            {
                writer.Write(0);
            }

            writer.Flush();
            return SHA256.HashData(stream.ToArray());
        }

        public static string CreateComputeKey(
            in RHIComputePipelineDescriptor descriptor,
            in RHIPipelineCacheIdentity identity)
        {
            ValidatePipelineLayout(descriptor.PipelineLayout);
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write("SharpGPU.ComputePipeline");
            WriteCacheIdentity(writer, identity);
            writer.Write(descriptor.ThreadSize.x);
            writer.Write(descriptor.ThreadSize.y);
            writer.Write(descriptor.ThreadSize.z);
            WriteFunction(writer, descriptor.ComputeFunction, "compute");
            writer.Write(descriptor.PipelineLayout.PipelineCacheIdentity.Span);
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        public static string CreateRasterKey(
            in RHIRasterPipelineDescriptor descriptor,
            in RHIPipelineCacheIdentity identity)
        {
            ValidatePipelineLayout(descriptor.PipelineLayout);
            RHIRasterPipelineDescriptor snapshot =
                RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write("SharpGPU.RasterPipeline");
            WriteCacheIdentity(writer, identity);
            writer.Write((byte)snapshot.SampleCount);
            writer.Write((ushort)snapshot.DepthFormat);

            ERHIPixelFormat[] colorFormats = snapshot.ColorFormats;
            writer.Write(colorFormats.Length);
            for (int index = 0; index < colorFormats.Length; ++index)
            {
                writer.Write((ushort)colorFormats[index]);
            }

            RHIAttachmentInterfaceSignature attachment =
                snapshot.AttachmentInterface;
            writer.Write(attachment.ColorAttachmentCount);
            writer.Write(attachment.ColorInputSlotCount);
            for (int inputIndex = 0;
                 inputIndex < attachment.ColorInputSlotCount;
                 ++inputIndex)
            {
                writer.Write(attachment.GetColorInputLogicalAttachment(inputIndex));
            }
            writer.Write(attachment.ColorOutputLocationCount);
            for (int outputLocation = 0;
                 outputLocation < attachment.ColorOutputLocationCount;
                 ++outputLocation)
            {
                writer.Write(
                    attachment.GetColorOutputLogicalAttachment(outputLocation));
            }
            writer.Write(attachment.FramebufferReadWriteMask);
            writer.Write(attachment.LayeredAccessMask);
            writer.Write((byte)attachment.DepthStencilFlags);
            writer.Write(attachment.UsesDualSourceColor);
            writer.Write(snapshot.AttachmentShaderAbiClaim.HasValue);
            if (snapshot.AttachmentShaderAbiClaim.HasValue)
            {
                RHIRasterAttachmentShaderAbiClaim claim =
                    snapshot.AttachmentShaderAbiClaim.Value;
                writer.Write((byte)claim.Backend);
                writer.Write(claim.Revision);
                writer.Write(claim.ContractHash);
            }

            WriteRenderState(writer, snapshot.RenderState);
            WritePrimitiveAssembler(writer, snapshot.PrimitiveAssembler);
            WriteFunction(writer, snapshot.FragmentFunction, "fragment");
            RHIPipelineLayout pipelineLayout = snapshot.PipelineLayout
                ?? throw new ArgumentNullException(nameof(snapshot), "Raster pipeline cache key requires a PipelineLayout.");
            ValidatePipelineLayout(pipelineLayout);
            writer.Write(pipelineLayout.PipelineCacheIdentity.Span);
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        private static void WriteCacheIdentity(
            BinaryWriter writer,
            in RHIPipelineCacheIdentity identity)
        {
            writer.Write(RHIPipelineCacheIdentity.CurrentSchemaRevision);
            writer.Write(RHIPipelineCacheIdentity.CurrentPipelineAbiRevision);
            writer.Write((byte)identity.Backend);
            writer.Write(identity.VendorId);
            writer.Write(identity.DeviceId);
            writer.Write(identity.DriverVersion);
        }

        private static void ValidatePipelineLayout(RHIPipelineLayout? layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            if (layout.IsDisposed)
            {
                throw new ObjectDisposedException(layout.GetType().FullName);
            }
            if (layout.PipelineCacheIdentity.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"{layout.GetType().Name} has no canonical pipeline-layout cache identity.");
            }
        }

        private static void WriteBindingTableLayout(
            BinaryWriter writer,
            RHIBindingTableLayout? layout,
            in int layoutIndex)
        {
            ArgumentNullException.ThrowIfNull(layout);
            if (layout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    layout.GetType().FullName,
                    $"Pipeline argument-table layout {layoutIndex} is disposed.");
            }

            writer.Write(layout.CanonicalIndex);
            ReadOnlySpan<RHIBindingTableLayoutElement> elements =
                layout.CanonicalElements;
            writer.Write(elements.Length);
            for (int index = 0; index < elements.Length; ++index)
            {
                ref readonly RHIBindingTableLayoutElement element =
                    ref elements[index];
                WriteArgumentElement(
                    writer,
                    element.Slot,
                    element.Count,
                    element.Type,
                    element.Stages,
                    element.Requirement);
            }
        }

        private static void WriteArgumentElement(
            BinaryWriter writer,
            in uint slot,
            in uint count,
            in ERHIBindType type,
            in ERHIShaderStageMask stages,
            in ERHIBindingRequirement requirement)
        {
            writer.Write(slot);
            writer.Write(count);
            writer.Write((byte)type);
            writer.Write((ushort)stages);
            writer.Write((byte)requirement);
        }

        private static void WriteSampler(
            BinaryWriter writer,
            in RHISamplerDescriptor descriptor)
        {
            writer.Write(descriptor.LodMin);
            writer.Write(descriptor.LodMax);
            writer.Write(descriptor.MipLODBias);
            writer.Write(descriptor.Anisotropy);
            writer.Write((byte)descriptor.MinFilter);
            writer.Write((byte)descriptor.MagFilter);
            writer.Write((byte)descriptor.MipFilter);
            writer.Write((byte)descriptor.AddressModeU);
            writer.Write((byte)descriptor.AddressModeV);
            writer.Write((byte)descriptor.AddressModeW);
            writer.Write((byte)descriptor.ComparisonMode);
        }

        private static void WriteFunction(
            BinaryWriter writer,
            RHIFunction? function,
            string role)
        {
            writer.Write(function != null);
            if (function == null)
            {
                return;
            }
            if (function.IsDisposed)
            {
                throw new ObjectDisposedException(
                    function.GetType().FullName,
                    $"The {role} shader function is disposed.");
            }

            function.ThrowIfSourceUnavailable();
            RHIFunctionDescriptor descriptor = function.Descriptor;
            ReadOnlySpan<byte> contentDigest = function.ContentDigest.Span;
            if (contentDigest.Length != RHIFunction.ContentDigestByteCount)
            {
                throw new ArgumentException(
                    $"The {role} shader function is missing a {RHIFunction.ContentDigestByteCount}-byte content digest.",
                    nameof(function));
            }

            writer.Write((byte)function.SourceKind);
            writer.Write((byte)descriptor.PayloadKind);
            writer.Write((byte)descriptor.Type);
            writer.Write(descriptor.EntryName ?? string.Empty);
            writer.Write(contentDigest);
        }

        private static void WritePrimitiveAssembler(
            BinaryWriter writer,
            in RHIPrimitiveAssemblerDescriptor descriptor)
        {
            writer.Write((byte)descriptor.PrimitiveType);
            writer.Write((byte)descriptor.PrimitiveTopology);
            writer.Write(descriptor.VertexAssembler.HasValue);
            if (descriptor.VertexAssembler.HasValue)
            {
                RHIVertexAssemblerDescriptor vertexAssembler =
                    descriptor.VertexAssembler.Value;
                WriteFunction(writer, vertexAssembler.VertexFunction, "vertex");
                Span<RHIVertexLayoutDescriptor> layouts =
                    vertexAssembler.VertexLayouts.Span;
                writer.Write(layouts.Length);
                for (int layoutIndex = 0; layoutIndex < layouts.Length; ++layoutIndex)
                {
                    ref RHIVertexLayoutDescriptor layout = ref layouts[layoutIndex];
                    writer.Write(layout.Index);
                    writer.Write(layout.Stride);
                    writer.Write(layout.StepRate);
                    writer.Write((byte)layout.StepMode);
                    Span<RHIVertexElementDescriptor> elements =
                        layout.VertexElements.Span;
                    writer.Write(elements.Length);
                    for (int elementIndex = 0;
                         elementIndex < elements.Length;
                         ++elementIndex)
                    {
                        ref RHIVertexElementDescriptor element =
                            ref elements[elementIndex];
                        writer.Write(element.Slot);
                        writer.Write(element.Offset);
                        writer.Write((byte)element.Type);
                        writer.Write((byte)element.Format);
                    }
                }
            }

            writer.Write(descriptor.MeshletAssembler.HasValue);
            if (descriptor.MeshletAssembler.HasValue)
            {
                RHIMeshletAssemblerDescriptor meshletAssembler =
                    descriptor.MeshletAssembler.Value;
                WriteFunction(writer, meshletAssembler.TaskFunction, "task");
                WriteFunction(writer, meshletAssembler.MeshFunction, "mesh");
            }
        }

        private static void WriteRenderState(
            BinaryWriter writer,
            in RHIRenderStateDescriptor descriptor)
        {
            writer.Write(descriptor.SampleMask.HasValue);
            if (descriptor.SampleMask.HasValue)
            {
                writer.Write(descriptor.SampleMask.Value);
            }

            writer.Write(descriptor.BlendState.AlphaToCoverage);
            writer.Write(descriptor.BlendState.IndependentBlend);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor0);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor1);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor2);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor3);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor4);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor5);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor6);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor7);

            RHIRasterizerStateDescriptor rasterizer = descriptor.RasterizerState;
            writer.Write((byte)rasterizer.FillMode);
            writer.Write((byte)rasterizer.CullMode);
            writer.Write(rasterizer.DepthClipEnable);
            writer.Write(rasterizer.ConservativeRaster);
            writer.Write(rasterizer.AntialiasedLineEnable);
            writer.Write(rasterizer.FrontCounterClockwise);
            writer.Write(rasterizer.DepthBias);
            writer.Write(rasterizer.DepthBiasClamp);
            writer.Write(rasterizer.SlopeScaledDepthBias);

            RHIDepthStencilStateDescriptor depthStencil =
                descriptor.DepthStencilState;
            writer.Write(depthStencil.DepthEnable);
            writer.Write(depthStencil.DepthWriteMask);
            writer.Write(depthStencil.StencilEnable);
            writer.Write(depthStencil.StencilReadMask);
            writer.Write(depthStencil.StencilWriteMask);
            writer.Write((byte)depthStencil.ComparisonMode);
            WriteStencil(writer, depthStencil.FrontFace);
            WriteStencil(writer, depthStencil.BackFace);
        }

        private static void WriteBlend(
            BinaryWriter writer,
            in RHIBlendDescriptor descriptor)
        {
            writer.Write(descriptor.BlendEnable);
            writer.Write((byte)descriptor.BlendOpColor);
            writer.Write((byte)descriptor.SrcBlendColor);
            writer.Write((byte)descriptor.DstBlendColor);
            writer.Write((byte)descriptor.BlendOpAlpha);
            writer.Write((byte)descriptor.SrcBlendAlpha);
            writer.Write((byte)descriptor.DstBlendAlpha);
            writer.Write((byte)descriptor.ColorWriteChannel);
        }

        private static void WriteStencil(
            BinaryWriter writer,
            in RHIStencilStateDescriptor descriptor)
        {
            writer.Write((byte)descriptor.StencilPassOp);
            writer.Write((byte)descriptor.StencilFailOp);
            writer.Write((byte)descriptor.StencilDepthFailOp);
            writer.Write((byte)descriptor.ComparisonMode);
        }
    }
    #endregion
}
