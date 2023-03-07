using System;
using Infinity.Core;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    public struct RHICommandBufferScoper : IDisposable
    {
        RHICommandBuffer m_CommandBuffer;

        internal RHICommandBufferScoper(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
        }

        public void Dispose()
        {
            m_CommandBuffer.End();
        }
    }

    public abstract class RHICommandBuffer : Disposal
    {
        public RHICommandPool CommandPool
        {
            get
            {
                return m_CommandPool;
            }
        }

        protected RHICommandPool m_CommandPool;

        public RHICommandBufferScoper BeginScoped(string name)
        {
            Begin(name);
            return new RHICommandBufferScoper(this);
        }

        public abstract void Begin(string name);
        public abstract RHIBlitEncoder GetBlitEncoder();
        public abstract RHIComputeEncoder GetComputeEncoder();
        public abstract RHIMeshletEncoder GetMeshletEncoder();
        public abstract RHIGraphicsEncoder GetGraphicsEncoder();
        public abstract RHIRaytracingEncoder GetRaytracingEncoder();
        public abstract void End();
        //public abstract void Commit(RHIFence? fence = null);
        //public abstract void WaitUntilCompleted();
    }

    public struct RHIIndirectCommandBufferDescription
    {

    }

    public abstract class RHIIndirectCommandBuffer : Disposal
    {

    }
#pragma warning restore CS8618
}
