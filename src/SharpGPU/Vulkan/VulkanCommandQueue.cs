using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanCommandQueue : RHICommandQueue
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                return m_VulkanDevice;
            }
        }
        public VkQueue NativeQueue
        {
            get
            {
                return m_NativeQueue;
            }
        }
        public uint QueueFamilyIndex
        {
            get
            {
                return m_QueueFamilyIndex;
            }
        }
        public override ulong Frequency
        {
            get
            {
                VkPhysicalDeviceProperties properties;
                VulkanNative.vkGetPhysicalDeviceProperties(m_VulkanDevice.NativePhysicalDevice, &properties);
                float period = properties.limits.timestampPeriod;
                return period > 0 ? (ulong)(1_000_000_000.0 / period) : 1_000_000_000;
            }
        }

        protected override object DeviceIdentity => m_VulkanDevice;

        private VulkanDevice m_VulkanDevice;
        private VkQueue m_NativeQueue;
        private uint m_QueueFamilyIndex;

        public VulkanCommandQueue(VulkanDevice device, in ERHIPipelineType pipeline, uint queueFamilyIndex, uint queueIndex)
        {
            m_VulkanDevice = device;
            m_PipelineType = pipeline;
            m_QueueFamilyIndex = queueFamilyIndex;

            fixed (VkQueue* queuePtr = &m_NativeQueue)
            {
                VulkanNative.vkGetDeviceQueue(device.NativeDevice, queueFamilyIndex, queueIndex, queuePtr);
            }
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new VulkanCommandBuffer(this);
        }

        public override void Submit(in RHIQueueSubmitDescriptor descriptor)
        {
            ValidateSubmit(in descriptor);
            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHICommandBuffer> commandBuffers = descriptor.CommandBuffers.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            int waitCount = waitSemaphores.Length;
            int commandBufferCount = commandBuffers.Length;
            int signalCount = signalSemaphores.Length;

            for (int i = 0; i < waitCount; ++i)
            {
                if (waitSemaphores[i].Semaphore is not VulkanSemaphore)
                {
                    throw new ArgumentException($"Wait semaphore at index {i} is not a Vulkan semaphore.", nameof(descriptor));
                }
            }

            for (int i = 0; i < commandBufferCount; ++i)
            {
                if (commandBuffers[i] is not VulkanCommandBuffer)
                {
                    throw new ArgumentException($"Command buffer at index {i} is not a Vulkan command buffer.", nameof(descriptor));
                }
            }

            for (int i = 0; i < signalCount; ++i)
            {
                if (signalSemaphores[i] is not VulkanSemaphore)
                {
                    throw new ArgumentException($"Signal semaphore at index {i} is not a Vulkan semaphore.", nameof(descriptor));
                }
            }

            if (descriptor.CompletionFence != null && descriptor.CompletionFence is not VulkanFence)
            {
                throw new ArgumentException("The completion fence is not a Vulkan fence.", nameof(descriptor));
            }

            VkSemaphore* waitSems = stackalloc VkSemaphore[Math.Max(waitCount, 1)];
            VkPipelineStageFlags* waitStages = stackalloc VkPipelineStageFlags[Math.Max(waitCount, 1)];
            VkCommandBuffer* nativeCommandBuffers = stackalloc VkCommandBuffer[Math.Max(commandBufferCount, 1)];
            VkSemaphore* signalSems = stackalloc VkSemaphore[Math.Max(signalCount, 1)];
            for (int i = 0; i < waitCount; ++i)
            {
                waitSems[i] = ((VulkanSemaphore)waitSemaphores[i].Semaphore).NativeSemaphore;
                waitStages[i] = VulkanUtility.ConvertToVkPipelineStage(waitSemaphores[i].StageMask, m_PipelineType);
            }

            for (int i = 0; i < commandBufferCount; ++i)
            {
                nativeCommandBuffers[i] = ((VulkanCommandBuffer)commandBuffers[i]).NativeCommandBuffer;
            }

            for (int i = 0; i < signalCount; ++i)
            {
                signalSems[i] = ((VulkanSemaphore)signalSemaphores[i]).NativeSemaphore;
            }

            VkFence fence = default;
            if (descriptor.CompletionFence is VulkanFence vkFence)
            {
                fence = vkFence.NativeFence;
            }

            VkSubmitInfo submitInfo = new VkSubmitInfo()
            {
                sType = VkStructureType.SubmitInfo,
                waitSemaphoreCount = (uint)waitCount,
                pWaitSemaphores = waitCount > 0 ? waitSems : null,
                pWaitDstStageMask = waitCount > 0 ? waitStages : null,
                commandBufferCount = (uint)commandBufferCount,
                pCommandBuffers = commandBufferCount > 0 ? nativeCommandBuffers : null,
                signalSemaphoreCount = (uint)signalCount,
                pSignalSemaphores = signalCount > 0 ? signalSems : null
            };

            ReserveSubmit(in descriptor);
            try
            {
                VulkanUtility.CheckErrors(VulkanNative.vkQueueSubmit(m_NativeQueue, 1, &submitInfo, fence));
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
                    m_VulkanDevice.MarkDeviceLost(deviceLoss);
                }
                throw;
            }
        }

        public override void BindSparse(in RHISparseBindDescriptor descriptor)
        {
            m_VulkanDevice.Capabilities.Memory.SparseBinding.Require(
                "Vulkan queue-ordered sparse texture binding");
            if (!m_VulkanDevice.SupportsSparseQueueFamily(m_QueueFamilyIndex))
            {
                throw new NotSupportedException(
                    "This Vulkan queue family does not support sparse binding.");
            }

            ValidateSparseBind(in descriptor);
            ValidateVulkanSparseBindings(in descriptor);

            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waits.Length; ++i)
            {
                if (waits[i] is not VulkanSemaphore)
                {
                    throw new ArgumentException(
                        $"Wait semaphore at index {i} is not a Vulkan semaphore.",
                        nameof(descriptor));
                }
            }
            for (int i = 0; i < signals.Length; ++i)
            {
                if (signals[i] is not VulkanSemaphore)
                {
                    throw new ArgumentException(
                        $"Signal semaphore at index {i} is not a Vulkan semaphore.",
                        nameof(descriptor));
                }
            }
            if (descriptor.CompletionFence != null &&
                descriptor.CompletionFence is not VulkanFence)
            {
                throw new ArgumentException(
                    "The sparse completion fence is not a Vulkan fence.",
                    nameof(descriptor));
            }

            ReadOnlySpan<RHISparseTextureTileBinding> tileBindings =
                descriptor.TileBindings.Span;
            ReadOnlySpan<RHISparseTextureMipTailBinding> tailBindings =
                descriptor.MipTailBindings.Span;
            VkSemaphore* nativeWaits =
                stackalloc VkSemaphore[Math.Max(waits.Length, 1)];
            VkSemaphore* nativeSignals =
                stackalloc VkSemaphore[Math.Max(signals.Length, 1)];
            VkSparseImageMemoryBind* nativeTileBindings =
                stackalloc VkSparseImageMemoryBind[Math.Max(tileBindings.Length, 1)];
            VkSparseImageMemoryBindInfo* nativeImageBindings =
                stackalloc VkSparseImageMemoryBindInfo[Math.Max(tileBindings.Length, 1)];
            VkSparseMemoryBind* nativeTailBindings =
                stackalloc VkSparseMemoryBind[Math.Max(tailBindings.Length, 1)];
            VkSparseImageOpaqueMemoryBindInfo* nativeOpaqueBindings =
                stackalloc VkSparseImageOpaqueMemoryBindInfo[Math.Max(tailBindings.Length, 1)];

            for (int i = 0; i < waits.Length; ++i)
            {
                nativeWaits[i] = ((VulkanSemaphore)waits[i]).NativeSemaphore;
            }
            for (int i = 0; i < signals.Length; ++i)
            {
                nativeSignals[i] = ((VulkanSemaphore)signals[i]).NativeSemaphore;
            }
            for (int i = 0; i < tileBindings.Length; ++i)
            {
                ref readonly RHISparseTextureTileBinding binding =
                    ref tileBindings[i];
                VulkanTexture texture = (VulkanTexture)binding.Texture;
                RHISparseTextureMemoryRequirements requirements =
                    texture.SparseRequirements!;
                RHITextureDescriptor textureDescriptor = texture.Descriptor;
                uint mipWidth = MipExtent(
                    textureDescriptor.Extent.x,
                    binding.MipLevel);
                uint mipHeight = MipExtent(
                    textureDescriptor.Extent.y,
                    binding.MipLevel);
                uint mipDepth = textureDescriptor.Dimension ==
                    ERHITextureDimension.Texture3D
                        ? MipExtent(
                            textureDescriptor.Extent.z,
                            binding.MipLevel)
                        : 1;
                uint texelOffsetX = checked(
                    binding.TileOffset.x * requirements.TileExtent.x);
                uint texelOffsetY = checked(
                    binding.TileOffset.y * requirements.TileExtent.y);
                uint texelOffsetZ = checked(
                    binding.TileOffset.z * requirements.TileExtent.z);
                uint texelExtentX = Math.Min(
                    checked(binding.TileExtent.x * requirements.TileExtent.x),
                    checked(mipWidth - texelOffsetX));
                uint texelExtentY = Math.Min(
                    checked(binding.TileExtent.y * requirements.TileExtent.y),
                    checked(mipHeight - texelOffsetY));
                uint texelExtentZ = Math.Min(
                    checked(binding.TileExtent.z * requirements.TileExtent.z),
                    checked(mipDepth - texelOffsetZ));
                nativeTileBindings[i] = new VkSparseImageMemoryBind
                {
                    subresource = new VkImageSubresource
                    {
                        aspectMask = VulkanUtility.ConvertToVkImageAspect(
                            binding.Aspect,
                            textureDescriptor.Format),
                        mipLevel = binding.MipLevel,
                        arrayLayer = binding.ArrayLayer,
                    },
                    offset = new VkOffset3D(
                        checked((int)texelOffsetX),
                        checked((int)texelOffsetY),
                        checked((int)texelOffsetZ)),
                    extent = new VkExtent3D(
                        texelExtentX,
                        texelExtentY,
                        texelExtentZ),
                    memory = binding.Operation == ERHISparseBindingOperation.Bind
                        ? ((VulkanHeap)binding.Heap!).NativeMemory
                        : default,
                    memoryOffset = binding.Operation == ERHISparseBindingOperation.Bind
                        ? binding.HeapOffset
                        : 0,
                    flags = VkSparseMemoryBindFlags.None,
                };
                nativeImageBindings[i] = new VkSparseImageMemoryBindInfo
                {
                    image = texture.NativeImage,
                    bindCount = 1,
                    pBinds = &nativeTileBindings[i],
                };
            }
            for (int i = 0; i < tailBindings.Length; ++i)
            {
                ref readonly RHISparseTextureMipTailBinding binding =
                    ref tailBindings[i];
                VulkanTexture texture = (VulkanTexture)binding.Texture;
                RHISparseTextureMipTail tail =
                    texture.SparseRequirements!.GetMipTail(
                        binding.MipTailIndex);
                nativeTailBindings[i] = new VkSparseMemoryBind
                {
                    resourceOffset = tail.VirtualOffsetBytes,
                    size = tail.SizeBytes,
                    memory = binding.Operation == ERHISparseBindingOperation.Bind
                        ? ((VulkanHeap)binding.Heap!).NativeMemory
                        : default,
                    memoryOffset = binding.Operation == ERHISparseBindingOperation.Bind
                        ? binding.HeapOffset
                        : 0,
                    flags = VkSparseMemoryBindFlags.None,
                };
                nativeOpaqueBindings[i] =
                    new VkSparseImageOpaqueMemoryBindInfo
                    {
                        image = texture.NativeImage,
                        bindCount = 1,
                        pBinds = &nativeTailBindings[i],
                    };
            }

            VkFence nativeFence = descriptor.CompletionFence is VulkanFence fence
                ? fence.NativeFence
                : default;
            VkBindSparseInfo bindInfo = new VkBindSparseInfo
            {
                sType = VkStructureType.BindSparseInfo,
                waitSemaphoreCount = (uint)waits.Length,
                pWaitSemaphores = waits.Length == 0 ? null : nativeWaits,
                imageOpaqueBindCount = (uint)tailBindings.Length,
                pImageOpaqueBinds = tailBindings.Length == 0
                    ? null
                    : nativeOpaqueBindings,
                imageBindCount = (uint)tileBindings.Length,
                pImageBinds = tileBindings.Length == 0
                    ? null
                    : nativeImageBindings,
                signalSemaphoreCount = (uint)signals.Length,
                pSignalSemaphores = signals.Length == 0
                    ? null
                    : nativeSignals,
            };

            ReserveSparseBind(in descriptor);
            try
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkQueueBindSparse(
                        m_NativeQueue,
                        1,
                        &bindInfo,
                        nativeFence));
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
                    m_VulkanDevice.MarkDeviceLost(deviceLoss);
                }
                throw;
            }
        }

        private void ValidateVulkanSparseBindings(
            in RHISparseBindDescriptor descriptor)
        {
            ReadOnlySpan<RHISparseTextureTileBinding> tileBindings =
                descriptor.TileBindings.Span;
            for (int i = 0; i < tileBindings.Length; ++i)
            {
                ref readonly RHISparseTextureTileBinding binding =
                    ref tileBindings[i];
                VulkanTexture texture = RequireSparseTexture(
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
                    VulkanHeap heap = RequireSparseHeap(
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
                VulkanTexture texture = RequireSparseTexture(
                    binding.Texture,
                    $"mip-tail binding at index {i}");
                RHISparseTextureMemoryRequirements requirements =
                    texture.SparseRequirements!;
                RHISparseTextureMipTail tail =
                    requirements.GetMipTail(binding.MipTailIndex);
                if (binding.Operation == ERHISparseBindingOperation.Bind)
                {
                    VulkanHeap heap = RequireSparseHeap(
                        binding.Heap,
                        $"mip-tail binding at index {i}");
                    heap.ValidateSparseSpan(
                        binding.HeapOffset,
                        tail.SizeBytes,
                        requirements.HeapCompatibility);
                }
            }
        }

        private VulkanTexture RequireSparseTexture(
            RHITexture texture,
            string argumentDescription)
        {
            if (texture.IsDisposed)
            {
                throw new ObjectDisposedException(texture.GetType().FullName);
            }
            if (texture is not VulkanTexture vulkanTexture ||
                !ReferenceEquals(vulkanTexture.VulkanDevice, m_VulkanDevice))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture belongs to a different backend or device.");
            }
            if (vulkanTexture.AllocationMode !=
                    ERHIResourceAllocationMode.Sparse ||
                vulkanTexture.SparseRequirements == null)
            {
                throw new ArgumentException(
                    $"The {argumentDescription} texture is not a sparse texture.");
            }
            return vulkanTexture;
        }

        private VulkanHeap RequireSparseHeap(
            RHIHeap? heap,
            string argumentDescription)
        {
            if (heap is not VulkanHeap vulkanHeap ||
                !ReferenceEquals(heap.OwnerDevice, m_VulkanDevice))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} heap belongs to a different backend or device.");
            }
            return vulkanHeap;
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

        private static uint MipExtent(uint value, uint mipLevel)
        {
            return Math.Max(1u, value >> checked((int)mipLevel));
        }

        protected override void Release()
        {
            // VkQueue does not need explicit destruction
        }
    }
}


