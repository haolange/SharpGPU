using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanMLPipeline : RHIMLPipeline
    {
        internal string Name => m_Name;

        private readonly string m_Name;
        private readonly VulkanDevice m_VulkanDevice;

        public VulkanMLPipeline(VulkanDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Name = descriptor.Name;

            // Vulkan does not have native ML pipeline support.
            // Estimate intermediates heap size from input tensor dimensions
            // so that the RHI abstraction reports consistent sizes across backends.
            ulong intermediatesSize = 0;
            for (int i = 0; i < descriptor.InputTensors.Length; ++i)
            {
                ref readonly RHIMLTensorDescriptor td = ref descriptor.InputTensors.Span[i];
                ulong tensorSize = 1;
                Span<uint> dims = td.Dimensions.Span;
                for (int d = 0; d < dims.Length; ++d)
                {
                    tensorSize *= dims[d];
                }
                tensorSize *= GetElementSize(td.DataType);
                intermediatesSize += tensorSize;
            }
            m_IntermediatesHeapSize = intermediatesSize;
        }

        private static ulong GetElementSize(ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 or ERHIMLDataType.Int32 or ERHIMLDataType.UInt32 => 4,
                ERHIMLDataType.Float16 or ERHIMLDataType.BFloat16 or ERHIMLDataType.Int16 or ERHIMLDataType.UInt16 => 2,
                ERHIMLDataType.Int8 or ERHIMLDataType.UInt8 => 1,
                _ => 4,
            };
        }

        protected override void Release()
        {
        }
    }

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
