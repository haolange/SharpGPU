using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        public VkBuffer NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeArgumentBuffer;
        private VkDeviceMemory m_NativeArgumentMemory;

        public VulkanComputeIndirectCommandBuffer(VulkanDevice device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_VulkanDevice = device;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ulong bufferSize = m_MaxCommandCount * 12; // sizeof(VkDispatchIndirectCommand) = 3 * uint = 12

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = bufferSize,
                usage = VkBufferUsageFlags.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
            };

            fixed (VkBuffer* bufferPtr = &m_NativeArgumentBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetBufferMemoryRequirements(device.NativeDevice, m_NativeArgumentBuffer, &memRequirements);

            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeArgumentMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, m_NativeArgumentBuffer, m_NativeArgumentMemory, 0));
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeArgumentBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeArgumentMemory, null);
        }
    }

    internal unsafe class VulkanRayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        public VkBuffer NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeArgumentBuffer;
        private VkDeviceMemory m_NativeArgumentMemory;

        public VulkanRayTracingIndirectCommandBuffer(VulkanDevice device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_VulkanDevice = device;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ulong bufferSize = m_MaxCommandCount * 12; // VkTraceRaysIndirectCommandKHR = 3 * uint

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = bufferSize,
                usage = VkBufferUsageFlags.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
            };

            fixed (VkBuffer* bufferPtr = &m_NativeArgumentBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetBufferMemoryRequirements(device.NativeDevice, m_NativeArgumentBuffer, &memRequirements);

            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeArgumentMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, m_NativeArgumentBuffer, m_NativeArgumentMemory, 0));
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeArgumentBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeArgumentMemory, null);
        }
    }

    internal unsafe class VulkanRasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        public VkBuffer NativeArgumentBuffer => m_NativeArgumentBuffer;
        public uint MaxCommandCount => m_MaxCommandCount;

        private uint m_MaxCommandCount;
        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeArgumentBuffer;
        private VkDeviceMemory m_NativeArgumentMemory;

        public VulkanRasterIndirectCommandBuffer(VulkanDevice device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_VulkanDevice = device;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            ulong bufferSize = m_MaxCommandCount * 20; // sizeof(VkDrawIndexedIndirectCommand) = 5 * uint = 20

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = bufferSize,
                usage = VkBufferUsageFlags.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT,
                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
            };

            fixed (VkBuffer* bufferPtr = &m_NativeArgumentBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetBufferMemoryRequirements(device.NativeDevice, m_NativeArgumentBuffer, &memRequirements);

            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeArgumentMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, m_NativeArgumentBuffer, m_NativeArgumentMemory, 0));
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeArgumentBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeArgumentMemory, null);
        }
    }
#pragma warning restore CS8618
}
