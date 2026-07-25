using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanTextureView : RHITextureView
    {
        public VulkanTexture VulkanTexture { get { ThrowIfDisposed(); return m_VulkanTexture; } }
        public VkImageView NativeImageView { get { ThrowIfDisposed(); return m_NativeImageView; } }
        public RHITextureViewDescriptor Descriptor { get { ThrowIfDisposed(); return m_Descriptor; } }
        internal VulkanDevice Device { get { ThrowIfDisposed(); return m_VulkanTexture.VulkanDevice; } }

        private VulkanTexture m_VulkanTexture;
        private readonly VulkanDevice m_VulkanDevice;
        private VkImageView m_NativeImageView;
        private readonly RHITextureViewDescriptor m_Descriptor;

        public VulkanTextureView(VulkanTexture texture, in RHITextureViewDescriptor descriptor)
        {
            m_VulkanTexture = texture;
            m_VulkanDevice = texture.VulkanDevice;
            m_Descriptor = descriptor;

            VkImageAspectFlags aspect = VkImageAspectFlags.Color;

            VkImageViewCreateInfo viewInfo = new VkImageViewCreateInfo()
            {
                sType = VkStructureType.ImageViewCreateInfo,
                image = texture.NativeImage,
                viewType = VulkanUtility.ConvertToVkImageViewType(texture.Descriptor.Dimension),
                format = VulkanUtility.ConvertToVkFormat(texture.Descriptor.Format),
                components = new VkComponentMapping()
                {
                    r = VkComponentSwizzle.Identity,
                    g = VkComponentSwizzle.Identity,
                    b = VkComponentSwizzle.Identity,
                    a = VkComponentSwizzle.Identity,
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
            ThrowIfDisposed();
            return new VkDescriptorImageInfo()
            {
                imageView = m_NativeImageView,
                imageLayout = layout,
            };
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyImageView(m_VulkanDevice.NativeDevice, m_NativeImageView, null);
        }
    }
}


