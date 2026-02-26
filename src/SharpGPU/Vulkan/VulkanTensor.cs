using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanTensor : RHITensor
    {
        public VkBuffer NativeBuffer => m_NativeBuffer;
        public VkDeviceMemory NativeMemory => m_NativeMemory;

        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;

        public VulkanTensor(VulkanDevice device, in RHIMLTensorDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            // Calculate tensor size
            ulong elementSize = GetElementSize(descriptor.DataType);
            ulong totalElements = 1;
            for (int i = 0; i < descriptor.Dimensions.Length; ++i)
            {
                totalElements *= descriptor.Dimensions.Span[i];
            }
            ulong bufferSize = totalElements * elementSize;

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = bufferSize,
                usage = VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
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
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, m_NativeBuffer, m_NativeMemory, 0));
        }

        private static ulong GetElementSize(ERHIMLDataType dataType)
        {
            switch (dataType)
            {
                case ERHIMLDataType.Float32:
                case ERHIMLDataType.Int32:
                case ERHIMLDataType.UInt32:
                    return 4;
                case ERHIMLDataType.Float16:
                case ERHIMLDataType.BFloat16:
                case ERHIMLDataType.Int16:
                case ERHIMLDataType.UInt16:
                    return 2;
                case ERHIMLDataType.Int8:
                case ERHIMLDataType.UInt8:
                    return 1;
                default:
                    return 4;
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
        }
    }
#pragma warning restore CS8618
}
