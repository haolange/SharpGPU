using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public struct RHIPipelineLayoutDescriptor
    {
        public bool bLocalSignature;
        public bool bUseVertexLayout;
        public uint PushConstantSize;
        public RHIArgumentTableLayout[] ArgumentTableLayouts;
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

    public readonly struct RHIAttachmentInterfaceSignature :
        IEquatable<RHIAttachmentInterfaceSignature>
    {
        public const int UnboundLogicalAttachment = -1;

        private const uint PackedUnboundAttachment = 0xFu;
        private const int BitsPerAttachmentSlot = 4;

        private readonly uint m_ColorInputs;
        private readonly uint m_ColorOutputs;
        private readonly uint m_SampledFeedbackInputs;

        public byte ColorAttachmentCount { get; }
        public byte ColorInputSlotCount { get; }
        public byte ColorOutputLocationCount { get; }
        public byte SampledFeedbackSlotCount { get; }
        public byte ColorInputMask { get; }
        public byte ColorOutputMask { get; }
        public byte RasterOrderedReadWriteMask { get; }
        public byte SampledFeedbackMask { get; }
        public byte LayeredAccessMask { get; }
        public ERHISubPassFlags DepthStencilFlags { get; }
        public bool UsesDualSourceColor { get; }

        public RHIAttachmentInterfaceSignature(
            int colorAttachmentCount,
            in RHIAttachmentIndexArray colorInputs,
            in RHIAttachmentIndexArray colorOutputs,
            in RHIAttachmentIndexArray sampledFeedbackInputs,
            byte rasterOrderedReadWriteMask = 0,
            ERHISubPassFlags depthStencilFlags = ERHISubPassFlags.None,
            bool usesDualSourceColor = false,
            byte layeredAccessMask = 0)
        {
            if ((uint)colorAttachmentCount > RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(colorAttachmentCount),
                    colorAttachmentCount,
                    $"Color attachment count must be in [0, {RHIAttachmentIndexArray.MaxAttachments}].");
            }

            uint packedColorInputs = PackOrderedSlots(
                colorInputs,
                colorAttachmentCount,
                nameof(colorInputs),
                out byte colorInputMask);
            uint packedColorOutputs = PackOrderedSlots(
                colorOutputs,
                colorAttachmentCount,
                nameof(colorOutputs),
                out byte colorOutputMask);
            uint packedSampledFeedbackInputs = PackOrderedSlots(
                sampledFeedbackInputs,
                colorAttachmentCount,
                nameof(sampledFeedbackInputs),
                out byte sampledFeedbackMask);
            if ((rasterOrderedReadWriteMask & ~(colorInputMask & colorOutputMask)) != 0)
            {
                throw new ArgumentException(
                    "RasterOrderedReadWrite entries must appear in both ColorInputMask and ColorOutputMask.",
                    nameof(rasterOrderedReadWriteMask));
            }
            if ((colorInputMask & sampledFeedbackMask) != 0)
            {
                throw new ArgumentException(
                    "An attachment cannot be both a local color input and SampledFeedback.");
            }
            byte specialAccessMask = checked((byte)(
                colorInputMask |
                rasterOrderedReadWriteMask |
                sampledFeedbackMask));
            if ((layeredAccessMask & ~specialAccessMask) != 0)
            {
                throw new ArgumentException(
                    "LayeredAccess entries must declare local input, RasterOrderedReadWrite, or SampledFeedback access.",
                    nameof(layeredAccessMask));
            }

            const ERHISubPassFlags knownDepthStencilFlags =
                ERHISubPassFlags.ReadOnlyDepthStencil;
            if ((depthStencilFlags & ~knownDepthStencilFlags) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(depthStencilFlags),
                    depthStencilFlags,
                    "Unknown subpass depth/stencil flags.");
            }
            if (usesDualSourceColor &&
                (colorOutputs.Length == 0 ||
                 GetPackedSlot(packedColorOutputs, 0) == UnboundLogicalAttachment))
            {
                throw new ArgumentException(
                    "Dual-source color output requires a bound output at location 0.",
                    nameof(usesDualSourceColor));
            }

            m_ColorInputs = packedColorInputs;
            m_ColorOutputs = packedColorOutputs;
            m_SampledFeedbackInputs = packedSampledFeedbackInputs;
            ColorAttachmentCount = checked((byte)colorAttachmentCount);
            ColorInputSlotCount = checked((byte)colorInputs.Length);
            ColorOutputLocationCount = checked((byte)colorOutputs.Length);
            SampledFeedbackSlotCount =
                checked((byte)sampledFeedbackInputs.Length);
            ColorInputMask = colorInputMask;
            ColorOutputMask = colorOutputMask;
            RasterOrderedReadWriteMask = rasterOrderedReadWriteMask;
            SampledFeedbackMask = sampledFeedbackMask;
            LayeredAccessMask = layeredAccessMask;
            DepthStencilFlags = depthStencilFlags;
            UsesDualSourceColor = usesDualSourceColor;
        }

        public int GetColorInputLogicalAttachment(int inputIndex)
        {
            ValidateOrderedSlot(
                inputIndex,
                ColorInputSlotCount,
                nameof(inputIndex));
            return GetPackedSlot(m_ColorInputs, inputIndex);
        }

        public int GetColorOutputLogicalAttachment(
            int outputLocation,
            int outputIndex = 0)
        {
            ValidateOrderedSlot(
                outputLocation,
                ColorOutputLocationCount,
                nameof(outputLocation));
            if ((uint)outputIndex > 1u)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputIndex),
                    outputIndex,
                    "Color output index must be 0 or 1.");
            }
            if (outputIndex == 1 &&
                (!UsesDualSourceColor || outputLocation != 0))
            {
                return UnboundLogicalAttachment;
            }
            return GetPackedSlot(m_ColorOutputs, outputLocation);
        }

        public int GetSampledFeedbackLogicalAttachment(
            int sampledFeedbackOrdinal)
        {
            ValidateOrderedSlot(
                sampledFeedbackOrdinal,
                SampledFeedbackSlotCount,
                nameof(sampledFeedbackOrdinal));
            return GetPackedSlot(
                m_SampledFeedbackInputs,
                sampledFeedbackOrdinal);
        }

        internal static int GetPrivateInputAttachmentBinding(int inputIndex)
        {
            ValidatePrivateBindingOrdinal(inputIndex, nameof(inputIndex));
            return inputIndex;
        }

        public bool Equals(RHIAttachmentInterfaceSignature other)
        {
            return ColorAttachmentCount == other.ColorAttachmentCount &&
                ColorInputSlotCount == other.ColorInputSlotCount &&
                ColorOutputLocationCount == other.ColorOutputLocationCount &&
                SampledFeedbackSlotCount == other.SampledFeedbackSlotCount &&
                m_ColorInputs == other.m_ColorInputs &&
                m_ColorOutputs == other.m_ColorOutputs &&
                m_SampledFeedbackInputs == other.m_SampledFeedbackInputs &&
                ColorInputMask == other.ColorInputMask &&
                ColorOutputMask == other.ColorOutputMask &&
                RasterOrderedReadWriteMask == other.RasterOrderedReadWriteMask &&
                SampledFeedbackMask == other.SampledFeedbackMask &&
                LayeredAccessMask == other.LayeredAccessMask &&
                DepthStencilFlags == other.DepthStencilFlags &&
                UsesDualSourceColor == other.UsesDualSourceColor;
        }

        public override bool Equals(object? obj) =>
            obj is RHIAttachmentInterfaceSignature other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(ColorAttachmentCount);
            hash.Add(ColorInputSlotCount);
            hash.Add(ColorOutputLocationCount);
            hash.Add(SampledFeedbackSlotCount);
            hash.Add(m_ColorInputs);
            hash.Add(m_ColorOutputs);
            hash.Add(m_SampledFeedbackInputs);
            hash.Add(RasterOrderedReadWriteMask);
            hash.Add(LayeredAccessMask);
            hash.Add(DepthStencilFlags);
            hash.Add(UsesDualSourceColor);
            return hash.ToHashCode();
        }

        public static bool operator ==(
            RHIAttachmentInterfaceSignature left,
            RHIAttachmentInterfaceSignature right) => left.Equals(right);

        public static bool operator !=(
            RHIAttachmentInterfaceSignature left,
            RHIAttachmentInterfaceSignature right) => !left.Equals(right);

        public override string ToString()
        {
            return $"Colors={ColorAttachmentCount}, " +
                $"InputSlots={ColorInputSlotCount}/0x{ColorInputMask:X2}, " +
                $"OutputLocations={ColorOutputLocationCount}/0x{ColorOutputMask:X2}, " +
                $"RORW=0x{RasterOrderedReadWriteMask:X2}, " +
                $"SampledFeedbackSlots={SampledFeedbackSlotCount}/0x{SampledFeedbackMask:X2}, " +
                $"Layered=0x{LayeredAccessMask:X2}, " +
                $"DS={DepthStencilFlags}, " +
                $"DualSource={UsesDualSourceColor}";
        }

        internal RHIAttachmentInterfaceSignature NormalizeForPipeline(
            int colorAttachmentCount,
            bool usesDualSourceColor)
        {
            if (Equals(default))
            {
                RHIAttachmentIndexArray outputs =
                    new RHIAttachmentIndexArray(colorAttachmentCount);
                for (int outputLocation = 0;
                     outputLocation < colorAttachmentCount;
                     ++outputLocation)
                {
                    outputs[outputLocation] = outputLocation;
                }
                return new RHIAttachmentInterfaceSignature(
                    colorAttachmentCount,
                    RHIAttachmentIndexArray.Empty,
                    outputs,
                    RHIAttachmentIndexArray.Empty,
                    usesDualSourceColor: usesDualSourceColor);
            }
            if (ColorAttachmentCount != colorAttachmentCount)
            {
                throw new ArgumentException(
                    $"Attachment interface declares {ColorAttachmentCount} color attachments, " +
                    $"but the pipeline declares {colorAttachmentCount} formats.");
            }
            if (UsesDualSourceColor != usesDualSourceColor)
            {
                throw new ArgumentException(
                    "Attachment interface dual-source declaration does not match the blend state.");
            }
            return this;
        }

        internal bool IsPassCompatibleWith(
            in RHIAttachmentInterfaceSignature pipelineSignature)
        {
            return ColorAttachmentCount == pipelineSignature.ColorAttachmentCount &&
                ColorInputSlotCount == pipelineSignature.ColorInputSlotCount &&
                ColorOutputLocationCount ==
                    pipelineSignature.ColorOutputLocationCount &&
                SampledFeedbackSlotCount ==
                    pipelineSignature.SampledFeedbackSlotCount &&
                m_ColorInputs == pipelineSignature.m_ColorInputs &&
                m_ColorOutputs == pipelineSignature.m_ColorOutputs &&
                m_SampledFeedbackInputs ==
                    pipelineSignature.m_SampledFeedbackInputs &&
                RasterOrderedReadWriteMask == pipelineSignature.RasterOrderedReadWriteMask &&
                LayeredAccessMask == pipelineSignature.LayeredAccessMask &&
                DepthStencilFlags == pipelineSignature.DepthStencilFlags &&
                (!pipelineSignature.UsesDualSourceColor ||
                 (ColorOutputLocationCount != 0 &&
                  GetPackedSlot(m_ColorOutputs, 0) !=
                      UnboundLogicalAttachment));
        }

        internal static byte CreateDeclaredMask(int colorAttachmentCount)
        {
            return colorAttachmentCount == RHIAttachmentIndexArray.MaxAttachments
                ? byte.MaxValue
                : checked((byte)((1 << colorAttachmentCount) - 1));
        }

        private static uint PackOrderedSlots(
            in RHIAttachmentIndexArray slots,
            int colorAttachmentCount,
            string parameterName,
            out byte attachmentMask)
        {
            uint packed = 0;
            byte mask = 0;
            for (int slot = 0; slot < slots.Length; ++slot)
            {
                int logicalAttachment = slots[slot];
                uint packedAttachment;
                if (logicalAttachment == UnboundLogicalAttachment)
                {
                    packedAttachment = PackedUnboundAttachment;
                }
                else
                {
                    if ((uint)logicalAttachment >= colorAttachmentCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            parameterName,
                            logicalAttachment,
                            $"Logical attachment at ordered slot {slot} must be -1 or in [0, {colorAttachmentCount}).");
                    }
                    byte attachmentBit =
                        checked((byte)(1 << logicalAttachment));
                    if ((mask & attachmentBit) != 0)
                    {
                        throw new ArgumentException(
                            $"{parameterName} maps logical attachment {logicalAttachment} more than once.",
                            parameterName);
                    }
                    mask |= attachmentBit;
                    packedAttachment = checked((uint)logicalAttachment);
                }
                packed |= packedAttachment << (slot * BitsPerAttachmentSlot);
            }
            attachmentMask = mask;
            return packed;
        }

        private static int GetPackedSlot(uint packed, int slot)
        {
            uint value =
                (packed >> (slot * BitsPerAttachmentSlot)) &
                PackedUnboundAttachment;
            return value == PackedUnboundAttachment
                ? UnboundLogicalAttachment
                : checked((int)value);
        }

        private static void ValidateOrderedSlot(
            int slot,
            int slotCount,
            string parameterName)
        {
            if ((uint)slot >= slotCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    slot,
                    $"Ordered attachment slot must be in [0, {slotCount}).");
            }
        }

        private static void ValidatePrivateBindingOrdinal(
            int ordinal,
            string parameterName)
        {
            if ((uint)ordinal >= RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    ordinal,
                    $"Private attachment binding ordinal must be in [0, {RHIAttachmentIndexArray.MaxAttachments}).");
            }
        }
    }

    public struct RHIRasterPipelineDescriptor
    {
        public ERHISampleCount SampleCount;
        public ERHIPixelFormat DepthFormat;
        public ERHIPixelFormat[] ColorFormats;
        public RHIAttachmentInterfaceSignature AttachmentInterface;
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
            return snapshot;
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

        private static RHIBlendDescriptor GetBlendDescriptor(
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
}
