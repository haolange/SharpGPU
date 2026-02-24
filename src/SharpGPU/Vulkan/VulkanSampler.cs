using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanSampler : RHISampler
    {
        public VkSampler NativeSampler => m_NativeSampler;

        private VulkanDevice m_VulkanDevice;
        private VkSampler m_NativeSampler;

        public VulkanSampler(VulkanDevice device, in RHISamplerDescriptor descriptor)
        {
            m_VulkanDevice = device;

            VkSamplerCreateInfo samplerInfo = new VkSamplerCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO,
                magFilter = VulkanUtility.ConvertToVkFilter(descriptor.MagFilter),
                minFilter = VulkanUtility.ConvertToVkFilter(descriptor.MinFilter),
                mipmapMode = VulkanUtility.ConvertToVkMipmapMode(descriptor.MipFilter),
                addressModeU = VulkanUtility.ConvertToVkAddressMode(descriptor.AddressModeU),
                addressModeV = VulkanUtility.ConvertToVkAddressMode(descriptor.AddressModeV),
                addressModeW = VulkanUtility.ConvertToVkAddressMode(descriptor.AddressModeW),
                mipLodBias = descriptor.MipLODBias,
                anisotropyEnable = descriptor.Anisotropy > 1,
                maxAnisotropy = descriptor.Anisotropy,
                compareEnable = descriptor.ComparisonMode != ERHIComparisonMode.Never,
                compareOp = VulkanUtility.ConvertToVkCompareOp(descriptor.ComparisonMode),
                minLod = descriptor.LodMin,
                maxLod = descriptor.LodMax,
                borderColor = VkBorderColor.VK_BORDER_COLOR_INT_OPAQUE_BLACK,
                unnormalizedCoordinates = false,
            };

            fixed (VkSampler* samplerPtr = &m_NativeSampler)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSampler(device.NativeDevice, &samplerInfo, null, samplerPtr));
            }
        }

        public VkDescriptorImageInfo GetDescriptorImageInfo()
        {
            return new VkDescriptorImageInfo()
            {
                sampler = m_NativeSampler,
            };
        }

        protected override void Release()
        {
            VulkanNative.vkDestroySampler(m_VulkanDevice.NativeDevice, m_NativeSampler, null);
        }
    }
#pragma warning restore CS8618
}
