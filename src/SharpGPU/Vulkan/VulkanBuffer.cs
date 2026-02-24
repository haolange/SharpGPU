using System;
using System.Diagnostics;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanBuffer : RHIBuffer
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                return m_VulkanDevice;
            }
        }
        public VkBuffer NativeBuffer
        {
            get
            {
                return m_NativeBuffer;
            }
        }
        public VkDeviceMemory NativeMemory
        {
            get
            {
                return m_NativeMemory;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;

        public VulkanBuffer(VulkanDevice device, in RHIBufferDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = (ulong)descriptor.ByteSize,
                usage = VulkanUtility.ConvertToVkBufferUsage(descriptor.UsageFlag),
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

        internal VulkanBuffer(VulkanDevice device, in RHIBufferDescriptor descriptor, VkBuffer existingBuffer, VkDeviceMemory existingMemory)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = existingBuffer;
            m_NativeMemory = existingMemory;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != ERHIStorageMode.GPULocal, "StorageMode is GPULocal it can't use Map()");
#endif
            void* data;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, readBegin, readEnd - readBegin, 0, &data));
            return new IntPtr(data);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != ERHIStorageMode.GPULocal, "StorageMode is GPULocal it can't use UnMap()");
#endif
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPUUpload || m_Descriptor.StorageMode == ERHIStorageMode.HostUpload)
            {
                VkMappedMemoryRange range = new VkMappedMemoryRange()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE,
                    memory = m_NativeMemory,
                    offset = writeBegin,
                    size = writeEnd - writeBegin,
                };
                VulkanNative.vkFlushMappedMemoryRanges(m_VulkanDevice.NativeDevice, 1, &range);
            }
            VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_NativeMemory);
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return new VulkanBufferView(this, descriptor);
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
        }
    }
#pragma warning restore CS8618
}
