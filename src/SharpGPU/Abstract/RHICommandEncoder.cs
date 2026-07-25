using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
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

    public struct RHITimestampDescriptor
    {
        public RHIQuery Query;
        public uint BeginIndex;
        public uint EndIndex;
    }

    public struct RHIOcclusionDescriptor
    {
        public RHIQuery Query;
    }

    public struct RHIStatisticsDescriptor
    {
        public RHIQuery Query;
        public uint WriteIndex;
    }

    public struct RHITransferPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public struct RHIComputePassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
        public RHIStatisticsDescriptor? Statistics;
    }

    public struct RHIRayTracingPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
        public RHIStatisticsDescriptor? Statistics;
    }

    public abstract class RHITransferEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;

        internal abstract void BeginPass(in RHITransferPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount);
        public abstract void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size);
        public abstract void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size);
        public abstract void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size);
        public void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);
            EndPassCore();
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal abstract void EndPassCore();
    }

    public abstract class RHIComputeEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIComputePipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIComputePassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void BeginStatistics(in uint index);
        public abstract void EndStatistics(in uint index);
        public abstract void SetPipeline(RHIComputePipeline pipeline);
        public abstract void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex);
        public abstract void SetPushConstants(IntPtr data, in uint size, in uint offset = 0);
        public abstract void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset);
        public void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The compute encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Compute);
            EndPassCore();
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal abstract void EndPassCore();
    }

    public abstract class RHIRaytracingEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIRaytracingPipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void BeginStatistics(in uint index);
        public abstract void EndStatistics(in uint index);
        public abstract void SetPipeline(RHIRaytracingPipeline pipeline);
        public abstract void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex);
        public abstract void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct);
        public abstract void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct);
        public abstract void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable);
        public abstract void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable);
        public void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The ray-tracing encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.RayTracing);
            EndPassCore();
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal abstract void EndPassCore();
    }
    
    public struct RHIMLPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public abstract class RHIMLEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIMLPipeline? m_CachedPipeline;
        protected RHIMLBindingSet? m_CachedBindingSet;

        internal abstract void BeginPass(in RHIMLPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void SetPipeline(RHIMLPipeline pipeline);
        public abstract void SetBindingSet(RHIMLBindingSet bindingSet);
        public abstract void Dispatch();
        public void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The machine-learning encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.MachineLearning);
            EndPassCore();
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal abstract void EndPassCore();
    }

    public struct RHIWorkGraphPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public abstract class RHIWorkGraphEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIWorkGraphPipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIWorkGraphPassDescriptor descriptor);
        public abstract void Barrier(in RHIBarrier barrier);
        public abstract void Barriers(ReadOnlySpan<RHIBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void SetPipeline(RHIWorkGraphPipeline pipeline);
        public abstract void SetArgumentTable(RHIArgumentTable resourceTable, in uint tableIndex);
        public abstract void SetPushConstants(IntPtr data, in uint size, in uint offset = 0);
        public abstract void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize);
        public abstract void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null);
        public void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The work-graph encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.WorkGraph);
            EndPassCore();
            commandBuffer.MarkEncoderEndFromEncoder();
        }

        internal abstract void EndPassCore();
    }
}
