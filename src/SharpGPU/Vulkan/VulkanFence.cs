using System;
using Vortice.Vulkan;

namespace SharpGPU
{
#pragma warning disable CS8618
    internal unsafe class VulkanFence : RHIFence
    {
        public VkFence NativeFence => m_NativeFence;

        public override EFenceStatus Status
        {
            get
            {
                VkResult result = VulkanNative.vkGetFenceStatus(m_VulkanDevice.NativeDevice, m_NativeFence);
                return result == VkResult.Success ? EFenceStatus.Success : EFenceStatus.NotReady;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VkFence m_NativeFence;

        public VulkanFence(VulkanDevice device)
        {
            m_VulkanDevice = device;

            VkFenceCreateInfo fenceInfo = new VkFenceCreateInfo()
            {
                sType = VkStructureType.FenceCreateInfo,
                flags = 0,
            };

            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateFence(device.NativeDevice, &fenceInfo, null, fencePtr));
            }
        }

        public override void Reset()
        {
            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkResetFences(m_VulkanDevice.NativeDevice, 1, fencePtr));
            }
        }

        public override void Wait()
        {
            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkWaitForFences(m_VulkanDevice.NativeDevice, 1, fencePtr, true, ulong.MaxValue));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyFence(m_VulkanDevice.NativeDevice, m_NativeFence, null);
        }
    }
#pragma warning restore CS8618
}


