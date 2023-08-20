using System;
using System.Runtime.CompilerServices;
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

    public struct RHITransferPassScoper : IDisposable
    {
        RHITransferEncoder m_TransferEncoder;

        internal RHITransferPassScoper(RHITransferEncoder transferEncoder)
        {
            m_TransferEncoder = transferEncoder;
        }

        public void Dispose()
        {
            m_TransferEncoder.EndPass();
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

    public static class RHICommandBufferUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHICommandBufferScoper BeginScoped(this RHICommandBuffer cmdBuffer, string name)
        {
            cmdBuffer.Begin(name);
            return new RHICommandBufferScoper(cmdBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHITransferPassScoper BeginScopedTransferPass(this RHICommandBuffer cmdBuffer, in RHITransferPassDescriptor descriptor)
        {
            return new RHITransferPassScoper(cmdBuffer.BeginTransferPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIComputePassScoper BeginScopedComputePass(this RHICommandBuffer cmdBuffer, in RHIComputePassDescriptor descriptor)
        {
            return new RHIComputePassScoper(cmdBuffer.BeginComputePass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIRaytracingPassScoper BeginScopedRaytracingPass(this RHICommandBuffer cmdBuffer, in RHIRayTracingPassDescriptor descriptor)
        {
            return new RHIRaytracingPassScoper(cmdBuffer.BeginRaytracingPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIMeshletPassScoper BeginScopedMeshletPass(this RHICommandBuffer cmdBuffer, in RHIMeshletPassDescriptor descriptor)
        {
            return new RHIMeshletPassScoper(cmdBuffer.BeginMeshletPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIGraphicsPassScoper BeginScopedGraphicsPass(this RHICommandBuffer cmdBuffer, in RHIGraphicsPassDescriptor descriptor)
        {
            return new RHIGraphicsPassScoper(cmdBuffer.BeginGraphicsPass(descriptor));
        }
    }

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
        internal RHIPipelineLayout? PipelineLayout;

        protected RHICommandQueue m_CommandQueue;

        public abstract void Begin(string name);
        public abstract RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor);
        public abstract void EndTransferPass();
        public abstract RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor);
        public abstract void EndComputePass();
        public abstract RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void EndRaytracingPass();
        public abstract RHIMeshletEncoder BeginMeshletPass(in RHIMeshletPassDescriptor descriptor);
        public abstract void EndMeshletPass();
        public abstract RHIGraphicsEncoder BeginGraphicsPass(in RHIGraphicsPassDescriptor descriptor);
        public abstract void EndGraphicsPass();
        public abstract void End();
        public abstract RHITransferEncoder GetTransferEncoder();
        public abstract RHIComputeEncoder GetComputeEncoder();
        public abstract RHIRaytracingEncoder GetRaytracingEncoder();
        public abstract RHIMeshletEncoder GetMeshletEncoder();
        public abstract RHIGraphicsEncoder GetGraphicsEncoder();
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
