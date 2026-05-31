using System;
using Vortice.Vulkan;

namespace SharpGPU
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
                sType = VkStructureType.SubmitInfo,
            };

            VkSemaphore waitSem = default;
            VkPipelineStageFlags waitStage = VkPipelineStageFlags.AllCommands;
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
                sType = VkStructureType.SubmitInfo,
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
                    waitStages[i] = VkPipelineStageFlags.AllCommands;
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
                sType = VkStructureType.SubmitInfo,
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
                    waitStages[i] = VkPipelineStageFlags.AllCommands;
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
            BindSparseTextureRegions(tiledTextureRegions, bind: true);
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            BindSparseTextureRegions(tiledTextureRegions, bind: false);
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            BindSparsePackedMips(tiledTexturePackedMips, bind: true);
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            BindSparsePackedMips(tiledTexturePackedMips, bind: false);
        }

        private void BindSparseTextureRegions(in RHITiledTextureRegions tiledTextureRegions, bool bind)
        {
            VulkanTexture vkTexture = tiledTextureRegions.Texture as VulkanTexture;
            int regionCount = tiledTextureRegions.Regions.Length;
            if (regionCount == 0) return;

            VkSparseImageMemoryBind* imageBinds = stackalloc VkSparseImageMemoryBind[regionCount];

            for (int i = 0; i < regionCount; ++i)
            {
                ref RHITextureCoordinateRegion region = ref tiledTextureRegions.Regions.Span[i];

                imageBinds[i] = new VkSparseImageMemoryBind()
                {
                    subresource = new VkImageSubresource()
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        mipLevel = (uint)region.MipLevel,
                        arrayLayer = (uint)region.Layer,
                    },
                    offset = new VkOffset3D() { x = region.Start.X, y = region.Start.Y, z = region.Start.Z },
                    extent = new VkExtent3D()
                    {
                        width = (uint)(region.End.X - region.Start.X),
                        height = (uint)(region.End.Y - region.Start.Y),
                        depth = (uint)Math.Max(region.End.Z - region.Start.Z, 1),
                    },
                    memory = bind ? vkTexture.NativeMemory : default,
                    memoryOffset = 0,
                    flags = 0,
                };
            }

            VkSparseImageMemoryBindInfo imageMemoryBindInfo = new VkSparseImageMemoryBindInfo()
            {
                image = vkTexture.NativeImage,
                bindCount = (uint)regionCount,
                pBinds = imageBinds,
            };

            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.BindSparseInfo,
                imageBindCount = 1,
                pImageBinds = &imageMemoryBindInfo,
            };

            VulkanUtility.CheckErrors(VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default));
        }

        private void BindSparsePackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips, bool bind)
        {
            int packedMipCount = tiledTexturePackedMips.PackedMips.Length;
            if (packedMipCount == 0) return;

            // For packed mips, use opaque sparse binds (VkSparseImageOpaqueMemoryBindInfo)
            // since packed mip tails use opaque bindings rather than per-subresource bindings
            VkSparseMemoryBind* opaqueBinds = stackalloc VkSparseMemoryBind[packedMipCount];

            for (int i = 0; i < packedMipCount; ++i)
            {
                opaqueBinds[i] = new VkSparseMemoryBind()
                {
                    resourceOffset = 0,
                    size = 0,
                    memory = bind ? default : default,
                    memoryOffset = 0,
                    flags = VkSparseMemoryBindFlags.Metadata,
                };
            }

            // Use the first packed mip's texture for the image
            VulkanTexture vkTexture = tiledTexturePackedMips.PackedMips.Span[0].Texture as VulkanTexture;

            VkSparseImageOpaqueMemoryBindInfo opaqueBindInfo = new VkSparseImageOpaqueMemoryBindInfo()
            {
                image = vkTexture.NativeImage,
                bindCount = (uint)packedMipCount,
                pBinds = opaqueBinds,
            };

            VkBindSparseInfo bindInfo = new VkBindSparseInfo()
            {
                sType = VkStructureType.BindSparseInfo,
                imageOpaqueBindCount = 1,
                pImageOpaqueBinds = &opaqueBindInfo,
            };

            VulkanUtility.CheckErrors(VulkanNative.vkQueueBindSparse(m_NativeQueue, 1, &bindInfo, default));
        }

        protected override void Release()
        {
            // VkQueue does not need explicit destruction
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}


