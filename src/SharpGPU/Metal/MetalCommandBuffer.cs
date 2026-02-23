using System;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal enum MetalActiveEncoderType : byte
    {
        None,
        Transfer,
        Compute,
        Raster,
        Raytracing,
        ML
    }

    internal enum MetalCommandEncodingPath : byte
    {
        Unknown = 0,
        Classic = 1,
        MTL4 = 2
    }

    internal static class MetalCommandEncodingPathLock
    {
        internal static MetalCommandEncodingPath Lock(in MetalCommandEncodingPath current, in MetalCommandEncodingPath requested, string scope)
        {
            if (requested == MetalCommandEncodingPath.Unknown)
            {
                throw new InvalidOperationException("Requested command-buffer encoding path must not be Unknown.");
            }

            if (current == MetalCommandEncodingPath.Unknown || current == requested)
            {
                return requested;
            }

            throw new InvalidOperationException($"Metal command buffer encoding-path conflict in {scope}. current={current}, requested={requested}. Split command buffers or override INFINITY_METAL_BINDING_MODE.");
        }
    }

    internal sealed class MetalCommandBuffer : RHICommandBuffer
    {
        internal MTLCommandBuffer NativeCommandBuffer => m_NativeCommandBuffer;
        internal MTL4CommandBuffer NativeCommandBuffer4 => m_NativeCommandBuffer4;
        internal CAMetalDrawable PresentDrawable => m_PresentDrawable;
        internal bool EnableMetal4Barriers => m_EnableMetal4Barriers;
        internal MetalCommandEncodingPath EncodingPath => m_EncodingPath;

        private readonly MetalTransferEncoder m_TransferEncoder;
        private readonly MetalComputeEncoder m_ComputeEncoder;
        private readonly MetalRasterEncoder m_RasterEncoder;
        private readonly MetalRaytracingEncoder m_RaytracingEncoder;
        private readonly MetalMLEncoder m_MLEncoder;
        private readonly MTLFence m_BarrierFence;
        private readonly bool m_EnableMetal4Barriers;

        private MTLCommandBuffer m_NativeCommandBuffer;
        private MTL4CommandBuffer m_NativeCommandBuffer4;
        private CAMetalDrawable m_PresentDrawable;
        private MetalActiveEncoderType m_ActiveEncoder;
        private MetalActiveEncoderType m_LastCompletedEncoder;
        private MetalCommandEncodingPath m_EncodingPath;
        private bool m_Mtl4CommandBufferEnded;
        private string m_CommandBufferName = string.Empty;
        private bool m_HasPendingBarrier;
        private ulong m_PendingAfterStages;
        private ulong m_PendingBeforeStages;
        private MTLBarrierScope m_PendingBarrierScope;

        public MetalCommandBuffer(MetalCommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;
            m_TransferEncoder = new MetalTransferEncoder(this);
            m_ComputeEncoder = new MetalComputeEncoder(this);
            m_RasterEncoder = new MetalRasterEncoder(this);
            m_RaytracingEncoder = new MetalRaytracingEncoder(this);
            m_MLEncoder = new MetalMLEncoder(this);
            m_BarrierFence = commandQueue.MetalDevice.NativeDevice.NewFence;
            m_EnableMetal4Barriers = commandQueue.MetalDevice.SupportsMetal4Barriers && IsMetal4BarrierEnabledByEnv();

            ResetState();
        }

        public override void Begin(string name)
        {
            m_CommandBufferName = string.IsNullOrWhiteSpace(name) ? "MetalCommandBuffer" : name;
            ResetState();
        }

        public override void ResourceBarrier(in RHIResourceBarrier barrier)
        {
            MTLBarrierScope scope = MetalUtility.ConvertToMetalBarrierScope(barrier.ResourceType);
            ulong afterStages = 0;
            ulong beforeStages = 0;

            if (barrier.ResourceBarrierType == ERHIResourceBarrierType.Triansition)
            {
                if (barrier.ResourceType == ERHIResourceType.Buffer)
                {
                    afterStages = MetalUtility.ConvertToMetal4Stages(barrier.BufferBarrierInfo.SrcStage);
                    beforeStages = MetalUtility.ConvertToMetal4Stages(barrier.BufferBarrierInfo.DstStage);
                }
                else
                {
                    afterStages = MetalUtility.ConvertToMetal4Stages(barrier.TextureBarrierInfo.SrcStage);
                    beforeStages = MetalUtility.ConvertToMetal4Stages(barrier.TextureBarrierInfo.DstStage);
                }
            }

            ApplyBarrier(scope, afterStages, beforeStages);
        }

        public override void ResourceBarriers(in Memory<RHIResourceBarrier> barriers)
        {
            Span<RHIResourceBarrier> span = barriers.Span;
            for (int i = 0; i < span.Length; ++i)
            {
                ResourceBarrier(span[i]);
            }
        }

        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            // Transfer pass can be encoded via classic blit encoder or via MTL4 compute encoder copy APIs.
            MetalCommandEncodingPath requestedPath = m_EncodingPath == MetalCommandEncodingPath.MTL4
                ? MetalCommandEncodingPath.MTL4
                : MetalCommandEncodingPath.Classic;
            LockEncodingPath(requestedPath, "transfer pass");
            m_TransferEncoder.BeginPass(descriptor);
            ApplyPendingBarrierToTransfer();
            m_ActiveEncoder = MetalActiveEncoderType.Transfer;
            return m_TransferEncoder;
        }

        public override void EndTransferPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Transfer)
            {
                return;
            }

            m_TransferEncoder.SignalFence(m_BarrierFence);
            m_TransferEncoder.EndPass();
            m_LastCompletedEncoder = MetalActiveEncoderType.Transfer;
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            m_ComputeEncoder.BeginPass(descriptor);
            m_ActiveEncoder = MetalActiveEncoderType.Compute;
            if (m_EncodingPath != MetalCommandEncodingPath.Unknown)
            {
                ApplyPendingBarrierToCompute();
            }

            return m_ComputeEncoder;
        }

        public override void EndComputePass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Compute)
            {
                return;
            }

            m_ComputeEncoder.SignalFence(m_BarrierFence);
            m_ComputeEncoder.EndPass();
            m_LastCompletedEncoder = MetalActiveEncoderType.Compute;
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            m_RaytracingEncoder.BeginPass(descriptor);
            m_ActiveEncoder = MetalActiveEncoderType.Raytracing;
            if (m_EncodingPath != MetalCommandEncodingPath.Unknown)
            {
                ApplyPendingBarrierToRaytracing();
            }

            return m_RaytracingEncoder;
        }

        public override void EndRaytracingPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Raytracing)
            {
                return;
            }

            m_RaytracingEncoder.SignalFence(m_BarrierFence);
            m_RaytracingEncoder.EndPass();
            m_LastCompletedEncoder = MetalActiveEncoderType.Raytracing;
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            m_RasterEncoder.BeginPass(descriptor);
            m_ActiveEncoder = MetalActiveEncoderType.Raster;
            if (m_EncodingPath != MetalCommandEncodingPath.Unknown)
            {
                ApplyPendingBarrierToRaster();
            }

            return m_RasterEncoder;
        }

        public override void EndRasterPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.Raster)
            {
                return;
            }

            m_RasterEncoder.SignalFence(m_BarrierFence);
            m_RasterEncoder.EndPass();
            m_LastCompletedEncoder = MetalActiveEncoderType.Raster;
            m_ActiveEncoder = MetalActiveEncoderType.None;
        }

        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            m_MLEncoder.BeginPass(descriptor);
            m_ActiveEncoder = MetalActiveEncoderType.ML;
            return m_MLEncoder;
        }

        public override void EndMLPass()
        {
            if (m_ActiveEncoder != MetalActiveEncoderType.ML)
            {
                return;
            }

            m_MLEncoder.EndPass();
            m_LastCompletedEncoder = MetalActiveEncoderType.ML;
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
            }

            if (m_EncodingPath == MetalCommandEncodingPath.MTL4 &&
                m_NativeCommandBuffer4.NativePtr != IntPtr.Zero &&
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

        internal void SetPresentDrawable(in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr != IntPtr.Zero)
            {
                m_PresentDrawable = drawable;
            }
        }

        internal MTLCommandBuffer EnsureClassicCommandBuffer()
        {
            if (m_EncodingPath == MetalCommandEncodingPath.MTL4)
            {
                throw new InvalidOperationException("Classic command-buffer path is unavailable after command buffer is locked to MTL4.");
            }

            if (m_NativeCommandBuffer.NativePtr != IntPtr.Zero)
            {
                return m_NativeCommandBuffer;
            }

            MetalCommandQueue queue = (MetalCommandQueue)m_CommandQueue!;
            m_NativeCommandBuffer = queue.NativeQueue.CommandBuffer();
            if (m_NativeCommandBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLCommandBuffer.");
            }

            m_NativeCommandBuffer.Label = new SharpMetal.Foundation.NSString(m_CommandBufferName);
            return m_NativeCommandBuffer;
        }

        internal MTL4CommandBuffer EnsureMtl4CommandBuffer()
        {
            if (m_EncodingPath == MetalCommandEncodingPath.Classic)
            {
                throw new InvalidOperationException("MTL4 command-buffer path is unavailable after command buffer is locked to Classic.");
            }

            if (m_NativeCommandBuffer4.NativePtr != IntPtr.Zero)
            {
                return m_NativeCommandBuffer4;
            }

            MetalCommandQueue queue = (MetalCommandQueue)m_CommandQueue!;
            if (!queue.SupportsMtl4Submission)
            {
                throw new InvalidOperationException("MTL4 command submission is unavailable on current queue/device.");
            }

            m_NativeCommandBuffer4 = queue.MetalDevice.NativeDevice.NewMTL4CommandBuffer();
            if (m_NativeCommandBuffer4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4CommandBuffer.");
            }

            m_NativeCommandBuffer4.BeginCommandBuffer(queue.NativeMtl4CommandAllocator);
            m_NativeCommandBuffer4.Label = new SharpMetal.Foundation.NSString(m_CommandBufferName);
            m_Mtl4CommandBufferEnded = false;
            return m_NativeCommandBuffer4;
        }

        internal void LockEncodingPath(in MetalCommandEncodingPath requestedPath, string scope)
        {
            MetalCommandEncodingPath lockedPath = MetalCommandEncodingPathLock.Lock(m_EncodingPath, requestedPath, scope);
            m_EncodingPath = lockedPath;
        }

        internal void ApplyPendingBarrierForActiveEncoderIfNeeded()
        {
            if (!m_HasPendingBarrier)
            {
                return;
            }

            switch (m_ActiveEncoder)
            {
                case MetalActiveEncoderType.Transfer:
                    ApplyPendingBarrierToTransfer();
                    break;
                case MetalActiveEncoderType.Compute:
                    ApplyPendingBarrierToCompute();
                    break;
                case MetalActiveEncoderType.Raster:
                    ApplyPendingBarrierToRaster();
                    break;
                case MetalActiveEncoderType.Raytracing:
                    ApplyPendingBarrierToRaytracing();
                    break;
            }
        }

        internal void FinalizeForSubmit()
        {
            End();
        }

        private void ApplyBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_ActiveEncoder == MetalActiveEncoderType.Compute)
            {
                m_ComputeEncoder.ApplyImmediateBarrier(scope, afterStages, beforeStages);
                return;
            }

            if (m_ActiveEncoder == MetalActiveEncoderType.Raster)
            {
                m_RasterEncoder.ApplyImmediateBarrier(scope, afterStages, beforeStages);
                return;
            }

            if (m_ActiveEncoder == MetalActiveEncoderType.Raytracing)
            {
                m_RaytracingEncoder.ApplyImmediateBarrier(scope, afterStages, beforeStages);
                return;
            }

            m_HasPendingBarrier = true;
            m_PendingBarrierScope |= scope;
            m_PendingAfterStages = afterStages;
            m_PendingBeforeStages = beforeStages;
        }

        private void ApplyPendingBarrierToTransfer()
        {
            if (!m_HasPendingBarrier)
            {
                return;
            }

            if (m_BarrierFence.NativePtr != IntPtr.Zero && m_LastCompletedEncoder != MetalActiveEncoderType.None)
            {
                m_TransferEncoder.WaitForFence(m_BarrierFence);
            }

            ClearPendingBarrier();
        }

        private void ApplyPendingBarrierToCompute()
        {
            if (!m_HasPendingBarrier)
            {
                return;
            }

            if (m_BarrierFence.NativePtr != IntPtr.Zero && m_LastCompletedEncoder != MetalActiveEncoderType.None)
            {
                m_ComputeEncoder.WaitForFence(m_BarrierFence);
            }

            m_ComputeEncoder.ApplyImmediateBarrier(m_PendingBarrierScope, m_PendingAfterStages, m_PendingBeforeStages);
            ClearPendingBarrier();
        }

        private void ApplyPendingBarrierToRaster()
        {
            if (!m_HasPendingBarrier)
            {
                return;
            }

            if (m_BarrierFence.NativePtr != IntPtr.Zero && m_LastCompletedEncoder != MetalActiveEncoderType.None)
            {
                m_RasterEncoder.WaitForFence(m_BarrierFence);
            }

            m_RasterEncoder.ApplyImmediateBarrier(m_PendingBarrierScope, m_PendingAfterStages, m_PendingBeforeStages);
            ClearPendingBarrier();
        }

        private void ApplyPendingBarrierToRaytracing()
        {
            if (!m_HasPendingBarrier)
            {
                return;
            }

            if (m_BarrierFence.NativePtr != IntPtr.Zero && m_LastCompletedEncoder != MetalActiveEncoderType.None)
            {
                m_RaytracingEncoder.WaitForFence(m_BarrierFence);
            }

            m_RaytracingEncoder.ApplyImmediateBarrier(m_PendingBarrierScope, m_PendingAfterStages, m_PendingBeforeStages);
            ClearPendingBarrier();
        }

        private void ClearPendingBarrier()
        {
            m_HasPendingBarrier = false;
            m_PendingAfterStages = 0;
            m_PendingBeforeStages = 0;
            m_PendingBarrierScope = 0;
        }

        private void ResetState()
        {
            m_NativeCommandBuffer = default;
            m_NativeCommandBuffer4 = default;
            m_PresentDrawable = default;
            m_ActiveEncoder = MetalActiveEncoderType.None;
            m_LastCompletedEncoder = MetalActiveEncoderType.None;
            m_EncodingPath = MetalCommandEncodingPath.Unknown;
            m_Mtl4CommandBufferEnded = false;
            ClearPendingBarrier();
        }

        private static bool IsMetal4BarrierEnabledByEnv()
        {
            string? value = Environment.GetEnvironmentVariable("INFINITY_METAL_USE_MTL4_BARRIER");
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string lower = value.Trim().ToLowerInvariant();
            return lower == "1" || lower == "true" || lower == "yes" || lower == "on";
        }

        protected override void Release()
        {
            m_TransferEncoder.Dispose();
            m_ComputeEncoder.Dispose();
            m_RasterEncoder.Dispose();
            m_RaytracingEncoder.Dispose();
            m_MLEncoder.Dispose();

            if (m_BarrierFence.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_BarrierFence);
            }
        }
    }

    internal sealed class MetalComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        internal MetalComputeIndirectCommandBuffer(in RHIComputeIndirectCommandBufferDescription descriptor)
        {
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalRayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        internal MetalRayTracingIndirectCommandBuffer(in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalRasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        internal MetalRasterIndirectCommandBuffer(in RHIRasterIndirectCommandBufferDescription descriptor)
        {
        }

        protected override void Release()
        {
        }
    }
}
