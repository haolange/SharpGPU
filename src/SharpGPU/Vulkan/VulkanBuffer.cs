using System;
using Vortice.Vulkan;
using System.Diagnostics;

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
        private void* m_MappedBaseAddress;
        private bool m_IsMapped;

        public VulkanBuffer(VulkanDevice device, in RHIBufferDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.BufferCreateInfo,
                size = (ulong)descriptor.ByteSize,
                usage = VulkanUtility.ConvertToVkBufferUsage(descriptor.UsageFlag),
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
            VkMemoryAllocateFlagsInfo allocFlagsInfo = default;
            if ((bufferInfo.usage & VkBufferUsageFlags.ShaderDeviceAddress) != 0)
            {
                allocFlagsInfo.sType = VkStructureType.MemoryAllocateFlagsInfo;
                allocFlagsInfo.flags = VkMemoryAllocateFlags.DeviceAddress;
                allocInfo.pNext = &allocFlagsInfo;
            }

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
            if (!m_IsMapped)
            {
                void* data;
                VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, 0, ulong.MaxValue, 0, &data));
                m_MappedBaseAddress = data;
                m_IsMapped = true;
            }

            byte* mappedAddress = (byte*)m_MappedBaseAddress + readBegin;
            return new IntPtr(mappedAddress);
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
                    sType = VkStructureType.MappedMemoryRange,
                    memory = m_NativeMemory,
                    // Flush whole allocation to satisfy non-coherent atom-size alignment constraints.
                    offset = 0,
                    size = ulong.MaxValue,
                };
                VulkanNative.vkFlushMappedMemoryRanges(m_VulkanDevice.NativeDevice, 1, &range);
            }

            if (m_IsMapped)
            {
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_NativeMemory);
                m_MappedBaseAddress = null;
                m_IsMapped = false;
            }
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


