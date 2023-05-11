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
            m_BlitEncoder.EndPass();
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
        public RHIBlitPassScoper BeginScopedBlitPass(string name)
        {
            return new RHIBlitPassScoper(BeginBlitPass(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIComputePassScoper BeginScopedComputePass(string name)
        {
            return new RHIComputePassScoper(BeginComputePass(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIRaytracingPassScoper BeginScopedRaytracingPass(string name)
        {
            return new RHIRaytracingPassScoper(BeginRaytracingPass(name));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIMeshletPassScoper BeginScopedMeshletPass(in RHIMeshletPassDescriptor descriptor)
        {
            return new RHIMeshletPassScoper(BeginMeshletPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RHIGraphicsPassScoper BeginScopedGraphicsPass(in RHIGraphicsPassDescriptor descriptor)
        {
            return new RHIGraphicsPassScoper(BeginGraphicsPass(descriptor));
        }

        public abstract void Begin(string name);
        public abstract RHIBlitEncoder BeginBlitPass(string name);
        public abstract void EndBlitPass();
        public abstract RHIComputeEncoder BeginComputePass(string name);
        public abstract void EndComputePass();
        public abstract RHIRaytracingEncoder BeginRaytracingPass(string name);
        public abstract void EndRaytracingPass();
        public abstract RHIMeshletEncoder BeginMeshletPass(in RHIMeshletPassDescriptor descriptor);
        public abstract void EndMeshletPass();
        public abstract RHIGraphicsEncoder BeginGraphicsPass(in RHIGraphicsPassDescriptor descriptor);
        public abstract void EndGraphicsPass();
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
