using System;
using Vortice.Vulkan;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CS8618
    internal unsafe class VulkanTopLevelAccelStruct : RHITopLevelAccelStruct
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct VkAccelerationStructureInstanceRaw
        {
            public fixed float transform[12];
            public uint instanceCustomIndexAndMask;
            public uint instanceSbtRecordOffsetAndFlags;
            public ulong accelerationStructureReference;
        }

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
        private ulong m_InstanceBufferSize;

        public VulkanTopLevelAccelStruct(VulkanDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            uint instanceCount = (uint)descriptor.Instances.Length;

            // Create instance buffer for VkAccelerationStructureInstanceKHR data
            ulong instanceDataSize = Math.Max(instanceCount, 1u) * (ulong)sizeof(VkAccelerationStructureInstanceRaw);
            ulong instanceBufferSize = descriptor.Offset + instanceDataSize;
            m_InstanceBufferSize = instanceBufferSize;
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
            geometry.geometry.instances.data.deviceAddress = instanceBufferAddress + descriptor.Offset;

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
            uint buildSizeInstanceCount = Math.Max(instanceCount, 1u);
            VulkanNative.vkGetAccelerationStructureBuildSizesKHR(device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.Device,
                &buildInfo, &buildSizeInstanceCount, &sizeInfo);

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

            UploadInstanceData(descriptor);
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            UploadInstanceData(descriptor);
        }

        private void UploadInstanceData(in RHITopLevelAccelStructDescriptor descriptor)
        {
            int instanceCount = descriptor.Instances.Length;
            if (instanceCount == 0)
            {
                return;
            }

            ulong byteSize = (ulong)(instanceCount * sizeof(VkAccelerationStructureInstanceRaw));
            ulong uploadOffset = descriptor.Offset;
            if (uploadOffset + byteSize > m_InstanceBufferSize)
            {
                throw new InvalidOperationException("TLAS instance upload exceeds allocated instance buffer size.");
            }
            VkAccelerationStructureInstanceRaw* rawInstances = stackalloc VkAccelerationStructureInstanceRaw[instanceCount];

            Span<RHIAccelStructInstance> instances = descriptor.Instances.Span;
            for (int i = 0; i < instanceCount; ++i)
            {
                rawInstances[i] = BuildRawInstance(in instances[i]);
            }

            void* mapped = null;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_NativeInstanceMemory, uploadOffset, byteSize, 0, &mapped));
            try
            {
                Buffer.MemoryCopy(rawInstances, mapped, byteSize, byteSize);
            }
            finally
            {
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_NativeInstanceMemory);
            }
        }

        private VkAccelerationStructureInstanceRaw BuildRawInstance(in RHIAccelStructInstance instance)
        {
            VulkanBottomLevelAccelStruct vkBLAS = instance.BottomLevelAccelStruct as VulkanBottomLevelAccelStruct
                ?? throw new InvalidOperationException("TLAS instance references a non-Vulkan BLAS.");

            VkAccelerationStructureInstanceRaw raw = default;
            raw.transform[0] = instance.TransformMatrix.c0.x;
            raw.transform[1] = instance.TransformMatrix.c1.x;
            raw.transform[2] = instance.TransformMatrix.c2.x;
            raw.transform[3] = instance.TransformMatrix.c3.x;
            raw.transform[4] = instance.TransformMatrix.c0.y;
            raw.transform[5] = instance.TransformMatrix.c1.y;
            raw.transform[6] = instance.TransformMatrix.c2.y;
            raw.transform[7] = instance.TransformMatrix.c3.y;
            raw.transform[8] = instance.TransformMatrix.c0.z;
            raw.transform[9] = instance.TransformMatrix.c1.z;
            raw.transform[10] = instance.TransformMatrix.c2.z;
            raw.transform[11] = instance.TransformMatrix.c3.z;

            uint instanceCustomIndex = instance.InstanceID & 0x00FFFFFFu;
            uint instanceMask = (uint)instance.InstanceMask & 0xFFu;
            uint sbtRecordOffset = instance.HitGroupIndex & 0x00FFFFFFu;
            uint geometryInstanceFlags = ((uint)ConvertToVkGeometryInstanceFlags(instance.Flag)) & 0xFFu;

            raw.instanceCustomIndexAndMask = instanceCustomIndex | (instanceMask << 24);
            raw.instanceSbtRecordOffsetAndFlags = sbtRecordOffset | (geometryInstanceFlags << 24);
            raw.accelerationStructureReference = GetAccelerationStructureDeviceAddress(vkBLAS.NativeAccelerationStructure);
            return raw;
        }

        private ulong GetAccelerationStructureDeviceAddress(VkAccelerationStructureKHR accelerationStructure)
        {
            VkAccelerationStructureDeviceAddressInfoKHR addressInfo = new VkAccelerationStructureDeviceAddressInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureDeviceAddressInfoKHR,
                accelerationStructure = accelerationStructure,
            };
            return VulkanNative.vkGetAccelerationStructureDeviceAddressKHR(m_VulkanDevice.NativeDevice, &addressInfo);
        }

        private static VkGeometryInstanceFlagsKHR ConvertToVkGeometryInstanceFlags(EAccelStructInstanceFlag flag)
        {
            VkGeometryInstanceFlagsKHR result = 0;
            if ((flag & EAccelStructInstanceFlag.TriangleCullDisable) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.TriangleFacingCullDisable;
            }
            if ((flag & EAccelStructInstanceFlag.TriangleFrontCounterclockwise) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.TriangleFrontCounterclockwise;
            }
            if ((flag & EAccelStructInstanceFlag.ForceOpaque) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.ForceOpaque;
            }
            if ((flag & EAccelStructInstanceFlag.ForceNonOpaque) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.ForceNoOpaque;
            }
            return result;
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
        [StructLayout(LayoutKind.Sequential)]
        private struct VkAabbPositionRaw
        {
            public float MinX;
            public float MinY;
            public float MinZ;
            public float MaxX;
            public float MaxY;
            public float MaxZ;
        }

        public VkAccelerationStructureKHR NativeAccelerationStructure => m_NativeAccelStruct;
        public VkBuffer NativeBuffer => m_NativeBuffer;
        public VkBuffer NativeScratchBuffer => m_NativeScratchBuffer;

        private VulkanDevice m_VulkanDevice;
        private VkAccelerationStructureKHR m_NativeAccelStruct;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private VkBuffer m_NativeScratchBuffer;
        private VkDeviceMemory m_NativeScratchMemory;
        private VkBuffer[] m_NativeCurveAabbBuffers;
        private VkDeviceMemory[] m_NativeCurveAabbMemories;

        public VulkanBottomLevelAccelStruct(VulkanDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            int geometryCount = descriptor.Geometries.Length;
            VkAccelerationStructureGeometryKHR* geometries = stackalloc VkAccelerationStructureGeometryKHR[Math.Max(geometryCount, 1)];
            uint* maxPrimitiveCounts = stackalloc uint[Math.Max(geometryCount, 1)];
            m_NativeCurveAabbBuffers = geometryCount > 0 ? new VkBuffer[geometryCount] : Array.Empty<VkBuffer>();
            m_NativeCurveAabbMemories = geometryCount > 0 ? new VkDeviceMemory[geometryCount] : Array.Empty<VkDeviceMemory>();

            for (int i = 0; i < geometryCount; ++i)
            {
                RHIAccelStructGeometry geom = descriptor.Geometries[i];

                if (geom.GeometryType == EAccelStructGeometryType.Triangle)
                {
                    RHIAccelStructTriangles triangleGeometry = (RHIAccelStructTriangles)geom;
                    VulkanBuffer vertexBuffer = triangleGeometry.VertexBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Triangle geometry requires a Vulkan vertex buffer.");
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
                    geometries[i].geometry.triangles.vertexFormat = VulkanUtility.ConvertToVkAccelerationStructureVertexFormat(triangleGeometry.VertexFormat);
                    geometries[i].geometry.triangles.vertexData.deviceAddress = vertexAddress + triangleGeometry.VertexOffset;
                    geometries[i].geometry.triangles.vertexStride = triangleGeometry.VertexStride;
                    geometries[i].geometry.triangles.maxVertex = triangleGeometry.VertexCount > 0
                        ? triangleGeometry.VertexCount - 1
                        : 0;

                    if (triangleGeometry.IndexBuffer != null)
                    {
                        VulkanBuffer indexBuffer = triangleGeometry.IndexBuffer as VulkanBuffer
                            ?? throw new InvalidOperationException("Triangle index buffer is not a Vulkan buffer.");
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
                    VulkanBuffer aabbBuffer = aabbGeometry.AABBBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("AABB geometry requires a Vulkan buffer.");
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
                else if (geom.GeometryType == EAccelStructGeometryType.Curves)
                {
                    RHIAccelStructCurves curveGeometry = geom as RHIAccelStructCurves
                        ?? throw new InvalidOperationException("Curve geometry descriptor type mismatch.");
                    VulkanBuffer controlPointBuffer = curveGeometry.ControlPointBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Curve geometry requires a Vulkan control-point buffer.");
                    VulkanBuffer radiusBuffer = curveGeometry.RadiusBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Curve geometry requires a Vulkan radius buffer.");
                    VulkanBuffer curveIndexBuffer = curveGeometry.IndexBuffer as VulkanBuffer;
                    if (curveGeometry.IndexBuffer != null && curveIndexBuffer == null)
                    {
                        throw new InvalidOperationException("Curve index buffer is not a Vulkan buffer.");
                    }

                    uint curveAabbCount = curveGeometry.SegmentCount;
                    if (curveAabbCount == 0)
                    {
                        throw new InvalidOperationException("Curve geometry has no segments.");
                    }

                    uint segmentControlPointCount = curveGeometry.SegmentControlPointCount;
                    if (segmentControlPointCount == 0)
                    {
                        throw new InvalidOperationException("Curve geometry segment control-point count must be greater than zero.");
                    }

                    if (curveGeometry.ControlPointStride < 12u)
                    {
                        throw new InvalidOperationException($"Curve control-point stride ({curveGeometry.ControlPointStride}) is smaller than 3-float position size.");
                    }

                    if (curveGeometry.RadiusStride < 4u)
                    {
                        throw new InvalidOperationException($"Curve radius stride ({curveGeometry.RadiusStride}) is smaller than float size.");
                    }

                    ulong curveAabbBufferSize = (ulong)curveAabbCount * (ulong)sizeof(VkAabbPositionRaw);
                    VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                        device,
                        curveAabbBufferSize,
                        VkBufferUsageFlags.AccelerationStructureBuildInputReadOnlyKHR | VkBufferUsageFlags.ShaderDeviceAddress,
                        VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                        out VkBuffer curveAabbBuffer,
                        out VkDeviceMemory curveAabbMemory);

                    m_NativeCurveAabbBuffers[i] = curveAabbBuffer;
                    m_NativeCurveAabbMemories[i] = curveAabbMemory;

                    IntPtr mappedControlPoint = controlPointBuffer.Map(0, 0);
                    IntPtr mappedRadius = radiusBuffer.Map(0, 0);
                    IntPtr mappedIndex = IntPtr.Zero;
                    if (curveIndexBuffer != null)
                    {
                        mappedIndex = curveIndexBuffer.Map(0, 0);
                    }

                    void* mappedAabb = null;
                    VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(device.NativeDevice, curveAabbMemory, 0, curveAabbBufferSize, 0, &mappedAabb));
                    try
                    {
                        BuildCurveAabbs(
                            (VkAabbPositionRaw*)mappedAabb,
                            curveAabbCount,
                            segmentControlPointCount,
                            curveGeometry,
                            (byte*)mappedControlPoint.ToPointer(),
                            (byte*)mappedRadius.ToPointer(),
                            mappedIndex == IntPtr.Zero ? null : (byte*)mappedIndex.ToPointer());
                    }
                    finally
                    {
                        VulkanNative.vkUnmapMemory(device.NativeDevice, curveAabbMemory);
                        if (curveIndexBuffer != null)
                        {
                            curveIndexBuffer.UnMap(0, 0);
                        }
                        radiusBuffer.UnMap(0, 0);
                        controlPointBuffer.UnMap(0, 0);
                    }

                    VkBufferDeviceAddressInfo curveAabbAddrInfo = new VkBufferDeviceAddressInfo()
                    {
                        sType = VkStructureType.BufferDeviceAddressInfo,
                        buffer = curveAabbBuffer,
                    };
                    ulong curveAabbAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &curveAabbAddrInfo);

                    geometries[i] = new VkAccelerationStructureGeometryKHR()
                    {
                        sType = VkStructureType.AccelerationStructureGeometryKHR,
                        geometryType = VkGeometryTypeKHR.Aabbs,
                        flags = (geom.GeometryFlag & EAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.AccelerationStructureGeometryAabbsDataKHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = curveAabbAddress;
                    geometries[i].geometry.aabbs.stride = (ulong)sizeof(VkAabbPositionRaw);
                    maxPrimitiveCounts[i] = curveAabbCount;
                }
                else
                {
                    throw new NotSupportedException($"Unsupported BLAS geometry type '{geom.GeometryType}'.");
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
            for (int i = 0; i < m_NativeCurveAabbBuffers.Length; ++i)
            {
                VkBuffer curveAabbBuffer = m_NativeCurveAabbBuffers[i];
                if (curveAabbBuffer.Handle != 0)
                {
                    VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, curveAabbBuffer, null);
                    m_NativeCurveAabbBuffers[i] = default;
                }

                VkDeviceMemory curveAabbMemory = m_NativeCurveAabbMemories[i];
                if (curveAabbMemory.Handle != 0)
                {
                    VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, curveAabbMemory, null);
                    m_NativeCurveAabbMemories[i] = default;
                }
            }

            VulkanNative.vkDestroyAccelerationStructureKHR(m_VulkanDevice.NativeDevice, m_NativeAccelStruct, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeScratchBuffer, null);
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeScratchMemory, null);
        }

        internal VkBuffer GetCurveAabbBuffer(in int geometryIndex)
        {
            if ((uint)geometryIndex >= (uint)m_NativeCurveAabbBuffers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(geometryIndex));
            }

            VkBuffer curveAabbBuffer = m_NativeCurveAabbBuffers[geometryIndex];
            if (curveAabbBuffer.Handle == 0)
            {
                throw new InvalidOperationException($"Curve AABB buffer for geometry index {geometryIndex} is not initialized.");
            }

            return curveAabbBuffer;
        }

        private static void BuildCurveAabbs(
            VkAabbPositionRaw* aabbs,
            in uint segmentCount,
            in uint segmentControlPointCount,
            in RHIAccelStructCurves curveGeometry,
            byte* controlPointData,
            byte* radiusData,
            byte* indexData)
        {
            byte* controlPointBase = controlPointData + curveGeometry.ControlPointOffset;
            byte* radiusBase = radiusData + curveGeometry.RadiusOffset;
            byte* indexBase = indexData != null ? indexData + curveGeometry.IndexOffset : null;
            uint sequentialSegmentStep = segmentControlPointCount > 1u ? segmentControlPointCount - 1u : 1u;

            for (uint segmentIndex = 0; segmentIndex < segmentCount; ++segmentIndex)
            {
                float minX = float.PositiveInfinity;
                float minY = float.PositiveInfinity;
                float minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxY = float.NegativeInfinity;
                float maxZ = float.NegativeInfinity;

                for (uint localControlPoint = 0; localControlPoint < segmentControlPointCount; ++localControlPoint)
                {
                    uint controlPointIndex;
                    if (indexBase != null)
                    {
                        uint indexElement = segmentIndex * segmentControlPointCount + localControlPoint;
                        controlPointIndex = ReadCurveControlPointIndex(indexBase, curveGeometry.IndexFormat, indexElement);
                    }
                    else
                    {
                        controlPointIndex = segmentIndex * sequentialSegmentStep + localControlPoint;
                    }

                    if (controlPointIndex >= curveGeometry.ControlPointCount)
                    {
                        throw new InvalidOperationException($"Curve control-point index {controlPointIndex} out of range (count={curveGeometry.ControlPointCount}).");
                    }

                    float* controlPoint = (float*)(controlPointBase + (controlPointIndex * curveGeometry.ControlPointStride));
                    float* radius = (float*)(radiusBase + (controlPointIndex * curveGeometry.RadiusStride));
                    float inflatedRadius = *radius;
                    float candidateMinX = controlPoint[0] - inflatedRadius;
                    float candidateMinY = controlPoint[1] - inflatedRadius;
                    float candidateMinZ = controlPoint[2] - inflatedRadius;
                    float candidateMaxX = controlPoint[0] + inflatedRadius;
                    float candidateMaxY = controlPoint[1] + inflatedRadius;
                    float candidateMaxZ = controlPoint[2] + inflatedRadius;

                    if (candidateMinX < minX) minX = candidateMinX;
                    if (candidateMinY < minY) minY = candidateMinY;
                    if (candidateMinZ < minZ) minZ = candidateMinZ;
                    if (candidateMaxX > maxX) maxX = candidateMaxX;
                    if (candidateMaxY > maxY) maxY = candidateMaxY;
                    if (candidateMaxZ > maxZ) maxZ = candidateMaxZ;
                }

                aabbs[segmentIndex].MinX = minX;
                aabbs[segmentIndex].MinY = minY;
                aabbs[segmentIndex].MinZ = minZ;
                aabbs[segmentIndex].MaxX = maxX;
                aabbs[segmentIndex].MaxY = maxY;
                aabbs[segmentIndex].MaxZ = maxZ;
            }
        }

        private static uint ReadCurveControlPointIndex(byte* indexBase, in ERHIBufferFormat format, in uint elementIndex)
        {
            return format switch
            {
                ERHIBufferFormat.UInt16 => ((ushort*)indexBase)[elementIndex],
                ERHIBufferFormat.UInt32 => ((uint*)indexBase)[elementIndex],
                _ => throw new NotSupportedException($"Unsupported curve index format '{format}'."),
            };
        }
    }

    internal static unsafe class VulkanAccelStructHelper
    {
        internal static void CreateDeviceAddressBuffer(VulkanDevice device, ulong size, VkBufferUsageFlags usage, VkMemoryPropertyFlags memProps, out VkBuffer buffer, out VkDeviceMemory memory)
        {
            if (size == 0)
            {
                throw new InvalidOperationException("Vulkan buffer size must be greater than zero.");
            }

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
