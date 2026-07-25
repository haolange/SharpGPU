using System;

namespace SharpGPU
{
    internal enum EDx12RasterPassStrategy
    {
        NativeRenderPass = 0,
        OmMultipass = 1,
    }

    internal readonly struct Dx12RasterSubPassLowering
    {
        internal byte RenderTargetMask { get; }
        internal byte ShaderResourceMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PrivateShaderResourceMask { get; }
        internal byte PreserveMask { get; }
        internal byte TransitionMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool RequiresPrivateAttachmentTable =>
            PrivateShaderResourceMask != 0 ||
            RasterOrderedMask != 0;

        internal ReadOnlyMemory<int> RenderTargetLogicalAttachments =>
            m_RenderTargetLogicalAttachments;
        internal ReadOnlyMemory<int> OutputLogicalAttachments =>
            m_OutputLogicalAttachments;
        internal ReadOnlyMemory<int> PrivateInputLogicalAttachments =>
            m_PrivateInputLogicalAttachments;
        internal ReadOnlyMemory<int> SampledFeedbackLogicalAttachments =>
            m_SampledFeedbackLogicalAttachments;
        internal ReadOnlyMemory<ERHIPixelFormat> OutputLocationFormats =>
            m_OutputLocationFormats;
        internal bool HasSparseOutputLocations { get; }

        private readonly int[] m_RenderTargetLogicalAttachments;
        private readonly int[] m_OutputLogicalAttachments;
        private readonly int[] m_PrivateInputLogicalAttachments;
        private readonly int[] m_SampledFeedbackLogicalAttachments;
        private readonly ERHIPixelFormat[] m_OutputLocationFormats;

        internal Dx12RasterSubPassLowering(
            in RasterSubPassPlan plan,
            ReadOnlySpan<ERHIPixelFormat> logicalAttachmentFormats)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            RenderTargetMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~rasterOrderedMask));
            ShaderResourceMask = checked((byte)(
                (attachmentInterface.ColorInputMask |
                 attachmentInterface.SampledFeedbackMask) &
                ~rasterOrderedMask));
            RasterOrderedMask = rasterOrderedMask;
            PrivateShaderResourceMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~rasterOrderedMask));
            PreserveMask = plan.PreserveMask;
            TransitionMask = plan.TransitionMask;
            DepthStencilFlags = attachmentInterface.DepthStencilFlags;

            m_RenderTargetLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            m_OutputLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            bool hasSparseOutputLocations = false;
            for (int outputLocation = 0;
                 outputLocation < m_RenderTargetLogicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                m_OutputLogicalAttachments[outputLocation] =
                    logicalAttachment;
                hasSparseOutputLocations |=
                    logicalAttachment ==
                    RHIAttachmentInterfaceSignature
                        .UnboundLogicalAttachment;
                if (logicalAttachment >= 0 &&
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_RenderTargetLogicalAttachments[outputLocation] =
                    logicalAttachment;
            }
            HasSparseOutputLocations = hasSparseOutputLocations;
            m_OutputLocationFormats = ResolveOutputLocationFormats(
                in attachmentInterface,
                logicalAttachmentFormats);

            m_PrivateInputLogicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            for (int inputIndex = 0;
                 inputIndex < m_PrivateInputLogicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment >= 0 &&
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_PrivateInputLogicalAttachments[inputIndex] =
                    logicalAttachment;
            }

            m_SampledFeedbackLogicalAttachments =
                new int[attachmentInterface.SampledFeedbackSlotCount];
            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    m_SampledFeedbackLogicalAttachments.Length;
                 ++sampledOrdinal)
            {
                m_SampledFeedbackLogicalAttachments[sampledOrdinal] =
                    attachmentInterface
                        .GetSampledFeedbackLogicalAttachment(
                            sampledOrdinal);
            }
        }

        internal int GetRenderTargetLogicalAttachment(
            int outputLocation) =>
            m_RenderTargetLogicalAttachments[outputLocation];

        internal int GetOutputLogicalAttachment(int outputLocation) =>
            m_OutputLogicalAttachments[outputLocation];

        internal int GetPrivateInputLogicalAttachment(int inputIndex) =>
            m_PrivateInputLogicalAttachments[inputIndex];

        internal int GetSampledFeedbackLogicalAttachment(
            int sampledOrdinal) =>
            m_SampledFeedbackLogicalAttachments[sampledOrdinal];

        internal bool HasIdentityOutputMapping(int logicalAttachmentCount)
        {
            if (m_OutputLogicalAttachments.Length !=
                logicalAttachmentCount)
            {
                return false;
            }

            for (int outputLocation = 0;
                 outputLocation < m_OutputLogicalAttachments.Length;
                 ++outputLocation)
            {
                if (m_OutputLogicalAttachments[outputLocation] !=
                    outputLocation)
                {
                    return false;
                }
            }
            return true;
        }

        internal static ERHIPixelFormat[] ResolveOutputLocationFormats(
            in RHIAttachmentInterfaceSignature attachmentInterface,
            ReadOnlySpan<ERHIPixelFormat> logicalAttachmentFormats)
        {
            if (logicalAttachmentFormats.Length !=
                attachmentInterface.ColorAttachmentCount)
            {
                throw new ArgumentException(
                    "DX12 output format lowering requires one format per " +
                    "logical color attachment.",
                    nameof(logicalAttachmentFormats));
            }

            int outputLocationCount =
                attachmentInterface.ColorOutputLocationCount;
            if (outputLocationCount == 0)
            {
                return Array.Empty<ERHIPixelFormat>();
            }

            int anchorLogicalAttachment =
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment;
            for (int outputLocation = 0;
                 outputLocation < outputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (logicalAttachment >= 0)
                {
                    anchorLogicalAttachment = logicalAttachment;
                    break;
                }
            }

            if (anchorLogicalAttachment < 0)
            {
                throw new ArgumentException(
                    "A non-empty DX12 output-location interface must bind " +
                    "at least one logical color attachment.",
                    nameof(attachmentInterface));
            }

            ERHIPixelFormat anchorFormat =
                logicalAttachmentFormats[anchorLogicalAttachment];
            ValidateOutputFormat(anchorFormat, anchorLogicalAttachment);
            ERHIPixelFormat[] outputFormats =
                new ERHIPixelFormat[outputLocationCount];
            for (int outputLocation = 0;
                 outputLocation < outputFormats.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                ERHIPixelFormat format = logicalAttachment >= 0
                    ? logicalAttachmentFormats[logicalAttachment]
                    : anchorFormat;
                ValidateOutputFormat(
                    format,
                    logicalAttachment >= 0
                        ? logicalAttachment
                        : anchorLogicalAttachment);
                outputFormats[outputLocation] = format;
            }
            return outputFormats;
        }

        private static void ValidateOutputFormat(
            ERHIPixelFormat format,
            int logicalAttachment)
        {
            if (format == ERHIPixelFormat.Unknown)
            {
                throw new ArgumentException(
                    $"DX12 logical color attachment {logicalAttachment} " +
                    "has an unknown output format.");
            }
        }
    }

    internal sealed class Dx12RasterPassLowering
    {
        internal const int PrivateInputDescriptorCount =
            RHIAttachmentIndexArray.MaxAttachments;
        internal const int RasterOrderedDescriptorCount =
            RHIAttachmentIndexArray.MaxAttachments;
        internal const int PrivateDescriptorCountPerSubPass =
            PrivateInputDescriptorCount +
            RasterOrderedDescriptorCount;
        internal EDx12RasterPassStrategy Strategy { get; }
        internal bool UsesEnhancedBarriers { get; }
        internal bool RequiresPrivateAttachmentTable { get; }
        internal ReadOnlyMemory<Dx12RasterSubPassLowering> SubPasses =>
            m_SubPasses;

        private readonly Dx12RasterSubPassLowering[] m_SubPasses;

        private Dx12RasterPassLowering(
            EDx12RasterPassStrategy strategy,
            bool usesEnhancedBarriers,
            bool requiresPrivateAttachmentTable,
            Dx12RasterSubPassLowering[] subPasses)
        {
            Strategy = strategy;
            UsesEnhancedBarriers = usesEnhancedBarriers;
            RequiresPrivateAttachmentTable =
                requiresPrivateAttachmentTable;
            m_SubPasses = subPasses;
        }

        internal static Dx12RasterPassLowering Compile(
            RasterPassPlan plan,
            bool supportsNativeRenderPass,
            bool supportsRasterOrderedViews,
            bool usesEnhancedBarriers)
        {
            ArgumentNullException.ThrowIfNull(plan);

            bool hasAttachmentReads = false;
            bool hasRasterOrderedAccess = false;
            bool requiresPrivateAttachmentTable = false;
            Span<ERHIPixelFormat> logicalAttachmentFormats =
                stackalloc ERHIPixelFormat[
                    RHIAttachmentIndexArray.MaxAttachments];
            for (int attachmentIndex = 0;
                 attachmentIndex < plan.ColorAttachmentCount;
                 ++attachmentIndex)
            {
                logicalAttachmentFormats[attachmentIndex] =
                    plan.GetColorAttachment(attachmentIndex)
                        .RenderTarget.Descriptor.Format;
            }
            Dx12RasterSubPassLowering[] subPasses =
                new Dx12RasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                ValidateAccessModel(in subPass, i);
                Dx12RasterSubPassLowering lowering =
                    new Dx12RasterSubPassLowering(
                        subPass,
                        logicalAttachmentFormats.Slice(
                            0,
                            plan.ColorAttachmentCount));
                subPasses[i] = lowering;
                hasAttachmentReads |=
                    lowering.ShaderResourceMask != 0 ||
                    lowering.RasterOrderedMask != 0;
                hasRasterOrderedAccess |= lowering.RasterOrderedMask != 0;
                requiresPrivateAttachmentTable |=
                    lowering.PrivateShaderResourceMask != 0 ||
                    lowering.RasterOrderedMask != 0;
            }

            if (hasRasterOrderedAccess && !supportsRasterOrderedViews)
            {
                throw new NotSupportedException(
                    "The raster pass requires DirectX 12 rasterizer-ordered " +
                    "views, but the current adapter does not support them.");
            }
            if (hasRasterOrderedAccess &&
                plan.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException(
                    "The DX12 attachment ABI exposes exact " +
                    "RasterizerOrderedTexture2D/Texture2DArray access only; " +
                    "multisampled RasterOrderedReadWrite is unsupported.");
            }

            EDx12RasterPassStrategy strategy =
                supportsNativeRenderPass &&
                plan.SubPassCount == 1 &&
                !hasAttachmentReads &&
                !subPasses[0].HasSparseOutputLocations &&
                subPasses[0].HasIdentityOutputMapping(
                    plan.ColorAttachmentCount)
                    ? EDx12RasterPassStrategy.NativeRenderPass
                    : EDx12RasterPassStrategy.OmMultipass;

            return new Dx12RasterPassLowering(
                strategy,
                usesEnhancedBarriers,
                requiresPrivateAttachmentTable,
                subPasses);
        }

        internal static int GetPrivateDescriptorTableOffset(
            int subPassIndex)
        {
            if (subPassIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(subPassIndex));
            }
            return checked(
                subPassIndex *
                PrivateDescriptorCountPerSubPass);
        }

        internal static int GetPrivateInputDescriptorOffset(
            int subPassIndex,
            int inputIndex)
        {
            ValidatePrivateOrdinal(inputIndex, nameof(inputIndex));
            return checked(
                GetPrivateDescriptorTableOffset(subPassIndex) +
                inputIndex);
        }

        internal static int GetRasterOrderedDescriptorOffset(
            int subPassIndex,
            int logicalAttachment)
        {
            ValidatePrivateOrdinal(
                logicalAttachment,
                nameof(logicalAttachment));
            return checked(
                GetPrivateDescriptorTableOffset(subPassIndex) +
                PrivateInputDescriptorCount +
                logicalAttachment);
        }

        private static void ValidatePrivateOrdinal(
            int ordinal,
            string parameterName)
        {
            if ((uint)ordinal >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    ordinal,
                    $"DX12 private raster descriptor ordinals must be in " +
                    $"[0, {RHIAttachmentIndexArray.MaxAttachments - 1}].");
            }
        }

        private static void ValidateAccessModel(
            in RasterSubPassPlan subPass,
            int subPassIndex)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                subPass.AttachmentInterface;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0)
                {
                    continue;
                }

                byte bit = checked((byte)(1 << logicalAttachment));
                bool isOutput =
                    (attachmentInterface.ColorOutputMask & bit) != 0;
                bool isRasterOrdered =
                    (attachmentInterface.RasterOrderedReadWriteMask &
                     bit) != 0;
                if (isOutput && !isRasterOrdered)
                {
                    throw new NotSupportedException(
                        $"DX12 subpass {subPassIndex} reads and writes logical " +
                        $"attachment {logicalAttachment} in one phase without " +
                        "the exact RasterOrderedReadWrite qualifier.");
                }
            }

            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    attachmentInterface.SampledFeedbackSlotCount;
                 ++sampledOrdinal)
            {
                int logicalAttachment =
                    attachmentInterface
                        .GetSampledFeedbackLogicalAttachment(
                            sampledOrdinal);
                if (logicalAttachment < 0)
                {
                    continue;
                }
                byte bit = checked((byte)(1 << logicalAttachment));
                if ((attachmentInterface.ColorOutputMask & bit) != 0)
                {
                    throw new NotSupportedException(
                        $"DX12 subpass {subPassIndex} cannot sample and render " +
                        $"to logical attachment {logicalAttachment} in one " +
                        "phase. Use a phase boundary or an exact " +
                        "RasterOrderedReadWrite attachment.");
                }
            }
        }
    }
}
