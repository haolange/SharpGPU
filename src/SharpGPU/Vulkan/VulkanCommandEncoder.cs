using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using Evergine.Bindings.Vulkan;
using Viewport = Infinity.Mathmatics.Viewport;

namespace Infinity.Graphics
{
#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8604, CS8618

    // ========== Transfer Encoder ==========
    internal unsafe class VulkanTransferEncoder : RHITransferEncoder
    {
        private RHITransferPassDescriptor m_PassDescriptor;

        public VulkanTransferEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.Triansition:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        VulkanBuffer vkBuffer = barrier.BufferBarrierInfo.Handle as VulkanBuffer;
                        VkBufferMemoryBarrier bufferBarrier = new VkBufferMemoryBarrier()
                        {
                            sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
                            srcAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.SrcState),
                            dstAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.DstState),
                            buffer = vkBuffer.NativeBuffer,
                            offset = 0,
                            size = unchecked((ulong)(-1)),
                        };
                        VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                            VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                            VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                            0, 0, null, 1, &bufferBarrier, 0, null);
                    }
                    else
                    {
                        VulkanTexture vkTexture = barrier.TextureBarrierInfo.Handle as VulkanTexture;
                        VkImageMemoryBarrier imageBarrier = new VkImageMemoryBarrier()
                        {
                            sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                            srcAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.SrcState),
                            dstAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.DstState),
                            oldLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.SrcState),
                            newLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.DstState),
                            srcQueueFamilyIndex = unchecked((uint)(-1)),
                            dstQueueFamilyIndex = unchecked((uint)(-1)),
                            image = vkTexture.NativeImage,
                            subresourceRange = new VkImageSubresourceRange()
                            {
                                aspectMask = VulkanUtility.GetVkImageAspect(vkTexture.Descriptor.Format),
                                baseMipLevel = 0,
                                levelCount = unchecked((uint)(-1)),
                                baseArrayLayer = 0,
                                layerCount = unchecked((uint)(-1)),
                            },
                        };
                        VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                            VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                            VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                            0, 0, null, 0, null, 1, &imageBarrier);
                    }
                    break;

                case ERHIResourceBarrierType.UAV:
                {
                    VkMemoryBarrier memBarrier = new VkMemoryBarrier()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_BARRIER,
                        srcAccessMask = VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT,
                        dstAccessMask = VkAccessFlags.VK_ACCESS_SHADER_READ_BIT | VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT,
                    };
                    VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,
                        0, 1, &memBarrier, 0, null, 0, null);
                    break;
                }
            }
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            for (int i = 0; i < barriers.Length; ++i)
            {
                ResourceBarrier(barriers.Span[i]);
            }
        }

        public override void PushDebugGroup(string name)
        {
            // Debug marker support via VK_EXT_debug_utils
        }

        public override void PopDebugGroup()
        {
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT, vkQuery.NativeQueryPool, index);
            }
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanQuery vkQuery = query as VulkanQuery;
            VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, startIndex, queriesCount);
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkSrc = srcBuffer as VulkanBuffer;
            VulkanBuffer vkDst = dstBuffer as VulkanBuffer;

            VkBufferCopy region = new VkBufferCopy()
            {
                srcOffset = (ulong)srcOffset,
                dstOffset = (ulong)dstOffset,
                size = (ulong)size,
            };

            VulkanNative.vkCmdCopyBuffer(vkCmdBuf.NativeCommandBuffer, vkSrc.NativeBuffer, vkDst.NativeBuffer, 1, &region);
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkSrcBuffer = src.Buffer as VulkanBuffer;
            VulkanTexture vkDstTexture = dst.Texture as VulkanTexture;

            VkBufferImageCopy region = new VkBufferImageCopy()
            {
                bufferOffset = src.Offset,
                bufferRowLength = 0,
                bufferImageHeight = 0,
                imageSubresource = new VkImageSubresourceLayers()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkDstTexture.Descriptor.Format),
                    mipLevel = dst.MipLevel,
                    baseArrayLayer = dst.SliceBase,
                    layerCount = dst.SliceCount > 0 ? dst.SliceCount : 1,
                },
                imageOffset = new VkOffset3D() { x = (int)dst.Origin.x, y = (int)dst.Origin.y, z = (int)dst.Origin.z },
                imageExtent = new VkExtent3D() { width = (uint)size.x, height = (uint)size.y, depth = (uint)size.z },
            };

            VulkanNative.vkCmdCopyBufferToImage(vkCmdBuf.NativeCommandBuffer, vkSrcBuffer.NativeBuffer, vkDstTexture.NativeImage, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanTexture vkSrcTexture = src.Texture as VulkanTexture;
            VulkanBuffer vkDstBuffer = dst.Buffer as VulkanBuffer;

            VkBufferImageCopy region = new VkBufferImageCopy()
            {
                bufferOffset = dst.Offset,
                bufferRowLength = 0,
                bufferImageHeight = 0,
                imageSubresource = new VkImageSubresourceLayers()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkSrcTexture.Descriptor.Format),
                    mipLevel = src.MipLevel,
                    baseArrayLayer = src.SliceBase,
                    layerCount = src.SliceCount > 0 ? src.SliceCount : 1,
                },
                imageOffset = new VkOffset3D() { x = (int)src.Origin.x, y = (int)src.Origin.y, z = (int)src.Origin.z },
                imageExtent = new VkExtent3D() { width = (uint)size.x, height = (uint)size.y, depth = (uint)size.z },
            };

            VulkanNative.vkCmdCopyImageToBuffer(vkCmdBuf.NativeCommandBuffer, vkSrcTexture.NativeImage, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, vkDstBuffer.NativeBuffer, 1, &region);
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanTexture vkSrc = src.Texture as VulkanTexture;
            VulkanTexture vkDst = dst.Texture as VulkanTexture;

            VkImageCopy region = new VkImageCopy()
            {
                srcSubresource = new VkImageSubresourceLayers()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkSrc.Descriptor.Format),
                    mipLevel = src.MipLevel,
                    baseArrayLayer = src.SliceBase,
                    layerCount = src.SliceCount > 0 ? src.SliceCount : 1,
                },
                srcOffset = new VkOffset3D() { x = (int)src.Origin.x, y = (int)src.Origin.y, z = (int)src.Origin.z },
                dstSubresource = new VkImageSubresourceLayers()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkDst.Descriptor.Format),
                    mipLevel = dst.MipLevel,
                    baseArrayLayer = dst.SliceBase,
                    layerCount = dst.SliceCount > 0 ? dst.SliceCount : 1,
                },
                dstOffset = new VkOffset3D() { x = (int)dst.Origin.x, y = (int)dst.Origin.y, z = (int)dst.Origin.z },
                extent = new VkExtent3D() { width = (uint)size.x, height = (uint)size.y, depth = (uint)size.z },
            };

            VulkanNative.vkCmdCopyImage(vkCmdBuf.NativeCommandBuffer, vkSrc.NativeImage, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, vkDst.NativeImage, VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &region);
        }

        public override void EndPass()
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
        }

        protected override void Release()
        {
        }
    }

    // ========== Compute Encoder ==========
    internal unsafe class VulkanComputeEncoder : RHIComputeEncoder
    {
        private RHIComputePassDescriptor m_PassDescriptor;

        public VulkanComputeEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIComputePassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;

            if (barrier.ResourceBarrierType == ERHIResourceBarrierType.Triansition)
            {
                if (barrier.ResourceType == ERHIResourceType.Buffer)
                {
                    VulkanBuffer vkBuffer = barrier.BufferBarrierInfo.Handle as VulkanBuffer;
                    VkBufferMemoryBarrier bufferBarrier = new VkBufferMemoryBarrier()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
                        srcAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.SrcState),
                        dstAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.DstState),
                        buffer = vkBuffer.NativeBuffer,
                        offset = 0,
                        size = unchecked((ulong)(-1)),
                    };
                    VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                        0, 0, null, 1, &bufferBarrier, 0, null);
                }
                else
                {
                    VulkanTexture vkTexture = barrier.TextureBarrierInfo.Handle as VulkanTexture;
                    VkImageMemoryBarrier imageBarrier = new VkImageMemoryBarrier()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                        srcAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.SrcState),
                        dstAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.DstState),
                        oldLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.SrcState),
                        newLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.DstState),
                        srcQueueFamilyIndex = unchecked((uint)(-1)),
                        dstQueueFamilyIndex = unchecked((uint)(-1)),
                        image = vkTexture.NativeImage,
                        subresourceRange = new VkImageSubresourceRange()
                        {
                            aspectMask = VulkanUtility.GetVkImageAspect(vkTexture.Descriptor.Format),
                            baseMipLevel = 0,
                            levelCount = unchecked((uint)(-1)),
                            baseArrayLayer = 0,
                            layerCount = unchecked((uint)(-1)),
                        },
                    };
                    VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                        0, 0, null, 0, null, 1, &imageBarrier);
                }
            }
            else
            {
                VkMemoryBarrier memBarrier = new VkMemoryBarrier()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_BARRIER,
                    srcAccessMask = VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT,
                    dstAccessMask = VkAccessFlags.VK_ACCESS_SHADER_READ_BIT | VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT,
                };
                VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                    VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                    VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                    0, 1, &memBarrier, 0, null, 0, null);
            }
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            for (int i = 0; i < barriers.Length; ++i)
                ResourceBarrier(barriers.Span[i]);
        }

        public override void PushDebugGroup(string name) { }
        public override void PopDebugGroup() { }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery;
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 0);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery;
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkBuffer = buffer as VulkanBuffer;

            VkBufferMemoryBarrier bufferBarrier = new VkBufferMemoryBarrier()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
                srcAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(srcState),
                dstAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(dstState),
                buffer = vkBuffer.NativeBuffer,
                offset = 0,
                size = unchecked((ulong)(-1)),
            };

            VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                0, 0, null, 1, &bufferBarrier, 0, null);
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanTexture vkTexture = texture as VulkanTexture;

            VkImageMemoryBarrier imageBarrier = new VkImageMemoryBarrier()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                srcAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(srcState),
                dstAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(dstState),
                oldLayout = VulkanUtility.ConvertToVkImageLayout(srcState),
                newLayout = VulkanUtility.ConvertToVkImageLayout(dstState),
                srcQueueFamilyIndex = unchecked((uint)(-1)),
                dstQueueFamilyIndex = unchecked((uint)(-1)),
                image = vkTexture.NativeImage,
                subresourceRange = new VkImageSubresourceRange()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkTexture.Descriptor.Format),
                    baseMipLevel = 0,
                    levelCount = unchecked((uint)(-1)),
                    baseArrayLayer = 0,
                    layerCount = unchecked((uint)(-1)),
                },
            };

            VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT,
                0, 0, null, 0, null, 1, &imageBarrier);
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanComputePipeline vkPipeline = pipeline as VulkanComputePipeline;
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_COMPUTE, vkPipeline.NativePipeline);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanResourceTable vkResourceTable = resourceTable as VulkanResourceTable;
            VulkanComputePipeline vkPipeline = m_CachedPipeline as VulkanComputePipeline;
            VkDescriptorSet set = vkResourceTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_COMPUTE, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDispatch(vkCmdBuf.NativeCommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgsBuffer = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDispatchIndirect(vkCmdBuf.NativeCommandBuffer, vkArgsBuffer.NativeBuffer, argsOffset);
        }

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            // Vulkan indirect command buffers are handled via VkIndirectCommandsLayoutNV (not yet supported)
        }

        public override void EndPass()
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
        }

        protected override void Release() { }
    }

    // ========== Raster Encoder ==========
    internal unsafe class VulkanRasterEncoder : RHIRasterEncoder
    {
        private RHIRasterPassDescriptor m_PassDescriptor;

        public VulkanRasterEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }

            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;

            // Use dynamic rendering (Vulkan 1.3)
            int colorAttachmentCount = descriptor.ColorAttachments.Length;
            VkRenderingAttachmentInfo* colorAttachments = stackalloc VkRenderingAttachmentInfo[Math.Max(colorAttachmentCount, 1)];

            uint renderWidth = 0;
            uint renderHeight = 0;

            for (int i = 0; i < colorAttachmentCount; ++i)
            {
                ref RHIColorAttachmentDescriptor colorDesc = ref descriptor.ColorAttachments.Span[i];
                VulkanTexture vkTexture = colorDesc.RenderTarget as VulkanTexture;

                // Create inline image view
                VkImageViewCreateInfo viewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                    image = vkTexture.NativeImage,
                    viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D,
                    format = VulkanUtility.ConvertToVkFormat(vkTexture.Descriptor.Format),
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT,
                        baseMipLevel = colorDesc.MipLevel,
                        levelCount = 1,
                        baseArrayLayer = colorDesc.ArraySlice,
                        layerCount = 1,
                    },
                };

                VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
                VkImageView imageView;
                VulkanNative.vkCreateImageView(vkQueue.VulkanDevice.NativeDevice, &viewInfo, null, &imageView);

                VkClearValue clearValue = default;
                clearValue.color.float32_0 = colorDesc.ClearValue.x;
                clearValue.color.float32_1 = colorDesc.ClearValue.y;
                clearValue.color.float32_2 = colorDesc.ClearValue.z;
                clearValue.color.float32_3 = colorDesc.ClearValue.w;

                colorAttachments[i] = new VkRenderingAttachmentInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO,
                    imageView = imageView,
                    imageLayout = VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL,
                    loadOp = VulkanUtility.ConvertToVkLoadOp(colorDesc.LoadAction),
                    storeOp = VulkanUtility.ConvertToVkStoreOp(colorDesc.StoreAction),
                    clearValue = clearValue,
                };

                if (renderWidth == 0)
                {
                    renderWidth = vkTexture.Descriptor.Extent.x;
                    renderHeight = vkTexture.Descriptor.Extent.y;
                }
            }

            // Depth stencil
            VkRenderingAttachmentInfo depthAttachment = default;
            VkRenderingAttachmentInfo* pDepthAttachment = null;
            VkImageView depthImageView = default;

            if (descriptor.DepthStencilAttachment.HasValue)
            {
                ref RHIDepthStencilAttachmentDescriptor depthDesc = ref System.Runtime.CompilerServices.Unsafe.AsRef(in descriptor.DepthStencilAttachment.Value);
                VulkanTexture vkDepthTexture = depthDesc.RenderTarget as VulkanTexture;

                VkImageViewCreateInfo depthViewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO,
                    image = vkDepthTexture.NativeImage,
                    viewType = VkImageViewType.VK_IMAGE_VIEW_TYPE_2D,
                    format = VulkanUtility.ConvertToVkFormat(vkDepthTexture.Descriptor.Format),
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VulkanUtility.GetVkImageAspect(vkDepthTexture.Descriptor.Format),
                        baseMipLevel = depthDesc.MipLevel,
                        levelCount = 1,
                        baseArrayLayer = depthDesc.ArraySlice,
                        layerCount = 1,
                    },
                };

                VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
                VulkanNative.vkCreateImageView(vkQueue.VulkanDevice.NativeDevice, &depthViewInfo, null, &depthImageView);

                VkClearValue depthClearValue = default;
                depthClearValue.depthStencil.depth = depthDesc.DepthClearValue;
                depthClearValue.depthStencil.stencil = (uint)depthDesc.StencilClearValue;

                depthAttachment = new VkRenderingAttachmentInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_ATTACHMENT_INFO,
                    imageView = depthImageView,
                    imageLayout = VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL,
                    loadOp = VulkanUtility.ConvertToVkLoadOp(depthDesc.DepthLoadOp),
                    storeOp = VulkanUtility.ConvertToVkStoreOp(depthDesc.DepthStoreOp),
                    clearValue = depthClearValue,
                };
                pDepthAttachment = &depthAttachment;

                if (renderWidth == 0)
                {
                    renderWidth = vkDepthTexture.Descriptor.Extent.x;
                    renderHeight = vkDepthTexture.Descriptor.Extent.y;
                }
            }

            VkRenderingInfo renderingInfo = new VkRenderingInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_RENDERING_INFO,
                renderArea = new VkRect2D()
                {
                    offset = new VkOffset2D() { x = 0, y = 0 },
                    extent = new VkExtent2D() { width = renderWidth, height = renderHeight },
                },
                layerCount = 1,
                colorAttachmentCount = (uint)colorAttachmentCount,
                pColorAttachments = colorAttachmentCount > 0 ? colorAttachments : null,
                pDepthAttachment = pDepthAttachment,
                pStencilAttachment = pDepthAttachment,
            };

            VulkanNative.vkCmdBeginRendering(vkCmdBuf.NativeCommandBuffer, &renderingInfo);
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;

            if (barrier.ResourceBarrierType == ERHIResourceBarrierType.Triansition)
            {
                if (barrier.ResourceType == ERHIResourceType.Texture)
                {
                    VulkanTexture vkTexture = barrier.TextureBarrierInfo.Handle as VulkanTexture;
                    VkImageMemoryBarrier imageBarrier = new VkImageMemoryBarrier()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                        srcAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.SrcState),
                        dstAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(barrier.TextureBarrierInfo.DstState),
                        oldLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.SrcState),
                        newLayout = VulkanUtility.ConvertToVkImageLayout(barrier.TextureBarrierInfo.DstState),
                        srcQueueFamilyIndex = unchecked((uint)(-1)),
                        dstQueueFamilyIndex = unchecked((uint)(-1)),
                        image = vkTexture.NativeImage,
                        subresourceRange = new VkImageSubresourceRange()
                        {
                            aspectMask = VulkanUtility.GetVkImageAspect(vkTexture.Descriptor.Format),
                            baseMipLevel = 0,
                            levelCount = unchecked((uint)(-1)),
                            baseArrayLayer = 0,
                            layerCount = unchecked((uint)(-1)),
                        },
                    };
                    VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT,
                        VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT,
                        0, 0, null, 0, null, 1, &imageBarrier);
                }
            }
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            for (int i = 0; i < barriers.Length; ++i)
                ResourceBarrier(barriers.Span[i]);
        }

        public override void PushDebugGroup(string name) { }
        public override void PopDebugGroup() { }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_GRAPHICS_BIT, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginOcclusion(in uint index)
        {
            if (m_PassDescriptor.Occlusion.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Occlusion.Value.Query as VulkanQuery;
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, VkQueryControlFlags.VK_QUERY_CONTROL_PRECISE_BIT);
            }
        }

        public override void EndOcclusion(in uint index)
        {
            if (m_PassDescriptor.Occlusion.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Occlusion.Value.Query as VulkanQuery;
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery;
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 0);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery;
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void NextSubPass()
        {
            // Dynamic rendering does not use subpasses
        }

        public override void SetScissor(in Rect rect)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VkRect2D scissor = new VkRect2D()
            {
                offset = new VkOffset2D() { x = rect.left, y = rect.top },
                extent = new VkExtent2D() { width = (uint)(rect.right - rect.left), height = (uint)(rect.bottom - rect.top) },
            };
            VulkanNative.vkCmdSetScissor(vkCmdBuf.NativeCommandBuffer, 0, 1, &scissor);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VkRect2D* scissors = stackalloc VkRect2D[rects.Length];
            for (int i = 0; i < rects.Length; ++i)
            {
                ref Rect rect = ref rects.Span[i];
                scissors[i] = new VkRect2D()
                {
                    offset = new VkOffset2D() { x = rect.left, y = rect.top },
                    extent = new VkExtent2D() { width = (uint)(rect.right - rect.left), height = (uint)(rect.bottom - rect.top) },
                };
            }
            VulkanNative.vkCmdSetScissor(vkCmdBuf.NativeCommandBuffer, 0, (uint)rects.Length, scissors);
        }

        public override void SetViewport(in Viewport viewport)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VkViewport vkViewport = new VkViewport()
            {
                x = viewport.TopLeftX,
                y = viewport.TopLeftY,
                width = viewport.Width,
                height = viewport.Height,
                minDepth = viewport.MinDepth,
                maxDepth = viewport.MaxDepth,
            };
            VulkanNative.vkCmdSetViewport(vkCmdBuf.NativeCommandBuffer, 0, 1, &vkViewport);
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VkViewport* vkViewports = stackalloc VkViewport[viewports.Length];
            for (int i = 0; i < viewports.Length; ++i)
            {
                ref Viewport vp = ref viewports.Span[i];
                vkViewports[i] = new VkViewport()
                {
                    x = vp.TopLeftX,
                    y = vp.TopLeftY,
                    width = vp.Width,
                    height = vp.Height,
                    minDepth = vp.MinDepth,
                    maxDepth = vp.MaxDepth,
                };
            }
            VulkanNative.vkCmdSetViewport(vkCmdBuf.NativeCommandBuffer, 0, (uint)viewports.Length, vkViewports);
        }

        public override void SetStencilRef(in uint value)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdSetStencilReference(vkCmdBuf.NativeCommandBuffer, VkStencilFaceFlags.VK_STENCIL_FACE_FRONT_AND_BACK, value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            float* blendConstants = stackalloc float[4] { value.x, value.y, value.z, value.w };
            VulkanNative.vkCmdSetBlendConstants(vkCmdBuf.NativeCommandBuffer, blendConstants);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanRasterPipeline vkPipeline = pipeline as VulkanRasterPipeline;
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, vkPipeline.NativePipeline);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanResourceTable vkResourceTable = resourceTable as VulkanResourceTable;
            VulkanRasterPipeline vkPipeline = m_CachedPipeline as VulkanRasterPipeline;
            VkDescriptorSet set = vkResourceTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_GRAPHICS, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkBuffer = buffer as VulkanBuffer;
            VulkanNative.vkCmdBindIndexBuffer(vkCmdBuf.NativeCommandBuffer, vkBuffer.NativeBuffer, offset, VkIndexType.VK_INDEX_TYPE_UINT32);
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot, in uint offset)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkBuffer = buffer as VulkanBuffer;
            VkBuffer nativeBuffer = vkBuffer.NativeBuffer;
            ulong bufferOffset = offset;
            VulkanNative.vkCmdBindVertexBuffers(vkCmdBuf.NativeCommandBuffer, slot, 1, &nativeBuffer, &bufferOffset);
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
            // VRS requires VK_KHR_fragment_shading_rate extension
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDraw(vkCmdBuf.NativeCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDrawIndexed(vkCmdBuf.NativeCommandBuffer, indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgs = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDrawIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgs = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDrawIndexedIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            // Requires VK_EXT_mesh_shader
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            // Requires VK_EXT_mesh_shader
        }

        public override void DispatchGraph()
        {
            // Work graphs not supported in Vulkan
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            // Requires VK_NV_device_generated_commands
        }

        public override void EndPass()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdEndRendering(vkCmdBuf.NativeCommandBuffer);

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
        }

        protected override void Release() { }
    }

    // ========== Raytracing Encoder ==========
    internal unsafe class VulkanRaytracingEncoder : RHIRaytracingEncoder
    {
        private RHIRayTracingPassDescriptor m_PassDescriptor;

        public VulkanRaytracingEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIRayTracingPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;

            if (barrier.ResourceBarrierType == ERHIResourceBarrierType.Triansition && barrier.ResourceType == ERHIResourceType.Buffer)
            {
                VulkanBuffer vkBuffer = barrier.BufferBarrierInfo.Handle as VulkanBuffer;
                VkBufferMemoryBarrier bufferBarrier = new VkBufferMemoryBarrier()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
                    srcAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.SrcState),
                    dstAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(barrier.BufferBarrierInfo.DstState),
                    buffer = vkBuffer.NativeBuffer,
                    offset = 0,
                    size = unchecked((ulong)(-1)),
                };
                VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                    VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                    VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                    0, 0, null, 1, &bufferBarrier, 0, null);
            }
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            for (int i = 0; i < barriers.Length; ++i)
                ResourceBarrier(barriers.Span[i]);
        }

        public override void PushDebugGroup(string name) { }
        public override void PopDebugGroup() { }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index) { }
        public override void EndStatistics(in uint index) { }

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkBuffer = buffer as VulkanBuffer;
            VkBufferMemoryBarrier bufferBarrier = new VkBufferMemoryBarrier()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_MEMORY_BARRIER,
                srcAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(srcState),
                dstAccessMask = VulkanUtility.ConvertToVkBufferAccessFlag(dstState),
                buffer = vkBuffer.NativeBuffer,
                offset = 0,
                size = unchecked((ulong)(-1)),
            };
            VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                0, 0, null, 1, &bufferBarrier, 0, null);
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanTexture vkTexture = texture as VulkanTexture;
            VkImageMemoryBarrier imageBarrier = new VkImageMemoryBarrier()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER,
                srcAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(srcState),
                dstAccessMask = VulkanUtility.ConvertToVkTextureAccessFlag(dstState),
                oldLayout = VulkanUtility.ConvertToVkImageLayout(srcState),
                newLayout = VulkanUtility.ConvertToVkImageLayout(dstState),
                srcQueueFamilyIndex = unchecked((uint)(-1)),
                dstQueueFamilyIndex = unchecked((uint)(-1)),
                image = vkTexture.NativeImage,
                subresourceRange = new VkImageSubresourceRange()
                {
                    aspectMask = VulkanUtility.GetVkImageAspect(vkTexture.Descriptor.Format),
                    baseMipLevel = 0,
                    levelCount = unchecked((uint)(-1)),
                    baseArrayLayer = 0,
                    layerCount = unchecked((uint)(-1)),
                },
            };
            VulkanNative.vkCmdPipelineBarrier(vkCmdBuf.NativeCommandBuffer,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR,
                0, 0, null, 0, null, 1, &imageBarrier);
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanRaytracingPipeline vkPipeline = pipeline as VulkanRaytracingPipeline;
            if (vkPipeline.NativePipeline.Handle != 0)
            {
                VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_RAY_TRACING_KHR, vkPipeline.NativePipeline);
            }
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanResourceTable vkResourceTable = resourceTable as VulkanResourceTable;
            VulkanRaytracingPipeline vkPipeline = m_CachedPipeline as VulkanRaytracingPipeline;
            VkDescriptorSet set = vkResourceTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.VK_PIPELINE_BIND_POINT_RAY_TRACING_KHR, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            // Stub: requires VK_KHR_acceleration_structure
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            // Stub: requires VK_KHR_acceleration_structure
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            // Stub: requires VK_KHR_ray_tracing_pipeline vkCmdTraceRaysKHR
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            // Stub: requires VK_KHR_ray_tracing_pipeline vkCmdTraceRaysIndirectKHR
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
        }

        public override void EndPass()
        {
        }

        protected override void Release() { }
    }

    // ========== ML Encoder ==========
    internal unsafe class VulkanMLEncoder : RHIMLEncoder
    {
        private RHIMLPassDescriptor m_PassDescriptor;

        public VulkanMLEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier) { }
        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers) { }
        public override void PushDebugGroup(string name) { }
        public override void PopDebugGroup() { }

        public override void WriteTimestamp(in uint index) { }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex) { }
        public override void SetInputTensor(RHITensor tensor, in uint index) { }
        public override void SetOutputTensor(RHITensor tensor, in uint index) { }

        public override void Dispatch(RHIHeap intermediatesHeap)
        {
            // ML inference is not natively supported in Vulkan
            // This would typically use compute shaders or a dedicated ML library
        }

        public override void EndPass() { }

        protected override void Release() { }
    }

#pragma warning restore CS0414, CS8600, CS8601, CS8602, CS8604, CS8618
}
