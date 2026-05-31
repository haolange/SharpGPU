using System;
using System.Runtime.CompilerServices;

namespace SharpGPU.Scopes
{
    public readonly struct RHICommandBufferScope : IDisposable
    {
        private readonly RHICommandBuffer m_CommandBuffer;

        internal RHICommandBufferScope(RHICommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer ?? throw new ArgumentNullException(nameof(commandBuffer));
        }

        public void Dispose()
        {
            m_CommandBuffer.End();
        }
    }

    public readonly struct RHITransferPassScope : IDisposable
    {
        private readonly RHITransferEncoder m_TransferEncoder;
        public RHITransferEncoder Encoder => m_TransferEncoder;

        internal RHITransferPassScope(RHITransferEncoder transferEncoder)
        {
            m_TransferEncoder = transferEncoder ?? throw new ArgumentNullException(nameof(transferEncoder));
        }

        public void Dispose()
        {
            m_TransferEncoder.EndPass();
        }
    }

    public readonly struct RHIComputePassScope : IDisposable
    {
        private readonly RHIComputeEncoder m_ComputeEncoder;
        public RHIComputeEncoder Encoder => m_ComputeEncoder;

        internal RHIComputePassScope(RHIComputeEncoder computeEncoder)
        {
            m_ComputeEncoder = computeEncoder ?? throw new ArgumentNullException(nameof(computeEncoder));
        }

        public void Dispose()
        {
            m_ComputeEncoder.EndPass();
        }
    }

    public readonly struct RHIRasterPassScope : IDisposable
    {
        private readonly RHIRasterEncoder m_RasterEncoder;
        public RHIRasterEncoder Encoder => m_RasterEncoder;

        internal RHIRasterPassScope(RHIRasterEncoder rasterEncoder)
        {
            m_RasterEncoder = rasterEncoder ?? throw new ArgumentNullException(nameof(rasterEncoder));
        }

        public void Dispose()
        {
            m_RasterEncoder.EndPass();
        }
    }

    public readonly struct RHIRaytracingPassScope : IDisposable
    {
        private readonly RHIRaytracingEncoder m_RaytracingEncoder;
        public RHIRaytracingEncoder Encoder => m_RaytracingEncoder;

        internal RHIRaytracingPassScope(RHIRaytracingEncoder raytracingEncoder)
        {
            m_RaytracingEncoder = raytracingEncoder ?? throw new ArgumentNullException(nameof(raytracingEncoder));
        }

        public void Dispose()
        {
            m_RaytracingEncoder.EndPass();
        }
    }

    public readonly struct RHIMLPassScope : IDisposable
    {
        private readonly RHIMLEncoder m_MLEncoder;
        public RHIMLEncoder Encoder => m_MLEncoder;

        internal RHIMLPassScope(RHIMLEncoder mlEncoder)
        {
            m_MLEncoder = mlEncoder ?? throw new ArgumentNullException(nameof(mlEncoder));
        }

        public void Dispose()
        {
            m_MLEncoder.EndPass();
        }
    }

    public readonly struct RHIWorkGraphPassScope : IDisposable
    {
        private readonly RHIWorkGraphEncoder m_WorkGraphEncoder;
        public RHIWorkGraphEncoder Encoder => m_WorkGraphEncoder;

        internal RHIWorkGraphPassScope(RHIWorkGraphEncoder workGraphEncoder)
        {
            m_WorkGraphEncoder = workGraphEncoder ?? throw new ArgumentNullException(nameof(workGraphEncoder));
        }

        public void Dispose()
        {
            m_WorkGraphEncoder.EndPass();
        }
    }

    public static class RHICommandBufferScopeExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHICommandBufferScope BeginScoped(this RHICommandBuffer commandBuffer, string name)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            commandBuffer.Begin(name);
            return new RHICommandBufferScope(commandBuffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHITransferPassScope BeginScopedTransferPass(this RHICommandBuffer commandBuffer, in RHITransferPassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHITransferPassScope(commandBuffer.BeginTransferPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIComputePassScope BeginScopedComputePass(this RHICommandBuffer commandBuffer, in RHIComputePassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHIComputePassScope(commandBuffer.BeginComputePass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIRaytracingPassScope BeginScopedRaytracingPass(this RHICommandBuffer commandBuffer, in RHIRayTracingPassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHIRaytracingPassScope(commandBuffer.BeginRaytracingPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIRasterPassScope BeginScopedRasterPass(this RHICommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHIRasterPassScope(commandBuffer.BeginRasterPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIMLPassScope BeginScopedMLPass(this RHICommandBuffer commandBuffer, in RHIMLPassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHIMLPassScope(commandBuffer.BeginMLPass(descriptor));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RHIWorkGraphPassScope BeginScopedWorkGraphPass(this RHICommandBuffer commandBuffer, in RHIWorkGraphPassDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(commandBuffer);
            return new RHIWorkGraphPassScope(commandBuffer.BeginWorkGraphPass(descriptor));
        }
    }
}
