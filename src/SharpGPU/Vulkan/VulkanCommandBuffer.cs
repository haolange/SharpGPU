using System;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8618
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
            m_RasterEncoder = new VulkanRasterEncoder(this);
            m_RaytracingEncoder = new VulkanRaytracingEncoder(this);
            m_MLEncoder = new VulkanMLEncoder(this);
            m_WorkGraphEncoder = new VulkanWorkGraphEncoder(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            VulkanUtility.CheckErrors(VulkanNative.vkResetCommandBuffer(m_NativeCommandBuffer, 0));
            ReleaseTransientResources();

            VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
            {
                sType = VkStructureType.CommandBufferBeginInfo,
                flags = VkCommandBufferUsageFlags.OneTimeSubmit,
            };

            VulkanUtility.CheckErrors(VulkanNative.vkBeginCommandBuffer(m_NativeCommandBuffer, &beginInfo));
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

        internal void RegisterTransientImageView(VkImageView imageView)
        {
            if (imageView.Equals(default(VkImageView)))
            {
                return;
            }

            m_TransientImageViews ??= new List<VkImageView>(16);
            m_TransientImageViews.Add(imageView);
        }

        private void ReleaseTransientResources()
        {
            VulkanCommandQueue vkQueue = m_CommandQueue as VulkanCommandQueue;

            if (m_TransientImageViews != null && m_TransientImageViews.Count > 0)
            {
                // Dynamic rendering image views are baked into recorded commands;
                // destroy them only after command buffer reset confirms prior execution is complete.
                for (int i = 0; i < m_TransientImageViews.Count; ++i)
                {
                    if (!m_TransientImageViews[i].Equals(default(VkImageView)))
                    {
                        VulkanNative.vkDestroyImageView(vkQueue.VulkanDevice.NativeDevice, m_TransientImageViews[i], null);
                    }
                }
                m_TransientImageViews.Clear();
            }

            if (m_TransientAllocations == null || m_TransientAllocations.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_TransientAllocations.Count; ++i)
            {
                if (m_TransientAllocations[i] != IntPtr.Zero)
                {
                    NativeMemory.Free((void*)m_TransientAllocations[i]);
                }
            }

            m_TransientAllocations.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            m_TransferEncoder.BeginPass(descriptor);
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
            m_ComputeEncoder.BeginPass(descriptor);
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
            m_RaytracingEncoder.BeginPass(descriptor);
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
            m_RasterEncoder.BeginPass(descriptor);
            return m_RasterEncoder;
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
            VulkanUtility.CheckErrors(VulkanNative.vkEndCommandBuffer(m_NativeCommandBuffer));
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
            m_WorkGraphEncoder.BeginPass(descriptor);
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
            VulkanCommandQueue vkQueue = m_CommandQueue as VulkanCommandQueue;
            VulkanNative.vkDestroyCommandPool(vkQueue.VulkanDevice.NativeDevice, m_NativeCommandPool, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
