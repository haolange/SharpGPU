using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Infinity.Mathmatics;
using Vortice.Vulkan;
using Viewport = Infinity.Mathmatics.Viewport;

namespace Infinity.Graphics
{
#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8604, CS8618

    internal static unsafe class VulkanBarrierEmitter
    {
        private sealed class Sync1Bucket
        {
            internal VkPipelineStageFlags SrcStages;
            internal VkPipelineStageFlags DstStages;
            internal readonly List<VkMemoryBarrier> MemoryBarriers = new List<VkMemoryBarrier>(4);
            internal readonly List<VkBufferMemoryBarrier> BufferBarriers = new List<VkBufferMemoryBarrier>(8);
            internal readonly List<VkImageMemoryBarrier> ImageBarriers = new List<VkImageMemoryBarrier>(8);
        }

        internal readonly struct Sync1BucketPlan
        {
            internal readonly VkPipelineStageFlags SrcStages;
            internal readonly VkPipelineStageFlags DstStages;
            internal readonly int BarrierCount;

            internal Sync1BucketPlan(VkPipelineStageFlags srcStages, VkPipelineStageFlags dstStages, int barrierCount)
            {
                SrcStages = srcStages;
                DstStages = dstStages;
                BarrierCount = barrierCount;
            }
        }

        internal static void EmitBarrier(VulkanCommandBuffer commandBuffer, in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            EmitBarriers(commandBuffer, singleBarrier);
        }

        internal static void EmitBarriers(VulkanCommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0)
            {
                return;
            }

            VulkanCommandQueue queue = commandBuffer.CommandQueue as VulkanCommandQueue
                ?? throw new InvalidOperationException("Vulkan barrier emitter requires a Vulkan command queue.");
            if (queue.VulkanDevice.UseSynchronization2)
            {
                EmitBarriersSync2(commandBuffer, barriers, queue.VulkanDevice);
                return;
            }

            EmitBarriersSync1(commandBuffer, barriers, queue.PipelineType);
        }

        internal static Sync1BucketPlan[] PlanSync1BucketsForTesting(ERHIPipelineType queuePipeline, RHIBarrier[] barriers)
        {
            List<Sync1BucketPlan> buckets = new List<Sync1BucketPlan>(4);
            for (int i = 0; i < barriers.Length; ++i)
            {
                if (!TryGetSync1StagePair(barriers[i], queuePipeline, out VkPipelineStageFlags srcStages, out VkPipelineStageFlags dstStages))
                {
                    continue;
                }

                bool merged = false;
                for (int bucketIndex = 0; bucketIndex < buckets.Count; ++bucketIndex)
                {
                    Sync1BucketPlan bucket = buckets[bucketIndex];
                    if (bucket.SrcStages == srcStages && bucket.DstStages == dstStages)
                    {
                        buckets[bucketIndex] = new Sync1BucketPlan(bucket.SrcStages, bucket.DstStages, bucket.BarrierCount + 1);
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    buckets.Add(new Sync1BucketPlan(srcStages, dstStages, 1));
                }
            }

            return buckets.ToArray();
        }

        private static void EmitBarriersSync2(VulkanCommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers, VulkanDevice device)
        {
            ERHIPipelineType queuePipeline = commandBuffer.CommandQueue.PipelineType;
            List<VkMemoryBarrier2> memoryBarriers = new List<VkMemoryBarrier2>(4);
            List<VkBufferMemoryBarrier2> bufferBarriers = new List<VkBufferMemoryBarrier2>(8);
            List<VkImageMemoryBarrier2> imageBarriers = new List<VkImageMemoryBarrier2>(8);

            for (int i = 0; i < barriers.Length; ++i)
            {
                switch (barriers[i].Kind)
                {
                    case ERHIBarrierKind.Global:
                    {
                        RHIGlobalBarrier globalBarrier = barriers[i].GlobalBarrier;
                        memoryBarriers.Add(new VkMemoryBarrier2
                        {
                            sType = VkStructureType.MemoryBarrier2,
                            srcStageMask = VulkanUtility.ConvertToVkPipelineStage2(globalBarrier.SyncBefore, queuePipeline),
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags2(globalBarrier.AccessBefore),
                            dstStageMask = VulkanUtility.ConvertToVkPipelineStage2(globalBarrier.SyncAfter, queuePipeline),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags2(globalBarrier.AccessAfter)
                        });
                        break;
                    }

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barriers[i].BufferBarrier;
                        VulkanBuffer vkBuffer = bufferBarrier.Resource as VulkanBuffer;
                        if (vkBuffer == null)
                        {
                            throw new InvalidOperationException($"Vulkan buffer barrier resource is null at index {i}.");
                        }

                        bufferBarriers.Add(new VkBufferMemoryBarrier2
                        {
                            sType = VkStructureType.BufferMemoryBarrier2,
                            srcStageMask = VulkanUtility.ConvertToVkPipelineStage2(bufferBarrier.SyncBefore, queuePipeline),
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags2(bufferBarrier.AccessBefore),
                            dstStageMask = VulkanUtility.ConvertToVkPipelineStage2(bufferBarrier.SyncAfter, queuePipeline),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags2(bufferBarrier.AccessAfter),
                            srcQueueFamilyIndex = unchecked((uint)(-1)),
                            dstQueueFamilyIndex = unchecked((uint)(-1)),
                            buffer = vkBuffer.NativeBuffer,
                            offset = bufferBarrier.Range.Offset,
                            size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                        });
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barriers[i].TextureBarrier;
                        VulkanTexture vkTexture = textureBarrier.Resource as VulkanTexture;
                        if (vkTexture == null)
                        {
                            throw new InvalidOperationException($"Vulkan texture barrier resource is null at index {i}.");
                        }

                        ResolveTextureLayouts(vkTexture, textureBarrier.LayoutBefore, textureBarrier.LayoutAfter, out VkImageLayout oldLayout, out VkImageLayout newLayout);
                        imageBarriers.Add(new VkImageMemoryBarrier2
                        {
                            sType = VkStructureType.ImageMemoryBarrier2,
                            srcStageMask = VulkanUtility.ConvertToVkPipelineStage2(textureBarrier.SyncBefore, queuePipeline),
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags2(textureBarrier.AccessBefore),
                            dstStageMask = VulkanUtility.ConvertToVkPipelineStage2(textureBarrier.SyncAfter, queuePipeline),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags2(textureBarrier.AccessAfter),
                            oldLayout = oldLayout,
                            newLayout = newLayout,
                            srcQueueFamilyIndex = unchecked((uint)(-1)),
                            dstQueueFamilyIndex = unchecked((uint)(-1)),
                            image = vkTexture.NativeImage,
                            subresourceRange = ConvertToVkSubresourceRange(textureBarrier.SubresourceRange, vkTexture.Descriptor.Format)
                        });
                        vkTexture.CurrentLayout = newLayout;
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported Vulkan barrier kind {barriers[i].Kind}.");
                }
            }

            VkMemoryBarrier2[] memoryArray = memoryBarriers.Count == 0 ? Array.Empty<VkMemoryBarrier2>() : memoryBarriers.ToArray();
            VkBufferMemoryBarrier2[] bufferArray = bufferBarriers.Count == 0 ? Array.Empty<VkBufferMemoryBarrier2>() : bufferBarriers.ToArray();
            VkImageMemoryBarrier2[] imageArray = imageBarriers.Count == 0 ? Array.Empty<VkImageMemoryBarrier2>() : imageBarriers.ToArray();

            fixed (VkMemoryBarrier2* memoryPtr = memoryArray)
            fixed (VkBufferMemoryBarrier2* bufferPtr = bufferArray)
            fixed (VkImageMemoryBarrier2* imagePtr = imageArray)
            {
                VkDependencyInfo dependencyInfo = new VkDependencyInfo
                {
                    sType = VkStructureType.DependencyInfo,
                    memoryBarrierCount = (uint)memoryArray.Length,
                    pMemoryBarriers = memoryPtr,
                    bufferMemoryBarrierCount = (uint)bufferArray.Length,
                    pBufferMemoryBarriers = bufferPtr,
                    imageMemoryBarrierCount = (uint)imageArray.Length,
                    pImageMemoryBarriers = imagePtr
                };

                if (device.UseSynchronization2KhrCommand)
                {
                    VulkanNative.vkCmdPipelineBarrier2KHR(commandBuffer.NativeCommandBuffer, &dependencyInfo);
                }
                else
                {
                    VulkanNative.vkCmdPipelineBarrier2(commandBuffer.NativeCommandBuffer, &dependencyInfo);
                }
            }
        }

        private static void EmitBarriersSync1(VulkanCommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers, ERHIPipelineType queuePipeline)
        {
            List<Sync1Bucket> buckets = new List<Sync1Bucket>(4);

            for (int i = 0; i < barriers.Length; ++i)
            {
                if (!TryGetSync1StagePair(barriers[i], queuePipeline, out VkPipelineStageFlags srcStages, out VkPipelineStageFlags dstStages))
                {
                    continue;
                }

                Sync1Bucket bucket = GetOrAddSync1Bucket(buckets, srcStages, dstStages);
                switch (barriers[i].Kind)
                {
                    case ERHIBarrierKind.Global:
                    {
                        RHIGlobalBarrier globalBarrier = barriers[i].GlobalBarrier;
                        bucket.MemoryBarriers.Add(new VkMemoryBarrier
                        {
                            sType = VkStructureType.MemoryBarrier,
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags(globalBarrier.AccessBefore),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags(globalBarrier.AccessAfter)
                        });
                        break;
                    }

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barriers[i].BufferBarrier;
                        VulkanBuffer vkBuffer = bufferBarrier.Resource as VulkanBuffer;
                        if (vkBuffer == null)
                        {
                            throw new InvalidOperationException($"Vulkan buffer barrier resource is null at index {i}.");
                        }

                        bucket.BufferBarriers.Add(new VkBufferMemoryBarrier
                        {
                            sType = VkStructureType.BufferMemoryBarrier,
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags(bufferBarrier.AccessBefore),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags(bufferBarrier.AccessAfter),
                            srcQueueFamilyIndex = unchecked((uint)(-1)),
                            dstQueueFamilyIndex = unchecked((uint)(-1)),
                            buffer = vkBuffer.NativeBuffer,
                            offset = bufferBarrier.Range.Offset,
                            size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                        });
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barriers[i].TextureBarrier;
                        VulkanTexture vkTexture = textureBarrier.Resource as VulkanTexture;
                        if (vkTexture == null)
                        {
                            throw new InvalidOperationException($"Vulkan texture barrier resource is null at index {i}.");
                        }

                        ResolveTextureLayouts(vkTexture, textureBarrier.LayoutBefore, textureBarrier.LayoutAfter, out VkImageLayout oldLayout, out VkImageLayout newLayout);
                        bucket.ImageBarriers.Add(new VkImageMemoryBarrier
                        {
                            sType = VkStructureType.ImageMemoryBarrier,
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags(textureBarrier.AccessBefore),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags(textureBarrier.AccessAfter),
                            oldLayout = oldLayout,
                            newLayout = newLayout,
                            srcQueueFamilyIndex = unchecked((uint)(-1)),
                            dstQueueFamilyIndex = unchecked((uint)(-1)),
                            image = vkTexture.NativeImage,
                            subresourceRange = ConvertToVkSubresourceRange(textureBarrier.SubresourceRange, vkTexture.Descriptor.Format)
                        });
                        vkTexture.CurrentLayout = newLayout;
                        break;
                    }
                }
            }

            for (int i = 0; i < buckets.Count; ++i)
            {
                Sync1Bucket bucket = buckets[i];
                if (bucket.MemoryBarriers.Count == 0 && bucket.BufferBarriers.Count == 0 && bucket.ImageBarriers.Count == 0)
                {
                    continue;
                }

                VkMemoryBarrier[] memoryArray = bucket.MemoryBarriers.Count == 0 ? Array.Empty<VkMemoryBarrier>() : bucket.MemoryBarriers.ToArray();
                VkBufferMemoryBarrier[] bufferArray = bucket.BufferBarriers.Count == 0 ? Array.Empty<VkBufferMemoryBarrier>() : bucket.BufferBarriers.ToArray();
                VkImageMemoryBarrier[] imageArray = bucket.ImageBarriers.Count == 0 ? Array.Empty<VkImageMemoryBarrier>() : bucket.ImageBarriers.ToArray();

                fixed (VkMemoryBarrier* memoryPtr = memoryArray)
                fixed (VkBufferMemoryBarrier* bufferPtr = bufferArray)
                fixed (VkImageMemoryBarrier* imagePtr = imageArray)
                {
                    VulkanNative.vkCmdPipelineBarrier(
                        commandBuffer.NativeCommandBuffer,
                        bucket.SrcStages,
                        bucket.DstStages,
                        0,
                        (uint)memoryArray.Length,
                        memoryPtr,
                        (uint)bufferArray.Length,
                        bufferPtr,
                        (uint)imageArray.Length,
                        imagePtr);
                }
            }
        }

        private static Sync1Bucket GetOrAddSync1Bucket(List<Sync1Bucket> buckets, VkPipelineStageFlags srcStages, VkPipelineStageFlags dstStages)
        {
            for (int i = 0; i < buckets.Count; ++i)
            {
                if (buckets[i].SrcStages == srcStages && buckets[i].DstStages == dstStages)
                {
                    return buckets[i];
                }
            }

            Sync1Bucket bucket = new Sync1Bucket
            {
                SrcStages = srcStages,
                DstStages = dstStages
            };
            buckets.Add(bucket);
            return bucket;
        }

        private static bool TryGetSync1StagePair(in RHIBarrier barrier, ERHIPipelineType queuePipeline, out VkPipelineStageFlags srcStages, out VkPipelineStageFlags dstStages)
        {
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                    srcStages = VulkanUtility.ConvertToVkPipelineStage(barrier.GlobalBarrier.SyncBefore, queuePipeline);
                    dstStages = VulkanUtility.ConvertToVkPipelineStage(barrier.GlobalBarrier.SyncAfter, queuePipeline);
                    return true;
                case ERHIBarrierKind.Buffer:
                    srcStages = VulkanUtility.ConvertToVkPipelineStage(barrier.BufferBarrier.SyncBefore, queuePipeline);
                    dstStages = VulkanUtility.ConvertToVkPipelineStage(barrier.BufferBarrier.SyncAfter, queuePipeline);
                    return true;
                case ERHIBarrierKind.Texture:
                    srcStages = VulkanUtility.ConvertToVkPipelineStage(barrier.TextureBarrier.SyncBefore, queuePipeline);
                    dstStages = VulkanUtility.ConvertToVkPipelineStage(barrier.TextureBarrier.SyncAfter, queuePipeline);
                    return true;
                default:
                    srcStages = 0;
                    dstStages = 0;
                    return false;
            }
        }

        private static void ResolveTextureLayouts(VulkanTexture vkTexture, ERHITextureLayout layoutBefore, ERHITextureLayout layoutAfter, out VkImageLayout oldLayout, out VkImageLayout newLayout)
        {
            oldLayout = VulkanUtility.ConvertToVkImageLayout(layoutBefore);
            if (oldLayout == VkImageLayout.Undefined)
            {
                oldLayout = vkTexture.CurrentLayout;
            }

            newLayout = VulkanUtility.ConvertToVkImageLayout(layoutAfter);
            if (newLayout == VkImageLayout.Undefined)
            {
                newLayout = oldLayout;
            }
        }

        private static VkImageSubresourceRange ConvertToVkSubresourceRange(in RHITextureSubresourceRange range, ERHIPixelFormat format)
        {
            return new VkImageSubresourceRange
            {
                aspectMask = VulkanUtility.ConvertToVkImageAspect(range.AspectMask, format),
                baseMipLevel = range.BaseMipLevel,
                levelCount = range.MipLevelCount == RHITextureSubresourceRange.All ? unchecked((uint)(-1)) : range.MipLevelCount,
                baseArrayLayer = range.BaseArrayLayer,
                layerCount = range.ArrayLayerCount == RHITextureSubresourceRange.All ? unchecked((uint)(-1)) : range.ArrayLayerCount
            };
        }
    }

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

        public override void Barrier(in RHIBarrier barrier)
        {
            VulkanBarrierEmitter.EmitBarrier((VulkanCommandBuffer)m_CommandBuffer!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers((VulkanCommandBuffer)m_CommandBuffer!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.AllCommands, vkQuery.NativeQueryPool, index);
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

            VulkanNative.vkCmdCopyBufferToImage(vkCmdBuf.NativeCommandBuffer, vkSrcBuffer.NativeBuffer, vkDstTexture.NativeImage, VkImageLayout.TransferDstOptimal, 1, &region);
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

            VulkanNative.vkCmdCopyImageToBuffer(vkCmdBuf.NativeCommandBuffer, vkSrcTexture.NativeImage, VkImageLayout.TransferSrcOptimal, vkDstBuffer.NativeBuffer, 1, &region);
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

            VulkanNative.vkCmdCopyImage(vkCmdBuf.NativeCommandBuffer, vkSrc.NativeImage, VkImageLayout.TransferSrcOptimal, vkDst.NativeImage, VkImageLayout.TransferDstOptimal, 1, &region);
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

        public override void Barrier(in RHIBarrier barrier)
        {
            VulkanBarrierEmitter.EmitBarrier((VulkanCommandBuffer)m_CommandBuffer!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers((VulkanCommandBuffer)m_CommandBuffer!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.ComputeShader, vkQuery.NativeQueryPool, index);
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

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanComputePipeline vkPipeline = pipeline as VulkanComputePipeline;
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Compute, vkPipeline.NativePipeline);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanArgumentTable vkArgumentTable = resourceTable as VulkanArgumentTable;
            VulkanComputePipeline vkPipeline = m_CachedPipeline as VulkanComputePipeline;
            VkDescriptorSet set = vkArgumentTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Compute, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanComputePipeline vkPipeline = m_CachedPipeline as VulkanComputePipeline;
#if DEBUG
            Debug.Assert(offset + size <= vkPipeline.VulkanPipelineLayout.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({vkPipeline.VulkanPipelineLayout.PushConstantSize}).");
#endif
            VulkanNative.vkCmdPushConstants(vkCmdBuf.NativeCommandBuffer, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, VkShaderStageFlags.All, offset, size, data.ToPointer());
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
        private bool m_RenderingActive;
        private bool m_HasIssuedDraw;
        private VkImageView[]? m_ActiveColorAttachmentViews;
        private VkImageView m_ActiveDepthAttachmentView;
        private bool m_ActiveHasDepthAttachmentView;
        private VkRenderingAttachmentInfo* m_ActiveColorAttachments;
        private VkRenderingAttachmentInfo* m_ActiveDepthAttachment;

        public VulkanRasterEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            m_PassDescriptor = descriptor;
            m_HasIssuedDraw = false;
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
            BeginRenderingIfNeeded();
        }

        private void BeginRenderingIfNeeded()
        {
            if (m_RenderingActive)
            {
                return;
            }

            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            ClearActiveAttachmentViews();
            DestroyActiveAttachmentInfos();

            int colorAttachmentCount = m_PassDescriptor.ColorAttachments.Length;
            m_ActiveColorAttachmentViews = colorAttachmentCount > 0 ? new VkImageView[colorAttachmentCount] : null;
            m_ActiveColorAttachments = (VkRenderingAttachmentInfo*)NativeMemory.AllocZeroed((nuint)Math.Max(colorAttachmentCount, 1), (nuint)sizeof(VkRenderingAttachmentInfo));
            vkCmdBuf.RegisterTransientAllocation(m_ActiveColorAttachments);
            VkRenderingAttachmentInfo* colorAttachments = m_ActiveColorAttachments;

            uint renderWidth = 0;
            uint renderHeight = 0;

            for (int i = 0; i < colorAttachmentCount; ++i)
            {
                ref RHIColorAttachmentDescriptor colorDesc = ref m_PassDescriptor.ColorAttachments.Span[i];
                VulkanTexture vkTexture = colorDesc.RenderTarget as VulkanTexture;

                VkImageViewCreateInfo viewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.ImageViewCreateInfo,
                    image = vkTexture.NativeImage,
                    viewType = VkImageViewType.Image2D,
                    format = VulkanUtility.ConvertToVkFormat(vkTexture.Descriptor.Format),
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        baseMipLevel = colorDesc.MipLevel,
                        levelCount = 1,
                        baseArrayLayer = colorDesc.ArraySlice,
                        layerCount = 1,
                    },
                };

                VkImageView imageView;
                VulkanUtility.CheckErrors(VulkanNative.vkCreateImageView(vkQueue.VulkanDevice.NativeDevice, &viewInfo, null, &imageView));
                m_ActiveColorAttachmentViews![i] = imageView;
                vkCmdBuf.RegisterTransientImageView(imageView);

                VkClearValue clearValue = default;
                clearValue.color.float32[0] = colorDesc.ClearValue.x;
                clearValue.color.float32[1] = colorDesc.ClearValue.y;
                clearValue.color.float32[2] = colorDesc.ClearValue.z;
                clearValue.color.float32[3] = colorDesc.ClearValue.w;

                colorAttachments[i] = new VkRenderingAttachmentInfo()
                {
                    sType = VkStructureType.RenderingAttachmentInfo,
                    imageView = imageView,
                    imageLayout = VkImageLayout.ColorAttachmentOptimal,
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

            VkRenderingAttachmentInfo* pDepthAttachment = null;
            m_ActiveDepthAttachment = null;

            if (m_PassDescriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthDesc = m_PassDescriptor.DepthStencilAttachment.Value;
                VulkanTexture vkDepthTexture = depthDesc.RenderTarget as VulkanTexture;

                VkImageViewCreateInfo depthViewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.ImageViewCreateInfo,
                    image = vkDepthTexture.NativeImage,
                    viewType = VkImageViewType.Image2D,
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

                VkImageView depthImageView;
                VulkanUtility.CheckErrors(VulkanNative.vkCreateImageView(vkQueue.VulkanDevice.NativeDevice, &depthViewInfo, null, &depthImageView));
                m_ActiveDepthAttachmentView = depthImageView;
                m_ActiveHasDepthAttachmentView = true;
                vkCmdBuf.RegisterTransientImageView(depthImageView);

                VkClearValue depthClearValue = new VkClearValue(depthDesc.DepthClearValue, (uint)depthDesc.StencilClearValue);
                m_ActiveDepthAttachment = (VkRenderingAttachmentInfo*)NativeMemory.AllocZeroed(1, (nuint)sizeof(VkRenderingAttachmentInfo));
                vkCmdBuf.RegisterTransientAllocation(m_ActiveDepthAttachment);
                m_ActiveDepthAttachment[0] = new VkRenderingAttachmentInfo()
                {
                    sType = VkStructureType.RenderingAttachmentInfo,
                    imageView = m_ActiveDepthAttachmentView,
                    imageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                    loadOp = VulkanUtility.ConvertToVkLoadOp(depthDesc.DepthLoadOp),
                    storeOp = VulkanUtility.ConvertToVkStoreOp(depthDesc.DepthStoreOp),
                    clearValue = depthClearValue,
                };
                pDepthAttachment = m_ActiveDepthAttachment;

                if (renderWidth == 0)
                {
                    renderWidth = vkDepthTexture.Descriptor.Extent.x;
                    renderHeight = vkDepthTexture.Descriptor.Extent.y;
                }
            }

            VkRenderingInfo renderingInfo = new VkRenderingInfo()
            {
                sType = VkStructureType.RenderingInfo,
                renderArea = new VkRect2D()
                {
                    offset = new VkOffset2D() { x = 0, y = 0 },
                    extent = new VkExtent2D() { width = renderWidth, height = renderHeight },
                },
                layerCount = 1,
                colorAttachmentCount = (uint)colorAttachmentCount,
                pColorAttachments = colorAttachmentCount > 0 ? colorAttachments : null,
                pDepthAttachment = pDepthAttachment,
                pStencilAttachment = null,
            };

            try
            {
                VulkanNative.vkCmdBeginRendering(vkCmdBuf.NativeCommandBuffer, &renderingInfo);
                m_RenderingActive = true;
            }
            catch
            {
                ClearActiveAttachmentViews();
                DestroyActiveAttachmentInfos();
                throw;
            }
        }

        private void EndRenderingIfNeeded()
        {
            if (!m_RenderingActive)
            {
                return;
            }

            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdEndRendering(vkCmdBuf.NativeCommandBuffer);
            m_RenderingActive = false;
            ClearActiveAttachmentViews();
            DestroyActiveAttachmentInfos();
        }

        private void ClearActiveAttachmentViews()
        {
            m_ActiveColorAttachmentViews = null;
            m_ActiveDepthAttachmentView = default;
            m_ActiveHasDepthAttachmentView = false;
        }

        private void DestroyActiveAttachmentInfos()
        {
            m_ActiveColorAttachments = null;
            m_ActiveDepthAttachment = null;
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            EndRenderingIfNeeded();
            VulkanBarrierEmitter.EmitBarrier((VulkanCommandBuffer)m_CommandBuffer!, barrier);

            if (!m_HasIssuedDraw)
            {
                BeginRenderingIfNeeded();
            }
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            EndRenderingIfNeeded();
            VulkanBarrierEmitter.EmitBarriers((VulkanCommandBuffer)m_CommandBuffer!, barriers);

            if (!m_HasIssuedDraw)
            {
                BeginRenderingIfNeeded();
            }
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.AllGraphics, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginOcclusion(in uint index)
        {
            if (m_PassDescriptor.Occlusion.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Occlusion.Value.Query as VulkanQuery;
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, VkQueryControlFlags.Precise);
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
                offset = new VkOffset2D() { x = (int)rect.left, y = (int)rect.top },
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
                    offset = new VkOffset2D() { x = (int)rect.left, y = (int)rect.top },
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
            VulkanNative.vkCmdSetStencilReference(vkCmdBuf.NativeCommandBuffer, VkStencilFaceFlags.FrontAndBack, value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            float* blendConstants = stackalloc float[4];
            blendConstants[0] = value.x;
            blendConstants[1] = value.y;
            blendConstants[2] = value.z;
            blendConstants[3] = value.w;
            VulkanNative.vkCmdSetBlendConstants(vkCmdBuf.NativeCommandBuffer, blendConstants);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanRasterPipeline vkPipeline = pipeline as VulkanRasterPipeline;
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.NativePipeline);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanArgumentTable vkArgumentTable = resourceTable as VulkanArgumentTable;
            VulkanRasterPipeline vkPipeline = m_CachedPipeline as VulkanRasterPipeline;
            VkDescriptorSet set = vkArgumentTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanRasterPipeline vkPipeline = m_CachedPipeline as VulkanRasterPipeline;
#if DEBUG
            Debug.Assert(offset + size <= vkPipeline.VulkanPipelineLayout.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({vkPipeline.VulkanPipelineLayout.PushConstantSize}).");
#endif
            VulkanNative.vkCmdPushConstants(vkCmdBuf.NativeCommandBuffer, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, VkShaderStageFlags.All, offset, size, data.ToPointer());
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkBuffer = buffer as VulkanBuffer;
            VkIndexType indexType = buffer.Descriptor.Format switch
            {
                ERHIBufferFormat.UInt16 => VkIndexType.Uint16,
                _ => VkIndexType.Uint32,
            };
            VulkanNative.vkCmdBindIndexBuffer(vkCmdBuf.NativeCommandBuffer, vkBuffer.NativeBuffer, offset, indexType);
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
            // TODO: Enable only when bound pipeline declares VK_DYNAMIC_STATE_FRAGMENT_SHADING_RATE_KHR.
            // Calling vkCmdSetFragmentShadingRateKHR unconditionally causes validation failures for static pipelines.
            _ = shadingRate;
            _ = shadingRateCombiner;
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDraw(vkCmdBuf.NativeCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDrawIndexed(vkCmdBuf.NativeCommandBuffer, indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgs = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDrawIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgs = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDrawIndexedIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanNative.vkCmdDrawMeshTasksEXT(vkCmdBuf.NativeCommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBuffer vkArgs = argsBuffer as VulkanBuffer;
            VulkanNative.vkCmdDrawMeshTasksIndirectEXT(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, argsOffset, 1, 0);
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            // Requires VK_NV_device_generated_commands
        }

        public override void EndPass()
        {
            EndRenderingIfNeeded();

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
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            VulkanBarrierEmitter.EmitBarrier((VulkanCommandBuffer)m_CommandBuffer!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers((VulkanCommandBuffer)m_CommandBuffer!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.RayTracingShaderKHR, vkQuery.NativeQueryPool, index);
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

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanRaytracingPipeline vkPipeline = pipeline as VulkanRaytracingPipeline;
            if (vkPipeline.NativePipeline.Handle != 0)
            {
                VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.RayTracingKHR, vkPipeline.NativePipeline);
            }
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanArgumentTable vkArgumentTable = resourceTable as VulkanArgumentTable;
            VulkanRaytracingPipeline vkPipeline = m_CachedPipeline as VulkanRaytracingPipeline;
            VkDescriptorSet set = vkArgumentTable.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.RayTracingKHR, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, tableIndex, 1, &set, 0, null);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanTopLevelAccelStruct vkTLAS = topLevelAccelStruct as VulkanTopLevelAccelStruct;

            // Get instance buffer device address
            VkBufferDeviceAddressInfo instanceAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = vkTLAS.NativeInstanceBuffer,
            };
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            ulong instanceBufferAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &instanceAddrInfo) + topLevelAccelStruct.Descriptor.Offset;

            VkAccelerationStructureGeometryKHR geometry = new VkAccelerationStructureGeometryKHR()
            {
                sType = VkStructureType.AccelerationStructureGeometryKHR,
                geometryType = VkGeometryTypeKHR.Instances,
                flags = VkGeometryFlagsKHR.Opaque,
            };
            geometry.geometry.instances.sType = VkStructureType.AccelerationStructureGeometryInstancesDataKHR;
            geometry.geometry.instances.arrayOfPointers = false;
            geometry.geometry.instances.data.deviceAddress = instanceBufferAddress;

            // Get scratch buffer device address
            VkBufferDeviceAddressInfo scratchAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = vkTLAS.NativeScratchBuffer,
            };
            ulong scratchAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &scratchAddrInfo);

            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
                type = VkAccelerationStructureTypeKHR.TopLevel,
                flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace |
                        VkBuildAccelerationStructureFlagsKHR.AllowUpdate,
                mode = VkBuildAccelerationStructureModeKHR.Build,
                dstAccelerationStructure = vkTLAS.NativeAccelerationStructure,
                geometryCount = 1,
                pGeometries = &geometry,
                scratchData = new VkDeviceOrHostAddressKHR() { deviceAddress = scratchAddress },
            };

            uint instanceCount = (uint)topLevelAccelStruct.Descriptor.Instances.Length;
            VkAccelerationStructureBuildRangeInfoKHR rangeInfo = new VkAccelerationStructureBuildRangeInfoKHR()
            {
                primitiveCount = instanceCount,
                primitiveOffset = 0,
                firstVertex = 0,
                transformOffset = 0,
            };
            VkAccelerationStructureBuildRangeInfoKHR* pRangeInfo = &rangeInfo;

            VulkanNative.vkCmdBuildAccelerationStructuresKHR(vkCmdBuf.NativeCommandBuffer, 1, &buildInfo, &pRangeInfo);
            InsertAccelerationStructureBuildBarrier(vkCmdBuf);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanBottomLevelAccelStruct vkBLAS = bottomLevelAccelStruct as VulkanBottomLevelAccelStruct;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;

            RHIBottomLevelAccelStructDescriptor descriptor = bottomLevelAccelStruct.Descriptor;
            int geometryCount = descriptor.Geometries.Length;
            VkAccelerationStructureGeometryKHR* geometries = stackalloc VkAccelerationStructureGeometryKHR[Math.Max(geometryCount, 1)];
            VkAccelerationStructureBuildRangeInfoKHR* rangeInfos = stackalloc VkAccelerationStructureBuildRangeInfoKHR[Math.Max(geometryCount, 1)];

            for (int i = 0; i < geometryCount; ++i)
            {
                RHIAccelStructGeometry geom = descriptor.Geometries[i];

                if (geom.GeometryType == EAccelStructGeometryType.Triangle)
                {
                    RHIAccelStructTriangles triangleGeometry = (RHIAccelStructTriangles)geom;
                    VulkanBuffer vertexBuffer = triangleGeometry.VertexBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Triangle geometry requires a Vulkan vertex buffer.");
                    VkBufferDeviceAddressInfo vertexAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = vertexBuffer.NativeBuffer,
                    };
                    ulong vertexAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &vertexAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Triangles,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.triangles.sType = VkStructureType.AccelerationStructureGeometryTrianglesDataKHR;
                    geometries[i].geometry.triangles.vertexFormat = VulkanUtility.ConvertToVkAccelerationStructureVertexFormat(triangleGeometry.VertexFormat);
                    geometries[i].geometry.triangles.vertexData.deviceAddress = vertexAddress + triangleGeometry.VertexOffset;
                    geometries[i].geometry.triangles.vertexStride = triangleGeometry.VertexStride;
                    geometries[i].geometry.triangles.maxVertex = triangleGeometry.VertexCount > 0
                        ? triangleGeometry.VertexCount - 1
                        : 0;

                    if (triangleGeometry.IndexBuffer != null)
                    {
                        VulkanBuffer indexBuffer = triangleGeometry.IndexBuffer as VulkanBuffer
                            ?? throw new InvalidOperationException("Triangle index buffer is not a Vulkan buffer.");
                        VkBufferDeviceAddressInfo indexAddrInfo = new VkBufferDeviceAddressInfo()
                        {
                            sType = VkStructureType.BufferDeviceAddressInfo,
                            buffer = indexBuffer.NativeBuffer,
                        };
                        ulong indexAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &indexAddrInfo);
                        geometries[i].geometry.triangles.indexType = VulkanUtility.ConvertToVkIndexType(triangleGeometry.IndexFormat);
                        geometries[i].geometry.triangles.indexData.deviceAddress = indexAddress + triangleGeometry.IndexOffset;
                        rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR()
                        {
                            primitiveCount = triangleGeometry.IndexCount / 3,
                        };
                    }
                    else
                    {
                        geometries[i].geometry.triangles.indexType = VkIndexType.NoneKHR;
                        rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR()
                        {
                            primitiveCount = triangleGeometry.VertexCount / 3,
                        };
                    }
                }
                else if (geom.GeometryType == EAccelStructGeometryType.AABB)
                {
                    RHIAccelStructAABBs aabbGeometry = (RHIAccelStructAABBs)geom;
                    VulkanBuffer aabbBuffer = aabbGeometry.AABBBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("AABB geometry requires a Vulkan buffer.");
                    VkBufferDeviceAddressInfo aabbAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = aabbBuffer.NativeBuffer,
                    };
                    ulong aabbAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &aabbAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Aabbs,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.AccelerationStructureGeometryAabbsDataKHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = aabbAddress + aabbGeometry.Offset;
                    geometries[i].geometry.aabbs.stride = aabbGeometry.Stride;
                    rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR()
                    {
                        primitiveCount = aabbGeometry.Count,
                    };
                }
                else if (geom.GeometryType == EAccelStructGeometryType.Curves)
                {
                    RHIAccelStructCurves curveGeometry = geom as RHIAccelStructCurves
                        ?? throw new InvalidOperationException("Curve geometry descriptor type mismatch.");
                    VkBuffer curveAabbBuffer = vkBLAS.GetCurveAabbBuffer(i);

                    VkBufferDeviceAddressInfo curveAabbAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = curveAabbBuffer,
                    };
                    ulong curveAabbAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &curveAabbAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Aabbs,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.AccelerationStructureGeometryAabbsDataKHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = curveAabbAddress;
                    geometries[i].geometry.aabbs.stride = (ulong)sizeof(float) * 6UL;
                    rangeInfos[i] = new VkAccelerationStructureBuildRangeInfoKHR()
                    {
                        primitiveCount = curveGeometry.SegmentCount,
                    };
                }
                else
                {
                    throw new NotSupportedException($"Unsupported BLAS geometry type '{geom.GeometryType}'.");
                }
            }

            // Get scratch buffer device address
            VkBufferDeviceAddressInfo scratchAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = vkBLAS.NativeScratchBuffer,
            };
            ulong scratchAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &scratchAddrInfo);

            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
                type = VkAccelerationStructureTypeKHR.BottomLevel,
                flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace,
                mode = VkBuildAccelerationStructureModeKHR.Build,
                dstAccelerationStructure = vkBLAS.NativeAccelerationStructure,
                geometryCount = (uint)geometryCount,
                pGeometries = geometries,
                scratchData = new VkDeviceOrHostAddressKHR() { deviceAddress = scratchAddress },
            };

            VkAccelerationStructureBuildRangeInfoKHR* pRangeInfos = rangeInfos;
            VulkanNative.vkCmdBuildAccelerationStructuresKHR(vkCmdBuf.NativeCommandBuffer, 1, &buildInfo, &pRangeInfos);
            InsertAccelerationStructureBuildBarrier(vkCmdBuf);
        }

        private static void InsertAccelerationStructureBuildBarrier(VulkanCommandBuffer vkCmdBuf)
        {
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue
                ?? throw new InvalidOperationException("Vulkan AS barrier requires a Vulkan command queue.");
            VulkanDevice device = vkQueue.VulkanDevice;

            // Ensure BLAS/TLAS writes are visible to subsequent AS builds and ray tracing shader reads in this command buffer.
            if (device.UseSynchronization2)
            {
                VkMemoryBarrier2 memoryBarrier = new VkMemoryBarrier2()
                {
                    sType = VkStructureType.MemoryBarrier2,
                    srcStageMask = VkPipelineStageFlags2.AccelerationStructureBuildKHR,
                    srcAccessMask = VkAccessFlags2.AccelerationStructureReadKHR | VkAccessFlags2.AccelerationStructureWriteKHR,
                    dstStageMask = VkPipelineStageFlags2.AccelerationStructureBuildKHR | VkPipelineStageFlags2.RayTracingShaderKHR,
                    dstAccessMask = VkAccessFlags2.AccelerationStructureReadKHR | VkAccessFlags2.AccelerationStructureWriteKHR | VkAccessFlags2.ShaderRead,
                };

                VkDependencyInfo dependencyInfo = new VkDependencyInfo()
                {
                    sType = VkStructureType.DependencyInfo,
                    memoryBarrierCount = 1,
                    pMemoryBarriers = &memoryBarrier,
                };

                if (device.UseSynchronization2KhrCommand)
                {
                    VulkanNative.vkCmdPipelineBarrier2KHR(vkCmdBuf.NativeCommandBuffer, &dependencyInfo);
                }
                else
                {
                    VulkanNative.vkCmdPipelineBarrier2(vkCmdBuf.NativeCommandBuffer, &dependencyInfo);
                }
                return;
            }

            VkMemoryBarrier memoryBarrierSync1 = new VkMemoryBarrier()
            {
                sType = VkStructureType.MemoryBarrier,
                srcAccessMask = VkAccessFlags.AccelerationStructureReadKHR | VkAccessFlags.AccelerationStructureWriteKHR,
                dstAccessMask = VkAccessFlags.AccelerationStructureReadKHR | VkAccessFlags.AccelerationStructureWriteKHR | VkAccessFlags.ShaderRead,
            };

            VulkanNative.vkCmdPipelineBarrier(
                vkCmdBuf.NativeCommandBuffer,
                VkPipelineStageFlags.AccelerationStructureBuildKHR,
                VkPipelineStageFlags.AccelerationStructureBuildKHR | VkPipelineStageFlags.RayTracingShaderKHR,
                0,
                1,
                &memoryBarrierSync1,
                0,
                null,
                0,
                null);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            Debug.Assert(m_CachedPipeline != null, "Raytracing pipeline must be set before Dispatch");
            Debug.Assert(functionTable != null, "FunctionTable must not be null for raytracing Dispatch");

            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanFunctionTable vkFuncTable = functionTable as VulkanFunctionTable;

            VkStridedDeviceAddressRegionKHR rayGenRegion = vkFuncTable.RayGenRegion;
            VkStridedDeviceAddressRegionKHR missRegion = vkFuncTable.MissRegion;
            VkStridedDeviceAddressRegionKHR hitGroupRegion = vkFuncTable.HitGroupRegion;
            VkStridedDeviceAddressRegionKHR callableRegion = vkFuncTable.CallableRegion;

            VulkanNative.vkCmdTraceRaysKHR(vkCmdBuf.NativeCommandBuffer,
                &rayGenRegion, &missRegion, &hitGroupRegion, &callableRegion,
                width, height, depth);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            Debug.Assert(m_CachedPipeline != null, "Raytracing pipeline must be set before DispatchIndirect");
            Debug.Assert(functionTable != null, "FunctionTable must not be null for raytracing DispatchIndirect");
            Debug.Assert(argsBuffer != null, "Args buffer must not be null for DispatchIndirect");

            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanFunctionTable vkFuncTable = functionTable as VulkanFunctionTable;
            VulkanBuffer vkArgsBuffer = argsBuffer as VulkanBuffer;

            VkStridedDeviceAddressRegionKHR rayGenRegion = vkFuncTable.RayGenRegion;
            VkStridedDeviceAddressRegionKHR missRegion = vkFuncTable.MissRegion;
            VkStridedDeviceAddressRegionKHR hitGroupRegion = vkFuncTable.HitGroupRegion;
            VkStridedDeviceAddressRegionKHR callableRegion = vkFuncTable.CallableRegion;

            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            VkBufferDeviceAddressInfo addrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = vkArgsBuffer.NativeBuffer,
            };
            ulong indirectAddress = VulkanNative.vkGetBufferDeviceAddress(vkQueue.VulkanDevice.NativeDevice, &addrInfo) + argsOffset;

            VulkanNative.vkCmdTraceRaysIndirectKHR(vkCmdBuf.NativeCommandBuffer,
                &rayGenRegion, &missRegion, &hitGroupRegion, &callableRegion,
                indirectAddress);
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
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

    // ========== ML Encoder ==========
    // Vulkan ML encoder uses a compute shader bridge approach since Vulkan
    // has no native ML inference API. Tensors are backed by VkBuffer and
    // the ML pipeline wraps a compute pipeline when available.
    internal unsafe class VulkanMLEncoder : RHIMLEncoder
    {
        private RHIMLPassDescriptor m_PassDescriptor;
        private VulkanTensor?[] m_InputTensors;
        private VulkanTensor?[] m_OutputTensors;

        public VulkanMLEncoder(VulkanCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_InputTensors = new VulkanTensor?[8];
            m_OutputTensors = new VulkanTensor?[8];
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
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

        public override void Barrier(in RHIBarrier barrier)
        {
            VulkanBarrierEmitter.EmitBarrier((VulkanCommandBuffer)m_CommandBuffer!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers((VulkanCommandBuffer)m_CommandBuffer!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
            VulkanCommandQueue vkQueue = vkCmdBuf.CommandQueue as VulkanCommandQueue;
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = m_CommandBuffer as VulkanCommandBuffer;
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery;
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.ComputeShader, vkQuery.NativeQueryPool, index);
            }
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            // Resource tables for the compute bridge pipeline are bound here if the ML pipeline
            // has been compiled to a compute pipeline with descriptor sets.
        }

        public override void SetInputTensor(RHITensor tensor, in uint index)
        {
            if (tensor is not VulkanTensor vkTensor)
            {
                throw new InvalidOperationException($"VulkanMLEncoder expects {nameof(VulkanTensor)} but got {tensor?.GetType().Name ?? "<null>"}.");
            }

            if (index < m_InputTensors.Length)
            {
                m_InputTensors[index] = vkTensor;
            }
        }

        public override void SetOutputTensor(RHITensor tensor, in uint index)
        {
            if (tensor is not VulkanTensor vkTensor)
            {
                throw new InvalidOperationException($"VulkanMLEncoder expects {nameof(VulkanTensor)} but got {tensor?.GetType().Name ?? "<null>"}.");
            }

            if (index < m_OutputTensors.Length)
            {
                m_OutputTensors[index] = vkTensor;
            }
        }

        public override void Dispatch(RHIHeap intermediatesHeap)
        {
            // Vulkan does not have native ML inference.
            // The compute bridge approach dispatches a pre-compiled compute shader
            // that implements the neural network layers. This is a no-op placeholder
            // because the actual compute shaders for ML layers must be provided by
            // the application's ML compiler toolchain.
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

#pragma warning restore CS0414, CS8600, CS8601, CS8602, CS8604, CS8618

    // ========== WorkGraph Encoder ==========
    internal sealed class VulkanWorkGraphEncoder : RHIWorkGraphEncoder
    {
        internal VulkanWorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }
        public override void PushDebugGroup(string name)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void PopDebugGroup()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void WriteTimestamp(in uint index)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void SetPipeline(RHIWorkGraphPipeline pipeline)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        public override void EndPass()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Vulkan backend.");
        }

        protected override void Release()
        {
        }
    }
}
