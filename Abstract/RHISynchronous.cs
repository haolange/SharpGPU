using Infinity.Core;
using TerraFX.Interop.Gdiplus;

namespace Infinity.Graphics
{
#pragma warning disable CS0414
    public struct RHIBufferBarrierDescriptor
    {
        public RHIBuffer Handle;
        public ERHIBufferState SrcState;
        public ERHIBufferState DstState;
        public ERHIPipeline SrcPipeline;
        public ERHIPipeline DstPipeline;
    }

    public struct RHITextureBarrierDescriptor
    {
        public RHITexture Handle;
        public ERHITextureState SrcState;
        public ERHITextureState DstState;
        public ERHIPipeline SrcPipeline;
        public ERHIPipeline DstPipeline;
    }

    public struct RHIBarrier
    {
        internal ERHIBarrierType BarrierType => m_BarrierType;
        internal ERHIResourceType ResourceType => m_ResourceType;
        internal RHIBufferBarrierDescriptor BufferBarrierInfo => m_BufferBarrierInfo;
        internal RHITextureBarrierDescriptor TextureBarrierInfo => m_TextureBarrierInfo;

        private ERHIBarrierType m_BarrierType;
        private ERHIResourceType m_ResourceType;
        private RHIBufferBarrierDescriptor m_BufferBarrierInfo;
        private RHITextureBarrierDescriptor m_TextureBarrierInfo;

        public static RHIBarrier SyncUAV(RHIBuffer buffer, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.UAV;
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            return barrier;
        }

        public static RHIBarrier SyncUAV(RHITexture texture, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.UAV;
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHIBuffer buffer, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.Aliasing;
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHITexture texture, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.Aliasing;
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            return barrier;
        }

        public static RHIBarrier Transition(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.Triansition;
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.SrcState = srcState;
            barrier.m_BufferBarrierInfo.DstState = dstState;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState, in ERHIPipeline srcPipeline = ERHIPipeline.Graphics, in ERHIPipeline dstPipeline = ERHIPipeline.Graphics)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ERHIBarrierType.Triansition;
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.SrcState = srcState;
            barrier.m_TextureBarrierInfo.DstState = dstState;
            return barrier;
        }
    }

    public enum EFenceStatus
    {
        Success,
        NotReady,
        Undefined
    };

    public abstract class RHIFence : Disposal
    {
        public abstract EFenceStatus Status
        {
            get;
        }

        internal abstract void Reset();
        public abstract void Wait();
    }

    public abstract class RHISemaphore : Disposal
    {

    }
#pragma warning restore CS0414
}
