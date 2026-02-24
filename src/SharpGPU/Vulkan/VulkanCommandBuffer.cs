using System;
using System.Runtime.CompilerServices;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
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
        private VkCommandPool m_NativeCommandPool;
        private VkCommandBuffer m_NativeCommandBuffer;

        public VulkanCommandBuffer(VulkanCommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;

            VulkanCommandQueue vkQueue = commandQueue;

            // Create command pool
            VkCommandPoolCreateInfo poolInfo = new VkCommandPoolCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                flags = VkCommandPoolCreateFlags.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = vkQueue.QueueFamilyIndex,
            };

            fixed (VkCommandPool* poolPtr = &m_NativeCommandPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateCommandPool(vkQueue.VulkanDevice.NativeDevice, &poolInfo, null, poolPtr));
            }

            // Allocate command buffer
            VkCommandBufferAllocateInfo allocInfo = new VkCommandBufferAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                commandPool = m_NativeCommandPool,
                level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            VulkanUtility.CheckErrors(VulkanNative.vkResetCommandBuffer(m_NativeCommandBuffer, 0));

            VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                flags = VkCommandBufferUsageFlags.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
            };

            VulkanUtility.CheckErrors(VulkanNative.vkBeginCommandBuffer(m_NativeCommandBuffer, &beginInfo));
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
            m_MLEncoder.BeginPass(descriptor);
            return m_MLEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndMLPass()
        {
            m_MLEncoder.EndPass();
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

        protected override void Release()
        {
            VulkanCommandQueue vkQueue = m_CommandQueue as VulkanCommandQueue;
            VulkanNative.vkDestroyCommandPool(vkQueue.VulkanDevice.NativeDevice, m_NativeCommandPool, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
