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
        internal EBarrierStage BarrierStage => m_BarrierStage;
        internal EResourceType ResourceType => m_ResourceType;
        internal RHIBufferBarrierDescriptor BufferAliasing => m_BufferAliasing;
        internal RHITextureBarrierDescriptor TextureAliasing => m_TextureAliasing;
        internal RHIBufferBarrierDescriptor BufferTransition => m_BufferTransition;
        internal RHITextureBarrierDescriptor TextureTransition => m_TextureTransition;

        private EBarrierType m_BarrierType;
        private EBarrierStage m_BarrierStage;
        private EResourceType m_ResourceType;
        private RHIBufferBarrierDescriptor m_BufferAliasing;
        private RHITextureBarrierDescriptor m_TextureAliasing;
        private RHIBufferBarrierDescriptor m_BufferTransition;
        private RHITextureBarrierDescriptor m_TextureTransition;

        public static RHIBarrier Aliasing(RHIBuffer buffer, in EOwnerState ownerState, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferAliasing.Handle = buffer;
            barrier.m_BufferAliasing.Owner = ownerState;
            barrier.m_BufferTransition.After = after;
            return barrier;
        }

        public static RHIBarrier Aliasing(RHITexture texture, in EOwnerState ownerState, in ETextureState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = EBarrierType.Aliasing;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureAliasing.Handle = texture;
            barrier.m_TextureAliasing.Owner = ownerState;
            barrier.m_TextureTransition.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHIBuffer buffer, in EOwnerState ownerState, in EBarrierStage barrierState, in EBufferState before, in EBufferState after)
        {
            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = ((before & EBufferState.UnorderedAccess) != 0) && ((after & EBufferState.UnorderedAccess) != 0) ? EBarrierType.UAV : EBarrierType.Triansition;
            barrier.m_BarrierStage = barrierState;
            barrier.m_ResourceType = EResourceType.Buffer;
            barrier.m_BufferTransition.Handle = buffer;
            barrier.m_BufferTransition.Owner = ownerState;
            barrier.m_BufferTransition.Before = before;
            barrier.m_BufferTransition.After = after;
            return barrier;
        }

        public static RHIBarrier Transition(RHITexture texture, in EOwnerState ownerState, in EBarrierStage barrierState, in ETextureState before, in ETextureState after)
        {
            bool isUAV = ((before & ETextureState.UnorderedAccess) != 0) && ((after & ETextureState.UnorderedAccess) != 0);

            RHIBarrier barrier = new RHIBarrier();
            barrier.m_BarrierType = isUAV ? EBarrierType.UAV : EBarrierType.Triansition;
            barrier.m_BarrierStage = barrierState;
            barrier.m_ResourceType = EResourceType.Texture;
            barrier.m_TextureTransition.Handle = texture;
            barrier.m_TextureTransition.Owner = ownerState;
            barrier.m_TextureTransition.Before = before;
            barrier.m_TextureTransition.After = after;
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
