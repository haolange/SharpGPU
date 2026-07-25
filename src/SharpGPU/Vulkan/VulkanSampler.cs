using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanSampler : RHISampler
    {
        public VkSampler NativeSampler { get { ThrowIfDisposed(); return m_NativeSampler; } }
        internal VulkanDevice Device { get { ThrowIfDisposed(); return m_VulkanDevice; } }

        private VulkanDevice m_VulkanDevice;
        private VkSampler m_NativeSampler;

        public VulkanSampler(VulkanDevice device, in RHISamplerDescriptor descriptor)
        {
            m_VulkanDevice = device;

            VkSamplerCreateInfo samplerInfo = new VkSamplerCreateInfo()
            {
                sType = VkStructureType.SamplerCreateInfo,
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
                borderColor = VkBorderColor.IntOpaqueBlack,
                unnormalizedCoordinates = false,
            };

            fixed (VkSampler* samplerPtr = &m_NativeSampler)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSampler(device.NativeDevice, &samplerInfo, null, samplerPtr));
            }
        }

        public VkDescriptorImageInfo GetDescriptorImageInfo()
        {
            ThrowIfDisposed();
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
}


