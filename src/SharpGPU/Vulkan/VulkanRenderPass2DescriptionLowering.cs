using System;
using Vortice.Vulkan;

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
