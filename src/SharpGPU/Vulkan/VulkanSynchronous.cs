using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    #region Fence
internal unsafe class VulkanFence : RHIFence
    {
        public VkFence NativeFence => m_NativeFence;

        public override ERHIFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return ERHIFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VkResult result = VulkanNative.vkGetFenceStatus(m_VulkanDevice.NativeDevice, m_NativeFence);
                if (result == VkResult.Success)
                {
                    MarkSignaled();
                    return ERHIFenceStatus.Success;
                }

                if (result == VkResult.NotReady)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
                return ERHIFenceStatus.Undefined;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VkFence m_NativeFence;

        public VulkanFence(VulkanDevice device) : base(device)
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
            if (IsSignalPending)
            {
                _ = Status;
            }

            if (!BeginReset())
            {
                return;
            }

            try
            {
                fixed (VkFence* fencePtr = &m_NativeFence)
                {
                    VulkanUtility.CheckErrors(VulkanNative.vkResetFences(m_VulkanDevice.NativeDevice, 1, fencePtr));
                }

                CompleteReset();
            }
            catch
            {
                RollbackReset();
                throw;
            }
        }

        public override ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return ERHIFenceStatus.Success;
            }

            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VkResult result = VulkanNative.vkWaitForFences(
                    m_VulkanDevice.NativeDevice,
                    1,
                    fencePtr,
                    true,
                    timeoutNanoseconds);
                if (result == VkResult.Timeout)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
            }

            MarkSignaled();
            return ERHIFenceStatus.Success;
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyFence(m_VulkanDevice.NativeDevice, m_NativeFence, null);
        }
    }
    #endregion

    #region Semaphore
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
    #endregion
}
