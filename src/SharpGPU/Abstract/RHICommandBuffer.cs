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

    public struct RHIRasterPassScoper : IDisposable
    {
        RHIRasterEncoder m_RasterEncoder;

        internal RHIRasterPassScoper(RHIRasterEncoder rasterEncoder)
        {
            m_RasterEncoder = rasterEncoder;
        }

        public void Dispose()
        {
            m_RasterEncoder.EndPass();
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
        public static RHIRasterPassScoper BeginScopedRasterPass(this RHICommandBuffer cmdBuffer, in RHIRasterPassDescriptor descriptor)
        {
            return new RHIRasterPassScoper(cmdBuffer.BeginRasterPass(descriptor));
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

        protected RHICommandQueue m_CommandQueue;

        public abstract void Begin(string name);
        public abstract void ResourceBarrier(in RHIResourceBarrier barrier);
        public abstract void ResourceBarriers(in Memory<RHIResourceBarrier> barriers);
        public abstract RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor);
        public abstract void EndTransferPass();
        public abstract RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor);
        public abstract void EndComputePass();
        public abstract RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void EndRaytracingPass();
        public abstract RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor);
        public abstract void EndRasterPass();
        public abstract void End();
        public abstract RHITransferEncoder GetTransferEncoder();
        public abstract RHIComputeEncoder GetComputeEncoder();
        public abstract RHIRaytracingEncoder GetRaytracingEncoder();
        public abstract RHIRasterEncoder GetRasterEncoder();
    }

    public struct RHIComputeIndirectCommandBufferDescription
    {

    }

    public abstract class RHIComputeIndirectCommandBuffer : Disposal
    {

    }

    public struct RHIRayTracingIndirectCommandBufferDescription
    {

    }

    public abstract class RHIRayTracingIndirectCommandBuffer : Disposal
    {

    }

    public struct RHIRasterIndirectCommandBufferDescription
    {

    }

    public abstract class RHIRasterIndirectCommandBuffer : Disposal
    {

    }
#pragma warning restore CS8618
}
