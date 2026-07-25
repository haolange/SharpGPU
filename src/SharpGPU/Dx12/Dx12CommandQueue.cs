using System;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12CommandQueue : RHICommandQueue
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandQueue NativeCommandQueue
        {
            get
            {
                return m_NativeCommandQueue;
            }
        }
        public override ulong Frequency
        {
            get
            {
                m_NativeCommandQueue.GetTimestampFrequency(out ulong result);
                return result;
            }
        }

        protected override object DeviceIdentity => m_Dx12Device;

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12CommandQueue m_NativeCommandQueue;

        public Dx12CommandQueue(Dx12Device device, in ERHIPipelineType pipeline)
        {
            m_Dx12Device = device;
            m_PipelineType = pipeline;

            Vortice.Direct3D12.CommandQueueDescription queueDesc = new Vortice.Direct3D12.CommandQueueDescription();
            queueDesc.Flags = Vortice.Direct3D12.CommandQueueFlags.None;
            queueDesc.Type = Dx12Utility.ConvertToDx12QueueType(pipeline);

            Vortice.Direct3D12.ID3D12CommandQueue? commandQueue;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommandQueue(queueDesc, out commandQueue);
            m_NativeCommandQueue = Dx12Utility.RequireCreatedObject(
                commandQueue,
                hResult,
                "ID3D12Device.CreateCommandQueue");
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new Dx12CommandBuffer(this);
        }

        public override void Submit(in RHIQueueSubmitDescriptor descriptor)
        {
            ValidateSubmit(in descriptor);
            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHICommandBuffer> commandBuffers = descriptor.CommandBuffers.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waitSemaphores.Length; ++i)
            {
                if (waitSemaphores[i].Semaphore is not Dx12Semaphore)
                {
                    throw new ArgumentException($"Wait semaphore at index {i} is not a DX12 semaphore.", nameof(descriptor));
                }
            }

            for (int i = 0; i < commandBuffers.Length; ++i)
            {
                if (commandBuffers[i] is not Dx12CommandBuffer)
                {
                    throw new ArgumentException($"Command buffer at index {i} is not a DX12 command buffer.", nameof(descriptor));
                }
            }

            for (int i = 0; i < signalSemaphores.Length; ++i)
            {
                if (signalSemaphores[i] is not Dx12Semaphore)
                {
                    throw new ArgumentException($"Signal semaphore at index {i} is not a DX12 semaphore.", nameof(descriptor));
                }
            }

            if (descriptor.CompletionFence != null && descriptor.CompletionFence is not Dx12Fence)
            {
                throw new ArgumentException("The completion fence is not a DX12 fence.", nameof(descriptor));
            }

            ReserveSubmit(in descriptor);
            try
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = (Dx12Semaphore)waitSemaphores[i].Semaphore;
                    ulong waitValue = dx12Semaphore.LastSignaledValue;
                    if (waitValue == 0)
                    {
                        throw new InvalidOperationException($"Wait semaphore at index {i} has no native signal value.");
                    }

                    SharpGen.Runtime.Result waitResult = m_NativeCommandQueue.Wait(dx12Semaphore.NativeFence, waitValue);
                    Dx12Utility.CHECK_HR(waitResult);
                }

                if (commandBuffers.Length == 1)
                {
                    m_NativeCommandQueue.ExecuteCommandList(((Dx12CommandBuffer)commandBuffers[0]).NativeCommandList);
                }
                else if (commandBuffers.Length > 1)
                {
                    Vortice.Direct3D12.ID3D12CommandList[] commandLists =
                        new Vortice.Direct3D12.ID3D12CommandList[commandBuffers.Length];
                    for (int i = 0; i < commandBuffers.Length; ++i)
                    {
                        commandLists[i] = ((Dx12CommandBuffer)commandBuffers[i]).NativeCommandList;
                    }

                    m_NativeCommandQueue.ExecuteCommandLists(commandLists);
                }

                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = (Dx12Semaphore)signalSemaphores[i];
                    ulong signalValue = dx12Semaphore.PrepareSignalValue();
                    SharpGen.Runtime.Result signalResult = m_NativeCommandQueue.Signal(dx12Semaphore.NativeFence, signalValue);
                    Dx12Utility.CHECK_HR(signalResult);
                }

                if (descriptor.CompletionFence is Dx12Fence dx12Fence)
                {
                    ulong signalValue = dx12Fence.PrepareSignalValue();
                    SharpGen.Runtime.Result signalResult = m_NativeCommandQueue.Signal(dx12Fence.NativeFence, signalValue);
                    Dx12Utility.CHECK_HR(signalResult);
                }

                CommitSubmit(in descriptor);
            }
            catch (Exception exception)
            {
                RollbackSubmit(in descriptor);
                if (exception is RHIException
                    {
                        ErrorCode: ERHIErrorCode.DeviceLost
                    } deviceLoss)
                {
                    m_Dx12Device.MarkDeviceLost(deviceLoss);
                }
                throw;
            }
        }

        protected override void WaitIdleCore()
        {
            using Dx12Fence completion = new(m_Dx12Device);
            RHIQueueSubmitDescriptor descriptor =
                new(completionFence: completion);
            Submit(in descriptor);
            EFenceStatus status = completion.Wait();
            if (status != EFenceStatus.Success)
            {
                throw new RHIException(
                    ERHIErrorCode.SynchronizationFailed,
                    ERHIBackend.DirectX12,
                    nativeCode: 0,
                    $"DX12 queue lifecycle drain completed with '{status}'.",
                    ERHIDeviceState.Operational);
            }
        }

        public override void BindSparse(in RHISparseBindDescriptor descriptor)
        {
            m_Dx12Device.Capabilities.Memory.SparseBinding.Require(
                "DX12 queue-ordered sparse texture binding");
            ValidateSparseBind(in descriptor);
            ValidateDx12SparseBindings(in descriptor);

            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waits.Length; ++i)
            {
                if (waits[i] is not Dx12Semaphore)
                {
                    throw new ArgumentException(
                        $"Wait semaphore at index {i} is not a DX12 semaphore.",
                        nameof(descriptor));
                }
            }
            for (int i = 0; i < signals.Length; ++i)
            {
                if (signals[i] is not Dx12Semaphore)
                {
                    throw new ArgumentException(
                        $"Signal semaphore at index {i} is not a DX12 semaphore.",
                        nameof(descriptor));
                }
            }
            if (descriptor.CompletionFence != null &&
                descriptor.CompletionFence is not Dx12Fence)
            {
                throw new ArgumentException(
                    "The sparse completion fence is not a DX12 fence.",
                    nameof(descriptor));
            }

            ReserveSparseBind(in descriptor);
            try
            {
                for (int i = 0; i < waits.Length; ++i)
                {
                    Dx12Semaphore semaphore = (Dx12Semaphore)waits[i];
                    ulong value = semaphore.LastSignaledValue;
                    if (value == 0)
                    {
                        throw new InvalidOperationException(
                            $"Wait semaphore at index {i} has no native signal value.");
                    }
                    Dx12Utility.CHECK_HR(
                        m_NativeCommandQueue.Wait(semaphore.NativeFence, value));
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
                    Dx12Semaphore semaphore = (Dx12Semaphore)signals[i];
                    ulong value = semaphore.PrepareSignalValue();
                    Dx12Utility.CHECK_HR(
                        m_NativeCommandQueue.Signal(semaphore.NativeFence, value));
                }
                if (descriptor.CompletionFence is Dx12Fence fence)
                {
                    ulong value = fence.PrepareSignalValue();
                    Dx12Utility.CHECK_HR(
                        m_NativeCommandQueue.Signal(fence.NativeFence, value));
                }

                CommitSparseBind(in descriptor);
            }
            catch (Exception exception)
            {
                RollbackSparseBind(in descriptor);
                if (exception is RHIException
                    {
                        ErrorCode: ERHIErrorCode.DeviceLost
                    } deviceLoss)
                {
                    m_Dx12Device.MarkDeviceLost(deviceLoss);
                }
                throw;
            }
        }

        private void ValidateDx12SparseBindings(
            in RHISparseBindDescriptor descriptor)
        {
            ReadOnlySpan<RHISparseTextureTileBinding> tileBindings =
                descriptor.TileBindings.Span;
            for (int i = 0; i < tileBindings.Length; ++i)
            {
                ref readonly RHISparseTextureTileBinding binding =
                    ref tileBindings[i];
                Dx12Texture texture = RequireSparseTexture(
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
                _ = checked((ushort)binding.TileExtent.y);
                _ = checked((ushort)binding.TileExtent.z);
                if (binding.Operation == ERHISparseBindingOperation.Bind)
                {
                    Dx12Heap heap = RequireSparseHeap(
                        binding.Heap,
                        $"tile binding at index {i}");
                    heap.ValidateSparseSpan(
                        binding.HeapOffset,
                        checked(tileCount * requirements.TileSizeBytes),
                        requirements.HeapCompatibility);
                    _ = checked((int)(
                        binding.HeapOffset / requirements.TileSizeBytes));
                }
            }

            ReadOnlySpan<RHISparseTextureMipTailBinding> tailBindings =
                descriptor.MipTailBindings.Span;
            for (int i = 0; i < tailBindings.Length; ++i)
            {
                ref readonly RHISparseTextureMipTailBinding binding =
                    ref tailBindings[i];
                Dx12Texture texture = RequireSparseTexture(
                    binding.Texture,
                    $"mip-tail binding at index {i}");
                RHISparseTextureMemoryRequirements requirements =
                    texture.SparseRequirements!;
                RHISparseTextureMipTail tail =
                    requirements.GetMipTail(binding.MipTailIndex);
                _ = checked((int)(tail.SizeBytes / requirements.TileSizeBytes));
                if (binding.Operation == ERHISparseBindingOperation.Bind)
                {
                    Dx12Heap heap = RequireSparseHeap(
                        binding.Heap,
                        $"mip-tail binding at index {i}");
                    heap.ValidateSparseSpan(
                        binding.HeapOffset,
                        tail.SizeBytes,
                        requirements.HeapCompatibility);
                    _ = checked((int)(
                        binding.HeapOffset / requirements.TileSizeBytes));
                }
            }
        }

        private void ExecuteTileBinding(
            in RHISparseTextureTileBinding binding)
        {
            Dx12Texture texture = (Dx12Texture)binding.Texture;
            RHISparseTextureMemoryRequirements requirements =
                texture.SparseRequirements!;
            ulong tileCount64 = checked(
                (ulong)binding.TileExtent.x *
                binding.TileExtent.y *
                binding.TileExtent.z);
            int tileCount = checked((int)tileCount64);
            uint nativeSubresource = checked(
                binding.MipLevel +
                binding.ArrayLayer * texture.Descriptor.MipCount);
            Vortice.Direct3D12.TiledResourceCoordinate[] coordinates =
            {
                new Vortice.Direct3D12.TiledResourceCoordinate(
                    binding.TileOffset.x,
                    binding.TileOffset.y,
                    binding.TileOffset.z,
                    nativeSubresource),
            };
            Vortice.Direct3D12.TileRegionSize[] regions =
            {
                new Vortice.Direct3D12.TileRegionSize(
                    binding.TileExtent.x,
                    checked((ushort)binding.TileExtent.y),
                    checked((ushort)binding.TileExtent.z)),
            };
            Vortice.Direct3D12.TileRangeFlags[] rangeFlags =
            {
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? Vortice.Direct3D12.TileRangeFlags.None
                    : Vortice.Direct3D12.TileRangeFlags.Null,
            };
            int[] heapOffsets =
            {
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? checked((int)(binding.HeapOffset / requirements.TileSizeBytes))
                    : 0,
            };
            int[] tileCounts = { tileCount };
            Vortice.Direct3D12.ID3D12Heap? nativeHeap =
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? ((Dx12Heap)binding.Heap!).NativeHeap
                    : null;
            m_NativeCommandQueue.UpdateTileMappings(
                texture.NativeResource,
                coordinates,
                regions,
                nativeHeap!,
                rangeFlags,
                heapOffsets,
                tileCounts);
        }

        private void ExecuteMipTailBinding(
            in RHISparseTextureMipTailBinding binding)
        {
            Dx12Texture texture = (Dx12Texture)binding.Texture;
            RHISparseTextureMemoryRequirements requirements =
                texture.SparseRequirements!;
            RHISparseTextureMipTail tail =
                requirements.GetMipTail(binding.MipTailIndex);
            int tileCount = checked((int)(
                tail.SizeBytes / requirements.TileSizeBytes));
            Vortice.Direct3D12.TiledResourceCoordinate[] coordinates =
            {
                new Vortice.Direct3D12.TiledResourceCoordinate(
                    0,
                    0,
                    0,
                    tail.FirstMipLevel),
            };
            Vortice.Direct3D12.TileRegionSize[] regions =
            {
                new Vortice.Direct3D12.TileRegionSize(checked((uint)tileCount)),
            };
            Vortice.Direct3D12.TileRangeFlags[] rangeFlags =
            {
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? Vortice.Direct3D12.TileRangeFlags.None
                    : Vortice.Direct3D12.TileRangeFlags.Null,
            };
            int[] heapOffsets =
            {
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? checked((int)(binding.HeapOffset / requirements.TileSizeBytes))
                    : 0,
            };
            int[] tileCounts = { tileCount };
            Vortice.Direct3D12.ID3D12Heap? nativeHeap =
                binding.Operation == ERHISparseBindingOperation.Bind
                    ? ((Dx12Heap)binding.Heap!).NativeHeap
                    : null;
            m_NativeCommandQueue.UpdateTileMappings(
                texture.NativeResource,
                coordinates,
                regions,
                nativeHeap!,
                rangeFlags,
                heapOffsets,
                tileCounts);
        }

        private Dx12Texture RequireSparseTexture(
            RHITexture texture,
            string argumentDescription)
        {
            if (texture.IsDisposed)
            {
                throw new ObjectDisposedException(texture.GetType().FullName);
            }
            if (texture is not Dx12Texture dx12Texture ||
                !ReferenceEquals(dx12Texture.Dx12Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture belongs to a different backend or device.");
            }
            if (dx12Texture.AllocationMode != ERHIResourceAllocationMode.Sparse ||
                dx12Texture.SparseRequirements == null)
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture is not a sparse texture.");
            }
            return dx12Texture;
        }

        private Dx12Heap RequireSparseHeap(
            RHIHeap? heap,
            string argumentDescription)
        {
            if (heap is not Dx12Heap dx12Heap ||
                !ReferenceEquals(heap.OwnerDevice, m_Dx12Device))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} heap belongs to a different backend or device.");
            }
            return dx12Heap;
        }

        private static void ValidateTileRange(
            in SharpGPU.Mathematics.uint3 offset,
            in SharpGPU.Mathematics.uint3 extent,
            in SharpGPU.Mathematics.uint3 available,
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

        protected override void Release()
        {
            m_NativeCommandQueue.Release();
        }
    }
#pragma warning restore CA1416
}
