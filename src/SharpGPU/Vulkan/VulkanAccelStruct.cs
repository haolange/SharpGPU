using System;
using Vortice.Vulkan;

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

            uint instanceCount = (uint)descriptor.Instances.Length;

            // Create instance buffer for VkAccelerationStructureInstanceKHR data
            ulong instanceBufferSize = instanceCount * 64; // sizeof(VkAccelerationStructureInstanceKHR) = 64
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, instanceBufferSize,
                VkBufferUsageFlags.AccelerationStructureBuildInputReadOnlyKHR |
                VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                out m_NativeInstanceBuffer, out m_NativeInstanceMemory);

            // Get device address of instance buffer
            VkBufferDeviceAddressInfo instanceAddrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = m_NativeInstanceBuffer,
            };
            ulong instanceBufferAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &instanceAddrInfo);

            // Setup geometry for TLAS (instances)
            VkAccelerationStructureGeometryKHR geometry = new VkAccelerationStructureGeometryKHR()
            {
                sType = VkStructureType.AccelerationStructureGeometryKHR,
                geometryType = VkGeometryTypeKHR.Instances,
                flags = VkGeometryFlagsKHR.Opaque,
            };
            geometry.geometry.instances.sType = VkStructureType.AccelerationStructureGeometryInstancesDataKHR;
            geometry.geometry.instances.arrayOfPointers = false;
            geometry.geometry.instances.data.deviceAddress = instanceBufferAddress;

            // Get build sizes
            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
                type = VkAccelerationStructureTypeKHR.TopLevel,
                flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace |
                        VkBuildAccelerationStructureFlagsKHR.AllowUpdate,
                geometryCount = 1,
                pGeometries = &geometry,
            };

            VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new VkAccelerationStructureBuildSizesInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildSizesInfoKHR,
            };
            VulkanNative.vkGetAccelerationStructureBuildSizesKHR(device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.Device,
                &buildInfo, &instanceCount, &sizeInfo);

            // Create result buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.accelerationStructureSize,
                VkBufferUsageFlags.AccelerationStructureStorageKHR |
                VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
                out m_NativeBuffer, out m_NativeMemory);

            // Create acceleration structure
            VkAccelerationStructureCreateInfoKHR createInfo = new VkAccelerationStructureCreateInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureCreateInfoKHR,
                buffer = m_NativeBuffer,
                size = sizeInfo.accelerationStructureSize,
                type = VkAccelerationStructureTypeKHR.TopLevel,
            };

            fixed (VkAccelerationStructureKHR* asPtr = &m_NativeAccelStruct)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateAccelerationStructureKHR(device.NativeDevice, &createInfo, null, asPtr));
            }

            // Create scratch buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.buildScratchSize,
                VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
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

            int geometryCount = descriptor.Geometries.Length;
            VkAccelerationStructureGeometryKHR* geometries = stackalloc VkAccelerationStructureGeometryKHR[Math.Max(geometryCount, 1)];
            uint* maxPrimitiveCounts = stackalloc uint[Math.Max(geometryCount, 1)];

            for (int i = 0; i < geometryCount; ++i)
            {
                RHIAccelStructGeometry geom = descriptor.Geometries[i];

                if (geom.GeometryType == EAccelStructGeometryType.Triangle)
                {
                    RHIAccelStructTriangles triangleGeometry = (RHIAccelStructTriangles)geom;
                    VulkanBuffer vertexBuffer = triangleGeometry.VertexBuffer as VulkanBuffer;
                    VkBufferDeviceAddressInfo vertexAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = vertexBuffer.NativeBuffer,
                    };
                    ulong vertexAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &vertexAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Triangles,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.triangles.sType = VkStructureType.AccelerationStructureGeometryTrianglesDataKHR;
                    geometries[i].geometry.triangles.vertexFormat = VulkanUtility.ConvertToVkFormat(triangleGeometry.VertexFormat);
                    geometries[i].geometry.triangles.vertexData.deviceAddress = vertexAddress + triangleGeometry.VertexOffset;
                    geometries[i].geometry.triangles.vertexStride = triangleGeometry.VertexStride;
                    geometries[i].geometry.triangles.maxVertex = triangleGeometry.VertexCount;

                    if (triangleGeometry.IndexBuffer != null)
                    {
                        VulkanBuffer indexBuffer = triangleGeometry.IndexBuffer as VulkanBuffer;
                        VkBufferDeviceAddressInfo indexAddrInfo = new VkBufferDeviceAddressInfo()
                        {
                            sType = VkStructureType.BufferDeviceAddressInfo,
                            buffer = indexBuffer.NativeBuffer,
                        };
                        ulong indexAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &indexAddrInfo);
                        geometries[i].geometry.triangles.indexType = VulkanUtility.ConvertToVkIndexType(triangleGeometry.IndexFormat);
                        geometries[i].geometry.triangles.indexData.deviceAddress = indexAddress + triangleGeometry.IndexOffset;
                        maxPrimitiveCounts[i] = triangleGeometry.IndexCount / 3;
                    }
                    else
                    {
                        geometries[i].geometry.triangles.indexType = VkIndexType.NoneKHR;
                        maxPrimitiveCounts[i] = triangleGeometry.VertexCount / 3;
                    }
                }
                else if (geom.GeometryType == EAccelStructGeometryType.AABB)
                {
                    RHIAccelStructAABBs aabbGeometry = (RHIAccelStructAABBs)geom;
                    VulkanBuffer aabbBuffer = aabbGeometry.AABBBuffer as VulkanBuffer;
                    VkBufferDeviceAddressInfo aabbAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = aabbBuffer.NativeBuffer,
                    };
                    ulong aabbAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &aabbAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Aabbs,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.AccelerationStructureGeometryAabbsDataKHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = aabbAddress + aabbGeometry.Offset;
                    geometries[i].geometry.aabbs.stride = aabbGeometry.Stride;
                    maxPrimitiveCounts[i] = aabbGeometry.Count;
                }
            }

            // Get build sizes
            VkAccelerationStructureBuildGeometryInfoKHR buildInfo = new VkAccelerationStructureBuildGeometryInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildGeometryInfoKHR,
                type = VkAccelerationStructureTypeKHR.BottomLevel,
                flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace,
                geometryCount = (uint)geometryCount,
                pGeometries = geometries,
            };

            VkAccelerationStructureBuildSizesInfoKHR sizeInfo = new VkAccelerationStructureBuildSizesInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureBuildSizesInfoKHR,
            };
            VulkanNative.vkGetAccelerationStructureBuildSizesKHR(device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.Device,
                &buildInfo, maxPrimitiveCounts, &sizeInfo);

            // Create result buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.accelerationStructureSize,
                VkBufferUsageFlags.AccelerationStructureStorageKHR |
                VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
                out m_NativeBuffer, out m_NativeMemory);

            // Create acceleration structure
            VkAccelerationStructureCreateInfoKHR createInfo = new VkAccelerationStructureCreateInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureCreateInfoKHR,
                buffer = m_NativeBuffer,
                size = sizeInfo.accelerationStructureSize,
                type = VkAccelerationStructureTypeKHR.BottomLevel,
            };

            fixed (VkAccelerationStructureKHR* asPtr = &m_NativeAccelStruct)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateAccelerationStructureKHR(device.NativeDevice, &createInfo, null, asPtr));
            }

            // Create scratch buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(device, sizeInfo.buildScratchSize,
                VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
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
                sType = VkStructureType.BufferCreateInfo,
                size = size,
                usage = usage,
                sharingMode = VkSharingMode.Exclusive,
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
                sType = VkStructureType.MemoryAllocateFlagsInfo,
                flags = VkMemoryAllocateFlags.DeviceAddress,
            };

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.MemoryAllocateInfo,
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


