using SharpGPU.Core;

namespace SharpGPU
{
#pragma warning disable CS8618
    public abstract class RHICommandBuffer : Disposal
    {
        public RHICommandQueue CommandQueue
        {
            get
            {
                return m_CommandQueue;
            }
        }

        internal uint OcclusionQueryIndex;
        internal uint TimestampQueryIndex;
        internal uint StatisticsQueryIndex;
        internal RHIQuery? OcclusionQueryHeap;
        internal RHIQuery? TimestampQueryHeap;
        internal RHIQuery? StatisticsQueryHeap;

        protected RHICommandQueue m_CommandQueue;

        public abstract void Begin(string name);
        public abstract RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor);
        public abstract void EndTransferPass();
        public abstract RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor);
        public abstract void EndComputePass();
        public abstract RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void EndRaytracingPass();
        public abstract RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor);
        public abstract void EndRasterPass();
        public abstract RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor);
        public abstract void EndMLPass();
        public abstract RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor);
        public abstract void EndWorkGraphPass();
        public abstract void End();
        public abstract RHITransferEncoder GetTransferEncoder();
        public abstract RHIComputeEncoder GetComputeEncoder();
        public abstract RHIRaytracingEncoder GetRaytracingEncoder();
        public abstract RHIRasterEncoder GetRasterEncoder();
        public abstract RHIMLEncoder GetMLEncoder();
        public abstract RHIWorkGraphEncoder GetWorkGraphEncoder();
    }

    public struct RHIComputeIndirectCommandBufferDescription
    {
        public uint MaxCommandCount;
    }

    public abstract class RHIComputeIndirectCommandBuffer : Disposal
    {

    }

    public struct RHIRayTracingIndirectCommandBufferDescription
    {
        public uint MaxCommandCount;
    }

    public abstract class RHIRayTracingIndirectCommandBuffer : Disposal
    {

    }

    public struct RHIRasterIndirectCommandBufferDescription
    {
        public uint MaxCommandCount;
    }

    public abstract class RHIRasterIndirectCommandBuffer : Disposal
    {

    }
#pragma warning restore CS8618
}
