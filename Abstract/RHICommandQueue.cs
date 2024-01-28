using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHICoordinate
    {
        public int X;
        public int Y;
        public int Z;
    };

    public struct RHICoordinateRegion
    {
        public RHICoordinate Start;
        public RHICoordinate End;
    };

    public struct RHITextureCoordinateRegion
    {
        public int Layer;
        public int MipLevel;
        public RHICoordinate Start;
        public RHICoordinate End;
    };

    public struct RHITiledTextureRegions
    {
        public RHITexture Texture;
        public Memory<RHITextureCoordinateRegion> Regions;
    };

    public struct RHITiledTexturePackedMip
    {
        public int Layer;
        public RHITexture Texture;
    };

    public struct RHITiledTexturePackedMips
    {
        public Memory<RHITiledTexturePackedMip> PackedMips;
    };

    public abstract class RHICommandQueue : Disposal
    {
        public ERHIPipelineType PipelineType
        {
            get
            {
                return m_PipelineType;
            }
        }
        public abstract ulong Frequency
        {
            get;
        }

        protected ERHIPipelineType m_PipelineType;
        public abstract RHICommandBuffer CreateCommandBuffer();
        public abstract void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions);
        public abstract void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions);
        public abstract void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips);
        public abstract void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips);
        public abstract void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore);
        public abstract void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores);
    }
}
