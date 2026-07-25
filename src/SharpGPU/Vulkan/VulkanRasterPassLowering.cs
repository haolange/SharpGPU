using System;

namespace SharpGPU
{
    internal enum EVulkanRasterPassStrategy
    {
        DynamicRendering = 0,
        DynamicRenderingLocalRead = 1,
        NativeRenderPass2 = 2,
    }

    internal enum EVulkanRasterPassForcedStrategy
    {
        Auto = 0,
        DynamicRenderingLocalRead = 1,
        NativeRenderPass2 = 2,
    }

    internal enum EVulkanRasterAttachmentScopeLayout : byte
    {
        ColorAttachment = 0,
        RenderingLocalRead = 1,
        AttachmentFeedbackLoop = 2,
        GeneralStorage = 3,
    }

    [Flags]
    internal enum EVulkanRasterPhaseBarrierKind : byte
    {
        None = 0,
        FramebufferByRegion = 1 << 0,
        FeedbackLoopByRegion = 1 << 1,
        OrderedStorageByRegion = 1 << 2,
    }

    internal readonly struct VulkanRasterCapabilities
    {
        internal bool DynamicRendering { get; }
        internal bool DynamicRenderingLocalRead { get; }
        internal bool DynamicRenderingLocalReadDepthStencil { get; }
        internal bool DynamicRenderingLocalReadMultisampled { get; }
        internal bool RenderPass2 { get; }
        internal bool AttachmentFeedbackLoopLayout { get; }
        internal bool OrderedFragmentPixelInterlock { get; }
        internal bool FragmentStoresAndAtomics { get; }
        internal bool UnifiedImageLayouts { get; }

        internal VulkanRasterCapabilities(
            bool dynamicRendering,
            bool dynamicRenderingLocalRead,
            bool dynamicRenderingLocalReadDepthStencil,
            bool dynamicRenderingLocalReadMultisampled,
            bool renderPass2,
            bool attachmentFeedbackLoopLayout,
            bool orderedFragmentPixelInterlock,
            bool fragmentStoresAndAtomics,
            bool unifiedImageLayouts)
        {
            DynamicRendering = dynamicRendering;
            DynamicRenderingLocalRead = dynamicRenderingLocalRead;
            DynamicRenderingLocalReadDepthStencil =
                dynamicRenderingLocalReadDepthStencil;
            DynamicRenderingLocalReadMultisampled =
                dynamicRenderingLocalReadMultisampled;
            RenderPass2 = renderPass2;
            AttachmentFeedbackLoopLayout =
                attachmentFeedbackLoopLayout;
            OrderedFragmentPixelInterlock =
                orderedFragmentPixelInterlock;
            FragmentStoresAndAtomics = fragmentStoresAndAtomics;
            UnifiedImageLayouts = unifiedImageLayouts;
        }
    }

    internal readonly struct VulkanRasterPhaseBarrierPlan
    {
        internal EVulkanRasterPhaseBarrierKind Kind { get; }
        internal byte AttachmentMask { get; }
        internal bool IsByRegion =>
            Kind != EVulkanRasterPhaseBarrierKind.None;
        internal bool PerformsLayoutTransition => false;
        internal bool PerformsQueueFamilyTransfer => false;

        internal VulkanRasterPhaseBarrierPlan(
            EVulkanRasterPhaseBarrierKind kind,
            byte attachmentMask)
        {
            Kind = kind;
            AttachmentMask = attachmentMask;
        }
    }

    internal readonly struct VulkanRasterSubPassLowering
    {
        internal byte LocalInputMask { get; }
        internal byte SampledInputMask { get; }
        internal byte OutputMask { get; }
        internal byte DeclaredOutputMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PreserveMask { get; }
        internal byte TransitionMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool HasNonIdentityOutputMapping { get; }
        internal bool RequiresFeedbackLoopLayout =>
            SampledInputMask != 0;
        internal int ColorInputSlotCount =>
            m_ColorInputLogicalAttachments.Length;
        internal int ColorOutputLocationCount =>
            m_ColorOutputLogicalAttachments.Length;
        internal int SampledFeedbackSlotCount =>
            m_SampledFeedbackLogicalAttachments.Length;

        private readonly int[] m_ColorInputLogicalAttachments;
        private readonly int[] m_ColorOutputLogicalAttachments;
        private readonly int[] m_SampledFeedbackLogicalAttachments;
        private readonly int[] m_OutputLocationsByPhysicalAttachment;
        private readonly int[] m_InputIndicesByPhysicalAttachment;

        internal VulkanRasterSubPassLowering(
            in RasterSubPassPlan plan)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            RasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            LocalInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~RasterOrderedMask));
            SampledInputMask =
                attachmentInterface.SampledFeedbackMask;
            DeclaredOutputMask =
                attachmentInterface.ColorOutputMask;
            OutputMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~RasterOrderedMask));
            PreserveMask = plan.PreserveMask;
            TransitionMask = plan.TransitionMask;
            DepthStencilFlags =
                attachmentInterface.DepthStencilFlags;

            m_ColorInputLogicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            m_ColorOutputLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            m_SampledFeedbackLogicalAttachments =
                new int[attachmentInterface.SampledFeedbackSlotCount];
            m_OutputLocationsByPhysicalAttachment =
                new int[attachmentInterface.ColorAttachmentCount];
            m_InputIndicesByPhysicalAttachment =
                new int[attachmentInterface.ColorAttachmentCount];
            Array.Fill(
                m_OutputLocationsByPhysicalAttachment,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            Array.Fill(
                m_InputIndicesByPhysicalAttachment,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);

            for (int inputIndex = 0;
                 inputIndex <
                    m_ColorInputLogicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (IsRasterOrdered(
                        logicalAttachment,
                        RasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_ColorInputLogicalAttachments[inputIndex] =
                    logicalAttachment;
                if (logicalAttachment >= 0)
                {
                    m_InputIndicesByPhysicalAttachment[
                        logicalAttachment] = inputIndex;
                }
            }

            bool hasNonIdentityOutputMapping = false;
            for (int outputLocation = 0;
                 outputLocation <
                    m_ColorOutputLogicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (IsRasterOrdered(
                        logicalAttachment,
                        RasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_ColorOutputLogicalAttachments[outputLocation] =
                    logicalAttachment;
                hasNonIdentityOutputMapping |=
                    logicalAttachment != outputLocation;
                if (logicalAttachment >= 0)
                {
                    m_OutputLocationsByPhysicalAttachment[
                        logicalAttachment] = outputLocation;
                }
            }
            HasNonIdentityOutputMapping =
                hasNonIdentityOutputMapping;

            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    m_SampledFeedbackLogicalAttachments.Length;
                 ++sampledOrdinal)
            {
                m_SampledFeedbackLogicalAttachments[
                    sampledOrdinal] =
                        attachmentInterface
                            .GetSampledFeedbackLogicalAttachment(
                                sampledOrdinal);
            }
        }

        internal int GetColorInputLogicalAttachment(int inputIndex)
        {
            if ((uint)inputIndex >=
                (uint)m_ColorInputLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return m_ColorInputLogicalAttachments[inputIndex];
        }

        internal int GetColorOutputLogicalAttachment(
            int outputLocation)
        {
            if ((uint)outputLocation >=
                (uint)m_ColorOutputLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputLocation));
            }
            return m_ColorOutputLogicalAttachments[outputLocation];
        }

        internal int GetSampledFeedbackLogicalAttachment(
            int sampledOrdinal)
        {
            if ((uint)sampledOrdinal >=
                (uint)m_SampledFeedbackLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampledOrdinal));
            }
            return m_SampledFeedbackLogicalAttachments[
                sampledOrdinal];
        }

        internal int GetOutputLocationForPhysicalAttachment(
            int physicalAttachment)
        {
            if ((uint)physicalAttachment >=
                (uint)m_OutputLocationsByPhysicalAttachment.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physicalAttachment));
            }
            return m_OutputLocationsByPhysicalAttachment[
                physicalAttachment];
        }

        internal int GetInputIndexForPhysicalAttachment(
            int physicalAttachment)
        {
            if ((uint)physicalAttachment >=
                (uint)m_InputIndicesByPhysicalAttachment.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physicalAttachment));
            }
            return m_InputIndicesByPhysicalAttachment[
                physicalAttachment];
        }

        private static bool IsRasterOrdered(
            int logicalAttachment,
            byte rasterOrderedMask)
        {
            return logicalAttachment >= 0 &&
                (rasterOrderedMask &
                 (1 << logicalAttachment)) != 0;
        }
    }

    internal sealed class VulkanRasterPassLowering
    {
        internal EVulkanRasterPassStrategy Strategy { get; }
        internal bool UsesRenderPass2 =>
            Strategy == EVulkanRasterPassStrategy.NativeRenderPass2;
        internal bool UsesDynamicRenderingLocalRead =>
            Strategy ==
                EVulkanRasterPassStrategy.DynamicRenderingLocalRead;
        internal bool RequiresPrivateAttachmentTable { get; }
        internal bool UsesAttachmentFeedbackLoopLayout { get; }
        internal bool UsesSampledFeedback { get; }
        internal bool UsesOrderedFragmentInterlock { get; }
        internal ReadOnlyMemory<VulkanRasterSubPassLowering> SubPasses =>
            m_SubPasses;
        internal ReadOnlyMemory<VulkanRasterPhaseBarrierPlan>
            PhaseBarriers => m_PhaseBarriers;
        internal ReadOnlyMemory<EVulkanRasterAttachmentScopeLayout>
            AttachmentScopeLayouts => m_AttachmentScopeLayouts;

        private readonly VulkanRasterSubPassLowering[] m_SubPasses;
        private readonly VulkanRasterPhaseBarrierPlan[] m_PhaseBarriers;
        private readonly EVulkanRasterAttachmentScopeLayout[]
            m_AttachmentScopeLayouts;

        private VulkanRasterPassLowering(
            EVulkanRasterPassStrategy strategy,
            bool requiresPrivateAttachmentTable,
            bool usesAttachmentFeedbackLoopLayout,
            bool usesSampledFeedback,
            bool usesOrderedFragmentInterlock,
            VulkanRasterSubPassLowering[] subPasses,
            VulkanRasterPhaseBarrierPlan[] phaseBarriers,
            EVulkanRasterAttachmentScopeLayout[]
                attachmentScopeLayouts)
        {
            Strategy = strategy;
            RequiresPrivateAttachmentTable =
                requiresPrivateAttachmentTable;
            UsesAttachmentFeedbackLoopLayout =
                usesAttachmentFeedbackLoopLayout;
            UsesSampledFeedback = usesSampledFeedback;
            UsesOrderedFragmentInterlock =
                usesOrderedFragmentInterlock;
            m_SubPasses = subPasses;
            m_PhaseBarriers = phaseBarriers;
            m_AttachmentScopeLayouts = attachmentScopeLayouts;
        }

        internal static VulkanRasterPassLowering Compile(
            RasterPassPlan plan,
            in VulkanRasterCapabilities capabilities,
            EVulkanRasterPassForcedStrategy forcedStrategy =
                EVulkanRasterPassForcedStrategy.Auto)
        {
            ArgumentNullException.ThrowIfNull(plan);

            bool hasLocalRead = false;
            bool hasSampledFeedback = false;
            bool hasRasterOrderedAccess = false;
            bool requiresAttachmentMapping = false;
            byte passLocalReadMask = 0;
            byte passSampledFeedbackMask = 0;
            byte passRasterOrderedMask = 0;
            VulkanRasterSubPassLowering[] subPasses =
                new VulkanRasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                VulkanRasterSubPassLowering lowering =
                    new VulkanRasterSubPassLowering(subPass);
                subPasses[i] = lowering;
                hasLocalRead |= lowering.LocalInputMask != 0;
                hasSampledFeedback |=
                    lowering.SampledInputMask != 0;
                hasRasterOrderedAccess |=
                    lowering.RasterOrderedMask != 0;
                requiresAttachmentMapping |=
                    lowering.LocalInputMask != 0 ||
                    lowering.HasNonIdentityOutputMapping;
                passLocalReadMask |= lowering.LocalInputMask;
                passSampledFeedbackMask |=
                    lowering.SampledInputMask;
                passRasterOrderedMask |=
                    lowering.RasterOrderedMask;
            }

            ValidateInitialDepthStencilReadOnlyContract(
                plan,
                subPasses[0]);
            ValidateExactCapabilities(
                in capabilities,
                hasSampledFeedback,
                hasRasterOrderedAccess);
            ValidateNoCrossMechanismAlias(
                passLocalReadMask,
                passSampledFeedbackMask,
                passRasterOrderedMask);

            bool supportsExactDynamicLocalRead =
                capabilities.DynamicRenderingLocalRead &&
                (!plan.HasDepthStencilAttachment ||
                 capabilities
                    .DynamicRenderingLocalReadDepthStencil) &&
                (plan.SampleCount == ERHISampleCount.None ||
                 capabilities
                    .DynamicRenderingLocalReadMultisampled);
            EVulkanRasterPassStrategy strategy =
                SelectStrategy(
                    in capabilities,
                    supportsExactDynamicLocalRead,
                    requiresAttachmentMapping,
                    hasRasterOrderedAccess,
                    forcedStrategy);
            VulkanRasterPhaseBarrierPlan[] barriers =
                CompilePhaseBarriers(subPasses);
            EVulkanRasterAttachmentScopeLayout[] layouts =
                CompileScopeLayouts(
                    plan.ColorAttachmentCount,
                    passLocalReadMask,
                    passSampledFeedbackMask,
                    passRasterOrderedMask);
            return new VulkanRasterPassLowering(
                strategy,
                requiresPrivateAttachmentTable:
                    hasLocalRead || hasRasterOrderedAccess,
                usesAttachmentFeedbackLoopLayout:
                    hasSampledFeedback || hasRasterOrderedAccess,
                usesSampledFeedback: hasSampledFeedback,
                usesOrderedFragmentInterlock:
                    hasRasterOrderedAccess,
                subPasses,
                barriers,
                layouts);
        }

        private static void ValidateInitialDepthStencilReadOnlyContract(
            RasterPassPlan plan,
            in VulkanRasterSubPassLowering firstSubPass)
        {
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            if ((firstSubPass.DepthStencilFlags &
                 ERHISubPassFlags.ReadOnlyDepth) != 0 &&
                descriptor.DepthLoadOp != ERHILoadAction.Load)
            {
                throw new ArgumentException(
                    "The first Vulkan subpass cannot declare ReadOnlyDepth " +
                    "unless the depth attachment load action is Load.");
            }
            if ((firstSubPass.DepthStencilFlags &
                 ERHISubPassFlags.ReadOnlyStencil) != 0 &&
                descriptor.StencilLoadOp != ERHILoadAction.Load)
            {
                throw new ArgumentException(
                    "The first Vulkan subpass cannot declare ReadOnlyStencil " +
                    "unless the stencil attachment load action is Load.");
            }
        }

        private static void ValidateExactCapabilities(
            in VulkanRasterCapabilities capabilities,
            bool hasSampledFeedback,
            bool hasRasterOrderedAccess)
        {
            if (hasSampledFeedback &&
                !capabilities.AttachmentFeedbackLoopLayout)
            {
                throw new NotSupportedException(
                    "The raster pass declares SampledFeedback, but " +
                    "VK_EXT_attachment_feedback_loop_layout is unavailable.");
            }
            if (hasRasterOrderedAccess &&
                (!capabilities.DynamicRendering ||
                 !capabilities.OrderedFragmentPixelInterlock ||
                 !capabilities.FragmentStoresAndAtomics ||
                 !capabilities.AttachmentFeedbackLoopLayout ||
                 !capabilities.UnifiedImageLayouts))
            {
                throw new NotSupportedException(
                    "The raster pass declares RasterOrderedReadWrite, but " +
                    "the exact Vulkan path requires " +
                    "dynamic rendering, VK_KHR_unified_image_layouts, " +
                    "VK_EXT_attachment_feedback_loop_layout, " +
                    "VK_EXT_fragment_shader_interlock, " +
                    "fragmentStoresAndAtomics, and their required features.");
            }
        }

        private static void ValidateNoCrossMechanismAlias(
            byte passLocalReadMask,
            byte passSampledFeedbackMask,
            byte passRasterOrderedMask)
        {
            byte rasterOrderedAliasMask = checked((byte)(
                passRasterOrderedMask &
                (passLocalReadMask | passSampledFeedbackMask)));
            if (rasterOrderedAliasMask != 0)
            {
                throw new NotSupportedException(
                    "A Vulkan raster attachment cannot switch between " +
                    "RasterOrderedReadWrite storage access and local/sampled " +
                    "attachment access inside one native render scope.");
            }
        }

        private static VulkanRasterPhaseBarrierPlan[]
            CompilePhaseBarriers(
                ReadOnlySpan<VulkanRasterSubPassLowering> subPasses)
        {
            VulkanRasterPhaseBarrierPlan[] barriers =
                new VulkanRasterPhaseBarrierPlan[
                    Math.Max(0, subPasses.Length - 1)];
            byte priorWriteMask = subPasses.Length == 0
                ? (byte)0
                : subPasses[0].DeclaredOutputMask;
            byte priorOrderedStorageMask = subPasses.Length == 0
                ? (byte)0
                : subPasses[0].RasterOrderedMask;
            for (int destinationIndex = 1;
                 destinationIndex < subPasses.Length;
                 ++destinationIndex)
            {
                ref readonly VulkanRasterSubPassLowering destination =
                    ref subPasses[destinationIndex];
                byte localReadMask = checked((byte)(
                    priorWriteMask &
                    destination.LocalInputMask));
                byte feedbackLoopMask = checked((byte)(
                    priorWriteMask &
                    destination.SampledInputMask));
                byte orderedStorageMask = checked((byte)(
                    priorOrderedStorageMask &
                    destination.RasterOrderedMask));
                EVulkanRasterPhaseBarrierKind kind =
                    EVulkanRasterPhaseBarrierKind.None;
                if (localReadMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .FramebufferByRegion;
                }
                if (feedbackLoopMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .FeedbackLoopByRegion;
                }
                if (orderedStorageMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .OrderedStorageByRegion;
                }
                barriers[destinationIndex - 1] =
                    new VulkanRasterPhaseBarrierPlan(
                        kind,
                        checked((byte)(
                            localReadMask |
                            feedbackLoopMask |
                            orderedStorageMask)));
                priorWriteMask |= destination.DeclaredOutputMask;
                priorOrderedStorageMask |=
                    destination.RasterOrderedMask;
            }
            return barriers;
        }

        private static EVulkanRasterAttachmentScopeLayout[]
            CompileScopeLayouts(
                int colorAttachmentCount,
                byte passLocalReadMask,
                byte passSampledFeedbackMask,
                byte passRasterOrderedMask)
        {
            EVulkanRasterAttachmentScopeLayout[] layouts =
                new EVulkanRasterAttachmentScopeLayout[
                    colorAttachmentCount];
            for (int logicalAttachment = 0;
                 logicalAttachment < colorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((passRasterOrderedMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .GeneralStorage;
                }
                else if ((passSampledFeedbackMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .AttachmentFeedbackLoop;
                }
                else if ((passLocalReadMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .RenderingLocalRead;
                }
                else
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .ColorAttachment;
                }
            }
            return layouts;
        }

        private static EVulkanRasterPassStrategy SelectStrategy(
            in VulkanRasterCapabilities capabilities,
            bool supportsExactDynamicLocalRead,
            bool requiresAttachmentMapping,
            bool hasRasterOrderedAccess,
            EVulkanRasterPassForcedStrategy forcedStrategy)
        {
            switch (forcedStrategy)
            {
                case EVulkanRasterPassForcedStrategy.Auto:
                    // RasterOrderedReadWrite uses a storage-image binding in
                    // GENERAL plus VkAttachmentFeedbackLoopInfoEXT on the
                    // dynamic-rendering attachment. It has no local-input
                    // attachment semantic, so dynamic-local-read would add a
                    // needless device requirement and must not be selected.
                    if (hasRasterOrderedAccess &&
                        capabilities.DynamicRendering)
                    {
                        return EVulkanRasterPassStrategy.DynamicRendering;
                    }
                    if (!requiresAttachmentMapping &&
                        capabilities.DynamicRendering)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRendering;
                    }
                    if (capabilities.DynamicRendering &&
                        supportsExactDynamicLocalRead)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRenderingLocalRead;
                    }
                    if (!hasRasterOrderedAccess &&
                        capabilities.RenderPass2)
                    {
                        return EVulkanRasterPassStrategy
                            .NativeRenderPass2;
                    }
                    break;

                case EVulkanRasterPassForcedStrategy
                        .DynamicRenderingLocalRead:
                    if (capabilities.DynamicRendering &&
                        supportsExactDynamicLocalRead)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRenderingLocalRead;
                    }
                    break;

                case EVulkanRasterPassForcedStrategy.NativeRenderPass2:
                    if (hasRasterOrderedAccess)
                    {
                        throw new NotSupportedException(
                            "The exact Vulkan RasterOrderedReadWrite path " +
                            "requires VkAttachmentFeedbackLoopInfoEXT on a " +
                            "dynamic-rendering attachment while the storage " +
                            "descriptor remains in GENERAL; RenderPass2 " +
                            "cannot express that combination.");
                    }
                    if (capabilities.RenderPass2)
                    {
                        return EVulkanRasterPassStrategy
                            .NativeRenderPass2;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(forcedStrategy),
                        forcedStrategy,
                        "Unknown Vulkan raster lowering strategy.");
            }

            throw new NotSupportedException(
                $"The requested Vulkan raster strategy {forcedStrategy} " +
                "cannot express this pass on the current device.");
        }
    }
}
