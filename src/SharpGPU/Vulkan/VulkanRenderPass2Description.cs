using System;
using System.Collections.Generic;

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
