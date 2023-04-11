using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIIndirectDispatchArgs
    {
        public uint GroupCountX;
        public uint GroupCountY;
        public uint GroupCountZ;
        public RHIIndirectDispatchArgs(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            GroupCountX = groupCountX;
            GroupCountY = groupCountY;
            GroupCountZ = groupCountZ;
        }
    }

    public struct RHIIndirectDrawArgs
    {
        public uint VertexCount;
        public uint InstanceCount;
        public uint StartVertexLocation;
        public uint StartInstanceLocation;
        public RHIIndirectDrawArgs(in uint vertexCount, in uint instanceCount, in uint startVertexLocation, in uint startInstanceLocation)
        {
            VertexCount = vertexCount;
            InstanceCount = instanceCount;
            StartVertexLocation = startVertexLocation;
            StartInstanceLocation = startInstanceLocation;
        }
    }

    public struct RHIIndirectDrawIndexedArgs
    {
        public uint IndexCount;
        public uint InstanceCount;
        public uint StartIndexLocation;
        public int BaseVertexLocation;
        public uint StartInstanceLocation;
        public RHIIndirectDrawIndexedArgs(in uint indexCount, in uint instanceCount, in uint startIndexLocation, in int baseVertexLocation, in uint startInstanceLocation)
        {
            IndexCount = indexCount;
            InstanceCount = instanceCount;
            StartIndexLocation = startIndexLocation;
            BaseVertexLocation = baseVertexLocation;
            StartInstanceLocation = startInstanceLocation;
        }
    }

    public struct RHIBufferCopyDescriptor
    {
        public uint Offset;
        public uint RowPitch;
        public uint3 TextureHeight;
        public RHIBuffer Buffer;
    }

    public struct RHITextureCopyDescriptor
    {
        public uint MipLevel;
        public uint SliceBase;
        public uint SliceCount;
        public uint3 Origin;
        public RHITexture Texture;
    }

    public struct RHISubpassDescriptor
    {
        public bool bUaseDepthStencil;
        public ReadOnlyMemory<byte>? InputAttachmentIndex;
        public ReadOnlyMemory<byte>? OutputAttachmentIndex;
    }

    public struct RHIShadingRateDescriptor
    {
        public EShadingRate ShadingRate;
        public RHITexture? ShadingRateTexture;
        public EShadingRateCombiner ShadingRateCombiner;

        public RHIShadingRateDescriptor(in EShadingRate shadingRate, in EShadingRateCombiner shadingRateCombiner = EShadingRateCombiner.Max)
        {
            ShadingRate = shadingRate;
            ShadingRateTexture = null;
            ShadingRateCombiner = shadingRateCombiner;
        }

        public RHIShadingRateDescriptor(RHITexture shadingRateTexture, in EShadingRateCombiner shadingRateCombiner = EShadingRateCombiner.Max)
        {
            ShadingRate = EShadingRate.Rate1x1;
            ShadingRateTexture = shadingRateTexture;
            ShadingRateCombiner = shadingRateCombiner;
        }
    }

    public struct RHIColorAttachmentDescriptor
    {
        public float4 ClearValue;
        public ELoadAction LoadAction;
        public EStoreAction StoreAction;
        public RHITexture RenderTarget;
        public RHITexture ResolveTarget;
    }

    public struct RHIDepthStencilAttachmentDescriptor
    {
        public bool DepthReadOnly;
        public float DepthClearValue;
        public ELoadAction DepthLoadAction;
        public EStoreAction DepthStoreAction;
        public bool StencilReadOnly;
        public int StencilClearValue;
        public ELoadAction StencilLoadAction;
        public EStoreAction StencilStoreAction;
        public RHITexture DepthStencilTarget;
        public RHITexture DepthStencilResolve;
    }

    public struct RHIMeshletPassDescriptor
    {
        public string Name;
        public RHIShadingRateDescriptor? ShadingRateDescriptor;
        public Memory<RHIColorAttachmentDescriptor> ColorAttachmentDescriptors;
        public RHIDepthStencilAttachmentDescriptor? DepthStencilAttachmentDescriptor;
    }

    public struct RHIGraphicsPassDescriptor
    {
        public string Name;
        public RHIShadingRateDescriptor? ShadingRateDescriptor;
        public Memory<RHISubpassDescriptor>? SubpassDescriptors;
        public Memory<RHIColorAttachmentDescriptor> ColorAttachmentDescriptors;
        public RHIDepthStencilAttachmentDescriptor? DepthStencilAttachmentDescriptor;
    }

    public struct RHIBlitPassScoper : IDisposable
    {
        RHIBlitEncoder m_BlitEncoder;

        internal RHIBlitPassScoper(RHIBlitEncoder blitEncoder)
        {
            m_BlitEncoder = blitEncoder;
        }

        public void Dispose()
        {
            m_BlitEncoder.EndPass();
        }
    }

    public struct RHIComputePassScoper : IDisposable
    {
        RHIComputeEncoder m_ComputeEncoder;

        internal RHIComputePassScoper(RHIComputeEncoder computeEncoder)
        {
            m_ComputeEncoder = computeEncoder;
        }

        public void Dispose()
        {
            m_ComputeEncoder.EndPass();
        }
    }

    public struct RHIMeshletPassScoper : IDisposable
    {
        RHIMeshletEncoder m_MeshletEncoder;

        internal RHIMeshletPassScoper(RHIMeshletEncoder meshletEncoder)
        {
            m_MeshletEncoder = meshletEncoder;
        }

        public void Dispose()
        {
            m_MeshletEncoder.EndPass();
        }
    }

    public struct RHIGraphicsPassScoper : IDisposable
    {
        RHIGraphicsEncoder m_GraphicsEncoder;

        internal RHIGraphicsPassScoper(RHIGraphicsEncoder graphicsEncoder)
        {
            m_GraphicsEncoder = graphicsEncoder;
        }

        public void Dispose()
        {
            m_GraphicsEncoder.EndPass();
        }
    }

    public struct RHIRaytracingPassScoper : IDisposable
    {
        RHIRaytracingEncoder m_RaytracingEncoder;

        internal RHIRaytracingPassScoper(RHIRaytracingEncoder raytracingEncoder)
        {
            m_RaytracingEncoder = raytracingEncoder;
        }

        public void Dispose()
        {
            m_RaytracingEncoder.EndPass();
        }
    }

    public abstract class RHIBlitEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;

        public RHIBlitPassScoper BeginScopedPass(string name)
        {
            BeginPass(name);
            return new RHIBlitPassScoper(this);
        }

        public abstract void BeginPass(string name);
        public abstract void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size);
        public abstract void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public abstract void ResourceBarrier(in RHIBarrier barrier);
        public abstract void ResourceBarrier(in Memory<RHIBarrier> barriers);
        public abstract void BeginQuery(RHIQuery query, in uint index);
        public abstract void EndQuery(RHIQuery query, in uint index);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void EndPass();
        // TODO WriteTimeStamp(...)
        // TODO ResolveQuery(...)
    }

    public abstract class RHIComputeEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIPipelineLayout? m_PipelineLayout;
        protected RHIComputePipeline? m_Pipeline;

        public RHIComputePassScoper BeginScopedPass(string name)
        {
            BeginPass(name);
            return new RHIComputePassScoper(this);
        }

        public abstract void BeginPass(string name);
        public abstract void SetPipeline(RHIComputePipeline pipeline);
        //public abstract void SetPipelineLayout(RHIPipelineLayout pipelineLayout);
        public abstract void SetBindGroup(RHIBindGroup bindGroup);
        public abstract void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset);
        //public abstract void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer);
        public abstract void BeginQuery(RHIQuery query, in uint index);
        public abstract void EndQuery(RHIQuery query, in uint index);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void EndPass();
    }

    public abstract class RHIRaytracingEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIPipelineLayout? m_PipelineLayout;
        protected RHIRaytracingPipeline? m_Pipeline;

        public RHIRaytracingPassScoper BeginScopedPass(string name)
        {
            BeginPass(name);
            return new RHIRaytracingPassScoper(this);
        }

        public abstract void BeginPass(string name);
        public abstract void SetPipeline(RHIRaytracingPipeline pipeline);
        //public abstract void SetPipelineLayout(RHIPipelineLayout pipelineLayout);
        public abstract void SetBindGroup(RHIBindGroup bindGroup);
        public abstract void BuildAccelerationStructure(RHITopLevelAccelStruct tlas);
        public abstract void BuildAccelerationStructure(RHIBottomLevelAccelStruct blas);
        public abstract void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable);
        //public abstract void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer);
        public abstract void BeginQuery(RHIQuery query, in uint index);
        public abstract void EndQuery(RHIQuery query, in uint index);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void EndPass();
    }
    
    public abstract class RHIMeshletEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIPipelineLayout? m_PipelineLayout;
        protected RHIMeshletPipeline? m_Pipeline;

        public RHIMeshletPassScoper BeginScopedPass(in RHIMeshletPassDescriptor descriptor)
        {
            BeginPass(descriptor);
            return new RHIMeshletPassScoper(this);
        }

        public abstract void BeginPass(in RHIMeshletPassDescriptor descriptor);
        public abstract void SetScissor(in Rect rect);
        public abstract void SetScissors(in Memory<Rect> rects);
        public abstract void SetViewport(in Viewport viewport);
        public abstract void SetViewports(in Memory<Viewport> viewports);
        public abstract void SetStencilRef(in uint value);
        public abstract void SetBlendFactor(in float4 value);
        public abstract void NextSubpass();
        public abstract void SetPipeline(RHIMeshletPipeline pipeline);
        //public abstract void SetPipelineLayout(RHIPipelineLayout pipelineLayout);
        public abstract void SetBindGroup(RHIBindGroup bindGroup);
        public abstract void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset);
        //public abstract void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer);
        public abstract void BeginQuery(RHIQuery query, in uint index);
        public abstract void EndQuery(RHIQuery query, in uint index);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void EndPass();
    }

    public abstract class RHIGraphicsEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIPipelineLayout? m_PipelineLayout;
        protected RHIGraphicsPipeline? m_Pipeline;

        public RHIGraphicsPassScoper BeginScopedPass(in RHIGraphicsPassDescriptor descriptor)
        {
            BeginPass(descriptor);
            return new RHIGraphicsPassScoper(this);
        }

        public abstract void BeginPass(in RHIGraphicsPassDescriptor descriptor);
        public abstract void SetScissor(in Rect rect);
        public abstract void SetScissors(in Memory<Rect> rects);
        public abstract void SetViewport(in Viewport viewport);
        public abstract void SetViewports(in Memory<Viewport> viewports);
        public abstract void SetStencilRef(in uint value);
        public abstract void SetBlendFactor(in float4 value);
        public abstract void NextSubpass();
        public abstract void SetPipeline(RHIGraphicsPipeline pipeline);
        //public abstract void SetPipelineLayout(RHIPipelineLayout pipelineLayout);
        public abstract void SetBindGroup(RHIBindGroup bindGroup);
        public abstract void SetVertexBuffer(RHIBuffer buffer, in uint slot = 0, in uint offset = 0);
        public abstract void SetIndexBuffer(RHIBuffer buffer, in EIndexFormat format, in uint offset = 0);
        public abstract void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance);
        public abstract void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance);
        public abstract void DrawIndirect(RHIBuffer argsBuffer, in uint offset);
        public abstract void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset);
        //public abstract void ExecuteBundles(RHIIndirectCommandBuffer indirectCommandBuffer);
        public abstract void BeginQuery(RHIQuery query, in uint index);
        public abstract void EndQuery(RHIQuery query, in uint index);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void EndPass();
    }
}
