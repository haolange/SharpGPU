using System;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalCommandBuffer : RHICommandBuffer
    {
        internal MTL4CommandBuffer NativeCommandBuffer4 => m_NativeCommandBuffer4;
        internal CAMetalDrawable PresentDrawable => m_PresentDrawable;
        internal bool UsesMachineLearning => m_UsesMachineLearning;
        internal bool UsesNativeArgumentTables => m_UsesNativeArgumentTables;
        internal MetalTransientNativeBatch NativeTransientBatch =>
            m_NativeTransientBatch;

        private readonly MetalTransientNativeBatch m_NativeTransientBatch;
        private readonly MetalTransferEncoder m_TransferEncoder;
        private readonly MetalComputeEncoder m_ComputeEncoder;
        private readonly MetalRasterEncoder m_RasterEncoder;
        private readonly MetalRaytracingEncoder m_RaytracingEncoder;
        private readonly MetalMLEncoder m_MLEncoder;

        private MTL4CommandBuffer m_NativeCommandBuffer4;
        private MTL4CommandAllocator m_NativeCommandAllocator4;
        private CAMetalDrawable m_PresentDrawable;
        private bool m_Mtl4CommandBufferEnded;
        private bool m_UsesMachineLearning;
        private bool m_UsesNativeArgumentTables;
        private string m_CommandBufferName = string.Empty;

        // Barrier tracking: seenStagesMask tracks which Metal 4 stage bits have had
        // work encoded in the current encoder. Used to decide between
        // BarrierAfterEncoderStages (intra-encoder) vs BarrierAfterQueueStages (cross-encoder).
        private ulong m_CurrentEncoderSeenStages;

        public MetalCommandBuffer(MetalCommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;
            m_NativeTransientBatch = new MetalTransientNativeBatch();
            m_TransferEncoder = new MetalTransferEncoder(this);
            m_ComputeEncoder = new MetalComputeEncoder(this);
            m_RasterEncoder = new MetalRasterEncoder(this);
            m_RaytracingEncoder = new MetalRaytracingEncoder(this);
            m_MLEncoder = new MetalMLEncoder(this);

            ResetState();
        }

        public override void Begin(string name)
        {
            ValidateCanBegin();
            ResetEncodersForRecording();
            m_NativeTransientBatch.ReleaseForCommandBufferReuse();
            ReleaseNativeCommandObjects();
            m_CommandBufferName = string.IsNullOrWhiteSpace(name) ? "MetalCommandBuffer" : name;
            ResetState();
            MarkBeginSucceeded();
        }

        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Transfer);
            m_TransferEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Transfer);
            return m_TransferEncoder;
        }

        public override void EndTransferPass()
        {
            m_TransferEncoder.EndPass();
            EndEncoderBarrierState();
        }

        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Compute);
            m_ComputeEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Compute);
            return m_ComputeEncoder;
        }

        public override void EndComputePass()
        {
            m_ComputeEncoder.EndPass();
            EndEncoderBarrierState();
        }

        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.RayTracing);
            m_RaytracingEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.RayTracing);
            return m_RaytracingEncoder;
        }

        public override void EndRaytracingPass()
        {
            m_RaytracingEncoder.EndPass();
            EndEncoderBarrierState();
        }

        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Raster);
            m_RasterEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Raster);
            return m_RasterEncoder;
        }

        public override void EndRasterPass()
        {
            m_RasterEncoder.EndPass();
            EndEncoderBarrierState();
        }

        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.MachineLearning);
            ((MetalCommandQueue)m_CommandQueue).MetalDevice.Capabilities.MachineLearning.Execution.Require(
                "Metal machine-learning passes");
            m_MLEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.MachineLearning);
            m_UsesMachineLearning = true;
            return m_MLEncoder;
        }

        public override void EndMLPass()
        {
            m_MLEncoder.EndPass();
            EndEncoderBarrierState();
        }

        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            ((MetalCommandQueue)m_CommandQueue).MetalDevice.Capabilities.WorkGraph.Execution.Require(
                "Metal work-graph passes");
            throw new NotSupportedException(
                "Metal Work Graph execution is not exposed by SharpGPU.");
        }

        public override void EndWorkGraphPass()
        {
            ThrowIfDisposed();
            ((MetalCommandQueue)m_CommandQueue).MetalDevice.Capabilities.WorkGraph.Execution.Require(
                "Metal work-graph passes");
            throw new NotSupportedException(
                "Metal Work Graph execution is not exposed by SharpGPU.");
        }

        public override void End()
        {
            ValidateCanEnd();
            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero &&
                !m_Mtl4CommandBufferEnded)
            {
                m_NativeCommandBuffer4.EndCommandBuffer();
                m_Mtl4CommandBufferEnded = true;
            }
            MarkEndSucceeded();
        }

        public override RHITransferEncoder GetTransferEncoder()
        {
            return m_TransferEncoder;
        }

        public override RHIComputeEncoder GetComputeEncoder()
        {
            return m_ComputeEncoder;
        }

        public override RHIRaytracingEncoder GetRaytracingEncoder()
        {
            return m_RaytracingEncoder;
        }

        public override RHIRasterEncoder GetRasterEncoder()
        {
            return m_RasterEncoder;
        }

        public override RHIMLEncoder GetMLEncoder()
        {
            ThrowIfDisposed();
            ((MetalCommandQueue)m_CommandQueue).MetalDevice.Capabilities.MachineLearning.Execution.Require(
                "Metal machine-learning encoder");
            return m_MLEncoder;
        }

        public override RHIWorkGraphEncoder GetWorkGraphEncoder()
        {
            ThrowIfDisposed();
            ((MetalCommandQueue)m_CommandQueue).MetalDevice.Capabilities.WorkGraph.Execution.Require(
                "Metal work-graph encoder");
            throw new NotSupportedException(
                "Metal Work Graph execution is not exposed by SharpGPU.");
        }
        internal void SetPresentDrawable(in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr != IntPtr.Zero)
            {
                m_PresentDrawable = drawable;
            }
        }

        internal MTL4CommandBuffer EnsureMtl4CommandBuffer()
        {
            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero)
            {
                return m_NativeCommandBuffer4;
            }

            MetalCommandQueue queue = (MetalCommandQueue)m_CommandQueue!;
            m_NativeCommandBuffer4 = queue.MetalDevice.NativeDevice.NewMTL4CommandBuffer();
            if (m_NativeCommandBuffer4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4CommandBuffer.");
            }

            m_NativeCommandAllocator4 = queue.MetalDevice.NativeDevice.NewMTL4CommandAllocator();
            if (m_NativeCommandAllocator4.NativePtr == IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeCommandBuffer4);
                m_NativeCommandBuffer4 = default;
                throw new InvalidOperationException("Failed to create MTL4CommandAllocator.");
            }

            m_NativeCommandBuffer4.BeginCommandBuffer(m_NativeCommandAllocator4);
            if (queue.HasResidencySet)
            {
                m_NativeCommandBuffer4.UseResidencySet(queue.NativeResidencySet);
            }

            m_NativeCommandBuffer4.Label = new SharpMetal.Foundation.NSString(m_CommandBufferName);
            m_Mtl4CommandBufferEnded = false;
            return m_NativeCommandBuffer4;
        }

        internal void FinalizeForSubmit()
        {
            ValidateCanSubmit();
            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero &&
                !m_Mtl4CommandBufferEnded)
            {
                throw new InvalidOperationException(
                    "The native Metal command buffer was not ended before submission.");
            }
        }

        // ── Barrier tracking API for encoders ──

        /// <summary>
        /// Called by encoders after dispatch/draw/copy to record which stages have produced work.
        /// </summary>
        internal void MarkStagesSeen(ulong stages)
        {
            m_CurrentEncoderSeenStages |= stages;
        }

        internal void MarkNativeArgumentTablesUsed()
        {
            m_UsesNativeArgumentTables = true;
        }

        /// <summary>
        /// Returns true if the srcStage was already seen in the current encoder (intra-encoder dependency).
        /// Returns false if the srcStage must have been produced by a previous encoder (cross-encoder dependency).
        /// </summary>
        internal bool IsIntraEncoderBarrier(ulong afterStages)
        {
            return afterStages != 0 && (m_CurrentEncoderSeenStages & afterStages) != 0;
        }

        // ── Private helpers ──

        private void BeginEncoderBarrierState()
        {
            m_CurrentEncoderSeenStages = 0;
        }

        private void EndEncoderBarrierState()
        {
        }

        private void ResetState()
        {
            m_NativeCommandBuffer4 = default;
            m_NativeCommandAllocator4 = default;
            m_PresentDrawable = default;
            m_Mtl4CommandBufferEnded = false;
            m_CurrentEncoderSeenStages = 0;
            m_UsesMachineLearning = false;
            m_UsesNativeArgumentTables = false;
        }

        private void ResetEncodersForRecording()
        {
            m_ComputeEncoder.ResetForRecording();
            m_RasterEncoder.ResetForRecording();
            m_RaytracingEncoder.ResetForRecording();
        }

        private void ReleaseNativeCommandObjects()
        {
            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeCommandBuffer4);
                m_NativeCommandBuffer4 = default;
            }

            if (m_NativeCommandAllocator4.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeCommandAllocator4);
                m_NativeCommandAllocator4 = default;
            }
        }

        protected override void Release()
        {
            m_TransferEncoder.Dispose();
            m_ComputeEncoder.Dispose();
            m_RasterEncoder.Dispose();
            m_RaytracingEncoder.Dispose();
            m_MLEncoder.Dispose();
            m_NativeTransientBatch.Dispose();
            ReleaseNativeCommandObjects();
        }
    }

    #region IndirectCommands
internal sealed class MetalComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalComputeIndirectCommandBuffer(MetalDevice device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.ConcurrentDispatch;
            icbDesc.MaxKernelBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }

    internal sealed class MetalRayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalRayTracingIndirectCommandBuffer(MetalDevice device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.ConcurrentDispatch;
            icbDesc.MaxKernelBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }

    internal sealed class MetalRasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalRasterIndirectCommandBuffer(MetalDevice device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.Draw | MTLIndirectCommandType.DrawIndexed;
            icbDesc.MaxVertexBufferBindCount = 8;
            icbDesc.MaxFragmentBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }

    #endregion
}
