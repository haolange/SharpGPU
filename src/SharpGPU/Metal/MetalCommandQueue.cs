using System;
using SharpMetal.Metal;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal sealed class MetalCommandQueue : RHICommandQueue
    {
        public MTLCommandQueue NativeQueue => m_NativeQueue;
        public MetalDevice MetalDevice => m_MetalDevice;
        public override ulong Frequency => 1_000_000_000UL;

        private readonly MetalDevice m_MetalDevice;
        private MTLCommandQueue m_NativeQueue;
        private MTLCommandBuffer m_LastSubmittedCommandBuffer;

        public MetalCommandQueue(MetalDevice device, in ERHIPipelineType pipeline)
        {
            m_MetalDevice = device;
            m_PipelineType = pipeline;
            m_NativeQueue = device.NativeDevice.NewCommandQueue();
            m_LastSubmittedCommandBuffer = default;

            if (m_NativeQueue.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLCommandQueue.");
            }
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new MetalCommandBuffer(this);
        }

        public override void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            throw new NotSupportedException("Tiled texture mapping is not implemented in Metal backend.");
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            throw new NotSupportedException("Tiled texture unmapping is not implemented in Metal backend.");
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            throw new NotSupportedException("Packed mip mapping is not implemented in Metal backend.");
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            throw new NotSupportedException("Packed mip unmapping is not implemented in Metal backend.");
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            if (cmdBuffer == null)
            {
                WaitLastSubmission();
                SignalFence(signalFence as MetalFence);
                return;
            }

            MetalCommandBuffer metalCommandBuffer = cmdBuffer as MetalCommandBuffer ?? throw new ArgumentException("Invalid command buffer type for Metal queue.", nameof(cmdBuffer));
            MTLCommandBuffer nativeCommandBuffer = metalCommandBuffer.NativeCommandBuffer;
            if (nativeCommandBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Command buffer has not begun encoding.");
            }

            EncodeWait(nativeCommandBuffer, waitSemaphore as MetalSemaphore);
            EncodeSignal(nativeCommandBuffer, signalSemaphore as MetalSemaphore);
            PresentDrawable(nativeCommandBuffer, metalCommandBuffer.PresentDrawable);

            nativeCommandBuffer.Commit();
            m_LastSubmittedCommandBuffer = nativeCommandBuffer;

            if (signalFence != null)
            {
                nativeCommandBuffer.WaitUntilCompleted();
                SignalFence(signalFence as MetalFence);
            }
        }

        public override void Submits(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            MetalSemaphore? wait = (waitSemaphores != null && waitSemaphores.Length > 0) ? waitSemaphores[0] as MetalSemaphore : null;
            MetalSemaphore? signal = (signalSemaphores != null && signalSemaphores.Length > 0) ? signalSemaphores[0] as MetalSemaphore : null;
            Submit(cmdBuffer, signalFence, wait, signal);
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (cmdBuffers == null || cmdBuffers.Length == 0)
            {
                Submit(null, signalFence, null, null);
                return;
            }

            for (int i = 0; i < cmdBuffers.Length; ++i)
            {
                RHISemaphore wait = (i == 0 && waitSemaphores != null && waitSemaphores.Length > 0) ? waitSemaphores[0] : null;
                RHISemaphore signal = (i == cmdBuffers.Length - 1 && signalSemaphores != null && signalSemaphores.Length > 0) ? signalSemaphores[0] : null;
                RHIFence fence = i == cmdBuffers.Length - 1 ? signalFence : null;
                Submit(cmdBuffers[i], fence, wait, signal);
            }
        }

        private void WaitLastSubmission()
        {
            if (m_LastSubmittedCommandBuffer.NativePtr != IntPtr.Zero)
            {
                m_LastSubmittedCommandBuffer.WaitUntilCompleted();
                m_LastSubmittedCommandBuffer = default;
            }
        }

        private static void PresentDrawable(in MTLCommandBuffer nativeCommandBuffer, in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr != IntPtr.Zero)
            {
                nativeCommandBuffer.PresentDrawable(drawable);
            }
        }

        private static void EncodeWait(in MTLCommandBuffer nativeCommandBuffer, MetalSemaphore? waitSemaphore)
        {
            if (waitSemaphore == null)
            {
                return;
            }

            ulong waitValue = waitSemaphore.CurrentValue;
            if (waitValue > 1)
            {
                nativeCommandBuffer.EncodeWait(waitSemaphore.NativeEvent, waitValue - 1);
            }
        }

        private static void EncodeSignal(in MTLCommandBuffer nativeCommandBuffer, MetalSemaphore? signalSemaphore)
        {
            if (signalSemaphore == null)
            {
                return;
            }

            ulong signalValue = signalSemaphore.AcquireSignalValue();
            nativeCommandBuffer.EncodeSignalEvent(signalSemaphore.NativeEvent, signalValue);
        }

        private static void SignalFence(MetalFence? fence)
        {
            fence?.Signal();
        }

        protected override void Release()
        {
            if (m_NativeQueue.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeQueue);
                m_NativeQueue = default;
            }
        }
    }
}
