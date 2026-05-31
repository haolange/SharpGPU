using System;
using System.IO;
using Vortice.Vulkan;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanStorageQueue : RHIStorageQueue
    {
        private VulkanDevice m_VulkanDevice;
        private VkCommandPool m_CommandPool;
        private VkCommandBuffer m_CommandBuffer;
        private VkQueue m_TransferQueue;

        public VulkanStorageQueue(VulkanDevice device)
        {
            m_VulkanDevice = device;

            int transferFamily = device.TransferQueueFamilyIndex;
            uint queueFamilyIndex = (uint)(transferFamily >= 0 ? transferFamily : device.GraphicsQueueFamilyIndex);

            fixed (VkQueue* queuePtr = &m_TransferQueue)
            {
                VulkanNative.vkGetDeviceQueue(device.NativeDevice, queueFamilyIndex, 0, queuePtr);
            }

            VkCommandPoolCreateInfo poolInfo = new VkCommandPoolCreateInfo()
            {
                sType = VkStructureType.CommandPoolCreateInfo,
                flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
                queueFamilyIndex = queueFamilyIndex,
            };

            fixed (VkCommandPool* poolPtr = &m_CommandPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateCommandPool(device.NativeDevice, &poolInfo, null, poolPtr));
            }

            VkCommandBufferAllocateInfo allocInfo = new VkCommandBufferAllocateInfo()
            {
                sType = VkStructureType.CommandBufferAllocateInfo,
                commandPool = m_CommandPool,
                level = VkCommandBufferLevel.Primary,
                commandBufferCount = 1,
            };

            fixed (VkCommandBuffer* cmdBufPtr = &m_CommandBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateCommandBuffers(device.NativeDevice, &allocInfo, cmdBufPtr));
            }
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            IntPtr handle = IntPtr.Zero;

            if (File.Exists(absPath))
            {
                FileStream fs = File.OpenRead(absPath);
                handle = GCHandle.ToIntPtr(GCHandle.Alloc(fs));
            }

            return new RHIStorageFileHandle { NativeHandle = handle };
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            if (fileHandle.NativeHandle != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(fileHandle.NativeHandle);
                if (gcHandle.Target is FileStream fs)
                {
                    fs.Close();
                    fs.Dispose();
                }
                gcHandle.Free();
            }
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            if (fileHandle.NativeHandle != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(fileHandle.NativeHandle);
                if (gcHandle.Target is FileStream fs)
                {
                    return (ulong)fs.Length;
                }
            }
            return 0;
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            if (request.FileHandle.NativeHandle == IntPtr.Zero) return;

            GCHandle gcHandle = GCHandle.FromIntPtr(request.FileHandle.NativeHandle);
            if (gcHandle.Target is FileStream fs)
            {
                VulkanBuffer vkDstBuffer = request.DestinationBuffer as VulkanBuffer;

                // Create staging buffer
                RHIBufferDescriptor stagingDesc = new RHIBufferDescriptor()
                {
                    ByteSize = (int)request.FileSize,
                    UsageFlag = ERHIBufferUsage.CopySrc,
                    StorageMode = ERHIStorageMode.HostUpload,
                };

                VulkanBuffer stagingBuffer = new VulkanBuffer(m_VulkanDevice, stagingDesc);

                // Map and copy data
                void* data;
                VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory, 0, (ulong)request.FileSize, 0, &data);
                byte[] fileData = new byte[request.FileSize];
                fs.Seek((long)request.FileOffset, SeekOrigin.Begin);
                fs.ReadExactly(fileData, 0, (int)request.FileSize);
                Marshal.Copy(fileData, 0, new IntPtr(data), (int)request.FileSize);
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory);

                // Record copy command
                VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
                {
                    sType = VkStructureType.CommandBufferBeginInfo,
                    flags = VkCommandBufferUsageFlags.OneTimeSubmit,
                };
                VulkanNative.vkBeginCommandBuffer(m_CommandBuffer, &beginInfo);

                VkBufferCopy copyRegion = new VkBufferCopy()
                {
                    srcOffset = 0,
                    dstOffset = request.DestinationOffset,
                    size = request.FileSize,
                };
                VulkanNative.vkCmdCopyBuffer(m_CommandBuffer, stagingBuffer.NativeBuffer, vkDstBuffer.NativeBuffer, 1, &copyRegion);
                VulkanNative.vkEndCommandBuffer(m_CommandBuffer);

                // Submit
                VkCommandBuffer cmdBuf = m_CommandBuffer;
                VkSubmitInfo submitInfo = new VkSubmitInfo()
                {
                    sType = VkStructureType.SubmitInfo,
                    commandBufferCount = 1,
                    pCommandBuffers = &cmdBuf,
                };
                VulkanNative.vkQueueSubmit(m_TransferQueue, 1, &submitInfo, default);
                VulkanNative.vkQueueWaitIdle(m_TransferQueue);

                stagingBuffer.Dispose();
            }
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            if (request.FileHandle.NativeHandle == IntPtr.Zero) return;

            GCHandle gcHandle = GCHandle.FromIntPtr(request.FileHandle.NativeHandle);
            if (gcHandle.Target is FileStream fs)
            {
                VulkanTexture vkDstTexture = request.DestinationTexture as VulkanTexture;

                // Create staging buffer
                RHIBufferDescriptor stagingDesc = new RHIBufferDescriptor()
                {
                    ByteSize = (int)request.FileSize,
                    UsageFlag = ERHIBufferUsage.CopySrc,
                    StorageMode = ERHIStorageMode.HostUpload,
                };

                VulkanBuffer stagingBuffer = new VulkanBuffer(m_VulkanDevice, stagingDesc);

                // Map and copy file data to staging buffer
                void* data;
                VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory, 0, (ulong)request.FileSize, 0, &data);
                byte[] fileData = new byte[request.FileSize];
                fs.Seek((long)request.FileOffset, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < (int)request.FileSize)
                {
                    int read = fs.Read(fileData, totalRead, (int)request.FileSize - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                Marshal.Copy(fileData, 0, new IntPtr(data), totalRead);
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory);

                // Record copy commands
                VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
                {
                    sType = VkStructureType.CommandBufferBeginInfo,
                    flags = VkCommandBufferUsageFlags.OneTimeSubmit,
                };
                VulkanNative.vkBeginCommandBuffer(m_CommandBuffer, &beginInfo);

                // Transition image to TRANSFER_DST
                VkImageMemoryBarrier preCopyBarrier = new VkImageMemoryBarrier()
                {
                    sType = VkStructureType.ImageMemoryBarrier,
                    srcAccessMask = 0,
                    dstAccessMask = VkAccessFlags.TransferWrite,
                    oldLayout = VkImageLayout.Undefined,
                    newLayout = VkImageLayout.TransferDstOptimal,
                    image = vkDstTexture.NativeImage,
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        baseMipLevel = 0,
                        levelCount = 1,
                        baseArrayLayer = 0,
                        layerCount = 1,
                    },
                };
                EmitImageBarrier(
                    m_CommandBuffer,
                    &preCopyBarrier,
                    VkPipelineStageFlags.TopOfPipe,
                    VkPipelineStageFlags.Transfer,
                    VkPipelineStageFlags2.TopOfPipe,
                    VkPipelineStageFlags2.Transfer);

                // Copy buffer to image
                VkBufferImageCopy region = new VkBufferImageCopy()
                {
                    bufferOffset = 0,
                    bufferRowLength = 0,
                    bufferImageHeight = 0,
                    imageSubresource = new VkImageSubresourceLayers()
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        mipLevel = 0,
                        baseArrayLayer = 0,
                        layerCount = 1,
                    },
                    imageOffset = new VkOffset3D() { x = 0, y = 0, z = 0 },
                    imageExtent = new VkExtent3D()
                    {
                        width = vkDstTexture.Descriptor.Extent.x,
                        height = vkDstTexture.Descriptor.Extent.y,
                        depth = Math.Max(1u, vkDstTexture.Descriptor.Extent.z),
                    },
                };
                VulkanNative.vkCmdCopyBufferToImage(m_CommandBuffer, stagingBuffer.NativeBuffer, vkDstTexture.NativeImage, VkImageLayout.TransferDstOptimal, 1, &region);

                // Transition image to SHADER_READ
                VkImageMemoryBarrier postCopyBarrier = preCopyBarrier;
                postCopyBarrier.srcAccessMask = VkAccessFlags.TransferWrite;
                postCopyBarrier.dstAccessMask = VkAccessFlags.ShaderRead;
                postCopyBarrier.oldLayout = VkImageLayout.TransferDstOptimal;
                postCopyBarrier.newLayout = VkImageLayout.ShaderReadOnlyOptimal;
                EmitImageBarrier(
                    m_CommandBuffer,
                    &postCopyBarrier,
                    VkPipelineStageFlags.Transfer,
                    VkPipelineStageFlags.FragmentShader,
                    VkPipelineStageFlags2.Transfer,
                    VkPipelineStageFlags2.FragmentShader);

                VulkanNative.vkEndCommandBuffer(m_CommandBuffer);

                // Submit
                VkCommandBuffer cmdBuf = m_CommandBuffer;
                VkSubmitInfo submitInfo = new VkSubmitInfo()
                {
                    sType = VkStructureType.SubmitInfo,
                    commandBufferCount = 1,
                    pCommandBuffers = &cmdBuf,
                };
                VulkanNative.vkQueueSubmit(m_TransferQueue, 1, &submitInfo, default);
                VulkanNative.vkQueueWaitIdle(m_TransferQueue);

                stagingBuffer.Dispose();
            }
        }

        private void EmitImageBarrier(VkCommandBuffer commandBuffer,
                                      VkImageMemoryBarrier* sync1Barrier,
                                      VkPipelineStageFlags sync1SrcStage,
                                      VkPipelineStageFlags sync1DstStage,
                                      VkPipelineStageFlags2 sync2SrcStage,
                                      VkPipelineStageFlags2 sync2DstStage)
        {
            if (m_VulkanDevice.UseSynchronization2)
            {
                VkImageMemoryBarrier2 sync2Barrier = new VkImageMemoryBarrier2()
                {
                    sType = VkStructureType.ImageMemoryBarrier2,
                    srcStageMask = sync2SrcStage,
                    srcAccessMask = ConvertToVkAccessFlags2(sync1Barrier->srcAccessMask),
                    dstStageMask = sync2DstStage,
                    dstAccessMask = ConvertToVkAccessFlags2(sync1Barrier->dstAccessMask),
                    oldLayout = sync1Barrier->oldLayout,
                    newLayout = sync1Barrier->newLayout,
                    srcQueueFamilyIndex = sync1Barrier->srcQueueFamilyIndex,
                    dstQueueFamilyIndex = sync1Barrier->dstQueueFamilyIndex,
                    image = sync1Barrier->image,
                    subresourceRange = sync1Barrier->subresourceRange,
                };

                VkDependencyInfo dependencyInfo = new VkDependencyInfo()
                {
                    sType = VkStructureType.DependencyInfo,
                    imageMemoryBarrierCount = 1,
                    pImageMemoryBarriers = &sync2Barrier,
                };

                if (m_VulkanDevice.UseSynchronization2KhrCommand)
                {
                    VulkanNative.vkCmdPipelineBarrier2KHR(commandBuffer, &dependencyInfo);
                }
                else
                {
                    VulkanNative.vkCmdPipelineBarrier2(commandBuffer, &dependencyInfo);
                }
                return;
            }

            VulkanNative.vkCmdPipelineBarrier(commandBuffer,
                sync1SrcStage,
                sync1DstStage,
                0, 0, null, 0, null, 1, sync1Barrier);
        }

        private static VkAccessFlags2 ConvertToVkAccessFlags2(VkAccessFlags accessFlags)
        {
            VkAccessFlags2 result = VkAccessFlags2.None;
            if ((accessFlags & VkAccessFlags.IndirectCommandRead) != 0) result |= VkAccessFlags2.IndirectCommandRead;
            if ((accessFlags & VkAccessFlags.IndexRead) != 0) result |= VkAccessFlags2.IndexRead;
            if ((accessFlags & VkAccessFlags.VertexAttributeRead) != 0) result |= VkAccessFlags2.VertexAttributeRead;
            if ((accessFlags & VkAccessFlags.UniformRead) != 0) result |= VkAccessFlags2.UniformRead;
            if ((accessFlags & VkAccessFlags.ShaderRead) != 0) result |= VkAccessFlags2.ShaderRead;
            if ((accessFlags & VkAccessFlags.ShaderWrite) != 0) result |= VkAccessFlags2.ShaderWrite;
            if ((accessFlags & VkAccessFlags.ColorAttachmentRead) != 0) result |= VkAccessFlags2.ColorAttachmentRead;
            if ((accessFlags & VkAccessFlags.ColorAttachmentWrite) != 0) result |= VkAccessFlags2.ColorAttachmentWrite;
            if ((accessFlags & VkAccessFlags.DepthStencilAttachmentRead) != 0) result |= VkAccessFlags2.DepthStencilAttachmentRead;
            if ((accessFlags & VkAccessFlags.DepthStencilAttachmentWrite) != 0) result |= VkAccessFlags2.DepthStencilAttachmentWrite;
            if ((accessFlags & VkAccessFlags.TransferRead) != 0) result |= VkAccessFlags2.TransferRead;
            if ((accessFlags & VkAccessFlags.TransferWrite) != 0) result |= VkAccessFlags2.TransferWrite;
            if ((accessFlags & VkAccessFlags.AccelerationStructureReadKHR) != 0) result |= VkAccessFlags2.AccelerationStructureReadKHR;
            if ((accessFlags & VkAccessFlags.AccelerationStructureWriteKHR) != 0) result |= VkAccessFlags2.AccelerationStructureWriteKHR;
            if ((accessFlags & VkAccessFlags.MemoryRead) != 0) result |= VkAccessFlags2.MemoryRead;
            if ((accessFlags & VkAccessFlags.MemoryWrite) != 0) result |= VkAccessFlags2.MemoryWrite;
            return result;
        }

        public override void Submit(RHIFence signalFence)
        {
            if (signalFence != null)
            {
                VulkanFence vkFence = signalFence as VulkanFence;
                vkFence.Reset();

                VkSubmitInfo submitInfo = new VkSubmitInfo()
                {
                    sType = VkStructureType.SubmitInfo,
                    commandBufferCount = 0,
                };
                VulkanNative.vkQueueSubmit(m_TransferQueue, 1, &submitInfo, vkFence.NativeFence);
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyCommandPool(m_VulkanDevice.NativeDevice, m_CommandPool, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}

