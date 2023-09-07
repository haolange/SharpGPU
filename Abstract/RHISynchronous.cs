using Infinity.Core;
using TerraFX.Interop.Gdiplus;

namespace Infinity.Graphics
{
#pragma warning disable CS0414
    public struct RHIBufferBarrierDescriptor
    {
        public RHIBuffer Handle;
        public EBufferState Before;
        public EBufferState After;
    }

    public struct RHITextureBarrierDescriptor
    {
        public RHITexture Handle;
        public ETextureState Before;
        public ETextureState After;
    }

    public struct RHIBarrier
    {
        internal EBarrierType BarrierType => m_BarrierType;
        internal EResourceType ResourceType => m_ResourceType;
        internal RHIBufferBarrierDescriptor BufferBarrierInfo => m_BufferBarrierInfo;
        internal RHITextureBarrierDescriptor TextureBarrierInfo => m_TextureBarrierInfo;

        private EBarrierType m_BarrierType;
        private EResourceType m_ResourceType;
        private RHIBufferBarrierDescriptor m_BufferBarrierInfo;
        private RHITextureBarrierDescriptor m_TextureBarrierInfo;

        public static RHIBarrier SyncUAV(RHIBuffer buffer)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            return barrier;
        }

        public static RHIBarrier SyncUAV(RHITexture texture)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHIBuffer buffer, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHITexture texture, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHIBuffer buffer, in EBufferState before, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.Before = before;
            barrier.m_BufferBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in ETextureState before, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.Before = before;
            barrier.m_TextureBarrierInfo.After = after;
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
