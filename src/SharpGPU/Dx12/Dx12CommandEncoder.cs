using System;
using System.Diagnostics;
using SharpGPU.Collections;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Viewport = SharpGPU.Mathematics.Viewport;

namespace SharpGPU
{
#pragma warning disable CS0414, CA1416
    internal unsafe struct Dx12AttachmentInfo
    {
        public bool bDepthStencil;
        public Dx12DescriptorInfo AttachmentInfo;
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dx12Box
    {
        public uint left;
        public uint top;
        public uint front;
        public uint right;
        public uint bottom;
        public uint back;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vortice.Mathematics.Box(Dx12Box box)
            => new Vortice.Mathematics.Box((int)box.left, (int)box.top, (int)box.front, (int)box.right, (int)box.bottom, (int)box.back);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dx12TextureCopyLocation
    {
        public Vortice.Direct3D12.ID3D12Resource pResource;
        public Vortice.Direct3D12.TextureCopyType Type;
        public Vortice.Direct3D12.PlacedSubresourceFootPrint PlacedFootprint;
        public uint SubresourceIndex;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vortice.Direct3D12.TextureCopyLocation(Dx12TextureCopyLocation location)
        {
            return location.Type == Vortice.Direct3D12.TextureCopyType.PlacedFootPrint
                ? new Vortice.Direct3D12.TextureCopyLocation(location.pResource, location.PlacedFootprint)
                : new Vortice.Direct3D12.TextureCopyLocation(location.pResource, location.SubresourceIndex);
        }
    }

    internal static class Dx12ResourceBarrierUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vortice.Direct3D12.ResourceBarrier InitUAV(Vortice.Direct3D12.ID3D12Resource? resource)
            => Vortice.Direct3D12.ResourceBarrier.BarrierUnorderedAccessView(resource);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vortice.Direct3D12.ResourceBarrier InitAliasing(Vortice.Direct3D12.ID3D12Resource resourceBefore, Vortice.Direct3D12.ID3D12Resource resourceAfter)
            => Vortice.Direct3D12.ResourceBarrier.BarrierAliasing(resourceBefore, resourceAfter);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vortice.Direct3D12.ResourceBarrier InitTransition(Vortice.Direct3D12.ID3D12Resource resource, Vortice.Direct3D12.ResourceStates stateBefore, Vortice.Direct3D12.ResourceStates stateAfter)
            => Vortice.Direct3D12.ResourceBarrier.BarrierTransition(resource, stateBefore, stateAfter, Vortice.Direct3D12.D3D12.ResourceBarrierAllSubResources, Vortice.Direct3D12.ResourceBarrierFlags.None);
    }

    internal static class Dx12EncoderGuards
    {
        internal static Dx12CommandBuffer RequireCommandBuffer(RHICommandBuffer? commandBuffer)
        {
            return commandBuffer as Dx12CommandBuffer
                ?? throw new InvalidOperationException("DX12 encoder operations require a Dx12CommandBuffer.");
        }

        internal static Dx12Query RequireQuery(RHIQuery query)
        {
            return query as Dx12Query
                ?? throw new ArgumentException("DX12 query operations require a Dx12Query.", nameof(query));
        }

        internal static Dx12Buffer RequireBuffer(RHIBuffer buffer)
        {
            return buffer as Dx12Buffer
                ?? throw new ArgumentException("DX12 buffer operations require a Dx12Buffer.", nameof(buffer));
        }

        internal static Dx12Texture RequireTexture(RHITexture texture)
        {
            return texture as Dx12Texture
                ?? throw new ArgumentException("DX12 texture operations require a Dx12Texture.", nameof(texture));
        }

        internal static Dx12ComputePipeline RequireComputePipeline(RHIComputePipeline pipeline)
        {
            return pipeline as Dx12ComputePipeline
                ?? throw new ArgumentException("DX12 compute operations require a Dx12ComputePipeline.", nameof(pipeline));
        }

        internal static Dx12RaytracingPipeline RequireRaytracingPipeline(RHIRaytracingPipeline pipeline)
        {
            return pipeline as Dx12RaytracingPipeline
                ?? throw new ArgumentException("DX12 ray-tracing operations require a Dx12RaytracingPipeline.", nameof(pipeline));
        }

        internal static Dx12RasterPipeline RequireRasterPipeline(RHIRasterPipeline? pipeline)
        {
            return pipeline as Dx12RasterPipeline
                ?? throw new ArgumentException("DX12 raster operations require a Dx12RasterPipeline.", nameof(pipeline));
        }

        internal static Dx12PipelineLayout RequirePipelineLayout(RHIPipelineLayout? pipelineLayout)
        {
            return pipelineLayout as Dx12PipelineLayout
                ?? throw new ArgumentException("DX12 pipeline operations require a Dx12PipelineLayout.", nameof(pipelineLayout));
        }

        internal static Dx12FunctionTable RequireFunctionTable(RHIFunctionTable functionTable)
        {
            return functionTable as Dx12FunctionTable
                ?? throw new ArgumentException("DX12 ray-tracing dispatch requires a Dx12FunctionTable.", nameof(functionTable));
        }

        internal static Dx12TopLevelAccelStruct RequireTopLevelAccelStruct(RHITopLevelAccelStruct accelStruct)
        {
            return accelStruct as Dx12TopLevelAccelStruct
                ?? throw new ArgumentException("DX12 acceleration-structure build requires a Dx12TopLevelAccelStruct.", nameof(accelStruct));
        }

        internal static Dx12BottomLevelAccelStruct RequireBottomLevelAccelStruct(RHIBottomLevelAccelStruct accelStruct)
        {
            return accelStruct as Dx12BottomLevelAccelStruct
                ?? throw new ArgumentException("DX12 acceleration-structure build requires a Dx12BottomLevelAccelStruct.", nameof(accelStruct));
        }

        internal static Dx12PipelineLayout RequireCachedComputePipelineLayout(RHIComputePipeline? cachedPipeline)
        {
            if (cachedPipeline is not Dx12ComputePipeline dx12Pipeline)
            {
                throw new InvalidOperationException("DX12 compute encoder requires a bound Dx12ComputePipeline.");
            }

            return RequirePipelineLayout(dx12Pipeline.Descriptor.PipelineLayout);
        }

        internal static Dx12PipelineLayout RequireCachedRaytracingPipelineLayout(RHIRaytracingPipeline? cachedPipeline)
        {
            if (cachedPipeline is not Dx12RaytracingPipeline dx12Pipeline)
            {
                throw new InvalidOperationException("DX12 ray-tracing encoder requires a bound Dx12RaytracingPipeline.");
            }

            return RequirePipelineLayout(dx12Pipeline.Descriptor.PipelineLayout);
        }

        internal static Dx12PipelineLayout RequireCachedRasterPipelineLayout(RHIRasterPipeline? cachedPipeline)
        {
            if (cachedPipeline is not Dx12RasterPipeline dx12Pipeline)
            {
                throw new InvalidOperationException("DX12 raster encoder requires a bound Dx12RasterPipeline.");
            }

            return RequirePipelineLayout(dx12Pipeline.DescriptorInternal.PipelineLayout);
        }

        internal static Dx12CommandQueue RequireCommandQueue(RHICommandQueue? commandQueue)
        {
            return commandQueue as Dx12CommandQueue
                ?? throw new InvalidOperationException("DX12 operations require a Dx12CommandQueue.");
        }

        internal static Dx12Device RequireDevice(RHICommandBuffer? commandBuffer)
        {
            return RequireCommandQueue(RequireCommandBuffer(commandBuffer).CommandQueue).Dx12Device;
        }
    }

    internal static class Dx12QueryEncoderValidation
    {
        internal static (Dx12Query Query, Dx12CommandBuffer CommandBuffer) RequireTimestampQuery(
            RHICommandBuffer? commandBuffer,
            RHIQuery? queryHeap,
            uint index)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(commandBuffer);
            return RequireQueryHeap(
                dx12CommandBuffer,
                queryHeap,
                "timestamp",
                index,
                ERHIQueryType.Timestamp,
                ERHIQueryType.TimestampTransfer);
        }

        internal static (Dx12Query Query, Dx12CommandBuffer CommandBuffer) RequireOcclusionQuery(
            RHICommandBuffer? commandBuffer,
            RHIQuery? queryHeap,
            uint index)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(commandBuffer);
            return RequireQueryHeap(
                dx12CommandBuffer,
                queryHeap,
                "occlusion",
                index,
                ERHIQueryType.Occlusion);
        }

        internal static (Dx12Query Query, Dx12CommandBuffer CommandBuffer) RequireStatisticsQuery(
            RHICommandBuffer? commandBuffer,
            RHIQuery? queryHeap,
            uint index)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(commandBuffer);
            return RequireQueryHeap(
                dx12CommandBuffer,
                queryHeap,
                "statistics",
                index,
                ERHIQueryType.Statistics);
        }

        private static (Dx12Query Query, Dx12CommandBuffer CommandBuffer) RequireQueryHeap(
            Dx12CommandBuffer commandBuffer,
            RHIQuery? queryHeap,
            string heapName,
            uint index,
            params ERHIQueryType[] expectedTypes)
        {
            if (queryHeap is not Dx12Query dx12Query)
            {
                throw new InvalidOperationException($"Current pass {heapName} query heap is not a Dx12Query.");
            }

            if (dx12Query.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Query.GetType().FullName);
            }

            ERHIQueryType actualType = dx12Query.QueryDescriptor.Type;
            bool typeMatches = false;
            for (int i = 0; i < expectedTypes.Length; ++i)
            {
                if (actualType == expectedTypes[i])
                {
                    typeMatches = true;
                    break;
                }
            }

            if (!typeMatches)
            {
                throw new InvalidOperationException(
                    $"Current pass {heapName} query heap type is {actualType}.");
            }

            if (index >= dx12Query.QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"Query index {index} is out of range for {heapName} heap count {dx12Query.QueryDescriptor.Count}.");
            }

            return (dx12Query, commandBuffer);
        }
    }
    internal static unsafe class Dx12CommandListInteropExtensions
    {
        public static void ResourceBarrier(this Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList, uint barrierCount, Vortice.Direct3D12.ResourceBarrier* barriers)
        {
            if (barrierCount == 0 || barriers == null)
            {
                return;
            }

            Vortice.Direct3D12.ResourceBarrier[] nativeBarriers = new Vortice.Direct3D12.ResourceBarrier[barrierCount];
            for (int i = 0; i < barrierCount; ++i)
            {
                nativeBarriers[i] = barriers[i];
            }

            ((Vortice.Direct3D12.ID3D12GraphicsCommandList)commandList).ResourceBarrier(nativeBarriers);
        }

        public static void CopyTextureRegion(this Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList, Dx12TextureCopyLocation* destination, uint destinationX, uint destinationY, uint destinationZ, Dx12TextureCopyLocation* source, Dx12Box* sourceBox)
        {
            Vortice.Direct3D12.TextureCopyLocation nativeDestination = *destination;
            Vortice.Direct3D12.TextureCopyLocation nativeSource = *source;
            Vortice.Mathematics.Box? nativeBox = sourceBox == null ? null : (Vortice.Mathematics.Box)(*sourceBox);
            ((Vortice.Direct3D12.ID3D12GraphicsCommandList)commandList).CopyTextureRegion(nativeDestination, destinationX, destinationY, destinationZ, nativeSource, nativeBox);
        }
    }

    internal static unsafe class Dx12BarrierEmitter
    {

        public static void EmitBarrier(Dx12CommandBuffer commandBuffer, in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            EmitBarriers(commandBuffer, singleBarrier);
        }

        public static void EmitBarriers(Dx12CommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0)
            {
                return;
            }

            Dx12Device device = Dx12EncoderGuards.RequireDevice(commandBuffer);
            ERHIPipelineType recordingQueue = commandBuffer.CommandQueue.PipelineType;
            for (int i = 0; i < barriers.Length; ++i)
            {
                RHIBarrierUtility.ValidateQueueOwnership(in barriers[i], recordingQueue);
            }

            if (device.EnhancedBarriers.Tier != ERHICapabilityTier.Unavailable)
            {
                EmitEnhancedBarriers(commandBuffer, barriers);
            }
            else if (barriers.Length == 1)
            {
                EmitSingleResourceBarrier(commandBuffer, in barriers[0]);
            }
            else
            {
                EmitResourceBarriers(commandBuffer, barriers);
            }
        }

        internal static void EmitUnorderedAccessOrderingBarrier(
            Dx12CommandBuffer commandBuffer,
            Dx12Texture texture)
        {
            Dx12Device device =
                Dx12EncoderGuards.RequireDevice(commandBuffer);
            if (device.EnhancedBarriers.Tier !=
                ERHICapabilityTier.Unavailable)
            {
                RHIBarrier barrier = RHIBarrier.Texture(
                    texture,
                    RHIBarrierUtility.CreateWholeSubresourceRange(texture),
                    ERHITextureLayout.General,
                    ERHITextureLayout.General,
                    ERHISyncStageMask.Fragment,
                    ERHISyncStageMask.Fragment,
                    ERHIAccessMask.ShaderWrite,
                    ERHIAccessMask.ShaderRead |
                        ERHIAccessMask.ShaderWrite);
                EmitBarrier(commandBuffer, barrier);
                return;
            }

            Vortice.Direct3D12.ResourceBarrier nativeBarrier =
                Dx12ResourceBarrierUtil.InitUAV(texture.NativeResource);
            ((Vortice.Direct3D12.ID3D12GraphicsCommandList)
                commandBuffer.NativeCommandList).ResourceBarrier(
                    nativeBarrier);
        }

        private static void EmitSingleResourceBarrier(
            Dx12CommandBuffer commandBuffer,
            in RHIBarrier barrier)
        {
            Vortice.Direct3D12.ResourceBarrier nativeBarrier;
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                    nativeBarrier =
                        Dx12ResourceBarrierUtil.InitUAV(null);
                    break;

                case ERHIBarrierKind.Buffer:
                {
                    RHIBufferBarrier bufferBarrier =
                        barrier.BufferBarrier;
                    nativeBarrier = Dx12ResourceBarrierUtil.InitTransition(
                        GetBufferResource(
                            commandBuffer,
                            bufferBarrier.Resource,
                            0),
                        ConvertToResourceBufferStates(
                            bufferBarrier.AccessBefore),
                        ConvertToResourceBufferStates(
                            bufferBarrier.AccessAfter));
                    break;
                }

                case ERHIBarrierKind.Texture:
                {
                    RHITextureBarrier textureBarrier =
                        barrier.TextureBarrier;
                    nativeBarrier = Dx12ResourceBarrierUtil.InitTransition(
                        GetTexture(
                            commandBuffer,
                            textureBarrier.Resource,
                            0).NativeResource,
                        ConvertToResourceTextureStates(
                            textureBarrier.LayoutBefore,
                            textureBarrier.AccessBefore),
                        ConvertToResourceTextureStates(
                            textureBarrier.LayoutAfter,
                            textureBarrier.AccessAfter));
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Unsupported barrier kind {barrier.Kind}.");
            }

            ((Vortice.Direct3D12.ID3D12GraphicsCommandList)
                commandBuffer.NativeCommandList).ResourceBarrier(
                    nativeBarrier);
        }

        private static void EmitResourceBarriers(
            Dx12CommandBuffer commandBuffer,
            ReadOnlySpan<RHIBarrier> barriers)
        {
            Vortice.Direct3D12.ResourceBarrier[] nativeBarriers = new Vortice.Direct3D12.ResourceBarrier[barriers.Length];
            int barrierCount = 0;

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref readonly RHIBarrier barrier = ref barriers[i];
                switch (barrier.Kind)
                {
                    case ERHIBarrierKind.Global:
                        // The Resource Barrier API exposes global shader-memory
                        // ordering through the documented null-resource UAV
                        // barrier rather than stage/access-scoped global barriers.
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitUAV(null);
                        break;

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                        Vortice.Direct3D12.ResourceStates stateBefore = ConvertToResourceBufferStates(bufferBarrier.AccessBefore);
                        Vortice.Direct3D12.ResourceStates stateAfter = ConvertToResourceBufferStates(bufferBarrier.AccessAfter);
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitTransition(
                            GetBufferResource(commandBuffer, bufferBarrier.Resource, i),
                            stateBefore,
                            stateAfter);
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                        Vortice.Direct3D12.ResourceStates stateBefore = ConvertToResourceTextureStates(textureBarrier.LayoutBefore, textureBarrier.AccessBefore);
                        Vortice.Direct3D12.ResourceStates stateAfter = ConvertToResourceTextureStates(textureBarrier.LayoutAfter, textureBarrier.AccessAfter);
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitTransition(
                            GetTexture(commandBuffer, textureBarrier.Resource, i).NativeResource,
                            stateBefore,
                            stateAfter);
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported barrier kind {barrier.Kind}.");
                }
            }

            if (barrierCount == 0)
            {
                return;
            }

            fixed (Vortice.Direct3D12.ResourceBarrier* nativeBarriersPtr = nativeBarriers)
            {
                commandBuffer.NativeCommandList.ResourceBarrier((uint)barrierCount, nativeBarriersPtr);
            }
        }

        private static void EmitEnhancedBarriers(Dx12CommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers)
        {
            ERHIPipelineType queuePipeline = commandBuffer.CommandQueue.PipelineType;
            if (barriers.Length == 1)
            {
                EmitSingleEnhancedBarrier(commandBuffer, in barriers[0], queuePipeline);
                return;
            }

            List<Vortice.Direct3D12.GlobalBarrier>? globalBarriers = null;
            List<Vortice.Direct3D12.BufferBarrier>? bufferBarriers = null;
            List<Vortice.Direct3D12.TextureBarrier>? textureBarriers = null;

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref readonly RHIBarrier barrier = ref barriers[i];
                switch (barrier.Kind)
                {
                    case ERHIBarrierKind.Global:
                    {
                        RHIGlobalBarrier globalBarrier = barrier.GlobalBarrier;
                        (globalBarriers ??= new List<Vortice.Direct3D12.GlobalBarrier>(barriers.Length)).Add(new Vortice.Direct3D12.GlobalBarrier(
                            ResolveBarrierSync(globalBarrier.SyncBefore, queuePipeline),
                            ResolveBarrierSync(globalBarrier.SyncAfter, queuePipeline),
                            ConvertToBarrierAccess(globalBarrier.AccessBefore),
                            ConvertToBarrierAccess(globalBarrier.AccessAfter)));
                        break;
                    }

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                        Vortice.Direct3D12.BarrierAccess accessBefore = ConvertToBarrierAccess(bufferBarrier.AccessBefore);
                        Vortice.Direct3D12.BarrierAccess accessAfter = ConvertToBarrierAccess(bufferBarrier.AccessAfter);
                        Vortice.Direct3D12.BarrierSync syncBefore = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(bufferBarrier.SyncBefore, queuePipeline),
                            accessBefore,
                            queuePipeline);
                        Vortice.Direct3D12.BarrierSync syncAfter = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(bufferBarrier.SyncAfter, queuePipeline),
                            accessAfter,
                            queuePipeline);

                        (bufferBarriers ??= new List<Vortice.Direct3D12.BufferBarrier>(barriers.Length)).Add(new Vortice.Direct3D12.BufferBarrier
                        {
                            SyncBefore = syncBefore,
                            SyncAfter = syncAfter,
                            AccessBefore = accessBefore,
                            AccessAfter = accessAfter,
                            Resource = GetBufferResource(commandBuffer, bufferBarrier.Resource, i),
                            Offset = bufferBarrier.Range.Offset,
                            Size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                        });
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                        Vortice.Direct3D12.BarrierAccess accessBefore = ConvertToBarrierAccess(textureBarrier.AccessBefore);
                        Vortice.Direct3D12.BarrierAccess accessAfter = ConvertToBarrierAccess(textureBarrier.AccessAfter);
                        Vortice.Direct3D12.BarrierSync syncBefore = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(textureBarrier.SyncBefore, queuePipeline),
                            accessBefore,
                            queuePipeline);
                        Vortice.Direct3D12.BarrierSync syncAfter = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(textureBarrier.SyncAfter, queuePipeline),
                            accessAfter,
                            queuePipeline);

                        Dx12Texture texture = GetTexture(
                            commandBuffer,
                            textureBarrier.Resource,
                            i);
                        (textureBarriers ??= new List<Vortice.Direct3D12.TextureBarrier>(barriers.Length)).Add(new Vortice.Direct3D12.TextureBarrier
                        {
                            SyncBefore = syncBefore,
                            SyncAfter = syncAfter,
                            AccessBefore = accessBefore,
                            AccessAfter = accessAfter,
                            LayoutBefore = ConvertToBarrierLayout(textureBarrier.LayoutBefore, queuePipeline),
                            LayoutAfter = ConvertToBarrierLayout(textureBarrier.LayoutAfter, queuePipeline),
                            Resource = texture.NativeResource,
                            Subresources = ConvertToSubresourceRange(
                                textureBarrier.SubresourceRange,
                                texture.Descriptor),
                            Flags = Vortice.Direct3D12.TextureBarrierFlags.None
                        });
                        break;
                    }

                    default:
                        throw new InvalidOperationException($"Unsupported barrier kind {barrier.Kind}.");
                }
            }

            if (globalBarriers is { Count: > 0 })
            {
                commandBuffer.NativeCommandList.Barrier(new Vortice.Direct3D12.BarrierGroup(globalBarriers.ToArray()));
            }

            if (bufferBarriers is { Count: > 0 })
            {
                commandBuffer.NativeCommandList.Barrier(new Vortice.Direct3D12.BarrierGroup(bufferBarriers.ToArray()));
            }

            if (textureBarriers is { Count: > 0 })
            {
                commandBuffer.NativeCommandList.Barrier(new Vortice.Direct3D12.BarrierGroup(textureBarriers.ToArray()));
            }
        }

        private static void EmitSingleEnhancedBarrier(Dx12CommandBuffer commandBuffer, in RHIBarrier barrier, in ERHIPipelineType queuePipeline)
        {
            switch (barrier.Kind)
            {
                case ERHIBarrierKind.Global:
                {
                    RHIGlobalBarrier globalBarrier = barrier.GlobalBarrier;
                    Vortice.Direct3D12.GlobalBarrier nativeBarrier = new(
                        ResolveBarrierSync(globalBarrier.SyncBefore, queuePipeline),
                        ResolveBarrierSync(globalBarrier.SyncAfter, queuePipeline),
                        ConvertToBarrierAccess(globalBarrier.AccessBefore),
                        ConvertToBarrierAccess(globalBarrier.AccessAfter));
                    commandBuffer.NativeCommandList.Barrier(in nativeBarrier);
                    break;
                }

                case ERHIBarrierKind.Buffer:
                {
                    RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                    Vortice.Direct3D12.BarrierAccess accessBefore = ConvertToBarrierAccess(bufferBarrier.AccessBefore);
                    Vortice.Direct3D12.BarrierAccess accessAfter = ConvertToBarrierAccess(bufferBarrier.AccessAfter);
                    Vortice.Direct3D12.BufferBarrier nativeBarrier = new()
                    {
                        SyncBefore = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(bufferBarrier.SyncBefore, queuePipeline),
                            accessBefore,
                            queuePipeline),
                        SyncAfter = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(bufferBarrier.SyncAfter, queuePipeline),
                            accessAfter,
                            queuePipeline),
                        AccessBefore = accessBefore,
                        AccessAfter = accessAfter,
                        Resource = GetBufferResource(commandBuffer, bufferBarrier.Resource, 0),
                        Offset = bufferBarrier.Range.Offset,
                        Size = bufferBarrier.Range.Size == 0 ? RHIBufferRange.WholeSize : bufferBarrier.Range.Size
                    };
                    commandBuffer.NativeCommandList.Barrier(in nativeBarrier);
                    break;
                }

                case ERHIBarrierKind.Texture:
                {
                    RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                    Vortice.Direct3D12.BarrierAccess accessBefore = ConvertToBarrierAccess(textureBarrier.AccessBefore);
                    Vortice.Direct3D12.BarrierAccess accessAfter = ConvertToBarrierAccess(textureBarrier.AccessAfter);
                    Dx12Texture texture = GetTexture(
                        commandBuffer,
                        textureBarrier.Resource,
                        0);
                    Vortice.Direct3D12.TextureBarrier nativeBarrier = new()
                    {
                        SyncBefore = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(textureBarrier.SyncBefore, queuePipeline),
                            accessBefore,
                            queuePipeline),
                        SyncAfter = HarmonizeSyncWithAccess(
                            ResolveBarrierSync(textureBarrier.SyncAfter, queuePipeline),
                            accessAfter,
                            queuePipeline),
                        AccessBefore = accessBefore,
                        AccessAfter = accessAfter,
                        LayoutBefore = ConvertToBarrierLayout(textureBarrier.LayoutBefore, queuePipeline),
                        LayoutAfter = ConvertToBarrierLayout(textureBarrier.LayoutAfter, queuePipeline),
                        Resource = texture.NativeResource,
                        Subresources = ConvertToSubresourceRange(
                            textureBarrier.SubresourceRange,
                            texture.Descriptor),
                        Flags = Vortice.Direct3D12.TextureBarrierFlags.None
                    };
                    commandBuffer.NativeCommandList.Barrier(in nativeBarrier);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported barrier kind {barrier.Kind}.");
            }
        }

        private static Vortice.Direct3D12.ID3D12Resource GetBufferResource(
            Dx12CommandBuffer commandBuffer,
            RHIBuffer resource,
            in int index)
        {
            if (resource == null)
            {
                throw new ArgumentException(
                    index >= 0 ? $"Barrier buffer is null at index {index}." : "Barrier buffer is null.");
            }
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(resource.GetType().FullName);
            }

            Dx12Device device = Dx12EncoderGuards.RequireDevice(commandBuffer);
            if (resource is not Dx12Buffer buffer || !ReferenceEquals(buffer.Dx12Device, device))
            {
                throw new ArgumentException(
                    index >= 0
                        ? $"Barrier buffer at index {index} was created by a different backend or device."
                        : "Barrier buffer was created by a different backend or device.");
            }

            return buffer.NativeResource;
        }

        private static Dx12Texture GetTexture(
            Dx12CommandBuffer commandBuffer,
            RHITexture resource,
            in int index)
        {
            if (resource == null)
            {
                throw new ArgumentException(
                    index >= 0 ? $"Barrier texture is null at index {index}." : "Barrier texture is null.");
            }
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(resource.GetType().FullName);
            }

            Dx12Device device = Dx12EncoderGuards.RequireDevice(commandBuffer);
            if (resource is not Dx12Texture texture || !ReferenceEquals(texture.Dx12Device, device))
            {
                throw new ArgumentException(
                    index >= 0
                        ? $"Barrier texture at index {index} was created by a different backend or device."
                        : "Barrier texture was created by a different backend or device.");
            }

            return texture;
        }

        private static Vortice.Direct3D12.BarrierSubresourceRange ConvertToSubresourceRange(
            in RHITextureSubresourceRange range,
            in RHITextureDescriptor texture)
        {
            uint arrayLayerCount =
                texture.Dimension == ERHITextureDimension.Texture3D
                    ? 1u
                    : texture.Extent.z;
            return ConvertToSubresourceRange(
                range,
                texture.MipCount,
                arrayLayerCount,
                RHIBarrierUtility.InferAspectMask(texture.Format));
        }

        internal static Vortice.Direct3D12.BarrierSubresourceRange ConvertToSubresourceRange(
            in RHITextureSubresourceRange range,
            uint textureMipLevelCount,
            uint textureArrayLayerCount,
            ERHITextureAspectMask availableAspects)
        {

            bool stencilOnly =
                range.AspectMask == ERHITextureAspectMask.Stencil;
            bool depthStencil =
                range.AspectMask ==
                (ERHITextureAspectMask.Depth |
                 ERHITextureAspectMask.Stencil);
            if (range.AspectMask is not ERHITextureAspectMask.Color and
                not ERHITextureAspectMask.Depth and
                not ERHITextureAspectMask.Stencil &&
                !depthStencil)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    range.AspectMask,
                    "DX12 enhanced texture barriers require an exact color, " +
                    "depth, stencil, or depth-stencil aspect range.");
            }

            const ERHITextureAspectMask knownAspects =
                ERHITextureAspectMask.Color |
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
            if (availableAspects == ERHITextureAspectMask.None ||
                (availableAspects & ~knownAspects) != 0 ||
                (range.AspectMask & ~availableAspects) != 0)
            {
                throw new ArgumentException(
                    $"DX12 barrier aspect {range.AspectMask} is not available " +
                    $"for a texture with aspects {availableAspects}.",
                    nameof(range));
            }

            uint mipLevelCount = ResolveRangeCount(
                range.BaseMipLevel,
                range.MipLevelCount,
                textureMipLevelCount,
                "mip level");
            uint arrayLayerCount = ResolveRangeCount(
                range.BaseArrayLayer,
                range.ArrayLayerCount,
                textureArrayLayerCount,
                "array layer");
            return new Vortice.Direct3D12.BarrierSubresourceRange
            {
                IndexOrFirstMipLevel = range.BaseMipLevel,
                NumMipLevels = mipLevelCount,
                FirstArraySlice = range.BaseArrayLayer,
                NumArraySlices = arrayLayerCount,
                FirstPlane = stencilOnly ? 1u : 0u,
                NumPlanes = depthStencil ? 2u : 1u
            };
        }

        private static uint ResolveRangeCount(
            uint first,
            uint requestedCount,
            uint availableCount,
            string componentName)
        {
            if (availableCount == 0 || first >= availableCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(first),
                    first,
                    $"DX12 barrier {componentName} base must be inside " +
                    $"the available count {availableCount}.");
            }

            uint count = requestedCount == RHITextureSubresourceRange.All
                ? availableCount - first
                : requestedCount;
            if (count == 0 || count > availableCount - first)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedCount),
                    requestedCount,
                    $"DX12 barrier {componentName} count exceeds the " +
                    $"available range [{first}, {availableCount}).");
            }

            return count;
        }

        private static Vortice.Direct3D12.BarrierSync ResolveBarrierSync(in ERHISyncStageMask syncMask, in ERHIPipelineType queuePipeline)
        {
            Vortice.Direct3D12.BarrierSync sync = ConvertToBarrierSync(syncMask);
            if (sync == Vortice.Direct3D12.BarrierSync.None)
            {
                sync = GetDefaultQueueSync(queuePipeline);
            }

            return NormalizeBarrierSyncForQueue(sync, queuePipeline);
        }

        private static Vortice.Direct3D12.BarrierSync ConvertToBarrierSync(in ERHISyncStageMask syncMask)
        {
            if (syncMask == ERHISyncStageMask.None)
            {
                return Vortice.Direct3D12.BarrierSync.None;
            }

            Vortice.Direct3D12.BarrierSync sync = Vortice.Direct3D12.BarrierSync.None;
            if ((syncMask & ERHISyncStageMask.Transfer) != 0) sync |= Vortice.Direct3D12.BarrierSync.Copy;
            if ((syncMask & ERHISyncStageMask.Indirect) != 0) sync |= Vortice.Direct3D12.BarrierSync.ExecuteIndirect;
            if ((syncMask & ERHISyncStageMask.IndexInput) != 0) sync |= Vortice.Direct3D12.BarrierSync.IndexInput;
            if ((syncMask & ERHISyncStageMask.VertexInput) != 0) sync |= Vortice.Direct3D12.BarrierSync.IndexInput;
            if ((syncMask & ERHISyncStageMask.Vertex) != 0) sync |= Vortice.Direct3D12.BarrierSync.VertexShading;
            if ((syncMask & ERHISyncStageMask.Fragment) != 0) sync |= Vortice.Direct3D12.BarrierSync.PixelShading;
            if ((syncMask & ERHISyncStageMask.Compute) != 0) sync |= Vortice.Direct3D12.BarrierSync.ComputeShading;
            if ((syncMask & ERHISyncStageMask.Task) != 0) sync |= Vortice.Direct3D12.BarrierSync.NonPixelShading;
            if ((syncMask & ERHISyncStageMask.Mesh) != 0) sync |= Vortice.Direct3D12.BarrierSync.NonPixelShading;
            if ((syncMask & ERHISyncStageMask.AccelStructBuild) != 0) sync |= Vortice.Direct3D12.BarrierSync.BuildRaytracingAccelerationStructure;
            if ((syncMask & ERHISyncStageMask.AccelStructCopy) != 0) sync |= Vortice.Direct3D12.BarrierSync.CopyRaytracingAccelerationStructure;
            if ((syncMask & ERHISyncStageMask.MachineLearning) != 0) sync |= Vortice.Direct3D12.BarrierSync.ComputeShading;
            if ((syncMask & ERHISyncStageMask.RayTracing) != 0)
            {
                sync |= Vortice.Direct3D12.BarrierSync.Raytracing
                        | Vortice.Direct3D12.BarrierSync.BuildRaytracingAccelerationStructure
                        | Vortice.Direct3D12.BarrierSync.CopyRaytracingAccelerationStructure
                        | Vortice.Direct3D12.BarrierSync.EmitRaytracingAccelerationStructurePostBuildInfo;
            }

            if ((syncMask & ERHISyncStageMask.AllGraphics) != 0)
            {
                sync |= Vortice.Direct3D12.BarrierSync.Draw
                        | Vortice.Direct3D12.BarrierSync.RenderTarget
                        | Vortice.Direct3D12.BarrierSync.DepthStencil;
            }

            return sync;
        }

        private static Vortice.Direct3D12.BarrierAccess ConvertToBarrierAccess(in ERHIAccessMask accessMask)
        {
            if (accessMask == ERHIAccessMask.None)
            {
                return Vortice.Direct3D12.BarrierAccess.NoAccess;
            }

            Vortice.Direct3D12.BarrierAccess access = 0;
            bool hasShaderWrite = (accessMask & ERHIAccessMask.ShaderWrite) != 0;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.CopySource;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) access |= Vortice.Direct3D12.BarrierAccess.CopyDestination;
            if ((accessMask & ERHIAccessMask.ResolveRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.ResolveSource;
            if ((accessMask & ERHIAccessMask.ResolveWrite) != 0) access |= Vortice.Direct3D12.BarrierAccess.ResolveDestination;
            if ((accessMask & ERHIAccessMask.IndexRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.IndexBuffer;
            if ((accessMask & ERHIAccessMask.VertexRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.VertexBuffer;
            if ((accessMask & ERHIAccessMask.ConstantRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.ConstantBuffer;
            if ((accessMask & ERHIAccessMask.IndirectCommandRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.IndirectArgument;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0 && !hasShaderWrite) access |= Vortice.Direct3D12.BarrierAccess.ShaderResource;
            if (hasShaderWrite) access |= Vortice.Direct3D12.BarrierAccess.UnorderedAccess;
            if ((accessMask & ERHIAccessMask.RenderTargetRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.RenderTarget;
            if ((accessMask & ERHIAccessMask.RenderTargetWrite) != 0) access |= Vortice.Direct3D12.BarrierAccess.RenderTarget;
            if ((accessMask & ERHIAccessMask.DepthStencilRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.DepthStencilRead;
            if ((accessMask & ERHIAccessMask.DepthStencilWrite) != 0) access |= Vortice.Direct3D12.BarrierAccess.DepthStencilWrite;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.ShadingRateSource;
            if ((accessMask & ERHIAccessMask.AccelStructRead) != 0) access |= Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureRead;
            if ((accessMask & ERHIAccessMask.AccelStructWrite) != 0) access |= Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureWrite;
            if ((accessMask & ERHIAccessMask.Present) != 0) access |= Vortice.Direct3D12.BarrierAccess.Common;
            return access == 0 ? Vortice.Direct3D12.BarrierAccess.NoAccess : access;
        }

        private static Vortice.Direct3D12.BarrierLayout ConvertToBarrierLayout(in ERHITextureLayout layout, in ERHIPipelineType queuePipeline)
        {
            switch (layout)
            {
                case ERHITextureLayout.Common:
                    return Vortice.Direct3D12.BarrierLayout.Common;
                case ERHITextureLayout.Undefined:
                    return Vortice.Direct3D12.BarrierLayout.Undefined;
                case ERHITextureLayout.Present:
                    return Vortice.Direct3D12.BarrierLayout.Present;
                case ERHITextureLayout.RenderTarget:
                    return Vortice.Direct3D12.BarrierLayout.RenderTarget;
                case ERHITextureLayout.DepthStencilWrite:
                    return Vortice.Direct3D12.BarrierLayout.DepthStencilWrite;
                case ERHITextureLayout.DepthStencilReadOnly:
                    return Vortice.Direct3D12.BarrierLayout.DepthStencilRead;
                case ERHITextureLayout.General:
                    return queuePipeline == ERHIPipelineType.Compute
                        ? Vortice.Direct3D12.BarrierLayout.ComputeQueueUnorderedAccess
                        : Vortice.Direct3D12.BarrierLayout.DirectQueueUnorderedAccess;
                case ERHITextureLayout.ShaderReadOnly:
                    return queuePipeline == ERHIPipelineType.Compute
                        ? Vortice.Direct3D12.BarrierLayout.ComputeQueueShaderResource
                        : Vortice.Direct3D12.BarrierLayout.DirectQueueShaderResource;
                case ERHITextureLayout.CopySource:
                    return queuePipeline == ERHIPipelineType.Compute
                        ? Vortice.Direct3D12.BarrierLayout.ComputeQueueCopySource
                        : Vortice.Direct3D12.BarrierLayout.DirectQueueCopySource;
                case ERHITextureLayout.CopyDestination:
                    return queuePipeline == ERHIPipelineType.Compute
                        ? Vortice.Direct3D12.BarrierLayout.ComputeQueueCopyDestination
                        : Vortice.Direct3D12.BarrierLayout.DirectQueueCopyDestination;
                case ERHITextureLayout.ResolveSource:
                    return Vortice.Direct3D12.BarrierLayout.ResolveSource;
                case ERHITextureLayout.ResolveDestination:
                    return Vortice.Direct3D12.BarrierLayout.ResolveDestination;
                case ERHITextureLayout.ShadingRateSurface:
                    return Vortice.Direct3D12.BarrierLayout.ShadingRateSource;
                default:
                    return Vortice.Direct3D12.BarrierLayout.Common;
            }
        }

        private static Vortice.Direct3D12.ResourceStates ConvertToResourceBufferStates(in ERHIAccessMask accessMask)
        {
            if (accessMask == ERHIAccessMask.None)
            {
                return Vortice.Direct3D12.ResourceStates.Common;
            }

            Vortice.Direct3D12.ResourceStates result = 0;
            bool hasShaderWrite = (accessMask & ERHIAccessMask.ShaderWrite) != 0;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((accessMask & ERHIAccessMask.IndexRead) != 0) result |= Vortice.Direct3D12.ResourceStates.IndexBuffer;
            if ((accessMask & ERHIAccessMask.VertexRead) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((accessMask & ERHIAccessMask.ConstantRead) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((accessMask & ERHIAccessMask.IndirectCommandRead) != 0) result |= Vortice.Direct3D12.ResourceStates.IndirectArgument;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0 && !hasShaderWrite) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((accessMask & ERHIAccessMask.ShaderWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.UnorderedAccess;
            if ((accessMask & ERHIAccessMask.AccelStructRead) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((accessMask & ERHIAccessMask.AccelStructWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) result |= Vortice.Direct3D12.ResourceStates.ShadingRateSource;
            return result == 0 ? Vortice.Direct3D12.ResourceStates.Common : result;
        }

        private static Vortice.Direct3D12.ResourceStates ConvertToResourceTextureStates(in ERHITextureLayout layout, in ERHIAccessMask accessMask)
        {
            Vortice.Direct3D12.ResourceStates layoutState = ConvertTextureLayoutToResourceState(layout);
            Vortice.Direct3D12.ResourceStates accessState = ConvertTextureAccessToResourceState(accessMask);

            if (layoutState == Vortice.Direct3D12.ResourceStates.Common)
            {
                return accessState == 0 ? Vortice.Direct3D12.ResourceStates.Common : accessState;
            }

            if (accessState == 0)
            {
                return layoutState;
            }

            return layoutState | accessState;
        }

        private static Vortice.Direct3D12.ResourceStates ConvertTextureLayoutToResourceState(in ERHITextureLayout layout)
        {
            switch (layout)
            {
                case ERHITextureLayout.Common:
                    return Vortice.Direct3D12.ResourceStates.Common;
                case ERHITextureLayout.Present:
                    return Vortice.Direct3D12.ResourceStates.Present;
                case ERHITextureLayout.CopySource:
                    return Vortice.Direct3D12.ResourceStates.CopySource;
                case ERHITextureLayout.CopyDestination:
                    return Vortice.Direct3D12.ResourceStates.CopyDest;
                case ERHITextureLayout.ResolveSource:
                    return Vortice.Direct3D12.ResourceStates.ResolveSource;
                case ERHITextureLayout.ResolveDestination:
                    return Vortice.Direct3D12.ResourceStates.ResolveDest;
                case ERHITextureLayout.DepthStencilReadOnly:
                    return Vortice.Direct3D12.ResourceStates.DepthRead;
                case ERHITextureLayout.DepthStencilWrite:
                    return Vortice.Direct3D12.ResourceStates.DepthWrite;
                case ERHITextureLayout.RenderTarget:
                    return Vortice.Direct3D12.ResourceStates.RenderTarget;
                case ERHITextureLayout.ShaderReadOnly:
                    return Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
                case ERHITextureLayout.General:
                    return Vortice.Direct3D12.ResourceStates.UnorderedAccess;
                case ERHITextureLayout.ShadingRateSurface:
                    return Vortice.Direct3D12.ResourceStates.ShadingRateSource;
                case ERHITextureLayout.Undefined:
                default:
                    return Vortice.Direct3D12.ResourceStates.Common;
            }
        }

        private static Vortice.Direct3D12.ResourceStates ConvertTextureAccessToResourceState(in ERHIAccessMask accessMask)
        {
            Vortice.Direct3D12.ResourceStates result = 0;
            bool hasShaderWrite = (accessMask & ERHIAccessMask.ShaderWrite) != 0;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((accessMask & ERHIAccessMask.ResolveRead) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveSource;
            if ((accessMask & ERHIAccessMask.ResolveWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveDest;
            if ((accessMask & ERHIAccessMask.DepthStencilRead) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthRead;
            if ((accessMask & ERHIAccessMask.DepthStencilWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthWrite;
            if ((accessMask & ERHIAccessMask.RenderTargetRead) != 0) result |= Vortice.Direct3D12.ResourceStates.RenderTarget;
            if ((accessMask & ERHIAccessMask.RenderTargetWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.RenderTarget;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0 && !hasShaderWrite) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((accessMask & ERHIAccessMask.ShaderWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.UnorderedAccess;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) result |= Vortice.Direct3D12.ResourceStates.ShadingRateSource;
            if ((accessMask & ERHIAccessMask.Present) != 0) result |= Vortice.Direct3D12.ResourceStates.Present;
            return result;
        }

        private static Vortice.Direct3D12.BarrierSync ConvertToBarrierSync(in ERHIPipelineType pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipelineType.Transfer:
                    return Vortice.Direct3D12.BarrierSync.Copy;

                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.BarrierSync.ComputeShading;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.BarrierSync.All | Vortice.Direct3D12.BarrierSync.Copy | Vortice.Direct3D12.BarrierSync.Resolve;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported pipeline type '{0}' for enhanced barrier sync conversion.", pipeline));
            }
        }

        private static Vortice.Direct3D12.BarrierSync GetDefaultQueueSync(in ERHIPipelineType queuePipeline)
        {
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    return Vortice.Direct3D12.BarrierSync.Copy;

                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.BarrierSync.ComputeShading | Vortice.Direct3D12.BarrierSync.Copy;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.BarrierSync.All
                           | Vortice.Direct3D12.BarrierSync.AllShading
                           | Vortice.Direct3D12.BarrierSync.Copy
                           | Vortice.Direct3D12.BarrierSync.Resolve
                           | Vortice.Direct3D12.BarrierSync.ExecuteIndirect
                           | Vortice.Direct3D12.BarrierSync.Raytracing
                           | Vortice.Direct3D12.BarrierSync.BuildRaytracingAccelerationStructure
                           | Vortice.Direct3D12.BarrierSync.CopyRaytracingAccelerationStructure
                           | Vortice.Direct3D12.BarrierSync.EmitRaytracingAccelerationStructurePostBuildInfo
                           | Vortice.Direct3D12.BarrierSync.ClearUnorderedAccessView
                           | Vortice.Direct3D12.BarrierSync.IndexInput
                           | Vortice.Direct3D12.BarrierSync.Draw
                           | Vortice.Direct3D12.BarrierSync.VertexShading
                           | Vortice.Direct3D12.BarrierSync.PixelShading
                           | Vortice.Direct3D12.BarrierSync.NonPixelShading
                           | Vortice.Direct3D12.BarrierSync.RenderTarget
                           | Vortice.Direct3D12.BarrierSync.DepthStencil
                           | Vortice.Direct3D12.BarrierSync.ComputeShading;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported command queue pipeline '{0}' for enhanced barrier sync.", queuePipeline));
            }
        }

        private static Vortice.Direct3D12.BarrierSync GetUavBarrierSync(in ERHIPipelineType queuePipeline)
        {
            switch (queuePipeline)
            {
                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.BarrierSync.ComputeShading;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.BarrierSync.AllShading;

                default:
                    throw new InvalidOperationException(String.Format("Enhanced UAV barrier does not support queue pipeline '{0}'.", queuePipeline));
            }
        }

        private static Vortice.Direct3D12.BarrierSync NormalizeBarrierSyncForQueue(in Vortice.Direct3D12.BarrierSync sync, in ERHIPipelineType queuePipeline)
        {
            Vortice.Direct3D12.BarrierSync queueSupportedSync = GetQueueSupportedSyncMask(queuePipeline);
            if (sync == Vortice.Direct3D12.BarrierSync.None)
            {
                return GetDefaultQueueSync(queuePipeline);
            }

            Vortice.Direct3D12.BarrierSync unsupportedSync = sync & ~queueSupportedSync;
            if (unsupportedSync != Vortice.Direct3D12.BarrierSync.None)
            {
                throw new InvalidOperationException(String.Format("Enhanced barrier sync '{0}' contains unsupported bits '{1}' for queue '{2}'.", sync, unsupportedSync, queuePipeline));
            }

            return sync;
        }

        private static Vortice.Direct3D12.BarrierSync HarmonizeSyncWithAccess(in Vortice.Direct3D12.BarrierSync sync, in Vortice.Direct3D12.BarrierAccess access, in ERHIPipelineType queuePipeline)
        {
            Vortice.Direct3D12.BarrierAccess effectiveAccess = access & ~Vortice.Direct3D12.BarrierAccess.NoAccess;
            if (effectiveAccess == 0)
            {
                return Vortice.Direct3D12.BarrierSync.None;
            }

            Vortice.Direct3D12.BarrierSync requiredSync = 0;
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.CopySource | Vortice.Direct3D12.BarrierAccess.CopyDestination)) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.Copy;
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.ResolveSource | Vortice.Direct3D12.BarrierAccess.ResolveDestination)) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.Resolve;
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.IndexBuffer) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.IndexInput;
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.VertexBuffer) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.VertexShading | Vortice.Direct3D12.BarrierSync.NonPixelShading;
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.ConstantBuffer | Vortice.Direct3D12.BarrierAccess.ShaderResource | Vortice.Direct3D12.BarrierAccess.UnorderedAccess)) != 0)
            {
                requiredSync |= queuePipeline == ERHIPipelineType.Compute
                    ? Vortice.Direct3D12.BarrierSync.ComputeShading
                    : Vortice.Direct3D12.BarrierSync.AllShading;
            }
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.IndirectArgument) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.ExecuteIndirect;
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.RenderTarget) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.RenderTarget;
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.DepthStencilRead | Vortice.Direct3D12.BarrierAccess.DepthStencilWrite)) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.DepthStencil;
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureRead | Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureWrite)) != 0)
            {
                requiredSync |= Vortice.Direct3D12.BarrierSync.Raytracing
                                | Vortice.Direct3D12.BarrierSync.BuildRaytracingAccelerationStructure
                                | Vortice.Direct3D12.BarrierSync.CopyRaytracingAccelerationStructure
                                | Vortice.Direct3D12.BarrierSync.EmitRaytracingAccelerationStructurePostBuildInfo;
            }
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.ShadingRateSource) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.PixelShading;
            if ((effectiveAccess & Vortice.Direct3D12.BarrierAccess.Common) != 0) requiredSync |= GetDefaultQueueSync(queuePipeline);

            if (requiredSync == 0)
            {
                return sync;
            }

            Vortice.Direct3D12.BarrierSync filteredSync = sync & requiredSync;
            if (filteredSync == 0)
            {
                filteredSync = requiredSync;
            }

            return NormalizeBarrierSyncForQueue(filteredSync, queuePipeline);
        }

        private static Vortice.Direct3D12.BarrierSync GetQueueSupportedSyncMask(in ERHIPipelineType queuePipeline)
        {
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    return Vortice.Direct3D12.BarrierSync.Copy;

                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.BarrierSync.Copy | Vortice.Direct3D12.BarrierSync.ComputeShading | Vortice.Direct3D12.BarrierSync.ExecuteIndirect;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.BarrierSync.All
                           | Vortice.Direct3D12.BarrierSync.AllShading
                           | Vortice.Direct3D12.BarrierSync.Copy
                           | Vortice.Direct3D12.BarrierSync.Resolve
                           | Vortice.Direct3D12.BarrierSync.ExecuteIndirect
                           | Vortice.Direct3D12.BarrierSync.Raytracing
                           | Vortice.Direct3D12.BarrierSync.BuildRaytracingAccelerationStructure
                           | Vortice.Direct3D12.BarrierSync.CopyRaytracingAccelerationStructure
                           | Vortice.Direct3D12.BarrierSync.EmitRaytracingAccelerationStructurePostBuildInfo
                           | Vortice.Direct3D12.BarrierSync.ClearUnorderedAccessView
                           | Vortice.Direct3D12.BarrierSync.IndexInput
                           | Vortice.Direct3D12.BarrierSync.Draw
                           | Vortice.Direct3D12.BarrierSync.VertexShading
                           | Vortice.Direct3D12.BarrierSync.PixelShading
                           | Vortice.Direct3D12.BarrierSync.NonPixelShading
                           | Vortice.Direct3D12.BarrierSync.RenderTarget
                           | Vortice.Direct3D12.BarrierSync.DepthStencil
                           | Vortice.Direct3D12.BarrierSync.ComputeShading;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported command queue pipeline '{0}' for enhanced barrier validation.", queuePipeline));
            }
        }

        private static void EnsureUavBarrierQueueCompatibility(in ERHIPipelineType queuePipeline, in int index)
        {
            if (queuePipeline == ERHIPipelineType.Transfer)
            {
                throw new InvalidOperationException(String.Format("Enhanced UAV barrier at index {0} is invalid on Transfer queue.", index));
            }
        }

        private static void ValidateBufferStateForQueue(in ERHIPipelineType queuePipeline, in ERHIBufferState state, in int index, in bool isBefore)
        {
            string phase = isBefore ? "before" : "after";
            ERHIBufferState allKnownStates = ERHIBufferState.CopySrc
                                             | ERHIBufferState.CopyDst
                                             | ERHIBufferState.IndexBuffer
                                             | ERHIBufferState.VertexBuffer
                                             | ERHIBufferState.ConstantBuffer
                                             | ERHIBufferState.IndirectArgument
                                             | ERHIBufferState.ShaderResource
                                             | ERHIBufferState.UnorderedAccess
                                             | ERHIBufferState.RasterizerOrdered
                                             | ERHIBufferState.AccelStructRead
                                             | ERHIBufferState.AccelStructWrite
                                             | ERHIBufferState.AccelStructBuildInput
                                             | ERHIBufferState.AccelStructBuildBlast;

            ERHIBufferState unknownBits = state & ~allKnownStates;
            if (unknownBits != 0)
            {
                throw new InvalidOperationException(String.Format("Enhanced buffer transition at index {0} has unknown {1} state bits '{2}'.", index, phase, unknownBits));
            }

            ERHIBufferState allowedStates;
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    allowedStates = ERHIBufferState.CopySrc | ERHIBufferState.CopyDst;
                    break;

                case ERHIPipelineType.Compute:
                    allowedStates = ERHIBufferState.CopySrc
                                    | ERHIBufferState.CopyDst
                                    | ERHIBufferState.ConstantBuffer
                                    | ERHIBufferState.IndirectArgument
                                    | ERHIBufferState.ShaderResource
                                    | ERHIBufferState.UnorderedAccess
                                    | ERHIBufferState.RasterizerOrdered;
                    break;

                case ERHIPipelineType.Graphics:
                    allowedStates = allKnownStates;
                    break;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported command queue pipeline '{0}' for enhanced buffer transition validation.", queuePipeline));
            }

            ERHIBufferState unsupportedStates = state & ~allowedStates;
            if (unsupportedStates != 0)
            {
                throw new InvalidOperationException(String.Format("Enhanced buffer transition at index {0} has unsupported {1} state '{2}' on {3} queue.", index, phase, state, queuePipeline));
            }
        }

        private static void ValidateTextureStateForQueue(in ERHIPipelineType queuePipeline, in ERHITextureState state, in int index, in bool isBefore)
        {
            string phase = isBefore ? "before" : "after";
            ERHITextureState allKnownStates = ERHITextureState.Present
                                              | ERHITextureState.CopySrc
                                              | ERHITextureState.CopyDst
                                              | ERHITextureState.ResolveSrc
                                              | ERHITextureState.ResolveDst
                                              | ERHITextureState.DepthRead
                                              | ERHITextureState.DepthWrite
                                              | ERHITextureState.RenderTarget
                                              | ERHITextureState.ShaderResource
                                              | ERHITextureState.UnorderedAccess
                                              | ERHITextureState.RasterizerOrdered
                                              | ERHITextureState.ShadingRateSurface;

            ERHITextureState unknownBits = state & ~allKnownStates;
            if (unknownBits != 0)
            {
                throw new InvalidOperationException(String.Format("Enhanced texture transition at index {0} has unknown {1} state bits '{2}'.", index, phase, unknownBits));
            }

            ERHITextureState allowedStates;
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    allowedStates = ERHITextureState.CopySrc | ERHITextureState.CopyDst;
                    break;

                case ERHIPipelineType.Compute:
                    allowedStates = ERHITextureState.CopySrc
                                    | ERHITextureState.CopyDst
                                    | ERHITextureState.ShaderResource
                                    | ERHITextureState.UnorderedAccess
                                    | ERHITextureState.RasterizerOrdered;
                    break;

                case ERHIPipelineType.Graphics:
                    allowedStates = allKnownStates;
                    break;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported command queue pipeline '{0}' for enhanced texture transition validation.", queuePipeline));
            }

            ERHITextureState unsupportedStates = state & ~allowedStates;
            if (unsupportedStates != 0)
            {
                throw new InvalidOperationException(String.Format("Enhanced texture transition at index {0} has unsupported {1} state '{2}' on {3} queue.", index, phase, state, queuePipeline));
            }
        }

        private static Vortice.Direct3D12.BarrierAccess ConvertToBarrierAccess(in ERHIBufferState state)
        {
            if (state == ERHIBufferState.Undefine)
            {
                return Vortice.Direct3D12.BarrierAccess.NoAccess;
            }

            Vortice.Direct3D12.BarrierAccess result = 0;
            bool hasShaderWrite = (state & ERHIBufferState.UnorderedAccess) != 0 || (state & ERHIBufferState.RasterizerOrdered) != 0;

            if ((state & ERHIBufferState.CopyDst) != 0) result |= Vortice.Direct3D12.BarrierAccess.CopyDestination;
            if ((state & ERHIBufferState.CopySrc) != 0) result |= Vortice.Direct3D12.BarrierAccess.CopySource;
            if ((state & ERHIBufferState.IndexBuffer) != 0) result |= Vortice.Direct3D12.BarrierAccess.IndexBuffer;
            if ((state & ERHIBufferState.VertexBuffer) != 0) result |= Vortice.Direct3D12.BarrierAccess.VertexBuffer;
            if ((state & ERHIBufferState.ConstantBuffer) != 0) result |= Vortice.Direct3D12.BarrierAccess.ConstantBuffer;
            if ((state & ERHIBufferState.IndirectArgument) != 0) result |= Vortice.Direct3D12.BarrierAccess.IndirectArgument;
            if ((state & ERHIBufferState.ShaderResource) != 0 && !hasShaderWrite) result |= Vortice.Direct3D12.BarrierAccess.ShaderResource;
            if ((state & ERHIBufferState.UnorderedAccess) != 0) result |= Vortice.Direct3D12.BarrierAccess.UnorderedAccess;
            if ((state & ERHIBufferState.RasterizerOrdered) != 0) result |= Vortice.Direct3D12.BarrierAccess.UnorderedAccess;
            if ((state & ERHIBufferState.AccelStructRead) != 0) result |= Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureRead;
            if ((state & ERHIBufferState.AccelStructWrite) != 0) result |= Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureWrite;
            if ((state & ERHIBufferState.AccelStructBuildInput) != 0) result |= Vortice.Direct3D12.BarrierAccess.ShaderResource;
            if ((state & ERHIBufferState.AccelStructBuildBlast) != 0) result |= Vortice.Direct3D12.BarrierAccess.RaytracingAccelerationStructureWrite;

            return result == 0 ? Vortice.Direct3D12.BarrierAccess.NoAccess : result;
        }

        private static Vortice.Direct3D12.BarrierAccess ConvertToBarrierAccess(in ERHITextureState state)
        {
            if (state == ERHITextureState.Undefine)
            {
                return Vortice.Direct3D12.BarrierAccess.NoAccess;
            }

            Vortice.Direct3D12.BarrierAccess result = 0;
            bool hasShaderWrite = (state & ERHITextureState.UnorderedAccess) != 0 || (state & ERHITextureState.RasterizerOrdered) != 0;

            if ((state & ERHITextureState.CopyDst) != 0) result |= Vortice.Direct3D12.BarrierAccess.CopyDestination;
            if ((state & ERHITextureState.CopySrc) != 0) result |= Vortice.Direct3D12.BarrierAccess.CopySource;
            if ((state & ERHITextureState.ResolveDst) != 0) result |= Vortice.Direct3D12.BarrierAccess.ResolveDestination;
            if ((state & ERHITextureState.ResolveSrc) != 0) result |= Vortice.Direct3D12.BarrierAccess.ResolveSource;
            if ((state & ERHITextureState.DepthRead) != 0) result |= Vortice.Direct3D12.BarrierAccess.DepthStencilRead;
            if ((state & ERHITextureState.DepthWrite) != 0) result |= Vortice.Direct3D12.BarrierAccess.DepthStencilWrite;
            if ((state & ERHITextureState.RenderTarget) != 0) result |= Vortice.Direct3D12.BarrierAccess.RenderTarget;
            if ((state & ERHITextureState.ShaderResource) != 0 && !hasShaderWrite) result |= Vortice.Direct3D12.BarrierAccess.ShaderResource;
            if ((state & ERHITextureState.UnorderedAccess) != 0) result |= Vortice.Direct3D12.BarrierAccess.UnorderedAccess;
            if ((state & ERHITextureState.RasterizerOrdered) != 0) result |= Vortice.Direct3D12.BarrierAccess.UnorderedAccess;
            if ((state & ERHITextureState.ShadingRateSurface) != 0) result |= Vortice.Direct3D12.BarrierAccess.ShadingRateSource;

            return result == 0 ? Vortice.Direct3D12.BarrierAccess.NoAccess : result;
        }

        private static Vortice.Direct3D12.BarrierLayout ConvertToBarrierLayout(in ERHITextureState state, in ERHIPipelineType queuePipeline)
        {
            if (state == ERHITextureState.Undefine)
            {
                return Vortice.Direct3D12.BarrierLayout.Undefined;
            }

            if ((state & ERHITextureState.Present) != 0) return Vortice.Direct3D12.BarrierLayout.Present;
            if ((state & ERHITextureState.RenderTarget) != 0) return Vortice.Direct3D12.BarrierLayout.RenderTarget;
            if ((state & ERHITextureState.DepthWrite) != 0) return Vortice.Direct3D12.BarrierLayout.DepthStencilWrite;
            if ((state & ERHITextureState.DepthRead) != 0) return Vortice.Direct3D12.BarrierLayout.DepthStencilRead;
            if ((state & ERHITextureState.UnorderedAccess) != 0 || (state & ERHITextureState.RasterizerOrdered) != 0)
            {
                switch (queuePipeline)
                {
                    case ERHIPipelineType.Compute:
                        return Vortice.Direct3D12.BarrierLayout.ComputeQueueUnorderedAccess;

                    case ERHIPipelineType.Graphics:
                        return Vortice.Direct3D12.BarrierLayout.DirectQueueUnorderedAccess;

                    default:
                        return Vortice.Direct3D12.BarrierLayout.UnorderedAccess;
                }
            }
            if ((state & ERHITextureState.ShaderResource) != 0)
            {
                switch (queuePipeline)
                {
                    case ERHIPipelineType.Compute:
                        return Vortice.Direct3D12.BarrierLayout.ComputeQueueShaderResource;

                    case ERHIPipelineType.Graphics:
                        return Vortice.Direct3D12.BarrierLayout.DirectQueueShaderResource;

                    default:
                        return Vortice.Direct3D12.BarrierLayout.ShaderResource;
                }
            }
            if ((state & ERHITextureState.ShadingRateSurface) != 0) return Vortice.Direct3D12.BarrierLayout.ShadingRateSource;
            if ((state & ERHITextureState.ResolveSrc) != 0) return Vortice.Direct3D12.BarrierLayout.ResolveSource;
            if ((state & ERHITextureState.ResolveDst) != 0) return Vortice.Direct3D12.BarrierLayout.ResolveDestination;
            if ((state & ERHITextureState.CopySrc) != 0)
            {
                switch (queuePipeline)
                {
                    case ERHIPipelineType.Compute:
                        return Vortice.Direct3D12.BarrierLayout.ComputeQueueCopySource;

                    case ERHIPipelineType.Graphics:
                        return Vortice.Direct3D12.BarrierLayout.DirectQueueCopySource;

                    default:
                        return Vortice.Direct3D12.BarrierLayout.CopySource;
                }
            }
            if ((state & ERHITextureState.CopyDst) != 0)
            {
                switch (queuePipeline)
                {
                    case ERHIPipelineType.Compute:
                        return Vortice.Direct3D12.BarrierLayout.ComputeQueueCopyDestination;

                    case ERHIPipelineType.Graphics:
                        return Vortice.Direct3D12.BarrierLayout.DirectQueueCopyDestination;

                    default:
                        return Vortice.Direct3D12.BarrierLayout.CopyDestination;
                }
            }

            switch (queuePipeline)
            {
                case ERHIPipelineType.Compute:
                    return Vortice.Direct3D12.BarrierLayout.ComputeQueueCommon;

                case ERHIPipelineType.Graphics:
                    return Vortice.Direct3D12.BarrierLayout.DirectQueueCommon;

                case ERHIPipelineType.Transfer:
                    return Vortice.Direct3D12.BarrierLayout.Common;

                default:
                    throw new InvalidOperationException(String.Format("Unsupported command queue pipeline '{0}' for enhanced texture layout conversion.", queuePipeline));
            }
        }
    }

    internal unsafe class Dx12TransferEncoder : RHITransferEncoder
    {
        private RHITransferPassDescriptor m_PassDescriptor;

        public Dx12TransferEncoder(Dx12CommandBuffer cmdBuffer)
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
            }        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
            Dx12Query dx12Query = Dx12EncoderGuards.RequireQuery(query);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);

            switch (query.QueryDescriptor.Type)
            {
                case ERHIQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, startIndex, queriesCount, dx12Query.QueryResult, startIndex * dx12Query.ResultStrideInBytes);
                    break;

                case ERHIQueryType.Statistics:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, startIndex, queriesCount, dx12Query.QueryResult, startIndex * dx12Query.ResultStrideInBytes);
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, startIndex, queriesCount, dx12Query.QueryResult, startIndex * dx12Query.ResultStrideInBytes);
                    break;
            }
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            Dx12Buffer dx12SrcBuffer = Dx12EncoderGuards.RequireBuffer(srcBuffer);
            Dx12Buffer dx12DstBuffer = Dx12EncoderGuards.RequireBuffer(dstBuffer);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);

            dx12CommandBuffer.NativeCommandList.CopyBufferRegion(dx12DstBuffer.NativeResource, (ulong)dstOffset, dx12SrcBuffer.NativeResource, (ulong)srcOffset, (ulong)size);
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            Dx12Buffer srcBuffer = Dx12EncoderGuards.RequireBuffer(src.Buffer);
            Dx12Texture dstTexture = Dx12EncoderGuards.RequireTexture(dst.Texture);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);

            Dx12TextureCopyLocation srcLocation = new Dx12TextureCopyLocation
            {
                pResource = srcBuffer.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.PlacedFootPrint,
                PlacedFootprint = new Vortice.Direct3D12.PlacedSubresourceFootPrint
                {
                    Offset = (ulong)src.Offset,
                    Footprint = new Vortice.Direct3D12.SubresourceFootPrint
                    {
                        Format = Dx12Utility.ConvertToDx12Format(dstTexture.Descriptor.Format),
                        Width = (uint)size.x,
                        Height = (uint)size.y,
                        Depth = (uint)size.z,
                        RowPitch = Dx12Utility.ComputeRowPitch(dstTexture.Descriptor.Format, (uint)size.x)
                    }
                }
            };

            Dx12TextureCopyLocation dstLocation = new Dx12TextureCopyLocation
            {
                pResource = dstTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = dst.SliceBase * dstTexture.Descriptor.MipCount + dst.MipLevel
            };

            Dx12Box srcBox = new Dx12Box
            {
                left = 0,
                top = 0,
                front = 0,
                right = (uint)size.x,
                bottom = (uint)size.y,
                back = (uint)size.z
            };

            dx12CommandBuffer.NativeCommandList.CopyTextureRegion(&dstLocation, 0, 0, 0, &srcLocation, &srcBox);
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            Dx12Texture srcTexture = Dx12EncoderGuards.RequireTexture(src.Texture);
            Dx12Buffer dstBuffer = Dx12EncoderGuards.RequireBuffer(dst.Buffer);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);

            Dx12TextureCopyLocation srcLocation = new Dx12TextureCopyLocation
            {
                pResource = srcTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = src.SliceBase * srcTexture.Descriptor.MipCount + src.MipLevel
            };

            Dx12TextureCopyLocation dstLocation = new Dx12TextureCopyLocation
            {
                pResource = dstBuffer.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.PlacedFootPrint,
                PlacedFootprint = new Vortice.Direct3D12.PlacedSubresourceFootPrint
                {
                    Offset = dst.Offset,
                    Footprint = new Vortice.Direct3D12.SubresourceFootPrint
                    {
                        Format = Dx12Utility.ConvertToDx12Format(srcTexture.Descriptor.Format), // Assuming the format is the same
                        Width = (uint)size.x,
                        Height = (uint)size.y,
                        Depth = (uint)size.z,
                        RowPitch = Dx12Utility.ComputeRowPitch(srcTexture.Descriptor.Format, (uint)size.x)
                    }
                }
            };

            dx12CommandBuffer.NativeCommandList.CopyTextureRegion(&dstLocation, 0, 0, 0, &srcLocation, null);
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            Dx12Texture srcTexture = Dx12EncoderGuards.RequireTexture(src.Texture);
            Dx12Texture dstTexture = Dx12EncoderGuards.RequireTexture(dst.Texture);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);

            Dx12TextureCopyLocation srcLocation = new Dx12TextureCopyLocation
            {
                pResource = srcTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = src.SliceBase * srcTexture.Descriptor.MipCount + src.MipLevel
            };

            Dx12TextureCopyLocation dstLocation = new Dx12TextureCopyLocation
            {
                pResource = dstTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = dst.SliceBase * dstTexture.Descriptor.MipCount + dst.MipLevel
            };

            Dx12Box srcBox = new Dx12Box
            {
                left = 0,
                top = 0,
                front = 0,
                right = (uint)size.x,
                bottom = (uint)size.y,
                back = (uint)size.z
            };

            dx12CommandBuffer.NativeCommandList.CopyTextureRegion(&dstLocation, 0, 0, 0, &srcLocation, &srcBox);
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);
#if DEBUG
            PopDebugGroup();
#endif
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12ComputeEncoder : RHIComputeEncoder
    {
        private RHIComputePassDescriptor m_PassDescriptor;

        public Dx12ComputeEncoder(Dx12CommandBuffer cmdBuffer)
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
            }        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;

            Dx12ComputePipeline dx12Pipeline = Dx12EncoderGuards.RequireComputePipeline(pipeline);
            Dx12PipelineLayout dx12PipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(pipeline.Descriptor.PipelineLayout);

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList.SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequireCachedComputePipelineLayout(m_CachedPipeline);
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BindingTableBinder.BindCompute(commandBuffer.NativeCommandList, pipelineLayout, resourceTable, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequireCachedComputePipelineLayout(m_CachedPipeline);
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            if (!Dx12BindingTableBinder.ValidatePushConstantWrite(pipelineLayout, data, size, offset))
            {
                return;
            }
            commandBuffer.NativeCommandList.SetComputeRoot32BitConstants(pipelineLayout.PushConstantRootParameterIndex, size / 4, data.ToPointer(), offset / 4);
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(argsBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchComputeIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
        }

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            Dx12ComputeIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12ComputeIndirectCommandBuffer ?? throw new InvalidOperationException("DX12 compute indirect dispatch requires a Dx12ComputeIndirectCommandBuffer.");
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The compute encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Compute);
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12RaytracingEncoder : RHIRaytracingEncoder
    {
        private RHIRayTracingPassDescriptor m_PassDescriptor;

        public Dx12RaytracingEncoder(Dx12CommandBuffer cmdBuffer)
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
            }        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;

            Dx12RaytracingPipeline dx12Pipeline = Dx12EncoderGuards.RequireRaytracingPipeline(pipeline);
            Dx12PipelineLayout dx12PipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(pipeline.Descriptor.PipelineLayout);

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.SetPipelineState1(dx12Pipeline.NativePipeline);
            dx12CommandBuffer.NativeCommandList.SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequireCachedRaytracingPipelineLayout(m_CachedPipeline);
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BindingTableBinder.BindCompute(commandBuffer.NativeCommandList, pipelineLayout, resourceTable, tableIndex);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12TopLevelAccelStruct dx12TopLevelAccelStruct = Dx12EncoderGuards.RequireTopLevelAccelStruct(topLevelAccelStruct);
            Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription accelStructDescription = dx12TopLevelAccelStruct.NativeAccelStructDescriptor;
            dx12CommandBuffer.NativeCommandList.BuildRaytracingAccelerationStructure(accelStructDescription);

            Vortice.Direct3D12.ResourceBarrier uavBarrier = Dx12ResourceBarrierUtil.InitUAV(dx12TopLevelAccelStruct.ResultBuffer);
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &uavBarrier);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BottomLevelAccelStruct dx12BottomLevelAccelStruct = Dx12EncoderGuards.RequireBottomLevelAccelStruct(bottomLevelAccelStruct);
            Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription accelStructDescription = dx12BottomLevelAccelStruct.NativeAccelStructDescriptor;
            dx12CommandBuffer.NativeCommandList.BuildRaytracingAccelerationStructure(accelStructDescription);

            Vortice.Direct3D12.ResourceBarrier uavBarrier = Dx12ResourceBarrierUtil.InitUAV(dx12BottomLevelAccelStruct.NativeResultBuffer);
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &uavBarrier);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            Dx12FunctionTable dx12FunctionTable = Dx12EncoderGuards.RequireFunctionTable(functionTable)
                ?? throw new InvalidOperationException("Raytracing dispatch requires a Dx12FunctionTable.");
            if (!dx12FunctionTable.IsGenerated)
            {
                throw new InvalidOperationException("FunctionTable must call Generate() before Dispatch().");
            }

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer)
                ?? throw new InvalidOperationException("DX12 ray-tracing dispatch requires a Dx12CommandBuffer.");
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);

            if (dx12Device.Capabilities.RayTracing.Pipeline.Tier != ERHICapabilityTier.Unavailable)
            {
                Vortice.Direct3D12.DispatchRaysDescription dispatchRayDescriptor;
                {
                    dispatchRayDescriptor.Depth = depth;
                    dispatchRayDescriptor.Width = width;
                    dispatchRayDescriptor.Height = height;
                    dispatchRayDescriptor.MissShaderTable.SizeInBytes = dx12FunctionTable.MissSize;
                    dispatchRayDescriptor.MissShaderTable.StartAddress = dx12FunctionTable.MissAddress;
                    dispatchRayDescriptor.MissShaderTable.StrideInBytes = dx12FunctionTable.MissStride;
                    dispatchRayDescriptor.HitGroupTable.SizeInBytes = dx12FunctionTable.HitGroupSize;
                    dispatchRayDescriptor.HitGroupTable.StartAddress = dx12FunctionTable.HitGroupAddress;
                    dispatchRayDescriptor.HitGroupTable.StrideInBytes = dx12FunctionTable.HitGroupStride;
                    dispatchRayDescriptor.RayGenerationShaderRecord.SizeInBytes = dx12FunctionTable.RayGenSize;
                    dispatchRayDescriptor.RayGenerationShaderRecord.StartAddress = dx12FunctionTable.RayGenAddress;
                    dispatchRayDescriptor.CallableShaderTable.SizeInBytes = dx12FunctionTable.CallableSize;
                    dispatchRayDescriptor.CallableShaderTable.StartAddress = dx12FunctionTable.CallableAddress;
                    dispatchRayDescriptor.CallableShaderTable.StrideInBytes = dx12FunctionTable.CallableStride;
                }

                dx12CommandBuffer.NativeCommandList.DispatchRays(dispatchRayDescriptor);
            }
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(argsBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            if (dx12Device.Capabilities.RayTracing.Pipeline.Tier != ERHICapabilityTier.Unavailable)
            {
                Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
                dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchRayIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
            }
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
            Dx12RayTracingIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12RayTracingIndirectCommandBuffer ?? throw new InvalidOperationException("DX12 ray-tracing indirect dispatch requires a Dx12RayTracingIndirectCommandBuffer.");
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The ray-tracing encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.RayTracing);
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12RasterEncoder : RHIRasterEncoder
    {
        protected List<Dx12AttachmentInfo> m_AttachmentInfos;
        private bool m_UseNativeRenderPass;
        private bool m_IsNativeRenderPassActive;
        private RHIRasterPassDescriptor m_PassDescriptor;
        private Dx12RasterPassLowering? m_Lowering;
        private Dx12RasterPassLowering Lowering =>
            m_Lowering ?? throw new InvalidOperationException("DX12 raster pass has not begun.");
        private Vortice.Direct3D12.CpuDescriptorHandle[] m_RtvHandles;
        private Vortice.Direct3D12.CpuDescriptorHandle[][]
            m_OmSubPassRenderTargetHandles;
        private Vortice.Direct3D12.CpuDescriptorHandle? m_DsvHandle;
        private Vortice.Direct3D12.CpuDescriptorHandle? m_ReadOnlyDsvHandle;
        private Dx12DescriptorInfo m_PrivateAttachmentDescriptors;
        private int m_PrivateAttachmentDescriptorCount;
        private bool m_HasPrivateAttachmentDescriptors;
        private readonly ERHITextureLayout[] m_ColorLayouts;
        private ERHITextureLayout m_DepthStencilLayout;
        private Vortice.Direct3D12.RenderPassRenderTargetDescription[] m_NativeRenderPassColorDescriptions;
        private Vortice.Direct3D12.RenderPassDepthStencilDescription? m_NativeRenderPassDepthStencilDescription;

        public Dx12RasterEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_AttachmentInfos = new List<Dx12AttachmentInfo>(5);
            m_RtvHandles = Array.Empty<Vortice.Direct3D12.CpuDescriptorHandle>();
            m_OmSubPassRenderTargetHandles =
                Array.Empty<Vortice.Direct3D12.CpuDescriptorHandle[]>();
            m_ColorLayouts = new ERHITextureLayout[RHIAttachmentIndexArray.MaxAttachments];
            m_UseNativeRenderPass = false;
            m_IsNativeRenderPassActive = false;
            m_NativeRenderPassColorDescriptions = Array.Empty<Vortice.Direct3D12.RenderPassRenderTargetDescription>();
            m_NativeRenderPassDepthStencilDescription = null;
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
            try
            {
            m_PassDescriptor = plan.DescriptorSnapshot;
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
            m_AttachmentInfos.Clear();
            m_UseNativeRenderPass = false;
            m_IsNativeRenderPassActive = false;
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            Array.Clear(m_ColorLayouts);
            m_DepthStencilLayout = ERHITextureLayout.Undefined;
            m_HasPrivateAttachmentDescriptors = false;
            m_PrivateAttachmentDescriptorCount = 0;
            m_OmSubPassRenderTargetHandles =
                Array.Empty<Vortice.Direct3D12.CpuDescriptorHandle[]>();
            m_Lowering = Dx12RasterPassLowering.Compile(
                plan,
                dx12Device.NativeRenderPass.Tier != ERHICapabilityTier.Unavailable,
                dx12Device.Capabilities.Raster.RasterOrderedAccess.Tier !=
                    ERHICapabilityTier.Unavailable,
                dx12Device.EnhancedBarriers.Tier != ERHICapabilityTier.Unavailable);
            try
            {
                m_RtvHandles = CreateColorAttachmentViews(
                    descriptor,
                    dx12Device);
                CreateDepthStencilAttachmentViews(
                    descriptor,
                    Lowering.SubPasses.Span,
                    dx12Device,
                    out m_DsvHandle,
                    out m_ReadOnlyDsvHandle);

                if (Lowering.Strategy ==
                    EDx12RasterPassStrategy.NativeRenderPass)
                {
                    CacheNativeRenderPass(
                        dx12CommandBuffer,
                        descriptor,
                        m_RtvHandles,
                        m_DsvHandle);
                    m_UseNativeRenderPass = true;
                }
                else
                {
                    m_OmSubPassRenderTargetHandles =
                        CreateOmSubPassRenderTargetViews(
                            plan,
                            m_Lowering,
                            dx12Device);
                }

                if (Lowering.Strategy ==
                    EDx12RasterPassStrategy.OmMultipass)
                {
                    ClearOmAttachments(dx12CommandBuffer, descriptor);
                    BindOmSubPass(dx12CommandBuffer, plan, 0);
                }

                if (descriptor.ShadingRateTexture != null)
                {
                    Dx12Texture dx12Texture = Dx12EncoderGuards.RequireTexture(descriptor.ShadingRateTexture);
                    dx12CommandBuffer.NativeCommandList.RSSetShadingRateImage(dx12Texture.NativeResource);
                }

                // The private shader-visible allocation is committed last.
                // Any earlier BeginRasterPass failure can therefore release
                // every RTV/DSV/null descriptor immediately, while this
                // allocation remains transactional until command-buffer
                // registration succeeds.
                CreatePrivateAttachmentDescriptors(
                    plan,
                    m_Lowering,
                    dx12Device,
                    dx12CommandBuffer);
            }
            catch
            {
                ReleaseAttachmentDescriptors(dx12Device);
                ResetRasterDescriptorState();
                throw;
            }
            }
            catch
            {
                ClearRasterPassState();
                throw;
            }
        }

        private Vortice.Direct3D12.CpuDescriptorHandle[]
            CreateColorAttachmentViews(
                in RHIRasterPassDescriptor descriptor,
                Dx12Device device)
        {
            Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles = new Vortice.Direct3D12.CpuDescriptorHandle[descriptor.ColorAttachments.Length];

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor attachment = ref descriptor.ColorAttachments.Span[i];
                Dx12Texture texture = Dx12EncoderGuards.RequireTexture(attachment.RenderTarget) ?? throw new InvalidOperationException("Render target must be a Dx12Texture.");
                if (texture == null)
                {
                    throw new InvalidOperationException($"Color render target at index {i} is null.");
                }
                if (!ReferenceEquals(texture.Dx12Device, device))
                {
                    throw new ArgumentException(
                        $"Color render target at index {i} belongs to a " +
                        "different DX12 device.");
                }

                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = attachment.SubresourceRange.MipLevelCount;
                    viewDescriptor.BaseMipLevel = attachment.SubresourceRange.BaseMipLevel;
                    viewDescriptor.ArrayCount = attachment.SubresourceRange.ArrayLayerCount;
                    viewDescriptor.BaseArraySlice = attachment.SubresourceRange.BaseArrayLayer;
                    viewDescriptor.ViewType = ERHITextureViewType.Pending;
                }

                Vortice.Direct3D12.RenderTargetViewDescription desc = new Vortice.Direct3D12.RenderTargetViewDescription();
                desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                desc.ViewDimension = Dx12Utility.ConvertToDx12TextureRTVDimension(texture.Descriptor.Dimension);
                Dx12Utility.FillTexture2DRTV(ref desc.Texture2D, viewDescriptor, texture.Descriptor.Dimension);
                Dx12Utility.FillTexture3DRTV(ref desc.Texture3D, viewDescriptor, texture.Descriptor.Dimension);
                Dx12Utility.FillTexture2DArrayRTV(ref desc.Texture2DArray, viewDescriptor, texture.Descriptor.Dimension);

                Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
                {
                    dx12AttachmentInfo.bDepthStencil = false;
                    dx12AttachmentInfo.AttachmentInfo =
                        device.AllocateRtvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                rtvHandles[i] = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                device.NativeDevice.CreateRenderTargetView(
                    texture.NativeResource,
                    desc,
                    rtvHandles[i]);
            }

            return rtvHandles;
        }

        private void CreateDepthStencilAttachmentViews(
            in RHIRasterPassDescriptor descriptor,
            ReadOnlySpan<Dx12RasterSubPassLowering> subPasses,
            Dx12Device device,
            out Vortice.Direct3D12.CpuDescriptorHandle? writableHandle,
            out Vortice.Direct3D12.CpuDescriptorHandle? readOnlyHandle)
        {
            writableHandle = null;
            readOnlyHandle = null;
            if (!descriptor.DepthStencilAttachment.HasValue)
            {
                return;
            }

            writableHandle = CreateDepthStencilAttachmentView(
                descriptor.DepthStencilAttachment.Value,
                ERHISubPassFlags.None,
                device);

            ERHISubPassFlags readOnlyFlags = ERHISubPassFlags.None;
            for (int index = 0; index < subPasses.Length; ++index)
            {
                readOnlyFlags |= subPasses[index].DepthStencilFlags;
            }
            if (readOnlyFlags != ERHISubPassFlags.None)
            {
                readOnlyHandle = CreateDepthStencilAttachmentView(
                    descriptor.DepthStencilAttachment.Value,
                    readOnlyFlags,
                    device);
            }
        }

        private Vortice.Direct3D12.CpuDescriptorHandle
            CreateDepthStencilAttachmentView(
                in RHIDepthStencilAttachmentDescriptor attachment,
                ERHISubPassFlags readOnlyFlags,
                Dx12Device device)
        {
            Dx12Texture texture = Dx12EncoderGuards.RequireTexture(attachment.RenderTarget) ?? throw new InvalidOperationException("Render target must be a Dx12Texture.");
            if (texture == null)
            {
                throw new InvalidOperationException("Depth stencil render target is null.");
            }
            if (!ReferenceEquals(texture.Dx12Device, device))
            {
                throw new ArgumentException(
                    "Depth stencil render target belongs to a different " +
                    "DX12 device.");
            }

            RHITextureViewDescriptor viewDescriptor;
            {
                viewDescriptor.MipCount = attachment.SubresourceRange.MipLevelCount;
                viewDescriptor.BaseMipLevel = attachment.SubresourceRange.BaseMipLevel;
                viewDescriptor.ArrayCount = attachment.SubresourceRange.ArrayLayerCount;
                viewDescriptor.BaseArraySlice = attachment.SubresourceRange.BaseArrayLayer;
                viewDescriptor.ViewType = ERHITextureViewType.Pending;
            }

            Vortice.Direct3D12.DepthStencilViewDescription desc = new Vortice.Direct3D12.DepthStencilViewDescription();
            desc.Flags = Dx12Utility.GetDx12DSVFlag(
                (readOnlyFlags & ERHISubPassFlags.ReadOnlyDepth) != 0,
                (readOnlyFlags & ERHISubPassFlags.ReadOnlyStencil) != 0);
            desc.Format = Dx12Utility.ConvertToDx12Format(texture.Descriptor.Format);
            desc.ViewDimension = Dx12Utility.ConvertToDx12TextureDSVDimension(texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DDSV(ref desc.Texture2D, viewDescriptor, texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DArrayDSV(ref desc.Texture2DArray, viewDescriptor, texture.Descriptor.Dimension);

            Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
            {
                dx12AttachmentInfo.bDepthStencil = true;
                dx12AttachmentInfo.AttachmentInfo =
                    device.AllocateDsvDescriptor(1);
            }
            m_AttachmentInfos.Add(dx12AttachmentInfo);

            Vortice.Direct3D12.CpuDescriptorHandle dsvHandle = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
            device.NativeDevice.CreateDepthStencilView(
                texture.NativeResource,
                desc,
                dsvHandle);
            return dsvHandle;
        }

        private Vortice.Direct3D12.CpuDescriptorHandle[][]
            CreateOmSubPassRenderTargetViews(
                RasterPassPlan plan,
                Dx12RasterPassLowering lowering,
                Dx12Device device)
        {
            ReadOnlySpan<Dx12RasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            Vortice.Direct3D12.CpuDescriptorHandle[][] phaseHandles =
                new Vortice.Direct3D12.CpuDescriptorHandle[
                    subPasses.Length][];
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                ref readonly Dx12RasterSubPassLowering subPass =
                    ref subPasses[subPassIndex];
                int outputLocationCount =
                    subPass.OutputLogicalAttachments.Length;
                Vortice.Direct3D12.CpuDescriptorHandle[] outputHandles =
                    new Vortice.Direct3D12.CpuDescriptorHandle[
                        outputLocationCount];
                for (int outputLocation = 0;
                     outputLocation < outputLocationCount;
                     ++outputLocation)
                {
                    int renderTargetLogicalAttachment =
                        subPass.GetRenderTargetLogicalAttachment(
                            outputLocation);
                    outputHandles[outputLocation] =
                        renderTargetLogicalAttachment >= 0
                            ? m_RtvHandles[
                                renderTargetLogicalAttachment]
                            : CreateTypedNullRenderTargetView(
                                plan,
                                in subPass,
                                outputLocation,
                                device);
                }
                phaseHandles[subPassIndex] = outputHandles;
            }
            return phaseHandles;
        }

        private Vortice.Direct3D12.CpuDescriptorHandle
            CreateTypedNullRenderTargetView(
                RasterPassPlan plan,
                in Dx12RasterSubPassLowering subPass,
                int outputLocation,
                Dx12Device device)
        {
            int representativeLogicalAttachment =
                subPass.GetOutputLogicalAttachment(outputLocation);
            if (representativeLogicalAttachment < 0)
            {
                representativeLogicalAttachment =
                    FindFirstBoundOutput(in subPass);
            }
            if (representativeLogicalAttachment < 0)
            {
                throw new InvalidOperationException(
                    "DX12 typed null RTV creation requires a bound " +
                    "output-location format anchor.");
            }

            ref readonly RHIColorAttachmentDescriptor attachment =
                ref plan.GetColorAttachment(
                    representativeLogicalAttachment);
            Dx12Texture texture = Dx12EncoderGuards.RequireTexture(attachment.RenderTarget)
                ?? throw new ArgumentException(
                    $"DX12 attachment {representativeLogicalAttachment} " +
                    "belongs to a different backend.");
            RHITextureViewDescriptor viewDescriptor = new()
            {
                MipCount = attachment.SubresourceRange.MipLevelCount,
                BaseMipLevel =
                    attachment.SubresourceRange.BaseMipLevel,
                ArrayCount =
                    attachment.SubresourceRange.ArrayLayerCount,
                BaseArraySlice =
                    attachment.SubresourceRange.BaseArrayLayer,
                ViewType = ERHITextureViewType.Pending,
            };
            Vortice.Direct3D12.RenderTargetViewDescription description =
                new()
                {
                    Format = Dx12Utility.ConvertToDx12ViewFormat(
                        subPass.OutputLocationFormats.Span[
                            outputLocation]),
                    ViewDimension =
                        Dx12Utility.ConvertToDx12TextureRTVDimension(
                            texture.Descriptor.Dimension),
                };
            Dx12Utility.FillTexture2DRTV(
                ref description.Texture2D,
                viewDescriptor,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture3DRTV(
                ref description.Texture3D,
                viewDescriptor,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DArrayRTV(
                ref description.Texture2DArray,
                viewDescriptor,
                texture.Descriptor.Dimension);

            Dx12AttachmentInfo descriptor = new()
            {
                bDepthStencil = false,
                AttachmentInfo = device.AllocateRtvDescriptor(1),
            };
            m_AttachmentInfos.Add(descriptor);
            device.NativeDevice.CreateRenderTargetView(
                null,
                description,
                descriptor.AttachmentInfo.CpuHandle);
            return descriptor.AttachmentInfo.CpuHandle;
        }

        private static int FindFirstBoundOutput(
            in Dx12RasterSubPassLowering subPass)
        {
            for (int outputLocation = 0;
                 outputLocation < subPass.OutputLogicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    subPass.GetOutputLogicalAttachment(outputLocation);
                if (logicalAttachment >= 0)
                {
                    return logicalAttachment;
                }
            }
            return RHIAttachmentInterfaceSignature
                .UnboundLogicalAttachment;
        }

        private void CreatePrivateAttachmentDescriptors(
            RasterPassPlan plan,
            Dx12RasterPassLowering lowering,
            Dx12Device device,
            Dx12CommandBuffer commandBuffer)
        {
            m_HasPrivateAttachmentDescriptors = false;
            m_PrivateAttachmentDescriptorCount = 0;
            if (!lowering.RequiresPrivateAttachmentTable)
            {
                return;
            }

            ReadOnlySpan<Dx12RasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            int descriptorCount = checked(
                subPasses.Length *
                Dx12RasterPassLowering
                    .PrivateDescriptorCountPerSubPass);
            Dx12DescriptorInfo allocation =
                device.AllocateCbvSrvUavDescriptor(descriptorCount);
            try
            {
                uint descriptorSize =
                    device.DescriptorHeapCbvSrvUav.DescriptorSize;
                for (int subPassIndex = 0;
                     subPassIndex < subPasses.Length;
                     ++subPassIndex)
                {
                    ref readonly Dx12RasterSubPassLowering subPass =
                        ref subPasses[subPassIndex];
                    InitializeTypedNullPrivateAttachmentTable(
                        device,
                        allocation,
                        descriptorSize,
                        subPassIndex);

                    for (int inputIndex = 0;
                         inputIndex <
                            subPass.PrivateInputLogicalAttachments.Length;
                         ++inputIndex)
                    {
                        int logicalAttachment =
                            subPass.GetPrivateInputLogicalAttachment(
                                inputIndex);
                        if (logicalAttachment < 0)
                        {
                            continue;
                        }

                        GetPrivateAttachmentView(
                            plan,
                            logicalAttachment,
                            device,
                            out Dx12Texture texture,
                            out RHITextureViewDescriptor view);
                        Vortice.Direct3D12
                            .ShaderResourceViewDescription description =
                                CreatePrivateShaderResourceDescription(
                                    texture,
                                    in view);
                        device.NativeDevice.CreateShaderResourceView(
                            texture.NativeResource,
                            description,
                            new Vortice.Direct3D12
                                .CpuDescriptorHandle(
                                    in allocation.CpuHandle,
                                    Dx12RasterPassLowering
                                        .GetPrivateInputDescriptorOffset(
                                            subPassIndex,
                                            inputIndex),
                                    descriptorSize));
                    }

                    for (int logicalAttachment = 0;
                         logicalAttachment < plan.ColorAttachmentCount;
                         ++logicalAttachment)
                    {
                        byte bit = checked((byte)(
                            1 << logicalAttachment));
                        if ((subPass.RasterOrderedMask & bit) == 0)
                        {
                            continue;
                        }

                        GetPrivateAttachmentView(
                            plan,
                            logicalAttachment,
                            device,
                            out Dx12Texture texture,
                            out RHITextureViewDescriptor view);
                        Vortice.Direct3D12
                            .UnorderedAccessViewDescription description =
                                CreatePrivateRasterOrderedDescription(
                                    texture,
                                    in view);
                        device.NativeDevice.CreateUnorderedAccessView(
                            texture.NativeResource,
                            null,
                            description,
                            new Vortice.Direct3D12
                                .CpuDescriptorHandle(
                                    in allocation.CpuHandle,
                                    Dx12RasterPassLowering
                                        .GetRasterOrderedDescriptorOffset(
                                            subPassIndex,
                                            logicalAttachment),
                                    descriptorSize));
                    }
                }

                commandBuffer.RegisterTransientCbvSrvUavDescriptor(
                    allocation,
                    descriptorCount);
                m_PrivateAttachmentDescriptors = allocation;
                m_PrivateAttachmentDescriptorCount = descriptorCount;
                m_HasPrivateAttachmentDescriptors = true;
            }
            catch
            {
                device.FreeCbvSrvUavDescriptor(
                    allocation.Index,
                    descriptorCount);
                throw;
            }
        }

        private static void InitializeTypedNullPrivateAttachmentTable(
            Dx12Device device,
            in Dx12DescriptorInfo allocation,
            uint descriptorSize,
            int subPassIndex)
        {
            Vortice.Direct3D12.ShaderResourceViewDescription nullSrv =
                new()
                {
                    Format = Vortice.DXGI.Format.R32_UInt,
                    ViewDimension = Vortice.Direct3D12
                        .ShaderResourceViewDimension.Texture2D,
                    Shader4ComponentMapping = 5768,
                };
            Vortice.Direct3D12.UnorderedAccessViewDescription nullUav =
                new()
                {
                    Format = Vortice.DXGI.Format.R32_UInt,
                    ViewDimension = Vortice.Direct3D12
                        .UnorderedAccessViewDimension.Texture2D,
                };
            for (int ordinal = 0;
                 ordinal < RHIAttachmentIndexArray.MaxAttachments;
                 ++ordinal)
            {
                device.NativeDevice.CreateShaderResourceView(
                    null,
                    nullSrv,
                    new Vortice.Direct3D12.CpuDescriptorHandle(
                        in allocation.CpuHandle,
                        Dx12RasterPassLowering
                            .GetPrivateInputDescriptorOffset(
                                subPassIndex,
                                ordinal),
                        descriptorSize));
                device.NativeDevice.CreateUnorderedAccessView(
                    null,
                    null,
                    nullUav,
                    new Vortice.Direct3D12.CpuDescriptorHandle(
                        in allocation.CpuHandle,
                        Dx12RasterPassLowering
                            .GetRasterOrderedDescriptorOffset(
                                subPassIndex,
                                ordinal),
                        descriptorSize));
            }
        }

        private static void GetPrivateAttachmentView(
            RasterPassPlan plan,
            int logicalAttachment,
            Dx12Device device,
            out Dx12Texture texture,
            out RHITextureViewDescriptor view)
        {
            ref readonly RHIColorAttachmentDescriptor attachment =
                ref plan.GetColorAttachment(logicalAttachment);
            texture = Dx12EncoderGuards.RequireTexture(attachment.RenderTarget)
                ?? throw new ArgumentException(
                    $"DX12 attachment {logicalAttachment} belongs to a " +
                    "different backend.");
            if (!ReferenceEquals(texture.Dx12Device, device))
            {
                throw new ArgumentException(
                    $"DX12 attachment {logicalAttachment} belongs to a " +
                    "different device.");
            }
            view = new RHITextureViewDescriptor
            {
                BaseMipLevel =
                    attachment.SubresourceRange.BaseMipLevel,
                MipCount =
                    attachment.SubresourceRange.MipLevelCount,
                BaseArraySlice =
                    attachment.SubresourceRange.BaseArrayLayer,
                ArrayCount =
                    attachment.SubresourceRange.ArrayLayerCount,
            };
        }

        private static Vortice.Direct3D12
            .ShaderResourceViewDescription
            CreatePrivateShaderResourceDescription(
                Dx12Texture texture,
                in RHITextureViewDescriptor view)
        {
            Vortice.Direct3D12.ShaderResourceViewDescription description =
                new()
                {
                    Format = Dx12Utility.ConvertToDx12ViewFormat(
                        texture.Descriptor.Format),
                    ViewDimension =
                        Dx12Utility.ConvertToDx12TextureSRVDimension(
                            texture.Descriptor.Dimension),
                    Shader4ComponentMapping = 5768,
                };
            Dx12Utility.FillTexture2DSRV(
                ref description.Texture2D,
                view,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DArraySRV(
                ref description.Texture2DArray,
                view,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture3DSRV(
                ref description.Texture3D,
                view,
                texture.Descriptor.Dimension);
            return description;
        }

        private static Vortice.Direct3D12
            .UnorderedAccessViewDescription
            CreatePrivateRasterOrderedDescription(
                Dx12Texture texture,
                in RHITextureViewDescriptor view)
        {
            Vortice.Direct3D12.UnorderedAccessViewDescription description =
                new()
                {
                    Format = Dx12Utility.ConvertToDx12ViewFormat(
                        texture.Descriptor.Format),
                    ViewDimension =
                        Dx12Utility.ConvertToDx12TextureUAVDimension(
                            texture.Descriptor.Dimension),
                };
            Dx12Utility.FillTexture2DUAV(
                ref description.Texture2D,
                view,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DArrayUAV(
                ref description.Texture2DArray,
                view,
                texture.Descriptor.Dimension);
            Dx12Utility.FillTexture3DUAV(
                ref description.Texture3D,
                view,
                texture.Descriptor.Dimension);
            return description;
        }

        private void ClearOmAttachments(
            Dx12CommandBuffer dx12CommandBuffer,
            in RHIRasterPassDescriptor descriptor)
        {
            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachments.Span[i];
                if (colorAttachmentDescriptor.LoadAction != ERHILoadAction.Clear)
                {
                    continue;
                }

                float4 clearValue = colorAttachmentDescriptor.ClearValue;
                Vortice.Mathematics.Color4 nativeClearValue = new Vortice.Mathematics.Color4(clearValue.x, clearValue.y, clearValue.z, clearValue.w);
                dx12CommandBuffer.NativeCommandList.ClearRenderTargetView(m_RtvHandles[i], nativeClearValue);
            }

            if (m_DsvHandle.HasValue && descriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthStencilAttachmentDescriptor = descriptor.DepthStencilAttachment.Value;
                if (depthStencilAttachmentDescriptor.DepthLoadOp == ERHILoadAction.Clear || depthStencilAttachmentDescriptor.StencilLoadOp == ERHILoadAction.Clear)
                {
                    dx12CommandBuffer.NativeCommandList.ClearDepthStencilView(
                        m_DsvHandle.Value,
                        Dx12Utility.GetDx12ClearFlagByDSA(depthStencilAttachmentDescriptor),
                        depthStencilAttachmentDescriptor.DepthClearValue,
                        Convert.ToByte(depthStencilAttachmentDescriptor.StencilClearValue));
                }
            }
        }

        private void BindOmSubPass(
            Dx12CommandBuffer commandBuffer,
            RasterPassPlan plan,
            int subPassIndex)
        {
            ref readonly Dx12RasterSubPassLowering subPass =
                ref Lowering.SubPasses.Span[subPassIndex];
            ApplyOmSubPassTransitions(commandBuffer, plan, in subPass);

            Vortice.Direct3D12.CpuDescriptorHandle[] renderTargets =
                m_OmSubPassRenderTargetHandles[subPassIndex];
            int renderTargetCount = renderTargets.Length;

            Vortice.Direct3D12.CpuDescriptorHandle? depthStencilHandle =
                null;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilHandle =
                    subPass.DepthStencilFlags == ERHISubPassFlags.None
                        ? m_DsvHandle
                        : m_ReadOnlyDsvHandle;
            }

            fixed (Vortice.Direct3D12.CpuDescriptorHandle*
                   renderTargetsPtr = renderTargets)
            {
                commandBuffer.NativeCommandList.OMSetRenderTargets(
                    checked((uint)renderTargetCount),
                    renderTargetsPtr,
                    false,
                    depthStencilHandle);
            }
        }

        private void ApplyOmSubPassTransitions(
            Dx12CommandBuffer commandBuffer,
            RasterPassPlan plan,
            in Dx12RasterSubPassLowering subPass)
        {
            for (int attachmentIndex = 0;
                 attachmentIndex < plan.ColorAttachmentCount;
                 ++attachmentIndex)
            {
                byte bit = checked((byte)(1 << attachmentIndex));
                ERHITextureLayout desiredLayout;
                ERHIAccessMask desiredAccess;
                if ((subPass.RasterOrderedMask & bit) != 0)
                {
                    desiredLayout = ERHITextureLayout.General;
                    desiredAccess =
                        ERHIAccessMask.ShaderRead |
                        ERHIAccessMask.ShaderWrite;
                }
                else if ((subPass.ShaderResourceMask & bit) != 0)
                {
                    desiredLayout = ERHITextureLayout.ShaderReadOnly;
                    desiredAccess = ERHIAccessMask.ShaderRead;
                }
                else if ((subPass.RenderTargetMask & bit) != 0)
                {
                    desiredLayout = ERHITextureLayout.RenderTarget;
                    desiredAccess =
                        ERHIAccessMask.RenderTargetRead |
                        ERHIAccessMask.RenderTargetWrite;
                }
                else
                {
                    continue;
                }

                ERHITextureLayout currentLayout =
                    m_ColorLayouts[attachmentIndex] == ERHITextureLayout.Undefined
                        ? ERHITextureLayout.RenderTarget
                        : m_ColorLayouts[attachmentIndex];
                if (currentLayout == desiredLayout)
                {
                    continue;
                }

                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(attachmentIndex);
                RHIBarrier barrier = RHIBarrier.Texture(
                    attachment.RenderTarget,
                    attachment.SubresourceRange,
                    currentLayout,
                    desiredLayout,
                    ERHISyncStageMask.Fragment,
                    ERHISyncStageMask.Fragment,
                    AccessForColorLayout(currentLayout),
                    desiredAccess);
                Dx12BarrierEmitter.EmitBarrier(commandBuffer, barrier);
                m_ColorLayouts[attachmentIndex] = desiredLayout;
            }

            if (plan.HasDepthStencilAttachment)
            {
                ERHITextureLayout desiredLayout =
                    subPass.DepthStencilFlags ==
                        ERHISubPassFlags.ReadOnlyDepthStencil
                        ? ERHITextureLayout.DepthStencilReadOnly
                        : ERHITextureLayout.DepthStencilWrite;
                ERHITextureLayout currentLayout =
                    m_DepthStencilLayout == ERHITextureLayout.Undefined
                        ? ERHITextureLayout.DepthStencilWrite
                        : m_DepthStencilLayout;
                if (currentLayout != desiredLayout)
                {
                    RHIDepthStencilAttachmentDescriptor attachment =
                        plan.GetDepthStencilAttachment();
                    RHIBarrier barrier = RHIBarrier.Texture(
                        attachment.RenderTarget,
                        attachment.SubresourceRange,
                        currentLayout,
                        desiredLayout,
                        ERHISyncStageMask.Fragment,
                        ERHISyncStageMask.Fragment,
                        currentLayout == ERHITextureLayout.DepthStencilReadOnly
                            ? ERHIAccessMask.DepthStencilRead
                            : ERHIAccessMask.DepthStencilWrite,
                        desiredLayout == ERHITextureLayout.DepthStencilReadOnly
                            ? ERHIAccessMask.DepthStencilRead
                            : ERHIAccessMask.DepthStencilWrite);
                    Dx12BarrierEmitter.EmitBarrier(commandBuffer, barrier);
                    m_DepthStencilLayout = desiredLayout;
                }
            }
        }

        private static ERHIAccessMask AccessForColorLayout(
            ERHITextureLayout layout) => layout switch
        {
            ERHITextureLayout.General =>
                ERHIAccessMask.ShaderRead | ERHIAccessMask.ShaderWrite,
            ERHITextureLayout.ShaderReadOnly => ERHIAccessMask.ShaderRead,
            _ =>
                ERHIAccessMask.RenderTargetRead |
                ERHIAccessMask.RenderTargetWrite,
        };

        private void CacheNativeRenderPass(Dx12CommandBuffer dx12CommandBuffer,
                                           in RHIRasterPassDescriptor descriptor,
                                           Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles,
                                           Vortice.Direct3D12.CpuDescriptorHandle? dsvHandle)
        {
            Vortice.Direct3D12.RenderPassRenderTargetDescription[] colorDescriptions = new Vortice.Direct3D12.RenderPassRenderTargetDescription[descriptor.ColorAttachments.Length];
            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachments.Span[i];
                Dx12Texture colorTexture = Dx12EncoderGuards.RequireTexture(colorAttachmentDescriptor.RenderTarget);
                if (colorTexture == null)
                {
                    throw new InvalidOperationException($"Color render target at index {i} is null.");
                }

                float4 clearColor = colorAttachmentDescriptor.ClearValue;
                Vortice.Mathematics.Color4 nativeClearColor = new Vortice.Mathematics.Color4(clearColor.x, clearColor.y, clearColor.z, clearColor.w);
                Vortice.Direct3D12.ClearValue clearValue = new Vortice.Direct3D12.ClearValue(Dx12Utility.ConvertToDx12ViewFormat(colorTexture.Descriptor.Format), nativeClearColor);
                Vortice.Direct3D12.RenderPassBeginningAccess beginningAccess = BuildBeginningAccess(colorAttachmentDescriptor.LoadAction, clearValue);
                Vortice.Direct3D12.RenderPassEndingAccess endingAccess = BuildColorEndingAccess(colorAttachmentDescriptor);
                colorDescriptions[i] = new Vortice.Direct3D12.RenderPassRenderTargetDescription(rtvHandles[i], beginningAccess, endingAccess);
            }

            Vortice.Direct3D12.RenderPassDepthStencilDescription? depthStencilDescription = null;
            if (dsvHandle.HasValue && descriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthStencilAttachment = descriptor.DepthStencilAttachment.Value;
                Dx12Texture depthStencilTexture = Dx12EncoderGuards.RequireTexture(depthStencilAttachment.RenderTarget);
                if (depthStencilTexture == null)
                {
                    throw new InvalidOperationException("Depth stencil render target is null.");
                }

                Vortice.Direct3D12.DepthStencilValue depthStencilClear = new Vortice.Direct3D12.DepthStencilValue(depthStencilAttachment.DepthClearValue, Convert.ToByte(depthStencilAttachment.StencilClearValue));
                Vortice.Direct3D12.ClearValue clearValue = new Vortice.Direct3D12.ClearValue(Dx12Utility.ConvertToDx12Format(depthStencilTexture.Descriptor.Format), depthStencilClear);
                Vortice.Direct3D12.RenderPassBeginningAccess depthBeginningAccess = BuildBeginningAccess(depthStencilAttachment.DepthLoadOp, clearValue);
                Vortice.Direct3D12.RenderPassBeginningAccess stencilBeginningAccess = BuildBeginningAccess(depthStencilAttachment.StencilLoadOp, clearValue);
                Vortice.Direct3D12.RenderPassEndingAccess depthEndingAccess = BuildDepthStencilEndingAccess(depthStencilAttachment, true);
                Vortice.Direct3D12.RenderPassEndingAccess stencilEndingAccess = BuildDepthStencilEndingAccess(depthStencilAttachment, false);
                depthStencilDescription = new Vortice.Direct3D12.RenderPassDepthStencilDescription(dsvHandle.Value, depthBeginningAccess, stencilBeginningAccess, depthEndingAccess, stencilEndingAccess);
            }

            m_NativeRenderPassColorDescriptions = colorDescriptions;
            m_NativeRenderPassDepthStencilDescription = depthStencilDescription;
        }

        private void EnsureNativeRenderPassActive()
        {
            if (!m_UseNativeRenderPass || m_IsNativeRenderPassActive)
            {
                return;
            }

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.BeginRenderPass(m_NativeRenderPassColorDescriptions, m_NativeRenderPassDepthStencilDescription, Vortice.Direct3D12.RenderPassFlags.None);
            m_IsNativeRenderPassActive = true;
        }

        private static Vortice.Direct3D12.RenderPassBeginningAccess BuildBeginningAccess(in ERHILoadAction loadAction, in Vortice.Direct3D12.ClearValue clearValue)
        {
            switch (loadAction)
            {
                case ERHILoadAction.Load:
                    return new Vortice.Direct3D12.RenderPassBeginningAccess(Vortice.Direct3D12.RenderPassBeginningAccessType.Preserve);

                case ERHILoadAction.Clear:
                    return new Vortice.Direct3D12.RenderPassBeginningAccess(clearValue);

                case ERHILoadAction.DontCare:
                    return new Vortice.Direct3D12.RenderPassBeginningAccess(Vortice.Direct3D12.RenderPassBeginningAccessType.Discard);

                default:
                    return new Vortice.Direct3D12.RenderPassBeginningAccess(Vortice.Direct3D12.RenderPassBeginningAccessType.Preserve);
            }
        }

        private static Vortice.Direct3D12.RenderPassEndingAccess BuildColorEndingAccess(in RHIColorAttachmentDescriptor colorAttachmentDescriptor)
        {
            switch (colorAttachmentDescriptor.StoreAction)
            {
                case ERHIStoreAction.Store:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Preserve);

                case ERHIStoreAction.DontCare:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Discard);

                case ERHIStoreAction.Resolve:
                case ERHIStoreAction.StoreAndResolve:
                    if (colorAttachmentDescriptor.ResolveTarget == null)
                    {
                        throw new InvalidOperationException("Color resolve requires ResolveTarget to be set.");
                    }

                    Dx12Texture srcTexture = Dx12EncoderGuards.RequireTexture(colorAttachmentDescriptor.RenderTarget);
                    Dx12Texture dstTexture = Dx12EncoderGuards.RequireTexture(colorAttachmentDescriptor.ResolveTarget);
                    if (srcTexture == null || dstTexture == null)
                    {
                        throw new InvalidOperationException("Color resolve requires Dx12 textures for source and destination.");
                    }

                    Vortice.Direct3D12.RenderPassEndingAccessResolveParameters resolveParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveParameters
                    {
                        SrcResource = srcTexture.NativeResource,
                        DstResource = dstTexture.NativeResource,
                        SubresourceCount = 1,
                        SubresourceParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveSubresourceParameters(
                            ComputeResolveSubresourceIndex(srcTexture, colorAttachmentDescriptor.SubresourceRange.BaseMipLevel, colorAttachmentDescriptor.SubresourceRange.BaseArrayLayer),
                            ComputeResolveSubresourceIndex(dstTexture, colorAttachmentDescriptor.ResolveSubresourceRange.BaseMipLevel, colorAttachmentDescriptor.ResolveSubresourceRange.BaseArrayLayer),
                            0,
                            0,
                            default),
                        Format = Dx12Utility.ConvertToDx12Format(srcTexture.Descriptor.Format),
                        ResolveMode = Vortice.Direct3D12.ResolveMode.Average,
                        PreserveResolveSource = colorAttachmentDescriptor.StoreAction == ERHIStoreAction.StoreAndResolve
                    };
                    resolveParameters.Format = Dx12Utility.ConvertToDx12ViewFormat(srcTexture.Descriptor.Format);
                    return new Vortice.Direct3D12.RenderPassEndingAccess(resolveParameters);

                default:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Preserve);
            }
        }

        private static Vortice.Direct3D12.RenderPassEndingAccess BuildDepthStencilEndingAccess(in RHIDepthStencilAttachmentDescriptor depthStencilAttachmentDescriptor, bool isDepth)
        {
            ERHIStoreAction storeAction = isDepth ? depthStencilAttachmentDescriptor.DepthStoreOp : depthStencilAttachmentDescriptor.StencilStoreOp;

            switch (storeAction)
            {
                case ERHIStoreAction.Store:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Preserve);

                case ERHIStoreAction.DontCare:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Discard);

                case ERHIStoreAction.Resolve:
                case ERHIStoreAction.StoreAndResolve:
                    if (depthStencilAttachmentDescriptor.ResolveTarget == null)
                    {
                        throw new InvalidOperationException("Depth/stencil resolve requires ResolveTarget to be set.");
                    }

                    Dx12Texture srcTexture = Dx12EncoderGuards.RequireTexture(depthStencilAttachmentDescriptor.RenderTarget);
                    Dx12Texture dstTexture = Dx12EncoderGuards.RequireTexture(depthStencilAttachmentDescriptor.ResolveTarget);
                    if (srcTexture == null || dstTexture == null)
                    {
                        throw new InvalidOperationException("Depth/stencil resolve requires Dx12 textures for source and destination.");
                    }

                    EResolveMode requestedResolveMode = isDepth
                        ? depthStencilAttachmentDescriptor.DepthResolveMode
                        : depthStencilAttachmentDescriptor.StencilResolveMode;
                    Vortice.Direct3D12.ResolveMode resolveMode = ConvertDepthResolveMode(requestedResolveMode);
                    Vortice.Direct3D12.RenderPassEndingAccessResolveParameters resolveParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveParameters
                    {
                        SrcResource = srcTexture.NativeResource,
                        DstResource = dstTexture.NativeResource,
                        SubresourceCount = 1,
                        SubresourceParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveSubresourceParameters(
                            ComputeResolveSubresourceIndex(srcTexture, depthStencilAttachmentDescriptor.SubresourceRange.BaseMipLevel, depthStencilAttachmentDescriptor.SubresourceRange.BaseArrayLayer),
                            ComputeResolveSubresourceIndex(dstTexture, depthStencilAttachmentDescriptor.ResolveSubresourceRange.BaseMipLevel, depthStencilAttachmentDescriptor.ResolveSubresourceRange.BaseArrayLayer),
                            0,
                            0,
                            default),
                        ResolveMode = resolveMode,
                        PreserveResolveSource = storeAction == ERHIStoreAction.StoreAndResolve
                    };
                    resolveParameters.Format = Dx12Utility.ConvertToDx12ViewFormat(srcTexture.Descriptor.Format);
                    return new Vortice.Direct3D12.RenderPassEndingAccess(resolveParameters);

                default:
                    return new Vortice.Direct3D12.RenderPassEndingAccess(Vortice.Direct3D12.RenderPassEndingAccessType.Preserve);
            }
        }

        private static Vortice.Direct3D12.ResolveMode ConvertDepthResolveMode(in EResolveMode resolveMode)
        {
            switch (resolveMode)
            {
                case EResolveMode.None:
                    return Vortice.Direct3D12.ResolveMode.Average;

                case EResolveMode.Min:
                    return Vortice.Direct3D12.ResolveMode.Min;

                case EResolveMode.Max:
                    return Vortice.Direct3D12.ResolveMode.Max;

                case EResolveMode.Sample0:
                    throw new NotSupportedException("Depth/stencil resolve mode Sample0 is not supported in DX12 render pass path.");

                default:
                    return Vortice.Direct3D12.ResolveMode.Average;
            }
        }

        private static uint ComputeResolveSubresourceIndex(Dx12Texture texture, in uint mipLevel, in uint arraySlice)
        {
            return arraySlice * texture.Descriptor.MipCount + mipLevel;
        }

        private void EndOmRasterPass(Dx12CommandBuffer commandBuffer)
        {
            RestoreOmAttachmentLayouts(commandBuffer);
            ResolveOmAttachments(commandBuffer);
        }

        private void RestoreOmAttachmentLayouts(
            Dx12CommandBuffer commandBuffer)
        {
            for (int attachmentIndex = 0;
                 attachmentIndex < m_PassDescriptor.ColorAttachments.Length;
                 ++attachmentIndex)
            {
                ERHITextureLayout currentLayout =
                    m_ColorLayouts[attachmentIndex];
                if (currentLayout == ERHITextureLayout.Undefined ||
                    currentLayout == ERHITextureLayout.RenderTarget)
                {
                    continue;
                }
                ref RHIColorAttachmentDescriptor attachment =
                    ref m_PassDescriptor.ColorAttachments.Span[
                        attachmentIndex];
                RHIBarrier barrier = RHIBarrier.Texture(
                    attachment.RenderTarget,
                    attachment.SubresourceRange,
                    currentLayout,
                    ERHITextureLayout.RenderTarget,
                    ERHISyncStageMask.Fragment,
                    ERHISyncStageMask.Fragment,
                    AccessForColorLayout(currentLayout),
                    ERHIAccessMask.RenderTargetRead |
                        ERHIAccessMask.RenderTargetWrite);
                Dx12BarrierEmitter.EmitBarrier(commandBuffer, barrier);
                m_ColorLayouts[attachmentIndex] =
                    ERHITextureLayout.RenderTarget;
            }

            if (m_PassDescriptor.DepthStencilAttachment.HasValue &&
                m_DepthStencilLayout ==
                    ERHITextureLayout.DepthStencilReadOnly)
            {
                RHIDepthStencilAttachmentDescriptor attachment =
                    m_PassDescriptor.DepthStencilAttachment.Value;
                RHIBarrier barrier = RHIBarrier.Texture(
                    attachment.RenderTarget,
                    attachment.SubresourceRange,
                    ERHITextureLayout.DepthStencilReadOnly,
                    ERHITextureLayout.DepthStencilWrite,
                    ERHISyncStageMask.Fragment,
                    ERHISyncStageMask.Fragment,
                    ERHIAccessMask.DepthStencilRead,
                    ERHIAccessMask.DepthStencilWrite);
                Dx12BarrierEmitter.EmitBarrier(commandBuffer, barrier);
                m_DepthStencilLayout =
                    ERHITextureLayout.DepthStencilWrite;
            }
        }

        private void ResolveOmAttachments(Dx12CommandBuffer commandBuffer)
        {
            for (int attachmentIndex = 0;
                 attachmentIndex < m_PassDescriptor.ColorAttachments.Length;
                 ++attachmentIndex)
            {
                ref RHIColorAttachmentDescriptor attachment =
                    ref m_PassDescriptor.ColorAttachments.Span[
                        attachmentIndex];
                if (attachment.StoreAction != ERHIStoreAction.Resolve &&
                    attachment.StoreAction !=
                        ERHIStoreAction.StoreAndResolve)
                {
                    continue;
                }

                Dx12Texture source =
                    Dx12EncoderGuards.RequireTexture(attachment.RenderTarget)
                    ?? throw new InvalidOperationException(
                        "DX12 color resolve source belongs to a different backend.");
                Dx12Texture destination =
                    attachment.ResolveTarget is RHITexture resolveTarget ? Dx12EncoderGuards.RequireTexture(resolveTarget) : throw new InvalidOperationException("Resolve target must be a Dx12Texture.")
                    ?? throw new InvalidOperationException(
                        "DX12 color resolve destination belongs to a different backend.");
                RHIBarrier toResolve = RHIBarrier.Texture(
                    source,
                    attachment.SubresourceRange,
                    ERHITextureLayout.RenderTarget,
                    ERHITextureLayout.ResolveSource,
                    ERHISyncStageMask.Fragment,
                    ERHISyncStageMask.Transfer,
                    ERHIAccessMask.RenderTargetWrite,
                    ERHIAccessMask.ResolveRead);
                Dx12BarrierEmitter.EmitBarrier(commandBuffer, toResolve);
                for (uint layer = 0;
                     layer < attachment.SubresourceRange.ArrayLayerCount;
                     ++layer)
                {
                    commandBuffer.NativeCommandList.ResolveSubresourceRegion(
                        destination.NativeResource,
                        ComputeResolveSubresourceIndex(
                            destination,
                            attachment.ResolveSubresourceRange.BaseMipLevel,
                            attachment.ResolveSubresourceRange.BaseArrayLayer +
                                layer),
                        0,
                        0,
                        source.NativeResource,
                        ComputeResolveSubresourceIndex(
                            source,
                            attachment.SubresourceRange.BaseMipLevel,
                            attachment.SubresourceRange.BaseArrayLayer +
                                layer),
                        Dx12Utility.ConvertToDx12ViewFormat(
                            source.Descriptor.Format),
                        Vortice.Direct3D12.ResolveMode.Average);
                }
                RHIBarrier fromResolve = RHIBarrier.Texture(
                    source,
                    attachment.SubresourceRange,
                    ERHITextureLayout.ResolveSource,
                    ERHITextureLayout.RenderTarget,
                    ERHISyncStageMask.Transfer,
                    ERHISyncStageMask.Fragment,
                    ERHIAccessMask.ResolveRead,
                    ERHIAccessMask.RenderTargetRead |
                        ERHIAccessMask.RenderTargetWrite);
                Dx12BarrierEmitter.EmitBarrier(commandBuffer, fromResolve);
            }

            if (!m_PassDescriptor.DepthStencilAttachment.HasValue)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor depthStencil =
                m_PassDescriptor.DepthStencilAttachment.Value;
            if (depthStencil.DepthStoreOp != ERHIStoreAction.Resolve &&
                depthStencil.DepthStoreOp !=
                    ERHIStoreAction.StoreAndResolve &&
                depthStencil.StencilStoreOp != ERHIStoreAction.Resolve &&
                depthStencil.StencilStoreOp !=
                    ERHIStoreAction.StoreAndResolve)
            {
                return;
            }

            Dx12Texture depthSource =
                depthStencil.RenderTarget is RHITexture renderTarget ? Dx12EncoderGuards.RequireTexture(renderTarget) : throw new InvalidOperationException("Render target must be a Dx12Texture.")
                ?? throw new InvalidOperationException(
                    "DX12 depth/stencil resolve source belongs to a different backend.");
            Dx12Texture depthDestination =
                depthStencil.ResolveTarget as Dx12Texture
                ?? throw new InvalidOperationException(
                    "DX12 depth/stencil resolve destination belongs to a different backend.");
            RHIBarrier depthToResolve = RHIBarrier.Texture(
                depthSource,
                depthStencil.SubresourceRange,
                ERHITextureLayout.DepthStencilWrite,
                ERHITextureLayout.ResolveSource,
                ERHISyncStageMask.Fragment,
                ERHISyncStageMask.Transfer,
                ERHIAccessMask.DepthStencilWrite,
                ERHIAccessMask.ResolveRead);
            Dx12BarrierEmitter.EmitBarrier(commandBuffer, depthToResolve);
            ResolveDepthStencilAspect(
                commandBuffer,
                depthSource,
                depthDestination,
                depthStencil,
                isDepth: true);
            ResolveDepthStencilAspect(
                commandBuffer,
                depthSource,
                depthDestination,
                depthStencil,
                isDepth: false);
        }

        private static void ResolveDepthStencilAspect(
            Dx12CommandBuffer commandBuffer,
            Dx12Texture source,
            Dx12Texture destination,
            in RHIDepthStencilAttachmentDescriptor attachment,
            bool isDepth)
        {
            ERHIStoreAction storeAction =
                isDepth ? attachment.DepthStoreOp : attachment.StencilStoreOp;
            if (storeAction != ERHIStoreAction.Resolve &&
                storeAction != ERHIStoreAction.StoreAndResolve)
            {
                return;
            }
            uint plane = isDepth ? 0u : 1u;
            EResolveMode resolveMode =
                isDepth
                    ? attachment.DepthResolveMode
                    : attachment.StencilResolveMode;
            for (uint layer = 0;
                 layer < attachment.SubresourceRange.ArrayLayerCount;
                 ++layer)
            {
                commandBuffer.NativeCommandList.ResolveSubresourceRegion(
                    destination.NativeResource,
                    ComputeResolveSubresourceIndex(
                        destination,
                        attachment.ResolveSubresourceRange.BaseMipLevel,
                        attachment.ResolveSubresourceRange.BaseArrayLayer +
                            layer,
                        plane),
                    0,
                    0,
                    source.NativeResource,
                    ComputeResolveSubresourceIndex(
                        source,
                        attachment.SubresourceRange.BaseMipLevel,
                        attachment.SubresourceRange.BaseArrayLayer + layer,
                        plane),
                    Dx12Utility.ConvertToDx12ViewFormat(
                        source.Descriptor.Format),
                    ConvertDepthResolveMode(resolveMode));
            }
        }

        private static uint ComputeResolveSubresourceIndex(
            Dx12Texture texture,
            in uint mipLevel,
            in uint arraySlice,
            in uint planeSlice)
        {
            return checked(
                planeSlice *
                    texture.Descriptor.MipCount *
                    Math.Max(1u, texture.Descriptor.Extent.z) +
                arraySlice * texture.Descriptor.MipCount +
                mipLevel);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginOcclusion(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireOcclusionQuery(m_CommandBuffer, m_PassDescriptor.Occlusion?.Query, index);
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, index);
        }

        public override void EndOcclusion(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireOcclusionQuery(m_CommandBuffer, m_PassDescriptor.Occlusion?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, index);
        }

        public override void BeginStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireStatisticsQuery(m_CommandBuffer, m_PassDescriptor.Statistics?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            if (m_UseNativeRenderPass && m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            if (m_UseNativeRenderPass && m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void NextSubPass()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            int nextSubPassIndex = m_CurrentSubPassIndex + 1;
            if (nextSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            int sourceSubPassIndex = m_CurrentSubPassIndex;
            int destinationSubPassIndex = nextSubPassIndex;
            if (Lowering.Strategy !=
                EDx12RasterPassStrategy.OmMultipass ||
                destinationSubPassIndex != sourceSubPassIndex + 1)
            {
                throw new InvalidOperationException(
                    "DX12 subpass advancement does not match the compiled OM multipass plan.");
            }

            Dx12CommandBuffer commandBuffer =
                Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            ref readonly Dx12RasterSubPassLowering source =
                ref Lowering.SubPasses.Span[sourceSubPassIndex];
            ref readonly Dx12RasterSubPassLowering destination =
                ref Lowering.SubPasses.Span[destinationSubPassIndex];
            EmitRasterOrderPhaseBarriers(
                commandBuffer,
                plan,
                in source,
                in destination);
            BindOmSubPass(commandBuffer, plan, destinationSubPassIndex);
            m_CurrentSubPassIndex = nextSubPassIndex;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;
        }

        private static void EmitRasterOrderPhaseBarriers(
            Dx12CommandBuffer commandBuffer,
            RasterPassPlan plan,
            in Dx12RasterSubPassLowering source,
            in Dx12RasterSubPassLowering destination)
        {
            byte sharedRasterOrderedMask = checked((byte)(
                source.RasterOrderedMask &
                destination.RasterOrderedMask));
            if (sharedRasterOrderedMask == 0)
            {
                return;
            }

            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit = checked((byte)(1 << logicalAttachment));
                if ((sharedRasterOrderedMask & bit) == 0)
                {
                    continue;
                }

                Dx12Texture texture = plan
                    .GetColorAttachment(logicalAttachment)
                    .RenderTarget is RHITexture renderTarget ? Dx12EncoderGuards.RequireTexture(renderTarget) : throw new InvalidOperationException("Render target must be a Dx12Texture.")
                    ?? throw new InvalidOperationException(
                        $"DX12 ROV attachment {logicalAttachment} belongs " +
                        "to a different backend.");
                Dx12BarrierEmitter
                    .EmitUnorderedAccessOrderingBarrier(
                        commandBuffer,
                        texture);
            }
        }

        public override void SetScissor(in Rect rect)
        {
            Vortice.RawRect tempScissor = new Vortice.RawRect((int)rect.left, (int)rect.top, (int)rect.right, (int)rect.bottom);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.RSSetScissorRects(new[] { tempScissor });
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            Span<Rect> rectSpan = rects.Span;
            Vortice.RawRect[] tempScissors = new Vortice.RawRect[rectSpan.Length];
            for (int i = 0; i < rectSpan.Length; ++i)
            {
                tempScissors[i] = new Vortice.RawRect((int)rectSpan[i].left, (int)rectSpan[i].top, (int)rectSpan[i].right, (int)rectSpan[i].bottom);
            }
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.RSSetScissorRects((uint)rectSpan.Length, tempScissors);
        }

        public override void SetViewport(in Viewport viewport)
        {
            Vortice.Mathematics.Viewport tempViewport = new Vortice.Mathematics.Viewport(viewport.TopLeftX, viewport.TopLeftY, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.RSSetViewports(new[] { tempViewport });
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            Span<Viewport> viewportSpan = viewports.Span;
            Vortice.Mathematics.Viewport[] tempViewports = new Vortice.Mathematics.Viewport[viewportSpan.Length];
            for (int i = 0; i < viewportSpan.Length; ++i)
            {
                tempViewports[i] = new Vortice.Mathematics.Viewport(viewportSpan[i].TopLeftX, viewportSpan[i].TopLeftY, viewportSpan[i].Width, viewportSpan[i].Height, viewportSpan[i].MinDepth, viewportSpan[i].MaxDepth);
            }
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.RSSetViewports((uint)viewportSpan.Length, tempViewports);
        }

        public override void SetStencilRef(in uint value)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.OMSetStencilRef(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            float4 tempValue = value;
            Vortice.Mathematics.Color4 nativeBlendFactor = new Vortice.Mathematics.Color4(tempValue.x, tempValue.y, tempValue.z, tempValue.w);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.OMSetBlendFactor(nativeBlendFactor);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            ValidatePipelineCompatibility(plan, m_CurrentSubPassIndex, pipeline);
            m_CachedPipeline = pipeline;

            Dx12RasterPipeline dx12Pipeline = Dx12EncoderGuards.RequireRasterPipeline(pipeline);
            Dx12PipelineLayout dx12PipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(pipeline.DescriptorInternal.PipelineLayout);

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList.IASetPrimitiveTopology(dx12Pipeline.PrimitiveTopology);
            dx12CommandBuffer.NativeCommandList.SetGraphicsRootSignature(
                dx12Pipeline.NativeRootSignature);

            ref readonly Dx12RasterSubPassLowering subPass =
                ref Lowering.SubPasses.Span[CurrentSubPassIndex];
            if (subPass.RequiresPrivateAttachmentTable)
            {
                if (!m_HasPrivateAttachmentDescriptors ||
                    !dx12Pipeline.HasPrivateAttachmentRootSignature)
                {
                    throw new InvalidOperationException(
                        "DX12 raster attachment reads require the backend-private raster root signature and descriptor table.");
                }

                Dx12Device device =
                    Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
                int descriptorOffset = Dx12RasterPassLowering
                    .GetPrivateDescriptorTableOffset(
                        CurrentSubPassIndex);
                if (descriptorOffset < 0 ||
                    descriptorOffset +
                        Dx12RasterPassLowering
                            .PrivateDescriptorCountPerSubPass >
                        m_PrivateAttachmentDescriptorCount)
                {
                    throw new InvalidOperationException(
                        "DX12 private raster descriptor phase is outside " +
                        "the immutable pass allocation.");
                }
                dx12CommandBuffer.NativeCommandList
                    .SetGraphicsRootDescriptorTable(
                        dx12Pipeline.AttachmentRootParameterIndex,
                        new Vortice.Direct3D12.GpuDescriptorHandle(
                            in m_PrivateAttachmentDescriptors.GpuHandle,
                            descriptorOffset,
                            device.DescriptorHeapCbvSrvUav
                                .DescriptorSize));
            }

            m_PipelineSubPassIndex = m_CurrentSubPassIndex;
        }

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequireCachedRasterPipelineLayout(m_CachedPipeline);
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BindingTableBinder.BindGraphics(commandBuffer.NativeCommandList, pipelineLayout, resourceTable, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequireCachedRasterPipelineLayout(m_CachedPipeline);
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            if (!Dx12BindingTableBinder.ValidatePushConstantWrite(pipelineLayout, data, size, offset))
            {
                return;
            }
            commandBuffer.NativeCommandList.SetGraphicsRoot32BitConstants(pipelineLayout.PushConstantRootParameterIndex, size / 4, data.ToPointer(), offset / 4);
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(buffer);
            Vortice.Direct3D12.IndexBufferView indexBufferView = new Vortice.Direct3D12.IndexBufferView
            {
                Format = Dx12Utility.ConvertToDx12IndexFormat(buffer.Descriptor.Format),
                SizeInBytes = (uint)buffer.Descriptor.ByteSize - offset,
                BufferLocation = dx12Buffer.NativeResource.GPUVirtualAddress + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.IASetIndexBuffer(&indexBufferView);
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot = 0, in uint offset = 0)
        {
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(buffer);
            Dx12RasterPipeline dx12Pipeline = Dx12EncoderGuards.RequireRasterPipeline(m_CachedPipeline);

            Vortice.Direct3D12.VertexBufferView vertexBufferView = new Vortice.Direct3D12.VertexBufferView
            {
                SizeInBytes = (uint)buffer.Descriptor.ByteSize - offset,
                StrideInBytes = dx12Pipeline.VertexStrides[slot],
                BufferLocation = dx12Buffer.NativeResource.GPUVirtualAddress + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.IASetVertexBuffers(slot, 1, &vertexBufferView);
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
            Vortice.Direct3D12.ShadingRateCombiner nativeShadingRateCombiner = Dx12Utility.ConvertToDx12ShadingRateCombiner(shadingRateCombiner);
            Vortice.Direct3D12.ShadingRateCombiner[] shadingRateCombiners = new[] { nativeShadingRateCombiner, nativeShadingRateCombiner };
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(shadingRate), shadingRateCombiners);
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(argsBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(argsBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndexedIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            dx12Device.Capabilities.Mesh.Shader.Require("DX12 mesh shaders");
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.DispatchMesh(groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(argsBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            dx12Device.Capabilities.Mesh.Shader.Require("DX12 mesh shaders");
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchMeshIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            ValidateDrawState();
            EnsureNativeRenderPassActive();
            Dx12RasterIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12RasterIndirectCommandBuffer ?? throw new InvalidOperationException("DX12 raster indirect draw requires a Dx12RasterIndirectCommandBuffer.");
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
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

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
                _ = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            if (m_UseNativeRenderPass)
            {
                EnsureNativeRenderPassActive();
            }
            if (m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            else if (Lowering.Strategy ==
                     EDx12RasterPassStrategy.OmMultipass)
            {
                EndOmRasterPass(dx12CommandBuffer);
            }
            m_UseNativeRenderPass = false;
            m_NativeRenderPassColorDescriptions = Array.Empty<Vortice.Direct3D12.RenderPassRenderTargetDescription>();
            m_NativeRenderPassDepthStencilDescription = null;

            Dx12Device device =
                Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            ReleaseAttachmentDescriptors(device);
            ResetRasterDescriptorState();
            commandBuffer.MarkEncoderEndFromEncoder();
            ClearRasterPassState();
        }

        private void ReleaseAttachmentDescriptors(Dx12Device device)
        {
            for (int i = 0; i < m_AttachmentInfos.Count; ++i)
            {
                int index =
                    m_AttachmentInfos[i].AttachmentInfo.Index;
                if (m_AttachmentInfos[i].bDepthStencil)
                {
                    device.FreeDsvDescriptor(index);
                }
                else
                {
                    device.FreeRtvDescriptor(index);
                }
            }
            m_AttachmentInfos.Clear();
        }

        private void ResetRasterDescriptorState()
        {
            m_RtvHandles =
                Array.Empty<Vortice.Direct3D12.CpuDescriptorHandle>();
            m_OmSubPassRenderTargetHandles =
                Array.Empty<Vortice.Direct3D12.CpuDescriptorHandle[]>();
            m_DsvHandle = null;
            m_ReadOnlyDsvHandle = null;
            m_PrivateAttachmentDescriptors = default;
            m_PrivateAttachmentDescriptorCount = 0;
            m_HasPrivateAttachmentDescriptors = false;
            m_PassDescriptor = default;
            Array.Clear(m_ColorLayouts);
            m_DepthStencilLayout = ERHITextureLayout.Undefined;
            m_UseNativeRenderPass = false;
            m_IsNativeRenderPassActive = false;
            m_NativeRenderPassColorDescriptions =
                Array.Empty<Vortice.Direct3D12
                    .RenderPassRenderTargetDescription>();
            m_NativeRenderPassDepthStencilDescription = null;
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12MLEncoder : RHIMLEncoder
    {
        private RHIMLPassDescriptor m_PassDescriptor;

        private static readonly RHIBufferRange s_WholeBufferRange = RHIBufferRange.Whole();

#if DEBUG
        private bool m_PipelineSet;
#endif

        public Dx12MLEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
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
            }        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
#if DEBUG
            m_PipelineSet = true;
#endif
            m_CachedPipeline = pipeline as Dx12MLPipeline
                ?? throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12MLPipeline)} but got {pipeline?.GetType().Name ?? "<null>"}.");
        }

        public override void SetBindingTable(RHIMLBindingTable bindingTable)
        {
            if (bindingTable is not Dx12MLBindingTable dx12BindingTable)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12MLBindingTable)} but got {bindingTable?.GetType().Name ?? "<null>"}.");
            }

            m_CachedBindingTable = dx12BindingTable;
        }

        public override void Dispatch()
        {
            if (m_CachedPipeline is not Dx12MLPipeline dx12Pipeline)
            {
                throw new InvalidOperationException("Dx12MLEncoder: SetPipeline must be called before Dispatch.");
            }

            if (m_CachedBindingTable is not Dx12MLBindingTable dx12BindingTable)
            {
                throw new InvalidOperationException("Dx12MLEncoder: SetBindingTable must be called before Dispatch.");
            }

            Dx12CommandBuffer dx12CommandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer);
            Dx12Device dx12Device = Dx12EncoderGuards.RequireDevice(dx12CommandBuffer);

            if (!dx12BindingTable.InternalResourcesPrepared)
            {
                TransitionBoundResourcesToMachineLearning(dx12CommandBuffer, dx12BindingTable);
                dx12BindingTable.MarkInternalResourcesPrepared();
            }
            else
            {
                EmitMachineLearningUavBarriers(
                    dx12CommandBuffer,
                    CollectIntermediateAndPersistentTemporaryBuffers(dx12BindingTable));
            }

            if (!dx12BindingTable.IsInitialized)
            {
                dx12BindingTable.PrepareForInitialization();
                dx12Device.DirectMLCommandRecorder.RecordDispatch(dx12CommandBuffer.NativeCommandList, dx12Pipeline.OperatorInitializer, dx12BindingTable.InitializerBindingTable);
                EmitMachineLearningUavBarriers(dx12CommandBuffer, dx12BindingTable.PersistentBuffer, dx12BindingTable.TemporaryBuffer);
                dx12BindingTable.MarkInitialized();
            }

            for (int stageIndex = 0; stageIndex < dx12Pipeline.StageCount; ++stageIndex)
            {
                dx12BindingTable.PrepareForExecution(stageIndex);
                dx12Device.DirectMLCommandRecorder.RecordDispatch(dx12CommandBuffer.NativeCommandList, dx12Pipeline.GetCompiledOperator(stageIndex), dx12BindingTable.GetExecutionBindingTable(stageIndex));
                if (stageIndex + 1 < dx12Pipeline.StageCount)
                {
                    EmitMachineLearningUavBarriers(
                        dx12CommandBuffer,
                        CollectIntermediateAndPersistentTemporaryBuffers(dx12BindingTable));
                }
            }
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The machine-learning encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.MachineLearning);
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            m_CachedBindingTable = null;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        protected override void Release()
        {

        }

        private static void TransitionBoundResourcesToMachineLearning(Dx12CommandBuffer commandBuffer, Dx12MLBindingTable bindingTable)
        {
            Dx12Buffer[] intermediates = bindingTable.IntermediateBuffers;
            int barrierCount = (bindingTable.TemporaryBuffer != null ? 1 : 0)
                + (bindingTable.PersistentBuffer != null ? 1 : 0)
                + intermediates.Length;
            if (barrierCount == 0)
            {
                return;
            }

            RHIBarrier[] barriers = new RHIBarrier[barrierCount];
            int index = 0;

            if (bindingTable.TemporaryBuffer != null)
            {
                barriers[index++] = CreateMachineLearningBufferBarrier(bindingTable.TemporaryBuffer);
            }

            if (bindingTable.PersistentBuffer != null)
            {
                barriers[index++] = CreateMachineLearningBufferBarrier(bindingTable.PersistentBuffer);
            }

            for (int i = 0; i < intermediates.Length; ++i)
            {
                barriers[index++] = CreateMachineLearningBufferBarrier(intermediates[i]);
            }

            Dx12BarrierEmitter.EmitBarriers(commandBuffer, barriers);
        }

        private static Dx12Buffer?[] CollectIntermediateAndPersistentTemporaryBuffers(Dx12MLBindingTable bindingTable)
        {
            Dx12Buffer[] intermediates = bindingTable.IntermediateBuffers;
            int count = intermediates.Length + (bindingTable.PersistentBuffer != null ? 1 : 0) + (bindingTable.TemporaryBuffer != null ? 1 : 0);
            if (count == 0)
            {
                return Array.Empty<Dx12Buffer?>();
            }

            Dx12Buffer?[] buffers = new Dx12Buffer?[count];
            int index = 0;
            for (int i = 0; i < intermediates.Length; ++i)
            {
                buffers[index++] = intermediates[i];
            }

            if (bindingTable.PersistentBuffer != null)
            {
                buffers[index++] = bindingTable.PersistentBuffer;
            }

            if (bindingTable.TemporaryBuffer != null)
            {
                buffers[index++] = bindingTable.TemporaryBuffer;
            }

            return buffers;
        }

        private static RHIBarrier CreateMachineLearningBufferBarrier(RHIBuffer buffer)
        {
            return RHIBarrier.Buffer(
                buffer,
                s_WholeBufferRange,
                ERHISyncStageMask.None,
                ERHISyncStageMask.MachineLearning,
                ERHIAccessMask.None,
                ERHIAccessMask.ShaderWrite);
        }

        private static void EmitMachineLearningUavBarriers(Dx12CommandBuffer commandBuffer, params Dx12Buffer?[] buffers)
        {
            if (buffers.Length == 0)
            {
                return;
            }

            int barrierCount = 0;
            for (int i = 0; i < buffers.Length; ++i)
            {
                if (buffers[i] != null)
                {
                    ++barrierCount;
                }
            }

            if (barrierCount == 0)
            {
                return;
            }

            Vortice.Direct3D12.ResourceBarrier[] barriers = new Vortice.Direct3D12.ResourceBarrier[barrierCount];
            int index = 0;
            for (int i = 0; i < buffers.Length; ++i)
            {
                if (buffers[i] == null)
                {
                    continue;
                }

                barriers[index++] = Vortice.Direct3D12.ResourceBarrier.BarrierUnorderedAccessView(buffers[i]!.NativeResource);
            }

            ((Vortice.Direct3D12.ID3D12GraphicsCommandList)commandBuffer.NativeCommandList).ResourceBarrier(barriers);
        }
    }
#pragma warning restore CS0414, CA1416
    internal unsafe sealed class Dx12WorkGraphEncoder : RHIWorkGraphEncoder
    {
        private RHIWorkGraphPassDescriptor m_PassDescriptor;
        private Vortice.Direct3D12.ID3D12GraphicsCommandList10? m_CommandList10;
        private Dx12Buffer? m_BackingMemory;
        private ulong m_BackingMemoryGpuAddress;
        private ulong m_BackingMemorySize;
        private bool m_BackingMemoryInitialized;
        private bool m_WorkGraphProgramSet;
        private Dx12Buffer? m_NodeInputDescriptorUpload;
        private IntPtr m_NodeInputDescriptorUploadPtr;
        private ulong m_NodeInputDescriptorUploadGpuAddress;

        internal Dx12WorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            Dx12CommandBuffer commandBuffer = (Dx12CommandBuffer)m_CommandBuffer!;
            m_CommandList10 ??= commandBuffer.NativeCommandList.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12GraphicsCommandList10>()
                ?? throw new NotSupportedException("DX12 WorkGraph requires ID3D12GraphicsCommandList10 support.");
            m_PassDescriptor = descriptor;
            m_CachedPipeline = null;
            m_BackingMemory = null;
            m_BackingMemoryGpuAddress = 0;
            m_BackingMemorySize = 0;
            m_BackingMemoryInitialized = false;
            m_WorkGraphProgramSet = false;

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }

            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12BarrierEmitter.EmitBarrier((Dx12CommandBuffer)m_CommandBuffer!, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12BarrierEmitter.EmitBarriers((Dx12CommandBuffer)m_CommandBuffer!, barriers);
        }
        public override void PushDebugGroup(string name)
        {
            Dx12PixEventMarker.BeginEvent(((Dx12CommandBuffer)m_CommandBuffer!).NativeCommandList.NativePointer, name);
        }

        public override void PopDebugGroup()
        {
            Dx12PixEventMarker.EndEvent(((Dx12CommandBuffer)m_CommandBuffer!).NativeCommandList.NativePointer);
        }

        public override void WriteTimestamp(in uint index)
        {
            (Dx12Query dx12Query, Dx12CommandBuffer dx12CommandBuffer) =
                Dx12QueryEncoderValidation.RequireTimestampQuery(m_CommandBuffer!, m_PassDescriptor.Timestamp?.Query, index);
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void SetPipeline(RHIWorkGraphPipeline pipeline)
        {
            m_CachedPipeline = pipeline as Dx12WorkGraphPipeline ?? throw new InvalidOperationException("DX12 WorkGraph encoder requires a Dx12WorkGraphPipeline.")
                ?? throw new InvalidOperationException("DX12 WorkGraph encoder requires a Dx12WorkGraphPipeline.");

            Dx12PipelineLayout dx12PipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(pipeline.Descriptor.PipelineLayout)
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline requires a Dx12PipelineLayout.");
            ((Dx12CommandBuffer)m_CommandBuffer!).NativeCommandList.SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
            m_WorkGraphProgramSet = false;
        }

        public override void SetBindingTable(RHIBindingTable resourceTable, in uint tableIndex)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(RequirePipeline().Descriptor.PipelineLayout)
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline requires a Dx12PipelineLayout.");
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer)
                ?? throw new InvalidOperationException("DX12 WorkGraph encoder requires a Dx12CommandBuffer.");
            Dx12BindingTableBinder.BindCompute(commandBuffer.NativeCommandList, pipelineLayout, resourceTable, tableIndex);
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            Dx12PipelineLayout pipelineLayout = Dx12EncoderGuards.RequirePipelineLayout(RequirePipeline().Descriptor.PipelineLayout)
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline requires a Dx12PipelineLayout.");
            Dx12CommandBuffer commandBuffer = Dx12EncoderGuards.RequireCommandBuffer(m_CommandBuffer)
                ?? throw new InvalidOperationException("DX12 WorkGraph encoder requires a Dx12CommandBuffer.");
            if (!Dx12BindingTableBinder.ValidatePushConstantWrite(pipelineLayout, data, size, offset))
            {
                return;
            }
            commandBuffer.NativeCommandList.SetComputeRoot32BitConstants(
                pipelineLayout.PushConstantRootParameterIndex,
                size / 4,
                data.ToPointer(),
                offset / 4);
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            Dx12WorkGraphPipeline pipeline = RequirePipeline();
            Dx12Buffer dx12Buffer = Dx12EncoderGuards.RequireBuffer(backingMemory)
                ?? throw new InvalidOperationException("DX12 WorkGraph backing memory requires a Dx12Buffer.");
            if (byteOffset > (ulong)dx12Buffer.Descriptor.ByteSize || byteSize > (ulong)dx12Buffer.Descriptor.ByteSize - byteOffset)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), "WorkGraph backing memory range exceeds buffer bounds.");
            }

            Vortice.Direct3D12.WorkGraphMemoryRequirements requirements = pipeline.NativeMemoryRequirements;
            if (byteSize < requirements.MinSizeInBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), $"WorkGraph backing memory is smaller than the required minimum size ({requirements.MinSizeInBytes} bytes).");
            }

            uint granularity = requirements.SizeGranularityInBytes;
            if (granularity != 0 && (byteSize % granularity) != 0)
            {
                throw new ArgumentException($"WorkGraph backing memory size must be aligned to {granularity} bytes.", nameof(byteSize));
            }

            bool sameBackingRange = ReferenceEquals(m_BackingMemory, dx12Buffer)
                && m_BackingMemoryGpuAddress == dx12Buffer.NativeResource.GPUVirtualAddress + byteOffset
                && m_BackingMemorySize == byteSize;
            m_BackingMemoryInitialized = sameBackingRange && m_BackingMemoryInitialized;
            m_BackingMemory = dx12Buffer;
            m_BackingMemoryGpuAddress = dx12Buffer.NativeResource.GPUVirtualAddress + byteOffset;
            m_BackingMemorySize = byteSize;
            m_WorkGraphProgramSet = false;
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            if (numRecords == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(numRecords),
                    "Work-graph dispatch record count must be non-zero.");
            }

            Dx12WorkGraphPipeline pipeline = RequirePipeline();
            Vortice.Direct3D12.ID3D12GraphicsCommandList10 commandList10 = m_CommandList10
                ?? throw new InvalidOperationException("DX12 WorkGraph command list is unavailable.");
            if (m_BackingMemory == null)
            {
                throw new InvalidOperationException("WorkGraph backing memory must be set before DispatchGraph.");
            }
            if (inputRecordBuffer == null)
            {
                throw new ArgumentNullException(nameof(inputRecordBuffer), "DX12 WorkGraph GPU input mode requires an input record buffer when numRecords is greater than zero.");
            }
            if (inputRecordByteStride == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputRecordByteStride), "WorkGraph input record stride must be non-zero.");
            }

            Dx12Buffer dx12InputBuffer = Dx12EncoderGuards.RequireBuffer(inputRecordBuffer)
                ?? throw new InvalidOperationException("DX12 WorkGraph input records require a Dx12Buffer.");
            ulong requiredInputBytes = checked((ulong)numRecords * inputRecordByteStride);
            if (requiredInputBytes > (ulong)dx12InputBuffer.Descriptor.ByteSize)
            {
                throw new ArgumentOutOfRangeException(nameof(numRecords), "WorkGraph input record range exceeds buffer size.");
            }

            EnsureNodeInputDescriptorUpload();
            Vortice.Direct3D12.NodeGpuInput nodeInput = new Vortice.Direct3D12.NodeGpuInput
            {
                EntrypointIndex = pipeline.GetEntrypointIndex(entrypoint),
                NumRecords = numRecords,
                Records = new Vortice.Direct3D12.GpuVirtualAddressAndStride
                {
                    StartAddress = dx12InputBuffer.NativeResource.GPUVirtualAddress,
                    StrideInBytes = inputRecordByteStride
                }
            };
            Unsafe.Write(m_NodeInputDescriptorUploadPtr.ToPointer(), nodeInput);

            if (!m_WorkGraphProgramSet)
            {
                Vortice.Direct3D12.SetWorkGraphDescription workGraphDescription = new Vortice.Direct3D12.SetWorkGraphDescription
                {
                    ProgramIdentifier = pipeline.ProgramIdentifier,
                    Flags = m_BackingMemoryInitialized ? Vortice.Direct3D12.SetWorkGraphFlags.None : Vortice.Direct3D12.SetWorkGraphFlags.Initialize,
                    BackingMemory = new Vortice.Direct3D12.GpuVirtualAddressRange
                    {
                        StartAddress = m_BackingMemoryGpuAddress,
                        SizeInBytes = m_BackingMemorySize
                    }
                };
                commandList10.SetWorkGraphProgram(in workGraphDescription);
                m_BackingMemoryInitialized = true;
                m_WorkGraphProgramSet = true;
            }

            Vortice.Direct3D12.DispatchGraphDescription dispatchDescription = new Vortice.Direct3D12.DispatchGraphDescription
            {
                Mode = Vortice.Direct3D12.DispatchMode.NodeGpuInput
            };
            dispatchDescription.NodeGPUInput = m_NodeInputDescriptorUploadGpuAddress;
            commandList10.DispatchGraph(ref dispatchDescription);
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The work-graph encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.WorkGraph);
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }

            if (!string.IsNullOrWhiteSpace(m_PassDescriptor.Name))
            {
                PopDebugGroup();
            }

            m_CachedPipeline = null;
            m_BackingMemory = null;
            m_BackingMemoryGpuAddress = 0;
            m_BackingMemorySize = 0;
            m_WorkGraphProgramSet = false;
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal void ReleaseCommandListInterface()
        {
            Vortice.Direct3D12.ID3D12GraphicsCommandList10? commandList = m_CommandList10;
            m_CommandList10 = null;
            commandList?.Release();
        }

        protected override void Release()
        {
            if (m_NodeInputDescriptorUploadPtr != IntPtr.Zero && m_NodeInputDescriptorUpload != null)
            {
                m_NodeInputDescriptorUpload.UnMap(0, (uint)Unsafe.SizeOf<Vortice.Direct3D12.NodeGpuInput>());
                m_NodeInputDescriptorUploadPtr = IntPtr.Zero;
            }

            m_NodeInputDescriptorUpload?.Dispose();
            m_NodeInputDescriptorUpload = null;
            ReleaseCommandListInterface();
        }

        private Dx12WorkGraphPipeline RequirePipeline()
        {
            return m_CachedPipeline as Dx12WorkGraphPipeline ?? throw new InvalidOperationException("DX12 WorkGraph encoder requires a bound Dx12WorkGraphPipeline.")
                ?? throw new InvalidOperationException("DX12 WorkGraph pipeline must be set before this operation.");
        }

        private void EnsureNodeInputDescriptorUpload()
        {
            if (m_NodeInputDescriptorUpload != null)
            {
                return;
            }

            Dx12Device device = Dx12EncoderGuards.RequireDevice(m_CommandBuffer);
            int byteSize = Unsafe.SizeOf<Vortice.Direct3D12.NodeGpuInput>();
            m_NodeInputDescriptorUpload = new Dx12Buffer(device, new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                UsageFlag = ERHIBufferUsage.CopySrc,
                StorageMode = ERHIStorageMode.HostUpload
            });
            m_NodeInputDescriptorUploadPtr = m_NodeInputDescriptorUpload.Map(0, 0);
            m_NodeInputDescriptorUploadGpuAddress = m_NodeInputDescriptorUpload.NativeResource.GPUVirtualAddress;
        }
    }


    #region PixMarkers
internal static unsafe class Dx12PixEventMarker
    {
#if DEBUG
        private const string PixRuntimeFileName = "WinPixEventRuntime.dll";
        private static readonly object s_Sync = new();
        private static readonly object s_EventDepthSync = new();
        private static bool s_RuntimeLoadAttempted;
        private static bool s_RuntimeAvailable;
        private static nint s_RuntimeHandle;
        private static readonly Dictionary<nint, int> s_EventDepthByCommandList = new();

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXBeginEventOnCommandList(nint commandList, ulong color, [MarshalAs(UnmanagedType.LPStr)] string formatString);

        [DllImport(PixRuntimeFileName, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void PIXEndEventOnCommandList(nint commandList);
#endif

        public static void BeginEvent(nint commandList, string name)
        {
#if DEBUG
            if (commandList == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Unnamed";
            }

            if (!EnsureRuntimeLoaded())
            {
                return;
            }

            try
            {
                PIXBeginEventOnCommandList(commandList, 0UL, name);
                lock (s_EventDepthSync)
                {
                    s_EventDepthByCommandList.TryGetValue(commandList, out int depth);
                    s_EventDepthByCommandList[commandList] = depth + 1;
                }
            }
            catch
            {
            }
#endif
        }

        public static void EndEvent(nint commandList)
        {
#if DEBUG
            if (commandList == 0 || !EnsureRuntimeLoaded())
            {
                return;
            }

            bool hasEventDepth = false;
            lock (s_EventDepthSync)
            {
                if (s_EventDepthByCommandList.TryGetValue(commandList, out int depth) && depth > 0)
                {
                    depth--;
                    if (depth == 0)
                    {
                        s_EventDepthByCommandList.Remove(commandList);
                    }
                    else
                    {
                        s_EventDepthByCommandList[commandList] = depth;
                    }
                    hasEventDepth = true;
                }
            }

            if (!hasEventDepth)
            {
                return;
            }

            try
            {
                PIXEndEventOnCommandList(commandList);
            }
            catch
            {
            }
#endif
        }

#if DEBUG
        private static bool EnsureRuntimeLoaded()
        {
            lock (s_Sync)
            {
                if (s_RuntimeLoadAttempted)
                {
                    return s_RuntimeAvailable;
                }

                s_RuntimeLoadAttempted = true;
                ThirdPartyNativeLibraryResolver.EnsureResolverRegistered(typeof(Dx12PixEventMarker).Assembly);

                if (ThirdPartyNativeLibraryResolver.TryResolve(PixRuntimeFileName, out s_RuntimeHandle, out string? runtimePath))
                {
                    s_RuntimeAvailable = true;
                    Debug.WriteLine($"[Dx12PixEventMarker] PIX runtime loaded from '{runtimePath ?? PixRuntimeFileName}'");
                    return true;
                }

                string candidateList = string.Join(", ", ThirdPartyNativeLibraryResolver.EnumerateCandidates(PixRuntimeFileName));
                if (string.IsNullOrWhiteSpace(candidateList))
                {
                    candidateList = "(none)";
                }

                Debug.WriteLine($"[Dx12PixEventMarker] PIX runtime unavailable, markers disabled. Candidates: {candidateList}");
                return false;
            }
        }
#endif
    }
    #endregion

    #region RasterPass
internal enum EDx12RasterPassStrategy
    {
        NativeRenderPass = 0,
        OmMultipass = 1,
    }

    internal readonly struct Dx12RasterSubPassLowering
    {
        internal byte RenderTargetMask { get; }
        internal byte ShaderResourceMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PrivateShaderResourceMask { get; }
        internal byte PreserveMask { get; }
        internal byte TransitionMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool RequiresPrivateAttachmentTable =>
            PrivateShaderResourceMask != 0 ||
            RasterOrderedMask != 0;

        internal ReadOnlyMemory<int> RenderTargetLogicalAttachments =>
            m_RenderTargetLogicalAttachments;
        internal ReadOnlyMemory<int> OutputLogicalAttachments =>
            m_OutputLogicalAttachments;
        internal ReadOnlyMemory<int> PrivateInputLogicalAttachments =>
            m_PrivateInputLogicalAttachments;
        internal ReadOnlyMemory<int> SampledFeedbackLogicalAttachments =>
            m_SampledFeedbackLogicalAttachments;
        internal ReadOnlyMemory<ERHIPixelFormat> OutputLocationFormats =>
            m_OutputLocationFormats;
        internal bool HasSparseOutputLocations { get; }

        private readonly int[] m_RenderTargetLogicalAttachments;
        private readonly int[] m_OutputLogicalAttachments;
        private readonly int[] m_PrivateInputLogicalAttachments;
        private readonly int[] m_SampledFeedbackLogicalAttachments;
        private readonly ERHIPixelFormat[] m_OutputLocationFormats;

        internal Dx12RasterSubPassLowering(
            in RasterSubPassPlan plan,
            ReadOnlySpan<ERHIPixelFormat> logicalAttachmentFormats)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            RenderTargetMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~rasterOrderedMask));
            ShaderResourceMask = checked((byte)(
                (attachmentInterface.ColorInputMask |
                 attachmentInterface.SampledFeedbackMask) &
                ~rasterOrderedMask));
            RasterOrderedMask = rasterOrderedMask;
            PrivateShaderResourceMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~rasterOrderedMask));
            PreserveMask = plan.PreserveMask;
            TransitionMask = plan.TransitionMask;
            DepthStencilFlags = attachmentInterface.DepthStencilFlags;

            m_RenderTargetLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            m_OutputLogicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            bool hasSparseOutputLocations = false;
            for (int outputLocation = 0;
                 outputLocation < m_RenderTargetLogicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                m_OutputLogicalAttachments[outputLocation] =
                    logicalAttachment;
                hasSparseOutputLocations |=
                    logicalAttachment ==
                    RHIAttachmentInterfaceSignature
                        .UnboundLogicalAttachment;
                if (logicalAttachment >= 0 &&
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_RenderTargetLogicalAttachments[outputLocation] =
                    logicalAttachment;
            }
            HasSparseOutputLocations = hasSparseOutputLocations;
            m_OutputLocationFormats = ResolveOutputLocationFormats(
                in attachmentInterface,
                logicalAttachmentFormats);

            m_PrivateInputLogicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            for (int inputIndex = 0;
                 inputIndex < m_PrivateInputLogicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment >= 0 &&
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                m_PrivateInputLogicalAttachments[inputIndex] =
                    logicalAttachment;
            }

            m_SampledFeedbackLogicalAttachments =
                new int[attachmentInterface.SampledFeedbackSlotCount];
            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    m_SampledFeedbackLogicalAttachments.Length;
                 ++sampledOrdinal)
            {
                m_SampledFeedbackLogicalAttachments[sampledOrdinal] =
                    attachmentInterface
                        .GetSampledFeedbackLogicalAttachment(
                            sampledOrdinal);
            }
        }

        internal int GetRenderTargetLogicalAttachment(
            int outputLocation) =>
            m_RenderTargetLogicalAttachments[outputLocation];

        internal int GetOutputLogicalAttachment(int outputLocation) =>
            m_OutputLogicalAttachments[outputLocation];

        internal int GetPrivateInputLogicalAttachment(int inputIndex) =>
            m_PrivateInputLogicalAttachments[inputIndex];

        internal int GetSampledFeedbackLogicalAttachment(
            int sampledOrdinal) =>
            m_SampledFeedbackLogicalAttachments[sampledOrdinal];

        internal bool HasIdentityOutputMapping(int logicalAttachmentCount)
        {
            if (m_OutputLogicalAttachments.Length !=
                logicalAttachmentCount)
            {
                return false;
            }

            for (int outputLocation = 0;
                 outputLocation < m_OutputLogicalAttachments.Length;
                 ++outputLocation)
            {
                if (m_OutputLogicalAttachments[outputLocation] !=
                    outputLocation)
                {
                    return false;
                }
            }
            return true;
        }

        internal static ERHIPixelFormat[] ResolveOutputLocationFormats(
            in RHIAttachmentInterfaceSignature attachmentInterface,
            ReadOnlySpan<ERHIPixelFormat> logicalAttachmentFormats)
        {
            if (logicalAttachmentFormats.Length !=
                attachmentInterface.ColorAttachmentCount)
            {
                throw new ArgumentException(
                    "DX12 output format lowering requires one format per " +
                    "logical color attachment.",
                    nameof(logicalAttachmentFormats));
            }

            int outputLocationCount =
                attachmentInterface.ColorOutputLocationCount;
            if (outputLocationCount == 0)
            {
                return Array.Empty<ERHIPixelFormat>();
            }

            int anchorLogicalAttachment =
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment;
            for (int outputLocation = 0;
                 outputLocation < outputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (logicalAttachment >= 0)
                {
                    anchorLogicalAttachment = logicalAttachment;
                    break;
                }
            }

            if (anchorLogicalAttachment < 0)
            {
                throw new ArgumentException(
                    "A non-empty DX12 output-location interface must bind " +
                    "at least one logical color attachment.",
                    nameof(attachmentInterface));
            }

            ERHIPixelFormat anchorFormat =
                logicalAttachmentFormats[anchorLogicalAttachment];
            ValidateOutputFormat(anchorFormat, anchorLogicalAttachment);
            ERHIPixelFormat[] outputFormats =
                new ERHIPixelFormat[outputLocationCount];
            for (int outputLocation = 0;
                 outputLocation < outputFormats.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                ERHIPixelFormat format = logicalAttachment >= 0
                    ? logicalAttachmentFormats[logicalAttachment]
                    : anchorFormat;
                ValidateOutputFormat(
                    format,
                    logicalAttachment >= 0
                        ? logicalAttachment
                        : anchorLogicalAttachment);
                outputFormats[outputLocation] = format;
            }
            return outputFormats;
        }

        private static void ValidateOutputFormat(
            ERHIPixelFormat format,
            int logicalAttachment)
        {
            if (format == ERHIPixelFormat.Unknown)
            {
                throw new ArgumentException(
                    $"DX12 logical color attachment {logicalAttachment} " +
                    "has an unknown output format.");
            }
        }
    }

    internal sealed class Dx12RasterPassLowering
    {
        internal const int PrivateInputDescriptorCount =
            RHIAttachmentIndexArray.MaxAttachments;
        internal const int RasterOrderedDescriptorCount =
            RHIAttachmentIndexArray.MaxAttachments;
        internal const int PrivateDescriptorCountPerSubPass =
            PrivateInputDescriptorCount +
            RasterOrderedDescriptorCount;
        internal EDx12RasterPassStrategy Strategy { get; }
        internal bool UsesEnhancedBarriers { get; }
        internal bool RequiresPrivateAttachmentTable { get; }
        internal ReadOnlyMemory<Dx12RasterSubPassLowering> SubPasses =>
            m_SubPasses;

        private readonly Dx12RasterSubPassLowering[] m_SubPasses;

        private Dx12RasterPassLowering(
            EDx12RasterPassStrategy strategy,
            bool usesEnhancedBarriers,
            bool requiresPrivateAttachmentTable,
            Dx12RasterSubPassLowering[] subPasses)
        {
            Strategy = strategy;
            UsesEnhancedBarriers = usesEnhancedBarriers;
            RequiresPrivateAttachmentTable =
                requiresPrivateAttachmentTable;
            m_SubPasses = subPasses;
        }

        internal static Dx12RasterPassLowering Compile(
            RasterPassPlan plan,
            bool supportsNativeRenderPass,
            bool supportsRasterOrderedViews,
            bool usesEnhancedBarriers)
        {
            ArgumentNullException.ThrowIfNull(plan);

            bool hasAttachmentReads = false;
            bool hasRasterOrderedAccess = false;
            bool requiresPrivateAttachmentTable = false;
            Span<ERHIPixelFormat> logicalAttachmentFormats =
                stackalloc ERHIPixelFormat[
                    RHIAttachmentIndexArray.MaxAttachments];
            for (int attachmentIndex = 0;
                 attachmentIndex < plan.ColorAttachmentCount;
                 ++attachmentIndex)
            {
                logicalAttachmentFormats[attachmentIndex] =
                    plan.GetColorAttachment(attachmentIndex)
                        .RenderTarget.Descriptor.Format;
            }
            Dx12RasterSubPassLowering[] subPasses =
                new Dx12RasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                ValidateAccessModel(in subPass, i);
                Dx12RasterSubPassLowering lowering =
                    new Dx12RasterSubPassLowering(
                        subPass,
                        logicalAttachmentFormats.Slice(
                            0,
                            plan.ColorAttachmentCount));
                subPasses[i] = lowering;
                hasAttachmentReads |=
                    lowering.ShaderResourceMask != 0 ||
                    lowering.RasterOrderedMask != 0;
                hasRasterOrderedAccess |= lowering.RasterOrderedMask != 0;
                requiresPrivateAttachmentTable |=
                    lowering.PrivateShaderResourceMask != 0 ||
                    lowering.RasterOrderedMask != 0;
            }

            if (hasRasterOrderedAccess && !supportsRasterOrderedViews)
            {
                throw new NotSupportedException(
                    "The raster pass requires DirectX 12 rasterizer-ordered " +
                    "views, but the current adapter does not support them.");
            }
            if (hasRasterOrderedAccess &&
                plan.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException(
                    "The DX12 attachment ABI exposes exact " +
                    "RasterizerOrderedTexture2D/Texture2DArray access only; " +
                    "multisampled RasterOrderedReadWrite is unsupported.");
            }

            EDx12RasterPassStrategy strategy =
                supportsNativeRenderPass &&
                plan.SubPassCount == 1 &&
                !hasAttachmentReads &&
                !subPasses[0].HasSparseOutputLocations &&
                subPasses[0].HasIdentityOutputMapping(
                    plan.ColorAttachmentCount)
                    ? EDx12RasterPassStrategy.NativeRenderPass
                    : EDx12RasterPassStrategy.OmMultipass;

            return new Dx12RasterPassLowering(
                strategy,
                usesEnhancedBarriers,
                requiresPrivateAttachmentTable,
                subPasses);
        }

        internal static int GetPrivateDescriptorTableOffset(
            int subPassIndex)
        {
            if (subPassIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(subPassIndex));
            }
            return checked(
                subPassIndex *
                PrivateDescriptorCountPerSubPass);
        }

        internal static int GetPrivateInputDescriptorOffset(
            int subPassIndex,
            int inputIndex)
        {
            ValidatePrivateOrdinal(inputIndex, nameof(inputIndex));
            return checked(
                GetPrivateDescriptorTableOffset(subPassIndex) +
                inputIndex);
        }

        internal static int GetRasterOrderedDescriptorOffset(
            int subPassIndex,
            int logicalAttachment)
        {
            ValidatePrivateOrdinal(
                logicalAttachment,
                nameof(logicalAttachment));
            return checked(
                GetPrivateDescriptorTableOffset(subPassIndex) +
                PrivateInputDescriptorCount +
                logicalAttachment);
        }

        private static void ValidatePrivateOrdinal(
            int ordinal,
            string parameterName)
        {
            if ((uint)ordinal >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    ordinal,
                    $"DX12 private raster descriptor ordinals must be in " +
                    $"[0, {RHIAttachmentIndexArray.MaxAttachments - 1}].");
            }
        }

        private static void ValidateAccessModel(
            in RasterSubPassPlan subPass,
            int subPassIndex)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                subPass.AttachmentInterface;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0)
                {
                    continue;
                }

                byte bit = checked((byte)(1 << logicalAttachment));
                bool isOutput =
                    (attachmentInterface.ColorOutputMask & bit) != 0;
                bool isRasterOrdered =
                    (attachmentInterface.RasterOrderedReadWriteMask &
                     bit) != 0;
                if (isOutput && !isRasterOrdered)
                {
                    throw new NotSupportedException(
                        $"DX12 subpass {subPassIndex} reads and writes logical " +
                        $"attachment {logicalAttachment} in one phase without " +
                        "the exact RasterOrderedReadWrite qualifier.");
                }
            }

            for (int sampledOrdinal = 0;
                 sampledOrdinal <
                    attachmentInterface.SampledFeedbackSlotCount;
                 ++sampledOrdinal)
            {
                int logicalAttachment =
                    attachmentInterface
                        .GetSampledFeedbackLogicalAttachment(
                            sampledOrdinal);
                if (logicalAttachment < 0)
                {
                    continue;
                }
                byte bit = checked((byte)(1 << logicalAttachment));
                if ((attachmentInterface.ColorOutputMask & bit) != 0)
                {
                    throw new NotSupportedException(
                        $"DX12 subpass {subPassIndex} cannot sample and render " +
                        $"to logical attachment {logicalAttachment} in one " +
                        "phase. Use a phase boundary or an exact " +
                        "RasterOrderedReadWrite attachment.");
                }
            }
        }
    }

    /// <summary>
    /// Builds the backend-private root-signature variant used only by raster
    /// pipelines whose attachment interface contains local reads or ROV access.
    /// The caller-visible pipeline layout and its root-parameter numbering are
    /// left unchanged.
    /// </summary>
    internal static class Dx12RasterAttachmentRootSignature
    {
        internal static Vortice.Direct3D12.ID3D12RootSignature Create(
            Dx12Device device,
            Dx12PipelineLayout pipelineLayout,
            out uint attachmentRootParameterIndex)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            if (pipelineLayout.IsLocalSignature)
            {
                throw new NotSupportedException(
                    "A DX12 local root signature cannot be used by a raster attachment interface.");
            }

            Dx12PipelineLayoutPlan plan = pipelineLayout.Plan;
            ulong rootDwordCost =
                (ulong)plan.DescriptorTableParameterCount +
                plan.PushConstantSize / 4u +
                1u;
            if (rootDwordCost > 64u)
            {
                throw new ArgumentException(
                    $"DX12 raster attachment root signature costs {rootDwordCost} DWORDs, exceeding the 64-DWORD limit.",
                    nameof(pipelineLayout));
            }

            attachmentRootParameterIndex =
                checked((uint)plan.TotalRootParameterCount);
            Vortice.Direct3D12.RootParameter1[] rootParameters =
                new Vortice.Direct3D12.RootParameter1[
                    checked(plan.TotalRootParameterCount + 1)];
            PopulateCallerParameters(plan, rootParameters);
            rootParameters[attachmentRootParameterIndex] =
                CreateAttachmentParameter();

            Vortice.Direct3D12.RootSignatureFlags flags =
                Vortice.Direct3D12.RootSignatureFlags.DenyHullShaderRootAccess |
                Vortice.Direct3D12.RootSignatureFlags.DenyDomainShaderRootAccess |
                Vortice.Direct3D12.RootSignatureFlags.DenyGeometryShaderRootAccess;
            if (pipelineLayout.UsesVertexLayout)
            {
                flags |=
                    Vortice.Direct3D12.RootSignatureFlags.AllowInputAssemblerInputLayout;
            }

            Vortice.Direct3D12.VersionedRootSignatureDescription description =
                new Vortice.Direct3D12.VersionedRootSignatureDescription(
                    new Vortice.Direct3D12.RootSignatureDescription1(
                        flags,
                        rootParameters,
                        Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob? signature = null;
            try
            {
                string error =
                    Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(
                        description,
                        out signature);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException(
                        $"DX12 raster attachment root-signature serialization failed: {error}");
                }
                if (signature == null)
                {
                    throw new InvalidOperationException(
                        "DX12 raster attachment root-signature serialization returned no signature blob.");
                }

                SharpGen.Runtime.Result result =
                    device.NativeDevice.CreateRootSignature(
                        0,
                        signature.BufferPointer,
                        signature.BufferSize,
                        out Vortice.Direct3D12.ID3D12RootSignature? rootSignature);
                return Dx12Utility.RequireCreatedObject(
                    rootSignature,
                    result,
                    "ID3D12Device.CreateRootSignature(raster attachment interface)");
            }
            finally
            {
                signature?.Release();
            }
        }

        private static void PopulateCallerParameters(
            Dx12PipelineLayoutPlan plan,
            Vortice.Direct3D12.RootParameter1[] rootParameters)
        {
            foreach (Dx12PipelineBindingTablePlan tablePlan in plan.TablePlans)
            {
                Dx12BindingTableLayout layout = tablePlan.Layout;
                for (int groupIndex = 0;
                     groupIndex < layout.Groups.Length;
                     ++groupIndex)
                {
                    Dx12BindingTableGroupPlan group = layout.Groups[groupIndex];
                    Vortice.Direct3D12.DescriptorRange1[] ranges =
                        new Vortice.Direct3D12.DescriptorRange1[
                            group.BindingIndices.Length];
                    for (int rangeIndex = 0;
                         rangeIndex < group.BindingIndices.Length;
                         ++rangeIndex)
                    {
                        ref readonly Dx12BindInfo bindInfo =
                            ref layout.BindInfos[
                                group.BindingIndices[rangeIndex]];
                        ranges[rangeIndex] =
                            new Vortice.Direct3D12.DescriptorRange1
                            {
                                RangeType = bindInfo.NativeRangeType,
                                NumDescriptors = bindInfo.Count,
                                BaseShaderRegister = bindInfo.Slot,
                                RegisterSpace = layout.Index,
                                Flags =
                                    Dx12Utility.GetDx12DescriptorRangeFlags(
                                        bindInfo.Type),
                                OffsetInDescriptorsFromTableStart =
                                    checked((uint)bindInfo.DescriptorOffset),
                            };
                    }

                    rootParameters[
                        tablePlan.RootParameterIndices[groupIndex]] =
                        new Vortice.Direct3D12.RootParameter1(
                            new Vortice.Direct3D12.RootDescriptorTable1(ranges),
                            group.Visibility);
                }
            }

            if (plan.PushConstantSize != 0)
            {
                rootParameters[plan.PushConstantRootParameterIndex] =
                    new Vortice.Direct3D12.RootParameter1(
                        new Vortice.Direct3D12.RootConstants(
                            0,
                            0,
                            plan.PushConstantSize / 4u),
                        Vortice.Direct3D12.ShaderVisibility.All);
            }
        }

        private static Vortice.Direct3D12.RootParameter1
            CreateAttachmentParameter()
        {
            Vortice.Direct3D12.DescriptorRange1[] ranges =
            {
                new Vortice.Direct3D12.DescriptorRange1
                {
                    RangeType =
                        Vortice.Direct3D12.DescriptorRangeType.ShaderResourceView,
                    NumDescriptors = RHIAttachmentIndexArray.MaxAttachments,
                    BaseShaderRegister = 0,
                    RegisterSpace =
                        Dx12PipelineLayoutPlan.AttachmentRegisterSpace,
                    Flags =
                        Vortice.Direct3D12.DescriptorRangeFlags
                            .DataStaticWhileSetAtExecute,
                    OffsetInDescriptorsFromTableStart = 0,
                },
                new Vortice.Direct3D12.DescriptorRange1
                {
                    RangeType =
                        Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView,
                    NumDescriptors = RHIAttachmentIndexArray.MaxAttachments,
                    BaseShaderRegister = 0,
                    RegisterSpace =
                        Dx12PipelineLayoutPlan.AttachmentRegisterSpace,
                    Flags =
                        Vortice.Direct3D12.DescriptorRangeFlags.DataVolatile,
                    OffsetInDescriptorsFromTableStart =
                        RHIAttachmentIndexArray.MaxAttachments,
                },
            };
            return new Vortice.Direct3D12.RootParameter1(
                new Vortice.Direct3D12.RootDescriptorTable1(ranges),
                Vortice.Direct3D12.ShaderVisibility.Pixel);
        }
    }
    #endregion
}
