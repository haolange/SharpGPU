using System;
using Vortice.Vulkan;
using System.Diagnostics;

namespace SharpGPU
{
    internal unsafe class VulkanBuffer : RHIBuffer
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                ThrowIfDisposed(); return m_VulkanDevice;
            }
        }
        public VkBuffer NativeBuffer
        {
            get
            {
                ThrowIfDisposed(); return m_NativeBuffer;
            }
        }
        public VkDeviceMemory NativeMemory
        {
            get
            {
                ThrowIfDisposed(); return m_NativeMemory;
            }
        }
        public override ulong GpuVirtualAddress
        {
            get
            {
                ThrowIfDisposed();
                m_VulkanDevice.Capabilities.Memory.GpuVirtualAddress.Require(
                    "Vulkan buffer device address");
                return GetNativeDeviceAddress();
            }
        }

        internal ulong GetNativeDeviceAddress()
        {
            ThrowIfDisposed();
            VkBufferDeviceAddressInfo info = new()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = m_NativeBuffer,
            };
            return VulkanNative.vkGetBufferDeviceAddress(
                m_VulkanDevice.NativeDevice,
                &info);
        }

        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private void* m_MappedBaseAddress;
        private ulong m_MemoryOffset;
        private ulong m_MemorySize;
        private bool m_IsMapped;
        private bool m_OwnsMemory = true;
        private RHIHeapPlacement? m_Placement;
        private VulkanHeap? m_PlacedHeap;

        public VulkanBuffer(VulkanDevice device, in RHIBufferDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            VkBufferCreateInfo bufferInfo =
                VulkanMemoryUtility.BuildBufferCreateInfo(descriptor);
            ApplyOpacityMicromapUsage(device, ref bufferInfo, in descriptor);

            try
            {
                fixed (VkBuffer* bufferPtr = &m_NativeBuffer)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreateBuffer(
                            device.NativeDevice,
                            &bufferInfo,
                            null,
                            bufferPtr));
                }

                VkMemoryRequirements memRequirements;
                VulkanNative.vkGetBufferMemoryRequirements(
                    device.NativeDevice,
                    m_NativeBuffer,
                    &memRequirements);

                VkMemoryPropertyFlags memProps =
                    VulkanUtility.ConvertToVkMemoryProperty(
                        descriptor.StorageMode);
                uint memTypeIndex = VulkanUtility.FindMemoryType(
                    device.MemoryProperties,
                    memRequirements.memoryTypeBits,
                    memProps);
                m_MemoryOffset = 0;
                m_MemorySize = memRequirements.size;

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
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkAllocateMemory(
                            device.NativeDevice,
                            &allocInfo,
                            null,
                            memPtr));
                }

                VulkanUtility.CheckErrors(
                    VulkanNative.vkBindBufferMemory(
                        device.NativeDevice,
                        m_NativeBuffer,
                        m_NativeMemory,
                        0));
            }
            catch
            {
                ReleaseNativeAllocationAfterConstructionFailure();
                throw;
            }
        }

        internal VulkanBuffer(
            VulkanDevice device,
            in RHIBufferDescriptor descriptor,
            VulkanHeap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;
            m_NativeMemory = heap.NativeMemory;
            m_MemoryOffset = heapOffset;
            m_MemorySize = heap.Descriptor.Size;
            m_OwnsMemory = false;
            m_PlacedHeap = heap;

            VkBufferCreateInfo bufferInfo =
                VulkanMemoryUtility.BuildBufferCreateInfo(descriptor);
            ApplyOpacityMicromapUsage(device, ref bufferInfo, in descriptor);
            fixed (VkBuffer* bufferPtr = &m_NativeBuffer)
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            try
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkBindBufferMemory(
                        device.NativeDevice,
                        m_NativeBuffer,
                        m_NativeMemory,
                        heapOffset));
                m_Placement = placement;
            }
            catch
            {
                VulkanNative.vkDestroyBuffer(device.NativeDevice, m_NativeBuffer, null);
                m_NativeBuffer = default;
                throw;
            }
        }

        internal VulkanBuffer(VulkanDevice device, in RHIBufferDescriptor descriptor, VkBuffer existingBuffer, VkDeviceMemory existingMemory)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = existingBuffer;
            m_NativeMemory = existingMemory;
            m_MemorySize = checked((ulong)descriptor.ByteSize);
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
            ThrowIfDisposed();
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPULocal)
            {
                throw new InvalidOperationException("A GPU-local Vulkan buffer cannot be mapped.");
            }
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (readBegin > byteSize || (readEnd != 0 && (readEnd < readBegin || readEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(readEnd), "The read range must be within the buffer.");
            }
            if (!m_IsMapped)
            {
                void* data;
                bool invalidate =
                    m_Descriptor.StorageMode ==
                        ERHIStorageMode.Readback;
                if (m_PlacedHeap != null)
                {
                    data = m_PlacedHeap.MapShared(invalidate);
                }
                else
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkMapMemory(
                            m_VulkanDevice.NativeDevice,
                            m_NativeMemory,
                            0,
                            m_MemorySize,
                            0,
                            &data));

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
                }

                m_MappedBaseAddress = data;
                m_IsMapped = true;
            }

            byte* mappedAddress = (byte*)m_MappedBaseAddress + m_MemoryOffset + readBegin;
            return new IntPtr(mappedAddress);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
            ThrowIfDisposed();
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (writeBegin > byteSize || (writeEnd != 0 && (writeEnd < writeBegin || writeEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(writeEnd), "The write range must be within the buffer.");
            }
            if (!m_IsMapped)
            {
                throw new InvalidOperationException(
                    "A Vulkan buffer cannot be unmapped before it is mapped.");
            }

            bool flush =
                m_Descriptor.StorageMode ==
                    ERHIStorageMode.GPUUpload ||
                m_Descriptor.StorageMode ==
                    ERHIStorageMode.HostUpload;
            if (m_PlacedHeap != null)
            {
                m_PlacedHeap.FlushAndUnmapShared(flush);
            }
            else
            {
                if (flush)
                {
                    VkMappedMemoryRange range =
                        new VkMappedMemoryRange
                        {
                            sType =
                                VkStructureType.MappedMemoryRange,
                            memory = m_NativeMemory,
                            // Flush the allocation to satisfy non-coherent atom-size alignment.
                            offset = 0,
                            size = ulong.MaxValue,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkFlushMappedMemoryRanges(
                            m_VulkanDevice.NativeDevice,
                            1,
                            &range));
                }

                VulkanNative.vkUnmapMemory(
                    m_VulkanDevice.NativeDevice,
                    m_NativeMemory);
            }

            m_MappedBaseAddress = null;
            m_IsMapped = false;
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new VulkanBufferView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_IsMapped)
            {
                if (m_PlacedHeap != null)
                {
                    m_PlacedHeap.ReleaseSharedMappingAfterResourceDispose();
                }
                else
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                }

                m_MappedBaseAddress = null;
                m_IsMapped = false;
            }
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            if (m_OwnsMemory)
            {
                VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            }
            m_NativeBuffer = default;
            m_NativeMemory = default;
            m_Placement?.Dispose();
            m_Placement = null;
            m_PlacedHeap = null;
        }

        private static void ApplyOpacityMicromapUsage(
            VulkanDevice device,
            ref VkBufferCreateInfo bufferInfo,
            in RHIBufferDescriptor descriptor)
        {
            if (device.OpacityMicromapEnabled &&
                (descriptor.UsageFlag & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct)
            {
                bufferInfo.usage |=
                    VulkanOpacityMicromapNative.MicromapBuildInputReadOnly |
                    VulkanOpacityMicromapNative.MicromapStorage;
            }
        }

        private void ReleaseNativeAllocationAfterConstructionFailure()
        {
            if (m_NativeMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(
                    m_VulkanDevice.NativeDevice,
                    m_NativeMemory,
                    null);
                m_NativeMemory = default;
            }
            if (m_NativeBuffer.Handle != 0)
            {
                VulkanNative.vkDestroyBuffer(
                    m_VulkanDevice.NativeDevice,
                    m_NativeBuffer,
                    null);
                m_NativeBuffer = default;
            }
        }
    }
}
