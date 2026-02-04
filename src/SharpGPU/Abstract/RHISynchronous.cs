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
        public ERHIPipelineStage SrcStage;
        public ERHIPipelineStage DstStage;
        public ERHIPipelineType SrcPipeline;
        public ERHIPipelineType DstPipeline;
    }

    public struct RHITextureBarrierDescriptor
    {
        public RHITexture Handle;
        public ERHITextureState SrcState;
        public ERHITextureState DstState;
        public ERHIPipelineStage SrcStage;
        public ERHIPipelineStage DstStage;
        public ERHIPipelineType SrcPipeline;
        public ERHIPipelineType DstPipeline;
    }

    public struct RHIResourceBarrier
    {
        internal ERHIResourceType ResourceType => m_ResourceType;
        internal ERHIResourceBarrierType ResourceBarrierType => m_ResourceBarrierType;
        internal RHIBufferBarrierDescriptor BufferBarrierInfo => m_BufferBarrierInfo;
        internal RHITextureBarrierDescriptor TextureBarrierInfo => m_TextureBarrierInfo;

        private ERHIResourceType m_ResourceType;
        private ERHIResourceBarrierType m_ResourceBarrierType;
        private RHIBufferBarrierDescriptor m_BufferBarrierInfo;
        private RHITextureBarrierDescriptor m_TextureBarrierInfo;

        public static RHIResourceBarrier SyncUAV(RHIBuffer buffer, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.UAV;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            return barrier;
        }

        public static RHIResourceBarrier SyncUAV(RHITexture texture, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.UAV;
            barrier.m_TextureBarrierInfo.Handle = texture;
            return barrier;
        }

        public static RHIResourceBarrier Aliasing(RHIBuffer buffer, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.Aliasing;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.SrcPipeline = srcPipeline;
            barrier.m_BufferBarrierInfo.DstPipeline = dstPipeline;
            return barrier;
        }

        public static RHIResourceBarrier Aliasing(RHITexture texture, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.Aliasing;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.SrcPipeline = srcPipeline;
            barrier.m_TextureBarrierInfo.DstPipeline = dstPipeline;
            return barrier;
        }

        public static RHIResourceBarrier Transition(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState, in ERHIPipelineStage srcStage, in ERHIPipelineStage dstStage, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Buffer;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.Triansition;
            barrier.m_BufferBarrierInfo.Handle = buffer;
            barrier.m_BufferBarrierInfo.SrcState = srcState;
            barrier.m_BufferBarrierInfo.DstState = dstState;
            barrier.m_BufferBarrierInfo.SrcStage = srcStage;
            barrier.m_BufferBarrierInfo.DstStage = dstStage;
            barrier.m_BufferBarrierInfo.SrcPipeline = srcPipeline;
            barrier.m_BufferBarrierInfo.DstPipeline = dstPipeline;
            return barrier;
        }

        public static RHIResourceBarrier Transition(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState, in ERHIPipelineStage srcStage, in ERHIPipelineStage dstStage, in ERHIPipelineType srcPipeline = ERHIPipelineType.Graphics, in ERHIPipelineType dstPipeline = ERHIPipelineType.Graphics)
        {
            RHIResourceBarrier barrier = new RHIResourceBarrier();
            barrier.m_ResourceType = ERHIResourceType.Texture;
            barrier.m_ResourceBarrierType = ERHIResourceBarrierType.Triansition;
            barrier.m_TextureBarrierInfo.Handle = texture;
            barrier.m_TextureBarrierInfo.SrcState = srcState;
            barrier.m_TextureBarrierInfo.DstState = dstState;
            barrier.m_TextureBarrierInfo.SrcStage = srcStage;
            barrier.m_TextureBarrierInfo.DstStage = dstStage;
            barrier.m_TextureBarrierInfo.SrcPipeline = srcPipeline;
            barrier.m_TextureBarrierInfo.DstPipeline = dstPipeline;
            return barrier;
        }
    }

    public enum EFenceStatus : byte
    {
        Success = 0,
        NotReady = 1,
        Undefined
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
