using Vortice.Vulkan;

namespace SharpGPU
{
    internal readonly struct VulkanSampledFeedbackAttachmentFact
    {
        internal VkImage Image { get; }
        internal RHITextureSubresourceRange Range { get; }
        internal byte AttachmentMask { get; }

        internal VulkanSampledFeedbackAttachmentFact(
            VkImage image,
            in RHITextureSubresourceRange range,
            byte attachmentMask)
        {
            Image = image;
            Range = range;
            AttachmentMask = attachmentMask;
        }
    }

    internal readonly struct VulkanSampledImageDescriptorFact
    {
        internal bool IsBound { get; }
        internal VkImage Image { get; }
        internal VkImageView ImageView { get; }
        internal RHITextureSubresourceRange Range { get; }

        internal VulkanSampledImageDescriptorFact(
            VkImage image,
            VkImageView imageView,
            in RHITextureSubresourceRange range)
        {
            IsBound = true;
            Image = image;
            ImageView = imageView;
            Range = range;
        }
    }
}
