using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanSemaphore : RHISemaphore
    {
        public VkSemaphore NativeSemaphore => m_NativeSemaphore;

        private VulkanDevice m_VulkanDevice;
        private VkSemaphore m_NativeSemaphore;

        public VulkanSemaphore(VulkanDevice device) : base(device)
        {
            m_VulkanDevice = device;

            VkSemaphoreCreateInfo semaphoreInfo = new VkSemaphoreCreateInfo()
            {
                sType = VkStructureType.SemaphoreCreateInfo,
            };

            fixed (VkSemaphore* semPtr = &m_NativeSemaphore)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSemaphore(device.NativeDevice, &semaphoreInfo, null, semPtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroySemaphore(m_VulkanDevice.NativeDevice, m_NativeSemaphore, null);
        }
    }
}


