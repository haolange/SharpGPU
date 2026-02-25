using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    internal sealed class Dx12WorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal Dx12WorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
            // TODO(ROADMAP-P2-1): Release COM resources after TerraFX SDK upgrade.
        }
    }

    internal sealed class Dx12WorkGraphEncoder : RHIWorkGraphEncoder
    {
        internal Dx12WorkGraphEncoder(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void PushDebugGroup(string name)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void PopDebugGroup()
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void WriteTimestamp(in uint index)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void SetProgram(RHIWorkGraphPipeline pipeline)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void SetBackingMemory(RHIBuffer backingMemory, ulong byteOffset, ulong byteSize)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void DispatchGraph(string entrypoint, uint numRecords, ulong inputRecordByteStride, RHIBuffer? inputRecordBuffer = null)
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        public override void EndPass()
        {
            throw new NotImplementedException("WorkGraph not yet implemented. Requires TerraFX SDK upgrade. Tracked: ROADMAP.md P2-1.");
        }

        protected override void Release()
        {
        }
    }
}
