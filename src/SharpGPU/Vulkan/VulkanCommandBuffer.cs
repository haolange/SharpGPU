using System;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
    internal unsafe class VulkanCommandBuffer : RHICommandBuffer
    {
        public VkCommandBuffer NativeCommandBuffer
        {
            get
            {
                return m_NativeCommandBuffer;
            }
        }
        public VkCommandPool NativeCommandPool
        {
            get
            {
                return m_NativeCommandPool;
            }
        }

        private VulkanTransferEncoder m_TransferEncoder;
        private VulkanComputeEncoder m_ComputeEncoder;
        private VulkanRasterEncoder m_RasterEncoder;
        private VulkanRaytracingEncoder m_RaytracingEncoder;
        private VulkanMLEncoder m_MLEncoder;
        private VulkanWorkGraphEncoder m_WorkGraphEncoder;
        private VkCommandPool m_NativeCommandPool;
        private VkCommandBuffer m_NativeCommandBuffer;
        private List<IntPtr>? m_TransientAllocations;
        private List<VkImageView>? m_TransientImageViews;
        private List<VkFramebuffer>? m_TransientFramebuffers;
        private List<VkRenderPass>? m_TransientRenderPasses;
        private List<VulkanDescriptorSetLease>?
            m_TransientDescriptorSetLeases;
        private List<VulkanRasterNativeVariant>?
            m_TransientRasterPipelines;
        private readonly VulkanCommandBufferImageLayoutOverlay
            m_ImageLayoutOverlay = new();

        public VulkanCommandBuffer(VulkanCommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;

            VulkanCommandQueue vkQueue = commandQueue;

            // Create command pool
            VkCommandPoolCreateInfo poolInfo = new VkCommandPoolCreateInfo()
            {
                sType = VkStructureType.CommandPoolCreateInfo,
                flags = VkCommandPoolCreateFlags.ResetCommandBuffer,
                queueFamilyIndex = vkQueue.QueueFamilyIndex,
            };

            fixed (VkCommandPool* poolPtr = &m_NativeCommandPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateCommandPool(vkQueue.VulkanDevice.NativeDevice, &poolInfo, null, poolPtr));
            }

            // Allocate command buffer
            VkCommandBufferAllocateInfo allocInfo = new VkCommandBufferAllocateInfo()
            {
                sType = VkStructureType.CommandBufferAllocateInfo,
                commandPool = m_NativeCommandPool,
                level = VkCommandBufferLevel.Primary,
                commandBufferCount = 1,
            };

            fixed (VkCommandBuffer* cmdBufPtr = &m_NativeCommandBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateCommandBuffers(vkQueue.VulkanDevice.NativeDevice, &allocInfo, cmdBufPtr));
            }

            m_TransferEncoder = new VulkanTransferEncoder(this);
            m_ComputeEncoder = new VulkanComputeEncoder(this);
            m_RasterEncoder = new VulkanRasterSubpassEncoder(this);
            m_RaytracingEncoder = new VulkanRaytracingEncoder(this);
            m_MLEncoder = new VulkanMLEncoder(this);
            m_WorkGraphEncoder = new VulkanWorkGraphEncoder(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            ValidateCanBegin();
            m_ImageLayoutOverlay.Clear();
            VulkanUtility.CheckErrors(VulkanNative.vkResetCommandBuffer(m_NativeCommandBuffer, 0));
            ReleaseTransientResources();

            VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
            {
                sType = VkStructureType.CommandBufferBeginInfo,
                flags = VkCommandBufferUsageFlags.OneTimeSubmit,
            };

            VulkanUtility.CheckErrors(VulkanNative.vkBeginCommandBuffer(m_NativeCommandBuffer, &beginInfo));
            MarkBeginSucceeded();
        }

        internal int CaptureImageLayoutCheckpoint() =>
            m_ImageLayoutOverlay.CaptureCheckpoint();

        internal void ValidateDeclaredImageLayout(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout declaredLayout)
        {
            ArgumentNullException.ThrowIfNull(texture);
            RHITextureDescriptor textureDescriptor =
                texture.Descriptor;
            RHITextureSubresourceRange normalizedRange =
                VulkanTextureSubresourceRangeUtility.Normalize(
                    in textureDescriptor,
                    in range);
            m_ImageLayoutOverlay.ValidateDeclaredLayout(
                texture.NativeImage,
                in normalizedRange,
                declaredLayout);
        }

        internal void RequireKnownImageLayout(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout requiredLayout,
            string operation)
        {
            ArgumentNullException.ThrowIfNull(texture);
            RHITextureDescriptor textureDescriptor =
                texture.Descriptor;
            RHITextureSubresourceRange normalizedRange =
                VulkanTextureSubresourceRangeUtility.Normalize(
                    in textureDescriptor,
                    in range);
            m_ImageLayoutOverlay.RequireKnownLayout(
                texture.NativeImage,
                in normalizedRange,
                requiredLayout,
                operation);
        }

        internal void SetKnownImageLayout(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout layout)
        {
            ArgumentNullException.ThrowIfNull(texture);
            RHITextureDescriptor textureDescriptor =
                texture.Descriptor;
            RHITextureSubresourceRange normalizedRange =
                VulkanTextureSubresourceRangeUtility.Normalize(
                    in textureDescriptor,
                    in range);
            m_ImageLayoutOverlay.SetLayout(
                texture.NativeImage,
                in normalizedRange,
                layout);
        }

        internal void RollbackImageLayouts(int checkpoint)
        {
            m_ImageLayoutOverlay.Rollback(checkpoint);
        }
        private void AbortRecordingAfterRasterBeginFailure(
            in VulkanTransientResourceCheckpoint transientCheckpoint,
            int layoutCheckpoint)
        {
            _ = transientCheckpoint;
            _ = layoutCheckpoint;
            try
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkResetCommandBuffer(
                        m_NativeCommandBuffer,
                        0));
            }
            finally
            {
                try
                {
                    ReleaseTransientResources();
                }
                finally
                {
                    m_ImageLayoutOverlay.Clear();
                    ((VulkanRasterSubpassEncoder)m_RasterEncoder)
                        .AbortPassState();
                    MarkRecordingInvalid();
                }
            }
        }
        private protected override void OnSubmitted()
        {
            m_ImageLayoutOverlay.Clear();
        }

        internal VulkanTransientResourceCheckpoint
            CaptureTransientResourceCheckpoint() =>
            new(
                m_TransientAllocations?.Count ?? 0,
                m_TransientImageViews?.Count ?? 0,
                m_TransientFramebuffers?.Count ?? 0,
                m_TransientRenderPasses?.Count ?? 0,
                m_TransientDescriptorSetLeases?.Count ?? 0,
                m_TransientRasterPipelines?.Count ?? 0);

        internal void RollbackTransientResources(
            in VulkanTransientResourceCheckpoint checkpoint)
        {
            VulkanCommandQueue vkQueue =
                VulkanEncoderGuards.RequireCommandQueue(m_CommandQueue);
            ValidateCheckpoint(
                m_TransientDescriptorSetLeases?.Count ?? 0,
                checkpoint.DescriptorSetLeaseCount,
                nameof(checkpoint.DescriptorSetLeaseCount));
            ValidateCheckpoint(
                m_TransientRasterPipelines?.Count ?? 0,
                checkpoint.RasterPipelineCount,
                nameof(checkpoint.RasterPipelineCount));
            ValidateCheckpoint(
                m_TransientFramebuffers?.Count ?? 0,
                checkpoint.FramebufferCount,
                nameof(checkpoint.FramebufferCount));
            ValidateCheckpoint(
                m_TransientRenderPasses?.Count ?? 0,
                checkpoint.RenderPassCount,
                nameof(checkpoint.RenderPassCount));
            ValidateCheckpoint(
                m_TransientImageViews?.Count ?? 0,
                checkpoint.ImageViewCount,
                nameof(checkpoint.ImageViewCount));
            ValidateCheckpoint(
                m_TransientAllocations?.Count ?? 0,
                checkpoint.AllocationCount,
                nameof(checkpoint.AllocationCount));

            if (m_TransientDescriptorSetLeases != null)
            {
                for (int index =
                         m_TransientDescriptorSetLeases.Count - 1;
                     index >= checkpoint.DescriptorSetLeaseCount;
                     --index)
                {
                    vkQueue.VulkanDevice.DescriptorPoolAllocator
                        .Free(m_TransientDescriptorSetLeases[index]);
                }
                RemoveTail(
                    m_TransientDescriptorSetLeases,
                    checkpoint.DescriptorSetLeaseCount);
            }

            if (m_TransientRasterPipelines != null)
            {
                for (int index =
                         m_TransientRasterPipelines.Count - 1;
                     index >= checkpoint.RasterPipelineCount;
                     --index)
                {
                    m_TransientRasterPipelines[index].Dispose();
                }
                RemoveTail(
                    m_TransientRasterPipelines,
                    checkpoint.RasterPipelineCount);
            }

            if (m_TransientFramebuffers != null)
            {
                for (int index =
                         m_TransientFramebuffers.Count - 1;
                     index >= checkpoint.FramebufferCount;
                     --index)
                {
                    VulkanNative.vkDestroyFramebuffer(
                        vkQueue.VulkanDevice.NativeDevice,
                        m_TransientFramebuffers[index],
                        null);
                }
                RemoveTail(
                    m_TransientFramebuffers,
                    checkpoint.FramebufferCount);
            }

            if (m_TransientRenderPasses != null)
            {
                for (int index =
                         m_TransientRenderPasses.Count - 1;
                     index >= checkpoint.RenderPassCount;
                     --index)
                {
                    VulkanNative.vkDestroyRenderPass(
                        vkQueue.VulkanDevice.NativeDevice,
                        m_TransientRenderPasses[index],
                        null);
                }
                RemoveTail(
                    m_TransientRenderPasses,
                    checkpoint.RenderPassCount);
            }

            if (m_TransientImageViews != null)
            {
                for (int index =
                         m_TransientImageViews.Count - 1;
                     index >= checkpoint.ImageViewCount;
                     --index)
                {
                    VulkanNative.vkDestroyImageView(
                        vkQueue.VulkanDevice.NativeDevice,
                        m_TransientImageViews[index],
                        null);
                }
                RemoveTail(
                    m_TransientImageViews,
                    checkpoint.ImageViewCount);
            }

            if (m_TransientAllocations != null)
            {
                for (int index =
                         m_TransientAllocations.Count - 1;
                     index >= checkpoint.AllocationCount;
                     --index)
                {
                    NativeMemory.Free(
                        (void*)m_TransientAllocations[index]);
                }
                RemoveTail(
                    m_TransientAllocations,
                    checkpoint.AllocationCount);
            }
        }

        internal void RegisterTransientAllocation(void* ptr)
        {
            if (ptr == null)
            {
                return;
            }
            m_TransientAllocations ??= new List<IntPtr>(16);
            m_TransientAllocations.Add((IntPtr)ptr);
        }

        internal void RegisterTransientImageView(
            VkImageView imageView)
        {
            if (imageView.Handle == 0)
            {
                return;
            }
            m_TransientImageViews ??=
                new List<VkImageView>(16);
            m_TransientImageViews.Add(imageView);
        }

        internal void RegisterTransientFramebuffer(
            VkFramebuffer framebuffer)
        {
            if (framebuffer.Handle == 0)
            {
                return;
            }
            m_TransientFramebuffers ??=
                new List<VkFramebuffer>(4);
            m_TransientFramebuffers.Add(framebuffer);
        }

        internal void RegisterTransientRenderPass(
            VkRenderPass renderPass)
        {
            if (renderPass.Handle == 0)
            {
                return;
            }
            m_TransientRenderPasses ??=
                new List<VkRenderPass>(4);
            m_TransientRenderPasses.Add(renderPass);
        }

        internal void RegisterTransientRasterPipeline(
            VulkanRasterNativeVariant pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            try
            {
                m_TransientRasterPipelines ??=
                    new List<VulkanRasterNativeVariant>(4);
                m_TransientRasterPipelines.Add(pipeline);
            }
            catch
            {
                pipeline.Dispose();
                throw;
            }
        }

        internal void RegisterTransientDescriptorSetLease(
            VulkanDescriptorSetLease lease)
        {
            if (lease.Pool.Handle == 0 ||
                lease.Set.Handle == 0)
            {
                throw new ArgumentException(
                    "A complete native descriptor-set lease is required.",
                    nameof(lease));
            }
            m_TransientDescriptorSetLeases ??=
                new List<VulkanDescriptorSetLease>(8);
            m_TransientDescriptorSetLeases.Add(lease);
        }

        private void ReleaseTransientResources()
        {
            RollbackTransientResources(
                VulkanTransientResourceCheckpoint.Empty);
        }

        private static void ValidateCheckpoint(
            int currentCount,
            int checkpointCount,
            string checkpointMember)
        {
            if ((uint)checkpointCount > (uint)currentCount)
            {
                throw new InvalidOperationException(
                    $"Transient checkpoint member {checkpointMember} " +
                    $"{checkpointCount} exceeds current count " +
                    $"{currentCount}.");
            }
        }

        private static void RemoveTail<T>(
            List<T> values,
            int retainedCount)
        {
            int removeCount = values.Count - retainedCount;
            if (removeCount != 0)
            {
                values.RemoveRange(retainedCount, removeCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Transfer);
            m_TransferEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Transfer);
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndTransferPass()
        {
            m_TransferEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Compute);
            m_ComputeEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Compute);
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndComputePass()
        {
            m_ComputeEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.RayTracing);
            m_RaytracingEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.RayTracing);
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRaytracingPass()
        {
            m_RaytracingEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.Raster);
            VulkanTransientResourceCheckpoint transientCheckpoint =
                CaptureTransientResourceCheckpoint();
            int layoutCheckpoint =
                CaptureImageLayoutCheckpoint();
            try
            {
                m_RasterEncoder.BeginPass(descriptor);
                MarkEncoderBeginSucceeded(
                    ERHICommandEncoderKind.Raster);
                return m_RasterEncoder;
            }
            catch (Exception beginFailure)
            {
                try
                {
                    AbortRecordingAfterRasterBeginFailure(
                        in transientCheckpoint,
                        layoutCheckpoint);
                }
                catch (Exception abortFailure)
                {
                    throw new AggregateException(
                        "Vulkan raster-pass begin failed and " +
                        "native command-buffer rollback also failed.",
                        beginFailure,
                        abortFailure);
                }
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRasterPass()
        {
            m_RasterEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            throw new NotSupportedException("Vulkan ML is not supported in SharpGPU v1.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndMLPass()
        {
            throw new NotSupportedException("Vulkan ML is not supported in SharpGPU v1.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void End()
        {
            ValidateCanEnd();
            VulkanUtility.CheckErrors(VulkanNative.vkEndCommandBuffer(m_NativeCommandBuffer));
            MarkEndSucceeded();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder GetTransferEncoder()
        {
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder GetComputeEncoder()
        {
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder GetRaytracingEncoder()
        {
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder GetRasterEncoder()
        {
            return m_RasterEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder GetMLEncoder()
        {
            return m_MLEncoder;
        }

        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            ValidateCanBeginEncoder(ERHICommandEncoderKind.WorkGraph);
            m_WorkGraphEncoder.BeginPass(descriptor);
            MarkEncoderBeginSucceeded(ERHICommandEncoderKind.WorkGraph);
            return m_WorkGraphEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndWorkGraphPass()
        {
            m_WorkGraphEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIWorkGraphEncoder GetWorkGraphEncoder()
        {
            return m_WorkGraphEncoder;
        }

        protected override void Release()
        {
            ReleaseTransientResources();
            VulkanCommandQueue vkQueue = VulkanEncoderGuards.RequireCommandQueue(m_CommandQueue);
            VulkanNative.vkDestroyCommandPool(vkQueue.VulkanDevice.NativeDevice, m_NativeCommandPool, null);
        }
    }
}
