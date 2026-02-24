using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanCommandQueue : RHICommandQueue
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                return m_VulkanDevice;
            }
        }
        public VkQueue NativeQueue
        {
            get
            {
                return m_NativeQueue;
            }
        }
        public uint QueueFamilyIndex
        {
            get
            {
                return m_QueueFamilyIndex;
            }
        }
        public override ulong Frequency
        {
            get
            {
                VkPhysicalDeviceProperties properties;
                VulkanNative.vkGetPhysicalDeviceProperties(m_VulkanDevice.NativePhysicalDevice, &properties);
                float period = properties.limits.timestampPeriod;
                return period > 0 ? (ulong)(1_000_000_000.0 / period) : 1_000_000_000;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VkQueue m_NativeQueue;
        private uint m_QueueFamilyIndex;

        public VulkanCommandQueue(VulkanDevice device, in ERHIPipelineType pipeline, uint queueFamilyIndex, uint queueIndex)
        {
            m_VulkanDevice = device;
            m_PipelineType = pipeline;
            m_QueueFamilyIndex = queueFamilyIndex;

            fixed (VkQueue* queuePtr = &m_NativeQueue)
            {
                VulkanNative.vkGetDeviceQueue(device.NativeDevice, queueFamilyIndex, queueIndex, queuePtr);
            }
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new VulkanCommandBuffer(this);
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            VkSubmitInfo submitInfo = new VkSubmitInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            };

            VkSemaphore waitSem = default;
            VkPipelineStageFlags waitStage = VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
            if (waitSemaphore != null)
            {
                VulkanSemaphore vkWaitSem = waitSemaphore as VulkanSemaphore;
                waitSem = vkWaitSem.NativeSemaphore;
                submitInfo.waitSemaphoreCount = 1;
                submitInfo.pWaitSemaphores = &waitSem;
                submitInfo.pWaitDstStageMask = &waitStage;
            }

            VkCommandBuffer nativeCmdBuffer = default;
            if (cmdBuffer != null)
            {
                VulkanCommandBuffer vkCmdBuffer = cmdBuffer as VulkanCommandBuffer;
                nativeCmdBuffer = vkCmdBuffer.NativeCommandBuffer;
                submitInfo.commandBufferCount = 1;
                submitInfo.pCommandBuffers = &nativeCmdBuffer;
            }

            VkSemaphore signalSem = default;
            if (signalSemaphore != null)
            {
                VulkanSemaphore vkSignalSem = signalSemaphore as VulkanSemaphore;
                signalSem = vkSignalSem.NativeSemaphore;
                submitInfo.signalSemaphoreCount = 1;
                submitInfo.pSignalSemaphores = &signalSem;
            }

            VkFence fence = default;
            if (signalFence != null)
            {
                VulkanFence vkFence = signalFence as VulkanFence;
                fence = vkFence.NativeFence;
            }

            VulkanUtility.CheckErrors(VulkanNative.vkQueueSubmit(m_NativeQueue, 1, &submitInfo, fence));
        }

        public override void Submits(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            VkSubmitInfo submitInfo = new VkSubmitInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            };

            int waitCount = waitSemaphores != null ? waitSemaphores.Length : 0;
            VkSemaphore* waitSems = stackalloc VkSemaphore[Math.Max(waitCount, 1)];
            VkPipelineStageFlags* waitStages = stackalloc VkPipelineStageFlags[Math.Max(waitCount, 1)];
            if (waitCount > 0)
            {
                for (int i = 0; i < waitCount; ++i)
                {
                    VulkanSemaphore vkSem = waitSemaphores[i] as VulkanSemaphore;
                    waitSems[i] = vkSem.NativeSemaphore;
                    waitStages[i] = VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
                }
                submitInfo.waitSemaphoreCount = (uint)waitCount;
                submitInfo.pWaitSemaphores = waitSems;
                submitInfo.pWaitDstStageMask = waitStages;
            }

            VkCommandBuffer nativeCmdBuffer = default;
            if (cmdBuffer != null)
            {
                VulkanCommandBuffer vkCmdBuffer = cmdBuffer as VulkanCommandBuffer;
                nativeCmdBuffer = vkCmdBuffer.NativeCommandBuffer;
                submitInfo.commandBufferCount = 1;
                submitInfo.pCommandBuffers = &nativeCmdBuffer;
            }

            int signalCount = signalSemaphores != null ? signalSemaphores.Length : 0;
            VkSemaphore* signalSems = stackalloc VkSemaphore[Math.Max(signalCount, 1)];
            if (signalCount > 0)
            {
                for (int i = 0; i < signalCount; ++i)
                {
                    VulkanSemaphore vkSem = signalSemaphores[i] as VulkanSemaphore;
                    signalSems[i] = vkSem.NativeSemaphore;
                }
                submitInfo.signalSemaphoreCount = (uint)signalCount;
                submitInfo.pSignalSemaphores = signalSems;
            }

            VkFence fence = default;
            if (signalFence != null)
            {
                VulkanFence vkFence = signalFence as VulkanFence;
                fence = vkFence.NativeFence;
            }

            VulkanUtility.CheckErrors(VulkanNative.vkQueueSubmit(m_NativeQueue, 1, &submitInfo, fence));
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            VkSubmitInfo submitInfo = new VkSubmitInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO,
            };

            int waitCount = waitSemaphores != null ? waitSemaphores.Length : 0;
            VkSemaphore* waitSems = stackalloc VkSemaphore[Math.Max(waitCount, 1)];
            VkPipelineStageFlags* waitStages = stackalloc VkPipelineStageFlags[Math.Max(waitCount, 1)];
            if (waitCount > 0)
            {
                for (int i = 0; i < waitCount; ++i)
                {
                    VulkanSemaphore vkSem = waitSemaphores[i] as VulkanSemaphore;
                    waitSems[i] = vkSem.NativeSemaphore;
                    waitStages[i] = VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
                }
                submitInfo.waitSemaphoreCount = (uint)waitCount;
                submitInfo.pWaitSemaphores = waitSems;
                submitInfo.pWaitDstStageMask = waitStages;
            }

            int cmdCount = cmdBuffers != null ? cmdBuffers.Length : 0;
            VkCommandBuffer* nativeCmdBuffers = stackalloc VkCommandBuffer[Math.Max(cmdCount, 1)];
            if (cmdCount > 0)
            {
                for (int i = 0; i < cmdCount; ++i)
                {
                    VulkanCommandBuffer vkCmdBuf = cmdBuffers[i] as VulkanCommandBuffer;
                    nativeCmdBuffers[i] = vkCmdBuf.NativeCommandBuffer;
                }
                submitInfo.commandBufferCount = (uint)cmdCount;
                submitInfo.pCommandBuffers = nativeCmdBuffers;
            }

            int signalCount = signalSemaphores != null ? signalSemaphores.Length : 0;
            VkSemaphore* signalSems = stackalloc VkSemaphore[Math.Max(signalCount, 1)];
            if (signalCount > 0)
            {
                for (int i = 0; i < signalCount; ++i)
                {
                    VulkanSemaphore vkSem = signalSemaphores[i] as VulkanSemaphore;
                    signalSems[i] = vkSem.NativeSemaphore;
                }
                submitInfo.signalSemaphoreCount = (uint)signalCount;
                submitInfo.pSignalSemaphores = signalSems;
            }

            VkFence fence = default;
            if (signalFence != null)
            {
                VulkanFence vkFence = signalFence as VulkanFence;
                fence = vkFence.NativeFence;
            }

            VulkanUtility.CheckErrors(VulkanNative.vkQueueSubmit(m_NativeQueue, 1, &submitInfo, fence));
        }

        public override void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BIND_SPARSE_INFO,
            };

            VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default);
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BIND_SPARSE_INFO,
            };

            VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default);
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BIND_SPARSE_INFO,
            };

            VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default);
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BIND_SPARSE_INFO,
            };

            VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default);
        }

        protected override void Release()
        {
            // VkQueue does not need explicit destruction
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
