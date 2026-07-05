using System;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal enum MetalActiveEncoderType : byte
    {
        None,
        Transfer,
        Compute,
        Raster,
        Raytracing,
        ML,
        WorkGraph
    }

    internal sealed class MetalCommandBuffer : RHICommandBuffer
    {
        internal MTL4CommandBuffer NativeCommandBuffer4 => m_NativeCommandBuffer4;
        internal CAMetalDrawable PresentDrawable => m_PresentDrawable;
        internal bool UsesMachineLearning => m_UsesMachineLearning;

        private readonly MetalTransferEncoder m_TransferEncoder;
        private readonly MetalComputeEncoder m_ComputeEncoder;
        private readonly MetalRasterEncoder m_RasterEncoder;
        private readonly MetalRaytracingEncoder m_RaytracingEncoder;
        private readonly MetalMLEncoder m_MLEncoder;
        private readonly MetalWorkGraphEncoder m_WorkGraphEncoder;

        private MTL4CommandBuffer m_NativeCommandBuffer4;
        private MTL4CommandAllocator m_NativeCommandAllocator4;
        private CAMetalDrawable m_PresentDrawable;
        private MetalActiveEncoderType m_ActiveEncoder;
        private bool m_Mtl4CommandBufferEnded;
        private bool m_UsesMachineLearning;
        private string m_CommandBufferName = string.Empty;

        // Barrier tracking: seenStagesMask tracks which Metal 4 stage bits have had
        // work encoded in the current encoder. Used to decide between
        // BarrierAfterEncoderStages (intra-encoder) vs BarrierAfterQueueStages (cross-encoder).
        private ulong m_CurrentEncoderSeenStages;

        public MetalCommandBuffer(MetalCommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;
            m_TransferEncoder = new MetalTransferEncoder(this);
            m_ComputeEncoder = new MetalComputeEncoder(this);
            m_RasterEncoder = new MetalRasterEncoder(this);
            m_RaytracingEncoder = new MetalRaytracingEncoder(this);
            m_MLEncoder = new MetalMLEncoder(this);
            m_WorkGraphEncoder = new MetalWorkGraphEncoder(this);

            ResetState();
        }

        public override void Begin(string name)
        {
            m_CommandBufferName = string.IsNullOrWhiteSpace(name) ? "MetalCommandBuffer" : name;
            ResetState();
        }

        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            m_TransferEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.Transfer;
            return m_TransferEncoder;
        }

        public override void EndTransferPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Transfer)
            {
                return;
            }

            m_TransferEncoder.EndPass();
            EndEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            m_ComputeEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.Compute;
            return m_ComputeEncoder;
        }

        public override void EndComputePass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Compute)
            {
                return;
            }

            m_ComputeEncoder.EndPass();
            EndEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            m_RaytracingEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.Raytracing;
            return m_RaytracingEncoder;
        }

        public override void EndRaytracingPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Raytracing)
            {
                return;
            }

            m_RaytracingEncoder.EndPass();
            EndEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            m_RasterEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.Raster;
            return m_RasterEncoder;
        }

        public override void EndRasterPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Raster)
            {
                return;
            }

            m_RasterEncoder.EndPass();
            EndEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            m_MLEncoder.BeginPass(descriptor);
            BeginEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.ML;
            m_UsesMachineLearning = true;
            return m_MLEncoder;
        }

        public override void EndMLPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.ML)
            {
                return;
            }

            m_MLEncoder.EndPass();
            EndEncoderBarrierState();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            m_WorkGraphEncoder.BeginPass(descriptor);
            m_ActiveEncoder = MetalActiveEncoderType.WorkGraph;
            return m_WorkGraphEncoder;
        }

        public override void EndWorkGraphPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.WorkGraph)
            {
                return;
            }

            m_WorkGraphEncoder.EndPass();
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override void End()
        {
            switch (m_ActiveEncoder)
            {
                case MetalActiveEncoderType.Transfer:
                    EndTransferPass();
                    break;
                case MetalActiveEncoderType.Compute:
                    EndComputePass();
                    break;
                case MetalActiveEncoderType.Raster:
                    EndRasterPass();
                    break;
                case MetalActiveEncoderType.Raytracing:
                    EndRaytracingPass();
                    break;
                case MetalActiveEncoderType.ML:
                    EndMLPass();
                    break;
                case MetalActiveEncoderType.WorkGraph:
                    EndWorkGraphPass();
                    break;
            }

            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero &&
                !m_Mtl4CommandBufferEnded)
            {
                m_NativeCommandBuffer4.EndCommandBuffer();
                m_Mtl4CommandBufferEnded = true;
            }
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
            return m_MLEncoder;
        }

        public override RHIWorkGraphEncoder GetWorkGraphEncoder()
        {
            return m_WorkGraphEncoder;
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
            End();
        }

        // ── Barrier tracking API for encoders ──

        /// <summary>
        /// Called by encoders after dispatch/draw/copy to record which stages have produced work.
        /// </summary>
        internal void MarkStagesSeen(ulong stages)
        {
            m_CurrentEncoderSeenStages |= stages;
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
            m_ActiveEncoder = MetalActiveEncoderType.None;
            m_Mtl4CommandBufferEnded = false;
            m_CurrentEncoderSeenStages = 0;
            m_UsesMachineLearning = false;
        }

        protected override void Release()
        {
            m_TransferEncoder.Dispose();
            m_ComputeEncoder.Dispose();
            m_RasterEncoder.Dispose();
            m_RaytracingEncoder.Dispose();
            m_MLEncoder.Dispose();
            m_WorkGraphEncoder.Dispose();
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
    }

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
}
