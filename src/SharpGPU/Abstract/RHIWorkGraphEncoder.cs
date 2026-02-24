using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIWorkGraphPipelineDescriptor
    {
        public string Name;
        public RHIFunctionLibrary FunctionLibrary;
        public RHIPipelineLayout PipelineLayout;
    }

    public struct RHIWorkGraphPassDescriptor
    {
        public string Name;
        public RHITimestampDescriptor? Timestamp;
    }

    public abstract class RHIWorkGraphPipeline : Disposal
    {
        public RHIWorkGraphPipelineDescriptor Descriptor => m_Descriptor;

        protected RHIWorkGraphPipelineDescriptor m_Descriptor;
    }

    public abstract class RHIWorkGraphEncoder : Disposal
    {
        protected RHICommandBuffer? m_CommandBuffer;
        protected RHIWorkGraphPipeline? m_CachedPipeline;

        internal abstract void BeginPass(in RHIWorkGraphPassDescriptor descriptor);
        public abstract void ResourceBarrier(in RHIResourceBarrier barrier);
        public abstract void ResourceBarriers(in Memory<RHIResourceBarrier> barriers);
        public abstract void PushDebugGroup(string name);
        public abstract void PopDebugGroup();
        public abstract void WriteTimestamp(in uint index);
        public abstract void SetProgram(RHIWorkGraphPipeline pipeline);
        public abstract void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize);
        public abstract void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null);
        public abstract void EndPass();
    }
}
