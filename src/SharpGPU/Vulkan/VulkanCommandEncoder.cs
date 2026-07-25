using System;
using Vortice.Vulkan;
using System.Diagnostics;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Viewport = SharpGPU.Mathematics.Viewport;

namespace SharpGPU
{
#pragma warning disable CS0414

    internal static class VulkanEncoderGuards
    {
        internal static VulkanCommandBuffer RequireCommandBuffer(RHICommandBuffer? commandBuffer)
        {
            return commandBuffer as VulkanCommandBuffer
                ?? throw new InvalidOperationException("Vulkan encoder operations require a VulkanCommandBuffer.");
        }

        internal static VulkanComputePipeline RequireComputePipeline(RHIComputePipeline? pipeline)
        {
            return pipeline as VulkanComputePipeline
                ?? throw new InvalidOperationException("Vulkan compute operations require a bound VulkanComputePipeline.");
        }

        internal static VulkanRasterPipeline RequireRasterPipeline(RHIRasterPipeline? pipeline)
        {
            return pipeline as VulkanRasterPipeline
                ?? throw new InvalidOperationException("Vulkan raster operations require a bound VulkanRasterPipeline.");
        }

        internal static VulkanBuffer RequireBuffer(RHIBuffer buffer)
        {
            return buffer as VulkanBuffer
                ?? throw new ArgumentException("Vulkan buffer operations require a VulkanBuffer.", nameof(buffer));
        }

        internal static VulkanTexture RequireTexture(RHITexture texture)
        {
            return texture as VulkanTexture
                ?? throw new ArgumentException("Vulkan texture operations require a VulkanTexture.", nameof(texture));
        }

        internal static VulkanQuery RequireQuery(RHIQuery query)
        {
            return query as VulkanQuery
                ?? throw new ArgumentException("Vulkan query operations require a VulkanQuery.", nameof(query));
        }

        internal static VulkanCommandQueue RequireCommandQueue(RHICommandQueue? commandQueue)
        {
            return commandQueue as VulkanCommandQueue
                ?? throw new InvalidOperationException("Vulkan encoder operations require a VulkanCommandQueue.");
        }

        internal static VulkanRaytracingPipeline RequireRaytracingPipeline(RHIRaytracingPipeline pipeline)
        {
            return pipeline as VulkanRaytracingPipeline
                ?? throw new ArgumentException("Vulkan ray-tracing operations require a VulkanRaytracingPipeline.", nameof(pipeline));
        }

        internal static VulkanTopLevelAccelStruct RequireTopLevelAccelStruct(RHITopLevelAccelStruct accelStruct)
        {
            return accelStruct as VulkanTopLevelAccelStruct
                ?? throw new ArgumentException("Vulkan TLAS build requires a VulkanTopLevelAccelStruct.", nameof(accelStruct));
        }

        internal static VulkanBottomLevelAccelStruct RequireBottomLevelAccelStruct(RHIBottomLevelAccelStruct accelStruct)
        {
            return accelStruct as VulkanBottomLevelAccelStruct
                ?? throw new ArgumentException("Vulkan BLAS build requires a VulkanBottomLevelAccelStruct.", nameof(accelStruct));
        }

        internal static VulkanFunctionTable RequireFunctionTable(RHIFunctionTable functionTable)
        {
            return functionTable as VulkanFunctionTable
                ?? throw new ArgumentException("Vulkan ray-tracing dispatch requires a VulkanFunctionTable.", nameof(functionTable));
        }

        internal static VulkanComputePipeline RequireCachedComputePipeline(RHIComputePipeline? cachedPipeline)
        {
            return cachedPipeline as VulkanComputePipeline
                ?? throw new InvalidOperationException("Vulkan compute encoder requires a bound VulkanComputePipeline.");
        }

        internal static VulkanRasterPipeline RequireCachedRasterPipeline(RHIRasterPipeline? cachedPipeline)
        {
            return cachedPipeline as VulkanRasterPipeline
                ?? throw new InvalidOperationException("Vulkan raster encoder requires a bound VulkanRasterPipeline.");
        }

        internal static VulkanRaytracingPipeline RequireCachedRaytracingPipeline(RHIRaytracingPipeline? cachedPipeline)
        {
            return cachedPipeline as VulkanRaytracingPipeline
                ?? throw new InvalidOperationException("Vulkan ray-tracing encoder requires a bound VulkanRaytracingPipeline.");
        }

        internal static VulkanDevice RequireDevice(RHICommandBuffer? commandBuffer)
        {
            return RequireCommandQueue(RequireCommandBuffer(commandBuffer).CommandQueue).VulkanDevice;
        }
    }

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

            VulkanCommandQueue queue = VulkanEncoderGuards.RequireCommandQueue(commandBuffer.CommandQueue)
                ?? throw new InvalidOperationException("Vulkan barrier emitter requires a Vulkan command queue.");
            for (int i = 0; i < barriers.Length; ++i)
            {
                RHIBarrierUtility.ValidateQueueOwnership(
                    in barriers[i],
                    queue.PipelineType);
                if (barriers[i].Kind == ERHIBarrierKind.Texture)
                {
                    RHITextureBarrier textureBarrier =
                        barriers[i].TextureBarrier;
                    ValidateTextureLayoutContract(
                        in textureBarrier);
                }
            }

            int layoutCheckpoint =
                commandBuffer.CaptureImageLayoutCheckpoint();
            try
            {
                if (queue.VulkanDevice.UseSynchronization2)
                {
                    EmitBarriersSync2(
                        commandBuffer,
                        barriers,
                        queue.VulkanDevice);
                    return;
                }

                EmitBarriersSync1(
                    commandBuffer,
                    barriers,
                    queue.VulkanDevice);
            }
            catch
            {
                commandBuffer.RollbackImageLayouts(
                    layoutCheckpoint);
                throw;
            }
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
                            srcStageMask = AddRequiredStages2(VulkanUtility.ConvertToVkPipelineStage2(globalBarrier.SyncBefore, queuePipeline), globalBarrier.AccessBefore),
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags2(globalBarrier.AccessBefore),
                            dstStageMask = AddRequiredStages2(VulkanUtility.ConvertToVkPipelineStage2(globalBarrier.SyncAfter, queuePipeline), globalBarrier.AccessAfter),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags2(globalBarrier.AccessAfter)
                        });
                        break;
                    }

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barriers[i].BufferBarrier;
                        VulkanBuffer vkBuffer = GetBuffer(device, bufferBarrier.Resource, i);

                        ResolveQueueFamilyIndices(device, in barriers[i], out uint sourceFamily, out uint destinationFamily);
                        bufferBarriers.Add(new VkBufferMemoryBarrier2
                        {
                            sType = VkStructureType.BufferMemoryBarrier2,
                            srcStageMask = AddRequiredStages2(VulkanUtility.ConvertToVkPipelineStage2(bufferBarrier.SyncBefore, queuePipeline), bufferBarrier.AccessBefore),
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags2(bufferBarrier.AccessBefore),
                            dstStageMask = AddRequiredStages2(VulkanUtility.ConvertToVkPipelineStage2(bufferBarrier.SyncAfter, queuePipeline), bufferBarrier.AccessAfter),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags2(bufferBarrier.AccessAfter),
                            srcQueueFamilyIndex = sourceFamily,
                            dstQueueFamilyIndex = destinationFamily,
                            buffer = vkBuffer.NativeBuffer,
                            offset = bufferBarrier.Range.Offset,
                            size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                        });
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barriers[i].TextureBarrier;
                        VulkanTexture vkTexture = GetTexture(device, textureBarrier.Resource, i);

                        VkImageLayout newLayout =
                            VulkanUtility.ConvertToVkImageLayout(
                                textureBarrier.LayoutAfter);
                        if (IsPureLayoutAssertion(
                                in textureBarrier))
                        {
                            commandBuffer.ValidateDeclaredImageLayout(
                                vkTexture,
                                in textureBarrier.SubresourceRange,
                                newLayout);
                            commandBuffer.SetKnownImageLayout(
                                vkTexture,
                                in textureBarrier.SubresourceRange,
                                newLayout);
                            break;
                        }

                        ResolveQueueFamilyIndices(
                            device,
                            in barriers[i],
                            out uint sourceFamily,
                            out uint destinationFamily);
                        AppendImageBarriersSync2(
                            imageBarriers,
                            commandBuffer,
                            vkTexture,
                            in textureBarrier,
                            queuePipeline,
                            sourceFamily,
                            destinationFamily,
                            newLayout);
                        commandBuffer.SetKnownImageLayout(
                            vkTexture,
                            in textureBarrier.SubresourceRange,
                            newLayout == VkImageLayout.Undefined
                                ? VulkanUtility.ConvertToVkImageLayout(
                                    textureBarrier.LayoutBefore)
                                : newLayout);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported Vulkan barrier kind {barriers[i].Kind}.");
                }
            }

            VkMemoryBarrier2[] memoryArray = memoryBarriers.Count == 0 ? Array.Empty<VkMemoryBarrier2>() : memoryBarriers.ToArray();
            VkBufferMemoryBarrier2[] bufferArray = bufferBarriers.Count == 0 ? Array.Empty<VkBufferMemoryBarrier2>() : bufferBarriers.ToArray();
            VkImageMemoryBarrier2[] imageArray = imageBarriers.Count == 0 ? Array.Empty<VkImageMemoryBarrier2>() : imageBarriers.ToArray();

            if (memoryArray.Length == 0 &&
                bufferArray.Length == 0 &&
                imageArray.Length == 0)
            {
                return;
            }
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

        private static void EmitBarriersSync1(VulkanCommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers, VulkanDevice device)
        {
            ERHIPipelineType queuePipeline = commandBuffer.CommandQueue.PipelineType;
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
                        VulkanBuffer vkBuffer = GetBuffer(device, bufferBarrier.Resource, i);

                        ResolveQueueFamilyIndices(device, in barriers[i], out uint sourceFamily, out uint destinationFamily);
                        bucket.BufferBarriers.Add(new VkBufferMemoryBarrier
                        {
                            sType = VkStructureType.BufferMemoryBarrier,
                            srcAccessMask = VulkanUtility.ConvertToVkAccessFlags(bufferBarrier.AccessBefore),
                            dstAccessMask = VulkanUtility.ConvertToVkAccessFlags(bufferBarrier.AccessAfter),
                            srcQueueFamilyIndex = sourceFamily,
                            dstQueueFamilyIndex = destinationFamily,
                            buffer = vkBuffer.NativeBuffer,
                            offset = bufferBarrier.Range.Offset,
                            size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                        });
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barriers[i].TextureBarrier;
                        VulkanTexture vkTexture = GetTexture(device, textureBarrier.Resource, i);

                        VkImageLayout newLayout =
                            VulkanUtility.ConvertToVkImageLayout(
                                textureBarrier.LayoutAfter);
                        if (IsPureLayoutAssertion(
                                in textureBarrier))
                        {
                            commandBuffer.ValidateDeclaredImageLayout(
                                vkTexture,
                                in textureBarrier.SubresourceRange,
                                newLayout);
                            commandBuffer.SetKnownImageLayout(
                                vkTexture,
                                in textureBarrier.SubresourceRange,
                                newLayout);
                            break;
                        }

                        ResolveQueueFamilyIndices(
                            device,
                            in barriers[i],
                            out uint sourceFamily,
                            out uint destinationFamily);
                        AppendImageBarriersSync1(
                            bucket.ImageBarriers,
                            commandBuffer,
                            vkTexture,
                            in textureBarrier,
                            sourceFamily,
                            destinationFamily,
                            newLayout);
                        commandBuffer.SetKnownImageLayout(
                            vkTexture,
                            in textureBarrier.SubresourceRange,
                            newLayout == VkImageLayout.Undefined
                                ? VulkanUtility.ConvertToVkImageLayout(
                                    textureBarrier.LayoutBefore)
                                : newLayout);
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

        private static void ValidateTextureLayoutContract(
            in RHITextureBarrier barrier)
        {
            if (barrier.LayoutAfter ==
                ERHITextureLayout.Undefined)
            {
                throw new ArgumentException(
                    "A Vulkan texture barrier LayoutAfter cannot " +
                    "be Undefined.",
                    nameof(barrier));
            }
            if (!Enum.IsDefined(barrier.LayoutBefore) ||
                !Enum.IsDefined(barrier.LayoutAfter))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(barrier),
                    "A Vulkan texture barrier contains an unknown " +
                    "layout value.");
            }
        }
        private static bool IsPureLayoutAssertion(
            in RHITextureBarrier barrier) =>
            barrier.LayoutBefore != ERHITextureLayout.Undefined &&
            barrier.LayoutBefore == barrier.LayoutAfter &&
            barrier.SyncBefore == ERHISyncStageMask.None &&
            barrier.SyncAfter == ERHISyncStageMask.None &&
            barrier.AccessBefore == ERHIAccessMask.None &&
            barrier.AccessAfter == ERHIAccessMask.None &&
            !barrier.SourceQueue.HasValue &&
            !barrier.DestinationQueue.HasValue;
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

        private static VulkanBuffer GetBuffer(
            VulkanDevice device,
            RHIBuffer resource,
            int index)
        {
            if (resource == null)
            {
                throw new ArgumentException($"Vulkan buffer barrier resource is null at index {index}.");
            }
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(resource.GetType().FullName);
            }

            if (resource is not VulkanBuffer buffer ||
                !ReferenceEquals(buffer.VulkanDevice, device))
            {
                throw new ArgumentException(
                    $"Vulkan buffer barrier resource at index {index} was created by a different backend or device.");
            }

            return buffer;
        }

        private static VulkanTexture GetTexture(
            VulkanDevice device,
            RHITexture resource,
            int index)
        {
            if (resource == null)
            {
                throw new ArgumentException($"Vulkan texture barrier resource is null at index {index}.");
            }
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(resource.GetType().FullName);
            }

            if (resource is not VulkanTexture texture ||
                !ReferenceEquals(texture.VulkanDevice, device))
            {
                throw new ArgumentException(
                    $"Vulkan texture barrier resource at index {index} was created by a different backend or device.");
            }

            return texture;
        }

        private static void ResolveQueueFamilyIndices(
            VulkanDevice device,
            in RHIBarrier barrier,
            out uint sourceFamily,
            out uint destinationFamily)
        {
            const uint QueueFamilyIgnored = unchecked((uint)(-1));
            sourceFamily = QueueFamilyIgnored;
            destinationFamily = QueueFamilyIgnored;
            if (!RHIBarrierUtility.TryGetQueueOwnership(
                    in barrier,
                    out ERHIPipelineType sourceQueue,
                    out ERHIPipelineType destinationQueue))
            {
                return;
            }

            int resolvedSource = device.GetQueueFamilyIndex(sourceQueue);
            int resolvedDestination = device.GetQueueFamilyIndex(destinationQueue);
            if (resolvedSource < 0 || resolvedDestination < 0)
            {
                throw new NotSupportedException(
                    $"Vulkan cannot lower the requested {sourceQueue}->{destinationQueue} queue ownership transfer because a queue family is unavailable.");
            }

            if (resolvedSource != resolvedDestination)
            {
                sourceFamily = checked((uint)resolvedSource);
                destinationFamily = checked((uint)resolvedDestination);
            }
        }

        private static bool TryGetSync1StagePair(
            in RHIBarrier barrier,
            ERHIPipelineType queuePipeline,
            out VkPipelineStageFlags srcStages,
            out VkPipelineStageFlags dstStages)
        {
            ERHISyncStageMask syncBefore;
            ERHISyncStageMask syncAfter;
            ERHIAccessMask accessBefore;
            ERHIAccessMask accessAfter;
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                    syncBefore = barrier.GlobalBarrier.SyncBefore;
                    syncAfter = barrier.GlobalBarrier.SyncAfter;
                    accessBefore = barrier.GlobalBarrier.AccessBefore;
                    accessAfter = barrier.GlobalBarrier.AccessAfter;
                    break;
                case ERHIBarrierKind.Buffer:
                    syncBefore = barrier.BufferBarrier.SyncBefore;
                    syncAfter = barrier.BufferBarrier.SyncAfter;
                    accessBefore = barrier.BufferBarrier.AccessBefore;
                    accessAfter = barrier.BufferBarrier.AccessAfter;
                    break;
                case ERHIBarrierKind.Texture:
                    syncBefore = barrier.TextureBarrier.SyncBefore;
                    syncAfter = barrier.TextureBarrier.SyncAfter;
                    accessBefore = barrier.TextureBarrier.AccessBefore;
                    accessAfter = barrier.TextureBarrier.AccessAfter;
                    break;
                default:
                    srcStages = 0;
                    dstStages = 0;
                    return false;
            }

            srcStages = AddRequiredStages(
                VulkanUtility.ConvertToVkPipelineStage(
                    syncBefore,
                    queuePipeline),
                accessBefore);
            dstStages = AddRequiredStages(
                VulkanUtility.ConvertToVkPipelineStage(
                    syncAfter,
                    queuePipeline),
                accessAfter);
            return true;
        }

        private static VkPipelineStageFlags AddRequiredStages(
            VkPipelineStageFlags stages,
            ERHIAccessMask access)
        {
            if ((access &
                 (ERHIAccessMask.RenderTargetRead |
                  ERHIAccessMask.RenderTargetWrite)) != 0)
            {
                stages |= VkPipelineStageFlags.ColorAttachmentOutput;
            }
            if ((access &
                 (ERHIAccessMask.DepthStencilRead |
                  ERHIAccessMask.DepthStencilWrite)) != 0)
            {
                stages |=
                    VkPipelineStageFlags.EarlyFragmentTests |
                    VkPipelineStageFlags.LateFragmentTests;
            }
            if ((access &
                 (ERHIAccessMask.TransferRead |
                  ERHIAccessMask.TransferWrite |
                  ERHIAccessMask.ResolveRead |
                  ERHIAccessMask.ResolveWrite)) != 0)
            {
                stages |= VkPipelineStageFlags.Transfer;
            }
            return stages;
        }

        private static VkPipelineStageFlags2 AddRequiredStages2(
            VkPipelineStageFlags2 stages,
            ERHIAccessMask access)
        {
            if ((access &
                 (ERHIAccessMask.RenderTargetRead |
                  ERHIAccessMask.RenderTargetWrite)) != 0)
            {
                stages |=
                    VkPipelineStageFlags2.ColorAttachmentOutput;
            }
            if ((access &
                 (ERHIAccessMask.DepthStencilRead |
                  ERHIAccessMask.DepthStencilWrite)) != 0)
            {
                stages |=
                    VkPipelineStageFlags2.EarlyFragmentTests |
                    VkPipelineStageFlags2.LateFragmentTests;
            }
            if ((access &
                 (ERHIAccessMask.TransferRead |
                  ERHIAccessMask.TransferWrite |
                  ERHIAccessMask.ResolveRead |
                  ERHIAccessMask.ResolveWrite)) != 0)
            {
                stages |= VkPipelineStageFlags2.Transfer;
            }
            return stages;
        }

        private static void AppendImageBarriersSync2(
            List<VkImageMemoryBarrier2> destination,
            VulkanCommandBuffer commandBuffer,
            VulkanTexture texture,
            in RHITextureBarrier barrier,
            ERHIPipelineType queuePipeline,
            uint sourceFamily,
            uint destinationFamily,
            VkImageLayout newLayout)
        {
            VkImageLayout declaredOldLayout =
                VulkanUtility.ConvertToVkImageLayout(
                    barrier.LayoutBefore);
            commandBuffer.ValidateDeclaredImageLayout(
                texture,
                in barrier.SubresourceRange,
                declaredOldLayout);
            destination.Add(CreateImageBarrier2(
                texture,
                in barrier,
                in barrier.SubresourceRange,
                queuePipeline,
                sourceFamily,
                destinationFamily,
                declaredOldLayout,
                newLayout));
        }
        private static VkImageMemoryBarrier2 CreateImageBarrier2(
            VulkanTexture texture,
            in RHITextureBarrier barrier,
            in RHITextureSubresourceRange range,
            ERHIPipelineType queuePipeline,
            uint sourceFamily,
            uint destinationFamily,
            VkImageLayout oldLayout,
            VkImageLayout newLayout) =>
            new()
            {
                sType = VkStructureType.ImageMemoryBarrier2,
                srcStageMask =
                    AddRequiredStages2(
                        VulkanUtility.ConvertToVkPipelineStage2(
                            barrier.SyncBefore,
                            queuePipeline),
                        barrier.AccessBefore),
                srcAccessMask =
                    VulkanUtility.ConvertToVkAccessFlags2(
                        barrier.AccessBefore),
                dstStageMask =
                    AddRequiredStages2(
                        VulkanUtility.ConvertToVkPipelineStage2(
                            barrier.SyncAfter,
                            queuePipeline),
                        barrier.AccessAfter),
                dstAccessMask =
                    VulkanUtility.ConvertToVkAccessFlags2(
                        barrier.AccessAfter),
                oldLayout = oldLayout,
                newLayout = newLayout == VkImageLayout.Undefined
                    ? oldLayout
                    : newLayout,
                srcQueueFamilyIndex = sourceFamily,
                dstQueueFamilyIndex = destinationFamily,
                image = texture.NativeImage,
                subresourceRange =
                    ConvertToVkSubresourceRange(
                        in range,
                        texture.Descriptor.Format),
            };

        private static void AppendImageBarriersSync1(
            List<VkImageMemoryBarrier> destination,
            VulkanCommandBuffer commandBuffer,
            VulkanTexture texture,
            in RHITextureBarrier barrier,
            uint sourceFamily,
            uint destinationFamily,
            VkImageLayout newLayout)
        {
            VkImageLayout declaredOldLayout =
                VulkanUtility.ConvertToVkImageLayout(
                    barrier.LayoutBefore);
            commandBuffer.ValidateDeclaredImageLayout(
                texture,
                in barrier.SubresourceRange,
                declaredOldLayout);
            destination.Add(CreateImageBarrier(
                texture,
                in barrier,
                in barrier.SubresourceRange,
                sourceFamily,
                destinationFamily,
                declaredOldLayout,
                newLayout));
        }
        private static VkImageMemoryBarrier CreateImageBarrier(
            VulkanTexture texture,
            in RHITextureBarrier barrier,
            in RHITextureSubresourceRange range,
            uint sourceFamily,
            uint destinationFamily,
            VkImageLayout oldLayout,
            VkImageLayout newLayout) =>
            new()
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask =
                    VulkanUtility.ConvertToVkAccessFlags(
                        barrier.AccessBefore),
                dstAccessMask =
                    VulkanUtility.ConvertToVkAccessFlags(
                        barrier.AccessAfter),
                oldLayout = oldLayout,
                newLayout = newLayout == VkImageLayout.Undefined
                    ? oldLayout
                    : newLayout,
                srcQueueFamilyIndex = sourceFamily,
                dstQueueFamilyIndex = destinationFamily,
                image = texture.NativeImage,
                subresourceRange =
                    ConvertToVkSubresourceRange(
                        in range,
                        texture.Descriptor.Format),
            };

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
            VulkanBarrierEmitter.EmitBarrier(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.AllCommands, vkQuery.NativeQueryPool, index);
            }
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanQuery vkQuery = VulkanEncoderGuards.RequireQuery(query);
            VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, startIndex, queriesCount);
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkSrc = VulkanEncoderGuards.RequireBuffer(srcBuffer);
            VulkanBuffer vkDst = VulkanEncoderGuards.RequireBuffer(dstBuffer);

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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkSrcBuffer = VulkanEncoderGuards.RequireBuffer(src.Buffer);
            VulkanTexture vkDstTexture = VulkanEncoderGuards.RequireTexture(dst.Texture);

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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanTexture vkSrcTexture = VulkanEncoderGuards.RequireTexture(src.Texture);
            VulkanBuffer vkDstBuffer = VulkanEncoderGuards.RequireBuffer(dst.Buffer);

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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanTexture vkSrc = VulkanEncoderGuards.RequireTexture(src.Texture);
            VulkanTexture vkDst = VulkanEncoderGuards.RequireTexture(dst.Texture);

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

        internal override void EndPassCore()
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
            VulkanBarrierEmitter.EmitBarrier(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.ComputeShader, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 0);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanComputePipeline vkPipeline = VulkanEncoderGuards.RequireComputePipeline(pipeline);
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Compute, vkPipeline.NativePipeline);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanComputePipeline pipeline =
                VulkanEncoderGuards.RequireCachedComputePipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan compute pipeline must be set before "
                    + "binding an argument table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VulkanComputePipeline));
            }
            VulkanArgumentTable table =
                pipeline.VulkanPipelineLayout.ResolveReadyTable(
                    resourceTable,
                    tableIndex);
            VulkanCommandBuffer commandBuffer =
                VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VkDescriptorSet set = table.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(
                commandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Compute,
                pipeline.VulkanPipelineLayout.NativePipelineLayout,
                tableIndex,
                1,
                &set,
                0,
                null);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanComputePipeline vkPipeline = VulkanEncoderGuards.RequireComputePipeline(m_CachedPipeline);
            if (offset + size > vkPipeline.VulkanPipelineLayout.PushConstantSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    offset,
                    $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({vkPipeline.VulkanPipelineLayout.PushConstantSize}).");
            }

            VulkanNative.vkCmdPushConstants(vkCmdBuf.NativeCommandBuffer, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, VkShaderStageFlags.All, offset, size, data.ToPointer());
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDispatch(vkCmdBuf.NativeCommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgsBuffer = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDispatchIndirect(vkCmdBuf.NativeCommandBuffer, vkArgsBuffer.NativeBuffer, argsOffset);
        }

        internal override void EndPassCore()
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

        internal override void BeginPassCore(RasterPassPlan plan)
        {
            if (plan.SubPassCount > 1)
            {
                throw new NotSupportedException(
                    "The selected Vulkan dynamic-rendering strategy cannot lower multiple ordered subpasses.");
            }

            RHIRasterPassDescriptor descriptor = plan.DescriptorSnapshot;
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

            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
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
                VulkanTexture vkTexture = VulkanEncoderGuards.RequireTexture(colorDesc.RenderTarget);

                VkImageViewCreateInfo viewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.ImageViewCreateInfo,
                    image = vkTexture.NativeImage,
                    viewType = VkImageViewType.Image2D,
                    format = VulkanUtility.ConvertToVkFormat(vkTexture.Descriptor.Format),
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VkImageAspectFlags.Color,
                        baseMipLevel = colorDesc.SubresourceRange.BaseMipLevel,
                        levelCount = colorDesc.SubresourceRange.MipLevelCount,
                        baseArrayLayer = colorDesc.SubresourceRange.BaseArrayLayer,
                        layerCount = colorDesc.SubresourceRange.ArrayLayerCount,
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
                    renderWidth = Math.Max(1u, vkTexture.Descriptor.Extent.x >> checked((int)colorDesc.SubresourceRange.BaseMipLevel));
                    renderHeight = Math.Max(1u, vkTexture.Descriptor.Extent.y >> checked((int)colorDesc.SubresourceRange.BaseMipLevel));
                }
            }

            VkRenderingAttachmentInfo* pDepthAttachment = null;
            m_ActiveDepthAttachment = null;

            if (m_PassDescriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthDesc = m_PassDescriptor.DepthStencilAttachment.Value;
                VulkanTexture vkDepthTexture = VulkanEncoderGuards.RequireTexture(depthDesc.RenderTarget);

                VkImageViewCreateInfo depthViewInfo = new VkImageViewCreateInfo()
                {
                    sType = VkStructureType.ImageViewCreateInfo,
                    image = vkDepthTexture.NativeImage,
                    viewType = VkImageViewType.Image2D,
                    format = VulkanUtility.ConvertToVkFormat(vkDepthTexture.Descriptor.Format),
                    subresourceRange = new VkImageSubresourceRange()
                    {
                        aspectMask = VulkanUtility.GetVkImageAspect(vkDepthTexture.Descriptor.Format),
                        baseMipLevel = depthDesc.SubresourceRange.BaseMipLevel,
                        levelCount = depthDesc.SubresourceRange.MipLevelCount,
                        baseArrayLayer = depthDesc.SubresourceRange.BaseArrayLayer,
                        layerCount = depthDesc.SubresourceRange.ArrayLayerCount,
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
                    renderWidth = Math.Max(1u, vkDepthTexture.Descriptor.Extent.x >> checked((int)depthDesc.SubresourceRange.BaseMipLevel));
                    renderHeight = Math.Max(1u, vkDepthTexture.Descriptor.Extent.y >> checked((int)depthDesc.SubresourceRange.BaseMipLevel));
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
                layerCount = m_PassDescriptor.ArrayLength,
                colorAttachmentCount = (uint)colorAttachmentCount,
                pColorAttachments = colorAttachmentCount > 0 ? colorAttachments : null,
                pDepthAttachment = pDepthAttachment,
                pStencilAttachment = null,
            };

            try
            {
                VulkanNative.vkCmdBeginRendering(vkCmdBuf.NativeCommandBuffer, &renderingInfo, vkQueue.VulkanDevice.UseDynamicRenderingKhrCommands);
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

            VulkanCommandBuffer vkCmdBuf =
                VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!;
            VulkanCommandQueue vkQueue =
                (VulkanCommandQueue)vkCmdBuf.CommandQueue;
            VulkanNative.vkCmdEndRendering(
                vkCmdBuf.NativeCommandBuffer,
                vkQueue.VulkanDevice.UseDynamicRenderingKhrCommands);
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
            VulkanBarrierEmitter.EmitBarrier(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barrier);

            if (!m_HasIssuedDraw)
            {
                BeginRenderingIfNeeded();
            }
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            EndRenderingIfNeeded();
            VulkanBarrierEmitter.EmitBarriers(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barriers);

            if (!m_HasIssuedDraw)
            {
                BeginRenderingIfNeeded();
            }
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.AllGraphics, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginOcclusion(in uint index)
        {
            if (m_PassDescriptor.Occlusion.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Occlusion.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, VkQueryControlFlags.Precise);
            }
        }

        public override void EndOcclusion(in uint index)
        {
            if (m_PassDescriptor.Occlusion.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Occlusion.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 0);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        internal override void NextSubPassCore(
            RasterPassPlan plan,
            int sourceSubPassIndex,
            int destinationSubPassIndex)
        {
            throw new NotSupportedException(
                "The selected Vulkan dynamic-rendering strategy cannot express ordered subpass advancement.");
        }

        public override void SetScissor(in Rect rect)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VkRect2D scissor = new VkRect2D()
            {
                offset = new VkOffset2D() { x = (int)rect.left, y = (int)rect.top },
                extent = new VkExtent2D() { width = (uint)(rect.right - rect.left), height = (uint)(rect.bottom - rect.top) },
            };
            VulkanNative.vkCmdSetScissor(vkCmdBuf.NativeCommandBuffer, 0, 1, &scissor);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdSetStencilReference(vkCmdBuf.NativeCommandBuffer, VkStencilFaceFlags.FrontAndBack, value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            float* blendConstants = stackalloc float[4];
            blendConstants[0] = value.x;
            blendConstants[1] = value.y;
            blendConstants[2] = value.z;
            blendConstants[3] = value.w;
            VulkanNative.vkCmdSetBlendConstants(vkCmdBuf.NativeCommandBuffer, blendConstants);
        }

        internal override void SetPipelineCore(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanRasterPipeline vkPipeline = VulkanEncoderGuards.RequireRasterPipeline(pipeline);
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.NativePipeline);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanRasterPipeline pipeline =
                VulkanEncoderGuards.RequireCachedRasterPipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before "
                    + "binding an argument table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VulkanRasterPipeline));
            }
            VulkanArgumentTable table =
                pipeline.VulkanPipelineLayout.ResolveReadyTable(
                    resourceTable,
                    tableIndex);
            VulkanCommandBuffer commandBuffer =
                VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VkDescriptorSet set = table.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(
                commandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                pipeline.VulkanPipelineLayout.NativePipelineLayout,
                tableIndex,
                1,
                &set,
                0,
                null);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanRasterPipeline vkPipeline = VulkanEncoderGuards.RequireRasterPipeline(m_CachedPipeline);
            if (offset + size > vkPipeline.VulkanPipelineLayout.PushConstantSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    offset,
                    $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({vkPipeline.VulkanPipelineLayout.PushConstantSize}).");
            }

            VulkanNative.vkCmdPushConstants(vkCmdBuf.NativeCommandBuffer, vkPipeline.VulkanPipelineLayout.NativePipelineLayout, VkShaderStageFlags.All, offset, size, data.ToPointer());
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkBuffer = VulkanEncoderGuards.RequireBuffer(buffer);
            VkIndexType indexType = buffer.Descriptor.Format switch
            {
                ERHIBufferFormat.UInt16 => VkIndexType.Uint16,
                _ => VkIndexType.Uint32,
            };
            VulkanNative.vkCmdBindIndexBuffer(vkCmdBuf.NativeCommandBuffer, vkBuffer.NativeBuffer, offset, indexType);
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot, in uint offset)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkBuffer = VulkanEncoderGuards.RequireBuffer(buffer);
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

        internal override void DrawCore(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDraw(vkCmdBuf.NativeCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        internal override void DrawIndexedCore(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDrawIndexed(vkCmdBuf.NativeCommandBuffer, indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        internal override void DrawIndirectCore(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        internal override void DrawIndexedIndirectCore(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawIndexedIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        internal override void DispatchMeshCore(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDrawMeshTasksEXT(vkCmdBuf.NativeCommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        internal override void DispatchMeshIndirectCore(RHIBuffer argsBuffer, in uint argsOffset)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawMeshTasksIndirectEXT(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, argsOffset, 1, 0);
        }

        internal override void EndPassCore()
        {
            EndRenderingIfNeeded();

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            m_PassDescriptor = default;
            m_HasIssuedDraw = false;
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
            VulkanBarrierEmitter.EmitBarrier(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            VulkanBarrierEmitter.EmitBarriers(VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer)!, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdBeginDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer, name);
        }

        public override void PopDebugGroup()
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
            vkQueue.VulkanDevice.VulkanInstance.CmdEndDebugUtilsLabel(vkCmdBuf.NativeCommandBuffer);
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Timestamp.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdWriteTimestamp(vkCmdBuf.NativeCommandBuffer, VkPipelineStageFlags.RayTracingShaderKHR, vkQuery.NativeQueryPool, index);
            }
        }

        public override void BeginStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdResetQueryPool(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 1);
                VulkanNative.vkCmdBeginQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index, 0);
            }
        }

        public override void EndStatistics(in uint index)
        {
            if (m_PassDescriptor.Statistics.HasValue)
            {
                VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                VulkanQuery vkQuery = m_PassDescriptor.Statistics.Value.Query as VulkanQuery ?? throw new InvalidOperationException("Pass query must be a VulkanQuery.");
                VulkanNative.vkCmdEndQuery(vkCmdBuf.NativeCommandBuffer, vkQuery.NativeQueryPool, index);
            }
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanRaytracingPipeline vkPipeline = VulkanEncoderGuards.RequireRaytracingPipeline(pipeline);
            if (vkPipeline.NativePipeline.Handle != 0)
            {
                VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.RayTracingKHR, vkPipeline.NativePipeline);
            }
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            VulkanRaytracingPipeline pipeline =
                VulkanEncoderGuards.RequireCachedRaytracingPipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan ray-tracing pipeline must be set before "
                    + "binding an argument table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanRaytracingPipeline));
            }
            VulkanArgumentTable table =
                pipeline.VulkanPipelineLayout.ResolveReadyTable(
                    resourceTable,
                    tableIndex);
            VulkanCommandBuffer commandBuffer =
                VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VkDescriptorSet set = table.NativeDescriptorSet;
            VulkanNative.vkCmdBindDescriptorSets(
                commandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.RayTracingKHR,
                pipeline.VulkanPipelineLayout.NativePipelineLayout,
                tableIndex,
                1,
                &set,
                0,
                null);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanTopLevelAccelStruct vkTLAS = VulkanEncoderGuards.RequireTopLevelAccelStruct(topLevelAccelStruct);

            // Get instance buffer device address
            VkBufferDeviceAddressInfo instanceAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = vkTLAS.NativeInstanceBuffer,
            };
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
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
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBottomLevelAccelStruct vkBLAS = VulkanEncoderGuards.RequireBottomLevelAccelStruct(bottomLevelAccelStruct);
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);

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
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue)
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

            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanFunctionTable vkFuncTable = VulkanEncoderGuards.RequireFunctionTable(functionTable);

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

            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanFunctionTable vkFuncTable = VulkanEncoderGuards.RequireFunctionTable(functionTable);
            VulkanBuffer vkArgsBuffer = VulkanEncoderGuards.RequireBuffer(argsBuffer);

            VkStridedDeviceAddressRegionKHR rayGenRegion = vkFuncTable.RayGenRegion;
            VkStridedDeviceAddressRegionKHR missRegion = vkFuncTable.MissRegion;
            VkStridedDeviceAddressRegionKHR hitGroupRegion = vkFuncTable.HitGroupRegion;
            VkStridedDeviceAddressRegionKHR callableRegion = vkFuncTable.CallableRegion;

            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(vkCmdBuf.CommandQueue);
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

        internal override void EndPassCore()
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
#pragma warning restore CS0414

    // ========== WorkGraph Encoder ==========
}
