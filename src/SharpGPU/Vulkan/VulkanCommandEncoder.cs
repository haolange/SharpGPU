using System;
using System.Numerics;
using Vortice.Vulkan;
using System.Diagnostics;
using System.Threading;
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

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            commandBuffer.MarkEncoderEndFromEncoder();
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

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            VulkanComputePipeline pipeline =
                VulkanEncoderGuards.RequireCachedComputePipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan compute pipeline must be set before "
                    + "binding an binding table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VulkanComputePipeline));
            }
            VulkanBindingTable table =
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

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            VulkanEncoderGuards.RequireDevice(m_CommandBuffer).Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan compute ExecuteIndirectCommandBuffer");
            throw new NotSupportedException(
                "Vulkan compute ExecuteIndirectCommandBuffer is unavailable.");
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The compute encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Compute);

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release() { }
    }

    #region RasterPass
internal enum EVulkanRasterPassStrategy
    {
        DynamicRendering = 0,
        DynamicRenderingLocalRead = 1,
        NativeRenderPass2 = 2,
    }

    internal enum EVulkanRasterPassForcedStrategy
    {
        Auto = 0,
        DynamicRenderingLocalRead = 1,
        NativeRenderPass2 = 2,
    }

    internal enum EVulkanRasterAttachmentScopeLayout : byte
    {
        ColorAttachment = 0,
        RenderingLocalRead = 1,
        AttachmentFeedbackLoop = 2,
        GeneralStorage = 3,
    }

    [Flags]
    internal enum EVulkanRasterPhaseBarrierKind : byte
    {
        None = 0,
        FramebufferByRegion = 1 << 0,
        FeedbackLoopByRegion = 1 << 1,
        OrderedStorageByRegion = 1 << 2,
    }

    internal readonly struct VulkanRasterCapabilities
    {
        internal bool DynamicRendering { get; }
        internal bool DynamicRenderingLocalRead { get; }
        internal bool DynamicRenderingLocalReadDepthStencil { get; }
        internal bool DynamicRenderingLocalReadMultisampled { get; }
        internal bool RenderPass2 { get; }
        internal bool AttachmentFeedbackLoopLayout { get; }
        internal bool OrderedFragmentPixelInterlock { get; }
        internal bool FragmentStoresAndAtomics { get; }
        internal bool UnifiedImageLayouts { get; }

        internal VulkanRasterCapabilities(
            bool dynamicRendering,
            bool dynamicRenderingLocalRead,
            bool dynamicRenderingLocalReadDepthStencil,
            bool dynamicRenderingLocalReadMultisampled,
            bool renderPass2,
            bool attachmentFeedbackLoopLayout,
            bool orderedFragmentPixelInterlock,
            bool fragmentStoresAndAtomics,
            bool unifiedImageLayouts)
        {
            DynamicRendering = dynamicRendering;
            DynamicRenderingLocalRead = dynamicRenderingLocalRead;
            DynamicRenderingLocalReadDepthStencil =
                dynamicRenderingLocalReadDepthStencil;
            DynamicRenderingLocalReadMultisampled =
                dynamicRenderingLocalReadMultisampled;
            RenderPass2 = renderPass2;
            AttachmentFeedbackLoopLayout =
                attachmentFeedbackLoopLayout;
            OrderedFragmentPixelInterlock =
                orderedFragmentPixelInterlock;
            FragmentStoresAndAtomics = fragmentStoresAndAtomics;
            UnifiedImageLayouts = unifiedImageLayouts;
        }
    }

    internal readonly struct VulkanRasterPhaseBarrierPlan
    {
        internal EVulkanRasterPhaseBarrierKind Kind { get; }
        internal byte AttachmentMask { get; }
        internal bool IsByRegion =>
            Kind != EVulkanRasterPhaseBarrierKind.None;
        internal bool PerformsLayoutTransition => false;
        internal bool PerformsQueueFamilyTransfer => false;

        internal VulkanRasterPhaseBarrierPlan(
            EVulkanRasterPhaseBarrierKind kind,
            byte attachmentMask)
        {
            Kind = kind;
            AttachmentMask = attachmentMask;
        }
    }

    internal readonly struct VulkanRasterSubPassLowering
    {
        internal byte LocalInputMask { get; }
        internal byte SampledInputMask { get; }
        internal byte OutputMask { get; }
        internal byte DeclaredOutputMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PreserveMask { get; }
        internal byte TransitionMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool HasNonIdentityOutputMapping { get; }
        internal bool RequiresFeedbackLoopLayout =>
            SampledInputMask != 0;
        internal int ColorInputSlotCount =>
            m_ColorInputLogicalAttachments.Length;
        internal int ColorOutputLocationCount =>
            m_ColorOutputLogicalAttachments.Length;
        internal int SampledFeedbackSlotCount =>
            m_SampledFeedbackLogicalAttachments.Length;

        private readonly int[] m_ColorInputLogicalAttachments;
        private readonly int[] m_ColorOutputLogicalAttachments;
        private readonly int[] m_SampledFeedbackLogicalAttachments;
        private readonly int[] m_OutputLocationsByPhysicalAttachment;
        private readonly int[] m_InputIndicesByPhysicalAttachment;

        internal VulkanRasterSubPassLowering(
            in RasterSubPassPlan plan)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            RasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            LocalInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~RasterOrderedMask));
            SampledInputMask =
                attachmentInterface.SampledFeedbackMask;
            DeclaredOutputMask =
                attachmentInterface.ColorOutputMask;
            OutputMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~RasterOrderedMask));
            PreserveMask = plan.PreserveMask;
            TransitionMask = plan.TransitionMask;
            DepthStencilFlags =
                attachmentInterface.DepthStencilFlags;

            m_ColorInputLogicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            m_ColorOutputLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            m_SampledFeedbackLogicalAttachments =
                new int[attachmentInterface.SampledFeedbackSlotCount];
            m_OutputLocationsByPhysicalAttachment =
                new int[attachmentInterface.ColorAttachmentCount];
            m_InputIndicesByPhysicalAttachment =
                new int[attachmentInterface.ColorAttachmentCount];
            Array.Fill(
                m_OutputLocationsByPhysicalAttachment,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            Array.Fill(
                m_InputIndicesByPhysicalAttachment,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);

            for (int inputIndex = 0;
                 inputIndex <
                    m_ColorInputLogicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (IsRasterOrdered(
                        logicalAttachment,
                        RasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_ColorInputLogicalAttachments[inputIndex] =
                    logicalAttachment;
                if (logicalAttachment >= 0)
                {
                    m_InputIndicesByPhysicalAttachment[
                        logicalAttachment] = inputIndex;
                }
            }

            bool hasNonIdentityOutputMapping = false;
            for (int outputLocation = 0;
                 outputLocation <
                    m_ColorOutputLogicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (IsRasterOrdered(
                        logicalAttachment,
                        RasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_ColorOutputLogicalAttachments[outputLocation] =
                    logicalAttachment;
                hasNonIdentityOutputMapping |=
                    logicalAttachment != outputLocation;
                if (logicalAttachment >= 0)
                {
                    m_OutputLocationsByPhysicalAttachment[
                        logicalAttachment] = outputLocation;
                }
            }
            HasNonIdentityOutputMapping =
                hasNonIdentityOutputMapping;

            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    m_SampledFeedbackLogicalAttachments.Length;
                 ++sampledOrdinal)
            {
                m_SampledFeedbackLogicalAttachments[
                    sampledOrdinal] =
                        attachmentInterface
                            .GetSampledFeedbackLogicalAttachment(
                                sampledOrdinal);
            }
        }

        internal int GetColorInputLogicalAttachment(int inputIndex)
        {
            if ((uint)inputIndex >=
                (uint)m_ColorInputLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return m_ColorInputLogicalAttachments[inputIndex];
        }

        internal int GetColorOutputLogicalAttachment(
            int outputLocation)
        {
            if ((uint)outputLocation >=
                (uint)m_ColorOutputLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputLocation));
            }
            return m_ColorOutputLogicalAttachments[outputLocation];
        }

        internal int GetSampledFeedbackLogicalAttachment(
            int sampledOrdinal)
        {
            if ((uint)sampledOrdinal >=
                (uint)m_SampledFeedbackLogicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sampledOrdinal));
            }
            return m_SampledFeedbackLogicalAttachments[
                sampledOrdinal];
        }

        internal int GetOutputLocationForPhysicalAttachment(
            int physicalAttachment)
        {
            if ((uint)physicalAttachment >=
                (uint)m_OutputLocationsByPhysicalAttachment.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physicalAttachment));
            }
            return m_OutputLocationsByPhysicalAttachment[
                physicalAttachment];
        }

        internal int GetInputIndexForPhysicalAttachment(
            int physicalAttachment)
        {
            if ((uint)physicalAttachment >=
                (uint)m_InputIndicesByPhysicalAttachment.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(physicalAttachment));
            }
            return m_InputIndicesByPhysicalAttachment[
                physicalAttachment];
        }

        private static bool IsRasterOrdered(
            int logicalAttachment,
            byte rasterOrderedMask)
        {
            return logicalAttachment >= 0 &&
                (rasterOrderedMask &
                 (1 << logicalAttachment)) != 0;
        }
    }

    internal sealed class VulkanRasterPassLowering
    {
        internal EVulkanRasterPassStrategy Strategy { get; }
        internal bool UsesRenderPass2 =>
            Strategy == EVulkanRasterPassStrategy.NativeRenderPass2;
        internal bool UsesDynamicRenderingLocalRead =>
            Strategy ==
                EVulkanRasterPassStrategy.DynamicRenderingLocalRead;
        internal bool RequiresPrivateAttachmentTable { get; }
        internal bool UsesAttachmentFeedbackLoopLayout { get; }
        internal bool UsesSampledFeedback { get; }
        internal bool UsesOrderedFragmentInterlock { get; }
        internal ReadOnlyMemory<VulkanRasterSubPassLowering> SubPasses =>
            m_SubPasses;
        internal ReadOnlyMemory<VulkanRasterPhaseBarrierPlan>
            PhaseBarriers => m_PhaseBarriers;
        internal ReadOnlyMemory<EVulkanRasterAttachmentScopeLayout>
            AttachmentScopeLayouts => m_AttachmentScopeLayouts;

        private readonly VulkanRasterSubPassLowering[] m_SubPasses;
        private readonly VulkanRasterPhaseBarrierPlan[] m_PhaseBarriers;
        private readonly EVulkanRasterAttachmentScopeLayout[]
            m_AttachmentScopeLayouts;

        private VulkanRasterPassLowering(
            EVulkanRasterPassStrategy strategy,
            bool requiresPrivateAttachmentTable,
            bool usesAttachmentFeedbackLoopLayout,
            bool usesSampledFeedback,
            bool usesOrderedFragmentInterlock,
            VulkanRasterSubPassLowering[] subPasses,
            VulkanRasterPhaseBarrierPlan[] phaseBarriers,
            EVulkanRasterAttachmentScopeLayout[]
                attachmentScopeLayouts)
        {
            Strategy = strategy;
            RequiresPrivateAttachmentTable =
                requiresPrivateAttachmentTable;
            UsesAttachmentFeedbackLoopLayout =
                usesAttachmentFeedbackLoopLayout;
            UsesSampledFeedback = usesSampledFeedback;
            UsesOrderedFragmentInterlock =
                usesOrderedFragmentInterlock;
            m_SubPasses = subPasses;
            m_PhaseBarriers = phaseBarriers;
            m_AttachmentScopeLayouts = attachmentScopeLayouts;
        }

        internal static VulkanRasterPassLowering Compile(
            RasterPassPlan plan,
            in VulkanRasterCapabilities capabilities,
            EVulkanRasterPassForcedStrategy forcedStrategy =
                EVulkanRasterPassForcedStrategy.Auto)
        {
            ArgumentNullException.ThrowIfNull(plan);

            bool hasLocalRead = false;
            bool hasSampledFeedback = false;
            bool hasRasterOrderedAccess = false;
            bool requiresAttachmentMapping = false;
            byte passLocalReadMask = 0;
            byte passSampledFeedbackMask = 0;
            byte passRasterOrderedMask = 0;
            VulkanRasterSubPassLowering[] subPasses =
                new VulkanRasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                VulkanRasterSubPassLowering lowering =
                    new VulkanRasterSubPassLowering(subPass);
                subPasses[i] = lowering;
                hasLocalRead |= lowering.LocalInputMask != 0;
                hasSampledFeedback |=
                    lowering.SampledInputMask != 0;
                hasRasterOrderedAccess |=
                    lowering.RasterOrderedMask != 0;
                requiresAttachmentMapping |=
                    lowering.LocalInputMask != 0 ||
                    lowering.HasNonIdentityOutputMapping;
                passLocalReadMask |= lowering.LocalInputMask;
                passSampledFeedbackMask |=
                    lowering.SampledInputMask;
                passRasterOrderedMask |=
                    lowering.RasterOrderedMask;
            }

            ValidateInitialDepthStencilReadOnlyContract(
                plan,
                subPasses[0]);
            ValidateExactCapabilities(
                in capabilities,
                hasSampledFeedback,
                hasRasterOrderedAccess);
            ValidateNoCrossMechanismAlias(
                passLocalReadMask,
                passSampledFeedbackMask,
                passRasterOrderedMask);

            bool supportsExactDynamicLocalRead =
                capabilities.DynamicRenderingLocalRead &&
                (!plan.HasDepthStencilAttachment ||
                 capabilities
                    .DynamicRenderingLocalReadDepthStencil) &&
                (plan.SampleCount == ERHISampleCount.None ||
                 capabilities
                    .DynamicRenderingLocalReadMultisampled);
            EVulkanRasterPassStrategy strategy =
                SelectStrategy(
                    in capabilities,
                    supportsExactDynamicLocalRead,
                    requiresAttachmentMapping,
                    hasRasterOrderedAccess,
                    forcedStrategy);
            VulkanRasterPhaseBarrierPlan[] barriers =
                CompilePhaseBarriers(subPasses);
            EVulkanRasterAttachmentScopeLayout[] layouts =
                CompileScopeLayouts(
                    plan.ColorAttachmentCount,
                    passLocalReadMask,
                    passSampledFeedbackMask,
                    passRasterOrderedMask);
            return new VulkanRasterPassLowering(
                strategy,
                requiresPrivateAttachmentTable:
                    hasLocalRead || hasRasterOrderedAccess,
                usesAttachmentFeedbackLoopLayout:
                    hasSampledFeedback || hasRasterOrderedAccess,
                usesSampledFeedback: hasSampledFeedback,
                usesOrderedFragmentInterlock:
                    hasRasterOrderedAccess,
                subPasses,
                barriers,
                layouts);
        }

        private static void ValidateInitialDepthStencilReadOnlyContract(
            RasterPassPlan plan,
            in VulkanRasterSubPassLowering firstSubPass)
        {
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            if ((firstSubPass.DepthStencilFlags &
                 ERHISubPassFlags.ReadOnlyDepth) != 0 &&
                descriptor.DepthLoadOp != ERHILoadAction.Load)
            {
                throw new ArgumentException(
                    "The first Vulkan subpass cannot declare ReadOnlyDepth " +
                    "unless the depth attachment load action is Load.");
            }
            if ((firstSubPass.DepthStencilFlags &
                 ERHISubPassFlags.ReadOnlyStencil) != 0 &&
                descriptor.StencilLoadOp != ERHILoadAction.Load)
            {
                throw new ArgumentException(
                    "The first Vulkan subpass cannot declare ReadOnlyStencil " +
                    "unless the stencil attachment load action is Load.");
            }
        }

        private static void ValidateExactCapabilities(
            in VulkanRasterCapabilities capabilities,
            bool hasSampledFeedback,
            bool hasRasterOrderedAccess)
        {
            if (hasSampledFeedback &&
                !capabilities.AttachmentFeedbackLoopLayout)
            {
                throw new NotSupportedException(
                    "The raster pass declares SampledFeedback, but " +
                    "VK_EXT_attachment_feedback_loop_layout is unavailable.");
            }
            if (hasRasterOrderedAccess &&
                (!capabilities.DynamicRendering ||
                 !capabilities.OrderedFragmentPixelInterlock ||
                 !capabilities.FragmentStoresAndAtomics ||
                 !capabilities.AttachmentFeedbackLoopLayout ||
                 !capabilities.UnifiedImageLayouts))
            {
                throw new NotSupportedException(
                    "The raster pass declares RasterOrderedReadWrite, but " +
                    "the exact Vulkan path requires " +
                    "dynamic rendering, VK_KHR_unified_image_layouts, " +
                    "VK_EXT_attachment_feedback_loop_layout, " +
                    "VK_EXT_fragment_shader_interlock, " +
                    "fragmentStoresAndAtomics, and their required features.");
            }
        }

        private static void ValidateNoCrossMechanismAlias(
            byte passLocalReadMask,
            byte passSampledFeedbackMask,
            byte passRasterOrderedMask)
        {
            byte rasterOrderedAliasMask = checked((byte)(
                passRasterOrderedMask &
                (passLocalReadMask | passSampledFeedbackMask)));
            if (rasterOrderedAliasMask != 0)
            {
                throw new NotSupportedException(
                    "A Vulkan raster attachment cannot switch between " +
                    "RasterOrderedReadWrite storage access and local/sampled " +
                    "attachment access inside one native render scope.");
            }
        }

        private static VulkanRasterPhaseBarrierPlan[]
            CompilePhaseBarriers(
                ReadOnlySpan<VulkanRasterSubPassLowering> subPasses)
        {
            VulkanRasterPhaseBarrierPlan[] barriers =
                new VulkanRasterPhaseBarrierPlan[
                    Math.Max(0, subPasses.Length - 1)];
            byte priorWriteMask = subPasses.Length == 0
                ? (byte)0
                : subPasses[0].DeclaredOutputMask;
            byte priorOrderedStorageMask = subPasses.Length == 0
                ? (byte)0
                : subPasses[0].RasterOrderedMask;
            for (int destinationIndex = 1;
                 destinationIndex < subPasses.Length;
                 ++destinationIndex)
            {
                ref readonly VulkanRasterSubPassLowering destination =
                    ref subPasses[destinationIndex];
                byte localReadMask = checked((byte)(
                    priorWriteMask &
                    destination.LocalInputMask));
                byte feedbackLoopMask = checked((byte)(
                    priorWriteMask &
                    destination.SampledInputMask));
                byte orderedStorageMask = checked((byte)(
                    priorOrderedStorageMask &
                    destination.RasterOrderedMask));
                EVulkanRasterPhaseBarrierKind kind =
                    EVulkanRasterPhaseBarrierKind.None;
                if (localReadMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .FramebufferByRegion;
                }
                if (feedbackLoopMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .FeedbackLoopByRegion;
                }
                if (orderedStorageMask != 0)
                {
                    kind |= EVulkanRasterPhaseBarrierKind
                        .OrderedStorageByRegion;
                }
                barriers[destinationIndex - 1] =
                    new VulkanRasterPhaseBarrierPlan(
                        kind,
                        checked((byte)(
                            localReadMask |
                            feedbackLoopMask |
                            orderedStorageMask)));
                priorWriteMask |= destination.DeclaredOutputMask;
                priorOrderedStorageMask |=
                    destination.RasterOrderedMask;
            }
            return barriers;
        }

        private static EVulkanRasterAttachmentScopeLayout[]
            CompileScopeLayouts(
                int colorAttachmentCount,
                byte passLocalReadMask,
                byte passSampledFeedbackMask,
                byte passRasterOrderedMask)
        {
            EVulkanRasterAttachmentScopeLayout[] layouts =
                new EVulkanRasterAttachmentScopeLayout[
                    colorAttachmentCount];
            for (int logicalAttachment = 0;
                 logicalAttachment < colorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((passRasterOrderedMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .GeneralStorage;
                }
                else if ((passSampledFeedbackMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .AttachmentFeedbackLoop;
                }
                else if ((passLocalReadMask & bit) != 0)
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .RenderingLocalRead;
                }
                else
                {
                    layouts[logicalAttachment] =
                        EVulkanRasterAttachmentScopeLayout
                            .ColorAttachment;
                }
            }
            return layouts;
        }

        private static EVulkanRasterPassStrategy SelectStrategy(
            in VulkanRasterCapabilities capabilities,
            bool supportsExactDynamicLocalRead,
            bool requiresAttachmentMapping,
            bool hasRasterOrderedAccess,
            EVulkanRasterPassForcedStrategy forcedStrategy)
        {
            switch (forcedStrategy)
            {
                case EVulkanRasterPassForcedStrategy.Auto:
                    // RasterOrderedReadWrite uses a storage-image binding in
                    // GENERAL plus VkAttachmentFeedbackLoopInfoEXT on the
                    // dynamic-rendering attachment. It has no local-input
                    // attachment semantic, so dynamic-local-read would add a
                    // needless device requirement and must not be selected.
                    if (hasRasterOrderedAccess &&
                        capabilities.DynamicRendering)
                    {
                        return EVulkanRasterPassStrategy.DynamicRendering;
                    }
                    if (!requiresAttachmentMapping &&
                        capabilities.DynamicRendering)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRendering;
                    }
                    if (capabilities.DynamicRendering &&
                        supportsExactDynamicLocalRead)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRenderingLocalRead;
                    }
                    if (!hasRasterOrderedAccess &&
                        capabilities.RenderPass2)
                    {
                        return EVulkanRasterPassStrategy
                            .NativeRenderPass2;
                    }
                    break;

                case EVulkanRasterPassForcedStrategy
                        .DynamicRenderingLocalRead:
                    if (capabilities.DynamicRendering &&
                        supportsExactDynamicLocalRead)
                    {
                        return EVulkanRasterPassStrategy
                            .DynamicRenderingLocalRead;
                    }
                    break;

                case EVulkanRasterPassForcedStrategy.NativeRenderPass2:
                    if (hasRasterOrderedAccess)
                    {
                        throw new NotSupportedException(
                            "The exact Vulkan RasterOrderedReadWrite path " +
                            "requires VkAttachmentFeedbackLoopInfoEXT on a " +
                            "dynamic-rendering attachment while the storage " +
                            "descriptor remains in GENERAL; RenderPass2 " +
                            "cannot express that combination.");
                    }
                    if (capabilities.RenderPass2)
                    {
                        return EVulkanRasterPassStrategy
                            .NativeRenderPass2;
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(forcedStrategy),
                        forcedStrategy,
                        "Unknown Vulkan raster lowering strategy.");
            }

            throw new NotSupportedException(
                $"The requested Vulkan raster strategy {forcedStrategy} " +
                "cannot express this pass on the current device.");
        }
    }



    internal readonly struct VulkanPrivateRasterBindingPlan
    {
        internal const uint InputAttachmentBindingBase = 0;
        internal const uint RasterOrderedBindingBase = 8;

        internal uint DescriptorSet { get; }
        internal byte LocalInputMask { get; }
        internal byte LocalInputBindingMask { get; }
        internal byte RasterOrderedMask { get; }
        internal uint InputAttachmentCount =>
            checked((uint)BitOperations.PopCount((uint)LocalInputBindingMask));
        internal uint StorageImageCount =>
            checked((uint)BitOperations.PopCount((uint)RasterOrderedMask));
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            new VulkanDescriptorPoolRequirements(
                samplers: 0,
                sampledImages: 0,
                storageImages: StorageImageCount,
                uniformBuffers: 0,
                storageBuffers: 0,
                accelerationStructures: 0,
                inputAttachments: InputAttachmentCount);

        internal bool HasPrivateBindings =>
            LocalInputBindingMask != 0 ||
            RasterOrderedMask != 0;

        private VulkanPrivateRasterBindingPlan(
            uint descriptorSet,
            byte localInputMask,
            byte localInputBindingMask,
            byte rasterOrderedMask)
        {
            DescriptorSet = descriptorSet;
            LocalInputMask = localInputMask;
            LocalInputBindingMask = localInputBindingMask;
            RasterOrderedMask = rasterOrderedMask;
        }

        internal bool UsesInputAttachmentBinding(int inputIndex)
        {
            if ((uint)inputIndex >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return (LocalInputBindingMask & (1 << inputIndex)) != 0;
        }

        internal uint GetInputAttachmentBinding(int inputIndex)
        {
            if (!UsesInputAttachmentBinding(inputIndex))
            {
                throw new InvalidOperationException(
                    $"Input index {inputIndex} is not a private Vulkan " +
                    "input-attachment binding.");
            }
            return checked(
                InputAttachmentBindingBase +
                checked((uint)inputIndex));
        }

        internal uint GetRasterOrderedBinding(
            int logicalAttachment)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            byte bit =
                checked((byte)(1 << logicalAttachment));
            if ((RasterOrderedMask & bit) == 0)
            {
                throw new InvalidOperationException(
                    $"Logical attachment {logicalAttachment} is not " +
                    "raster-ordered in this pipeline.");
            }
            return checked(
                RasterOrderedBindingBase +
                checked((uint)logicalAttachment));
        }

        internal static uint GetPrivateAttachmentDescriptorSet(
            ReadOnlySpan<uint> ordinaryDescriptorSets)
        {
            bool hasOrdinarySet = false;
            uint highestOrdinarySet = 0;
            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet = ordinaryDescriptorSets[index];
                if (!hasOrdinarySet ||
                    descriptorSet > highestOrdinarySet)
                {
                    highestOrdinarySet = descriptorSet;
                    hasOrdinarySet = true;
                }
            }

            if (hasOrdinarySet &&
                highestOrdinarySet == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordinaryDescriptorSets),
                    highestOrdinarySet,
                    "The highest Vulkan descriptor-set index leaves no " +
                    "representable slot for the private attachment set.");
            }
            return hasOrdinarySet
                ? highestOrdinarySet + 1
                : 0;
        }

        internal static VulkanPrivateRasterBindingPlan Compile(
            ReadOnlySpan<uint> ordinaryDescriptorSets,
            uint maximumBoundDescriptorSets,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumBoundDescriptorSets == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBoundDescriptorSets),
                    maximumBoundDescriptorSets,
                    "Vulkan must expose at least one bound descriptor set.");
            }

            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet =
                    ordinaryDescriptorSets[index];
                if (descriptorSet >= maximumBoundDescriptorSets)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ordinaryDescriptorSets),
                        descriptorSet,
                        $"Ordinary Vulkan descriptor set {descriptorSet} " +
                        $"exceeds maxBoundDescriptorSets " +
                        $"{maximumBoundDescriptorSets}.");
                }
                for (int previous = 0;
                     previous < index;
                     ++previous)
                {
                    if (ordinaryDescriptorSets[previous] ==
                        descriptorSet)
                    {
                        throw new ArgumentException(
                            $"Ordinary Vulkan descriptor set " +
                            $"{descriptorSet} is duplicated.",
                            nameof(ordinaryDescriptorSets));
                    }
                }
            }
            uint privateDescriptorSet =
                GetPrivateAttachmentDescriptorSet(
                    ordinaryDescriptorSets);

            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            byte localInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~rasterOrderedMask));
            byte localInputBindingMask = 0;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0 ||
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    continue;
                }
                localInputBindingMask |=
                    checked((byte)(1 << inputIndex));
            }

            bool hasPrivateBindings =
                localInputBindingMask != 0 ||
                rasterOrderedMask != 0;
            if (hasPrivateBindings &&
                privateDescriptorSet >= maximumBoundDescriptorSets)
            {
                throw new NotSupportedException(
                    $"Vulkan ordinary descriptor sets consume slots through " +
                    $"{privateDescriptorSet - 1}; no slot remains for the private " +
                    $"attachment set within maxBoundDescriptorSets " +
                    $"{maximumBoundDescriptorSets}.");
            }

            return new VulkanPrivateRasterBindingPlan(
                privateDescriptorSet,
                localInputMask,
                localInputBindingMask,
                rasterOrderedMask);
        }

        internal static void ValidatePipelineLayoutIdentity(
            bool isDisposed,
            object? actualDevice,
            object expectedDevice)
        {
            ArgumentNullException.ThrowIfNull(expectedDevice);
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Vulkan pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }
    }

    internal unsafe sealed class VulkanPrivateRasterDescriptorLayout :
        IDisposable
    {
        internal VulkanPrivateRasterBindingPlan Plan { get; }
        internal VkDescriptorSetLayout NativeLayout { get; private set; }
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            Plan.PoolRequirements;

        private readonly VkDevice m_NativeDevice;
        private bool m_Disposed;

        internal VulkanPrivateRasterDescriptorLayout(
            VulkanDevice device,
            in VulkanPrivateRasterBindingPlan plan)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (!plan.HasPrivateBindings)
            {
                throw new ArgumentException(
                    "A Vulkan private descriptor layout requires at least " +
                    "one input-attachment or raster-ordered binding.",
                    nameof(plan));
            }
            ValidateLimits(device.DescriptorLimits, in plan);

            m_NativeDevice = device.NativeDevice;
            Plan = plan;
            VkDescriptorSetLayoutBinding* bindings =
                stackalloc VkDescriptorSetLayoutBinding[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            int bindingCount = 0;
            for (int inputIndex = 0;
                 inputIndex < RHIAttachmentIndexArray.MaxAttachments;
                 ++inputIndex)
            {
                if (!plan.UsesInputAttachmentBinding(inputIndex))
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetInputAttachmentBinding(inputIndex),
                        descriptorType =
                            VkDescriptorType.InputAttachment,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((plan.RasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetRasterOrderedBinding(
                                logicalAttachment),
                        descriptorType =
                            VkDescriptorType.StorageImage,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }

            VkDescriptorSetLayoutCreateInfo createInfo =
                new VkDescriptorSetLayoutCreateInfo
                {
                    sType =
                        VkStructureType.DescriptorSetLayoutCreateInfo,
                    bindingCount = checked((uint)bindingCount),
                    pBindings = bindings,
                };
            VkDescriptorSetLayout nativeLayout = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateDescriptorSetLayout(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &nativeLayout));
            NativeLayout = nativeLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (NativeLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(
                    m_NativeDevice,
                    NativeLayout,
                    null);
                NativeLayout = default;
            }
            m_Disposed = true;
        }

        private static void ValidateLimits(
            in VulkanDescriptorLimits limits,
            in VulkanPrivateRasterBindingPlan plan)
        {
            if (plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerSet ||
                plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster input-attachment count " +
                    $"{plan.InputAttachmentCount} exceeds the device " +
                    "descriptor limits.");
            }
            if (plan.StorageImageCount >
                    limits.MaximumStorageImagesPerSet ||
                plan.StorageImageCount >
                    limits.MaximumStorageImagesPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster storage-image count " +
                    $"{plan.StorageImageCount} exceeds the device " +
                    "descriptor limits.");
            }
        }
    }

    internal sealed class VulkanDescriptorSetLeaseTransaction :
        IDisposable
    {
        private readonly Action<VulkanDescriptorSetLease> m_Rollback;
        private VulkanDescriptorSetLease m_Lease;
        private bool m_Committed;
        private bool m_Disposed;

        internal VulkanDescriptorSetLeaseTransaction(
            in VulkanDescriptorSetLease lease,
            Action<VulkanDescriptorSetLease> rollback)
        {
            ArgumentNullException.ThrowIfNull(rollback);
            if (lease.Pool.Handle == 0 || lease.Set.Handle == 0)
            {
                throw new ArgumentException(
                    "A private Vulkan descriptor transaction requires a " +
                    "complete native lease.",
                    nameof(lease));
            }
            m_Lease = lease;
            m_Rollback = rollback;
        }

        internal VulkanDescriptorSetLease Lease
        {
            get
            {
                ObjectDisposedException.ThrowIf(m_Disposed, this);
                return m_Lease;
            }
        }

        internal void Commit(
            Action<VulkanDescriptorSetLease> register)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            ArgumentNullException.ThrowIfNull(register);
            if (m_Committed)
            {
                throw new InvalidOperationException(
                    "The Vulkan descriptor lease is already committed.");
            }
            register(m_Lease);
            m_Committed = true;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            try
            {
                if (!m_Committed)
                {
                    m_Rollback(m_Lease);
                }
            }
            finally
            {
                m_Lease = default;
                m_Disposed = true;
            }
        }
    }



    internal sealed unsafe class
        VulkanDynamicRenderingAttachmentMappingPlan
    {
        internal const uint AttachmentUnused = uint.MaxValue;

        internal int ColorAttachmentCount =>
            m_OutputLocationsByPhysicalAttachment.Length;
        internal bool HasLocalReadOrRemapping { get; }

        private readonly uint[]
            m_OutputLocationsByPhysicalAttachment;
        private readonly uint[]
            m_InputIndicesByPhysicalAttachment;

        private VulkanDynamicRenderingAttachmentMappingPlan(
            uint[] outputLocationsByPhysicalAttachment,
            uint[] inputIndicesByPhysicalAttachment,
            bool hasLocalReadOrRemapping)
        {
            m_OutputLocationsByPhysicalAttachment =
                outputLocationsByPhysicalAttachment;
            m_InputIndicesByPhysicalAttachment =
                inputIndicesByPhysicalAttachment;
            HasLocalReadOrRemapping = hasLocalReadOrRemapping;
        }

        internal static
            VulkanDynamicRenderingAttachmentMappingPlan Compile(
                in RHIAttachmentInterfaceSignature signature)
        {
            int colorCount = signature.ColorAttachmentCount;
            uint[] outputs = new uint[colorCount];
            uint[] inputs = new uint[colorCount];
            Array.Fill(outputs, AttachmentUnused);
            Array.Fill(inputs, AttachmentUnused);

            byte rasterOrderedMask =
                signature.RasterOrderedReadWriteMask;
            bool requiresMapping = false;
            for (int outputLocation = 0;
                 outputLocation <
                    signature.ColorOutputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    signature.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    outputs[logicalAttachment] =
                        checked((uint)outputLocation);
                }
                requiresMapping |=
                    logicalAttachment != outputLocation;
            }

            for (int inputIndex = 0;
                 inputIndex <
                    signature.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    signature.GetColorInputLogicalAttachment(
                        inputIndex);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    inputs[logicalAttachment] =
                        checked((uint)inputIndex);
                    requiresMapping = true;
                }
            }

            for (int physicalAttachment = 0;
                 physicalAttachment < colorCount;
                 ++physicalAttachment)
            {
                requiresMapping |=
                    outputs[physicalAttachment] !=
                        checked((uint)physicalAttachment) ||
                    inputs[physicalAttachment] != AttachmentUnused;
            }

            return new(
                outputs,
                inputs,
                requiresMapping);
        }

        internal void Populate(
            uint* outputLocations,
            uint* inputIndices)
        {
            if (ColorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < ColorAttachmentCount;
                 ++physicalAttachment)
            {
                outputLocations[physicalAttachment] =
                    m_OutputLocationsByPhysicalAttachment[
                        physicalAttachment];
                inputIndices[physicalAttachment] =
                    m_InputIndicesByPhysicalAttachment[
                        physicalAttachment];
            }
        }

        internal static void Populate(
            in VulkanRasterSubPassLowering subPass,
            int colorAttachmentCount,
            uint* outputLocations,
            uint* inputIndices)
        {
            if (colorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < colorAttachmentCount;
                 ++physicalAttachment)
            {
                int outputLocation =
                    subPass.GetOutputLocationForPhysicalAttachment(
                        physicalAttachment);
                int inputIndex =
                    subPass.GetInputIndexForPhysicalAttachment(
                        physicalAttachment);
                outputLocations[physicalAttachment] =
                    outputLocation < 0
                        ? AttachmentUnused
                        : checked((uint)outputLocation);
                inputIndices[physicalAttachment] =
                    inputIndex < 0
                        ? AttachmentUnused
                        : checked((uint)inputIndex);
            }
        }

        private static bool IsRasterOrdered(
            int logicalAttachment,
            byte rasterOrderedMask) =>
            logicalAttachment >= 0 &&
            (rasterOrderedMask &
             (1 << logicalAttachment)) != 0;
    }



    internal sealed class VulkanRenderPass2SubPassDescription
    {
        internal ReadOnlyMemory<int> InputAttachmentsByIndex =>
            m_InputAttachmentsByIndex;
        internal ReadOnlyMemory<int> ColorAttachmentsByLocation =>
            m_ColorAttachmentsByLocation;
        internal ReadOnlyMemory<int> ResolveAttachmentsByLocation =>
            m_ResolveAttachmentsByLocation;
        internal ReadOnlyMemory<int> SampledFeedbackAttachments =>
            m_SampledFeedbackAttachments;
        internal ReadOnlyMemory<int> PreserveAttachments =>
            m_PreserveAttachments;
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool ResolvesDepthStencil { get; }
        internal bool UsesAttachmentFeedbackLoopLayout =>
            m_SampledFeedbackAttachments.Length != 0;
        internal bool UsesOrderedFragmentInterlock { get; }
        internal byte RasterOrderedMask { get; }
        internal byte InputMask { get; }
        internal byte OutputMask { get; }
        internal byte SampledFeedbackMask { get; }
        internal byte OrdinaryReadWriteMask =>
            checked((byte)(InputMask & OutputMask));

        private readonly int[] m_InputAttachmentsByIndex;
        private readonly int[] m_ColorAttachmentsByLocation;
        private readonly int[] m_ResolveAttachmentsByLocation;
        private readonly int[] m_SampledFeedbackAttachments;
        private readonly int[] m_PreserveAttachments;

        internal VulkanRenderPass2SubPassDescription(
            int[] inputAttachmentsByIndex,
            int[] colorAttachmentsByLocation,
            int[] resolveAttachmentsByLocation,
            int[] sampledFeedbackAttachments,
            int[] preserveAttachments,
            ERHISubPassFlags depthStencilFlags,
            bool resolvesDepthStencil,
            byte rasterOrderedMask)
        {
            m_InputAttachmentsByIndex = inputAttachmentsByIndex;
            m_ColorAttachmentsByLocation =
                colorAttachmentsByLocation;
            m_ResolveAttachmentsByLocation =
                resolveAttachmentsByLocation;
            m_SampledFeedbackAttachments =
                sampledFeedbackAttachments;
            m_PreserveAttachments = preserveAttachments;
            DepthStencilFlags = depthStencilFlags;
            ResolvesDepthStencil = resolvesDepthStencil;
            RasterOrderedMask = rasterOrderedMask;
            UsesOrderedFragmentInterlock =
                rasterOrderedMask != 0;
            InputMask = BuildMask(inputAttachmentsByIndex);
            OutputMask = BuildMask(colorAttachmentsByLocation);
            SampledFeedbackMask =
                BuildMask(sampledFeedbackAttachments);
        }

        private static byte BuildMask(ReadOnlySpan<int> attachments)
        {
            byte mask = 0;
            for (int index = 0;
                 index < attachments.Length;
                 ++index)
            {
                int attachment = attachments[index];
                if (attachment >= 0)
                {
                    mask |= checked((byte)(1 << attachment));
                }
            }
            return mask;
        }
    }

    internal sealed class VulkanRenderPass2Description
    {
        internal ReadOnlyMemory<int> ColorResolveAttachmentIndices =>
            m_ColorResolveAttachmentIndices;
        internal ReadOnlyMemory<VulkanRenderPass2SubPassDescription>
            SubPasses => m_SubPasses;
        internal int DepthStencilAttachmentIndex { get; }
        internal int DepthStencilResolveAttachmentIndex { get; }
        internal int AttachmentCount { get; }

        private readonly int[] m_ColorResolveAttachmentIndices;
        private readonly VulkanRenderPass2SubPassDescription[] m_SubPasses;

        private VulkanRenderPass2Description(
            int[] colorResolveAttachmentIndices,
            VulkanRenderPass2SubPassDescription[] subPasses,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex,
            int attachmentCount)
        {
            m_ColorResolveAttachmentIndices =
                colorResolveAttachmentIndices;
            m_SubPasses = subPasses;
            DepthStencilAttachmentIndex =
                depthStencilAttachmentIndex;
            DepthStencilResolveAttachmentIndex =
                depthStencilResolveAttachmentIndex;
            AttachmentCount = attachmentCount;
        }

        internal static VulkanRenderPass2Description Compile(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(lowering);
            if (lowering.SubPasses.Length != plan.SubPassCount)
            {
                throw new ArgumentException(
                    "The Vulkan lowering does not belong to this raster pass.",
                    nameof(lowering));
            }

            int[] resolveAttachmentIndices =
                new int[plan.ColorAttachmentCount];
            Array.Fill(resolveAttachmentIndices, -1);
            int attachmentCount = plan.ColorAttachmentCount;
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                if (plan.GetColorAttachment(colorIndex).ResolveTarget !=
                    null)
                {
                    resolveAttachmentIndices[colorIndex] =
                        attachmentCount++;
                }
            }

            int depthStencilAttachmentIndex = -1;
            int depthStencilResolveAttachmentIndex = -1;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilAttachmentIndex = attachmentCount++;
                if (plan.GetDepthStencilAttachment().ResolveTarget != null)
                {
                    depthStencilResolveAttachmentIndex =
                        attachmentCount++;
                }
            }

            int[] finalColorOutputSubPass =
                new int[plan.ColorAttachmentCount];
            Array.Fill(finalColorOutputSubPass, -1);
            for (int subPassIndex = 0;
                 subPassIndex < plan.SubPassCount;
                 ++subPassIndex)
            {
                ref readonly VulkanRasterSubPassLowering subPass =
                    ref lowering.SubPasses.Span[subPassIndex];
                for (int outputLocation = 0;
                     outputLocation <
                        subPass.ColorOutputLocationCount;
                     ++outputLocation)
                {
                    int logicalAttachment =
                        subPass.GetColorOutputLogicalAttachment(
                            outputLocation);
                    if (logicalAttachment >= 0)
                    {
                        finalColorOutputSubPass[logicalAttachment] =
                            subPassIndex;
                    }
                }
            }

            VulkanRenderPass2SubPassDescription[] subPasses =
                new VulkanRenderPass2SubPassDescription[
                    plan.SubPassCount];
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                ref readonly RasterSubPassPlan planSubPass =
                    ref plan.GetSubPass(subPassIndex);
                ref readonly VulkanRasterSubPassLowering loweringSubPass =
                    ref lowering.SubPasses.Span[subPassIndex];

                int[] inputs =
                    new int[loweringSubPass.ColorInputSlotCount];
                for (int inputIndex = 0;
                     inputIndex < inputs.Length;
                     ++inputIndex)
                {
                    inputs[inputIndex] =
                        loweringSubPass
                            .GetColorInputLogicalAttachment(
                                inputIndex);
                }

                int[] colors =
                    new int[
                        loweringSubPass.ColorOutputLocationCount];
                int[] resolves = new int[colors.Length];
                Array.Fill(resolves, -1);
                for (int outputLocation = 0;
                     outputLocation < colors.Length;
                     ++outputLocation)
                {
                    int logicalAttachment =
                        loweringSubPass
                            .GetColorOutputLogicalAttachment(
                                outputLocation);
                    colors[outputLocation] = logicalAttachment;
                    if (logicalAttachment >= 0 &&
                        resolveAttachmentIndices[
                            logicalAttachment] >= 0 &&
                        finalColorOutputSubPass[
                            logicalAttachment] == subPassIndex)
                    {
                        resolves[outputLocation] =
                            resolveAttachmentIndices[
                                logicalAttachment];
                    }
                }

                int[] sampledFeedback =
                    new int[
                        loweringSubPass.SampledFeedbackSlotCount];
                for (int sampledOrdinal = 0;
                     sampledOrdinal < sampledFeedback.Length;
                     ++sampledOrdinal)
                {
                    sampledFeedback[sampledOrdinal] =
                        loweringSubPass
                            .GetSampledFeedbackLogicalAttachment(
                                sampledOrdinal);
                }

                byte nativeReferenceMask = checked((byte)(
                    loweringSubPass.LocalInputMask |
                    loweringSubPass.OutputMask));
                byte descriptorAttachmentMask = checked((byte)(
                    loweringSubPass.SampledInputMask |
                    loweringSubPass.RasterOrderedMask));
                byte preserveMask = checked((byte)(
                    planSubPass.PreserveMask |
                    (descriptorAttachmentMask &
                     ~nativeReferenceMask)));
                List<int> preserves =
                    new List<int>(plan.ColorAttachmentCount);
                for (int logicalAttachment = 0;
                     logicalAttachment < plan.ColorAttachmentCount;
                     ++logicalAttachment)
                {
                    if ((preserveMask &
                         (1 << logicalAttachment)) != 0)
                    {
                        preserves.Add(logicalAttachment);
                    }
                }

                subPasses[subPassIndex] =
                    new VulkanRenderPass2SubPassDescription(
                        inputs,
                        colors,
                        resolves,
                        sampledFeedback,
                        preserves.ToArray(),
                        loweringSubPass.DepthStencilFlags,
                        depthStencilResolveAttachmentIndex >= 0 &&
                            subPassIndex == plan.SubPassCount - 1,
                        loweringSubPass.RasterOrderedMask);
            }

            return new VulkanRenderPass2Description(
                resolveAttachmentIndices,
                subPasses,
                depthStencilAttachmentIndex,
                depthStencilResolveAttachmentIndex,
                attachmentCount);
        }
    }



    internal static unsafe class VulkanRenderPass2DescriptionLowering
    {
        private const uint AttachmentUnused = uint.MaxValue;

        internal static void PopulateSubpasses(
            RasterPassPlan plan,
            VulkanRenderPass2Description description,
            VkSubpassDescription2* subpasses,
            VkAttachmentReference2* inputReferences,
            VkAttachmentReference2* colorReferences,
            VkAttachmentReference2* resolveReferences,
            uint* preserveReferences,
            VkAttachmentReference2* depthReferences,
            VkAttachmentReference2* depthResolveReferences,
            VkSubpassDescriptionDepthStencilResolve*
                depthResolveDescriptions,
            int[] resolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(description);
            _ = resolveAttachmentIndices;

            for (int subPassIndex = 0;
                 subPassIndex < plan.SubPassCount;
                 ++subPassIndex)
            {
                VulkanRenderPass2SubPassDescription subPass =
                    description.SubPasses.Span[subPassIndex];
                int referenceBase =
                    subPassIndex *
                    RHIAttachmentIndexArray.MaxAttachments;

                ReadOnlySpan<int> inputs =
                    subPass.InputAttachmentsByIndex.Span;
                for (int inputIndex = 0;
                     inputIndex < inputs.Length;
                     ++inputIndex)
                {
                    int logicalAttachment = inputs[inputIndex];
                    inputReferences[referenceBase + inputIndex] =
                        CreateAttachmentReference(
                            logicalAttachment >= 0
                                ? checked((uint)logicalAttachment)
                                : AttachmentUnused,
                            logicalAttachment >= 0
                                ? GetColorReferenceLayout(
                                    description,
                                    subPassIndex,
                                    logicalAttachment,
                                    inputReference: true)
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);
                }

                ReadOnlySpan<int> colors =
                    subPass.ColorAttachmentsByLocation.Span;
                ReadOnlySpan<int> resolves =
                    subPass.ResolveAttachmentsByLocation.Span;
                bool hasResolve = false;
                for (int outputLocation = 0;
                     outputLocation < colors.Length;
                     ++outputLocation)
                {
                    int logicalAttachment = colors[outputLocation];
                    colorReferences[referenceBase + outputLocation] =
                        CreateAttachmentReference(
                            logicalAttachment >= 0
                                ? checked((uint)logicalAttachment)
                                : AttachmentUnused,
                            logicalAttachment >= 0
                                ? GetColorReferenceLayout(
                                    description,
                                    subPassIndex,
                                    logicalAttachment,
                                    inputReference: false)
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);

                    int resolveAttachment = resolves[outputLocation];
                    resolveReferences[referenceBase + outputLocation] =
                        CreateAttachmentReference(
                            resolveAttachment >= 0
                                ? checked((uint)resolveAttachment)
                                : AttachmentUnused,
                            resolveAttachment >= 0
                                ? VkImageLayout.ColorAttachmentOptimal
                                : VkImageLayout.Undefined,
                            VkImageAspectFlags.Color);
                    hasResolve |= resolveAttachment >= 0;
                }

                ReadOnlySpan<int> preserves =
                    subPass.PreserveAttachments.Span;
                for (int preserveIndex = 0;
                     preserveIndex < preserves.Length;
                     ++preserveIndex)
                {
                    preserveReferences[referenceBase + preserveIndex] =
                        checked((uint)preserves[preserveIndex]);
                }

                VkAttachmentReference2* depthReference = null;
                if (depthStencilAttachmentIndex >= 0)
                {
                    VkImageLayout layout =
                        subPass.DepthStencilFlags ==
                            ERHISubPassFlags.ReadOnlyDepthStencil
                            ? VkImageLayout.DepthStencilReadOnlyOptimal
                            : VkImageLayout.DepthStencilAttachmentOptimal;
                    depthReferences[subPassIndex] =
                        CreateAttachmentReference(
                            checked((uint)
                                depthStencilAttachmentIndex),
                            layout,
                            plan.GetDepthStencilAttachment()
                                .SubresourceRange.AspectMask
                                .ToVkImageAspectFlags());
                    depthReference = &depthReferences[subPassIndex];
                }

                void* next = null;
                if (depthStencilResolveAttachmentIndex >= 0 &&
                    subPass.ResolvesDepthStencil)
                {
                    RHIDepthStencilAttachmentDescriptor depthStencil =
                        plan.GetDepthStencilAttachment();
                    depthResolveReferences[subPassIndex] =
                        CreateAttachmentReference(
                            checked((uint)
                                depthStencilResolveAttachmentIndex),
                            VkImageLayout
                                .DepthStencilAttachmentOptimal,
                            depthStencil.SubresourceRange.AspectMask
                                .ToVkImageAspectFlags());
                    depthResolveDescriptions[subPassIndex] =
                        new VkSubpassDescriptionDepthStencilResolve
                        {
                            sType = VkStructureType
                                .SubpassDescriptionDepthStencilResolve,
                            depthResolveMode =
                                ConvertResolveMode(
                                    depthStencil.DepthResolveMode),
                            stencilResolveMode =
                                ConvertResolveMode(
                                    depthStencil.StencilResolveMode),
                            pDepthStencilResolveAttachment =
                                &depthResolveReferences[subPassIndex],
                        };
                    next = &depthResolveDescriptions[subPassIndex];
                }

                subpasses[subPassIndex] =
                    new VkSubpassDescription2
                    {
                        sType = VkStructureType.SubpassDescription2,
                        pNext = next,
                        pipelineBindPoint =
                            VkPipelineBindPoint.Graphics,
                        inputAttachmentCount =
                            checked((uint)inputs.Length),
                        pInputAttachments =
                            inputs.Length == 0
                                ? null
                                : &inputReferences[referenceBase],
                        colorAttachmentCount =
                            checked((uint)colors.Length),
                        pColorAttachments =
                            colors.Length == 0
                                ? null
                                : &colorReferences[referenceBase],
                        pResolveAttachments =
                            hasResolve
                                ? &resolveReferences[referenceBase]
                                : null,
                        pDepthStencilAttachment = depthReference,
                        preserveAttachmentCount =
                            checked((uint)preserves.Length),
                        pPreserveAttachments =
                            preserves.Length == 0
                                ? null
                                : &preserveReferences[referenceBase],
                    };
            }
        }
        private static VkImageLayout GetColorReferenceLayout(
            VulkanRenderPass2Description description,
            int subPassIndex,
            int logicalAttachment,
            bool inputReference)
        {
            ReadOnlySpan<VulkanRenderPass2SubPassDescription> subPasses =
                description.SubPasses.Span;
            byte bit = checked((byte)(1 << logicalAttachment));
            if ((subPasses[subPassIndex].OrdinaryReadWriteMask &
                 bit) != 0)
            {
                return VkImageLayout.General;
            }
            if ((subPasses[subPassIndex].SampledFeedbackMask &
                 bit) != 0)
            {
                return VkImageLayout
                    .AttachmentFeedbackLoopOptimalEXT;
            }
            return inputReference
                ? VkImageLayout.ShaderReadOnlyOptimal
                : VkImageLayout.ColorAttachmentOptimal;
        }

        private static VkAttachmentReference2
            CreateAttachmentReference(
                uint attachment,
                VkImageLayout layout,
                VkImageAspectFlags aspects) =>
            new()
            {
                sType = VkStructureType.AttachmentReference2,
                attachment = attachment,
                layout = layout,
                aspectMask = aspects,
            };

        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) => mode switch
        {
            EResolveMode.None => VkResolveModeFlags.None,
            EResolveMode.Sample0 => VkResolveModeFlags.SampleZero,
            EResolveMode.Min => VkResolveModeFlags.Min,
            EResolveMode.Max => VkResolveModeFlags.Max,
            _ => VkResolveModeFlags.Average,
        };
    }



    internal sealed class VulkanRenderPass2Plan
    {
        internal VkRenderPass NativeRenderPass { get; }
        internal int[] ColorResolveAttachmentIndices { get; }
        internal int DepthStencilAttachmentIndex { get; }
        internal int DepthStencilResolveAttachmentIndex { get; }
        internal int AttachmentCount { get; }
        internal bool UsesKhrEntryPoints { get; }

        internal VulkanRenderPass2Plan(
            VkRenderPass nativeRenderPass,
            int[] colorResolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex,
            int attachmentCount,
            bool usesKhrEntryPoints)
        {
            NativeRenderPass = nativeRenderPass;
            ColorResolveAttachmentIndices =
                colorResolveAttachmentIndices;
            DepthStencilAttachmentIndex =
                depthStencilAttachmentIndex;
            DepthStencilResolveAttachmentIndex =
                depthStencilResolveAttachmentIndex;
            AttachmentCount = attachmentCount;
            UsesKhrEntryPoints = usesKhrEntryPoints;
        }
    }

    internal static unsafe class VulkanRenderPass2Lowering
    {
        private const uint AttachmentUnused = uint.MaxValue;

        internal static VulkanRenderPass2Plan Create(
            VkDevice device,
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            bool useKhrEntryPoints)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(lowering);
            VulkanRenderPass2Description description =
                VulkanRenderPass2Description.Compile(plan, lowering);

            int[] resolveAttachmentIndices =
                new int[plan.ColorAttachmentCount];
            Array.Fill(resolveAttachmentIndices, -1);
            int attachmentCount = plan.ColorAttachmentCount;
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                if (color.ResolveTarget != null)
                {
                    resolveAttachmentIndices[colorIndex] =
                        attachmentCount++;
                }
            }

            int depthStencilAttachmentIndex = -1;
            int depthStencilResolveAttachmentIndex = -1;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilAttachmentIndex = attachmentCount++;
                if (plan.GetDepthStencilAttachment().ResolveTarget != null)
                {
                    depthStencilResolveAttachmentIndex =
                        attachmentCount++;
                }
            }

            VkAttachmentDescription2* attachments =
                Allocate<VkAttachmentDescription2>(attachmentCount);
            VkSubpassDescription2* subpasses =
                Allocate<VkSubpassDescription2>(plan.SubPassCount);
            VkSubpassDependency2* dependencies =
                Allocate<VkSubpassDependency2>(
                    checked(plan.SubPassCount * plan.SubPassCount));
            VkAttachmentReference2* inputReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* colorReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* resolveReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            uint* preserveReferences =
                Allocate<uint>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* depthReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkAttachmentReference2* depthResolveReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkSubpassDescriptionDepthStencilResolve*
                depthResolveDescriptions =
                    Allocate<VkSubpassDescriptionDepthStencilResolve>(
                        plan.SubPassCount);

            try
            {
                PopulateAttachmentDescriptions(
                    plan,
                    lowering,
                    attachments,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                VulkanRenderPass2DescriptionLowering.PopulateSubpasses(
                    plan,
                    description,
                    subpasses,
                    inputReferences,
                    colorReferences,
                    resolveReferences,
                    preserveReferences,
                    depthReferences,
                    depthResolveReferences,
                    depthResolveDescriptions,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                int dependencyCount = PopulateDependencies(
                    plan,
                    description,
                    dependencies);

                VkRenderPassCreateInfo2 createInfo = new()
                {
                    sType = VkStructureType.RenderPassCreateInfo2,
                    attachmentCount = checked((uint)attachmentCount),
                    pAttachments = attachments,
                    subpassCount = checked((uint)plan.SubPassCount),
                    pSubpasses = subpasses,
                    dependencyCount =
                        checked((uint)dependencyCount),
                    pDependencies = dependencies,
                };
                VkRenderPass renderPass;
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateRenderPass2(
                        device,
                        &createInfo,
                        null,
                        &renderPass,
                        useKhrEntryPoints));
                return new VulkanRenderPass2Plan(
                    renderPass,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex,
                    attachmentCount,
                    useKhrEntryPoints);
            }
            finally
            {
                NativeMemory.Free(attachments);
                NativeMemory.Free(subpasses);
                NativeMemory.Free(dependencies);
                NativeMemory.Free(inputReferences);
                NativeMemory.Free(colorReferences);
                NativeMemory.Free(resolveReferences);
                NativeMemory.Free(preserveReferences);
                NativeMemory.Free(depthReferences);
                NativeMemory.Free(depthResolveReferences);
                NativeMemory.Free(depthResolveDescriptions);
            }
        }

        private static void PopulateAttachmentDescriptions(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            VkAttachmentDescription2* attachments,
            int[] resolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                attachments[colorIndex] = new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            color.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            color.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            color.LoadAction),
                    storeOp =
                        color.StoreAction ==
                            ERHIStoreAction.StoreAndResolve ||
                        color.StoreAction == ERHIStoreAction.Store
                            ? VkAttachmentStoreOp.Store
                            : VkAttachmentStoreOp.DontCare,
                    stencilLoadOp = VkAttachmentLoadOp.DontCare,
                    stencilStoreOp = VkAttachmentStoreOp.DontCare,
                    initialLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                };

                int resolveIndex =
                    resolveAttachmentIndices[colorIndex];
                if (resolveIndex >= 0)
                {
                    attachments[resolveIndex] =
                        new VkAttachmentDescription2
                        {
                            sType =
                                VkStructureType.AttachmentDescription2,
                            format =
                                VulkanUtility.ConvertToVkFormat(
                                    color.ResolveTarget!.Descriptor.Format),
                            samples = VkSampleCountFlags.Count1,
                            loadOp = VkAttachmentLoadOp.DontCare,
                            storeOp = VkAttachmentStoreOp.Store,
                            stencilLoadOp =
                                VkAttachmentLoadOp.DontCare,
                            stencilStoreOp =
                                VkAttachmentStoreOp.DontCare,
                            initialLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                            finalLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                        };
                }
            }

            if (depthStencilAttachmentIndex < 0)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            attachments[depthStencilAttachmentIndex] =
                new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            depthStencil.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            depthStencil.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.DepthLoadOp),
                    storeOp =
                        ConvertSourceStoreOp(
                            depthStencil.DepthStoreOp),
                    stencilLoadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.StencilLoadOp),
                    stencilStoreOp =
                        ConvertSourceStoreOp(
                            depthStencil.StencilStoreOp),
                    initialLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                };

            if (depthStencilResolveAttachmentIndex >= 0)
            {
                attachments[depthStencilResolveAttachmentIndex] =
                    new VkAttachmentDescription2
                    {
                        sType =
                            VkStructureType.AttachmentDescription2,
                        format =
                            VulkanUtility.ConvertToVkFormat(
                                depthStencil.ResolveTarget!.Descriptor
                                    .Format),
                        samples = VkSampleCountFlags.Count1,
                        loadOp = VkAttachmentLoadOp.DontCare,
                        storeOp = VkAttachmentStoreOp.Store,
                        stencilLoadOp = VkAttachmentLoadOp.DontCare,
                        stencilStoreOp = VkAttachmentStoreOp.Store,
                        initialLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                        finalLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                    };
            }
        }

        private static int PopulateDependencies(
            RasterPassPlan plan,
            VulkanRenderPass2Description description,
            VkSubpassDependency2* dependencies)
        {
            int dependencyCount = 0;
            ReadOnlySpan<VulkanRenderPass2SubPassDescription> subPasses =
                description.SubPasses.Span;
            for (int sourceIndex = 0;
                 sourceIndex < subPasses.Length;
                 ++sourceIndex)
            {
                VulkanRenderPass2SubPassDescription source =
                    subPasses[sourceIndex];
                for (int destinationIndex = sourceIndex;
                     destinationIndex < subPasses.Length;
                     ++destinationIndex)
                {
                    VulkanRenderPass2SubPassDescription destination =
                        subPasses[destinationIndex];
                    byte destinationColorAccessMask =
                        checked((byte)(
                            destination.InputMask |
                            destination.OutputMask |
                            destination.SampledFeedbackMask));
                    byte colorHazardMask =
                        checked((byte)(
                            source.OutputMask &
                            destinationColorAccessMask));
                    bool selfDependency =
                        sourceIndex == destinationIndex;
                    bool ordinaryReadWrite =
                        selfDependency &&
                        source.OrdinaryReadWriteMask != 0;
                    bool sampledFeedback =
                        (colorHazardMask &
                         destination.SampledFeedbackMask) != 0;
                    bool colorHazard =
                        selfDependency
                            ? ordinaryReadWrite ||
                              sampledFeedback
                            : colorHazardMask != 0;
                    bool depthStencilHazard =
                        !selfDependency &&
                        plan.HasDepthStencilAttachment &&
                        WritesDepthOrStencil(
                            source.DepthStencilFlags);
                    if (!colorHazard && !depthStencilHazard)
                    {
                        continue;
                    }

                    VkPipelineStageFlags sourceStages = 0;
                    VkPipelineStageFlags destinationStages = 0;
                    VkAccessFlags sourceAccess = 0;
                    VkAccessFlags destinationAccess = 0;
                    if (colorHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .ColorAttachmentOutput;
                        sourceAccess |=
                            VkAccessFlags.ColorAttachmentWrite;
                        if ((colorHazardMask &
                             (destination.InputMask |
                              destination.SampledFeedbackMask)) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags.FragmentShader;
                            destinationAccess |=
                                VkAccessFlags.InputAttachmentRead |
                                VkAccessFlags.ShaderRead;
                        }
                        if ((colorHazardMask &
                             destination.OutputMask) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags
                                    .ColorAttachmentOutput;
                            destinationAccess |=
                                VkAccessFlags.ColorAttachmentRead |
                                VkAccessFlags.ColorAttachmentWrite;
                        }
                    }
                    if (depthStencilHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .LateFragmentTests;
                        destinationStages |=
                            VkPipelineStageFlags
                                .EarlyFragmentTests;
                        sourceAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                        destinationAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentRead |
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                    }

                    VkDependencyFlags flags =
                        VkDependencyFlags.ByRegion;
                    if (sampledFeedback)
                    {
                        flags |=
                            VkDependencyFlags.FeedbackLoopEXT;
                    }
                    dependencies[dependencyCount++] =
                        new VkSubpassDependency2
                        {
                            sType =
                                VkStructureType.SubpassDependency2,
                            srcSubpass =
                                checked((uint)sourceIndex),
                            dstSubpass =
                                checked((uint)destinationIndex),
                            srcStageMask = sourceStages,
                            dstStageMask = destinationStages,
                            srcAccessMask = sourceAccess,
                            dstAccessMask = destinationAccess,
                            dependencyFlags = flags,
                        };
                }
            }
            return dependencyCount;
        }

        private static bool WritesDepthOrStencil(
            ERHISubPassFlags flags) =>
            flags != ERHISubPassFlags.ReadOnlyDepthStencil;
        private static VkAttachmentReference2
            CreateAttachmentReference(
                uint attachment,
                VkImageLayout layout,
                VkImageAspectFlags aspects) =>
            new()
            {
                sType = VkStructureType.AttachmentReference2,
                attachment = attachment,
                layout = layout,
                aspectMask = aspects,
            };

        private static VkAttachmentStoreOp ConvertSourceStoreOp(
            ERHIStoreAction action) =>
            action == ERHIStoreAction.Store ||
            action == ERHIStoreAction.StoreAndResolve
                ? VkAttachmentStoreOp.Store
                : VkAttachmentStoreOp.DontCare;

        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) => mode switch
        {
            EResolveMode.None => VkResolveModeFlags.None,
            EResolveMode.Sample0 => VkResolveModeFlags.SampleZero,
            EResolveMode.Min => VkResolveModeFlags.Min,
            EResolveMode.Max => VkResolveModeFlags.Max,
            _ => VkResolveModeFlags.Average,
        };

        private static T* Allocate<T>(int count)
            where T : unmanaged =>
            count == 0
                ? null
                : (T*)NativeMemory.AllocZeroed(
                    checked((nuint)count),
                    (nuint)sizeof(T));
    }

    internal static class VulkanRasterAspectExtensions
    {
        internal static VkImageAspectFlags ToVkImageAspectFlags(
            this ERHITextureAspectMask aspects)
        {
            VkImageAspectFlags result = 0;
            if ((aspects & ERHITextureAspectMask.Color) != 0)
            {
                result |= VkImageAspectFlags.Color;
            }
            if ((aspects & ERHITextureAspectMask.Depth) != 0)
            {
                result |= VkImageAspectFlags.Depth;
            }
            if ((aspects & ERHITextureAspectMask.Stencil) != 0)
            {
                result |= VkImageAspectFlags.Stencil;
            }
            return result;
        }
    }



    internal unsafe sealed class VulkanRasterShaderModuleSet :
        IDisposable
    {
        private readonly VulkanDevice m_Device;
        private readonly VkShaderModule[] m_Modules;
        private readonly VkShaderStageFlags[] m_Stages;
        private readonly IntPtr[] m_EntryNames;
        private bool m_Disposed;

        private VulkanRasterShaderModuleSet(
            VulkanDevice device,
            int stageCount)
        {
            m_Device = device;
            m_Modules = new VkShaderModule[stageCount];
            m_Stages = new VkShaderStageFlags[stageCount];
            m_EntryNames = new IntPtr[stageCount];
        }

        internal int StageCount => m_Modules.Length;

        internal static VulkanRasterShaderModuleSet Create(
            VulkanDevice device,
            in RHIRasterPipelineDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            int stageCount = 0;
            if (descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                ++stageCount;
            }
            if (descriptor.PrimitiveAssembler.MeshletAssembler.HasValue)
            {
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .Value.TaskFunction != null)
                {
                    ++stageCount;
                }
                ++stageCount;
            }
            if (descriptor.FragmentFunction != null)
            {
                ++stageCount;
            }
            if (stageCount == 0)
            {
                throw new ArgumentException(
                    "A Vulkan raster pipeline requires at least one shader stage.",
                    nameof(descriptor));
            }

            VulkanRasterShaderModuleSet result =
                new(device, stageCount);
            try
            {
                int index = 0;
                if (descriptor.PrimitiveAssembler.VertexAssembler
                    .HasValue)
                {
                    result.Add(
                        index++,
                        descriptor.PrimitiveAssembler.VertexAssembler
                            .Value.VertexFunction);
                }
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .HasValue)
                {
                    RHIMeshletAssemblerDescriptor meshlet =
                        descriptor.PrimitiveAssembler.MeshletAssembler
                            .Value;
                    if (meshlet.TaskFunction != null)
                    {
                        result.Add(index++, meshlet.TaskFunction);
                    }
                    if (meshlet.MeshFunction == null)
                    {
                        throw new ArgumentException(
                            "Vulkan mesh pipeline requires a mesh shader function.",
                            nameof(descriptor));
                    }
                    result.Add(index++, meshlet.MeshFunction);
                }
                if (descriptor.FragmentFunction != null)
                {
                    result.Add(index, descriptor.FragmentFunction);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        internal void Populate(
            VkPipelineShaderStageCreateInfo* stages)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }
            for (int index = 0; index < m_Modules.Length; ++index)
            {
                stages[index] =
                    new VkPipelineShaderStageCreateInfo
                    {
                        sType = VkStructureType
                            .PipelineShaderStageCreateInfo,
                        stage = m_Stages[index],
                        module = m_Modules[index],
                        pName = (byte*)m_EntryNames[index],
                    };
            }
        }

        private void Add(int index, RHIFunction function)
        {
            ArgumentNullException.ThrowIfNull(function);
            if (function.IsDisposed)
            {
                throw new ObjectDisposedException(
                    function.GetType().FullName);
            }
            VulkanFunction vulkanFunction =
                function as VulkanFunction ??
                throw new ArgumentException(
                    "Vulkan raster pipelines require Vulkan shader functions.",
                    nameof(function));
            if (!ReferenceEquals(vulkanFunction.VulkanDevice, m_Device))
            {
                throw new ArgumentException(
                    "Vulkan shader function belongs to another device.",
                    nameof(function));
            }
            RHIFunctionDescriptor descriptor = function.Descriptor;
            if (descriptor.PayloadKind !=
                ERHIShaderPayloadKind.SpirV)
            {
                throw new ArgumentException(
                    "Vulkan raster shaders require SPIR-V payloads.",
                    nameof(function));
            }
            ReadOnlySpan<byte> bytecode = vulkanFunction.Bytecode;
            if (bytecode.IsEmpty)
            {
                throw new ArgumentException(
                    "Vulkan raster shader bytecode is empty.",
                    nameof(function));
            }

            VkShaderModule module = default;
            fixed (byte* bytecodePointer = bytecode)
            {
                VkShaderModuleCreateInfo createInfo = new()
                {
                    sType = VkStructureType.ShaderModuleCreateInfo,
                    codeSize = (nuint)bytecode.Length,
                    pCode = (uint*)bytecodePointer,
                };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateShaderModule(
                        m_Device.NativeDevice,
                        &createInfo,
                        null,
                        &module));
            }
            m_Modules[index] = module;
            m_Stages[index] =
                VulkanUtility.ConvertToVkShaderStageBit(
                    descriptor.Type);
            m_EntryNames[index] =
                Marshal.StringToCoTaskMemUTF8(
                    descriptor.EntryName ??
                    throw new ArgumentException(
                        "Vulkan shader entry name is null.",
                        nameof(function)));
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            for (int index = m_Modules.Length - 1;
                 index >= 0;
                 --index)
            {
                if (m_Modules[index].Handle != 0)
                {
                    VulkanNative.vkDestroyShaderModule(
                        m_Device.NativeDevice,
                        m_Modules[index],
                        null);
                    m_Modules[index] = default;
                }
                if (m_EntryNames[index] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(
                        m_EntryNames[index]);
                    m_EntryNames[index] = IntPtr.Zero;
                }
            }
            m_Disposed = true;
        }
    }



    internal interface IVulkanRasterNativePipeline
    {
        VkPipeline NativePipeline { get; }
        VkPipelineLayout EffectiveNativePipelineLayout { get; }
        VulkanPrivateRasterBindingPlan PrivateBindingPlan { get; }
        VulkanPrivateRasterDescriptorLayout? PrivateDescriptorLayout
        {
            get;
        }
        bool HasNativePipeline { get; }
    }

    internal unsafe sealed class VulkanRasterNativeVariant :
        IVulkanRasterNativePipeline,
        IDisposable
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VkPipelineLayout EffectiveNativePipelineLayout =>
            m_EffectiveNativePipelineLayout;
        public VulkanPrivateRasterBindingPlan PrivateBindingPlan =>
            m_PrivateBindingPlan;
        public VulkanPrivateRasterDescriptorLayout?
            PrivateDescriptorLayout => m_PrivateDescriptorLayout;
        public bool HasNativePipeline =>
            m_NativePipeline.Handle != 0;

        private readonly VkDevice m_NativeDevice;
        private VkPipeline m_NativePipeline;
        private VkPipelineLayout m_EffectiveNativePipelineLayout;
        private readonly bool m_OwnsEffectiveNativePipelineLayout;
        private readonly VulkanPrivateRasterBindingPlan
            m_PrivateBindingPlan;
        private VulkanPrivateRasterDescriptorLayout?
            m_PrivateDescriptorLayout;
        private bool m_Disposed;

        internal VulkanRasterNativeVariant(
            VkDevice nativeDevice,
            VkPipeline nativePipeline,
            VkPipelineLayout effectiveNativePipelineLayout,
            bool ownsEffectiveNativePipelineLayout,
            in VulkanPrivateRasterBindingPlan privateBindingPlan,
            VulkanPrivateRasterDescriptorLayout?
                privateDescriptorLayout)
        {
            if (nativeDevice.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan device is required.",
                    nameof(nativeDevice));
            }
            if (nativePipeline.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan raster pipeline is required.",
                    nameof(nativePipeline));
            }

            m_NativeDevice = nativeDevice;
            m_NativePipeline = nativePipeline;
            m_EffectiveNativePipelineLayout =
                effectiveNativePipelineLayout;
            m_OwnsEffectiveNativePipelineLayout =
                ownsEffectiveNativePipelineLayout;
            m_PrivateBindingPlan = privateBindingPlan;
            m_PrivateDescriptorLayout = privateDescriptorLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (m_NativePipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(
                    m_NativeDevice,
                    m_NativePipeline,
                    null);
                m_NativePipeline = default;
            }
            if (m_OwnsEffectiveNativePipelineLayout &&
                m_EffectiveNativePipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    m_NativeDevice,
                    m_EffectiveNativePipelineLayout,
                    null);
                m_EffectiveNativePipelineLayout = default;
            }
            m_PrivateDescriptorLayout?.Dispose();
            m_PrivateDescriptorLayout = null;
            m_Disposed = true;
        }
    }



    internal static class VulkanRasterStrategyDiagnostics
    {
        private static readonly AsyncLocal<EVulkanRasterPassForcedStrategy>
            s_ForcedStrategy = new();

        internal static EVulkanRasterPassForcedStrategy ForcedStrategy =>
            s_ForcedStrategy.Value;

        internal static IDisposable Push(
            EVulkanRasterPassForcedStrategy strategy)
        {
            if (strategy == EVulkanRasterPassForcedStrategy.Auto)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(strategy),
                    "A diagnostic scope must force a concrete Vulkan raster strategy.");
            }

            EVulkanRasterPassForcedStrategy previous =
                s_ForcedStrategy.Value;
            s_ForcedStrategy.Value = strategy;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly EVulkanRasterPassForcedStrategy m_Previous;
            private bool m_Disposed;

            internal Scope(
                EVulkanRasterPassForcedStrategy previous)
            {
                m_Previous = previous;
            }

            public void Dispose()
            {
                if (m_Disposed)
                {
                    return;
                }

                s_ForcedStrategy.Value = m_Previous;
                m_Disposed = true;
            }
        }
    }

internal readonly struct VulkanSampledFeedbackAttachmentFact
    {
        internal VkImage Image { get; }
        internal RHITextureSubresourceRange Range { get; }
        internal byte AttachmentMask { get; }

        internal VulkanSampledFeedbackAttachmentFact(
            VkImage image,
            in RHITextureSubresourceRange range,
            byte attachmentMask)
        {
            Image = image;
            Range = range;
            AttachmentMask = attachmentMask;
        }
    }

    internal readonly struct VulkanSampledImageDescriptorFact
    {
        internal bool IsBound { get; }
        internal VkImage Image { get; }
        internal VkImageView ImageView { get; }
        internal RHITextureSubresourceRange Range { get; }

        internal VulkanSampledImageDescriptorFact(
            VkImage image,
            VkImageView imageView,
            in RHITextureSubresourceRange range)
        {
            IsBound = true;
            Image = image;
            ImageView = imageView;
            Range = range;
        }
    }



    internal enum EVulkanSampledFeedbackRangeRelation : byte
    {
        DifferentImage = 0,
        Disjoint = 1,
        Exact = 2,
        PartialOverlap = 3,
    }

    internal static class VulkanSampledFeedbackRangeUtility
    {
        internal static EVulkanSampledFeedbackRangeRelation Classify(
            VkImage sampledImage,
            in RHITextureSubresourceRange sampledRange,
            VkImage attachmentImage,
            in RHITextureSubresourceRange attachmentRange)
        {
            if (sampledImage.Handle != attachmentImage.Handle)
            {
                return EVulkanSampledFeedbackRangeRelation.DifferentImage;
            }

            if (!RangesOverlap(in sampledRange, in attachmentRange))
            {
                return EVulkanSampledFeedbackRangeRelation.Disjoint;
            }

            return RangesEqual(in sampledRange, in attachmentRange)
                ? EVulkanSampledFeedbackRangeRelation.Exact
                : EVulkanSampledFeedbackRangeRelation.PartialOverlap;
        }

        private static bool RangesEqual(
            in RHITextureSubresourceRange left,
            in RHITextureSubresourceRange right) =>
            left.AspectMask == right.AspectMask &&
            left.BaseMipLevel == right.BaseMipLevel &&
            left.MipLevelCount == right.MipLevelCount &&
            left.BaseArrayLayer == right.BaseArrayLayer &&
            left.ArrayLayerCount == right.ArrayLayerCount;

        private static bool RangesOverlap(
            in RHITextureSubresourceRange left,
            in RHITextureSubresourceRange right) =>
            (left.AspectMask & right.AspectMask) != 0 &&
            IntervalsOverlap(
                left.BaseMipLevel,
                left.MipLevelCount,
                right.BaseMipLevel,
                right.MipLevelCount) &&
            IntervalsOverlap(
                left.BaseArrayLayer,
                left.ArrayLayerCount,
                right.BaseArrayLayer,
                right.ArrayLayerCount);

        private static bool IntervalsOverlap(
            uint leftStart,
            uint leftCount,
            uint rightStart,
            uint rightCount) =>
            (ulong)leftStart + leftCount > rightStart &&
            (ulong)rightStart + rightCount > leftStart;
    }

    internal static class VulkanRasterCapabilityUtility
    {
        internal static bool HasSampledFeedbackLoweringRoute(
            bool dynamicRendering,
            bool renderPass2) =>
            dynamicRendering || renderPass2;

        internal static bool HasFramebufferLocalReadLoweringRoute(
            bool dynamicRendering,
            bool dynamicRenderingLocalRead,
            bool renderPass2) =>
            (dynamicRendering && dynamicRenderingLocalRead) ||
            renderPass2;
    }
    internal static class VulkanRasterPipelineFlagUtility
    {
        internal static VkPipelineCreateFlags Get(
            in RHIAttachmentInterfaceSignature signature) =>
            (signature.SampledFeedbackMask |
             signature.RasterOrderedReadWriteMask) != 0
                ? VkPipelineCreateFlags.ColorAttachmentFeedbackLoopEXT
                : 0;
    }

    internal static class VulkanRasterFeedbackLoopUtility
    {
        internal static bool TryCreateAttachmentInfo(
            EVulkanRasterAttachmentScopeLayout scopeLayout,
            out VkAttachmentFeedbackLoopInfoEXT info)
        {
            if (scopeLayout !=
                EVulkanRasterAttachmentScopeLayout.GeneralStorage)
            {
                info = default;
                return false;
            }

            info = new VkAttachmentFeedbackLoopInfoEXT
            {
                sType = VkStructureType.AttachmentFeedbackLoopInfoEXT,
                feedbackLoopEnable = true,
            };
            return true;
        }
    }

    internal static class VulkanDepthStencilBarrierUtility
    {
        internal static ERHITextureAspectMask GetNativeAspectMask(
            bool supportsSeparateDepthStencilLayouts,
            ERHIPixelFormat format,
            ERHITextureAspectMask declaredAspects)
        {
            if (supportsSeparateDepthStencilLayouts)
            {
                return declaredAspects;
            }

            VkImageAspectFlags nativeAspects =
                VulkanUtility.GetVkImageAspect(format);
            bool combined =
                (nativeAspects &
                 (VkImageAspectFlags.Depth |
                  VkImageAspectFlags.Stencil)) ==
                (VkImageAspectFlags.Depth |
                 VkImageAspectFlags.Stencil);
            if (!combined ||
                (declaredAspects &
                 (ERHITextureAspectMask.Depth |
                  ERHITextureAspectMask.Stencil)) == 0)
            {
                return declaredAspects;
            }

            return declaredAspects |
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
        }
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
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;

            if (plan.SubPassCount > 1)
            {
                ClearRasterPassState();
                throw new NotSupportedException(
                    "The selected Vulkan dynamic-rendering strategy cannot lower multiple ordered subpasses.");
            }

            m_PassDescriptor = plan.DescriptorSnapshot;
            m_HasIssuedDraw = false;
            try
            {
#if DEBUG
                PushDebugGroup(descriptor.Name);
#endif
                if (descriptor.Timestamp.HasValue)
                {
                    WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
                }
                BeginRenderingIfNeeded();
            }
            catch
            {
                ClearRasterPassState();
                throw;
            }
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

        public override void NextSubPass()
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

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanRasterPipeline vkPipeline = VulkanEncoderGuards.RequireRasterPipeline(pipeline);
            VulkanNative.vkCmdBindPipeline(vkCmdBuf.NativeCommandBuffer, VkPipelineBindPoint.Graphics, vkPipeline.NativePipeline);
        }

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            VulkanRasterPipeline pipeline =
                VulkanEncoderGuards.RequireCachedRasterPipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before "
                    + "binding an binding table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(VulkanRasterPipeline));
            }
            VulkanBindingTable table =
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

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDraw(vkCmdBuf.NativeCommandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDrawIndexed(vkCmdBuf.NativeCommandBuffer, indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawIndexedIndirect(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, offset, drawCount, 20);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanNative.vkCmdDrawMeshTasksEXT(vkCmdBuf.NativeCommandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            BeginRenderingIfNeeded();
            m_HasIssuedDraw = true;
            VulkanCommandBuffer vkCmdBuf = VulkanEncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            VulkanBuffer vkArgs = VulkanEncoderGuards.RequireBuffer(argsBuffer);
            VulkanNative.vkCmdDrawMeshTasksIndirectEXT(vkCmdBuf.NativeCommandBuffer, vkArgs.NativeBuffer, argsOffset, 1, 0);
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            VulkanEncoderGuards.RequireDevice(m_CommandBuffer).Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan raster ExecuteIndirectCommandBuffer");
            throw new NotSupportedException(
                "Vulkan raster ExecuteIndirectCommandBuffer is unavailable.");
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The raster encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Raster);
            RasterPassPlan plan = RequireActiveRasterPass();
            if (m_CurrentSubPassIndex != plan.SubPassCount - 1)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' ended at subpass {m_CurrentSubPassIndex}, " +
                    $"but {plan.SubPassCount} subpasses were declared.");
            }

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
            commandBuffer.MarkEncoderEndFromEncoder();
            ClearRasterPassState();
        }

        protected override void Release() { }
    }

internal unsafe sealed class VulkanRasterSubpassEncoder :
        VulkanRasterEncoder
    {
        private const uint AttachmentUnused = uint.MaxValue;

        private readonly VulkanCommandBuffer m_VulkanCommandBuffer;
        private RHIRasterPassDescriptor m_PassDescriptor;
        private RasterPassPlan? m_Plan;
        private VulkanRasterPassLowering? m_Lowering;
        private VulkanRenderPass2Plan? m_RenderPass2Plan;
        private VkImageView[]? m_ColorViews;
        private VkImageView[]? m_ColorResolveViews;
        private VkImageView m_DepthView;
        private VkImageView m_DepthResolveView;
        private VkRenderingAttachmentInfo* m_ColorAttachmentInfos;
        private VkRenderingAttachmentInfo* m_DepthAttachmentInfo;
        private VkRenderingAttachmentInfo* m_StencilAttachmentInfo;
        private VulkanSampledFeedbackAttachmentFact[]?
            m_SampledFeedbackAttachments;
        private WeakReference<VulkanBindingTable>?[]?
            m_BoundFeedbackTables;
        private ulong[]? m_BoundFeedbackRevisions;
        private byte[]? m_BoundFeedbackMasks;
        private IVulkanRasterNativePipeline? m_ActiveNativePipeline;
        private bool m_RenderingActive;

        internal VulkanRasterSubpassEncoder(
            VulkanCommandBuffer commandBuffer)
            : base(commandBuffer)
        {
            m_VulkanCommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;

            VulkanDevice device = GetDevice();
            m_Plan = plan;
            m_PassDescriptor = plan.DescriptorSnapshot;
            m_ActiveNativePipeline = null;
            try
            {
                VulkanRasterCapabilities capabilities =
                    device.RasterCapabilities;
                m_Lowering = VulkanRasterPassLowering.Compile(
                    plan,
                    in capabilities,
                    VulkanRasterStrategyDiagnostics.ForcedStrategy);
                InitializeSampledFeedbackState(
                    plan,
                    m_Lowering,
                    device);
                ValidateRasterOrderedAttachmentStorageFormats(
                    plan,
                    m_Lowering,
                    device);

#if DEBUG
                PushDebugGroup(plan.Name);
#endif
                if (m_PassDescriptor.Timestamp.HasValue)
                {
                    WriteTimestamp(
                        m_PassDescriptor.Timestamp.Value.BeginIndex);
                }

                CreateAttachmentViews(plan);
                TransitionAttachmentScopeLayouts(plan, m_Lowering);
                if (m_Lowering.UsesRenderPass2)
                {
                    BeginRenderPass2(plan, m_Lowering);
                }
                else
                {
                    BeginDynamicRendering(plan, m_Lowering);
                    if (m_Lowering.UsesDynamicRenderingLocalRead)
                    {
                        ApplyDynamicAttachmentMapping(0);
                    }
                }
                m_RenderingActive = true;
            }
            catch
            {
                AbortPassState();
                ClearRasterPassState();
                throw;
            }
        }

        internal void AbortPassState()
        {
            m_RenderingActive = false;
            m_CachedPipeline = null;
            m_ActiveNativePipeline = null;
            m_Plan = null;
            m_Lowering = null;
            m_RenderPass2Plan = null;
            m_ColorViews = null;
            m_ColorResolveViews = null;
            m_DepthView = default;
            m_DepthResolveView = default;
            m_ColorAttachmentInfos = null;
            m_DepthAttachmentInfo = null;
            m_StencilAttachmentInfo = null;
            m_SampledFeedbackAttachments = null;
            m_BoundFeedbackTables = null;
            m_BoundFeedbackRevisions = null;
            m_BoundFeedbackMasks = null;
            m_PassDescriptor = default;
            m_CurrentSubPassIndex = 0;
        }

        public override void NextSubPass()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            int sourceSubPassIndex = m_CurrentSubPassIndex;
            int destinationSubPassIndex = sourceSubPassIndex + 1;
            if (destinationSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            VulkanRasterPassLowering lowering =
                RequireActiveLowering();
            if (destinationSubPassIndex != sourceSubPassIndex + 1)
            {
                throw new InvalidOperationException(
                    "Vulkan subpasses must advance exactly once in order.");
            }

            if (lowering.UsesRenderPass2)
            {
                VkSubpassBeginInfo beginInfo = new()
                {
                    sType = VkStructureType.SubpassBeginInfo,
                    contents = VkSubpassContents.Inline,
                };
                VkSubpassEndInfo endInfo = new()
                {
                    sType = VkStructureType.SubpassEndInfo,
                };
                VulkanNative.vkCmdNextSubpass2(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    &beginInfo,
                    &endInfo,
                    GetDevice().UseRenderPass2KhrCommands);
            }
            else
            {
                EmitDynamicPhaseBarrier(sourceSubPassIndex);
                if (lowering.UsesDynamicRenderingLocalRead)
                {
                    ApplyDynamicAttachmentMapping(
                        destinationSubPassIndex);
                }
            }
            m_CurrentSubPassIndex = destinationSubPassIndex;
            m_PipelineSubPassIndex = -1;
            m_ActiveNativePipeline = null;
        }

        public override void SetPipeline(
            RHIRasterPipeline pipeline)
        {
            if (pipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new ArgumentException(
                    "Vulkan raster encoding requires a Vulkan pipeline.",
                    nameof(pipeline));
            }
            IVulkanRasterNativePipeline nativePipeline = publicPipeline;
            if (RequireActiveLowering().UsesRenderPass2)
            {
                VulkanRasterNativeVariant variant =
                    publicPipeline.CreateCompatibleVariant(
                        m_RenderPass2Plan?.NativeRenderPass ??
                            throw new InvalidOperationException(
                                "The Vulkan RenderPass2 plan is not active."),
                        checked((uint)m_CurrentSubPassIndex));
                m_VulkanCommandBuffer
                    .RegisterTransientRasterPipeline(variant);
                nativePipeline = variant;
            }

            if (!nativePipeline.HasNativePipeline)
            {
                throw new NotSupportedException(
                    "This Vulkan raster pipeline has no native dynamic " +
                    "variant; bind it inside a compatible RenderPass2 pass.");
            }
            ClearBoundSampledFeedbackTables();
            m_CachedPipeline = publicPipeline;
            m_ActiveNativePipeline = nativePipeline;
            VulkanNative.vkCmdBindPipeline(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                nativePipeline.NativePipeline);
            BindPrivateAttachmentSet(nativePipeline);
        }

        public override void SetBindingTable(
            RHIBindingTable resourceTable,
            in uint tableIndex)
        {
            IVulkanRasterNativePipeline nativePipeline =
                m_ActiveNativePipeline
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before " +
                    "binding an binding table.");
            if (m_CachedPipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new InvalidOperationException(
                    "The public Vulkan raster pipeline is unavailable.");
            }
            VulkanBindingTable table =
                publicPipeline.VulkanPipelineLayout.ResolveReadyTable(
                    resourceTable,
                    tableIndex);
            VkDescriptorSet descriptorSet =
                table.NativeDescriptorSet;
            byte requiredMask =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex].SampledInputMask;
            if (requiredMask != 0)
            {
                VulkanSampledFeedbackAttachmentFact[] attachments =
                    m_SampledFeedbackAttachments
                    ?? throw new InvalidOperationException(
                        "The Vulkan sampled-feedback attachment facts " +
                        "are unavailable.");
                VulkanDescriptorSetLease clone =
                    table.CloneForSampledFeedback(
                        attachments,
                        out byte matchedMask,
                        out ulong descriptorRevision);
                using VulkanDescriptorSetLeaseTransaction transaction =
                    new(
                        in clone,
                        value =>
                            GetDevice().DescriptorPoolAllocator.Free(
                                in value));
                transaction.Commit(
                    m_VulkanCommandBuffer
                        .RegisterTransientDescriptorSetLease);
                descriptorSet = clone.Set;
                RecordSampledFeedbackTable(
                    tableIndex,
                    table,
                    descriptorRevision,
                    checked((byte)(matchedMask & requiredMask)));
            }
            else
            {
                ClearBoundSampledFeedbackTable(tableIndex);
            }
            VulkanNative.vkCmdBindDescriptorSets(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                nativePipeline.EffectiveNativePipelineLayout,
                tableIndex,
                1,
                &descriptorSet,
                0,
                null);
        }

        public override void SetPushConstants(
            IntPtr data,
            in uint size,
            in uint offset = 0)
        {
            IVulkanRasterNativePipeline nativePipeline =
                m_ActiveNativePipeline
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before " +
                    "writing push constants.");
            if (m_CachedPipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new InvalidOperationException(
                    "The public Vulkan raster pipeline is unavailable.");
            }
            if (offset + size >
                publicPipeline.VulkanPipelineLayout.PushConstantSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    "The push-constant range exceeds the pipeline layout.");
            }
            VulkanNative.vkCmdPushConstants(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                nativePipeline.EffectiveNativePipelineLayout,
                VkShaderStageFlags.All,
                offset,
                size,
                data.ToPointer());
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            throw new InvalidOperationException(
                "External Vulkan resource and queue barriers must be " +
                "recorded outside an active raster pass. Ordered subpass " +
                "dependencies are emitted by the private raster planner.");
        }

        public override void Barriers(
            ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length != 0)
            {
                throw new InvalidOperationException(
                    "External Vulkan resource and queue barriers must be " +
                    "recorded outside an active raster pass.");
            }
        }

        public override void WriteTimestamp(in uint index)
        {
            if (!m_PassDescriptor.Timestamp.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Timestamp.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The timestamp query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdWriteTimestamp(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.AllGraphics,
                query.NativeQueryPool,
                index);
        }

        public override void BeginOcclusion(in uint index)
        {
            if (!m_PassDescriptor.Occlusion.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Occlusion.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The occlusion query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdBeginQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                VkQueryControlFlags.Precise);
        }

        public override void EndOcclusion(in uint index)
        {
            if (!m_PassDescriptor.Occlusion.HasValue)
            {
                return;
            }
            VulkanQuery query =
                (VulkanQuery)m_PassDescriptor.Occlusion.Value.Query;
            VulkanNative.vkCmdEndQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index);
        }

        public override void BeginStatistics(in uint index)
        {
            if (!m_PassDescriptor.Statistics.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Statistics.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The statistics query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdBeginQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                0);
        }

        public override void EndStatistics(in uint index)
        {
            if (!m_PassDescriptor.Statistics.HasValue)
            {
                return;
            }
            VulkanQuery query =
                (VulkanQuery)m_PassDescriptor.Statistics.Value.Query;
            VulkanNative.vkCmdEndQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index);
        }

        public override void Draw(
            in uint vertexCount,
            in uint instanceCount,
            in uint firstVertex,
            in uint firstInstance)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDraw(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                vertexCount,
                instanceCount,
                firstVertex,
                firstInstance);
        }

        public override void DrawIndexed(
            in uint indexCount,
            in uint instanceCount,
            in uint firstIndex,
            in uint baseVertex,
            in uint firstInstance)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDrawIndexed(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                indexCount,
                instanceCount,
                firstIndex,
                checked((int)baseVertex),
                firstInstance);
        }

        public override void DrawIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawIndirect(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                offset,
                drawCount,
                20);
        }

        public override void DrawIndexedIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawIndexedIndirect(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                offset,
                drawCount,
                20);
        }

        public override void DispatchMesh(
            in uint groupCountX,
            in uint groupCountY,
            in uint groupCountZ)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDrawMeshTasksEXT(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                groupCountX,
                groupCountY,
                groupCountZ);
        }

        public override void DispatchMeshIndirect(
            RHIBuffer argsBuffer,
            in uint argsOffset)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawMeshTasksIndirectEXT(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                argsOffset,
                1,
                0);
        }

        public override void ExecuteIndirectCommandBuffer(
            RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            GetDevice().Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan raster ExecuteIndirectCommandBuffer");
            throw new NotSupportedException(
                "Vulkan raster ExecuteIndirectCommandBuffer is unavailable.");
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The raster encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Raster);
            RasterPassPlan plan = RequireActiveRasterPass();
            if (m_CurrentSubPassIndex != plan.SubPassCount - 1)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' ended at subpass {m_CurrentSubPassIndex}, " +
                    $"but {plan.SubPassCount} subpasses were declared.");
            }

            if (!m_RenderingActive)
            {
                throw new InvalidOperationException(
                    "The Vulkan raster pass is not active.");
            }
            VulkanRasterPassLowering lowering =
                RequireActiveLowering();
            if (lowering.UsesRenderPass2)
            {
                VkSubpassEndInfo endInfo = new()
                {
                    sType = VkStructureType.SubpassEndInfo,
                };
                VulkanNative.vkCmdEndRenderPass2(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    &endInfo,
                    GetDevice().UseRenderPass2KhrCommands);
            }
            else
            {
                VulkanNative.vkCmdEndRendering(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    GetDevice().UseDynamicRenderingKhrCommands);
            }
            RestoreCanonicalAttachmentLayouts();
            m_RenderingActive = false;

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(
                    m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            m_ActiveNativePipeline = null;
            m_Plan = null;
            m_Lowering = null;
            m_RenderPass2Plan = null;
            m_ColorViews = null;
            m_ColorResolveViews = null;
            m_DepthView = default;
            m_DepthResolveView = default;
            m_ColorAttachmentInfos = null;
            m_DepthAttachmentInfo = null;
            m_StencilAttachmentInfo = null;
            m_SampledFeedbackAttachments = null;
            m_BoundFeedbackTables = null;
            m_BoundFeedbackRevisions = null;
            m_BoundFeedbackMasks = null;
            m_PassDescriptor = default;
            commandBuffer.MarkEncoderEndFromEncoder();
            ClearRasterPassState();
        }

        private void CreateAttachmentViews(RasterPassPlan plan)
        {
            m_ColorViews =
                new VkImageView[plan.ColorAttachmentCount];
            m_ColorResolveViews =
                new VkImageView[plan.ColorAttachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(colorIndex);
                m_ColorViews[colorIndex] = CreateImageView(
                    attachment.RenderTarget,
                    attachment.SubresourceRange);
                if (attachment.ResolveTarget != null)
                {
                    m_ColorResolveViews[colorIndex] =
                        CreateImageView(
                            attachment.ResolveTarget,
                            attachment.ResolveSubresourceRange);
                }
            }

            if (plan.HasDepthStencilAttachment)
            {
                RHIDepthStencilAttachmentDescriptor depth =
                    plan.GetDepthStencilAttachment();
                m_DepthView = CreateImageView(
                    depth.RenderTarget,
                    depth.SubresourceRange);
                if (depth.ResolveTarget != null)
                {
                    m_DepthResolveView = CreateImageView(
                        depth.ResolveTarget,
                        depth.ResolveSubresourceRange);
                }
            }
        }

        private VkImageView CreateImageView(
            RHITexture texture,
            in RHITextureSubresourceRange range)
        {
            VulkanTexture vulkanTexture =
                texture as VulkanTexture
                ?? throw new ArgumentException(
                    "Raster attachments must belong to Vulkan.",
                    nameof(texture));
            VulkanDevice device = GetDevice();
            if (!ReferenceEquals(vulkanTexture.VulkanDevice, device))
            {
                throw new ArgumentException(
                    "Raster attachments must belong to the encoder device.",
                    nameof(texture));
            }
            VkImageViewCreateInfo createInfo = new()
            {
                sType = VkStructureType.ImageViewCreateInfo,
                image = vulkanTexture.NativeImage,
                viewType =
                    VulkanUtility.ConvertToVkImageViewType(
                        vulkanTexture.Descriptor.Dimension),
                format =
                    VulkanUtility.ConvertToVkFormat(
                        vulkanTexture.Descriptor.Format),
                subresourceRange =
                    new VkImageSubresourceRange
                    {
                        aspectMask =
                            range.AspectMask.ToVkImageAspectFlags(),
                        baseMipLevel = range.BaseMipLevel,
                        levelCount = range.MipLevelCount,
                        baseArrayLayer = range.BaseArrayLayer,
                        layerCount = range.ArrayLayerCount,
                    },
            };
            VkImageView view = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateImageView(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &view));
            m_VulkanCommandBuffer.RegisterTransientImageView(view);
            return view;
        }

        private void BeginDynamicRendering(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            VulkanDevice device = GetDevice();
            int colorCount = plan.ColorAttachmentCount;
            m_ColorAttachmentInfos =
                (VkRenderingAttachmentInfo*)NativeMemory.AllocZeroed(
                    (nuint)Math.Max(1, colorCount),
                    (nuint)sizeof(VkRenderingAttachmentInfo));
            m_VulkanCommandBuffer.RegisterTransientAllocation(
                m_ColorAttachmentInfos);
            VkAttachmentFeedbackLoopInfoEXT* feedbackLoopInfos =
                stackalloc VkAttachmentFeedbackLoopInfoEXT[
                    Math.Max(1, colorCount)];
            for (int colorIndex = 0;
                 colorIndex < colorCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(colorIndex);
                EVulkanRasterAttachmentScopeLayout scopeLayout =
                    lowering.AttachmentScopeLayouts.Span[colorIndex];

                VkResolveModeFlags colorResolveMode =
                    attachment.ResolveTarget == null
                        ? VkResolveModeFlags.None
                        : IsIntegerColorFormat(
                            attachment.RenderTarget.Descriptor.Format)
                            ? VkResolveModeFlags.SampleZero
                            : VkResolveModeFlags.Average;
                VkClearValue clearValue = default;
                clearValue.color.float32[0] =
                    attachment.ClearValue.x;
                clearValue.color.float32[1] =
                    attachment.ClearValue.y;
                clearValue.color.float32[2] =
                    attachment.ClearValue.z;
                clearValue.color.float32[3] =
                    attachment.ClearValue.w;
                bool hasOrderedFeedbackLoop =
                    VulkanRasterFeedbackLoopUtility.TryCreateAttachmentInfo(
                        scopeLayout,
                        out feedbackLoopInfos[colorIndex]);
                m_ColorAttachmentInfos[colorIndex] =
                    new VkRenderingAttachmentInfo
                    {
                        sType =
                            VkStructureType.RenderingAttachmentInfo,
                        pNext =
                            hasOrderedFeedbackLoop
                                ? &feedbackLoopInfos[colorIndex]
                                : null,
                        imageView = m_ColorViews![colorIndex],
                        imageLayout =
                            ConvertScopeLayout(scopeLayout),
                        resolveMode = colorResolveMode,
                        resolveImageView =
                            colorResolveMode ==
                                VkResolveModeFlags.None
                                ? default
                                : m_ColorResolveViews![colorIndex],
                        resolveImageLayout =
                            VkImageLayout.ColorAttachmentOptimal,
                        loadOp =
                            VulkanUtility.ConvertToVkLoadOp(
                                attachment.LoadAction),
                        storeOp =
                            ConvertRasterStoreOp(
                                attachment.StoreAction),
                        clearValue = clearValue,
                    };
            }

            VkRenderingAttachmentInfo* depthInfo = null;
            VkRenderingAttachmentInfo* stencilInfo = null;
            if (plan.HasDepthStencilAttachment)
            {
                RHIDepthStencilAttachmentDescriptor depthStencil =
                    plan.GetDepthStencilAttachment();
                bool hasDepth =
                    (plan.DepthStencilAspects &
                     ERHITextureAspectMask.Depth) != 0;
                bool hasStencil =
                    (plan.DepthStencilAspects &
                     ERHITextureAspectMask.Stencil) != 0;
                VkResolveModeFlags depthResolveMode = hasDepth
                    ? ConvertResolveMode(
                        depthStencil.DepthResolveMode)
                    : VkResolveModeFlags.None;
                VkResolveModeFlags stencilResolveMode = hasStencil
                    ? ConvertResolveMode(
                        depthStencil.StencilResolveMode)
                    : VkResolveModeFlags.None;
                ValidateDepthStencilResolveModes(
                    device,
                    depthStencil.ResolveTarget != null,
                    depthResolveMode,
                    stencilResolveMode);
                GetDepthStencilRenderingLayouts(
                    device,
                    lowering,
                    in depthStencil,
                    hasDepth,
                    hasStencil,
                    out VkImageLayout depthLayout,
                    out VkImageLayout stencilLayout);

                if (hasDepth)
                {
                    m_DepthAttachmentInfo =
                        (VkRenderingAttachmentInfo*)
                            NativeMemory.AllocZeroed(
                                1,
                                (nuint)sizeof(
                                    VkRenderingAttachmentInfo));
                    m_VulkanCommandBuffer
                        .RegisterTransientAllocation(
                            m_DepthAttachmentInfo);
                    m_DepthAttachmentInfo[0] =
                        new VkRenderingAttachmentInfo
                        {
                            sType =
                                VkStructureType
                                    .RenderingAttachmentInfo,
                            imageView = m_DepthView,
                            imageLayout = depthLayout,
                            resolveMode = depthResolveMode,
                            resolveImageView =
                                depthResolveMode ==
                                    VkResolveModeFlags.None
                                    ? default
                                    : m_DepthResolveView,
                            resolveImageLayout =
                                VkImageLayout
                                    .DepthStencilAttachmentOptimal,
                            loadOp =
                                VulkanUtility.ConvertToVkLoadOp(
                                    depthStencil.DepthLoadOp),
                            storeOp =
                                ConvertRasterStoreOp(
                                    depthStencil.DepthStoreOp),
                            clearValue =
                                new VkClearValue(
                                    depthStencil.DepthClearValue,
                                    checked((uint)
                                        depthStencil
                                            .StencilClearValue)),
                        };
                    depthInfo = m_DepthAttachmentInfo;
                }
                if (hasStencil)
                {
                    m_StencilAttachmentInfo =
                        (VkRenderingAttachmentInfo*)
                            NativeMemory.AllocZeroed(
                                1,
                                (nuint)sizeof(
                                    VkRenderingAttachmentInfo));
                    m_VulkanCommandBuffer
                        .RegisterTransientAllocation(
                            m_StencilAttachmentInfo);
                    m_StencilAttachmentInfo[0] =
                        new VkRenderingAttachmentInfo
                        {
                            sType =
                                VkStructureType
                                    .RenderingAttachmentInfo,
                            imageView = m_DepthView,
                            imageLayout = stencilLayout,
                            resolveMode = stencilResolveMode,
                            resolveImageView =
                                stencilResolveMode ==
                                    VkResolveModeFlags.None
                                    ? default
                                    : m_DepthResolveView,
                            resolveImageLayout =
                                VkImageLayout
                                    .DepthStencilAttachmentOptimal,
                            loadOp =
                                VulkanUtility.ConvertToVkLoadOp(
                                    depthStencil.StencilLoadOp),
                            storeOp =
                                ConvertRasterStoreOp(
                                    depthStencil.StencilStoreOp),
                            clearValue =
                                new VkClearValue(
                                    depthStencil.DepthClearValue,
                                    checked((uint)
                                        depthStencil
                                            .StencilClearValue)),
                        };
                    stencilInfo = m_StencilAttachmentInfo;
                }
            }

            VkRenderingInfo renderingInfo = new()
            {
                sType = VkStructureType.RenderingInfo,
                renderArea = new VkRect2D
                {
                    extent = new VkExtent2D
                    {
                        width = plan.Width,
                        height = plan.Height,
                    },
                },
                layerCount = plan.ArrayLength,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachments =
                    colorCount == 0
                        ? null
                        : m_ColorAttachmentInfos,
                pDepthAttachment = depthInfo,
                pStencilAttachment = stencilInfo,
            };
            VulkanNative.vkCmdBeginRendering(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &renderingInfo,
                device.UseDynamicRenderingKhrCommands);
            for (int colorIndex = 0;
                 colorIndex < colorCount;
                 ++colorIndex)
            {
                m_ColorAttachmentInfos[colorIndex].pNext = null;
            }
        }
        private void BeginRenderPass2(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            VulkanDevice device = GetDevice();
            m_RenderPass2Plan = VulkanRenderPass2Lowering.Create(
                device.NativeDevice,
                plan,
                lowering,
                device.UseRenderPass2KhrCommands);
            m_VulkanCommandBuffer.RegisterTransientRenderPass(
                m_RenderPass2Plan.NativeRenderPass);

            int attachmentCount =
                m_RenderPass2Plan.AttachmentCount;
            VkImageView* attachments =
                stackalloc VkImageView[attachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                attachments[colorIndex] =
                    m_ColorViews![colorIndex];
                int resolveIndex =
                    m_RenderPass2Plan
                        .ColorResolveAttachmentIndices[colorIndex];
                if (resolveIndex >= 0)
                {
                    attachments[resolveIndex] =
                        m_ColorResolveViews![colorIndex];
                }
            }
            if (m_RenderPass2Plan.DepthStencilAttachmentIndex >= 0)
            {
                attachments[
                    m_RenderPass2Plan
                        .DepthStencilAttachmentIndex] =
                    m_DepthView;
            }
            if (m_RenderPass2Plan
                    .DepthStencilResolveAttachmentIndex >= 0)
            {
                attachments[
                    m_RenderPass2Plan
                        .DepthStencilResolveAttachmentIndex] =
                    m_DepthResolveView;
            }

            VkFramebufferCreateInfo framebufferInfo = new()
            {
                sType = VkStructureType.FramebufferCreateInfo,
                renderPass = m_RenderPass2Plan.NativeRenderPass,
                attachmentCount =
                    checked((uint)attachmentCount),
                pAttachments = attachments,
                width = plan.Width,
                height = plan.Height,
                layers = plan.ArrayLength,
            };
            VkFramebuffer framebuffer = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateFramebuffer(
                    device.NativeDevice,
                    &framebufferInfo,
                    null,
                    &framebuffer));
            m_VulkanCommandBuffer.RegisterTransientFramebuffer(
                framebuffer);

            VkClearValue* clearValues =
                stackalloc VkClearValue[attachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                clearValues[colorIndex].color.float32[0] =
                    color.ClearValue.x;
                clearValues[colorIndex].color.float32[1] =
                    color.ClearValue.y;
                clearValues[colorIndex].color.float32[2] =
                    color.ClearValue.z;
                clearValues[colorIndex].color.float32[3] =
                    color.ClearValue.w;
            }
            if (m_RenderPass2Plan.DepthStencilAttachmentIndex >= 0)
            {
                RHIDepthStencilAttachmentDescriptor depth =
                    plan.GetDepthStencilAttachment();
                clearValues[
                    m_RenderPass2Plan
                        .DepthStencilAttachmentIndex] =
                    new VkClearValue(
                        depth.DepthClearValue,
                        checked((uint)depth.StencilClearValue));
            }

            VkRenderPassBeginInfo beginInfo = new()
            {
                sType = VkStructureType.RenderPassBeginInfo,
                renderPass = m_RenderPass2Plan.NativeRenderPass,
                framebuffer = framebuffer,
                renderArea = new VkRect2D
                {
                    extent = new VkExtent2D
                    {
                        width = plan.Width,
                        height = plan.Height,
                    },
                },
                clearValueCount =
                    checked((uint)attachmentCount),
                pClearValues = clearValues,
            };
            VkSubpassBeginInfo subPassBegin = new()
            {
                sType = VkStructureType.SubpassBeginInfo,
                contents = VkSubpassContents.Inline,
            };
            VulkanNative.vkCmdBeginRenderPass2(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &beginInfo,
                &subPassBegin,
                device.UseRenderPass2KhrCommands);
        }

        private void ApplyDynamicAttachmentMapping(
            int subPassIndex)
        {
            VulkanRasterSubPassLowering subPass =
                RequireActiveLowering().SubPasses.Span[
                    subPassIndex];
            int colorCount =
                m_Plan?.ColorAttachmentCount ??
                throw new InvalidOperationException(
                    "The Vulkan raster plan is not active.");
            uint* outputLocations =
                stackalloc uint[Math.Max(colorCount, 1)];
            uint* inputIndices =
                stackalloc uint[Math.Max(colorCount, 1)];
            VulkanDynamicRenderingAttachmentMappingPlan.Populate(
                in subPass,
                colorCount,
                outputLocations,
                inputIndices);

            VkRenderingAttachmentLocationInfo locationInfo = new()
            {
                sType =
                    VkStructureType
                        .RenderingAttachmentLocationInfo,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachmentLocations = outputLocations,
            };
            VkRenderingInputAttachmentIndexInfo inputInfo = new()
            {
                sType =
                    VkStructureType
                        .RenderingInputAttachmentIndexInfo,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachmentInputIndices = inputIndices,
            };
            VulkanDevice device = GetDevice();
            VulkanNative.vkCmdSetRenderingAttachmentLocations(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &locationInfo,
                device.UseDynamicRenderingLocalReadKhrCommands);
            VulkanNative.vkCmdSetRenderingInputAttachmentIndices(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &inputInfo,
                device.UseDynamicRenderingLocalReadKhrCommands);
        }

        private void EmitDynamicPhaseBarrier(
            int sourceSubPassIndex)
        {
            VulkanRasterPhaseBarrierPlan barrier =
                RequireActiveLowering().PhaseBarriers.Span[
                    sourceSubPassIndex];
            if (barrier.Kind ==
                EVulkanRasterPhaseBarrierKind.None)
            {
                return;
            }
            VkMemoryBarrier nativeBarrier = new()
            {
                sType = VkStructureType.MemoryBarrier,
                srcAccessMask =
                    VkAccessFlags.ColorAttachmentWrite |
                    VkAccessFlags.ShaderWrite,
                dstAccessMask =
                    VkAccessFlags.InputAttachmentRead |
                    VkAccessFlags.ShaderRead |
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite,
            };
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.ColorAttachmentOutput |
                    VkPipelineStageFlags.FragmentShader,
                VkPipelineStageFlags.FragmentShader |
                    VkPipelineStageFlags.ColorAttachmentOutput,
                VkDependencyFlags.ByRegion,
                1,
                &nativeBarrier,
                0,
                null,
                0,
                null);
        }

        private void TransitionAttachmentScopeLayouts(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                VulkanTexture texture =
                    (VulkanTexture)color.RenderTarget;
                VkImageLayout destinationLayout =
                    ConvertScopeLayout(
                        lowering.AttachmentScopeLayouts
                            .Span[colorIndex]);
                if (lowering.UsesRenderPass2 &&
                    destinationLayout is
                        VkImageLayout.RenderingLocalRead or
                        VkImageLayout.AttachmentFeedbackLoopOptimalEXT)
                {
                    destinationLayout =
                        VkImageLayout.ColorAttachmentOptimal;
                }
                TransitionExactColorAttachment(
                    texture,
                    in color.SubresourceRange,
                    destinationLayout);

                if (color.ResolveTarget != null)
                {
                    VulkanTexture colorResolveTexture =
                        (VulkanTexture)color.ResolveTarget;
                    TransitionExactAttachmentLayout(
                        colorResolveTexture,
                        in color.ResolveSubresourceRange,
                        VkImageLayout.TransferDstOptimal,
                        VkImageLayout.ColorAttachmentOptimal,
                        depthStencil: false,
                        "A color resolve attachment");
                }
            }

            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }

            VulkanDevice device = GetDevice();
            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            VulkanTexture depthTexture =
                (VulkanTexture)depthStencil.RenderTarget;
            bool hasDepth =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0;
            bool hasStencil =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0;
            bool depthReadOnly =
                hasDepth &&
                IsDepthAspectReadOnly(
                    lowering,
                    in depthStencil);
            bool stencilReadOnly =
                hasStencil &&
                IsStencilAspectReadOnly(
                    lowering,
                    in depthStencil);
            GetDepthStencilRenderingLayouts(
                device,
                lowering,
                in depthStencil,
                hasDepth,
                hasStencil,
                out VkImageLayout depthLayout,
                out VkImageLayout stencilLayout);

            bool combinedBarrier =
                RequiresCombinedDepthStencilBarrier(
                    device,
                    depthTexture);
            if (combinedBarrier)
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        depthTexture,
                        in depthStencil.SubresourceRange);
                bool allSelectedAspectsReadOnly =
                    (!hasDepth || depthReadOnly) &&
                    (!hasStencil || stencilReadOnly);
                VkImageLayout assertedLayout =
                    allSelectedAspectsReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal;
                VkImageLayout destinationLayout =
                    hasDepth ? depthLayout : stencilLayout;
                TransitionExactAttachmentLayout(
                    depthTexture,
                    in combinedRange,
                    assertedLayout,
                    destinationLayout,
                    depthStencil: true,
                    "A combined depth/stencil attachment");
            }
            else
            {
                if (hasDepth)
                {
                    RHITextureSubresourceRange depthRange =
                        SelectAspect(
                            in depthStencil.SubresourceRange,
                            ERHITextureAspectMask.Depth);
                    TransitionExactAttachmentLayout(
                        depthTexture,
                        in depthRange,
                        depthReadOnly
                            ? VkImageLayout
                                .DepthStencilReadOnlyOptimal
                            : VkImageLayout
                                .DepthStencilAttachmentOptimal,
                        depthLayout,
                        depthStencil: true,
                        "A depth attachment");
                }
                if (hasStencil)
                {
                    RHITextureSubresourceRange stencilRange =
                        SelectAspect(
                            in depthStencil.SubresourceRange,
                            ERHITextureAspectMask.Stencil);
                    TransitionExactAttachmentLayout(
                        depthTexture,
                        in stencilRange,
                        stencilReadOnly
                            ? VkImageLayout
                                .DepthStencilReadOnlyOptimal
                            : VkImageLayout
                                .DepthStencilAttachmentOptimal,
                        stencilLayout,
                        depthStencil: true,
                        "A stencil attachment");
                }
            }

            if (depthStencil.ResolveTarget == null)
            {
                return;
            }
            VulkanTexture resolveTexture =
                (VulkanTexture)depthStencil.ResolveTarget;
            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    resolveTexture))
            {
                RHITextureSubresourceRange combinedResolveRange =
                    ExpandToNativeDepthStencilAspects(
                        resolveTexture,
                        in depthStencil.ResolveSubresourceRange);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in combinedResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A combined depth/stencil resolve attachment");
                return;
            }
            if (hasDepth)
            {
                RHITextureSubresourceRange depthResolveRange =
                    SelectAspect(
                        in depthStencil.ResolveSubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in depthResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A depth resolve attachment");
            }
            if (hasStencil)
            {
                RHITextureSubresourceRange stencilResolveRange =
                    SelectAspect(
                        in depthStencil.ResolveSubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in stencilResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A stencil resolve attachment");
            }
        }

        private void TransitionExactColorAttachment(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout destinationLayout)
        {
            const VkImageLayout CanonicalLayout =
                VkImageLayout.ColorAttachmentOptimal;
            m_VulkanCommandBuffer.RequireKnownImageLayout(
                texture,
                in range,
                CanonicalLayout,
                "A color attachment");
            if (destinationLayout == CanonicalLayout)
            {
                return;
            }

            VkImageMemoryBarrier barrier = new()
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask =
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite,
                dstAccessMask =
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite |
                    VkAccessFlags.InputAttachmentRead |
                    VkAccessFlags.ShaderRead |
                    VkAccessFlags.ShaderWrite,
                oldLayout = CanonicalLayout,
                newLayout = destinationLayout,
                srcQueueFamilyIndex = uint.MaxValue,
                dstQueueFamilyIndex = uint.MaxValue,
                image = texture.NativeImage,
                subresourceRange =
                    ConvertToNativeRange(in range),
            };
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.ColorAttachmentOutput,
                VkPipelineStageFlags.FragmentShader |
                    VkPipelineStageFlags.ColorAttachmentOutput,
                VkDependencyFlags.ByRegion,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            m_VulkanCommandBuffer.SetKnownImageLayout(
                texture,
                in range,
                destinationLayout);
        }

        private void RestoreCanonicalAttachmentLayouts()
        {
            RasterPassPlan? plan = m_Plan;
            VulkanRasterPassLowering? lowering = m_Lowering;
            if (plan == null || lowering == null)
            {
                return;
            }
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                VkImageLayout sourceLayout =
                    ConvertScopeLayout(
                        lowering.AttachmentScopeLayouts
                            .Span[colorIndex]);
                if (lowering.UsesRenderPass2 &&
                    sourceLayout is
                        VkImageLayout.RenderingLocalRead or
                        VkImageLayout.AttachmentFeedbackLoopOptimalEXT)
                {
                    sourceLayout =
                        VkImageLayout.ColorAttachmentOptimal;
                }
                if (sourceLayout ==
                    VkImageLayout.ColorAttachmentOptimal)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                VulkanTexture texture =
                    (VulkanTexture)color.RenderTarget;
                m_VulkanCommandBuffer.RequireKnownImageLayout(
                    texture,
                    in color.SubresourceRange,
                    sourceLayout,
                    "A color attachment");
                VkImageMemoryBarrier barrier = new()
                {
                    sType = VkStructureType.ImageMemoryBarrier,
                    srcAccessMask =
                        VkAccessFlags.InputAttachmentRead |
                        VkAccessFlags.ShaderRead |
                        VkAccessFlags.ShaderWrite |
                        VkAccessFlags.ColorAttachmentRead |
                        VkAccessFlags.ColorAttachmentWrite,
                    dstAccessMask =
                        VkAccessFlags.ColorAttachmentRead |
                        VkAccessFlags.ColorAttachmentWrite,
                    oldLayout = sourceLayout,
                    newLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                    srcQueueFamilyIndex = uint.MaxValue,
                    dstQueueFamilyIndex = uint.MaxValue,
                    image = texture.NativeImage,
                    subresourceRange =
                        ConvertToNativeRange(
                            in color.SubresourceRange),
                };
                VulkanNative.vkCmdPipelineBarrier(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    VkPipelineStageFlags.FragmentShader |
                        VkPipelineStageFlags.ColorAttachmentOutput,
                    VkPipelineStageFlags.ColorAttachmentOutput,
                    VkDependencyFlags.ByRegion,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &barrier);
                m_VulkanCommandBuffer.SetKnownImageLayout(
                    texture,
                    in color.SubresourceRange,
                    VkImageLayout.ColorAttachmentOptimal);
            }
            RestoreResolveDestinationLayouts(plan);
            RestoreDepthStencilBoundaryLayouts(plan, lowering);
        }

        private void RestoreResolveDestinationLayouts(
            RasterPassPlan plan)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                if (color.ResolveTarget == null)
                {
                    continue;
                }
                VulkanTexture colorResolveTexture =
                    (VulkanTexture)color.ResolveTarget;
                TransitionExactAttachmentLayout(
                    colorResolveTexture,
                    in color.ResolveSubresourceRange,
                    VkImageLayout.ColorAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: false,
                    "A color resolve attachment");
            }
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            if (descriptor.ResolveTarget == null)
            {
                return;
            }

            VulkanDevice device = GetDevice();
            VulkanTexture resolveTexture =
                (VulkanTexture)descriptor.ResolveTarget;
            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    resolveTexture))
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        resolveTexture,
                        in descriptor.ResolveSubresourceRange);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in combinedRange,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A combined depth/stencil resolve attachment");
                return;
            }
            if ((plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.ResolveSubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in range,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A depth resolve attachment");
            }
            if ((plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.ResolveSubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in range,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A stencil resolve attachment");
            }
        }

        private void RestoreDepthStencilBoundaryLayouts(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            VulkanDevice device = GetDevice();
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            VulkanTexture texture =
                (VulkanTexture)descriptor.RenderTarget;
            bool hasDepth =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0;
            bool hasStencil =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0;
            bool depthReadOnly =
                hasDepth &&
                IsDepthAspectReadOnly(
                    lowering,
                    in descriptor);
            bool stencilReadOnly =
                hasStencil &&
                IsStencilAspectReadOnly(
                    lowering,
                    in descriptor);
            GetDepthStencilRenderingLayouts(
                device,
                lowering,
                in descriptor,
                hasDepth,
                hasStencil,
                out VkImageLayout depthLayout,
                out VkImageLayout stencilLayout);

            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    texture))
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        texture,
                        in descriptor.SubresourceRange);
                bool allSelectedAspectsReadOnly =
                    (!hasDepth || depthReadOnly) &&
                    (!hasStencil || stencilReadOnly);
                TransitionExactAttachmentLayout(
                    texture,
                    in combinedRange,
                    hasDepth ? depthLayout : stencilLayout,
                    allSelectedAspectsReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A combined depth/stencil attachment");
                return;
            }

            if (hasDepth)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.SubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    texture,
                    in range,
                    depthLayout,
                    depthReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A depth attachment");
            }
            if (hasStencil)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.SubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    texture,
                    in range,
                    stencilLayout,
                    stencilReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A stencil attachment");
            }
        }

        private void TransitionExactAttachmentLayout(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout sourceLayout,
            VkImageLayout destinationLayout,
            bool depthStencil,
            string operation)
        {
            m_VulkanCommandBuffer.RequireKnownImageLayout(
                texture,
                in range,
                sourceLayout,
                operation);
            if (sourceLayout == destinationLayout)
            {
                return;
            }
            VkImageMemoryBarrier barrier = new()
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask = depthStencil
                    ? VkAccessFlags.DepthStencilAttachmentRead |
                      VkAccessFlags.DepthStencilAttachmentWrite
                    : VkAccessFlags.ColorAttachmentRead |
                      VkAccessFlags.ColorAttachmentWrite |
                      VkAccessFlags.TransferWrite,
                dstAccessMask = depthStencil
                    ? VkAccessFlags.DepthStencilAttachmentRead |
                      VkAccessFlags.DepthStencilAttachmentWrite |
                      VkAccessFlags.TransferWrite
                    : VkAccessFlags.ColorAttachmentRead |
                      VkAccessFlags.ColorAttachmentWrite |
                      VkAccessFlags.TransferWrite,
                oldLayout = sourceLayout,
                newLayout = destinationLayout,
                srcQueueFamilyIndex = uint.MaxValue,
                dstQueueFamilyIndex = uint.MaxValue,
                image = texture.NativeImage,
                subresourceRange = ConvertToNativeRange(in range),
            };
            VkPipelineStageFlags stages = depthStencil
                ? VkPipelineStageFlags.EarlyFragmentTests |
                  VkPipelineStageFlags.LateFragmentTests |
                  VkPipelineStageFlags.Transfer
                : VkPipelineStageFlags.ColorAttachmentOutput |
                  VkPipelineStageFlags.Transfer;
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                stages,
                stages,
                VkDependencyFlags.ByRegion,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            m_VulkanCommandBuffer.SetKnownImageLayout(
                texture,
                in range,
                destinationLayout);
        }

        private static bool RequiresCombinedDepthStencilBarrier(
            VulkanDevice device,
            VulkanTexture texture)
        {
            ERHITextureAspectMask expanded =
                VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                    device.SupportsSeparateDepthStencilLayouts,
                    texture.Descriptor.Format,
                    ERHITextureAspectMask.Depth);
            return (expanded &
                    (ERHITextureAspectMask.Depth |
                     ERHITextureAspectMask.Stencil)) ==
                (ERHITextureAspectMask.Depth |
                 ERHITextureAspectMask.Stencil);
        }

        private static RHITextureSubresourceRange
            ExpandToNativeDepthStencilAspects(
                VulkanTexture texture,
                in RHITextureSubresourceRange range)
        {
            ERHITextureAspectMask aspects =
                VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                    supportsSeparateDepthStencilLayouts: false,
                    texture.Descriptor.Format,
                    range.AspectMask);
            return new RHITextureSubresourceRange
            {
                AspectMask = aspects,
                BaseMipLevel = range.BaseMipLevel,
                MipLevelCount = range.MipLevelCount,
                BaseArrayLayer = range.BaseArrayLayer,
                ArrayLayerCount = range.ArrayLayerCount,
            };
        }

        private static RHITextureSubresourceRange SelectAspect(
            in RHITextureSubresourceRange range,
            ERHITextureAspectMask aspect) =>
            new()
            {
                AspectMask = aspect,
                BaseMipLevel = range.BaseMipLevel,
                MipLevelCount = range.MipLevelCount,
                BaseArrayLayer = range.BaseArrayLayer,
                ArrayLayerCount = range.ArrayLayerCount,
            };

        private static bool IsDepthAspectReadOnly(
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor) =>
            descriptor.DepthLoadOp == ERHILoadAction.Load &&
            (lowering.SubPasses.Span[0].DepthStencilFlags &
             ERHISubPassFlags.ReadOnlyDepth) != 0;

        private static bool IsStencilAspectReadOnly(
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor) =>
            descriptor.StencilLoadOp == ERHILoadAction.Load &&
            (lowering.SubPasses.Span[0].DepthStencilFlags &
             ERHISubPassFlags.ReadOnlyStencil) != 0;
        private static VkImageSubresourceRange ConvertToNativeRange(
            in RHITextureSubresourceRange range) =>
            new()
            {
                aspectMask =
                    range.AspectMask.ToVkImageAspectFlags(),
                baseMipLevel = range.BaseMipLevel,
                levelCount = range.MipLevelCount,
                baseArrayLayer = range.BaseArrayLayer,
                layerCount = range.ArrayLayerCount,
            };

        private void BindPrivateAttachmentSet(
            IVulkanRasterNativePipeline pipeline)
        {
            VulkanPrivateRasterBindingPlan bindingPlan =
                pipeline.PrivateBindingPlan;
            if (!bindingPlan.HasPrivateBindings)
            {
                return;
            }
            VulkanPrivateRasterDescriptorLayout descriptorLayout =
                pipeline.PrivateDescriptorLayout
                ?? throw new InvalidOperationException(
                    "The private Vulkan raster descriptor layout is missing.");
            VulkanDevice device = GetDevice();
            VulkanDescriptorSetLease lease =
                device.DescriptorPoolAllocator.Allocate(
                    descriptorLayout.PoolRequirements,
                    descriptorLayout.NativeLayout);
            using VulkanDescriptorSetLeaseTransaction transaction =
                new VulkanDescriptorSetLeaseTransaction(
                    in lease,
                    value =>
                        device.DescriptorPoolAllocator.Free(in value));

            VkWriteDescriptorSet* writes =
                stackalloc VkWriteDescriptorSet[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            VkDescriptorImageInfo* images =
                stackalloc VkDescriptorImageInfo[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            int writeCount = 0;
            VulkanRasterSubPassLowering subPass =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex];
            for (int inputIndex = 0;
                 inputIndex <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++inputIndex)
            {
                if (!bindingPlan.UsesInputAttachmentBinding(
                        inputIndex))
                {
                    continue;
                }
                int logicalAttachment =
                    subPass.GetColorInputLogicalAttachment(
                        inputIndex);
                images[writeCount] =
                    new VkDescriptorImageInfo
                    {
                        imageView =
                            m_ColorViews![logicalAttachment],
                        imageLayout =
                            RequireActiveLowering().UsesRenderPass2
                                ? GetRenderPassInputLayout(
                                    logicalAttachment)
                                : VkImageLayout
                                    .RenderingLocalRead,
                    };
                writes[writeCount] =
                    new VkWriteDescriptorSet
                    {
                        sType =
                            VkStructureType.WriteDescriptorSet,
                        dstSet = lease.Set,
                        dstBinding =
                            bindingPlan.GetInputAttachmentBinding(
                                inputIndex),
                        descriptorCount = 1,
                        descriptorType =
                            VkDescriptorType.InputAttachment,
                        pImageInfo = &images[writeCount],
                    };
                ++writeCount;
            }
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((bindingPlan.RasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                images[writeCount] =
                    new VkDescriptorImageInfo
                    {
                        imageView =
                            m_ColorViews![logicalAttachment],
                        imageLayout = VkImageLayout.General,
                    };
                writes[writeCount] =
                    new VkWriteDescriptorSet
                    {
                        sType =
                            VkStructureType.WriteDescriptorSet,
                        dstSet = lease.Set,
                        dstBinding =
                            bindingPlan.GetRasterOrderedBinding(
                                logicalAttachment),
                        descriptorCount = 1,
                        descriptorType =
                            VkDescriptorType.StorageImage,
                        pImageInfo = &images[writeCount],
                    };
                ++writeCount;
            }

            VulkanNative.vkUpdateDescriptorSets(
                device.NativeDevice,
                checked((uint)writeCount),
                writes,
                0,
                null);
            VkDescriptorSet descriptorSet = lease.Set;
            VulkanNative.vkCmdBindDescriptorSets(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                pipeline.EffectiveNativePipelineLayout,
                bindingPlan.DescriptorSet,
                1,
                &descriptorSet,
                0,
                null);
            transaction.Commit(
                m_VulkanCommandBuffer
                    .RegisterTransientDescriptorSetLease);
        }

        private VkImageLayout GetRenderPassInputLayout(
            int logicalAttachment)
        {
            EVulkanRasterAttachmentScopeLayout scope =
                RequireActiveLowering().AttachmentScopeLayouts.Span[
                    logicalAttachment];
            return scope ==
                EVulkanRasterAttachmentScopeLayout
                    .AttachmentFeedbackLoop
                ? VkImageLayout
                    .AttachmentFeedbackLoopOptimalEXT
                : VkImageLayout.ShaderReadOnlyOptimal;
        }

        private VulkanDevice GetDevice()
        {
            VulkanCommandQueue queue =
                m_VulkanCommandBuffer.CommandQueue as
                    VulkanCommandQueue
                ?? throw new InvalidOperationException(
                    "The Vulkan command buffer has no Vulkan queue.");
            return queue.VulkanDevice;
        }

        private VulkanRasterPassLowering
            RequireActiveLowering() =>
            m_Lowering
            ?? throw new InvalidOperationException(
                "The Vulkan raster pass is not active.");

        private static void
            ValidateRasterOrderedAttachmentStorageFormats(
                RasterPassPlan plan,
                VulkanRasterPassLowering lowering,
                VulkanDevice device)
        {
            byte rasterOrderedMask = 0;
            ReadOnlySpan<VulkanRasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                rasterOrderedMask |=
                    subPasses[subPassIndex].RasterOrderedMask;
            }
            if (rasterOrderedMask == 0)
            {
                return;
            }

            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((rasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(logicalAttachment);
                if (attachment.RenderTarget is not
                    VulkanTexture texture)
                {
                    throw new ArgumentException(
                        "Vulkan RasterOrderedReadWrite attachments must " +
                        "belong to Vulkan.",
                        nameof(plan));
                }
                if ((texture.Descriptor.UsageFlag &
                     ERHITextureUsage.RasterizerOrdered) == 0)
                {
                    throw new ArgumentException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} lacks canonical " +
                        "RasterizerOrdered texture usage.",
                        nameof(plan));
                }
                VkImageUsageFlags nativeUsage =
                    VulkanUtility.ConvertToVkImageUsage(
                        texture.Descriptor.UsageFlag,
                        device.SupportsAttachmentFeedbackLoopLayout);
                if ((nativeUsage & VkImageUsageFlags.Storage) == 0)
                {
                    throw new InvalidOperationException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} was not created with " +
                        "VK_IMAGE_USAGE_STORAGE_BIT.");
                }
                if (texture.Descriptor.SampleCount !=
                    ERHISampleCount.None)
                {
                    throw new NotSupportedException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} is multisampled. The " +
                        "current exact storage-image ABI is single-sample.");
                }

                VkFormat format =
                    VulkanUtility.ConvertToVkFormat(
                        texture.Descriptor.Format);
                VkFormatProperties formatProperties = default;
                VulkanNative.vkGetPhysicalDeviceFormatProperties(
                    device.NativePhysicalDevice,
                    format,
                    &formatProperties);
                if ((formatProperties.optimalTilingFeatures &
                     VkFormatFeatureFlags.StorageImage) == 0)
                {
                    throw new NotSupportedException(
                        $"Vulkan format {format} for " +
                        $"RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} lacks optimal-tiling " +
                        "VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT.");
                }
            }
        }
        private void InitializeSampledFeedbackState(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            VulkanDevice device)
        {
            if (!lowering.UsesSampledFeedback)
            {
                m_SampledFeedbackAttachments = null;
                m_BoundFeedbackTables = null;
                m_BoundFeedbackRevisions = null;
                m_BoundFeedbackMasks = null;
                return;
            }

            byte passFeedbackMask = 0;
            ReadOnlySpan<VulkanRasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                passFeedbackMask |=
                    subPasses[subPassIndex].SampledInputMask;
            }

            int attachmentCount =
                System.Numerics.BitOperations.PopCount(
                    (uint)passFeedbackMask);
            VulkanSampledFeedbackAttachmentFact[] attachments =
                new VulkanSampledFeedbackAttachmentFact[
                    attachmentCount];
            int factIndex = 0;
            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((passFeedbackMask & bit) == 0)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(logicalAttachment);
                if (attachment.RenderTarget is not
                    VulkanTexture texture)
                {
                    throw new ArgumentException(
                        "Vulkan sampled-feedback attachments must " +
                        "belong to Vulkan.",
                        nameof(plan));
                }
                ERHITextureUsage requiredUsage =
                    ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.ShaderResource;
                if ((texture.Descriptor.UsageFlag & requiredUsage) !=
                    requiredUsage)
                {
                    throw new ArgumentException(
                        $"Vulkan SampledFeedback attachment " +
                        $"{logicalAttachment} requires both RenderTarget " +
                        "and ShaderResource texture usage.",
                        nameof(plan));
                }
                RHITextureSubresourceRange normalizedRange =
                    VulkanTextureSubresourceRangeUtility.Normalize(
                        texture.Descriptor,
                        in attachment.SubresourceRange);
                attachments[factIndex++] =
                    new VulkanSampledFeedbackAttachmentFact(
                        texture.NativeImage,
                        in normalizedRange,
                        bit);
            }

            int maximumBoundSets =
                checked((int)device.DescriptorLimits.MaximumBoundSets);
            m_SampledFeedbackAttachments = attachments;
            m_BoundFeedbackTables =
                new WeakReference<VulkanBindingTable>?[
                    maximumBoundSets];
            m_BoundFeedbackRevisions =
                new ulong[maximumBoundSets];
            m_BoundFeedbackMasks =
                new byte[maximumBoundSets];
        }

        private void RecordSampledFeedbackTable(
            uint tableIndex,
            VulkanBindingTable table,
            ulong descriptorRevision,
            byte matchedMask)
        {
            WeakReference<VulkanBindingTable>?[] tables =
                m_BoundFeedbackTables
                ?? throw new InvalidOperationException(
                    "The Vulkan sampled-feedback table state is " +
                    "unavailable.");
            if (tableIndex >= (uint)tables.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableIndex),
                    tableIndex,
                    "The descriptor-set index exceeds the Vulkan " +
                    "device limit.");
            }
            int index = checked((int)tableIndex);
            tables[index] =
                new WeakReference<VulkanBindingTable>(table);
            m_BoundFeedbackRevisions![index] =
                descriptorRevision;
            m_BoundFeedbackMasks![index] = matchedMask;
        }

        private void ClearBoundSampledFeedbackTable(
            uint tableIndex)
        {
            WeakReference<VulkanBindingTable>?[]? tables =
                m_BoundFeedbackTables;
            if (tables == null)
            {
                return;
            }
            if (tableIndex >= (uint)tables.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableIndex),
                    tableIndex,
                    "The descriptor-set index exceeds the Vulkan " +
                    "device limit.");
            }
            int index = checked((int)tableIndex);
            tables[index] = null;
            m_BoundFeedbackRevisions![index] = 0;
            m_BoundFeedbackMasks![index] = 0;
        }

        private void ClearBoundSampledFeedbackTables()
        {
            if (m_BoundFeedbackTables != null)
            {
                Array.Clear(m_BoundFeedbackTables);
                Array.Clear(m_BoundFeedbackRevisions!);
                Array.Clear(m_BoundFeedbackMasks!);
            }
        }

        private void RequireBoundPipeline()
        {
            if (m_ActiveNativePipeline == null)
            {
                throw new InvalidOperationException(
                    "A compatible Vulkan raster pipeline must be " +
                    "bound before drawing.");
            }
            RequireSampledFeedbackBindings();
        }

        private void RequireSampledFeedbackBindings()
        {
            byte requiredMask =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex].SampledInputMask;
            if (requiredMask == 0)
            {
                return;
            }

            WeakReference<VulkanBindingTable>?[] tables =
                m_BoundFeedbackTables
                ?? throw new InvalidOperationException(
                    "The Vulkan sampled-feedback table state is " +
                    "unavailable.");
            byte coveredMask = 0;
            for (int index = 0; index < tables.Length; ++index)
            {
                byte tableMask =
                    checked((byte)(
                        m_BoundFeedbackMasks![index] &
                        requiredMask));
                if (tableMask == 0)
                {
                    continue;
                }
                WeakReference<VulkanBindingTable>? weakTable =
                    tables[index];
                if (weakTable == null ||
                    !weakTable.TryGetTarget(
                        out VulkanBindingTable? table))
                {
                    throw new ObjectDisposedException(
                        $"VulkanBindingTable[{index}]",
                        "A SampledFeedback binding table is no " +
                        "longer alive; rebind a live table.");
                }
                table.EnsureReadyForBinding();
                if (table.DescriptorRevision !=
                    m_BoundFeedbackRevisions![index])
                {
                    throw new InvalidOperationException(
                        $"Vulkan SampledFeedback binding table {index} " +
                        "changed after binding. Rebind it before Draw.");
                }
                coveredMask |= tableMask;
            }
            if ((coveredMask & requiredMask) != requiredMask)
            {
                byte missingMask =
                    checked((byte)(requiredMask & ~coveredMask));
                throw new InvalidOperationException(
                    $"Vulkan SampledFeedback attachments 0x" +
                    $"{missingMask:X2} are not covered by exact sampled-" +
                    "image descriptor bindings in the current subpass.");
            }
        }

        private static VkImageLayout ConvertScopeLayout(
            EVulkanRasterAttachmentScopeLayout layout) =>
            layout switch
            {
                EVulkanRasterAttachmentScopeLayout
                    .ColorAttachment =>
                    VkImageLayout.ColorAttachmentOptimal,
                EVulkanRasterAttachmentScopeLayout
                    .RenderingLocalRead =>
                    VkImageLayout.RenderingLocalRead,
                EVulkanRasterAttachmentScopeLayout
                    .AttachmentFeedbackLoop =>
                    VkImageLayout
                        .AttachmentFeedbackLoopOptimalEXT,
                EVulkanRasterAttachmentScopeLayout
                    .GeneralStorage =>
                    VkImageLayout.General,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    layout,
                    "Unknown Vulkan raster attachment scope."),
            };

        private static VkAttachmentStoreOp ConvertRasterStoreOp(
            ERHIStoreAction action) =>
            action switch
            {
                ERHIStoreAction.Store or
                    ERHIStoreAction.StoreAndResolve =>
                    VkAttachmentStoreOp.Store,
                ERHIStoreAction.Resolve or
                    ERHIStoreAction.DontCare =>
                    VkAttachmentStoreOp.DontCare,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unknown raster attachment store action."),
            };

        private static bool IsIntegerColorFormat(
            ERHIPixelFormat format) =>
            format is
                ERHIPixelFormat.R8_UInt or
                ERHIPixelFormat.R8_SInt or
                ERHIPixelFormat.R16_UInt or
                ERHIPixelFormat.R16_SInt or
                ERHIPixelFormat.R8G8_UInt or
                ERHIPixelFormat.R8G8_SInt or
                ERHIPixelFormat.R32_UInt or
                ERHIPixelFormat.R32_SInt or
                ERHIPixelFormat.R16G16_UInt or
                ERHIPixelFormat.R16G16_SInt or
                ERHIPixelFormat.R8G8B8A8_UInt or
                ERHIPixelFormat.R8G8B8A8_SInt or
                ERHIPixelFormat.R10G10B10A2_UInt or
                ERHIPixelFormat.RG32_UInt or
                ERHIPixelFormat.RG32_SInt or
                ERHIPixelFormat.R16G16B16A16_UInt or
                ERHIPixelFormat.R16G16B16A16_SInt or
                ERHIPixelFormat.R32G32B32A32_UInt or
                ERHIPixelFormat.R32G32B32A32_SInt;

        private static void ValidateDepthStencilResolveModes(
            VulkanDevice device,
            bool hasResolveTarget,
            VkResolveModeFlags depthMode,
            VkResolveModeFlags stencilMode)
        {
            if (!hasResolveTarget)
            {
                if (depthMode != VkResolveModeFlags.None ||
                    stencilMode != VkResolveModeFlags.None)
                {
                    throw new ArgumentException(
                        "Depth/stencil resolve modes require a " +
                        "resolve target.");
                }
                return;
            }
            if (depthMode != VkResolveModeFlags.None &&
                (device.SupportedDepthResolveModes &
                 depthMode) == 0)
            {
                throw new NotSupportedException(
                    $"Vulkan depth resolve mode {depthMode} is " +
                    "not supported by the selected device.");
            }
            if (stencilMode != VkResolveModeFlags.None &&
                (device.SupportedStencilResolveModes &
                 stencilMode) == 0)
            {
                throw new NotSupportedException(
                    $"Vulkan stencil resolve mode {stencilMode} is " +
                    "not supported by the selected device.");
            }
            if (depthMode == stencilMode)
            {
                return;
            }
            bool oneIsNone =
                depthMode == VkResolveModeFlags.None ||
                stencilMode == VkResolveModeFlags.None;
            if (oneIsNone && !device.IndependentResolveNone)
            {
                throw new NotSupportedException(
                    "This Vulkan device cannot independently " +
                    "disable depth or stencil resolve.");
            }
            if (!oneIsNone && !device.IndependentResolve)
            {
                throw new NotSupportedException(
                    "This Vulkan device requires identical depth " +
                    "and stencil resolve modes.");
            }
        }

        private static void GetDepthStencilRenderingLayouts(
            VulkanDevice device,
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor,
            bool hasDepth,
            bool hasStencil,
            out VkImageLayout depthLayout,
            out VkImageLayout stencilLayout)
        {
            ERHISubPassFlags flags =
                lowering.SubPasses.Span[0].DepthStencilFlags;
            bool depthReadOnly =
                hasDepth &&
                descriptor.DepthLoadOp == ERHILoadAction.Load &&
                (flags & ERHISubPassFlags.ReadOnlyDepth) != 0;
            bool stencilReadOnly =
                hasStencil &&
                descriptor.StencilLoadOp == ERHILoadAction.Load &&
                (flags & ERHISubPassFlags.ReadOnlyStencil) != 0;
            if (hasDepth && hasStencil &&
                depthReadOnly != stencilReadOnly &&
                !device.SupportsSeparateDepthStencilLayouts)
            {
                depthReadOnly = false;
                stencilReadOnly = false;
            }
            if (device.SupportsSeparateDepthStencilLayouts &&
                hasDepth && hasStencil &&
                depthReadOnly != stencilReadOnly)
            {
                depthLayout = depthReadOnly
                    ? VkImageLayout.DepthReadOnlyOptimal
                    : VkImageLayout.DepthAttachmentOptimal;
                stencilLayout = stencilReadOnly
                    ? VkImageLayout.StencilReadOnlyOptimal
                    : VkImageLayout.StencilAttachmentOptimal;
                return;
            }
            VkImageLayout combined =
                depthReadOnly || stencilReadOnly
                    ? VkImageLayout
                        .DepthStencilReadOnlyOptimal
                    : VkImageLayout
                        .DepthStencilAttachmentOptimal;
            depthLayout = combined;
            stencilLayout = combined;
        }
        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) =>
            mode switch
            {
                EResolveMode.None => VkResolveModeFlags.None,
                EResolveMode.Sample0 =>
                    VkResolveModeFlags.SampleZero,
                EResolveMode.Min => VkResolveModeFlags.Min,
                EResolveMode.Max => VkResolveModeFlags.Max,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unknown depth/stencil resolve mode."),            };
    }

    #endregion

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

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            VulkanRaytracingPipeline pipeline =
                VulkanEncoderGuards.RequireCachedRaytracingPipeline(m_CachedPipeline)
                ?? throw new InvalidOperationException(
                    "A live Vulkan ray-tracing pipeline must be set before "
                    + "binding an binding table.");
            if (pipeline.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanRaytracingPipeline));
            }
            VulkanBindingTable table =
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

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
            VulkanEncoderGuards.RequireDevice(m_CommandBuffer).Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan ray-tracing ExecuteIndirectCommandBuffer");
            throw new NotSupportedException(
                "Vulkan ray-tracing ExecuteIndirectCommandBuffer is unavailable.");
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The ray-tracing encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.RayTracing);

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            commandBuffer.MarkEncoderEndFromEncoder();
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
