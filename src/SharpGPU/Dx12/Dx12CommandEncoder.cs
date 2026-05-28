using System;
using System.Diagnostics;
using SharpGPU.Collections;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Viewport = SharpGPU.Mathematics.Viewport;

namespace Infinity.Graphics
{
#pragma warning disable CS0414, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
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
        public static Vortice.Direct3D12.ResourceBarrier InitUAV(Vortice.Direct3D12.ID3D12Resource resource)
            => Vortice.Direct3D12.ResourceBarrier.BarrierUnorderedAccessView(resource);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vortice.Direct3D12.ResourceBarrier InitAliasing(Vortice.Direct3D12.ID3D12Resource resourceBefore, Vortice.Direct3D12.ID3D12Resource resourceAfter)
            => Vortice.Direct3D12.ResourceBarrier.BarrierAliasing(resourceBefore, resourceAfter);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vortice.Direct3D12.ResourceBarrier InitTransition(Vortice.Direct3D12.ID3D12Resource resource, Vortice.Direct3D12.ResourceStates stateBefore, Vortice.Direct3D12.ResourceStates stateAfter)
            => Vortice.Direct3D12.ResourceBarrier.BarrierTransition(resource, stateBefore, stateAfter, Vortice.Direct3D12.D3D12.ResourceBarrierAllSubResources, Vortice.Direct3D12.ResourceBarrierFlags.None);
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
        private static readonly Vortice.Direct3D12.BarrierSubresourceRange s_AllSubresourcesRange = new Vortice.Direct3D12.BarrierSubresourceRange
        {
            IndexOrFirstMipLevel = Vortice.Direct3D12.D3D12.ResourceBarrierAllSubResources,
            NumMipLevels = 0,
            FirstArraySlice = 0,
            NumArraySlices = 0,
            FirstPlane = 0,
            NumPlanes = 0
        };

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

            Dx12Device device = ((Dx12CommandQueue)commandBuffer.CommandQueue).Dx12Device;
            if (device.IsEnhancedBarriersSupported)
            {
                EmitEnhancedBarriers(commandBuffer, barriers);
            }
            else
            {
                EmitLegacyBarriers(commandBuffer, barriers);
            }
        }

        private static void EmitLegacyBarriers(Dx12CommandBuffer commandBuffer, ReadOnlySpan<RHIBarrier> barriers)
        {
            Vortice.Direct3D12.ResourceBarrier[] nativeBarriers = new Vortice.Direct3D12.ResourceBarrier[barriers.Length];
            int barrierCount = 0;

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref readonly RHIBarrier barrier = ref barriers[i];
                switch (barrier.Kind)
                {
                    case ERHIBarrierKind.Global:
                        // TODO: Legacy D3D12 cannot express global memory barriers with sync/access granularity.
                        // Use a conservative global UAV barrier.
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitUAV(null);
                        break;

                    case ERHIBarrierKind.Buffer:
                    {
                        RHIBufferBarrier bufferBarrier = barrier.BufferBarrier;
                        Vortice.Direct3D12.ResourceStates stateBefore = ConvertToLegacyBufferStates(bufferBarrier.AccessBefore);
                        Vortice.Direct3D12.ResourceStates stateAfter = ConvertToLegacyBufferStates(bufferBarrier.AccessAfter);
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitTransition(
                            GetBufferResource(bufferBarrier.Resource, i),
                            stateBefore,
                            stateAfter);
                        break;
                    }

                    case ERHIBarrierKind.Texture:
                    {
                        RHITextureBarrier textureBarrier = barrier.TextureBarrier;
                        Vortice.Direct3D12.ResourceStates stateBefore = ConvertToLegacyTextureStates(textureBarrier.LayoutBefore, textureBarrier.AccessBefore);
                        Vortice.Direct3D12.ResourceStates stateAfter = ConvertToLegacyTextureStates(textureBarrier.LayoutAfter, textureBarrier.AccessAfter);
                        nativeBarriers[barrierCount++] = Dx12ResourceBarrierUtil.InitTransition(
                            GetTextureResource(textureBarrier.Resource, i),
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
                            Resource = GetBufferResource(bufferBarrier.Resource, i),
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

                        (textureBarriers ??= new List<Vortice.Direct3D12.TextureBarrier>(barriers.Length)).Add(new Vortice.Direct3D12.TextureBarrier
                        {
                            SyncBefore = syncBefore,
                            SyncAfter = syncAfter,
                            AccessBefore = accessBefore,
                            AccessAfter = accessAfter,
                            LayoutBefore = ConvertToBarrierLayout(textureBarrier.LayoutBefore, queuePipeline),
                            LayoutAfter = ConvertToBarrierLayout(textureBarrier.LayoutAfter, queuePipeline),
                            Resource = GetTextureResource(textureBarrier.Resource, i),
                            Subresources = ConvertToSubresourceRange(textureBarrier.SubresourceRange),
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

        private static Vortice.Direct3D12.ID3D12Resource GetBufferResource(RHIBuffer resource, in int index)
        {
            Dx12Buffer buffer = resource as Dx12Buffer;
#if DEBUG
            Debug.Assert(buffer != null, index >= 0 ? String.Format("Barrier Buffer is null at index {0}.", index) : "Barrier Buffer is null");
#endif
            if (buffer == null)
            {
                throw new InvalidOperationException(index >= 0 ? String.Format("Barrier Buffer is null at index {0}.", index) : "Barrier Buffer is null");
            }

            return buffer.NativeResource;
        }

        private static Vortice.Direct3D12.ID3D12Resource GetTextureResource(RHITexture resource, in int index)
        {
            Dx12Texture texture = resource as Dx12Texture;
#if DEBUG
            Debug.Assert(texture != null, index >= 0 ? String.Format("Barrier Texture is null at index {0}.", index) : "Barrier Texture is null");
#endif
            if (texture == null)
            {
                throw new InvalidOperationException(index >= 0 ? String.Format("Barrier Texture is null at index {0}.", index) : "Barrier Texture is null");
            }

            return texture.NativeResource;
        }

        private static Vortice.Direct3D12.BarrierSubresourceRange ConvertToSubresourceRange(in RHITextureSubresourceRange range)
        {
            bool isWholeRange = range.BaseMipLevel == 0
                                && range.MipLevelCount == RHITextureSubresourceRange.All
                                && range.BaseArrayLayer == 0
                                && range.ArrayLayerCount == RHITextureSubresourceRange.All;
            if (isWholeRange)
            {
                return s_AllSubresourcesRange;
            }

            // TODO: plane range is not exposed in the RHI barrier model.
            // D3D12 enhanced texture barrier plane dimension is conservatively widened.
            return new Vortice.Direct3D12.BarrierSubresourceRange
            {
                IndexOrFirstMipLevel = range.BaseMipLevel,
                NumMipLevels = range.MipLevelCount == RHITextureSubresourceRange.All ? 0u : range.MipLevelCount,
                FirstArraySlice = range.BaseArrayLayer,
                NumArraySlices = range.ArrayLayerCount == RHITextureSubresourceRange.All ? 0u : range.ArrayLayerCount,
                FirstPlane = 0,
                NumPlanes = 0
            };
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
            if ((syncMask & ERHISyncStageMask.Transfer) != 0) sync |= Vortice.Direct3D12.BarrierSync.Copy | Vortice.Direct3D12.BarrierSync.Resolve;
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

        private static Vortice.Direct3D12.ResourceStates ConvertToLegacyBufferStates(in ERHIAccessMask accessMask)
        {
            if (accessMask == ERHIAccessMask.None)
            {
                return Vortice.Direct3D12.ResourceStates.Common;
            }

            Vortice.Direct3D12.ResourceStates result = 0;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((accessMask & ERHIAccessMask.IndexRead) != 0) result |= Vortice.Direct3D12.ResourceStates.IndexBuffer;
            if ((accessMask & ERHIAccessMask.VertexRead) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((accessMask & ERHIAccessMask.ConstantRead) != 0) result |= Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer;
            if ((accessMask & ERHIAccessMask.IndirectCommandRead) != 0) result |= Vortice.Direct3D12.ResourceStates.IndirectArgument;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
            if ((accessMask & ERHIAccessMask.ShaderWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.UnorderedAccess;
            if ((accessMask & ERHIAccessMask.AccelStructRead) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((accessMask & ERHIAccessMask.AccelStructWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) result |= Vortice.Direct3D12.ResourceStates.ShadingRateSource;
            return result == 0 ? Vortice.Direct3D12.ResourceStates.Common : result;
        }

        private static Vortice.Direct3D12.ResourceStates ConvertToLegacyTextureStates(in ERHITextureLayout layout, in ERHIAccessMask accessMask)
        {
            Vortice.Direct3D12.ResourceStates layoutState = ConvertTextureLayoutToLegacyState(layout);
            Vortice.Direct3D12.ResourceStates accessState = ConvertTextureAccessToLegacyState(accessMask);

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

        private static Vortice.Direct3D12.ResourceStates ConvertTextureLayoutToLegacyState(in ERHITextureLayout layout)
        {
            switch (layout)
            {
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

        private static Vortice.Direct3D12.ResourceStates ConvertTextureAccessToLegacyState(in ERHIAccessMask accessMask)
        {
            Vortice.Direct3D12.ResourceStates result = 0;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= Vortice.Direct3D12.ResourceStates.CopySource;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.CopyDest;
            if ((accessMask & ERHIAccessMask.ResolveRead) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveSource;
            if ((accessMask & ERHIAccessMask.ResolveWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.ResolveDest;
            if ((accessMask & ERHIAccessMask.DepthStencilRead) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthRead;
            if ((accessMask & ERHIAccessMask.DepthStencilWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.DepthWrite;
            if ((accessMask & ERHIAccessMask.RenderTargetRead) != 0) result |= Vortice.Direct3D12.ResourceStates.RenderTarget;
            if ((accessMask & ERHIAccessMask.RenderTargetWrite) != 0) result |= Vortice.Direct3D12.ResourceStates.RenderTarget;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0) result |= Vortice.Direct3D12.ResourceStates.PixelShaderResource | Vortice.Direct3D12.ResourceStates.NonPixelShaderResource;
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
            if ((effectiveAccess & (Vortice.Direct3D12.BarrierAccess.ConstantBuffer | Vortice.Direct3D12.BarrierAccess.ShaderResource | Vortice.Direct3D12.BarrierAccess.UnorderedAccess)) != 0) requiredSync |= Vortice.Direct3D12.BarrierSync.AllShading;
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
        public Dx12TransferEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.TimestampQueryHeap != null, "Current RasterPass TimestampQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.TimestampQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
            Dx12Query dx12Query = query as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            switch (query.QueryDescriptor.Type)
            {
                case ERHIQueryType.Occlusion:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, startIndex, queriesCount, dx12Query.QueryResult, startIndex * 8);
                    break;

                case ERHIQueryType.Statistics:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, startIndex, queriesCount, dx12Query.QueryResult, startIndex * (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics));
                    break;

                default:
                    dx12CommandBuffer.NativeCommandList.ResolveQueryData(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, startIndex, queriesCount, dx12Query.QueryResult, startIndex * 8);
                    break;
            }
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            Dx12Buffer dx12SrcBuffer = srcBuffer as Dx12Buffer;
            Dx12Buffer dx12DstBuffer = dstBuffer as Dx12Buffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            dx12CommandBuffer.NativeCommandList.CopyBufferRegion(dx12DstBuffer.NativeResource, (ulong)dstOffset, dx12SrcBuffer.NativeResource, (ulong)srcOffset, (ulong)size);
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            Dx12Buffer srcBuffer = src.Buffer as Dx12Buffer;
            Dx12Texture dstTexture = dst.Texture as Dx12Texture;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

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
                SubresourceIndex = dst.SliceCount * dstTexture.Descriptor.MipCount + dst.MipLevel
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
            Dx12Texture srcTexture = src.Texture as Dx12Texture;
            Dx12Buffer dstBuffer = dst.Buffer as Dx12Buffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            Dx12TextureCopyLocation srcLocation = new Dx12TextureCopyLocation
            {
                pResource = srcTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = src.SliceCount * srcTexture.Descriptor.MipCount + src.MipLevel
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
            Dx12Texture srcTexture = src.Texture as Dx12Texture;
            Dx12Texture dstTexture = dst.Texture as Dx12Texture;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            Dx12TextureCopyLocation srcLocation = new Dx12TextureCopyLocation
            {
                pResource = srcTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = src.SliceCount * srcTexture.Descriptor.MipCount + src.MipLevel
            };

            Dx12TextureCopyLocation dstLocation = new Dx12TextureCopyLocation
            {
                pResource = dstTexture.NativeResource,
                Type = Vortice.Direct3D12.TextureCopyType.SubresourceIndex,
                SubresourceIndex = dst.SliceCount * dstTexture.Descriptor.MipCount + dst.MipLevel
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
#if DEBUG
            PopDebugGroup();
#endif
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12ComputeEncoder : RHIComputeEncoder
    {
        public Dx12ComputeEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIComputePassDescriptor descriptor)
        {
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.TimestampQueryHeap != null, "Current RasterPass TimestampQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.TimestampQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;

            Dx12ComputePipeline dx12Pipeline = pipeline as Dx12ComputePipeline;
            Dx12PipelineLayout dx12PipelineLayout = pipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList.SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            Dx12ArgumentTable dx12ArgumentTable = resourceTable as Dx12ArgumentTable;
            Dx12ArgumentTableLayout dx12ArgumentTableLayout = dx12ArgumentTable.ArgumentTableLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_CachedPipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12ArgumentTableLayout.Index, "error resourceTable index");
#endif

            for (int i = 0; i < dx12ArgumentTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12ArgumentTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.Compute, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                if (parameter.HasValue)
                {
#if DEBUG
                    Debug.Assert(parameter.Value.Type == bindInfo.Type);
#endif
                    dx12CommandBuffer.NativeCommandList.SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PipelineLayout dx12PipelineLayout = m_CachedPipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;
#if DEBUG
            Debug.Assert(offset + size <= dx12PipelineLayout.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({dx12PipelineLayout.PushConstantSize}).");
#endif
            dx12CommandBuffer.NativeCommandList.SetComputeRoot32BitConstants(dx12PipelineLayout.PushConstantRootParameterIndex, size / 4, data.ToPointer(), offset / 4);
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.Dispatch(groupCountX, groupCountY, groupCountZ);
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchComputeIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
        }

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            Dx12ComputeIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12ComputeIndirectCommandBuffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
        }

        public override void EndPass()
        {
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12RaytracingEncoder : RHIRaytracingEncoder
    {
        public Dx12RaytracingEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
        }

        internal override void BeginPass(in RHIRayTracingPassDescriptor descriptor)
        {
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.TimestampQueryHeap != null, "Current RasterPass TimestampQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.TimestampQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            m_CachedPipeline = pipeline;

            Dx12RaytracingPipeline dx12Pipeline = pipeline as Dx12RaytracingPipeline;
            Dx12PipelineLayout dx12PipelineLayout = pipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.SetPipelineState1(dx12Pipeline.NativePipeline);
            dx12CommandBuffer.NativeCommandList.SetComputeRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            Dx12ArgumentTable dx12ArgumentTable = resourceTable as Dx12ArgumentTable;
            Dx12ArgumentTableLayout dx12ArgumentTableLayout = dx12ArgumentTable.ArgumentTableLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_CachedPipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12ArgumentTableLayout.Index, "error resourceTable index");
#endif

            for (int i = 0; i < dx12ArgumentTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12ArgumentTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.RayTracing, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                if (!parameter.HasValue)
                {
                    // DX12 RT pass uses SetComputeRoot* APIs. Allow tables authored as Compute-stage descriptors.
                    parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.Compute, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                }
                if (!parameter.HasValue)
                {
                    parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.All, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                }
                if (parameter.HasValue)
                {
#if DEBUG
                    Debug.Assert(parameter.Value.Type == bindInfo.Type);
#endif
                    dx12CommandBuffer.NativeCommandList.SetComputeRootDescriptorTable((uint)parameter.Value.Slot, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12TopLevelAccelStruct dx12TopLevelAccelStruct = topLevelAccelStruct as Dx12TopLevelAccelStruct;
            Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription accelStructDescription = dx12TopLevelAccelStruct.NativeAccelStructDescriptor;
            dx12CommandBuffer.NativeCommandList.BuildRaytracingAccelerationStructure(accelStructDescription);

            Vortice.Direct3D12.ResourceBarrier uavBarrier = Dx12ResourceBarrierUtil.InitUAV(dx12TopLevelAccelStruct.ResultBuffer);
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &uavBarrier);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BottomLevelAccelStruct dx12BottomLevelAccelStruct = bottomLevelAccelStruct as Dx12BottomLevelAccelStruct;
            Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription accelStructDescription = dx12BottomLevelAccelStruct.NativeAccelStructDescriptor;
            dx12CommandBuffer.NativeCommandList.BuildRaytracingAccelerationStructure(accelStructDescription);

            Vortice.Direct3D12.ResourceBarrier uavBarrier = Dx12ResourceBarrierUtil.InitUAV(dx12BottomLevelAccelStruct.NativeResultBuffer);
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &uavBarrier);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            Dx12FunctionTable dx12FunctionTable = functionTable as Dx12FunctionTable;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;

#if DEBUG
            Debug.Assert(dx12FunctionTable != null, "Raytracing dispatch requires a Dx12FunctionTable.");
            Debug.Assert(dx12FunctionTable.IsGenerated, "FunctionTable must call Generate() before Dispatch().");
#endif

            if (dx12Device.Feature.IsRaytracingSupported)
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
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            if (dx12Device.Feature.IsRaytracingSupported)
            {
                Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
                dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchRayIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
            }
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
            Dx12RayTracingIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12RayTracingIndirectCommandBuffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
        }

        public override void EndPass()
        {
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12RasterEncoder : RHIRasterEncoder
    {
        protected byte m_SubPassIndex;
        protected List<Dx12AttachmentInfo> m_AttachmentInfos;
        private bool m_UseNativeRenderPass;
        private bool m_IsNativeRenderPassActive;
        private Vortice.Direct3D12.RenderPassRenderTargetDescription[] m_NativeRenderPassColorDescriptions;
        private Vortice.Direct3D12.RenderPassDepthStencilDescription? m_NativeRenderPassDepthStencilDescription;

        public Dx12RasterEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_SubPassIndex = 0;
            m_CommandBuffer = cmdBuffer;
            m_AttachmentInfos = new List<Dx12AttachmentInfo>(5);
            m_UseNativeRenderPass = false;
            m_IsNativeRenderPassActive = false;
            m_NativeRenderPassColorDescriptions = Array.Empty<Vortice.Direct3D12.RenderPassRenderTargetDescription>();
            m_NativeRenderPassDepthStencilDescription = null;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            m_SubPassIndex = 0;
            m_AttachmentInfos.Clear();
            m_UseNativeRenderPass = false;
            m_IsNativeRenderPassActive = false;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles = CreateColorAttachmentViews(descriptor);
            Vortice.Direct3D12.CpuDescriptorHandle? dsvHandle = CreateDepthStencilAttachmentView(descriptor);

            if (dx12Device.IsNativeRenderPassSupported)
            {
                CacheNativeRenderPass(dx12CommandBuffer, descriptor, rtvHandles, dsvHandle);
                m_UseNativeRenderPass = true;
            }
            else
            {
                BeginLegacyRasterPass(dx12CommandBuffer, descriptor, rtvHandles, dsvHandle);
            }

            if (descriptor.ShadingRateTexture != null)
            {
                Dx12Texture dx12Texture = descriptor.ShadingRateTexture as Dx12Texture;
                dx12CommandBuffer.NativeCommandList.RSSetShadingRateImage(dx12Texture.NativeResource);
            }
        }

        private Vortice.Direct3D12.CpuDescriptorHandle[] CreateColorAttachmentViews(in RHIRasterPassDescriptor descriptor)
        {
            Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles = new Vortice.Direct3D12.CpuDescriptorHandle[descriptor.ColorAttachments.Length];

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                Dx12Texture texture = descriptor.ColorAttachments.Span[i].RenderTarget as Dx12Texture;
#if DEBUG
                Debug.Assert(texture != null, "ColorRenderTarget Texture is null");
#endif
                if (texture == null)
                {
                    throw new InvalidOperationException($"Color render target at index {i} is null.");
                }

                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.ArrayCount = texture.Descriptor.Extent.z;
                    viewDescriptor.BaseArraySlice = 0;
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
                    dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateRtvDescriptor(1);
                }
                m_AttachmentInfos.Add(dx12AttachmentInfo);

                rtvHandles[i] = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice.CreateRenderTargetView(texture.NativeResource, desc, rtvHandles[i]);
            }

            return rtvHandles;
        }

        private Vortice.Direct3D12.CpuDescriptorHandle? CreateDepthStencilAttachmentView(in RHIRasterPassDescriptor descriptor)
        {
            if (!descriptor.DepthStencilAttachment.HasValue)
            {
                return null;
            }

            Dx12Texture texture = descriptor.DepthStencilAttachment.Value.RenderTarget as Dx12Texture;
#if DEBUG
            Debug.Assert(texture != null, "DepthStencilTarget texture is null");
#endif
            if (texture == null)
            {
                throw new InvalidOperationException("Depth stencil render target is null.");
            }

            RHITextureViewDescriptor viewDescriptor;
            {
                viewDescriptor.MipCount = texture.Descriptor.MipCount;
                viewDescriptor.BaseMipLevel = 0;
                viewDescriptor.ArrayCount = texture.Descriptor.Extent.z;
                viewDescriptor.BaseArraySlice = 0;
                viewDescriptor.ViewType = ERHITextureViewType.Pending;
            }

            Vortice.Direct3D12.DepthStencilViewDescription desc = new Vortice.Direct3D12.DepthStencilViewDescription();
            desc.Flags = Dx12Utility.GetDx12DSVFlag(false, false);
            desc.Format = Dx12Utility.ConvertToDx12Format(texture.Descriptor.Format);
            desc.ViewDimension = Dx12Utility.ConvertToDx12TextureDSVDimension(texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DDSV(ref desc.Texture2D, viewDescriptor, texture.Descriptor.Dimension);
            Dx12Utility.FillTexture2DArrayDSV(ref desc.Texture2DArray, viewDescriptor, texture.Descriptor.Dimension);

            Dx12AttachmentInfo dx12AttachmentInfo = new Dx12AttachmentInfo();
            {
                dx12AttachmentInfo.bDepthStencil = true;
                dx12AttachmentInfo.AttachmentInfo = texture.Dx12Device.AllocateDsvDescriptor(1);
            }
            m_AttachmentInfos.Add(dx12AttachmentInfo);

            Vortice.Direct3D12.CpuDescriptorHandle dsvHandle = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
            texture.Dx12Device.NativeDevice.CreateDepthStencilView(texture.NativeResource, desc, dsvHandle);
            return dsvHandle;
        }

        private static void BeginLegacyRasterPass(Dx12CommandBuffer dx12CommandBuffer,
                                                  in RHIRasterPassDescriptor descriptor,
                                                  Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles,
                                                  Vortice.Direct3D12.CpuDescriptorHandle? dsvHandle)
        {
            fixed (Vortice.Direct3D12.CpuDescriptorHandle* rtvHandlesPtr = rtvHandles)
            {
                dx12CommandBuffer.NativeCommandList.OMSetRenderTargets((uint)rtvHandles.Length, rtvHandlesPtr, false, dsvHandle);
            }

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachments.Span[i];
                if (colorAttachmentDescriptor.LoadAction != ERHILoadAction.Clear)
                {
                    continue;
                }

                float4 clearValue = colorAttachmentDescriptor.ClearValue;
                Vortice.Mathematics.Color4 nativeClearValue = new Vortice.Mathematics.Color4(clearValue.x, clearValue.y, clearValue.z, clearValue.w);
                dx12CommandBuffer.NativeCommandList.ClearRenderTargetView(rtvHandles[i], nativeClearValue);
            }

            if (dsvHandle.HasValue && descriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthStencilAttachmentDescriptor = descriptor.DepthStencilAttachment.Value;
                if (depthStencilAttachmentDescriptor.DepthLoadOp == ERHILoadAction.Clear || depthStencilAttachmentDescriptor.StencilLoadOp == ERHILoadAction.Clear)
                {
                    dx12CommandBuffer.NativeCommandList.ClearDepthStencilView(
                        dsvHandle.Value,
                        Dx12Utility.GetDx12ClearFlagByDSA(depthStencilAttachmentDescriptor),
                        depthStencilAttachmentDescriptor.DepthClearValue,
                        Convert.ToByte(depthStencilAttachmentDescriptor.StencilClearValue));
                }
            }
        }

        private void CacheNativeRenderPass(Dx12CommandBuffer dx12CommandBuffer,
                                           in RHIRasterPassDescriptor descriptor,
                                           Vortice.Direct3D12.CpuDescriptorHandle[] rtvHandles,
                                           Vortice.Direct3D12.CpuDescriptorHandle? dsvHandle)
        {
            Vortice.Direct3D12.RenderPassRenderTargetDescription[] colorDescriptions = new Vortice.Direct3D12.RenderPassRenderTargetDescription[descriptor.ColorAttachments.Length];
            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachmentDescriptor = ref descriptor.ColorAttachments.Span[i];
                Dx12Texture colorTexture = colorAttachmentDescriptor.RenderTarget as Dx12Texture;
#if DEBUG
                Debug.Assert(colorTexture != null, "ColorRenderTarget Texture is null");
#endif
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
                Dx12Texture depthStencilTexture = depthStencilAttachment.RenderTarget as Dx12Texture;
#if DEBUG
                Debug.Assert(depthStencilTexture != null, "DepthStencilTarget texture is null");
#endif
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

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
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

                    Dx12Texture srcTexture = colorAttachmentDescriptor.RenderTarget as Dx12Texture;
                    Dx12Texture dstTexture = colorAttachmentDescriptor.ResolveTarget as Dx12Texture;
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
                            ComputeResolveSubresourceIndex(srcTexture, colorAttachmentDescriptor.MipLevel, colorAttachmentDescriptor.ArraySlice),
                            ComputeResolveSubresourceIndex(dstTexture, colorAttachmentDescriptor.ResolveMipLevel, colorAttachmentDescriptor.ResolveArraySlice),
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

                    Dx12Texture srcTexture = depthStencilAttachmentDescriptor.RenderTarget as Dx12Texture;
                    Dx12Texture dstTexture = depthStencilAttachmentDescriptor.ResolveTarget as Dx12Texture;
                    if (srcTexture == null || dstTexture == null)
                    {
                        throw new InvalidOperationException("Depth/stencil resolve requires Dx12 textures for source and destination.");
                    }

                    Vortice.Direct3D12.ResolveMode resolveMode = ConvertDepthResolveMode(depthStencilAttachmentDescriptor.ResolveMode);
                    Vortice.Direct3D12.RenderPassEndingAccessResolveParameters resolveParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveParameters
                    {
                        SrcResource = srcTexture.NativeResource,
                        DstResource = dstTexture.NativeResource,
                        SubresourceCount = 1,
                        SubresourceParameters = new Vortice.Direct3D12.RenderPassEndingAccessResolveSubresourceParameters(
                            ComputeResolveSubresourceIndex(srcTexture, depthStencilAttachmentDescriptor.MipLevel, depthStencilAttachmentDescriptor.ArraySlice),
                            ComputeResolveSubresourceIndex(dstTexture, depthStencilAttachmentDescriptor.ResolveMipLevel, depthStencilAttachmentDescriptor.ResolveArraySlice),
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

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.TimestampQueryHeap != null, "Current RasterPass TimestampQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.TimestampQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Timestamp, index);
        }

        public override void BeginOcclusion(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.OcclusionQueryHeap != null, "Current RasterPass OcclusionQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.OcclusionQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, index);
        }

        public override void EndOcclusion(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.OcclusionQueryHeap != null, "Current RasterPass OcclusionQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.OcclusionQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.Occlusion, index);
        }

        public override void BeginStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.BeginQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void EndStatistics(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.StatisticsQueryHeap != null, "Current RasterPass StatisticsQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.StatisticsQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.EndQuery(dx12Query.QueryHeap, Vortice.Direct3D12.QueryType.PipelineStatistics, index);
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            if (m_UseNativeRenderPass && m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            if (m_UseNativeRenderPass && m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void NextSubPass()
        {
            ++m_SubPassIndex;
        }

        public override void SetScissor(in Rect rect)
        {
            Vortice.RawRect tempScissor = new Vortice.RawRect((int)rect.left, (int)rect.top, (int)rect.right, (int)rect.bottom);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
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
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.RSSetScissorRects((uint)rectSpan.Length, tempScissors);
        }

        public override void SetViewport(in Viewport viewport)
        {
            Vortice.Mathematics.Viewport tempViewport = new Vortice.Mathematics.Viewport(viewport.TopLeftX, viewport.TopLeftY, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
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
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.RSSetViewports((uint)viewportSpan.Length, tempViewports);
        }

        public override void SetStencilRef(in uint value)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.OMSetStencilRef(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            float4 tempValue = value;
            Vortice.Mathematics.Color4 nativeBlendFactor = new Vortice.Mathematics.Color4(tempValue.x, tempValue.y, tempValue.z, tempValue.w);
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.OMSetBlendFactor(nativeBlendFactor);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;

            Dx12RasterPipeline dx12Pipeline = pipeline as Dx12RasterPipeline;
            Dx12PipelineLayout dx12PipelineLayout = pipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.SetPipelineState(dx12Pipeline.NativePipelineState);
            dx12CommandBuffer.NativeCommandList.IASetPrimitiveTopology(dx12Pipeline.PrimitiveTopology);
            dx12CommandBuffer.NativeCommandList.SetGraphicsRootSignature(dx12PipelineLayout.NativeRootSignature);
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            Dx12ArgumentTable dx12ArgumentTable = resourceTable as Dx12ArgumentTable;
            Dx12ArgumentTableLayout dx12ArgumentTableLayout = dx12ArgumentTable.ArgumentTableLayout;
            Dx12PipelineLayout dx12PipelineLayout = m_CachedPipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

#if DEBUG
            Debug.Assert(tableIndex == dx12ArgumentTableLayout.Index, "error resourceTable index");
#endif

            for (int i = 0; i < dx12ArgumentTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                Dx12BindTypeAndParameterSlot? parameter = null;
                ref Dx12BindInfo bindInfo = ref dx12ArgumentTableLayout.BindInfos[i];

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.All, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                if (parameter.HasValue)
                {
#if DEBUG
                    Debug.Assert(parameter.Value.Type == bindInfo.Type, String.Format("BindType is not equal in graphics at index {0}.", i));
#endif
                    dx12CommandBuffer.NativeCommandList.SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.Vertex, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                if (parameter.HasValue)
                {
#if DEBUG
                    Debug.Assert(parameter.Value.Type == bindInfo.Type, String.Format("BindType is not equal in vertex at index {0}.", i));
#endif
                    dx12CommandBuffer.NativeCommandList.SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
                }

                parameter = dx12PipelineLayout.QueryRootDescriptorParameterIndex(ERHIShaderStage.Fragment, dx12ArgumentTableLayout.Index, bindInfo.Slot, bindInfo.Type);
                if (parameter.HasValue)
                {
#if DEBUG
                    Debug.Assert(parameter.Value.Type == bindInfo.Type, String.Format("BindType is not equal in fragment at index {0}.", i));
#endif
                    dx12CommandBuffer.NativeCommandList.SetGraphicsRootDescriptorTable((uint)parameter.Value.Slot, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
                }
            }
        }

        public override void SetPushConstants(IntPtr data, in uint size, in uint offset = 0)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PipelineLayout dx12PipelineLayout = m_CachedPipeline.Descriptor.PipelineLayout as Dx12PipelineLayout;
#if DEBUG
            Debug.Assert(offset + size <= dx12PipelineLayout.PushConstantSize, $"Push constant range [{offset}..{offset + size}) exceeds declared PushConstantSize ({dx12PipelineLayout.PushConstantSize}).");
#endif
            dx12CommandBuffer.NativeCommandList.SetGraphicsRoot32BitConstants(dx12PipelineLayout.PushConstantRootParameterIndex, size / 4, data.ToPointer(), offset / 4);
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
            Vortice.Direct3D12.IndexBufferView indexBufferView = new Vortice.Direct3D12.IndexBufferView
            {
                Format = Dx12Utility.ConvertToDx12IndexFormat(buffer.Descriptor.Format),
                SizeInBytes = (uint)buffer.Descriptor.ByteSize - offset,
                BufferLocation = dx12Buffer.NativeResource.GPUVirtualAddress + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.IASetIndexBuffer(&indexBufferView);
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot = 0, in uint offset = 0)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
            Dx12RasterPipeline dx12Pipeline = m_CachedPipeline as Dx12RasterPipeline;

            Vortice.Direct3D12.VertexBufferView vertexBufferView = new Vortice.Direct3D12.VertexBufferView
            {
                SizeInBytes = (uint)buffer.Descriptor.ByteSize - offset,
                StrideInBytes = dx12Pipeline.VertexStrides[slot],
                BufferLocation = dx12Buffer.NativeResource.GPUVirtualAddress + offset
            };
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.IASetVertexBuffers(slot, 1, &vertexBufferView);
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
            Vortice.Direct3D12.ShadingRateCombiner nativeShadingRateCombiner = Dx12Utility.ConvertToDx12ShadingRateCombiner(shadingRateCombiner);
            Vortice.Direct3D12.ShadingRateCombiner[] shadingRateCombiners = new[] { nativeShadingRateCombiner, nativeShadingRateCombiner };
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.RSSetShadingRate(Dx12Utility.ConvertToDx12ShadingRate(shadingRate), shadingRateCombiners);
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            EnsureNativeRenderPassActive();
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            EnsureNativeRenderPassActive();
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndexedIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            EnsureNativeRenderPassActive();
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            if(dx12Device.Feature.IsMeshShadingSupported)
            {
                Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
                dx12CommandBuffer.NativeCommandList.DispatchMesh(groupCountX, groupCountY, groupCountZ);
            }
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            EnsureNativeRenderPassActive();
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            if (dx12Device.Feature.IsMeshShadingSupported)
            {
                Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
                dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DispatchMeshIndirectSignature, 1, dx12Buffer.NativeResource, argsOffset, null, 0);
            }
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            EnsureNativeRenderPassActive();
            Dx12RasterIndirectCommandBuffer dx12IndirectCmdBuffer = indirectCmdBuffer as Dx12RasterIndirectCommandBuffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12IndirectCmdBuffer.NativeCommandSignature, dx12IndirectCmdBuffer.MaxCommandCount, dx12IndirectCmdBuffer.NativeArgumentBuffer, 0, null, 0);
        }

        public override void EndPass()
        {
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            if (m_IsNativeRenderPassActive)
            {
                dx12CommandBuffer.NativeCommandList.EndRenderPass();
                m_IsNativeRenderPassActive = false;
            }
            m_UseNativeRenderPass = false;
            m_NativeRenderPassColorDescriptions = Array.Empty<Vortice.Direct3D12.RenderPassRenderTargetDescription>();
            m_NativeRenderPassDepthStencilDescription = null;

            Dx12Device device = (m_CommandBuffer.CommandQueue as Dx12CommandQueue).Dx12Device;

            for (int i = 0; i < m_AttachmentInfos.Count; ++i)
            {
                int index = m_AttachmentInfos[i].AttachmentInfo.Index;

                if (!m_AttachmentInfos[i].bDepthStencil)
                {
                    device.FreeRtvDescriptor(index);
                }
                else
                {
                    device.FreeDsvDescriptor(index);
                }
            }
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12MLEncoder : RHIMLEncoder
    {
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
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarrier(dx12CommandBuffer, barrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12BarrierEmitter.EmitBarriers(dx12CommandBuffer, barriers);
        }

        public override void PushDebugGroup(string name)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.BeginEvent((nint)dx12CommandBuffer.NativeCommandList, name);
        }

        public override void PopDebugGroup()
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12PixEventMarker.EndEvent((nint)dx12CommandBuffer.NativeCommandList);
        }

        public override void WriteTimestamp(in uint index)
        {
#if DEBUG
            Debug.Assert(m_CommandBuffer.TimestampQueryHeap != null, "Current MLPass TimestampQuery is null");
#endif
            Dx12Query dx12Query = m_CommandBuffer.TimestampQueryHeap as Dx12Query;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
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

        public override void SetBindingSet(RHIMLBindingSet bindingSet)
        {
            if (bindingSet is not Dx12MLBindingSet dx12BindingSet)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12MLBindingSet)} but got {bindingSet?.GetType().Name ?? "<null>"}.");
            }

            m_CachedBindingSet = dx12BindingSet;
        }

        public override void Dispatch()
        {
#if DEBUG
            Debug.Assert(m_PipelineSet, "Dx12MLEncoder: SetPipeline must be called before Dispatch.");
#endif
            if (m_CachedPipeline is not Dx12MLPipeline dx12Pipeline)
            {
                throw new InvalidOperationException("Dx12MLEncoder: SetPipeline must be called before Dispatch.");
            }

            if (m_CachedBindingSet is not Dx12MLBindingSet dx12BindingSet)
            {
                throw new InvalidOperationException("Dx12MLEncoder: SetBindingSet must be called before Dispatch.");
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)dx12CommandBuffer.CommandQueue).Dx12Device;

            if (!dx12BindingSet.InternalResourcesPrepared)
            {
                TransitionBoundResourcesToMachineLearning(dx12CommandBuffer, dx12BindingSet);
                dx12BindingSet.MarkInternalResourcesPrepared();
            }
            else
            {
                EmitMachineLearningUavBarriers(
                    dx12CommandBuffer,
                    dx12BindingSet.IntermediateBuffer,
                    dx12BindingSet.PersistentBuffer,
                    dx12BindingSet.TemporaryBuffer);
            }

            if (!dx12BindingSet.IsInitialized)
            {
                dx12BindingSet.PrepareForInitialization();
                dx12Device.DirectMLCommandRecorder.RecordDispatch(dx12CommandBuffer.NativeCommandList, dx12Pipeline.OperatorInitializer, dx12BindingSet.InitializerBindingTable);
                EmitMachineLearningUavBarriers(dx12CommandBuffer, dx12BindingSet.PersistentBuffer, dx12BindingSet.TemporaryBuffer);
                dx12BindingSet.MarkInitialized();
            }

            for (int stageIndex = 0; stageIndex < dx12Pipeline.StageCount; ++stageIndex)
            {
                dx12BindingSet.PrepareForExecution(stageIndex);
                dx12Device.DirectMLCommandRecorder.RecordDispatch(dx12CommandBuffer.NativeCommandList, dx12Pipeline.GetCompiledOperator(stageIndex), dx12BindingSet.GetExecutionBindingTable(stageIndex));
                if (stageIndex + 1 < dx12Pipeline.StageCount)
                {
                    EmitMachineLearningUavBarriers(
                        dx12CommandBuffer,
                        dx12BindingSet.IntermediateBuffer,
                        dx12BindingSet.PersistentBuffer,
                        dx12BindingSet.TemporaryBuffer);
                }
            }
        }

        public override void EndPass()
        {
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            m_CachedBindingSet = null;
        }

        protected override void Release()
        {

        }

        private static void TransitionBoundResourcesToMachineLearning(Dx12CommandBuffer commandBuffer, Dx12MLBindingSet bindingSet)
        {
            int barrierCount = (bindingSet.TemporaryBuffer != null ? 1 : 0)
                + (bindingSet.PersistentBuffer != null ? 1 : 0)
                + 1;
            RHIBarrier[] barriers = new RHIBarrier[barrierCount];
            int index = 0;

            if (bindingSet.TemporaryBuffer != null)
            {
                barriers[index++] = CreateMachineLearningBufferBarrier(bindingSet.TemporaryBuffer);
            }

            if (bindingSet.PersistentBuffer != null)
            {
                barriers[index++] = CreateMachineLearningBufferBarrier(bindingSet.PersistentBuffer);
            }

            barriers[index++] = CreateMachineLearningBufferBarrier(bindingSet.IntermediateBuffer);
            Dx12BarrierEmitter.EmitBarriers(commandBuffer, barriers);
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
#pragma warning restore CS0414, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal sealed class Dx12WorkGraphEncoder : RHIWorkGraphEncoder
    {
        internal Dx12WorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }
        public override void PushDebugGroup(string name)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void PopDebugGroup()
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void WriteTimestamp(in uint index)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void SetPipeline(RHIWorkGraphPipeline pipeline)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void EndPass()
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        protected override void Release()
        {
        }
    }
}
