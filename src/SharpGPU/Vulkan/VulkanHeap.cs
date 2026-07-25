using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanHeap : RHIHeap
    {
        public VkDeviceMemory NativeMemory
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeMemory;
            }
        }
        internal uint MemoryTypeIndex { get; }

        private VulkanDevice m_VulkanDevice;
        private VkDeviceMemory m_NativeMemory;
        private readonly object m_MapSync = new object();
        private void* m_MappedBaseAddress;
        private int m_MapReferenceCount;

        public VulkanHeap(VulkanDevice device, in RHIHeapDescription descriptor)
            : base(device, descriptor, SelectCompatibilityBit(device, descriptor))
        {
            m_VulkanDevice = device;

            uint memTypeIndex = 0;
            ulong compatibilityBit = CompatibilityBit;
            while ((compatibilityBit >>= 1) != 0)
            {
                ++memTypeIndex;
            }
            MemoryTypeIndex = memTypeIndex;

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.MemoryAllocateInfo,
                allocationSize = descriptor.Size,
                memoryTypeIndex = memTypeIndex,
            };
            VkMemoryAllocateFlagsInfo allocateFlags = default;
            if (descriptor.Compatibility.NativeAllocationFlags != 0)
            {
                allocateFlags.sType = VkStructureType.MemoryAllocateFlagsInfo;
                allocateFlags.flags =
                    (VkMemoryAllocateFlags)descriptor.Compatibility.NativeAllocationFlags;
                allocInfo.pNext = &allocateFlags;
            }

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }
        }

        internal void* MapShared(bool invalidate)
        {
            ThrowIfDisposed();
            lock (m_MapSync)
            {
                if (m_MapReferenceCount == 0)
                {
                    void* mappedAddress;
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkMapMemory(
                            m_VulkanDevice.NativeDevice,
                            m_NativeMemory,
                            0,
                            ulong.MaxValue,
                            0,
                            &mappedAddress));
                    m_MappedBaseAddress = mappedAddress;
                }

                if (invalidate)
                {
                    VkMappedMemoryRange range =
                        new VkMappedMemoryRange
                        {
                            sType =
                                VkStructureType.MappedMemoryRange,
                            memory = m_NativeMemory,
                            offset = 0,
                            size = ulong.MaxValue,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkInvalidateMappedMemoryRanges(
                            m_VulkanDevice.NativeDevice,
                            1,
                            &range));
                }

                m_MapReferenceCount = checked(m_MapReferenceCount + 1);
                return m_MappedBaseAddress;
            }
        }

        internal void FlushAndUnmapShared(bool flush)
        {
            ThrowIfDisposed();
            lock (m_MapSync)
            {
                if (m_MapReferenceCount <= 0 ||
                    m_MappedBaseAddress == null)
                {
                    throw new InvalidOperationException(
                        "The Vulkan heap has no active shared mapping.");
                }

                if (flush)
                {
                    VkMappedMemoryRange range =
                        new VkMappedMemoryRange
                        {
                            sType =
                                VkStructureType.MappedMemoryRange,
                            memory = m_NativeMemory,
                            offset = 0,
                            size = ulong.MaxValue,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkFlushMappedMemoryRanges(
                            m_VulkanDevice.NativeDevice,
                            1,
                            &range));
                }

                --m_MapReferenceCount;
                if (m_MapReferenceCount == 0)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }
            }
        }

        internal void ReleaseSharedMappingAfterResourceDispose()
        {
            lock (m_MapSync)
            {
                if (m_MapReferenceCount <= 0)
                {
                    return;
                }

                --m_MapReferenceCount;
                if (m_MapReferenceCount == 0 &&
                    m_MappedBaseAddress != null &&
                    !IsDisposed)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }
            }
        }

        private static ulong SelectCompatibilityBit(
            VulkanDevice device,
            in RHIHeapDescription descriptor)
        {
            uint compatibleTypes = checked((uint)descriptor.Compatibility.CompatibilityMask);
            VkMemoryPropertyFlags properties =
                VulkanUtility.ConvertToVkMemoryProperty(descriptor.StorageMode);
            uint memoryTypeIndex = VulkanUtility.FindMemoryType(
                device.MemoryProperties,
                compatibleTypes,
                properties);
            return 1UL << checked((int)memoryTypeIndex);
        }

        protected override void Release()
        {
            lock (m_MapSync)
            {
                if (m_MappedBaseAddress != null)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }

                m_MapReferenceCount = 0;
            }

            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            m_NativeMemory = default;
        }
    }
}


