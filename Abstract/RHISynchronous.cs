using Infinity.Core;

namespace Infinity.Graphics
{
#pragma warning disable CS0414
    public struct RHIBufferUAVBarrierDescriptor
    {
        public RHIBuffer Handle;
        public EOwnerState Owner;
    }

    public struct RHITextureUAVBarrierDescriptor
    {
        public RHITexture Handle;
        public EOwnerState Owner;
    }

    public struct RHIBufferAliasingBarrierDescriptor
    {
        public RHIBuffer Handle;
        public EOwnerState Owner;
    }

    public struct RHITextureAliasingBarrierDescriptor
    {
        public RHITexture Handle;
        public EOwnerState Owner;
    }

    public struct RHIBufferTransitionBarrierDescriptor
    {
        public RHIBuffer Handle;
        public EOwnerState Owner;
        public EBufferState Before;
        public EBufferState After;
    }

    public struct RHITextureTransitionBarrierDescriptor
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
        internal RHIBufferUAVBarrierDescriptor BufferUAVBarrier => m_BufferUAVBarrier;
        internal RHITextureUAVBarrierDescriptor TextureUAVBarrier => m_TextureUAVBarrier;
        internal RHIBufferAliasingBarrierDescriptor BufferAliasingBarrier => m_BufferAliasingBarrier;
        internal RHITextureAliasingBarrierDescriptor TextureAliasingBarrier => m_TextureAliasingBarrier;
        internal RHIBufferTransitionBarrierDescriptor BufferTransitionBarrier => m_BufferTransitionBarrier;
        internal RHITextureTransitionBarrierDescriptor TextureTransitionBarrier => m_TextureTransitionBarrier;

        private EBarrierType m_BarrierType;
        private EResourceType m_ResourceType;
        private RHIBufferUAVBarrierDescriptor m_BufferUAVBarrier;
        private RHITextureUAVBarrierDescriptor m_TextureUAVBarrier;
        private RHIBufferAliasingBarrierDescriptor m_BufferAliasingBarrier;
        private RHITextureAliasingBarrierDescriptor m_TextureAliasingBarrier;
        private RHIBufferTransitionBarrierDescriptor m_BufferTransitionBarrier;
        private RHITextureTransitionBarrierDescriptor m_TextureTransitionBarrier;

        public static RHIBarrier UAV(RHIBuffer buffer, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferUAVBarrier.Handle = buffer;
            barrier.m_BufferUAVBarrier.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier UAV(RHITexture texture, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.UAV;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureUAVBarrier.Handle = texture;
            barrier.m_TextureUAVBarrier.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHIBuffer buffer, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferAliasingBarrier.Handle = buffer;
            barrier.m_BufferAliasingBarrier.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHITexture texture, in EOwnerState ownerState)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureAliasingBarrier.Handle = texture;
            barrier.m_TextureAliasingBarrier.Owner = ownerState;
            return barrier;
        }

        public static RHIBarrier Transition(RHIBuffer buffer, in EOwnerState ownerState, in EBufferState before, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferTransitionBarrier.Handle = buffer;
            barrier.m_BufferTransitionBarrier.Owner = ownerState;
            barrier.m_BufferTransitionBarrier.Before = before;
            barrier.m_BufferTransitionBarrier.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in EOwnerState ownerState, in ETextureState before, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Triansition;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureTransitionBarrier.Handle = texture;
            barrier.m_TextureTransitionBarrier.Owner = ownerState;
            barrier.m_TextureTransitionBarrier.Before = before;
            barrier.m_TextureTransitionBarrier.After = after;
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
