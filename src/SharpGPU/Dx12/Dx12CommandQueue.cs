using System;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe class Dx12CommandQueue : RHICommandQueue
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }
        public ID3D12CommandQueue* NativeCommandQueue
        {
            get
            {
                return m_NativeCommandQueue;
            }
        }
        public override ulong Frequency
        {
            get
            {
                ulong result = 0;
                m_NativeCommandQueue->GetTimestampFrequency(&result);
                return result;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12CommandQueue* m_NativeCommandQueue;

        public Dx12CommandQueue(Dx12Device device, in ERHIPipelineType pipeline)
        {
            m_Dx12Device = device;
            m_PipelineType = pipeline;

            D3D12_COMMAND_QUEUE_DESC queueDesc = new D3D12_COMMAND_QUEUE_DESC();
            queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE;
            queueDesc.Type = Dx12Utility.ConvertToDx12QueueType(pipeline);

            ID3D12CommandQueue* commandQueue;
            HRESULT hResult = m_Dx12Device.NativeDevice->CreateCommandQueue(&queueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&commandQueue);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif

            m_NativeCommandQueue = commandQueue;
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new Dx12CommandBuffer(this);
        }

        public override void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            Dx12Texture dx12Texture = tiledTextureRegions.Texture as Dx12Texture;
            int regionCount = tiledTextureRegions.Regions.Length;

            D3D12_TILED_RESOURCE_COORDINATE* coordinates = stackalloc D3D12_TILED_RESOURCE_COORDINATE[regionCount];
            D3D12_TILE_REGION_SIZE* regionSizes = stackalloc D3D12_TILE_REGION_SIZE[regionCount];
            D3D12_TILE_RANGE_FLAGS* rangeFlags = stackalloc D3D12_TILE_RANGE_FLAGS[regionCount];
            uint* heapRangeStartOffsets = stackalloc uint[regionCount];
            uint* rangeTileCounts = stackalloc uint[regionCount];

            for (int i = 0; i < regionCount; ++i)
            {
                ref RHITextureCoordinateRegion region = ref tiledTextureRegions.Regions.Span[i];

                coordinates[i].X = (uint)region.Start.X;
                coordinates[i].Y = (uint)region.Start.Y;
                coordinates[i].Z = (uint)region.Start.Z;
                coordinates[i].Subresource = (uint)(region.Layer + region.MipLevel);

                uint width = (uint)(region.End.X - region.Start.X);
                uint height = (uint)(region.End.Y - region.Start.Y);
                uint depth = (uint)(region.End.Z - region.Start.Z);
                if (width < 1) width = 1;
                if (height < 1) height = 1;
                if (depth < 1) depth = 1;
                uint numTiles = width * height * depth;

                regionSizes[i].NumTiles = numTiles;
                regionSizes[i].UseBox = true;
                regionSizes[i].Width = width;
                regionSizes[i].Height = (ushort)height;
                regionSizes[i].Depth = (ushort)depth;

                rangeFlags[i] = D3D12_TILE_RANGE_FLAGS.D3D12_TILE_RANGE_FLAG_NONE;
                heapRangeStartOffsets[i] = 0;
                rangeTileCounts[i] = numTiles;
            }

            m_NativeCommandQueue->UpdateTileMappings(
                (ID3D12Resource*)dx12Texture.NativeResource,
                (uint)regionCount,
                coordinates,
                regionSizes,
                null,
                (uint)regionCount,
                rangeFlags,
                heapRangeStartOffsets,
                rangeTileCounts,
                D3D12_TILE_MAPPING_FLAGS.D3D12_TILE_MAPPING_FLAG_NONE);
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            Dx12Texture dx12Texture = tiledTextureRegions.Texture as Dx12Texture;
            int regionCount = tiledTextureRegions.Regions.Length;

            D3D12_TILED_RESOURCE_COORDINATE* coordinates = stackalloc D3D12_TILED_RESOURCE_COORDINATE[regionCount];
            D3D12_TILE_REGION_SIZE* regionSizes = stackalloc D3D12_TILE_REGION_SIZE[regionCount];
            D3D12_TILE_RANGE_FLAGS* rangeFlags = stackalloc D3D12_TILE_RANGE_FLAGS[regionCount];
            uint* rangeTileCounts = stackalloc uint[regionCount];

            for (int i = 0; i < regionCount; ++i)
            {
                ref RHITextureCoordinateRegion region = ref tiledTextureRegions.Regions.Span[i];

                coordinates[i].X = (uint)region.Start.X;
                coordinates[i].Y = (uint)region.Start.Y;
                coordinates[i].Z = (uint)region.Start.Z;
                coordinates[i].Subresource = (uint)(region.Layer + region.MipLevel);

                uint width = (uint)(region.End.X - region.Start.X);
                uint height = (uint)(region.End.Y - region.Start.Y);
                uint depth = (uint)(region.End.Z - region.Start.Z);
                if (width < 1) width = 1;
                if (height < 1) height = 1;
                if (depth < 1) depth = 1;
                uint numTiles = width * height * depth;

                regionSizes[i].NumTiles = numTiles;
                regionSizes[i].UseBox = true;
                regionSizes[i].Width = width;
                regionSizes[i].Height = (ushort)height;
                regionSizes[i].Depth = (ushort)depth;

                rangeFlags[i] = D3D12_TILE_RANGE_FLAGS.D3D12_TILE_RANGE_FLAG_NULL;
                rangeTileCounts[i] = numTiles;
            }

            m_NativeCommandQueue->UpdateTileMappings(
                (ID3D12Resource*)dx12Texture.NativeResource,
                (uint)regionCount,
                coordinates,
                regionSizes,
                null,
                (uint)regionCount,
                rangeFlags,
                null,
                rangeTileCounts,
                D3D12_TILE_MAPPING_FLAGS.D3D12_TILE_MAPPING_FLAG_NONE);
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            for (int i = 0; i < tiledTexturePackedMips.PackedMips.Length; ++i)
            {
                ref RHITiledTexturePackedMip packedMip = ref tiledTexturePackedMips.PackedMips.Span[i];
                Dx12Texture dx12Texture = packedMip.Texture as Dx12Texture;

                D3D12_TILE_RANGE_FLAGS rangeFlag = D3D12_TILE_RANGE_FLAGS.D3D12_TILE_RANGE_FLAG_NONE;
                uint startOffset = 0;
                uint tileCount = 1;

                m_NativeCommandQueue->UpdateTileMappings(
                    (ID3D12Resource*)dx12Texture.NativeResource,
                    1,
                    null,
                    null,
                    null,
                    1,
                    &rangeFlag,
                    &startOffset,
                    &tileCount,
                    D3D12_TILE_MAPPING_FLAGS.D3D12_TILE_MAPPING_FLAG_NONE);
            }
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            for (int i = 0; i < tiledTexturePackedMips.PackedMips.Length; ++i)
            {
                ref RHITiledTexturePackedMip packedMip = ref tiledTexturePackedMips.PackedMips.Span[i];
                Dx12Texture dx12Texture = packedMip.Texture as Dx12Texture;

                D3D12_TILE_RANGE_FLAGS rangeFlag = D3D12_TILE_RANGE_FLAGS.D3D12_TILE_RANGE_FLAG_NULL;
                uint tileCount = 1;

                m_NativeCommandQueue->UpdateTileMappings(
                    (ID3D12Resource*)dx12Texture.NativeResource,
                    1,
                    null,
                    null,
                    null,
                    1,
                    &rangeFlag,
                    null,
                    &tileCount,
                    D3D12_TILE_MAPPING_FLAGS.D3D12_TILE_MAPPING_FLAG_NONE);
            }
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            if (waitSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = waitSemaphore as Dx12Semaphore;
                m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (signalSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = signalSemaphore as Dx12Semaphore;
                dx12Semaphore.NativeFence->Signal(0);
                m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        public override void Submits(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
                }
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    dx12Semaphore.NativeFence->Signal(0);
                    m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
                }
            }

            if (cmdBuffers != null)
            {
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[cmdBuffers.Length];
                for (int i = 0; i < cmdBuffers.Length; ++i)
                {
                    Dx12CommandBuffer dx12CommandBuffer = cmdBuffers[i] as Dx12CommandBuffer;
                    ppCommandLists[i] = (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList;
                }
                m_NativeCommandQueue->ExecuteCommandLists((uint)cmdBuffers.Length, ppCommandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    dx12Semaphore.NativeFence->Signal(0);
                    m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        protected override void Release()
        {
            m_NativeCommandQueue->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
