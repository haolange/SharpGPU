using System;
using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using SharpGPU.Core;
using SharpGPU.Mathematics;

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
    /// Public RHI ML surface is Binary ??Pipeline ??BindingSet ??Encoder only (ADR-0052).
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

    #region MLProgramIR
    /// <summary>
    /// Backend-private ML operator kinds used by cook tools and internal program IR.
    /// Not part of the RHI public surface (ADR-0052).
    /// </summary>
    internal enum ERHIMLOpKind : ushort
    {
        Unknown = 0,

        ElementWiseAdd = 1,
        ElementWiseSubtract = 2,
        ElementWiseMultiply = 3,
        ElementWiseDivide = 4,
        ElementWiseNegate = 5,

        ActivationRelu = 10,
        ActivationSigmoid = 11,
        ActivationTanh = 12,

        MatrixMultiply = 20,
        GeneralMatrixMultiply = 21,

        ActivationSoftmax = 30,
        MeanVarianceNormalization = 31,
        ReduceMean = 32,

        Reshape = 40,
        Transpose = 41,

        ElementWiseIdentity = 50,
    }

    internal enum ERHIMLMatrixTransform : byte
    {
        None = 0,
        Transpose = 1,
    }

    internal enum ERHIMLFusedActivation : byte
    {
        None = 0,
        Relu = 1,
        Sigmoid = 2,
        Tanh = 3,
    }

    internal struct RHIMLOpTensorRef
    {
        public int InputIndex;
        public int OpIndex;
        public bool IsOpOutput;

        public static RHIMLOpTensorRef FromInput(int inputIndex)
        {
            return new RHIMLOpTensorRef { InputIndex = inputIndex, OpIndex = -1, IsOpOutput = false };
        }

        public static RHIMLOpTensorRef FromOpOutput(int opIndex)
        {
            if (opIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(opIndex), "Op index must be non-negative.");
            }

            return new RHIMLOpTensorRef { InputIndex = -1, OpIndex = opIndex, IsOpOutput = true };
        }

        public readonly override string ToString() => IsOpOutput ? $"op[{OpIndex}].out" : $"input[{InputIndex}]";
    }

    internal struct RHIMLOpDescriptor
    {
        public ERHIMLOpKind Kind;
        public RHIMLOpTensorRef[] Inputs;
        public RHIMLTensorDescriptor Output;
        public string Name;

        public float Alpha;
        public float Beta;
        public ERHIMLMatrixTransform TransformA;
        public ERHIMLMatrixTransform TransformB;
        public ERHIMLFusedActivation FusedActivation;
        public float Epsilon;
        public int[]? Axes;

        public static RHIMLOpDescriptor Create(ERHIMLOpKind kind, RHIMLOpTensorRef[] inputs, in RHIMLTensorDescriptor output, string? name = null)
        {
            return new RHIMLOpDescriptor
            {
                Kind = kind,
                Inputs = inputs ?? Array.Empty<RHIMLOpTensorRef>(),
                Output = output,
                Name = name ?? string.Empty,
                Alpha = 1.0f,
                Beta = 1.0f,
                TransformA = ERHIMLMatrixTransform.None,
                TransformB = ERHIMLMatrixTransform.None,
                FusedActivation = ERHIMLFusedActivation.None,
                Epsilon = 0.0f,
                Axes = null,
            };
        }
    }

    /// <summary>
    /// Backend-private ordered op-sequence program IR. Cook tools serialize this into
    /// <see cref="RHIMLBinary"/> payloads; runtime never exposes it publicly (ADR-0052).
    /// </summary>
    internal struct RHIMLProgramIR
    {
        public string Name;
        public RHIMLTensorDescriptor[] Inputs;
        public RHIMLTensorDescriptor[] Outputs;
        public RHIMLOpDescriptor[] Ops;

        public static RHIMLProgramIR Create(string name, RHIMLTensorDescriptor[] inputs, RHIMLTensorDescriptor[] outputs, RHIMLOpDescriptor[] ops)
        {
            return new RHIMLProgramIR
            {
                Name = name,
                Inputs = inputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Outputs = outputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Ops = ops ?? Array.Empty<RHIMLOpDescriptor>(),
            };
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

    #region MLBinaryLoader
    internal static class RHIMLBinaryLoader
    {
        internal static RHIMLBinary Load(ReadOnlyMemory<byte> container)
        {
            ReadOnlySpan<byte> bytes = container.Span;
            if (bytes.Length < 5)
            {
                throw new InvalidOperationException("ML binary container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            return magic switch
            {
                Dx12MlBinaryCodec.Magic => LoadDirectMl(container),
                MetalMlBinaryCodec.Magic => LoadMetalPackage(container),
                _ => throw new InvalidOperationException($"Unknown ML binary magic 0x{magic:X8}."),
            };
        }

        private static RHIMLBinary LoadDirectMl(ReadOnlyMemory<byte> container)
        {
            Dx12MlBinaryCodec.ValidateContainer(container, ERHIMLBinaryFormat.DirectMLProgramV1);
            RHIMLBinaryReflection reflection = Dx12MlBinaryCodec.ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.DirectMLProgramV1,
                container,
                reflection,
                contentHash);
        }

        private static RHIMLBinary LoadMetalPackage(ReadOnlyMemory<byte> container)
        {
            RHIMLBinaryReflection reflection = MetalMlBinaryCodec.ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.MetalPackageV1,
                container,
                reflection,
                contentHash);
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
        public const uint CurrentSchemaRevision = 1;
        public const uint CurrentPipelineAbiRevision = 4;

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
            writer.Write(attachment.SampledFeedbackSlotCount);
            for (int sampledOrdinal = 0;
                 sampledOrdinal < attachment.SampledFeedbackSlotCount;
                 ++sampledOrdinal)
            {
                writer.Write(
                    attachment.GetSampledFeedbackLogicalAttachment(sampledOrdinal));
            }
            writer.Write(attachment.RasterOrderedReadWriteMask);
            writer.Write(attachment.LayeredAccessMask);
            writer.Write((byte)attachment.DepthStencilFlags);
            writer.Write(attachment.UsesDualSourceColor);

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

            RHIFunctionDescriptor descriptor = function.Descriptor;
            writer.Write((byte)descriptor.Type);
            writer.Write((byte)descriptor.PayloadKind);
            writer.Write(descriptor.EntryName ?? string.Empty);
            writer.Write(descriptor.ByteSize);
            if (descriptor.ByteSize == 0 || descriptor.ByteCode == IntPtr.Zero)
            {
                throw new ArgumentException(
                    $"The {role} shader function has no bytecode.",
                    nameof(function));
            }
            if (descriptor.ByteSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(function),
                    $"The {role} shader function is larger than the supported cache-key input.");
            }

            ReadOnlySpan<byte> byteCode = new ReadOnlySpan<byte>(
                descriptor.ByteCode.ToPointer(),
                checked((int)descriptor.ByteSize));
            writer.Write(SHA256.HashData(byteCode));
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
