using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    internal sealed class MetalWorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal MetalWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalWorkGraphEncoder : RHIWorkGraphEncoder
    {
        internal MetalWorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void PushDebugGroup(string name)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void PopDebugGroup()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void WriteTimestamp(in uint index)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void SetProgram(RHIWorkGraphPipeline pipeline)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        public override void EndPass()
        {
            throw new NotSupportedException("WorkGraph is not supported on the Metal backend.");
        }

        protected override void Release()
        {
        }
    }
}
