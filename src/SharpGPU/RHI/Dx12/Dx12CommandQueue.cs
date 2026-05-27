using System;

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
        public Vortice.Direct3D12.ID3D12CommandQueue NativeCommandQueue
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
                m_NativeCommandQueue.GetTimestampFrequency(out ulong result);
                return result;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12CommandQueue m_NativeCommandQueue;

        public Dx12CommandQueue(Dx12Device device, in ERHIPipelineType pipeline)
        {
            m_Dx12Device = device;
            m_PipelineType = pipeline;

            Vortice.Direct3D12.CommandQueueDescription queueDesc = new Vortice.Direct3D12.CommandQueueDescription();
            queueDesc.Flags = Vortice.Direct3D12.CommandQueueFlags.None;
            queueDesc.Type = Dx12Utility.ConvertToDx12QueueType(pipeline);

            Vortice.Direct3D12.ID3D12CommandQueue commandQueue;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommandQueue(queueDesc, out commandQueue);
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

            Vortice.Direct3D12.TiledResourceCoordinate[] coordinates = new Vortice.Direct3D12.TiledResourceCoordinate[regionCount];
            Vortice.Direct3D12.TileRegionSize[] regionSizes = new Vortice.Direct3D12.TileRegionSize[regionCount];
            Vortice.Direct3D12.TileRangeFlags[] rangeFlags = new Vortice.Direct3D12.TileRangeFlags[regionCount];
            int[] heapRangeStartOffsets = new int[regionCount];
            int[] rangeTileCounts = new int[regionCount];

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

                rangeFlags[i] = Vortice.Direct3D12.TileRangeFlags.None;
                heapRangeStartOffsets[i] = 0;
                rangeTileCounts[i] = (int)numTiles;
            }

            m_NativeCommandQueue.UpdateTileMappings(
                (Vortice.Direct3D12.ID3D12Resource)dx12Texture.NativeResource,
                coordinates,
                regionSizes,
                null,
                rangeFlags,
                heapRangeStartOffsets,
                rangeTileCounts,
                Vortice.Direct3D12.TileMappingFlags.None);
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            Dx12Texture dx12Texture = tiledTextureRegions.Texture as Dx12Texture;
            int regionCount = tiledTextureRegions.Regions.Length;

            Vortice.Direct3D12.TiledResourceCoordinate[] coordinates = new Vortice.Direct3D12.TiledResourceCoordinate[regionCount];
            Vortice.Direct3D12.TileRegionSize[] regionSizes = new Vortice.Direct3D12.TileRegionSize[regionCount];
            Vortice.Direct3D12.TileRangeFlags[] rangeFlags = new Vortice.Direct3D12.TileRangeFlags[regionCount];
            int[] rangeTileCounts = new int[regionCount];

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

                rangeFlags[i] = Vortice.Direct3D12.TileRangeFlags.Null;
                rangeTileCounts[i] = (int)numTiles;
            }

            m_NativeCommandQueue.UpdateTileMappings(
                (Vortice.Direct3D12.ID3D12Resource)dx12Texture.NativeResource,
                coordinates,
                regionSizes,
                null,
                rangeFlags,
                null,
                rangeTileCounts,
                Vortice.Direct3D12.TileMappingFlags.None);
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            for (int i = 0; i < tiledTexturePackedMips.PackedMips.Length; ++i)
            {
                ref RHITiledTexturePackedMip packedMip = ref tiledTexturePackedMips.PackedMips.Span[i];
                Dx12Texture dx12Texture = packedMip.Texture as Dx12Texture;

                Vortice.Direct3D12.TileRangeFlags[] rangeFlag = { Vortice.Direct3D12.TileRangeFlags.None };
                int[] startOffset = { 0 };
                int[] tileCount = { 1 };

                m_NativeCommandQueue.UpdateTileMappings(
                    (Vortice.Direct3D12.ID3D12Resource)dx12Texture.NativeResource,
                    Array.Empty<Vortice.Direct3D12.TiledResourceCoordinate>(),
                    Array.Empty<Vortice.Direct3D12.TileRegionSize>(),
                    null,
                    rangeFlag,
                    startOffset,
                    tileCount,
                    Vortice.Direct3D12.TileMappingFlags.None);
            }
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            for (int i = 0; i < tiledTexturePackedMips.PackedMips.Length; ++i)
            {
                ref RHITiledTexturePackedMip packedMip = ref tiledTexturePackedMips.PackedMips.Span[i];
                Dx12Texture dx12Texture = packedMip.Texture as Dx12Texture;

                Vortice.Direct3D12.TileRangeFlags[] rangeFlag = { Vortice.Direct3D12.TileRangeFlags.Null };
                int[] tileCount = { 1 };

                m_NativeCommandQueue.UpdateTileMappings(
                    (Vortice.Direct3D12.ID3D12Resource)dx12Texture.NativeResource,
                    Array.Empty<Vortice.Direct3D12.TiledResourceCoordinate>(),
                    Array.Empty<Vortice.Direct3D12.TileRegionSize>(),
                    null,
                    rangeFlag,
                    null,
                    tileCount,
                    Vortice.Direct3D12.TileMappingFlags.None);
            }
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            if (waitSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = waitSemaphore as Dx12Semaphore;
                ulong waitValue = dx12Semaphore.LastSignaledValue;
                if (waitValue > 0)
                {
                    m_NativeCommandQueue.Wait(dx12Semaphore.NativeFence, waitValue);
                }
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                Vortice.Direct3D12.ID3D12CommandList[] commandLists = { dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue.ExecuteCommandLists(commandLists);
            }

            if (signalSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = signalSemaphore as Dx12Semaphore;
                ulong signalValue = dx12Semaphore.PrepareSignalValue();
                m_NativeCommandQueue.Signal(dx12Semaphore.NativeFence, signalValue);
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                ulong signalValue = dx12Fence.ConsumeSignalValue();
                m_NativeCommandQueue.Signal(dx12Fence.NativeFence, signalValue);
            }
        }

        public override void Submits(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    ulong waitValue = dx12Semaphore.LastSignaledValue;
                    if (waitValue > 0)
                    {
                        m_NativeCommandQueue.Wait(dx12Semaphore.NativeFence, waitValue);
                    }
                }
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                Vortice.Direct3D12.ID3D12CommandList[] commandLists = { dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue.ExecuteCommandLists(commandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    ulong signalValue = dx12Semaphore.PrepareSignalValue();
                    m_NativeCommandQueue.Signal(dx12Semaphore.NativeFence, signalValue);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                ulong signalValue = dx12Fence.ConsumeSignalValue();
                m_NativeCommandQueue.Signal(dx12Fence.NativeFence, signalValue);
            }
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    ulong waitValue = dx12Semaphore.LastSignaledValue;
                    if (waitValue > 0)
                    {
                        m_NativeCommandQueue.Wait(dx12Semaphore.NativeFence, waitValue);
                    }
                }
            }

            if (cmdBuffers != null)
            {
                Vortice.Direct3D12.ID3D12CommandList[] commandLists = new Vortice.Direct3D12.ID3D12CommandList[cmdBuffers.Length];
                for (int i = 0; i < cmdBuffers.Length; ++i)
                {
                    Dx12CommandBuffer dx12CommandBuffer = cmdBuffers[i] as Dx12CommandBuffer;
                    commandLists[i] = dx12CommandBuffer.NativeCommandList;
                }
                m_NativeCommandQueue.ExecuteCommandLists(commandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    ulong signalValue = dx12Semaphore.PrepareSignalValue();
                    m_NativeCommandQueue.Signal(dx12Semaphore.NativeFence, signalValue);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                ulong signalValue = dx12Fence.ConsumeSignalValue();
                m_NativeCommandQueue.Signal(dx12Fence.NativeFence, signalValue);
            }
        }

        protected override void Release()
        {
            m_NativeCommandQueue.Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
