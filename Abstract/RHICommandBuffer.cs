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

    public struct RHIBlitPassScoper : IDisposable
    {
        RHIBlitEncoder m_BlitEncoder;

        internal RHIBlitPassScoper(RHIBlitEncoder blitEncoder)
        {
            m_BlitEncoder = blitEncoder;
        }

        public void Dispose()
        {
            m_BlitEncoder.EndEncoding();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHICommandBufferScoper BeginScoped(string name)
        {
            Begin(name);
            return new RHICommandBufferScoper(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIBlitPassScoper BeginScopedBlitEncoding(string name)
        {
            return new RHIBlitPassScoper(BeginBlitEncoding(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIComputePassScoper BeginScopedComputeEncoding(string name)
        {
            return new RHIComputePassScoper(BeginComputeEncoding(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIRaytracingPassScoper BeginScopedRaytracingEncoding(string name)
        {
            return new RHIRaytracingPassScoper(BeginRaytracingEncoding(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIMeshletPassScoper BeginScopedMeshletEncoding(in RHIMeshletPassDescriptor descriptor)
        {
            return new RHIMeshletPassScoper(BeginMeshletEncoding(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIGraphicsPassScoper BeginScopedGraphicsEncoding(in RHIGraphicsPassDescriptor descriptor)
        {
            return new RHIGraphicsPassScoper(BeginGraphicsEncoding(descriptor));
        }

        public abstract void Begin(string name);
        public abstract RHIBlitEncoder BeginBlitEncoding(string name);
        public abstract void EndBlitEncoding();
        public abstract RHIComputeEncoder BeginComputeEncoding(string name);
        public abstract void EndComputeEncoding();
        public abstract RHIRaytracingEncoder BeginRaytracingEncoding(string name);
        public abstract void EndRaytracingEncoding();
        public abstract RHIMeshletEncoder BeginMeshletEncoding(in RHIMeshletPassDescriptor descriptor);
        public abstract void EndMeshletEncoding();
        public abstract RHIGraphicsEncoder BeginGraphicsEncoding(in RHIGraphicsPassDescriptor descriptor);
        public abstract void EndGraphicsEncoding();
        public abstract void End();
        public abstract RHIBlitEncoder GetBlitEncoder();
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
