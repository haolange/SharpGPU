using Vortice.Vulkan;

namespace SharpGPU
{
    internal enum EVulkanSampledFeedbackRangeRelation : byte
    {
        DifferentImage = 0,
        Disjoint = 1,
        Exact = 2,
        PartialOverlap = 3,
    }

    internal static class VulkanSampledFeedbackRangeUtility
    {
        internal static EVulkanSampledFeedbackRangeRelation Classify(
            VkImage sampledImage,
            in RHITextureSubresourceRange sampledRange,
            VkImage attachmentImage,
            in RHITextureSubresourceRange attachmentRange)
        {
            if (sampledImage.Handle != attachmentImage.Handle)
            {
                return EVulkanSampledFeedbackRangeRelation.DifferentImage;
            }

            if (!RangesOverlap(in sampledRange, in attachmentRange))
            {
                return EVulkanSampledFeedbackRangeRelation.Disjoint;
            }

            return RangesEqual(in sampledRange, in attachmentRange)
                ? EVulkanSampledFeedbackRangeRelation.Exact
                : EVulkanSampledFeedbackRangeRelation.PartialOverlap;
        }

        private static bool RangesEqual(
            in RHITextureSubresourceRange left,
            in RHITextureSubresourceRange right) =>
            left.AspectMask == right.AspectMask &&
            left.BaseMipLevel == right.BaseMipLevel &&
            left.MipLevelCount == right.MipLevelCount &&
            left.BaseArrayLayer == right.BaseArrayLayer &&
            left.ArrayLayerCount == right.ArrayLayerCount;

        private static bool RangesOverlap(
            in RHITextureSubresourceRange left,
            in RHITextureSubresourceRange right) =>
            (left.AspectMask & right.AspectMask) != 0 &&
            IntervalsOverlap(
                left.BaseMipLevel,
                left.MipLevelCount,
                right.BaseMipLevel,
                right.MipLevelCount) &&
            IntervalsOverlap(
                left.BaseArrayLayer,
                left.ArrayLayerCount,
                right.BaseArrayLayer,
                right.ArrayLayerCount);

        private static bool IntervalsOverlap(
            uint leftStart,
            uint leftCount,
            uint rightStart,
            uint rightCount) =>
            (ulong)leftStart + leftCount > rightStart &&
            (ulong)rightStart + rightCount > leftStart;
    }

    internal static class VulkanRasterCapabilityUtility
    {
        internal static bool HasSampledFeedbackLoweringRoute(
            bool dynamicRendering,
            bool renderPass2) =>
            dynamicRendering || renderPass2;

        internal static bool HasFramebufferLocalReadLoweringRoute(
            bool dynamicRendering,
            bool dynamicRenderingLocalRead,
            bool renderPass2) =>
            (dynamicRendering && dynamicRenderingLocalRead) ||
            renderPass2;
    }
    internal static class VulkanRasterPipelineFlagUtility
    {
        internal static VkPipelineCreateFlags Get(
            in RHIAttachmentInterfaceSignature signature) =>
            (signature.SampledFeedbackMask |
             signature.RasterOrderedReadWriteMask) != 0
                ? VkPipelineCreateFlags.ColorAttachmentFeedbackLoopEXT
                : 0;
    }

    internal static class VulkanRasterFeedbackLoopUtility
    {
        internal static bool TryCreateAttachmentInfo(
            EVulkanRasterAttachmentScopeLayout scopeLayout,
            out VkAttachmentFeedbackLoopInfoEXT info)
        {
            if (scopeLayout !=
                EVulkanRasterAttachmentScopeLayout.GeneralStorage)
            {
                info = default;
                return false;
            }

            info = new VkAttachmentFeedbackLoopInfoEXT
            {
                sType = VkStructureType.AttachmentFeedbackLoopInfoEXT,
                feedbackLoopEnable = true,
            };
            return true;
        }
    }

    internal static class VulkanDepthStencilBarrierUtility
    {
        internal static ERHITextureAspectMask GetNativeAspectMask(
            bool supportsSeparateDepthStencilLayouts,
            ERHIPixelFormat format,
            ERHITextureAspectMask declaredAspects)
        {
            if (supportsSeparateDepthStencilLayouts)
            {
                return declaredAspects;
            }

            VkImageAspectFlags nativeAspects =
                VulkanUtility.GetVkImageAspect(format);
            bool combined =
                (nativeAspects &
                 (VkImageAspectFlags.Depth |
                  VkImageAspectFlags.Stencil)) ==
                (VkImageAspectFlags.Depth |
                 VkImageAspectFlags.Stencil);
            if (!combined ||
                (declaredAspects &
                 (ERHITextureAspectMask.Depth |
                  ERHITextureAspectMask.Stencil)) == 0)
            {
                return declaredAspects;
            }

            return declaredAspects |
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
        }
    }
}
