using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanFence : RHIFence
    {
        public VkFence NativeFence => m_NativeFence;

        public override EFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return EFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return EFenceStatus.NotReady;
                }

                VkResult result = VulkanNative.vkGetFenceStatus(m_VulkanDevice.NativeDevice, m_NativeFence);
                if (result == VkResult.Success)
                {
                    MarkSignaled();
                    return EFenceStatus.Success;
                }

                if (result == VkResult.NotReady)
                {
                    return EFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
                return EFenceStatus.Undefined;
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

        public override EFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return EFenceStatus.Success;
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
                    return EFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
            }

            MarkSignaled();
            return EFenceStatus.Success;
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyFence(m_VulkanDevice.NativeDevice, m_NativeFence, null);
        }
    }
}


