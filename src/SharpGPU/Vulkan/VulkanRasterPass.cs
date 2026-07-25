using System;
using System.Numerics;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

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


namespace SharpGPU
{
    internal readonly struct VulkanPrivateRasterBindingPlan
    {
        internal const uint InputAttachmentBindingBase = 0;
        internal const uint RasterOrderedBindingBase = 8;

        internal uint DescriptorSet { get; }
        internal byte LocalInputMask { get; }
        internal byte LocalInputBindingMask { get; }
        internal byte RasterOrderedMask { get; }
        internal uint InputAttachmentCount =>
            checked((uint)BitOperations.PopCount((uint)LocalInputBindingMask));
        internal uint StorageImageCount =>
            checked((uint)BitOperations.PopCount((uint)RasterOrderedMask));
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            new VulkanDescriptorPoolRequirements(
                samplers: 0,
                sampledImages: 0,
                storageImages: StorageImageCount,
                uniformBuffers: 0,
                storageBuffers: 0,
                accelerationStructures: 0,
                inputAttachments: InputAttachmentCount);

        internal bool HasPrivateBindings =>
            LocalInputBindingMask != 0 ||
            RasterOrderedMask != 0;

        private VulkanPrivateRasterBindingPlan(
            uint descriptorSet,
            byte localInputMask,
            byte localInputBindingMask,
            byte rasterOrderedMask)
        {
            DescriptorSet = descriptorSet;
            LocalInputMask = localInputMask;
            LocalInputBindingMask = localInputBindingMask;
            RasterOrderedMask = rasterOrderedMask;
        }

        internal bool UsesInputAttachmentBinding(int inputIndex)
        {
            if ((uint)inputIndex >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return (LocalInputBindingMask & (1 << inputIndex)) != 0;
        }

        internal uint GetInputAttachmentBinding(int inputIndex)
        {
            if (!UsesInputAttachmentBinding(inputIndex))
            {
                throw new InvalidOperationException(
                    $"Input index {inputIndex} is not a private Vulkan " +
                    "input-attachment binding.");
            }
            return checked(
                InputAttachmentBindingBase +
                checked((uint)inputIndex));
        }

        internal uint GetRasterOrderedBinding(
            int logicalAttachment)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            byte bit =
                checked((byte)(1 << logicalAttachment));
            if ((RasterOrderedMask & bit) == 0)
            {
                throw new InvalidOperationException(
                    $"Logical attachment {logicalAttachment} is not " +
                    "raster-ordered in this pipeline.");
            }
            return checked(
                RasterOrderedBindingBase +
                checked((uint)logicalAttachment));
        }

        internal static uint GetPrivateAttachmentDescriptorSet(
            ReadOnlySpan<uint> ordinaryDescriptorSets)
        {
            bool hasOrdinarySet = false;
            uint highestOrdinarySet = 0;
            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet = ordinaryDescriptorSets[index];
                if (!hasOrdinarySet ||
                    descriptorSet > highestOrdinarySet)
                {
                    highestOrdinarySet = descriptorSet;
                    hasOrdinarySet = true;
                }
            }

            if (hasOrdinarySet &&
                highestOrdinarySet == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordinaryDescriptorSets),
                    highestOrdinarySet,
                    "The highest Vulkan descriptor-set index leaves no " +
                    "representable slot for the private attachment set.");
            }
            return hasOrdinarySet
                ? highestOrdinarySet + 1
                : 0;
        }

        internal static VulkanPrivateRasterBindingPlan Compile(
            ReadOnlySpan<uint> ordinaryDescriptorSets,
            uint maximumBoundDescriptorSets,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumBoundDescriptorSets == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBoundDescriptorSets),
                    maximumBoundDescriptorSets,
                    "Vulkan must expose at least one bound descriptor set.");
            }

            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet =
                    ordinaryDescriptorSets[index];
                if (descriptorSet >= maximumBoundDescriptorSets)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ordinaryDescriptorSets),
                        descriptorSet,
                        $"Ordinary Vulkan descriptor set {descriptorSet} " +
                        $"exceeds maxBoundDescriptorSets " +
                        $"{maximumBoundDescriptorSets}.");
                }
                for (int previous = 0;
                     previous < index;
                     ++previous)
                {
                    if (ordinaryDescriptorSets[previous] ==
                        descriptorSet)
                    {
                        throw new ArgumentException(
                            $"Ordinary Vulkan descriptor set " +
                            $"{descriptorSet} is duplicated.",
                            nameof(ordinaryDescriptorSets));
                    }
                }
            }
            uint privateDescriptorSet =
                GetPrivateAttachmentDescriptorSet(
                    ordinaryDescriptorSets);

            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            byte localInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~rasterOrderedMask));
            byte localInputBindingMask = 0;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0 ||
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    continue;
                }
                localInputBindingMask |=
                    checked((byte)(1 << inputIndex));
            }

            bool hasPrivateBindings =
                localInputBindingMask != 0 ||
                rasterOrderedMask != 0;
            if (hasPrivateBindings &&
                privateDescriptorSet >= maximumBoundDescriptorSets)
            {
                throw new NotSupportedException(
                    $"Vulkan ordinary descriptor sets consume slots through " +
                    $"{privateDescriptorSet - 1}; no slot remains for the private " +
                    $"attachment set within maxBoundDescriptorSets " +
                    $"{maximumBoundDescriptorSets}.");
            }

            return new VulkanPrivateRasterBindingPlan(
                privateDescriptorSet,
                localInputMask,
                localInputBindingMask,
                rasterOrderedMask);
        }

        internal static void ValidatePipelineLayoutIdentity(
            bool isDisposed,
            object? actualDevice,
            object expectedDevice)
        {
            ArgumentNullException.ThrowIfNull(expectedDevice);
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Vulkan pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }
    }

    internal unsafe sealed class VulkanPrivateRasterDescriptorLayout :
        IDisposable
    {
        internal VulkanPrivateRasterBindingPlan Plan { get; }
        internal VkDescriptorSetLayout NativeLayout { get; private set; }
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            Plan.PoolRequirements;

        private readonly VkDevice m_NativeDevice;
        private bool m_Disposed;

        internal VulkanPrivateRasterDescriptorLayout(
            VulkanDevice device,
            in VulkanPrivateRasterBindingPlan plan)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (!plan.HasPrivateBindings)
            {
                throw new ArgumentException(
                    "A Vulkan private descriptor layout requires at least " +
                    "one input-attachment or raster-ordered binding.",
                    nameof(plan));
            }
            ValidateLimits(device.DescriptorLimits, in plan);

            m_NativeDevice = device.NativeDevice;
            Plan = plan;
            VkDescriptorSetLayoutBinding* bindings =
                stackalloc VkDescriptorSetLayoutBinding[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            int bindingCount = 0;
            for (int inputIndex = 0;
                 inputIndex < RHIAttachmentIndexArray.MaxAttachments;
                 ++inputIndex)
            {
                if (!plan.UsesInputAttachmentBinding(inputIndex))
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetInputAttachmentBinding(inputIndex),
                        descriptorType =
                            VkDescriptorType.InputAttachment,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((plan.RasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetRasterOrderedBinding(
                                logicalAttachment),
                        descriptorType =
                            VkDescriptorType.StorageImage,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }

            VkDescriptorSetLayoutCreateInfo createInfo =
                new VkDescriptorSetLayoutCreateInfo
                {
                    sType =
                        VkStructureType.DescriptorSetLayoutCreateInfo,
                    bindingCount = checked((uint)bindingCount),
                    pBindings = bindings,
                };
            VkDescriptorSetLayout nativeLayout = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateDescriptorSetLayout(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &nativeLayout));
            NativeLayout = nativeLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (NativeLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(
                    m_NativeDevice,
                    NativeLayout,
                    null);
                NativeLayout = default;
            }
            m_Disposed = true;
        }

        private static void ValidateLimits(
            in VulkanDescriptorLimits limits,
            in VulkanPrivateRasterBindingPlan plan)
        {
            if (plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerSet ||
                plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster input-attachment count " +
                    $"{plan.InputAttachmentCount} exceeds the device " +
                    "descriptor limits.");
            }
            if (plan.StorageImageCount >
                    limits.MaximumStorageImagesPerSet ||
                plan.StorageImageCount >
                    limits.MaximumStorageImagesPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster storage-image count " +
                    $"{plan.StorageImageCount} exceeds the device " +
                    "descriptor limits.");
            }
        }
    }

    internal sealed class VulkanDescriptorSetLeaseTransaction :
        IDisposable
    {
        private readonly Action<VulkanDescriptorSetLease> m_Rollback;
        private VulkanDescriptorSetLease m_Lease;
        private bool m_Committed;
        private bool m_Disposed;

        internal VulkanDescriptorSetLeaseTransaction(
            in VulkanDescriptorSetLease lease,
            Action<VulkanDescriptorSetLease> rollback)
        {
            ArgumentNullException.ThrowIfNull(rollback);
            if (lease.Pool.Handle == 0 || lease.Set.Handle == 0)
            {
                throw new ArgumentException(
                    "A private Vulkan descriptor transaction requires a " +
                    "complete native lease.",
                    nameof(lease));
            }
            m_Lease = lease;
            m_Rollback = rollback;
        }

        internal VulkanDescriptorSetLease Lease
        {
            get
            {
                ObjectDisposedException.ThrowIf(m_Disposed, this);
                return m_Lease;
            }
        }

        internal void Commit(
            Action<VulkanDescriptorSetLease> register)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            ArgumentNullException.ThrowIfNull(register);
            if (m_Committed)
            {
                throw new InvalidOperationException(
                    "The Vulkan descriptor lease is already committed.");
            }
            register(m_Lease);
            m_Committed = true;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            try
            {
                if (!m_Committed)
                {
                    m_Rollback(m_Lease);
                }
            }
            finally
            {
                m_Lease = default;
                m_Disposed = true;
            }
        }
    }
}


namespace SharpGPU
{
    internal sealed unsafe class
        VulkanDynamicRenderingAttachmentMappingPlan
    {
        internal const uint AttachmentUnused = uint.MaxValue;

        internal int ColorAttachmentCount =>
            m_OutputLocationsByPhysicalAttachment.Length;
        internal bool HasLocalReadOrRemapping { get; }

        private readonly uint[]
            m_OutputLocationsByPhysicalAttachment;
        private readonly uint[]
            m_InputIndicesByPhysicalAttachment;

        private VulkanDynamicRenderingAttachmentMappingPlan(
            uint[] outputLocationsByPhysicalAttachment,
            uint[] inputIndicesByPhysicalAttachment,
            bool hasLocalReadOrRemapping)
        {
            m_OutputLocationsByPhysicalAttachment =
                outputLocationsByPhysicalAttachment;
            m_InputIndicesByPhysicalAttachment =
                inputIndicesByPhysicalAttachment;
            HasLocalReadOrRemapping = hasLocalReadOrRemapping;
        }

        internal static
            VulkanDynamicRenderingAttachmentMappingPlan Compile(
                in RHIAttachmentInterfaceSignature signature)
        {
            int colorCount = signature.ColorAttachmentCount;
            uint[] outputs = new uint[colorCount];
            uint[] inputs = new uint[colorCount];
            Array.Fill(outputs, AttachmentUnused);
            Array.Fill(inputs, AttachmentUnused);

            byte rasterOrderedMask =
                signature.RasterOrderedReadWriteMask;
            bool requiresMapping = false;
            for (int outputLocation = 0;
                 outputLocation <
                    signature.ColorOutputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    signature.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    outputs[logicalAttachment] =
                        checked((uint)outputLocation);
                }
                requiresMapping |=
                    logicalAttachment != outputLocation;
            }

            for (int inputIndex = 0;
                 inputIndex <
                    signature.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    signature.GetColorInputLogicalAttachment(
                        inputIndex);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    inputs[logicalAttachment] =
                        checked((uint)inputIndex);
                    requiresMapping = true;
                }
            }

            for (int physicalAttachment = 0;
                 physicalAttachment < colorCount;
                 ++physicalAttachment)
            {
                requiresMapping |=
                    outputs[physicalAttachment] !=
                        checked((uint)physicalAttachment) ||
                    inputs[physicalAttachment] != AttachmentUnused;
            }

            return new(
                outputs,
                inputs,
                requiresMapping);
        }

        internal void Populate(
            uint* outputLocations,
            uint* inputIndices)
        {
            if (ColorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < ColorAttachmentCount;
                 ++physicalAttachment)
            {
                outputLocations[physicalAttachment] =
                    m_OutputLocationsByPhysicalAttachment[
                        physicalAttachment];
                inputIndices[physicalAttachment] =
                    m_InputIndicesByPhysicalAttachment[
                        physicalAttachment];
            }
        }

        internal static void Populate(
            in VulkanRasterSubPassLowering subPass,
            int colorAttachmentCount,
            uint* outputLocations,
            uint* inputIndices)
        {
            if (colorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < colorAttachmentCount;
                 ++physicalAttachment)
            {
                int outputLocation =
                    subPass.GetOutputLocationForPhysicalAttachment(
                        physicalAttachment);
                int inputIndex =
                    subPass.GetInputIndexForPhysicalAttachment(
                        physicalAttachment);
                outputLocations[physicalAttachment] =
                    outputLocation < 0
                        ? AttachmentUnused
                        : checked((uint)outputLocation);
                inputIndices[physicalAttachment] =
                    inputIndex < 0
                        ? AttachmentUnused
                        : checked((uint)inputIndex);
            }
        }

        private static bool IsRasterOrdered(
            int logicalAttachment,
            byte rasterOrderedMask) =>
            logicalAttachment >= 0 &&
            (rasterOrderedMask &
             (1 << logicalAttachment)) != 0;
    }
}


namespace SharpGPU
{
    internal sealed class VulkanRenderPass2SubPassDescription
    {
        internal ReadOnlyMemory<int> InputAttachmentsByIndex =>
            m_InputAttachmentsByIndex;
        internal ReadOnlyMemory<int> ColorAttachmentsByLocation =>
            m_ColorAttachmentsByLocation;
        internal ReadOnlyMemory<int> ResolveAttachmentsByLocation =>
            m_ResolveAttachmentsByLocation;
        internal ReadOnlyMemory<int> SampledFeedbackAttachments =>
            m_SampledFeedbackAttachments;
        internal ReadOnlyMemory<int> PreserveAttachments =>
            m_PreserveAttachments;
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool ResolvesDepthStencil { get; }
        internal bool UsesAttachmentFeedbackLoopLayout =>
            m_SampledFeedbackAttachments.Length != 0;
        internal bool UsesOrderedFragmentInterlock { get; }
        internal byte RasterOrderedMask { get; }
        internal byte InputMask { get; }
        internal byte OutputMask { get; }
        internal byte SampledFeedbackMask { get; }
        internal byte OrdinaryReadWriteMask =>
            checked((byte)(InputMask & OutputMask));

        private readonly int[] m_InputAttachmentsByIndex;
        private readonly int[] m_ColorAttachmentsByLocation;
        private readonly int[] m_ResolveAttachmentsByLocation;
        private readonly int[] m_SampledFeedbackAttachments;
        private readonly int[] m_PreserveAttachments;

        internal VulkanRenderPass2SubPassDescription(
            int[] inputAttachmentsByIndex,
            int[] colorAttachmentsByLocation,
            int[] resolveAttachmentsByLocation,
            int[] sampledFeedbackAttachments,
            int[] preserveAttachments,
            ERHISubPassFlags depthStencilFlags,
            bool resolvesDepthStencil,
            byte rasterOrderedMask)
        {
            m_InputAttachmentsByIndex = inputAttachmentsByIndex;
            m_ColorAttachmentsByLocation =
                colorAttachmentsByLocation;
            m_ResolveAttachmentsByLocation =
                resolveAttachmentsByLocation;
            m_SampledFeedbackAttachments =
                sampledFeedbackAttachments;
            m_PreserveAttachments = preserveAttachments;
            DepthStencilFlags = depthStencilFlags;
            ResolvesDepthStencil = resolvesDepthStencil;
            RasterOrderedMask = rasterOrderedMask;
            UsesOrderedFragmentInterlock =
                rasterOrderedMask != 0;
            InputMask = BuildMask(inputAttachmentsByIndex);
            OutputMask = BuildMask(colorAttachmentsByLocation);
            SampledFeedbackMask =
                BuildMask(sampledFeedbackAttachments);
        }

        private static byte BuildMask(ReadOnlySpan<int> attachments)
        {
            byte mask = 0;
            for (int index = 0;
                 index < attachments.Length;
                 ++index)
            {
                int attachment = attachments[index];
                if (attachment >= 0)
                {
                    mask |= checked((byte)(1 << attachment));
                }
            }
            return mask;
        }
    }

    internal sealed class VulkanRenderPass2Description
    {
        internal ReadOnlyMemory<int> ColorResolveAttachmentIndices =>
            m_ColorResolveAttachmentIndices;
        internal ReadOnlyMemory<VulkanRenderPass2SubPassDescription>
            SubPasses => m_SubPasses;
        internal int DepthStencilAttachmentIndex { get; }
        internal int DepthStencilResolveAttachmentIndex { get; }
        internal int AttachmentCount { get; }

        private readonly int[] m_ColorResolveAttachmentIndices;
        private readonly VulkanRenderPass2SubPassDescription[] m_SubPasses;

        private VulkanRenderPass2Description(
            int[] colorResolveAttachmentIndices,
            VulkanRenderPass2SubPassDescription[] subPasses,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex,
            int attachmentCount)
        {
            m_ColorResolveAttachmentIndices =
                colorResolveAttachmentIndices;
            m_SubPasses = subPasses;
            DepthStencilAttachmentIndex =
                depthStencilAttachmentIndex;
            DepthStencilResolveAttachmentIndex =
                depthStencilResolveAttachmentIndex;
            AttachmentCount = attachmentCount;
        }

        internal static VulkanRenderPass2Description Compile(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(lowering);
            if (lowering.SubPasses.Length != plan.SubPassCount)
            {
                throw new ArgumentException(
                    "The Vulkan lowering does not belong to this raster pass.",
                    nameof(lowering));
            }

            int[] resolveAttachmentIndices =
                new int[plan.ColorAttachmentCount];
            Array.Fill(resolveAttachmentIndices, -1);
            int attachmentCount = plan.ColorAttachmentCount;
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                if (plan.GetColorAttachment(colorIndex).ResolveTarget !=
                    null)
                {
                    resolveAttachmentIndices[colorIndex] =
                        attachmentCount++;
                }
            }

            int depthStencilAttachmentIndex = -1;
            int depthStencilResolveAttachmentIndex = -1;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilAttachmentIndex = attachmentCount++;
                if (plan.GetDepthStencilAttachment().ResolveTarget != null)
                {
                    depthStencilResolveAttachmentIndex =
                        attachmentCount++;
                }
            }

            int[] finalColorOutputSubPass =
                new int[plan.ColorAttachmentCount];
            Array.Fill(finalColorOutputSubPass, -1);
            for (int subPassIndex = 0;
                 subPassIndex < plan.SubPassCount;
                 ++subPassIndex)
            {
                ref readonly VulkanRasterSubPassLowering subPass =
                    ref lowering.SubPasses.Span[subPassIndex];
                for (int outputLocation = 0;
                     outputLocation <
                        subPass.ColorOutputLocationCount;
                     ++outputLocation)
                {
                    int logicalAttachment =
                        subPass.GetColorOutputLogicalAttachment(
                            outputLocation);
                    if (logicalAttachment >= 0)
                    {
                        finalColorOutputSubPass[logicalAttachment] =
                            subPassIndex;
                    }
                }
            }

            VulkanRenderPass2SubPassDescription[] subPasses =
                new VulkanRenderPass2SubPassDescription[
                    plan.SubPassCount];
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                ref readonly RasterSubPassPlan planSubPass =
                    ref plan.GetSubPass(subPassIndex);
                ref readonly VulkanRasterSubPassLowering loweringSubPass =
                    ref lowering.SubPasses.Span[subPassIndex];

                int[] inputs =
                    new int[loweringSubPass.ColorInputSlotCount];
                for (int inputIndex = 0;
                     inputIndex < inputs.Length;
                     ++inputIndex)
                {
                    inputs[inputIndex] =
                        loweringSubPass
                            .GetColorInputLogicalAttachment(
                                inputIndex);
                }

                int[] colors =
                    new int[
                        loweringSubPass.ColorOutputLocationCount];
                int[] resolves = new int[colors.Length];
                Array.Fill(resolves, -1);
                for (int outputLocation = 0;
                     outputLocation < colors.Length;
                     ++outputLocation)
                {
                    int logicalAttachment =
                        loweringSubPass
                            .GetColorOutputLogicalAttachment(
                                outputLocation);
                    colors[outputLocation] = logicalAttachment;
                    if (logicalAttachment >= 0 &&
                        resolveAttachmentIndices[
                            logicalAttachment] >= 0 &&
                        finalColorOutputSubPass[
                            logicalAttachment] == subPassIndex)
                    {
                        resolves[outputLocation] =
                            resolveAttachmentIndices[
                                logicalAttachment];
                    }
                }

                int[] sampledFeedback =
                    new int[
                        loweringSubPass.SampledFeedbackSlotCount];
                for (int sampledOrdinal = 0;
                     sampledOrdinal < sampledFeedback.Length;
                     ++sampledOrdinal)
                {
                    sampledFeedback[sampledOrdinal] =
                        loweringSubPass
                            .GetSampledFeedbackLogicalAttachment(
                                sampledOrdinal);
                }

                byte nativeReferenceMask = checked((byte)(
                    loweringSubPass.LocalInputMask |
                    loweringSubPass.OutputMask));
                byte descriptorAttachmentMask = checked((byte)(
                    loweringSubPass.SampledInputMask |
                    loweringSubPass.RasterOrderedMask));
                byte preserveMask = checked((byte)(
                    planSubPass.PreserveMask |
                    (descriptorAttachmentMask &
                     ~nativeReferenceMask)));
                List<int> preserves =
                    new List<int>(plan.ColorAttachmentCount);
                for (int logicalAttachment = 0;
                     logicalAttachment < plan.ColorAttachmentCount;
                     ++logicalAttachment)
                {
                    if ((preserveMask &
                         (1 << logicalAttachment)) != 0)
                    {
                        preserves.Add(logicalAttachment);
                    }
                }

                subPasses[subPassIndex] =
                    new VulkanRenderPass2SubPassDescription(
                        inputs,
                        colors,
                        resolves,
                        sampledFeedback,
                        preserves.ToArray(),
                        loweringSubPass.DepthStencilFlags,
                        depthStencilResolveAttachmentIndex >= 0 &&
                            subPassIndex == plan.SubPassCount - 1,
                        loweringSubPass.RasterOrderedMask);
            }

            return new VulkanRenderPass2Description(
                resolveAttachmentIndices,
                subPasses,
                depthStencilAttachmentIndex,
                depthStencilResolveAttachmentIndex,
                attachmentCount);
        }
    }
}


namespace SharpGPU
{
    internal static unsafe class VulkanRenderPass2DescriptionLowering
    {
        private const uint AttachmentUnused = uint.MaxValue;

        internal static void PopulateSubpasses(
            RasterPassPlan plan,
            VulkanRenderPass2Description description,
            VkSubpassDescription2* subpasses,
            VkAttachmentReference2* inputReferences,
            VkAttachmentReference2* colorReferences,
            VkAttachmentReference2* resolveReferences,
            uint* preserveReferences,
            VkAttachmentReference2* depthReferences,
            VkAttachmentReference2* depthResolveReferences,
            VkSubpassDescriptionDepthStencilResolve*
                depthResolveDescriptions,
            int[] resolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(description);
            _ = resolveAttachmentIndices;

            for (int subPassIndex = 0;
                 subPassIndex < plan.SubPassCount;
                 ++subPassIndex)
            {
                VulkanRenderPass2SubPassDescription subPass =
                    description.SubPasses.Span[subPassIndex];
                int referenceBase =
                    subPassIndex *
                    RHIAttachmentIndexArray.MaxAttachments;

                ReadOnlySpan<int> inputs =
                    subPass.InputAttachmentsByIndex.Span;
                for (int inputIndex = 0;
                     inputIndex < inputs.Length;
                     ++inputIndex)
                {
                    int logicalAttachment = inputs[inputIndex];
                    inputReferences[referenceBase + inputIndex] =
                        CreateAttachmentReference(
                            logicalAttachment >= 0
                                ? checked((uint)logicalAttachment)
                                : AttachmentUnused,
                            logicalAttachment >= 0
                                ? GetColorReferenceLayout(
                                    description,
                                    subPassIndex,
                                    logicalAttachment,
                                    inputReference: true)
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);
                }

                ReadOnlySpan<int> colors =
                    subPass.ColorAttachmentsByLocation.Span;
                ReadOnlySpan<int> resolves =
                    subPass.ResolveAttachmentsByLocation.Span;
                bool hasResolve = false;
                for (int outputLocation = 0;
                     outputLocation < colors.Length;
                     ++outputLocation)
                {
                    int logicalAttachment = colors[outputLocation];
                    colorReferences[referenceBase + outputLocation] =
                        CreateAttachmentReference(
                            logicalAttachment >= 0
                                ? checked((uint)logicalAttachment)
                                : AttachmentUnused,
                            logicalAttachment >= 0
                                ? GetColorReferenceLayout(
                                    description,
                                    subPassIndex,
                                    logicalAttachment,
                                    inputReference: false)
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);

                    int resolveAttachment = resolves[outputLocation];
                    resolveReferences[referenceBase + outputLocation] =
                        CreateAttachmentReference(
                            resolveAttachment >= 0
                                ? checked((uint)resolveAttachment)
                                : AttachmentUnused,
                            resolveAttachment >= 0
                                ? VkImageLayout.ColorAttachmentOptimal
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);
                    hasResolve |= resolveAttachment >= 0;
                }

                ReadOnlySpan<int> preserves =
                    subPass.PreserveAttachments.Span;
                for (int preserveIndex = 0;
                     preserveIndex < preserves.Length;
                     ++preserveIndex)
                {
                    preserveReferences[referenceBase + preserveIndex] =
                        checked((uint)preserves[preserveIndex]);
                }

                VkAttachmentReference2* depthReference = null;
                if (depthStencilAttachmentIndex >= 0)
                {
                    VkImageLayout layout =
                        subPass.DepthStencilFlags ==
                            ERHISubPassFlags.ReadOnlyDepthStencil
                            ? VkImageLayout.DepthStencilReadOnlyOptimal
                            : VkImageLayout.DepthStencilAttachmentOptimal;
                    depthReferences[subPassIndex] =
                        CreateAttachmentReference(
                            checked((uint)
                                depthStencilAttachmentIndex),
                            layout,
                            plan.GetDepthStencilAttachment()
                                .SubresourceRange.AspectMask
                                .ToVkImageAspectFlags());
                    depthReference = &depthReferences[subPassIndex];
                }

                void* next = null;
                if (depthStencilResolveAttachmentIndex >= 0 &&
                    subPass.ResolvesDepthStencil)
                {
                    RHIDepthStencilAttachmentDescriptor depthStencil =
                        plan.GetDepthStencilAttachment();
                    depthResolveReferences[subPassIndex] =
                        CreateAttachmentReference(
                            checked((uint)
                                depthStencilResolveAttachmentIndex),
                            VkImageLayout
                                .DepthStencilAttachmentOptimal,
                            depthStencil.SubresourceRange.AspectMask
                                .ToVkImageAspectFlags());
                    depthResolveDescriptions[subPassIndex] =
                        new VkSubpassDescriptionDepthStencilResolve
                        {
                            sType = VkStructureType
                                .SubpassDescriptionDepthStencilResolve,
                            depthResolveMode =
                                ConvertResolveMode(
                                    depthStencil.DepthResolveMode),
                            stencilResolveMode =
                                ConvertResolveMode(
                                    depthStencil.StencilResolveMode),
                            pDepthStencilResolveAttachment =
                                &depthResolveReferences[subPassIndex],
                        };
                    next = &depthResolveDescriptions[subPassIndex];
                }

                subpasses[subPassIndex] =
                    new VkSubpassDescription2
                    {
                        sType = VkStructureType.SubpassDescription2,
                        pNext = next,
                        pipelineBindPoint =
                            VkPipelineBindPoint.Graphics,
                        inputAttachmentCount =
                            checked((uint)inputs.Length),
                        pInputAttachments =
                            inputs.Length == 0
                                ? null
                                : &inputReferences[referenceBase],
                        colorAttachmentCount =
                            checked((uint)colors.Length),
                        pColorAttachments =
                            colors.Length == 0
                                ? null
                                : &colorReferences[referenceBase],
                        pResolveAttachments =
                            hasResolve
                                ? &resolveReferences[referenceBase]
                                : null,
                        pDepthStencilAttachment = depthReference,
                        preserveAttachmentCount =
                            checked((uint)preserves.Length),
                        pPreserveAttachments =
                            preserves.Length == 0
                                ? null
                                : &preserveReferences[referenceBase],
                    };
            }
        }
        private static VkImageLayout GetColorReferenceLayout(
            VulkanRenderPass2Description description,
            int subPassIndex,
            int logicalAttachment,
            bool inputReference)
        {
            ReadOnlySpan<VulkanRenderPass2SubPassDescription> subPasses =
                description.SubPasses.Span;
            byte bit = checked((byte)(1 << logicalAttachment));
            if ((subPasses[subPassIndex].OrdinaryReadWriteMask &
                 bit) != 0)
            {
                return VkImageLayout.General;
            }
            if ((subPasses[subPassIndex].SampledFeedbackMask &
                 bit) != 0)
            {
                return VkImageLayout
                    .AttachmentFeedbackLoopOptimalEXT;
            }
            return inputReference
                ? VkImageLayout.ShaderReadOnlyOptimal
                : VkImageLayout.ColorAttachmentOptimal;
        }

        private static VkAttachmentReference2
            CreateAttachmentReference(
                uint attachment,
                VkImageLayout layout,
                VkImageAspectFlags aspects) =>
            new()
            {
                sType = VkStructureType.AttachmentReference2,
                attachment = attachment,
                layout = layout,
                aspectMask = aspects,
            };

        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) => mode switch
        {
            EResolveMode.None => VkResolveModeFlags.None,
            EResolveMode.Sample0 => VkResolveModeFlags.SampleZero,
            EResolveMode.Min => VkResolveModeFlags.Min,
            EResolveMode.Max => VkResolveModeFlags.Max,
            _ => VkResolveModeFlags.Average,
        };
    }
}


namespace SharpGPU
{
    internal sealed class VulkanRenderPass2Plan
    {
        internal VkRenderPass NativeRenderPass { get; }
        internal int[] ColorResolveAttachmentIndices { get; }
        internal int DepthStencilAttachmentIndex { get; }
        internal int DepthStencilResolveAttachmentIndex { get; }
        internal int AttachmentCount { get; }
        internal bool UsesKhrEntryPoints { get; }

        internal VulkanRenderPass2Plan(
            VkRenderPass nativeRenderPass,
            int[] colorResolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex,
            int attachmentCount,
            bool usesKhrEntryPoints)
        {
            NativeRenderPass = nativeRenderPass;
            ColorResolveAttachmentIndices =
                colorResolveAttachmentIndices;
            DepthStencilAttachmentIndex =
                depthStencilAttachmentIndex;
            DepthStencilResolveAttachmentIndex =
                depthStencilResolveAttachmentIndex;
            AttachmentCount = attachmentCount;
            UsesKhrEntryPoints = usesKhrEntryPoints;
        }
    }

    internal static unsafe class VulkanRenderPass2Lowering
    {
        private const uint AttachmentUnused = uint.MaxValue;

        internal static VulkanRenderPass2Plan Create(
            VkDevice device,
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            bool useKhrEntryPoints)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(lowering);
            VulkanRenderPass2Description description =
                VulkanRenderPass2Description.Compile(plan, lowering);

            int[] resolveAttachmentIndices =
                new int[plan.ColorAttachmentCount];
            Array.Fill(resolveAttachmentIndices, -1);
            int attachmentCount = plan.ColorAttachmentCount;
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                if (color.ResolveTarget != null)
                {
                    resolveAttachmentIndices[colorIndex] =
                        attachmentCount++;
                }
            }

            int depthStencilAttachmentIndex = -1;
            int depthStencilResolveAttachmentIndex = -1;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilAttachmentIndex = attachmentCount++;
                if (plan.GetDepthStencilAttachment().ResolveTarget != null)
                {
                    depthStencilResolveAttachmentIndex =
                        attachmentCount++;
                }
            }

            VkAttachmentDescription2* attachments =
                Allocate<VkAttachmentDescription2>(attachmentCount);
            VkSubpassDescription2* subpasses =
                Allocate<VkSubpassDescription2>(plan.SubPassCount);
            VkSubpassDependency2* dependencies =
                Allocate<VkSubpassDependency2>(
                    checked(plan.SubPassCount * plan.SubPassCount));
            VkAttachmentReference2* inputReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* colorReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* resolveReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            uint* preserveReferences =
                Allocate<uint>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* depthReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkAttachmentReference2* depthResolveReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkSubpassDescriptionDepthStencilResolve*
                depthResolveDescriptions =
                    Allocate<VkSubpassDescriptionDepthStencilResolve>(
                        plan.SubPassCount);

            try
            {
                PopulateAttachmentDescriptions(
                    plan,
                    lowering,
                    attachments,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                VulkanRenderPass2DescriptionLowering.PopulateSubpasses(
                    plan,
                    description,
                    subpasses,
                    inputReferences,
                    colorReferences,
                    resolveReferences,
                    preserveReferences,
                    depthReferences,
                    depthResolveReferences,
                    depthResolveDescriptions,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                int dependencyCount = PopulateDependencies(
                    plan,
                    description,
                    dependencies);

                VkRenderPassCreateInfo2 createInfo = new()
                {
                    sType = VkStructureType.RenderPassCreateInfo2,
                    attachmentCount = checked((uint)attachmentCount),
                    pAttachments = attachments,
                    subpassCount = checked((uint)plan.SubPassCount),
                    pSubpasses = subpasses,
                    dependencyCount =
                        checked((uint)dependencyCount),
                    pDependencies = dependencies,
                };
                VkRenderPass renderPass;
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateRenderPass2(
                        device,
                        &createInfo,
                        null,
                        &renderPass,
                        useKhrEntryPoints));
                return new VulkanRenderPass2Plan(
                    renderPass,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex,
                    attachmentCount,
                    useKhrEntryPoints);
            }
            finally
            {
                NativeMemory.Free(attachments);
                NativeMemory.Free(subpasses);
                NativeMemory.Free(dependencies);
                NativeMemory.Free(inputReferences);
                NativeMemory.Free(colorReferences);
                NativeMemory.Free(resolveReferences);
                NativeMemory.Free(preserveReferences);
                NativeMemory.Free(depthReferences);
                NativeMemory.Free(depthResolveReferences);
                NativeMemory.Free(depthResolveDescriptions);
            }
        }

        private static void PopulateAttachmentDescriptions(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            VkAttachmentDescription2* attachments,
            int[] resolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                attachments[colorIndex] = new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            color.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            color.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            color.LoadAction),
                    storeOp =
                        color.StoreAction ==
                            ERHIStoreAction.StoreAndResolve ||
                        color.StoreAction == ERHIStoreAction.Store
                            ? VkAttachmentStoreOp.Store
                            : VkAttachmentStoreOp.DontCare,
                    stencilLoadOp = VkAttachmentLoadOp.DontCare,
                    stencilStoreOp = VkAttachmentStoreOp.DontCare,
                    initialLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                };

                int resolveIndex =
                    resolveAttachmentIndices[colorIndex];
                if (resolveIndex >= 0)
                {
                    attachments[resolveIndex] =
                        new VkAttachmentDescription2
                        {
                            sType =
                                VkStructureType.AttachmentDescription2,
                            format =
                                VulkanUtility.ConvertToVkFormat(
                                    color.ResolveTarget!.Descriptor.Format),
                            samples = VkSampleCountFlags.Count1,
                            loadOp = VkAttachmentLoadOp.DontCare,
                            storeOp = VkAttachmentStoreOp.Store,
                            stencilLoadOp =
                                VkAttachmentLoadOp.DontCare,
                            stencilStoreOp =
                                VkAttachmentStoreOp.DontCare,
                            initialLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                            finalLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                        };
                }
            }

            if (depthStencilAttachmentIndex < 0)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            attachments[depthStencilAttachmentIndex] =
                new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            depthStencil.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            depthStencil.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.DepthLoadOp),
                    storeOp =
                        ConvertSourceStoreOp(
                            depthStencil.DepthStoreOp),
                    stencilLoadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.StencilLoadOp),
                    stencilStoreOp =
                        ConvertSourceStoreOp(
                            depthStencil.StencilStoreOp),
                    initialLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                };

            if (depthStencilResolveAttachmentIndex >= 0)
            {
                attachments[depthStencilResolveAttachmentIndex] =
                    new VkAttachmentDescription2
                    {
                        sType =
                            VkStructureType.AttachmentDescription2,
                        format =
                            VulkanUtility.ConvertToVkFormat(
                                depthStencil.ResolveTarget!.Descriptor
                                    .Format),
                        samples = VkSampleCountFlags.Count1,
                        loadOp = VkAttachmentLoadOp.DontCare,
                        storeOp = VkAttachmentStoreOp.Store,
                        stencilLoadOp = VkAttachmentLoadOp.DontCare,
                        stencilStoreOp = VkAttachmentStoreOp.Store,
                        initialLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                        finalLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                    };
            }
        }

        private static int PopulateDependencies(
            RasterPassPlan plan,
            VulkanRenderPass2Description description,
            VkSubpassDependency2* dependencies)
        {
            int dependencyCount = 0;
            ReadOnlySpan<VulkanRenderPass2SubPassDescription> subPasses =
                description.SubPasses.Span;
            for (int sourceIndex = 0;
                 sourceIndex < subPasses.Length;
                 ++sourceIndex)
            {
                VulkanRenderPass2SubPassDescription source =
                    subPasses[sourceIndex];
                for (int destinationIndex = sourceIndex;
                     destinationIndex < subPasses.Length;
                     ++destinationIndex)
                {
                    VulkanRenderPass2SubPassDescription destination =
                        subPasses[destinationIndex];
                    byte destinationColorAccessMask =
                        checked((byte)(
                            destination.InputMask |
                            destination.OutputMask |
                            destination.SampledFeedbackMask));
                    byte colorHazardMask =
                        checked((byte)(
                            source.OutputMask &
                            destinationColorAccessMask));
                    bool selfDependency =
                        sourceIndex == destinationIndex;
                    bool ordinaryReadWrite =
                        selfDependency &&
                        source.OrdinaryReadWriteMask != 0;
                    bool sampledFeedback =
                        (colorHazardMask &
                         destination.SampledFeedbackMask) != 0;
                    bool colorHazard =
                        selfDependency
                            ? ordinaryReadWrite ||
                              sampledFeedback
                            : colorHazardMask != 0;
                    bool depthStencilHazard =
                        !selfDependency &&
                        plan.HasDepthStencilAttachment &&
                        WritesDepthOrStencil(
                            source.DepthStencilFlags);
                    if (!colorHazard && !depthStencilHazard)
                    {
                        continue;
                    }

                    VkPipelineStageFlags sourceStages = 0;
                    VkPipelineStageFlags destinationStages = 0;
                    VkAccessFlags sourceAccess = 0;
                    VkAccessFlags destinationAccess = 0;
                    if (colorHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .ColorAttachmentOutput;
                        sourceAccess |=
                            VkAccessFlags.ColorAttachmentWrite;
                        if ((colorHazardMask &
                             (destination.InputMask |
                              destination.SampledFeedbackMask)) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags.FragmentShader;
                            destinationAccess |=
                                VkAccessFlags.InputAttachmentRead |
                                VkAccessFlags.ShaderRead;
                        }
                        if ((colorHazardMask &
                             destination.OutputMask) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags
                                    .ColorAttachmentOutput;
                            destinationAccess |=
                                VkAccessFlags.ColorAttachmentRead |
                                VkAccessFlags.ColorAttachmentWrite;
                        }
                    }
                    if (depthStencilHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .LateFragmentTests;
                        destinationStages |=
                            VkPipelineStageFlags
                                .EarlyFragmentTests;
                        sourceAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                        destinationAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentRead |
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                    }

                    VkDependencyFlags flags =
                        VkDependencyFlags.ByRegion;
                    if (sampledFeedback)
                    {
                        flags |=
                            VkDependencyFlags.FeedbackLoopEXT;
                    }
                    dependencies[dependencyCount++] =
                        new VkSubpassDependency2
                        {
                            sType =
                                VkStructureType.SubpassDependency2,
                            srcSubpass =
                                checked((uint)sourceIndex),
                            dstSubpass =
                                checked((uint)destinationIndex),
                            srcStageMask = sourceStages,
                            dstStageMask = destinationStages,
                            srcAccessMask = sourceAccess,
                            dstAccessMask = destinationAccess,
                            dependencyFlags = flags,
                        };
                }
            }
            return dependencyCount;
        }

        private static bool WritesDepthOrStencil(
            ERHISubPassFlags flags) =>
            flags != ERHISubPassFlags.ReadOnlyDepthStencil;
        private static VkAttachmentReference2
            CreateAttachmentReference(
                uint attachment,
                VkImageLayout layout,
                VkImageAspectFlags aspects) =>
            new()
            {
                sType = VkStructureType.AttachmentReference2,
                attachment = attachment,
                layout = layout,
                aspectMask = aspects,
            };

        private static VkAttachmentStoreOp ConvertSourceStoreOp(
            ERHIStoreAction action) =>
            action == ERHIStoreAction.Store ||
            action == ERHIStoreAction.StoreAndResolve
                ? VkAttachmentStoreOp.Store
                : VkAttachmentStoreOp.DontCare;

        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) => mode switch
        {
            EResolveMode.None => VkResolveModeFlags.None,
            EResolveMode.Sample0 => VkResolveModeFlags.SampleZero,
            EResolveMode.Min => VkResolveModeFlags.Min,
            EResolveMode.Max => VkResolveModeFlags.Max,
            _ => VkResolveModeFlags.Average,
        };

        private static T* Allocate<T>(int count)
            where T : unmanaged =>
            count == 0
                ? null
                : (T*)NativeMemory.AllocZeroed(
                    checked((nuint)count),
                    (nuint)sizeof(T));
    }

    internal static class VulkanRasterAspectExtensions
    {
        internal static VkImageAspectFlags ToVkImageAspectFlags(
            this ERHITextureAspectMask aspects)
        {
            VkImageAspectFlags result = 0;
            if ((aspects & ERHITextureAspectMask.Color) != 0)
            {
                result |= VkImageAspectFlags.Color;
            }
            if ((aspects & ERHITextureAspectMask.Depth) != 0)
            {
                result |= VkImageAspectFlags.Depth;
            }
            if ((aspects & ERHITextureAspectMask.Stencil) != 0)
            {
                result |= VkImageAspectFlags.Stencil;
            }
            return result;
        }
    }
}


namespace SharpGPU
{
    internal unsafe sealed class VulkanRasterShaderModuleSet :
        IDisposable
    {
        private readonly VulkanDevice m_Device;
        private readonly VkShaderModule[] m_Modules;
        private readonly VkShaderStageFlags[] m_Stages;
        private readonly IntPtr[] m_EntryNames;
        private bool m_Disposed;

        private VulkanRasterShaderModuleSet(
            VulkanDevice device,
            int stageCount)
        {
            m_Device = device;
            m_Modules = new VkShaderModule[stageCount];
            m_Stages = new VkShaderStageFlags[stageCount];
            m_EntryNames = new IntPtr[stageCount];
        }

        internal int StageCount => m_Modules.Length;

        internal static VulkanRasterShaderModuleSet Create(
            VulkanDevice device,
            in RHIRasterPipelineDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            int stageCount = 0;
            if (descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                ++stageCount;
            }
            if (descriptor.PrimitiveAssembler.MeshletAssembler.HasValue)
            {
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .Value.TaskFunction != null)
                {
                    ++stageCount;
                }
                ++stageCount;
            }
            if (descriptor.FragmentFunction != null)
            {
                ++stageCount;
            }
            if (stageCount == 0)
            {
                throw new ArgumentException(
                    "A Vulkan raster pipeline requires at least one shader stage.",
                    nameof(descriptor));
            }

            VulkanRasterShaderModuleSet result =
                new(device, stageCount);
            try
            {
                int index = 0;
                if (descriptor.PrimitiveAssembler.VertexAssembler
                    .HasValue)
                {
                    result.Add(
                        index++,
                        descriptor.PrimitiveAssembler.VertexAssembler
                            .Value.VertexFunction);
                }
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .HasValue)
                {
                    RHIMeshletAssemblerDescriptor meshlet =
                        descriptor.PrimitiveAssembler.MeshletAssembler
                            .Value;
                    if (meshlet.TaskFunction != null)
                    {
                        result.Add(index++, meshlet.TaskFunction);
                    }
                    if (meshlet.MeshFunction == null)
                    {
                        throw new ArgumentException(
                            "Vulkan mesh pipeline requires a mesh shader function.",
                            nameof(descriptor));
                    }
                    result.Add(index++, meshlet.MeshFunction);
                }
                if (descriptor.FragmentFunction != null)
                {
                    result.Add(index, descriptor.FragmentFunction);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        internal void Populate(
            VkPipelineShaderStageCreateInfo* stages)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }
            for (int index = 0; index < m_Modules.Length; ++index)
            {
                stages[index] =
                    new VkPipelineShaderStageCreateInfo
                    {
                        sType = VkStructureType
                            .PipelineShaderStageCreateInfo,
                        stage = m_Stages[index],
                        module = m_Modules[index],
                        pName = (byte*)m_EntryNames[index],
                    };
            }
        }

        private void Add(int index, RHIFunction function)
        {
            ArgumentNullException.ThrowIfNull(function);
            if (function.IsDisposed)
            {
                throw new ObjectDisposedException(
                    function.GetType().FullName);
            }
            VulkanFunction vulkanFunction =
                function as VulkanFunction ??
                throw new ArgumentException(
                    "Vulkan raster pipelines require Vulkan shader functions.",
                    nameof(function));
            if (!ReferenceEquals(vulkanFunction.VulkanDevice, m_Device))
            {
                throw new ArgumentException(
                    "Vulkan shader function belongs to another device.",
                    nameof(function));
            }
            RHIFunctionDescriptor descriptor = function.Descriptor;
            if (descriptor.PayloadKind !=
                ERHIShaderPayloadKind.SpirV)
            {
                throw new ArgumentException(
                    "Vulkan raster shaders require SPIR-V payloads.",
                    nameof(function));
            }
            ReadOnlySpan<byte> bytecode = vulkanFunction.Bytecode;
            if (bytecode.IsEmpty)
            {
                throw new ArgumentException(
                    "Vulkan raster shader bytecode is empty.",
                    nameof(function));
            }

            VkShaderModule module = default;
            fixed (byte* bytecodePointer = bytecode)
            {
                VkShaderModuleCreateInfo createInfo = new()
                {
                    sType = VkStructureType.ShaderModuleCreateInfo,
                    codeSize = (nuint)bytecode.Length,
                    pCode = (uint*)bytecodePointer,
                };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateShaderModule(
                        m_Device.NativeDevice,
                        &createInfo,
                        null,
                        &module));
            }
            m_Modules[index] = module;
            m_Stages[index] =
                VulkanUtility.ConvertToVkShaderStageBit(
                    descriptor.Type);
            m_EntryNames[index] =
                Marshal.StringToCoTaskMemUTF8(
                    descriptor.EntryName ??
                    throw new ArgumentException(
                        "Vulkan shader entry name is null.",
                        nameof(function)));
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            for (int index = m_Modules.Length - 1;
                 index >= 0;
                 --index)
            {
                if (m_Modules[index].Handle != 0)
                {
                    VulkanNative.vkDestroyShaderModule(
                        m_Device.NativeDevice,
                        m_Modules[index],
                        null);
                    m_Modules[index] = default;
                }
                if (m_EntryNames[index] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(
                        m_EntryNames[index]);
                    m_EntryNames[index] = IntPtr.Zero;
                }
            }
            m_Disposed = true;
        }
    }
}


namespace SharpGPU
{
    internal interface IVulkanRasterNativePipeline
    {
        VkPipeline NativePipeline { get; }
        VkPipelineLayout EffectiveNativePipelineLayout { get; }
        VulkanPrivateRasterBindingPlan PrivateBindingPlan { get; }
        VulkanPrivateRasterDescriptorLayout? PrivateDescriptorLayout
        {
            get;
        }
        bool HasNativePipeline { get; }
    }

    internal unsafe sealed class VulkanRasterNativeVariant :
        IVulkanRasterNativePipeline,
        IDisposable
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VkPipelineLayout EffectiveNativePipelineLayout =>
            m_EffectiveNativePipelineLayout;
        public VulkanPrivateRasterBindingPlan PrivateBindingPlan =>
            m_PrivateBindingPlan;
        public VulkanPrivateRasterDescriptorLayout?
            PrivateDescriptorLayout => m_PrivateDescriptorLayout;
        public bool HasNativePipeline =>
            m_NativePipeline.Handle != 0;

        private readonly VkDevice m_NativeDevice;
        private VkPipeline m_NativePipeline;
        private VkPipelineLayout m_EffectiveNativePipelineLayout;
        private readonly bool m_OwnsEffectiveNativePipelineLayout;
        private readonly VulkanPrivateRasterBindingPlan
            m_PrivateBindingPlan;
        private VulkanPrivateRasterDescriptorLayout?
            m_PrivateDescriptorLayout;
        private bool m_Disposed;

        internal VulkanRasterNativeVariant(
            VkDevice nativeDevice,
            VkPipeline nativePipeline,
            VkPipelineLayout effectiveNativePipelineLayout,
            bool ownsEffectiveNativePipelineLayout,
            in VulkanPrivateRasterBindingPlan privateBindingPlan,
            VulkanPrivateRasterDescriptorLayout?
                privateDescriptorLayout)
        {
            if (nativeDevice.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan device is required.",
                    nameof(nativeDevice));
            }
            if (nativePipeline.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan raster pipeline is required.",
                    nameof(nativePipeline));
            }

            m_NativeDevice = nativeDevice;
            m_NativePipeline = nativePipeline;
            m_EffectiveNativePipelineLayout =
                effectiveNativePipelineLayout;
            m_OwnsEffectiveNativePipelineLayout =
                ownsEffectiveNativePipelineLayout;
            m_PrivateBindingPlan = privateBindingPlan;
            m_PrivateDescriptorLayout = privateDescriptorLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (m_NativePipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(
                    m_NativeDevice,
                    m_NativePipeline,
                    null);
                m_NativePipeline = default;
            }
            if (m_OwnsEffectiveNativePipelineLayout &&
                m_EffectiveNativePipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    m_NativeDevice,
                    m_EffectiveNativePipelineLayout,
                    null);
                m_EffectiveNativePipelineLayout = default;
            }
            m_PrivateDescriptorLayout?.Dispose();
            m_PrivateDescriptorLayout = null;
            m_Disposed = true;
        }
    }
}


namespace SharpGPU
{
    internal static class VulkanRasterStrategyDiagnostics
    {
        private static readonly AsyncLocal<EVulkanRasterPassForcedStrategy>
            s_ForcedStrategy = new();

        internal static EVulkanRasterPassForcedStrategy ForcedStrategy =>
            s_ForcedStrategy.Value;

        internal static IDisposable Push(
            EVulkanRasterPassForcedStrategy strategy)
        {
            if (strategy == EVulkanRasterPassForcedStrategy.Auto)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(strategy),
                    "A diagnostic scope must force a concrete Vulkan raster strategy.");
            }

            EVulkanRasterPassForcedStrategy previous =
                s_ForcedStrategy.Value;
            s_ForcedStrategy.Value = strategy;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly EVulkanRasterPassForcedStrategy m_Previous;
            private bool m_Disposed;

            internal Scope(
                EVulkanRasterPassForcedStrategy previous)
            {
                m_Previous = previous;
            }

            public void Dispose()
            {
                if (m_Disposed)
                {
                    return;
                }

                s_ForcedStrategy.Value = m_Previous;
                m_Disposed = true;
            }
        }
    }
}

