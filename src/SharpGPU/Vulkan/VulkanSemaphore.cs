using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanSemaphore : RHISemaphore
    {
        public VkSemaphore NativeSemaphore => m_NativeSemaphore;

        private VulkanDevice m_VulkanDevice;
        private VkSemaphore m_NativeSemaphore;

        public VulkanSemaphore(VulkanDevice device)
        {
            m_VulkanDevice = device;

            VkSemaphoreCreateInfo semaphoreInfo = new VkSemaphoreCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO,
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
#pragma warning restore CS8618
}
