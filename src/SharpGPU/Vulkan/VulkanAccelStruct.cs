using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanTopLevelAccelStruct : RHITopLevelAccelStruct
    {
        public VkAccelerationStructureKHR NativeAccelerationStructure => m_NativeAccelStruct;
        public VkBuffer NativeBuffer => m_NativeBuffer;
        public VkDeviceMemory NativeMemory => m_NativeMemory;
        public VkBuffer NativeScratchBuffer => m_NativeScratchBuffer;
        public VkBuffer NativeInstanceBuffer => m_NativeInstanceBuffer;

        private VulkanDevice m_VulkanDevice;
        private VkAccelerationStructureKHR m_NativeAccelStruct;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private VkBuffer m_NativeScratchBuffer;
        private VkDeviceMemory m_NativeScratchMemory;
        private VkBuffer m_NativeInstanceBuffer;
        private VkDeviceMemory m_NativeInstanceMemory;

        public VulkanTopLevelAccelStruct(VulkanDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            uint instanceCount = descriptor.InstanceCount;

            // Create instance buffer for VkAccelerationStructureInstanceKHR data
            ulong instanceBufferSize = instanceCount * 64; // sizeof(VkAccelerationStructureInstanceKHR) = 64
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, instanceBufferSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_BUILD_INPUT_READ_ONLY_BIT_KHR |
                VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                out m_NativeInstanceBuffer, out m_NativeInstanceMemory);

            // Get device address of instance buffer
            VkBufferDeviceAddressInfo instanceAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                buffer = m_NativeInstanceBuffer,
            };
            ulong instanceBufferAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &instanceAddrInfo);

            // Setup geometry for TLAS (instances)
            VkAccelerationStructureGeometryKHR geometry = new VkAccelerationStructureGeometryKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR,
                geometryType = VkGeometryTypeKHR.VK_GEOMETRY_TYPE_INSTANCES_KHR,
                flags = VkGeometryFlagsKHR.VK_GEOMETRY_OPAQUE_BIT_KHR,
            };
            geometry.geometry.instances.sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_INSTANCES_DATA_KHR;
            geometry.geometry.instances.arrayOfPointers = false;
            geometry.geometry.instances.data.deviceAddress = instanceBufferAddress;

            // Get build sizes
            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR,
                type = VkAccelerationStructureTypeKHR.VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR,
                flags = VkBuildAccelerationStructureFlagsKHR.VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR |
                        VkBuildAccelerationStructureFlagsKHR.VK_BUILD_ACCELERATION_STRUCTURE_ALLOW_UPDATE_BIT_KHR,
                geometryCount = 1,
                pGeometries = &geometry,
            };

            VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new VkAccelerationStructureBuildSizesInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR,
            };
            VulkanNative.vkGetAccelerationStructureBuildSizesKHR(device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR,
                &buildInfo, &instanceCount, &sizeInfo);

            // Create result buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.accelerationStructureSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR |
                VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                out m_NativeBuffer, out m_NativeMemory);

            // Create acceleration structure
            VkAccelerationStructureCreateInfoKHR createInfo = new VkAccelerationStructureCreateInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR,
                buffer = m_NativeBuffer,
                size = sizeInfo.accelerationStructureSize,
                type = VkAccelerationStructureTypeKHR.VK_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL_KHR,
            };

            fixed (VkAccelerationStructureKHR* asPtr = &m_NativeAccelStruct)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateAccelerationStructureKHR(device.NativeDevice, &createInfo, null, asPtr));
            }

            // Create scratch buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.buildScratchSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                out m_NativeScratchBuffer, out m_NativeScratchMemory);
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            // Instance data update is handled when BuildAccelerationStructure is called on the encoder
            // with VK_BUILD_ACCELERATION_STRUCTURE_MODE_UPDATE_KHR
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyAccelerationStructureKHR(m_VulkanDevice.NativeDevice, m_NativeAccelStruct, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeScratchBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeScratchMemory, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeInstanceBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeInstanceMemory, null);
        }
    }

    internal unsafe class VulkanBottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        public VkAccelerationStructureKHR NativeAccelerationStructure => m_NativeAccelStruct;
        public VkBuffer NativeBuffer => m_NativeBuffer;
        public VkBuffer NativeScratchBuffer => m_NativeScratchBuffer;

        private VulkanDevice m_VulkanDevice;
        private VkAccelerationStructureKHR m_NativeAccelStruct;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private VkBuffer m_NativeScratchBuffer;
        private VkDeviceMemory m_NativeScratchMemory;

        public VulkanBottomLevelAccelStruct(VulkanDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            int geometryCount = descriptor.Geometrys.Length;
            VkAccelerationStructureGeometryKHR* geometries = stackalloc VkAccelerationStructureGeometryKHR[Math.Max(geometryCount, 1)];
            uint* maxPrimitiveCounts = stackalloc uint[Math.Max(geometryCount, 1)];

            for (int i = 0; i < geometryCount; ++i)
            {
                ref RHIAccelStructGeometry geom = ref descriptor.Geometrys.Span[i];

                if (geom.GeometryType == ERHIAccelStructGeometryType.Triangle)
                {
                    VulkanBuffer vertexBuffer = geom.TriangleGeometry.VertexBuffer as VulkanBuffer;
                    VkBufferDeviceAddressInfo vertexAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                        buffer = vertexBuffer.NativeBuffer,
                    };
                    ulong vertexAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &vertexAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR,
                        geometryType = VkGeometryTypeKHR.VK_GEOMETRY_TYPE_TRIANGLES_KHR,
                        flags = geom.IsOpaque ? VkGeometryFlagsKHR.VK_GEOMETRY_OPAQUE_BIT_KHR : 0,
                    };
                    geometries[i].geometry.triangles.sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_TRIANGLES_DATA_KHR;
                    geometries[i].geometry.triangles.vertexFormat = VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
                    geometries[i].geometry.triangles.vertexData.deviceAddress = vertexAddress + geom.TriangleGeometry.VertexOffset;
                    geometries[i].geometry.triangles.vertexStride = geom.TriangleGeometry.VertexStride;
                    geometries[i].geometry.triangles.maxVertex = geom.TriangleGeometry.VertexCount;

                    if (geom.TriangleGeometry.IndexBuffer != null)
                    {
                        VulkanBuffer indexBuffer = geom.TriangleGeometry.IndexBuffer as VulkanBuffer;
                        VkBufferDeviceAddressInfo indexAddrInfo = new VkBufferDeviceAddressInfo()
                        {
                            sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                            buffer = indexBuffer.NativeBuffer,
                        };
                        ulong indexAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &indexAddrInfo);
                        geometries[i].geometry.triangles.indexType = VkIndexType.VK_INDEX_TYPE_UINT32;
                        geometries[i].geometry.triangles.indexData.deviceAddress = indexAddress + geom.TriangleGeometry.IndexOffset;
                        maxPrimitiveCounts[i] = geom.TriangleGeometry.IndexCount / 3;
                    }
                    else
                    {
                        geometries[i].geometry.triangles.indexType = VkIndexType.VK_INDEX_TYPE_NONE_KHR;
                        maxPrimitiveCounts[i] = geom.TriangleGeometry.VertexCount / 3;
                    }
                }
                else if (geom.GeometryType == ERHIAccelStructGeometryType.BoundingBox)
                {
                    VulkanBuffer aabbBuffer = geom.BoundingBoxGeometry.BoundingBoxBuffer as VulkanBuffer;
                    VkBufferDeviceAddressInfo aabbAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                        buffer = aabbBuffer.NativeBuffer,
                    };
                    ulong aabbAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &aabbAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_KHR,
                        geometryType = VkGeometryTypeKHR.VK_GEOMETRY_TYPE_AABBS_KHR,
                        flags = geom.IsOpaque ? VkGeometryFlagsKHR.VK_GEOMETRY_OPAQUE_BIT_KHR : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_GEOMETRY_AABBS_DATA_KHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = aabbAddress + geom.BoundingBoxGeometry.Offset;
                    geometries[i].geometry.aabbs.stride = geom.BoundingBoxGeometry.Stride;
                    maxPrimitiveCounts[i] = geom.BoundingBoxGeometry.Count;
                }
            }

            // Get build sizes
            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_GEOMETRY_INFO_KHR,
                type = VkAccelerationStructureTypeKHR.VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR,
                flags = VkBuildAccelerationStructureFlagsKHR.VK_BUILD_ACCELERATION_STRUCTURE_PREFER_FAST_TRACE_BIT_KHR,
                geometryCount = (uint)geometryCount,
                pGeometries = geometries,
            };

            VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new VkAccelerationStructureBuildSizesInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_BUILD_SIZES_INFO_KHR,
            };
            VulkanNative.vkGetAccelerationStructureBuildSizesKHR(device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.VK_ACCELERATION_STRUCTURE_BUILD_TYPE_DEVICE_KHR,
                &buildInfo, maxPrimitiveCounts, &sizeInfo);

            // Create result buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.accelerationStructureSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR |
                VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                out m_NativeBuffer, out m_NativeMemory);

            // Create acceleration structure
            VkAccelerationStructureCreateInfoKHR createInfo = new VkAccelerationStructureCreateInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_ACCELERATION_STRUCTURE_CREATE_INFO_KHR,
                buffer = m_NativeBuffer,
                size = sizeInfo.accelerationStructureSize,
                type = VkAccelerationStructureTypeKHR.VK_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL_KHR,
            };

            fixed (VkAccelerationStructureKHR* asPtr = &m_NativeAccelStruct)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateAccelerationStructureKHR(device.NativeDevice, &createInfo, null, asPtr));
            }

            // Create scratch buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.buildScratchSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT | VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT,
                out m_NativeScratchBuffer, out m_NativeScratchMemory);
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyAccelerationStructureKHR(m_VulkanDevice.NativeDevice, m_NativeAccelStruct, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeScratchBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeScratchMemory, null);
        }
    }

    internal static unsafe class VulkanAccelStructHelper
    {
        internal static void CreateDeviceAddressBuffer(VulkanDevice device, ulong size, VkBufferUsageFlags usage, VkMemoryPropertyFlags memProps, out VkBuffer buffer, out VkDeviceMemory memory)
        {
            VkBufferCreateInfo bufferInfo = new VkBufferCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO,
                size = size,
                usage = usage,
                sharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
            };

            fixed (VkBuffer* bufferPtr = &buffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateBuffer(device.NativeDevice, &bufferInfo, null, bufferPtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetBufferMemoryRequirements(device.NativeDevice, buffer, &memRequirements);

            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, memProps);

            VkMemoryAllocateFlagsInfo flagsInfo = new VkMemoryAllocateFlagsInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_FLAGS_INFO,
                flags = VkMemoryAllocateFlags.VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT,
            };

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                pNext = &flagsInfo,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &memory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindBufferMemory(device.NativeDevice, buffer, memory, 0));
        }
    }
#pragma warning restore CS8618
}
