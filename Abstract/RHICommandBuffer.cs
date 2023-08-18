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
            m_TransferEncoder.EndEncoding();
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
            m_ComputeEncoder.EndEncoding();
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
            m_MeshletEncoder.EndEncoding();
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
            m_GraphicsEncoder.EndEncoding();
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
            m_RaytracingEncoder.EndEncoding();
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
        public static RHITransferPassScoper BeginScopedTransferEncoding(this RHICommandBuffer cmdBuffer, in RHITransferPassDescriptor descriptor)
        {
            return new RHITransferPassScoper(cmdBuffer.BeginTransferEncoding(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIComputePassScoper BeginScopedComputeEncoding(this RHICommandBuffer cmdBuffer, in RHIComputePassDescriptor descriptor)
        {
            return new RHIComputePassScoper(cmdBuffer.BeginComputeEncoding(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIRaytracingPassScoper BeginScopedRaytracingEncoding(this RHICommandBuffer cmdBuffer, in RHIRayTracingPassDescriptor descriptor)
        {
            return new RHIRaytracingPassScoper(cmdBuffer.BeginRaytracingEncoding(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIMeshletPassScoper BeginScopedMeshletEncoding(this RHICommandBuffer cmdBuffer, in RHIMeshletPassDescriptor descriptor)
        {
            return new RHIMeshletPassScoper(cmdBuffer.BeginMeshletEncoding(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIGraphicsPassScoper BeginScopedGraphicsEncoding(this RHICommandBuffer cmdBuffer, in RHIGraphicsPassDescriptor descriptor)
        {
            return new RHIGraphicsPassScoper(cmdBuffer.BeginGraphicsEncoding(descriptor));
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

        protected RHICommandQueue m_CommandQueue;

        public abstract void Begin(string name);
        public abstract RHITransferEncoder BeginTransferEncoding(in RHITransferPassDescriptor descriptor);
        public abstract void EndTransferEncoding();
        public abstract RHIComputeEncoder BeginComputeEncoding(in RHIComputePassDescriptor descriptor);
        public abstract void EndComputeEncoding();
        public abstract RHIRaytracingEncoder BeginRaytracingEncoding(in RHIRayTracingPassDescriptor descriptor);
        public abstract void EndRaytracingEncoding();
        public abstract RHIMeshletEncoder BeginMeshletEncoding(in RHIMeshletPassDescriptor descriptor);
        public abstract void EndMeshletEncoding();
        public abstract RHIGraphicsEncoder BeginGraphicsEncoding(in RHIGraphicsPassDescriptor descriptor);
        public abstract void EndGraphicsEncoding();
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
