using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanTextureView : RHITextureView
    {
        public VulkanTexture VulkanTexture => m_VulkanTexture;
        public VkImageView NativeImageView => m_NativeImageView;

        private VulkanTexture m_VulkanTexture;
        private VkImageView m_NativeImageView;

        public VulkanTextureView(VulkanTexture texture, in RHITextureViewDescriptor descriptor)
        {
            m_VulkanTexture = texture;

            VkImageAspectFlags aspect = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;

            VkImageViewCreateInfo viewInfo = new VkImageViewCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                image = texture.NativeImage,
                viewType = VulkanUtility.ConvertToVkImageViewType(texture.Descriptor.Dimension),
                format = VulkanUtility.ConvertToVkFormat(texture.Descriptor.Format),
                components = new VkComponentMapping()
                {
                    r = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                    g = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                    b = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                    a = VkComponentSwizzle.VK_COMPONENT_SWIZZLE_IDENTITY,
                },
                subresourceRange = new VkImageSubresourceRange()
                {
                    aspectMask = aspect,
                    baseMipLevel = descriptor.BaseMipLevel,
                    levelCount = descriptor.MipCount,
                    baseArrayLayer = descriptor.BaseArraySlice,
                    layerCount = descriptor.ArrayCount > 0 ? descriptor.ArrayCount : 1,
                },
            };

            fixed (VkImageView* viewPtr = &m_NativeImageView)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateImageView(texture.VulkanDevice.NativeDevice, &viewInfo, null, viewPtr));
            }
        }

        public VkDescriptorImageInfo GetDescriptorImageInfo(VkImageLayout layout)
        {
            return new VkDescriptorImageInfo()
            {
                imageView = m_NativeImageView,
                imageLayout = layout,
            };
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyImageView(m_VulkanTexture.VulkanDevice.NativeDevice, m_NativeImageView, null);
        }
    }
#pragma warning restore CS8618
}
