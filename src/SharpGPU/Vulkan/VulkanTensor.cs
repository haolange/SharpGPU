using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanTensor : RHITensor
    {
        public VkBuffer NativeBuffer => m_NativeBuffer;
        public VkDeviceMemory NativeMemory => m_NativeMemory;
        internal ulong ByteLength => m_ByteLength;
        internal ulong BackingBufferOffset => m_BackingBufferOffset;

        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private readonly ulong m_ByteLength;
        private readonly ulong m_BackingBufferOffset;
        private readonly bool m_OwnsBackingBuffer;

        public VulkanTensor(VulkanDevice device, in RHIMLTensorDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_BackingBufferOffset = descriptor.BackingBufferOffset;
            m_ByteLength = RHIMLHelpers.CalculateMinimumByteLength(descriptor);

            if (descriptor.BackingBuffer != null)
            {
                VulkanBuffer backingBuffer = descriptor.BackingBuffer as VulkanBuffer
                    ?? throw new InvalidOperationException($"Vulkan tensor requires a {nameof(VulkanBuffer)} backing buffer when BackingBuffer is supplied.");

                ulong backingByteLength = checked((ulong)backingBuffer.Descriptor.ByteSize);
                if (m_BackingBufferOffset + m_ByteLength > backingByteLength)
                {
                    throw new InvalidOperationException($"Vulkan tensor range [{m_BackingBufferOffset}, {m_BackingBufferOffset + m_ByteLength}) exceeds backing buffer size {backingByteLength}.");
                }

                m_NativeBuffer = backingBuffer.NativeBuffer;
                m_NativeMemory = backingBuffer.NativeMemory;
                m_OwnsBackingBuffer = false;
                return;
            }

            m_OwnsBackingBuffer = true;

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.BufferCreateInfo,
                size = m_ByteLength,
                usage = VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst,
                sharingMode = VkSharingMode.Exclusive,
            };

            fixed (VkBuffer* bufferPtr = &m_NativeBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetBufferMemoryRequirements(device.NativeDevice, m_NativeBuffer, &memRequirements);

            VkMemoryPropertyFlags memProps = VulkanUtility.ConvertToVkMemoryProperty(descriptor.StorageMode);
            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, memProps);

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.MemoryAllocateInfo,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, m_NativeBuffer, m_NativeMemory, 0));
        }

        protected override void Release()
        {
            if (!m_OwnsBackingBuffer)
            {
                return;
            }

            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
        }
    }
}


