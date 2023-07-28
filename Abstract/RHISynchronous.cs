using Infinity.Core;
using TerraFX.Interop.Gdiplus;

namespace Infinity.Graphics
{
#pragma warning disable CS0414
    public struct RHIBufferBarrierDescriptor
    {
        public RHIBuffer Handle;
        public EOwnerState Owner;
        public EBufferState Before;
        public EBufferState After;
    }

    public struct RHITextureBarrierDescriptor
    {
        public RHITexture Handle;
        public EOwnerState Owner;
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

        public static RHIBarrier SyncUAV(RHIBuffer buffer, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier SyncUAV(RHITexture texture, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHIBuffer buffer, in EOwnerState ownerState, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.Owner = ownerState;
            barrier.m_BufferBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHITexture texture, in EOwnerState ownerState, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.Owner = ownerState;
            barrier.m_TextureBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHIBuffer buffer, in EOwnerState ownerState, in EBufferState before, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.Owner = ownerState;
            barrier.m_BufferBarrierInfo.Before = before;
            barrier.m_BufferBarrierInfo.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in EOwnerState ownerState, in ETextureState before, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.Owner = ownerState;
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
