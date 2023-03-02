using Infinity.Core;

namespace Infinity.Graphics
{
#pragma warning disable CS0414
    public struct RHIBufferTransitionDescriptor
    {
        public RHIBuffer Handle;
        public EBufferState Before;
        public EBufferState After;
    }

    public struct RHITextureTransitionDescriptor
    {
        public RHITexture Handle;
        public ETextureState Before;
        public ETextureState After;
    }

    public struct RHIBarrier
    {
        public EResourceType Type => m_Type;
        public RHIBufferTransitionDescriptor Buffer => m_Buffer;
        public RHITextureTransitionDescriptor Texture => m_Texture;

        private EResourceType m_Type;
        private RHIBufferTransitionDescriptor m_Buffer;
        private RHITextureTransitionDescriptor m_Texture;

        public static RHIBarrier Transition(RHIBuffer buffer, in EBufferState before, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Type = EResourceType.Buffer;
            barrier.m_Buffer.Handle = buffer;
            barrier.m_Buffer.Before = before;
            barrier.m_Buffer.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in ETextureState before, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_Type = EResourceType.Texture;
            barrier.m_Texture.Handle = texture;
            barrier.m_Texture.Before = before;
            barrier.m_Texture.After = after;
            return barrier;
        }
    }

    public enum EFenceStatus
    {
        Success,
        NotReady,
        MAX
    };

    public abstract class RHIFence : Disposal
    {
        public abstract EFenceStatus Status
        {
            get;
        }

        public abstract void Reset();
        public abstract void Wait();
    }

    public abstract class RHISemaphore : Disposal
    {

    }
#pragma warning restore CS0414
}
