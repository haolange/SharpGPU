using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalCommandQueue : RHICommandQueue
    {
        public MetalDevice MetalDevice => m_MetalDevice;
        public override ulong Frequency => 1_000_000_000UL;

        internal bool SupportsMtl4Submission => m_NativeQueue4.NativePtr != IntPtr.Zero;

        internal MTL4CommandQueue NativeQueue4 => m_NativeQueue4;
        internal MTLResidencySet NativeResidencySet => m_ResidencySet;
        internal bool HasResidencySet => m_ResidencySet.NativePtr != IntPtr.Zero;
        protected override object DeviceIdentity => m_MetalDevice;

        private readonly MetalDevice m_MetalDevice;
        private MTL4CommandQueue m_NativeQueue4;
        private MTLResidencySet m_ResidencySet;
        private bool m_HasLoggedMtl4SubmitOrder;

        public MetalCommandQueue(MetalDevice device, in ERHIPipelineType pipeline)
        {
            m_MetalDevice = device;
            m_PipelineType = pipeline;
            m_NativeQueue4 = default;
            m_ResidencySet = default;
            m_HasLoggedMtl4SubmitOrder = false;

            m_NativeQueue4 = device.NativeDevice.NewMTL4CommandQueue();
            if (m_NativeQueue4.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal backend requires Metal 4 command queue support. Failed to create MTL4CommandQueue.");
            }

            InitializeResidencySet();
        }

        internal void WaitPresentation(ReadOnlySpan<RHISemaphore> waits)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            for (int i = 0; i < waits.Length; ++i)
            {
                if (waits[i] is not MetalSemaphore semaphore)
                {
                    throw new ArgumentException(
                        $"Wait semaphore at index {i} is not a Metal semaphore.");
                }

                ulong waitValue = semaphore.LastSignaledValue;
                if (waitValue == 0)
                {
                    throw new InvalidOperationException(
                        $"Wait semaphore at index {i} has no native signal value.");
                }

                m_NativeQueue4.WaitForEvent(semaphore.NativeEvent, waitValue);
            }
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            return new MetalCommandBuffer(this);
        }

        public override unsafe void Submit(in RHIQueueSubmitDescriptor descriptor)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            ValidateSubmit(in descriptor);
            if (!SupportsMtl4Submission)
            {
                throw new InvalidOperationException("MTL4 command submission is unavailable on this queue/device.");
            }

            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHICommandBuffer> commandBuffers = descriptor.CommandBuffers.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            int commandBufferCount = commandBuffers.Length;

            for (int i = 0; i < waitSemaphores.Length; ++i)
            {
                if (waitSemaphores[i].Semaphore is not MetalSemaphore)
                {
                    throw new ArgumentException($"Wait semaphore at index {i} is not a Metal semaphore.", nameof(descriptor));
                }
            }

            for (int i = 0; i < signalSemaphores.Length; ++i)
            {
                if (signalSemaphores[i] is not MetalSemaphore)
                {
                    throw new ArgumentException($"Signal semaphore at index {i} is not a Metal semaphore.", nameof(descriptor));
                }
            }

            if (descriptor.CompletionFence != null && descriptor.CompletionFence is not MetalFence)
            {
                throw new ArgumentException("The completion fence is not a Metal fence.", nameof(descriptor));
            }

            IntPtr* nativeCommandBuffers = stackalloc IntPtr[Math.Max(commandBufferCount, 1)];
            for (int i = 0; i < commandBufferCount; ++i)
            {
                if (commandBuffers[i] is not MetalCommandBuffer metalCommandBuffer)
                {
                    throw new ArgumentException($"Command buffer at index {i} is not a Metal command buffer.", nameof(descriptor));
                }

                metalCommandBuffer.FinalizeForSubmit();
                MTL4CommandBuffer nativeCommandBuffer = metalCommandBuffer.NativeCommandBuffer4;
                if (nativeCommandBuffer.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"MTL4 command buffer at index {i} has not begun encoding.");
                }

                nativeCommandBuffers[i] = nativeCommandBuffer.NativePtr;
            }

            ReserveSubmit(in descriptor);
            try
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    MetalSemaphore semaphore = (MetalSemaphore)waitSemaphores[i].Semaphore;
                    ulong waitValue = semaphore.LastSignaledValue;
                    if (waitValue == 0)
                    {
                        throw new InvalidOperationException($"Wait semaphore at index {i} has no native signal value.");
                    }

                    m_NativeQueue4.WaitForEvent(semaphore.NativeEvent, waitValue);
                }

                for (int i = 0; i < commandBufferCount; ++i)
                {
                    WaitForDrawable(m_NativeQueue4, ((MetalCommandBuffer)commandBuffers[i]).PresentDrawable);
                }

                if (commandBufferCount > 0)
                {
                    CommitWithFeedback((IntPtr)nativeCommandBuffers, (ulong)commandBufferCount);
                }

                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    MetalSemaphore semaphore = (MetalSemaphore)signalSemaphores[i];
                    ulong signalValue = semaphore.PrepareSignalValue();
                    m_NativeQueue4.SignalEvent(semaphore.NativeEvent, signalValue);
                }

                if (descriptor.CompletionFence is MetalFence fence)
                {
                    ulong signalValue = fence.PrepareSignalValue();
                    m_NativeQueue4.SignalEvent(fence.NativeEvent, signalValue);
                }

                CommitSubmit(in descriptor);
            }
            catch
            {
                RollbackSubmit(in descriptor);
                throw;
            }

            for (int i = 0; i < commandBufferCount; ++i)
            {
                MetalCommandBuffer commandBuffer = (MetalCommandBuffer)commandBuffers[i];
                SignalDrawable(m_NativeQueue4, commandBuffer.PresentDrawable);
            }

            if (!m_HasLoggedMtl4SubmitOrder)
            {
                Console.WriteLine("[MetalQueue] MTL4 submit order: waitForDrawable/waitForEvent -> commit(feedback) -> signalEvent/signalDrawable; RHISwapChain.Present owns presentation.");
                m_HasLoggedMtl4SubmitOrder = true;
            }
        }

        public override unsafe void BindSparse(
            in RHISparseBindDescriptor descriptor)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            m_MetalDevice.Capabilities.Memory.RequireSparseBind(
                descriptor,
                "Metal queue-ordered placement sparse texture binding");
            ValidateSparseBind(in descriptor);
            ValidateMetalSparseBindings(in descriptor);

            ReadOnlySpan<RHISemaphore> waits =
                descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals =
                descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waits.Length; ++i)
            {
                if (waits[i] is not MetalSemaphore)
                {
                    throw new ArgumentException(
                        $"Wait semaphore at index {i} is not a Metal semaphore.",
                        nameof(descriptor));
                }
            }
            for (int i = 0; i < signals.Length; ++i)
            {
                if (signals[i] is not MetalSemaphore)
                {
                    throw new ArgumentException(
                        $"Signal semaphore at index {i} is not a Metal semaphore.",
                        nameof(descriptor));
                }
            }
            if (descriptor.CompletionFence != null &&
                descriptor.CompletionFence is not MetalFence)
            {
                throw new ArgumentException(
                    "The sparse completion fence is not a Metal fence.",
                    nameof(descriptor));
            }

            ReserveSparseBind(in descriptor);
            try
            {
                for (int i = 0; i < waits.Length; ++i)
                {
                    MetalSemaphore semaphore =
                        (MetalSemaphore)waits[i];
                    ulong value = semaphore.LastSignaledValue;
                    if (value == 0)
                    {
                        throw new InvalidOperationException(
                            $"Wait semaphore at index {i} has no native signal value.");
                    }
                    m_NativeQueue4.WaitForEvent(
                        semaphore.NativeEvent,
                        value);
                }

                ReadOnlySpan<RHISparseTextureTileBinding> tileBindings =
                    descriptor.TileBindings.Span;
                for (int i = 0; i < tileBindings.Length; ++i)
                {
                    ExecuteTileBinding(in tileBindings[i]);
                }
                ReadOnlySpan<RHISparseTextureMipTailBinding> tailBindings =
                    descriptor.MipTailBindings.Span;
                for (int i = 0; i < tailBindings.Length; ++i)
                {
                    ExecuteMipTailBinding(in tailBindings[i]);
                }

                for (int i = 0; i < signals.Length; ++i)
                {
                    MetalSemaphore semaphore =
                        (MetalSemaphore)signals[i];
                    ulong value = semaphore.PrepareSignalValue();
                    m_NativeQueue4.SignalEvent(
                        semaphore.NativeEvent,
                        value);
                }
                if (descriptor.CompletionFence is MetalFence fence)
                {
                    ulong value = fence.PrepareSignalValue();
                    m_NativeQueue4.SignalEvent(fence.NativeEvent, value);
                }

                CommitSparseBind(in descriptor);
            }
            catch
            {
                RollbackSparseBind(in descriptor);
                throw;
            }
        }

        private void ValidateMetalSparseBindings(
            in RHISparseBindDescriptor descriptor)
        {
            ReadOnlySpan<RHISparseTextureTileBinding> tileBindings =
                descriptor.TileBindings.Span;
            for (int i = 0; i < tileBindings.Length; ++i)
            {
                ref readonly RHISparseTextureTileBinding binding =
                    ref tileBindings[i];
                MetalTexture texture = RequireSparseTexture(
                    binding.Texture,
                    $"tile binding at index {i}");
                RHISparseTextureMemoryRequirements requirements =
                    texture.SparseRequirements!;
                RHISparseTextureSubresourceTiling subresource =
                    requirements.GetSubresource(
                        binding.Aspect,
                        binding.MipLevel,
                        binding.ArrayLayer);
                ValidateTileRange(
                    binding.TileOffset,
                    binding.TileExtent,
                    subresource.TileCount,
                    $"tile binding at index {i}");
                ulong tileCount = checked(
                    (ulong)binding.TileExtent.x *
                    binding.TileExtent.y *
                    binding.TileExtent.z);
                if (binding.Operation == ERHISparseBindingOperation.Bind)
                {
                    MetalHeap heap = RequireSparseHeap(
                        binding.Heap,
                        $"tile binding at index {i}");
                    heap.ValidateSparseSpan(
                        binding.HeapOffset,
                        checked(tileCount * requirements.TileSizeBytes),
                        requirements.HeapCompatibility);
                }
            }

            ReadOnlySpan<RHISparseTextureMipTailBinding> tailBindings =
                descriptor.MipTailBindings.Span;
            for (int i = 0; i < tailBindings.Length; ++i)
            {
                ref readonly RHISparseTextureMipTailBinding binding =
                    ref tailBindings[i];
                MetalTexture texture = RequireSparseTexture(
                    binding.Texture,
                    $"mip-tail binding at index {i}");
                RHISparseTextureMemoryRequirements requirements =
                    texture.SparseRequirements!;
                RHISparseTextureMipTail tail =
                    requirements.GetMipTail(binding.MipTailIndex);
                if (binding.Operation == ERHISparseBindingOperation.Bind)
                {
                    MetalHeap heap = RequireSparseHeap(
                        binding.Heap,
                        $"mip-tail binding at index {i}");
                    heap.ValidateSparseSpan(
                        binding.HeapOffset,
                        tail.SizeBytes,
                        requirements.HeapCompatibility);
                }
            }
        }

        private unsafe void ExecuteTileBinding(
            in RHISparseTextureTileBinding binding)
        {
            MetalTexture texture = (MetalTexture)binding.Texture;
            RHISparseTextureMemoryRequirements requirements =
                texture.SparseRequirements!;
            MTL4UpdateSparseTextureMappingOperation operation =
                new MTL4UpdateSparseTextureMappingOperation
                {
                    mode = (ulong)(
                        binding.Operation == ERHISparseBindingOperation.Bind
                            ? MTLSparseTextureMappingMode.Map
                            : MTLSparseTextureMappingMode.Unmap),
                    textureRegion = new MTLRegion(
                        new MTLOrigin(
                            binding.TileOffset.x,
                            binding.TileOffset.y,
                            binding.TileOffset.z),
                        new MTLSize(
                            binding.TileExtent.x,
                            binding.TileExtent.y,
                            binding.TileExtent.z)),
                    textureLevel = binding.MipLevel,
                    textureSlice = binding.ArrayLayer,
                    heapOffset =
                        binding.Operation == ERHISparseBindingOperation.Bind
                            ? binding.HeapOffset /
                                requirements.TileSizeBytes
                            : 0,
                };
            MTLHeap nativeHeap =
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? ((MetalHeap)binding.Heap!).NativeHeap
                    : default;
            m_NativeQueue4.UpdateTextureMappings(
                texture.NativeTexture,
                nativeHeap,
                (IntPtr)(&operation),
                1);
        }

        private unsafe void ExecuteMipTailBinding(
            in RHISparseTextureMipTailBinding binding)
        {
            MetalTexture texture = (MetalTexture)binding.Texture;
            RHISparseTextureMemoryRequirements requirements =
                texture.SparseRequirements!;
            RHISparseTextureMipTail tail =
                requirements.GetMipTail(binding.MipTailIndex);
            MTL4UpdateSparseTextureMappingOperation operation =
                new MTL4UpdateSparseTextureMappingOperation
                {
                    mode = (ulong)(
                        binding.Operation == ERHISparseBindingOperation.Bind
                            ? MTLSparseTextureMappingMode.Map
                            : MTLSparseTextureMappingMode.Unmap),
                    textureRegion = new MTLRegion(
                        new MTLOrigin(0, 0, 0),
                        new MTLSize(1, 1, 1)),
                    textureLevel = tail.FirstMipLevel,
                    textureSlice = tail.FirstArrayLayer,
                    heapOffset =
                        binding.Operation == ERHISparseBindingOperation.Bind
                            ? binding.HeapOffset /
                                requirements.TileSizeBytes
                            : 0,
                };
            MTLHeap nativeHeap =
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? ((MetalHeap)binding.Heap!).NativeHeap
                    : default;
            m_NativeQueue4.UpdateTextureMappings(
                texture.NativeTexture,
                nativeHeap,
                (IntPtr)(&operation),
                1);
        }

        private MetalTexture RequireSparseTexture(
            RHITexture texture,
            string argumentDescription)
        {
            if (texture.IsDisposed)
            {
                throw new ObjectDisposedException(
                    texture.GetType().FullName);
            }
            if (texture is not MetalTexture metalTexture ||
                !ReferenceEquals(
                    metalTexture.MetalDevice,
                    m_MetalDevice))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture belongs to a different backend or device.");
            }
            if (metalTexture.AllocationMode !=
                    ERHIResourceAllocationMode.Sparse ||
                metalTexture.SparseRequirements == null)
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture is not a sparse texture.");
            }
            return metalTexture;
        }

        private MetalHeap RequireSparseHeap(
            RHIHeap? heap,
            string argumentDescription)
        {
            if (heap is not MetalHeap metalHeap ||
                !ReferenceEquals(heap.OwnerDevice, m_MetalDevice))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} heap belongs to a different backend or device.");
            }
            return metalHeap;
        }

        private static void ValidateTileRange(
            in SharpMath.uint3 offset,
            in SharpMath.uint3 extent,
            in SharpMath.uint3 available,
            string argumentDescription)
        {
            if ((ulong)offset.x + extent.x > available.x ||
                (ulong)offset.y + extent.y > available.y ||
                (ulong)offset.z + extent.z > available.z)
            {
                throw new ArgumentOutOfRangeException(
                    argumentDescription,
                    "Sparse tile binding exceeds the queried subresource tiling.");
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

        /// <summary>
        /// Registers an externally owned residency set (e.g. CAMetalLayer.residencySet)
        /// with this MTL4 queue so drawable textures can be made resident.
        /// </summary>
        internal void AddExternalResidencySet(in MTLResidencySet residencySet)
        {
            if (m_NativeQueue4.NativePtr == IntPtr.Zero ||
                residencySet.NativePtr == IntPtr.Zero)
            {
                return;
            }

            m_NativeQueue4.AddResidencySet(residencySet);
        }

        internal void RemoveResidencyAllocation(in MTLAllocation allocation)
        {
            if (m_ResidencySet.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (allocation.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_ResidencySet.ContainsAllocation(allocation))
            {
                m_ResidencySet.RemoveAllocation(allocation);
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

        private void CommitWithFeedback(
            IntPtr commandBuffers,
            ulong commandBufferCount)
        {
            // MTL4CommitOptions.addFeedbackHandler: requires a real ObjC block.
            // Our C# block trampoline still trips PAC faults on
            // com.Metal4.CompletionQueue (EXC_BAD_ACCESS in
            // _MTL4CommitFeedbackDispatch), which wedges subsequent encodes.
            // Commit without feedback until the trampoline is proven safe;
            // device-loss diagnostics continue to flow through other paths.
            m_NativeQueue4.Commit(commandBuffers, commandBufferCount);
        }

        private void ReleaseNativeObjects()
        {
            if (m_ResidencySet.NativePtr != IntPtr.Zero)
            {
                m_ResidencySet.EndResidency();
                ObjectiveCRuntime.Release(m_ResidencySet.NativePtr);
                m_ResidencySet = default;
            }

            if (m_NativeQueue4.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeQueue4.NativePtr);
                m_NativeQueue4 = default;
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

        private static void WaitForDrawable(in MTL4CommandQueue nativeQueue, in CAMetalDrawable drawable)
        {
            if (drawable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            nativeQueue.WaitForDrawable(drawable);
        }

        protected override void Release()
        {
            ReleaseNativeObjects();
        }
    }
}
