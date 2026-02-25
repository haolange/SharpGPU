using System;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal sealed class MetalCommandQueue : RHICommandQueue
    {
        public MTLCommandQueue NativeQueue => m_NativeQueue;
        public MetalDevice MetalDevice => m_MetalDevice;
        public override ulong Frequency => 1_000_000_000UL;

        internal bool SupportsMtl4Submission => m_NativeQueue4.NativePtr != IntPtr.Zero &&
                                                m_NativeMtl4CommandAllocator.NativePtr != IntPtr.Zero &&
                                                m_Mtl4CompletionEvent.NativePtr != IntPtr.Zero;

        internal MTL4CommandAllocator NativeMtl4CommandAllocator => m_NativeMtl4CommandAllocator;
        internal bool HasResidencySet => m_ResidencySet.NativePtr != IntPtr.Zero;

        private readonly MetalDevice m_MetalDevice;
        private MTLCommandQueue m_NativeQueue;
        private MTL4CommandQueue m_NativeQueue4;
        private MTL4CommandAllocator m_NativeMtl4CommandAllocator;
        private MTLSharedEvent m_Mtl4CompletionEvent;
        private MTLResidencySet m_ResidencySet;
        private MTLCommandBuffer m_LastSubmittedCommandBuffer;
        private ulong m_LastSubmittedMtl4Value;
        private ulong m_NextMtl4CompletionValue;
        private bool m_HasLoggedMtl4SubmitOrder;

        public MetalCommandQueue(MetalDevice device, in ERHIPipelineType pipeline)
        {
            m_MetalDevice = device;
            m_PipelineType = pipeline;
            m_NativeQueue = device.NativeDevice.NewCommandQueue();
            m_NativeQueue4 = default;
            m_NativeMtl4CommandAllocator = default;
            m_Mtl4CompletionEvent = default;
            m_ResidencySet = default;
            m_LastSubmittedCommandBuffer = default;
            m_LastSubmittedMtl4Value = 0;
            m_NextMtl4CompletionValue = 1;
            m_HasLoggedMtl4SubmitOrder = false;

            if (m_NativeQueue.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLCommandQueue.");
            }

            if (device.BindingCapabilities.SupportsArgumentTable)
            {
                m_NativeQueue4 = device.NativeDevice.NewMTL4CommandQueue();
                if (m_NativeQueue4.NativePtr != IntPtr.Zero)
                {
                    m_NativeMtl4CommandAllocator = device.NativeDevice.NewMTL4CommandAllocator();
                    if (m_NativeMtl4CommandAllocator.NativePtr != IntPtr.Zero)
                    {
                        m_Mtl4CompletionEvent = device.NativeDevice.NewSharedEvent();
                    }
                }

                if (SupportsMtl4Submission)
                {
                    InitializeResidencySet();
                }
                else
                {
                    Console.WriteLine("[MetalQueue] MTL4 queue/allocator/event is unavailable. MTL4 command submission is disabled for this queue.");
                    ReleaseMtl4Handles();
                }
            }
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new MetalCommandBuffer(this);
        }

        public override void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            UpdateSparseTextureMappings(tiledTextureRegions, MTLSparseTextureMappingMode.Map);
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            UpdateSparseTextureMappings(tiledTextureRegions, MTLSparseTextureMappingMode.Unmap);
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            UpdatePackedMipMappings(tiledTexturePackedMips, MTLSparseTextureMappingMode.Map);
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            UpdatePackedMipMappings(tiledTexturePackedMips, MTLSparseTextureMappingMode.Unmap);
        }

        private void UpdateSparseTextureMappings(in RHITiledTextureRegions tiledTextureRegions, MTLSparseTextureMappingMode mode)
        {
            MetalTexture metalTexture = tiledTextureRegions.Texture as MetalTexture ?? throw new ArgumentException("Invalid texture type for Metal sparse mapping.");
            Span<RHITextureCoordinateRegion> regions = tiledTextureRegions.Regions.Span;
            if (regions.Length == 0)
            {
                return;
            }

            MTLCommandBuffer cmdBuffer = m_NativeQueue.CommandBuffer();
            MTLResourceStateCommandEncoder encoder = cmdBuffer.ResourceStateCommandEncoder();
            if (encoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create resource state command encoder for sparse texture mapping.");
            }

            for (int i = 0; i < regions.Length; ++i)
            {
                ref RHITextureCoordinateRegion region = ref regions[i];
                MTLRegion mtlRegion;
                mtlRegion.origin.x = (ulong)Math.Max(0, region.Start.X);
                mtlRegion.origin.y = (ulong)Math.Max(0, region.Start.Y);
                mtlRegion.origin.z = (ulong)Math.Max(0, region.Start.Z);
                mtlRegion.size.width = (ulong)Math.Max(0, region.End.X - region.Start.X);
                mtlRegion.size.height = (ulong)Math.Max(0, region.End.Y - region.Start.Y);
                mtlRegion.size.depth = (ulong)Math.Max(0, region.End.Z - region.Start.Z);
                encoder.UpdateTextureMapping(metalTexture.NativeTexture, mode, mtlRegion, (ulong)Math.Max(0, region.MipLevel), (ulong)Math.Max(0, region.Layer));
            }

            encoder.EndEncoding();
            cmdBuffer.Commit();
            cmdBuffer.WaitUntilCompleted();
        }

        private void UpdatePackedMipMappings(in RHITiledTexturePackedMips tiledTexturePackedMips, MTLSparseTextureMappingMode mode)
        {
            Span<RHITiledTexturePackedMip> packedMips = tiledTexturePackedMips.PackedMips.Span;
            if (packedMips.Length == 0)
            {
                return;
            }

            MTLCommandBuffer cmdBuffer = m_NativeQueue.CommandBuffer();
            MTLResourceStateCommandEncoder encoder = cmdBuffer.ResourceStateCommandEncoder();
            if (encoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create resource state command encoder for packed mip mapping.");
            }

            for (int i = 0; i < packedMips.Length; ++i)
            {
                ref RHITiledTexturePackedMip packedMip = ref packedMips[i];
                MetalTexture metalTexture = packedMip.Texture as MetalTexture ?? throw new ArgumentException($"Invalid texture type for Metal packed mip mapping at index {i}.");

                ulong firstPackedMipLevel = metalTexture.NativeTexture.FirstMipmapInTail;
                ulong totalMipLevels = metalTexture.NativeTexture.MipmapLevelCount;

                for (ulong mip = firstPackedMipLevel; mip < totalMipLevels; ++mip)
                {
                    MTLRegion mtlRegion;
                    mtlRegion.origin.x = 0;
                    mtlRegion.origin.y = 0;
                    mtlRegion.origin.z = 0;
                    ulong mipWidth = Math.Max(1UL, metalTexture.NativeTexture.Width >> (int)mip);
                    ulong mipHeight = Math.Max(1UL, metalTexture.NativeTexture.Height >> (int)mip);
                    ulong mipDepth = Math.Max(1UL, metalTexture.NativeTexture.Depth >> (int)mip);
                    mtlRegion.size.width = mipWidth;
                    mtlRegion.size.height = mipHeight;
                    mtlRegion.size.depth = mipDepth;
                    encoder.UpdateTextureMapping(metalTexture.NativeTexture, mode, mtlRegion, mip, (ulong)Math.Max(0, packedMip.Layer));
                }
            }

            encoder.EndEncoding();
            cmdBuffer.Commit();
            cmdBuffer.WaitUntilCompleted();
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
            metalCommandBuffer.FinalizeForSubmit();

            switch (metalCommandBuffer.EncodingPath)
            {
                case MetalCommandEncodingPath.Classic:
                    SubmitClassic(metalCommandBuffer, signalFence as MetalFence, waitSemaphore as MetalSemaphore, signalSemaphore as MetalSemaphore);
                    break;
                case MetalCommandEncodingPath.MTL4:
                    SubmitMtl4(metalCommandBuffer, signalFence as MetalFence, waitSemaphore as MetalSemaphore, signalSemaphore as MetalSemaphore);
                    break;
                default:
                    throw new InvalidOperationException("Command buffer encoding path is unresolved. Set at least one pipeline before submission.");
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

        private void SubmitClassic(MetalCommandBuffer metalCommandBuffer, MetalFence? signalFence, MetalSemaphore? waitSemaphore, MetalSemaphore? signalSemaphore)
        {
            MTLCommandBuffer nativeCommandBuffer = metalCommandBuffer.NativeCommandBuffer;
            if (nativeCommandBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Classic command buffer has not begun encoding.");
            }

            EncodeWait(nativeCommandBuffer, waitSemaphore);
            EncodeSignal(nativeCommandBuffer, signalSemaphore);
            PresentDrawable(nativeCommandBuffer, metalCommandBuffer.PresentDrawable);

            nativeCommandBuffer.Commit();
            m_LastSubmittedCommandBuffer = nativeCommandBuffer;

            if (signalFence != null)
            {
                nativeCommandBuffer.WaitUntilCompleted();
                SignalFence(signalFence);
            }
        }

        private void SubmitMtl4(MetalCommandBuffer metalCommandBuffer, MetalFence? signalFence, MetalSemaphore? waitSemaphore, MetalSemaphore? signalSemaphore)
        {
            if (!SupportsMtl4Submission)
            {
                throw new InvalidOperationException("MTL4 command submission is unavailable on this queue/device.");
            }

            MTL4CommandBuffer nativeCommandBuffer = metalCommandBuffer.NativeCommandBuffer4;
            if (nativeCommandBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("MTL4 command buffer has not begun encoding.");
            }

            WaitForDrawable(m_NativeQueue4, metalCommandBuffer.PresentDrawable);
            EncodeWait(m_NativeQueue4, waitSemaphore);

            CommitMtl4(nativeCommandBuffer);

            if (!m_HasLoggedMtl4SubmitOrder)
            {
                Console.WriteLine("[MetalQueue] MTL4 submit order: waitForDrawable -> waitForEvent -> commit -> signalEvent -> signalDrawable -> present.");
                m_HasLoggedMtl4SubmitOrder = true;
            }

            EncodeSignal(m_NativeQueue4, signalSemaphore);

            ulong completionValue = m_NextMtl4CompletionValue++;
            m_NativeQueue4.SignalEvent(m_Mtl4CompletionEvent, completionValue);
            SignalDrawable(m_NativeQueue4, metalCommandBuffer.PresentDrawable);
            PresentDrawable(metalCommandBuffer.PresentDrawable);
            m_LastSubmittedMtl4Value = completionValue;

            if (signalFence != null)
            {
                WaitForMtl4Completion(completionValue);
                SignalFence(signalFence);
            }
        }

        internal void AddResidencyAllocation(in MTLAllocation allocation)
        {
            if (m_ResidencySet.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (allocation.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (!m_ResidencySet.ContainsAllocation(allocation))
            {
                m_ResidencySet.AddAllocation(allocation);
                m_ResidencySet.Commit();
            }
        }

        private void InitializeResidencySet()
        {
            MTLResidencySetDescriptor descriptor = MTLResidencySetDescriptor.New();
            descriptor.InitialCapacity = 256;

            NSError error = default;
            IntPtr residencySetPtr = m_MetalDevice.NativeDevice.NewResidencySetWithDescriptor(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);

            if (residencySetPtr == IntPtr.Zero)
            {
                Console.WriteLine("[MetalQueue] Failed to create MTLResidencySet. Residency tracking is disabled for this queue.");
                return;
            }

            m_ResidencySet = new MTLResidencySet(residencySetPtr);
            m_ResidencySet.RequestResidency();
            m_ResidencySet.Commit();
            m_NativeQueue4.AddResidencySet(m_ResidencySet);
        }

        private void WaitLastSubmission()
        {
            if (m_LastSubmittedCommandBuffer.NativePtr != IntPtr.Zero)
            {
                m_LastSubmittedCommandBuffer.WaitUntilCompleted();
                m_LastSubmittedCommandBuffer = default;
            }

            if (m_LastSubmittedMtl4Value > 0 && m_Mtl4CompletionEvent.NativePtr != IntPtr.Zero)
            {
                WaitForMtl4Completion(m_LastSubmittedMtl4Value);
                m_LastSubmittedMtl4Value = 0;
            }
        }

        private void WaitForMtl4Completion(in ulong completionValue)
        {
            if (m_Mtl4CompletionEvent.NativePtr == IntPtr.Zero || completionValue == 0)
            {
                return;
            }

            m_Mtl4CompletionEvent.WaitUntilSignaledValue(completionValue, ulong.MaxValue);
        }

        private unsafe void CommitMtl4(in MTL4CommandBuffer commandBuffer)
        {
            IntPtr* commandBufferArray = stackalloc IntPtr[1];
            commandBufferArray[0] = commandBuffer.NativePtr;
            m_NativeQueue4.Commit((IntPtr)commandBufferArray, 1);
        }

        private static void PresentDrawable(in MTLCommandBuffer nativeCommandBuffer, in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr != IntPtr.Zero)
            {
                nativeCommandBuffer.PresentDrawable(drawable);
            }
        }

        private static void SignalDrawable(in MTL4CommandQueue nativeQueue, in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            nativeQueue.SignalDrawable(drawable);
        }

        private static void PresentDrawable(in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            drawable.Present();
        }

        private static void WaitForDrawable(in MTL4CommandQueue nativeQueue, in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            nativeQueue.WaitForDrawable(drawable);
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

        private static void EncodeWait(in MTL4CommandQueue nativeQueue, MetalSemaphore? waitSemaphore)
        {
            if (waitSemaphore == null)
            {
                return;
            }

            ulong waitValue = waitSemaphore.CurrentValue;
            if (waitValue > 1)
            {
                nativeQueue.WaitForEvent(waitSemaphore.NativeEvent, waitValue - 1);
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

        private static void EncodeSignal(in MTL4CommandQueue nativeQueue, MetalSemaphore? signalSemaphore)
        {
            if (signalSemaphore == null)
            {
                return;
            }

            ulong signalValue = signalSemaphore.AcquireSignalValue();
            nativeQueue.SignalEvent(signalSemaphore.NativeEvent, signalValue);
        }

        private static void SignalFence(MetalFence? fence)
        {
            fence?.Signal();
        }

        protected override void Release()
        {
            ReleaseMtl4Handles();

            if (m_NativeQueue.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeQueue);
                m_NativeQueue = default;
            }
        }

        private void ReleaseMtl4Handles()
        {
            if (m_ResidencySet.NativePtr != IntPtr.Zero)
            {
                m_ResidencySet.EndResidency();
                ObjectiveCRuntime.Release(m_ResidencySet.NativePtr);
                m_ResidencySet = default;
            }

            if (m_Mtl4CompletionEvent.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_Mtl4CompletionEvent);
                m_Mtl4CompletionEvent = default;
            }

            if (m_NativeMtl4CommandAllocator.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeMtl4CommandAllocator.NativePtr);
                m_NativeMtl4CommandAllocator = default;
            }

            if (m_NativeQueue4.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeQueue4.NativePtr);
                m_NativeQueue4 = default;
            }
        }
    }
}
