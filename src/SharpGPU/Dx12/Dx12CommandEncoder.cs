using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Infinity.Mathmatics;
using Infinity.Collections;
using Viewport = Infinity.Mathmatics.Viewport;

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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            Vortice.Direct3D12.ID3D12Resource resource = null;
            Vortice.Direct3D12.ResourceBarrier resourceBarrier;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitUAV(resource);
                    break;

                case ERHIResourceBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                    break;

                case ERHIResourceBarrierType.Triansition:
                    Vortice.Direct3D12.ResourceStates srcState;
                    Vortice.Direct3D12.ResourceStates dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Vortice.Direct3D12.ID3D12Resource resource;
            Vortice.Direct3D12.ResourceStates srcState;
            Vortice.Direct3D12.ResourceStates dstState;
            Vortice.Direct3D12.ResourceBarrier* resourceBarriers = stackalloc Vortice.Direct3D12.ResourceBarrier[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIResourceBarrier barrier = ref barriers.Span[i];

                switch (barrier.ResourceBarrierType)
                {
                    case ERHIResourceBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitUAV(resource);
                        break;

                    case ERHIResourceBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                        break;

                    case ERHIResourceBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier((uint)barriers.Length, resourceBarriers);
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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            Vortice.Direct3D12.ID3D12Resource resource = null;
            Vortice.Direct3D12.ResourceBarrier resourceBarrier;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitUAV(resource);
                    break;

                case ERHIResourceBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                    break;

                case ERHIResourceBarrierType.Triansition:
                    Vortice.Direct3D12.ResourceStates srcState;
                    Vortice.Direct3D12.ResourceStates dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Vortice.Direct3D12.ID3D12Resource resource;
            Vortice.Direct3D12.ResourceStates srcState;
            Vortice.Direct3D12.ResourceStates dstState;
            Vortice.Direct3D12.ResourceBarrier* resourceBarriers = stackalloc Vortice.Direct3D12.ResourceBarrier[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIResourceBarrier barrier = ref barriers.Span[i];

                switch (barrier.ResourceBarrierType)
                {
                    case ERHIResourceBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitUAV(resource);
                        break;

                    case ERHIResourceBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                        break;

                    case ERHIResourceBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier((uint)barriers.Length, resourceBarriers);
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

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
#if DEBUG
            Debug.Assert(dx12Buffer != null, "Barrier Buffer is null");
#endif

            Vortice.Direct3D12.ID3D12Resource nativeResource = dx12Buffer.NativeResource;
            Vortice.Direct3D12.ResourceStates nativeSrcState = Dx12Utility.ConvertToDx12BufferState(srcState);
            Vortice.Direct3D12.ResourceStates nativeDstState = Dx12Utility.ConvertToDx12BufferState(dstState);
            Vortice.Direct3D12.ResourceBarrier nativeResourceBarrier = Dx12ResourceBarrierUtil.InitTransition(nativeResource, nativeSrcState, nativeDstState);

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &nativeResourceBarrier);
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            Dx12Texture dx12Texture = texture as Dx12Texture;
#if DEBUG
            Debug.Assert(texture != null, "Barrier Texture is null");
#endif

            Vortice.Direct3D12.ID3D12Resource  nativeResource = dx12Texture.NativeResource;
            Vortice.Direct3D12.ResourceStates nativeSrcState = Dx12Utility.ConvertToDx12TextureState(srcState);
            Vortice.Direct3D12.ResourceStates nativeDstState = Dx12Utility.ConvertToDx12TextureState(dstState);
            Vortice.Direct3D12.ResourceBarrier nativeResourceBarrier = Dx12ResourceBarrierUtil.InitTransition(nativeResource, nativeSrcState, nativeDstState);

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &nativeResourceBarrier);
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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            Vortice.Direct3D12.ID3D12Resource resource = null;
            Vortice.Direct3D12.ResourceBarrier resourceBarrier;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitUAV(resource);
                    break;

                case ERHIResourceBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                    break;

                case ERHIResourceBarrierType.Triansition:
                    Vortice.Direct3D12.ResourceStates srcState;
                    Vortice.Direct3D12.ResourceStates dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Vortice.Direct3D12.ID3D12Resource resource;
            Vortice.Direct3D12.ResourceStates srcState;
            Vortice.Direct3D12.ResourceStates dstState;
            Vortice.Direct3D12.ResourceBarrier* resourceBarriers = stackalloc Vortice.Direct3D12.ResourceBarrier[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIResourceBarrier barrier = ref barriers.Span[i];

                switch (barrier.ResourceBarrierType)
                {
                    case ERHIResourceBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitUAV(resource);
                        break;

                    case ERHIResourceBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                        break;

                    case ERHIResourceBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier((uint)barriers.Length, resourceBarriers);
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

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            Dx12Buffer dx12Buffer = buffer as Dx12Buffer;
#if DEBUG
            Debug.Assert(dx12Buffer != null, "Barrier Buffer is null");
#endif

            Vortice.Direct3D12.ID3D12Resource nativeResource = dx12Buffer.NativeResource;
            Vortice.Direct3D12.ResourceStates nativeSrcState = Dx12Utility.ConvertToDx12BufferState(srcState);
            Vortice.Direct3D12.ResourceStates nativeDstState = Dx12Utility.ConvertToDx12BufferState(dstState);
            Vortice.Direct3D12.ResourceBarrier nativeResourceBarrier = Dx12ResourceBarrierUtil.InitTransition(nativeResource, nativeSrcState, nativeDstState);

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &nativeResourceBarrier);
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            Dx12Texture dx12Texture = texture as Dx12Texture;
#if DEBUG
            Debug.Assert(texture != null, "Barrier Texture is null");
#endif

            Vortice.Direct3D12.ID3D12Resource nativeResource = dx12Texture.NativeResource;
            Vortice.Direct3D12.ResourceStates nativeSrcState = Dx12Utility.ConvertToDx12TextureState(srcState);
            Vortice.Direct3D12.ResourceStates nativeDstState = Dx12Utility.ConvertToDx12TextureState(dstState);
            Vortice.Direct3D12.ResourceBarrier nativeResourceBarrier = Dx12ResourceBarrierUtil.InitTransition(nativeResource, nativeSrcState, nativeDstState);

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &nativeResourceBarrier);
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

        public Dx12RasterEncoder(Dx12CommandBuffer cmdBuffer)
        {
            m_SubPassIndex = 0;
            m_CommandBuffer = cmdBuffer;
            m_AttachmentInfos = new List<Dx12AttachmentInfo>(5);
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
#if DEBUG
            PushDebugGroup(descriptor.Name);
#endif
            m_SubPassIndex = 0;
            m_AttachmentInfos.Clear();
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            Vortice.Direct3D12.CpuDescriptorHandle? dsvHandle = null;
            Vortice.Direct3D12.CpuDescriptorHandle* rtvHandles = stackalloc Vortice.Direct3D12.CpuDescriptorHandle[descriptor.ColorAttachments.Length];

            // create render target views
            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                Dx12Texture texture = descriptor.ColorAttachments.Span[i].RenderTarget as Dx12Texture;
#if DEBUG
                Debug.Assert(texture != null, "ColorRenderTarget Texture is null");
#endif
                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.ArrayCount = texture.Descriptor.Extent.z;
                    viewDescriptor.BaseArraySlice = 0;
                    //viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ERHITextureViewType.Pending;
                    //viewDescriptor.Dimension = texture.Descriptor.Dimension;
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

            // create depth stencil view
            if (descriptor.DepthStencilAttachment.HasValue)
            {
                Dx12Texture texture = descriptor.DepthStencilAttachment.Value.RenderTarget as Dx12Texture;
#if DEBUG
                Debug.Assert(texture != null, "DepthStencilTarget texture is null");
#endif
                RHITextureViewDescriptor viewDescriptor;
                {
                    viewDescriptor.MipCount = texture.Descriptor.MipCount;
                    viewDescriptor.BaseMipLevel = 0;
                    viewDescriptor.ArrayCount = texture.Descriptor.Extent.z;
                    viewDescriptor.BaseArraySlice = 0;
                    //viewDescriptor.Format = texture.Descriptor.Format;
                    viewDescriptor.ViewType = ERHITextureViewType.Pending;
                    //viewDescriptor.Dimension = texture.Descriptor.Dimension;
                }
                Vortice.Direct3D12.DepthStencilViewDescription desc = new Vortice.Direct3D12.DepthStencilViewDescription();
                desc.Flags = Dx12Utility.GetDx12DSVFlag(false, false);
                //desc.Flags = Dx12Utility.GetDx12DSVFlag(descriptor.DepthStencilAttachment.Value.DepthReadOnly, descriptor.DepthStencilAttachment.Value.StencilReadOnly);
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

                dsvHandle = dx12AttachmentInfo.AttachmentInfo.CpuHandle;
                texture.Dx12Device.NativeDevice.CreateDepthStencilView(texture.NativeResource, desc, dsvHandle.Value);
            }

            // set render targets
            dx12CommandBuffer.NativeCommandList.OMSetRenderTargets((uint)descriptor.ColorAttachments.Length, rtvHandles, false, dsvHandle);

            // clear render targets
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

            // clear depth stencil target
            if (dsvHandle.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor? depthStencilAttachmentDescriptor = descriptor.DepthStencilAttachment;
                if (depthStencilAttachmentDescriptor?.DepthLoadOp != ERHILoadAction.Clear && depthStencilAttachmentDescriptor?.StencilLoadOp != ERHILoadAction.Clear)
                {
                    return;
                }

                dx12CommandBuffer.NativeCommandList.ClearDepthStencilView(
                    dsvHandle.Value,
                    Dx12Utility.GetDx12ClearFlagByDSA(depthStencilAttachmentDescriptor.Value),
                    depthStencilAttachmentDescriptor.Value.DepthClearValue,
                    Convert.ToByte(depthStencilAttachmentDescriptor.Value.StencilClearValue));
            }

            // set shading rate
            if (descriptor.ShadingRateTexture != null)
            {
                Dx12Texture dx12Texture = descriptor.ShadingRateTexture as Dx12Texture;
                dx12CommandBuffer.NativeCommandList.RSSetShadingRateImage(dx12Texture.NativeResource);
            }
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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            Vortice.Direct3D12.ID3D12Resource resource = null;
            Vortice.Direct3D12.ResourceBarrier resourceBarrier;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitUAV(resource);
                    break;

                case ERHIResourceBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                    break;

                case ERHIResourceBarrierType.Triansition:
                    Vortice.Direct3D12.ResourceStates srcState;
                    Vortice.Direct3D12.ResourceStates dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Vortice.Direct3D12.ID3D12Resource resource;
            Vortice.Direct3D12.ResourceStates srcState;
            Vortice.Direct3D12.ResourceStates dstState;
            Vortice.Direct3D12.ResourceBarrier* resourceBarriers = stackalloc Vortice.Direct3D12.ResourceBarrier[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIResourceBarrier barrier = ref barriers.Span[i];

                switch (barrier.ResourceBarrierType)
                {
                    case ERHIResourceBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitUAV(resource);
                        break;

                    case ERHIResourceBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                        break;

                    case ERHIResourceBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier((uint)barriers.Length, resourceBarriers);
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
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.DrawInstanced(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.DrawIndexedInstanced(indexCount, instanceCount, firstIndex, (int)baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            Dx12Buffer dx12Buffer = argsBuffer as Dx12Buffer;
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ExecuteIndirect(dx12Device.DrawIndexedIndirectSignature, drawCount, dx12Buffer.NativeResource, offset, null, 0);
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            Dx12Device dx12Device = ((Dx12CommandQueue)m_CommandBuffer.CommandQueue).Dx12Device;
            if(dx12Device.Feature.IsMeshShadingSupported)
            {
                Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
                dx12CommandBuffer.NativeCommandList.DispatchMesh(groupCountX, groupCountY, groupCountZ);
            }
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            Vortice.Direct3D12.ID3D12Resource resource = null;
            Vortice.Direct3D12.ResourceBarrier resourceBarrier;

            switch (barrier.ResourceBarrierType)
            {
                case ERHIResourceBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitUAV(resource);
                    break;

                case ERHIResourceBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                    break;

                case ERHIResourceBarrierType.Triansition:
                    Vortice.Direct3D12.ResourceStates srcState;
                    Vortice.Direct3D12.ResourceStates dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                    break;
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Vortice.Direct3D12.ID3D12Resource resource;
            Vortice.Direct3D12.ResourceStates srcState;
            Vortice.Direct3D12.ResourceStates dstState;
            Vortice.Direct3D12.ResourceBarrier* resourceBarriers = stackalloc Vortice.Direct3D12.ResourceBarrier[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIResourceBarrier barrier = ref barriers.Span[i];

                switch (barrier.ResourceBarrierType)
                {
                    case ERHIResourceBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitUAV(resource);
                        break;

                    case ERHIResourceBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitAliasing(null, resource);
                        break;

                    case ERHIResourceBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = Dx12ResourceBarrierUtil.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
            dx12CommandBuffer.NativeCommandList.ResourceBarrier((uint)barriers.Length, resourceBarriers);
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
            m_CachedPipeline = pipeline;

            // ML pipeline uses compute shader bridge: bind the internal compute pipeline
            if (pipeline is not Dx12MLPipeline dx12MLPipeline)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12MLPipeline)} but got {pipeline?.GetType().Name ?? "<null>"}.");
            }

            if (dx12MLPipeline.ComputePipeline != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;
                dx12CommandBuffer.NativeCommandList.SetPipelineState(dx12MLPipeline.ComputePipeline.NativePipelineState);
            }
        }

        public override void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex)
        {
            // Bind resource table descriptors to compute root signature slots
            if (resourceTable is not Dx12ArgumentTable dx12ArgumentTable)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12ArgumentTable)} but got {resourceTable?.GetType().Name ?? "<null>"}.");
            }
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            for (int i = 0; i < dx12ArgumentTable.NativeGpuDescriptorHandles.Length; ++i)
            {
                dx12CommandBuffer.NativeCommandList.SetComputeRootDescriptorTable(tableIndex + (uint)i, dx12ArgumentTable.NativeGpuDescriptorHandles[i]);
            }
        }

        public override void SetInputTensor(RHITensor tensor, in uint index)
        {
#if DEBUG
            Debug.Assert(m_PipelineSet, "Dx12MLEncoder: SetPipeline must be called before SetInputTensor.");
#endif
            if (tensor is not Dx12Tensor dx12Tensor || dx12Tensor.BackingBuffer == null)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12Tensor)} with valid backing buffer.");
            }

            Dx12Buffer dx12Buffer = dx12Tensor.BackingBuffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            // Bind tensor backing buffer as SRV via root descriptor
            dx12CommandBuffer.NativeCommandList.SetComputeRootShaderResourceView(index, dx12Buffer.NativeResource.GPUVirtualAddress);
        }

        public override void SetOutputTensor(RHITensor tensor, in uint index)
        {
#if DEBUG
            Debug.Assert(m_PipelineSet, "Dx12MLEncoder: SetPipeline must be called before SetOutputTensor.");
#endif
            if (tensor is not Dx12Tensor dx12Tensor || dx12Tensor.BackingBuffer == null)
            {
                throw new InvalidOperationException($"Dx12MLEncoder expects {nameof(Dx12Tensor)} with valid backing buffer.");
            }

            Dx12Buffer dx12Buffer = dx12Tensor.BackingBuffer;
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            // Bind tensor backing buffer as UAV via root descriptor
            dx12CommandBuffer.NativeCommandList.SetComputeRootUnorderedAccessView(index, dx12Buffer.NativeResource.GPUVirtualAddress);
        }

        public override void Dispatch(RHIHeap intermediatesHeap)
        {
#if DEBUG
            Debug.Assert(m_PipelineSet, "Dx12MLEncoder: SetPipeline must be called before Dispatch.");
#endif
            Dx12CommandBuffer dx12CommandBuffer = m_CommandBuffer as Dx12CommandBuffer;

            // Dispatch compute shader serving as ML kernel
            // Workgroup count derived from intermediates heap size
            Dx12MLPipeline dx12MLPipeline = m_CachedPipeline as Dx12MLPipeline;
            uint workgroupCount = (uint)Math.Max(1, (long)dx12MLPipeline.IntermediatesHeapSize / 256);
            dx12CommandBuffer.NativeCommandList.Dispatch(workgroupCount, 1, 1);
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

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Tracked: ROADMAP.md P2-1.");
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
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
