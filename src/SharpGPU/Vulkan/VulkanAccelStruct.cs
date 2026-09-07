using System;
using Vortice.Vulkan;
using System.Runtime.InteropServices;

namespace SharpGPU
{
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

        public VkAccelerationStructureKHR NativeAccelerationStructure { get { ThrowIfDisposed(); return m_NativeAccelStruct; } }
        internal VulkanDevice Device { get { ThrowIfDisposed(); return m_VulkanDevice; } }
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
        private bool m_UsesMotion;

        public VulkanTopLevelAccelStruct(VulkanDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            RHIOpacityMicromapContract.ValidateTlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(device, in descriptor);
            m_UsesMotion = RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag);

            uint instanceCount = (uint)descriptor.Instances.Length;

            ulong instanceStride = m_UsesMotion
                ? (ulong)VulkanRayTracingMotionNative.MotionInstanceByteCount
                : (ulong)sizeof(VkAccelerationStructureInstanceRaw);
            ulong instanceDataSize = Math.Max(instanceCount, 1u) * instanceStride;
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
                flags = VulkanAccelStructHelper.ComposeTopLevelBuildFlags(descriptor),
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
            VulkanRayTracingMotionNative.VkAccelerationStructureMotionInfoNV motionInfo = default;
            VkAccelerationStructureCreateInfoKHR createInfo = new VkAccelerationStructureCreateInfoKHR()
            {
                sType = VkStructureType.AccelerationStructureCreateInfoKHR,
                buffer = m_NativeBuffer,
                size = sizeInfo.accelerationStructureSize,
                type = VkAccelerationStructureTypeKHR.TopLevel,
            };
            if (m_UsesMotion)
            {
                createInfo.createFlags = VulkanRayTracingMotionNative.CreateMotionBit;
                motionInfo.sType = VulkanRayTracingMotionNative.MotionInfoStructureType;
                motionInfo.maxInstances = Math.Max(instanceCount, 1u);
                createInfo.pNext = &motionInfo;
            }

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
            RHIOpacityMicromapContract.ValidateTlasDescriptor(m_VulkanDevice, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(m_VulkanDevice, in descriptor);
            RHIAccelStructMotionContract.RejectMotionModeSwitch(m_UsesMotion, descriptor.Flag);
            UploadInstanceData(descriptor);
            m_Descriptor = descriptor;
        }

        private void UploadInstanceData(in RHITopLevelAccelStructDescriptor descriptor)
        {
            int instanceCount = descriptor.Instances.Length;
            if (instanceCount == 0)
            {
                return;
            }

            ulong instanceStride = m_UsesMotion
                ? (ulong)VulkanRayTracingMotionNative.MotionInstanceByteCount
                : (ulong)sizeof(VkAccelerationStructureInstanceRaw);
            ulong byteSize = (ulong)instanceCount * instanceStride;
            ulong uploadOffset = descriptor.Offset;
            if (uploadOffset + byteSize > m_InstanceBufferSize)
            {
                throw new InvalidOperationException("TLAS instance upload exceeds allocated instance buffer size.");
            }

            byte[] packed = new byte[checked((int)byteSize)];
            Span<RHIAccelStructInstance> instances = descriptor.Instances.Span;
            fixed (byte* packedPtr = packed)
            {
                if (m_UsesMotion)
                {
                    if (sizeof(VulkanRayTracingMotionNative.VkAccelerationStructureMotionInstanceNV) !=
                        VulkanRayTracingMotionNative.MotionInstanceByteCount)
                    {
                        throw new InvalidOperationException(
                            "Vulkan motion-instance struct size must be the 160-byte spec stride.");
                    }

                    for (int i = 0; i < instanceCount; ++i)
                    {
                        VulkanRayTracingMotionNative.VkAccelerationStructureMotionInstanceNV motion =
                            BuildMotionInstance(in instances[i]);
                        Buffer.MemoryCopy(
                            &motion,
                            packedPtr + ((long)i * VulkanRayTracingMotionNative.MotionInstanceByteCount),
                            VulkanRayTracingMotionNative.MotionInstanceByteCount,
                            VulkanRayTracingMotionNative.MotionInstanceByteCount);
                    }
                }
                else
                {
                    VkAccelerationStructureInstanceRaw* rawInstances =
                        (VkAccelerationStructureInstanceRaw*)packedPtr;
                    for (int i = 0; i < instanceCount; ++i)
                    {
                        rawInstances[i] = BuildRawInstance(in instances[i]);
                    }
                }
            }

            void* mapped = null;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_NativeInstanceMemory, uploadOffset, byteSize, 0, &mapped));
            try
            {
                fixed (byte* packedPtr = packed)
                {
                    Buffer.MemoryCopy(packedPtr, mapped, (long)byteSize, (long)byteSize);
                }
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

        private VulkanRayTracingMotionNative.VkAccelerationStructureMotionInstanceNV BuildMotionInstance(
            in RHIAccelStructInstance instance)
        {
            VulkanBottomLevelAccelStruct vkBLAS = instance.BottomLevelAccelStruct as VulkanBottomLevelAccelStruct
                ?? throw new InvalidOperationException("TLAS instance references a non-Vulkan BLAS.");

            uint instanceCustomIndex = instance.InstanceID & 0x00FFFFFFu;
            uint instanceMask = (uint)instance.InstanceMask & 0xFFu;
            uint sbtRecordOffset = instance.HitGroupIndex & 0x00FFFFFFu;
            uint geometryInstanceFlags = ((uint)ConvertToVkGeometryInstanceFlags(instance.Flag)) & 0xFFu;
            uint packedCustomIndexAndMask = instanceCustomIndex | (instanceMask << 24);
            uint packedSbtAndFlags = sbtRecordOffset | (geometryInstanceFlags << 24);
            ulong accelerationStructureReference = GetAccelerationStructureDeviceAddress(vkBLAS.NativeAccelerationStructure);

            VulkanRayTracingMotionNative.VkAccelerationStructureMotionInstanceNV motion = default;
            if (instance.MotionType == ERHIAccelStructMotionInstanceType.Srt)
            {
                motion.type = VulkanRayTracingMotionNative.MotionInstanceTypeSrt;
                motion.data.srtMotionInstance.transformT0 = ConvertToSrt(instance.MotionSrtT0);
                motion.data.srtMotionInstance.transformT1 = ConvertToSrt(instance.MotionSrtT1);
                motion.data.srtMotionInstance.instanceCustomIndexAndMask = packedCustomIndexAndMask;
                motion.data.srtMotionInstance.instanceSbtRecordOffsetAndFlags = packedSbtAndFlags;
                motion.data.srtMotionInstance.accelerationStructureReference = accelerationStructureReference;
                return motion;
            }

            if (instance.MotionType == ERHIAccelStructMotionInstanceType.Matrix)
            {
                motion.type = VulkanRayTracingMotionNative.MotionInstanceTypeMatrix;
                WriteTransform(ref motion.data.matrixMotionInstance, useEndTransform: false, instance.TransformMatrix);
                WriteTransform(ref motion.data.matrixMotionInstance, useEndTransform: true, instance.MotionTransformMatrix);
                motion.data.matrixMotionInstance.instanceCustomIndexAndMask = packedCustomIndexAndMask;
                motion.data.matrixMotionInstance.instanceSbtRecordOffsetAndFlags = packedSbtAndFlags;
                motion.data.matrixMotionInstance.accelerationStructureReference = accelerationStructureReference;
                return motion;
            }

            motion.type = VulkanRayTracingMotionNative.MotionInstanceTypeStatic;
            WriteStaticTransform(ref motion.data.staticInstance, instance.TransformMatrix);
            motion.data.staticInstance.instanceCustomIndexAndMask = packedCustomIndexAndMask;
            motion.data.staticInstance.instanceSbtRecordOffsetAndFlags = packedSbtAndFlags;
            motion.data.staticInstance.accelerationStructureReference = accelerationStructureReference;
            return motion;
        }

        private static VulkanRayTracingMotionNative.VkSRTDataNV ConvertToSrt(in RHIAccelStructSrtTransform transform)
        {
            return new VulkanRayTracingMotionNative.VkSRTDataNV
            {
                sx = transform.Sx,
                a = transform.A,
                b = transform.B,
                pvx = transform.Pvx,
                sy = transform.Sy,
                c = transform.C,
                pvy = transform.Pvy,
                sz = transform.Sz,
                pvz = transform.Pvz,
                qx = transform.Qx,
                qy = transform.Qy,
                qz = transform.Qz,
                qw = transform.Qw,
                tx = transform.Tx,
                ty = transform.Ty,
                tz = transform.Tz,
            };
        }

        private static void WriteTransform(
            ref VulkanRayTracingMotionNative.VkAccelerationStructureMatrixMotionInstanceNV native,
            in bool useEndTransform,
            in SharpMath.float4x4 matrix)
        {
            if (useEndTransform)
            {
                native.transformT1[0] = matrix.c0.x;
                native.transformT1[1] = matrix.c1.x;
                native.transformT1[2] = matrix.c2.x;
                native.transformT1[3] = matrix.c3.x;
                native.transformT1[4] = matrix.c0.y;
                native.transformT1[5] = matrix.c1.y;
                native.transformT1[6] = matrix.c2.y;
                native.transformT1[7] = matrix.c3.y;
                native.transformT1[8] = matrix.c0.z;
                native.transformT1[9] = matrix.c1.z;
                native.transformT1[10] = matrix.c2.z;
                native.transformT1[11] = matrix.c3.z;
                return;
            }

            native.transformT0[0] = matrix.c0.x;
            native.transformT0[1] = matrix.c1.x;
            native.transformT0[2] = matrix.c2.x;
            native.transformT0[3] = matrix.c3.x;
            native.transformT0[4] = matrix.c0.y;
            native.transformT0[5] = matrix.c1.y;
            native.transformT0[6] = matrix.c2.y;
            native.transformT0[7] = matrix.c3.y;
            native.transformT0[8] = matrix.c0.z;
            native.transformT0[9] = matrix.c1.z;
            native.transformT0[10] = matrix.c2.z;
            native.transformT0[11] = matrix.c3.z;
        }

        private static void WriteStaticTransform(
            ref VulkanRayTracingMotionNative.VkAccelerationStructureInstanceRaw native,
            in SharpMath.float4x4 matrix)
        {
            native.transform[0] = matrix.c0.x;
            native.transform[1] = matrix.c1.x;
            native.transform[2] = matrix.c2.x;
            native.transform[3] = matrix.c3.x;
            native.transform[4] = matrix.c0.y;
            native.transform[5] = matrix.c1.y;
            native.transform[6] = matrix.c2.y;
            native.transform[7] = matrix.c3.y;
            native.transform[8] = matrix.c0.z;
            native.transform[9] = matrix.c1.z;
            native.transform[10] = matrix.c2.z;
            native.transform[11] = matrix.c3.z;
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

        private static VkGeometryInstanceFlagsKHR ConvertToVkGeometryInstanceFlags(ERHIAccelStructInstanceFlag flag)
        {
            VkGeometryInstanceFlagsKHR result = 0;
            if ((flag & ERHIAccelStructInstanceFlag.TriangleCullDisable) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.TriangleFacingCullDisable;
            }
            if ((flag & ERHIAccelStructInstanceFlag.TriangleFrontCounterclockwise) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.TriangleFrontCounterclockwise;
            }
            if ((flag & ERHIAccelStructInstanceFlag.ForceOpaque) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.ForceOpaque;
            }
            if ((flag & ERHIAccelStructInstanceFlag.ForceNonOpaque) != 0)
            {
                result |= VkGeometryInstanceFlagsKHR.ForceNoOpaque;
            }
            if ((flag & ERHIAccelStructInstanceFlag.ForceOmm2State) != 0)
            {
                result |= (VkGeometryInstanceFlagsKHR)0x10;
            }
            if ((flag & ERHIAccelStructInstanceFlag.DisableOmms) != 0)
            {
                result |= (VkGeometryInstanceFlagsKHR)0x20;
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

        internal VulkanDevice Device { get { ThrowIfDisposed(); return m_VulkanDevice; } }
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
        private VkBuffer[] m_NativeOmmSpecialIndexBuffers;
        private VkDeviceMemory[] m_NativeOmmSpecialIndexMemories;
        private IntPtr[] m_NativeOmmUsageCounts;

        public VulkanBottomLevelAccelStruct(VulkanDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_VulkanDevice = device;
            RHIOpacityMicromapContract.ValidateBlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateBlasDescriptor(device, in descriptor);
            m_Descriptor = descriptor;

            int geometryCount = descriptor.Geometries.Length;
            VkAccelerationStructureGeometryKHR* geometries = stackalloc VkAccelerationStructureGeometryKHR[Math.Max(geometryCount, 1)];
            uint* maxPrimitiveCounts = stackalloc uint[Math.Max(geometryCount, 1)];
            m_NativeCurveAabbBuffers = geometryCount > 0 ? new VkBuffer[geometryCount] : Array.Empty<VkBuffer>();
            m_NativeCurveAabbMemories = geometryCount > 0 ? new VkDeviceMemory[geometryCount] : Array.Empty<VkDeviceMemory>();
            m_NativeOmmSpecialIndexBuffers = geometryCount > 0 ? new VkBuffer[geometryCount] : Array.Empty<VkBuffer>();
            m_NativeOmmSpecialIndexMemories = geometryCount > 0 ? new VkDeviceMemory[geometryCount] : Array.Empty<VkDeviceMemory>();
            m_NativeOmmUsageCounts = geometryCount > 0 ? new IntPtr[geometryCount] : Array.Empty<IntPtr>();
            VulkanOpacityMicromapNative.VkAccelerationStructureTrianglesOpacityMicromapEXT* ommAttachments =
                stackalloc VulkanOpacityMicromapNative.VkAccelerationStructureTrianglesOpacityMicromapEXT[Math.Max(geometryCount, 1)];
            VulkanRayTracingMotionNative.VkAccelerationStructureGeometryMotionTrianglesDataNV* motionAttachments =
                stackalloc VulkanRayTracingMotionNative.VkAccelerationStructureGeometryMotionTrianglesDataNV[Math.Max(geometryCount, 1)];

            for (int i = 0; i < geometryCount; ++i)
            {
                RHIAccelStructGeometry geom = descriptor.Geometries[i];

                if (geom.GeometryType == ERHIAccelStructGeometryType.Triangle)
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
                        flags = (geom.GeometryFlag & ERHIAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
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

                    RHIOpacityMicromapContract.ValidateTriangleAttachment(triangleGeometry);
                    RHIAccelStructMotionContract.ValidateTriangleAttachment(triangleGeometry);
                    void* triangleNext = null;
                    if (triangleGeometry.OpacityMicromap != null)
                    {
                        FillOpacityMicromapAttachment(
                            device,
                            i,
                            triangleGeometry,
                            &ommAttachments[i]);
                        triangleNext = &ommAttachments[i];
                    }

                    if (RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag) &&
                        RHIAccelStructMotionContract.HasMotionTriangles(triangleGeometry))
                    {
                        FillMotionTriangleAttachment(
                            triangleGeometry,
                            &motionAttachments[i]);
                        motionAttachments[i].pNext = triangleNext;
                        triangleNext = &motionAttachments[i];
                    }

                    geometries[i].geometry.triangles.pNext = triangleNext;
                }
                else if (geom.GeometryType == ERHIAccelStructGeometryType.AABB)
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
                        flags = (geom.GeometryFlag & ERHIAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
                    };
                    geometries[i].geometry.aabbs.sType = VkStructureType.AccelerationStructureGeometryAabbsDataKHR;
                    geometries[i].geometry.aabbs.data.deviceAddress = aabbAddress + aabbGeometry.Offset;
                    geometries[i].geometry.aabbs.stride = aabbGeometry.Stride;
                    maxPrimitiveCounts[i] = aabbGeometry.Count;
                }
                else if (geom.GeometryType == ERHIAccelStructGeometryType.Curves)
                {
                    RHIAccelStructCurves curveGeometry = geom as RHIAccelStructCurves
                        ?? throw new InvalidOperationException("Curve geometry descriptor type mismatch.");
                    VulkanBuffer controlPointBuffer = curveGeometry.ControlPointBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Curve geometry requires a Vulkan control-point buffer.");
                    VulkanBuffer radiusBuffer = curveGeometry.RadiusBuffer as VulkanBuffer
                        ?? throw new InvalidOperationException("Curve geometry requires a Vulkan radius buffer.");
                    VulkanBuffer? curveIndexBuffer = curveGeometry.IndexBuffer as VulkanBuffer;
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
                        flags = (geom.GeometryFlag & ERHIAccelStructGeometryFlag.Opaque) != 0 ? VkGeometryFlagsKHR.Opaque : 0,
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
                flags = VulkanAccelStructHelper.ComposeBottomLevelBuildFlags(descriptor),
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
            if (RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag))
            {
                createInfo.createFlags = VulkanRayTracingMotionNative.CreateMotionBit;
            }

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

        internal static void FillMotionTriangleAttachment(
            in RHIAccelStructTriangles triangleGeometry,
            VulkanRayTracingMotionNative.VkAccelerationStructureGeometryMotionTrianglesDataNV* attachment)
        {
            VulkanBuffer motionVertexBuffer = triangleGeometry.MotionVertexBuffer as VulkanBuffer
                ?? throw new InvalidOperationException("Vulkan motion triangles require a Vulkan MotionVertexBuffer.");
            RHIAccelStructMotionContract.ResolveMotionVertexStride(in triangleGeometry);
            *attachment = new VulkanRayTracingMotionNative.VkAccelerationStructureGeometryMotionTrianglesDataNV
            {
                sType = VulkanRayTracingMotionNative.GeometryMotionTrianglesStructureType,
                vertexData = new VkDeviceOrHostAddressConstKHR
                {
                    deviceAddress = motionVertexBuffer.GetNativeDeviceAddress() + triangleGeometry.MotionVertexOffset,
                },
            };
        }

        internal void FillOpacityMicromapAttachment(
            VulkanDevice device,
            in int geometryIndex,
            in RHIAccelStructTriangles triangleGeometry,
            VulkanOpacityMicromapNative.VkAccelerationStructureTrianglesOpacityMicromapEXT* attachment)
        {
            VulkanOpacityMicromap micromap = triangleGeometry.OpacityMicromap as VulkanOpacityMicromap
                ?? throw new InvalidOperationException("Vulkan triangle OMM attachment requires a VulkanOpacityMicromap.");

            VkIndexType indexType;
            ulong indexAddress;
            ulong indexStride;
            if (triangleGeometry.OpacityMicromapIndexBuffer != null)
            {
                VulkanBuffer indexBuffer = triangleGeometry.OpacityMicromapIndexBuffer as VulkanBuffer
                    ?? throw new InvalidOperationException("Vulkan OMM index buffer must be a VulkanBuffer.");
                indexType = VulkanUtility.ConvertToVkIndexType(triangleGeometry.OpacityMicromapIndexFormat);
                indexAddress = indexBuffer.GetNativeDeviceAddress() + triangleGeometry.OpacityMicromapIndexOffset;
                indexStride = triangleGeometry.OpacityMicromapIndexStride == 0
                    ? (indexType == VkIndexType.Uint16 ? 2UL : 4UL)
                    : triangleGeometry.OpacityMicromapIndexStride;
            }
            else
            {
                uint triangleCount = triangleGeometry.IndexBuffer != null
                    ? triangleGeometry.IndexCount / 3u
                    : triangleGeometry.VertexCount / 3u;
                if (triangleCount == 0)
                {
                    throw new InvalidOperationException("OMM special-index attachment requires at least one triangle.");
                }

                ulong bufferSize = triangleCount * sizeof(int);
                if (m_NativeOmmSpecialIndexBuffers[geometryIndex].Handle == 0)
                {
                    VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                        device,
                        bufferSize,
                        VkBufferUsageFlags.AccelerationStructureBuildInputReadOnlyKHR |
                        VulkanOpacityMicromapNative.MicromapBuildInputReadOnly |
                        VkBufferUsageFlags.ShaderDeviceAddress,
                        VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                        out m_NativeOmmSpecialIndexBuffers[geometryIndex],
                        out m_NativeOmmSpecialIndexMemories[geometryIndex]);

                    void* mapped = null;
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkMapMemory(
                            device.NativeDevice,
                            m_NativeOmmSpecialIndexMemories[geometryIndex],
                            0,
                            bufferSize,
                            0,
                            &mapped));
                    try
                    {
                        int specialIndex = (int)triangleGeometry.OpacityMicromapSpecialIndex;
                        int* indices = (int*)mapped;
                        for (uint i = 0; i < triangleCount; ++i)
                        {
                            indices[i] = specialIndex;
                        }
                    }
                    finally
                    {
                        VulkanNative.vkUnmapMemory(
                            device.NativeDevice,
                            m_NativeOmmSpecialIndexMemories[geometryIndex]);
                    }
                }

                indexType = VkIndexType.Uint32;
                VkBufferDeviceAddressInfo addressInfo = new()
                {
                    sType = VkStructureType.BufferDeviceAddressInfo,
                    buffer = m_NativeOmmSpecialIndexBuffers[geometryIndex],
                };
                indexAddress = VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &addressInfo);
                indexStride = sizeof(int);
            }

            RHIOpacityMicromapUsageCount[] usageCounts = micromap.UsageCounts;
            int usageCount = usageCounts.Length;
            if (m_NativeOmmUsageCounts[geometryIndex] == IntPtr.Zero)
            {
                nuint byteCount = (nuint)(usageCount * sizeof(VulkanOpacityMicromapNative.VkMicromapUsageEXT));
                m_NativeOmmUsageCounts[geometryIndex] = (IntPtr)NativeMemory.Alloc(byteCount);
                VulkanOpacityMicromapNative.VkMicromapUsageEXT* stored =
                    (VulkanOpacityMicromapNative.VkMicromapUsageEXT*)m_NativeOmmUsageCounts[geometryIndex];
                for (int i = 0; i < usageCount; ++i)
                {
                    stored[i] = new VulkanOpacityMicromapNative.VkMicromapUsageEXT
                    {
                        count = usageCounts[i].Count,
                        subdivisionLevel = usageCounts[i].SubdivisionLevel,
                        format = (uint)usageCounts[i].Format,
                    };
                }
            }

            *attachment = new VulkanOpacityMicromapNative.VkAccelerationStructureTrianglesOpacityMicromapEXT
            {
                sType = VulkanOpacityMicromapNative.TrianglesOpacityMicromapStructureType,
                indexType = indexType,
                indexBuffer = new VkDeviceOrHostAddressConstKHR { deviceAddress = indexAddress },
                indexStride = indexStride,
                baseTriangle = 0,
                usageCountsCount = (uint)usageCount,
                pUsageCounts = (VulkanOpacityMicromapNative.VkMicromapUsageEXT*)m_NativeOmmUsageCounts[geometryIndex],
                micromap = micromap.NativeMicromap,
            };
        }

        protected override void Release()
        {
            for (int i = 0; i < m_NativeOmmSpecialIndexBuffers.Length; ++i)
            {
                VkBuffer specialIndexBuffer = m_NativeOmmSpecialIndexBuffers[i];
                if (specialIndexBuffer.Handle != 0)
                {
                    VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, specialIndexBuffer, null);
                    m_NativeOmmSpecialIndexBuffers[i] = default;
                }

                VkDeviceMemory specialIndexMemory = m_NativeOmmSpecialIndexMemories[i];
                if (specialIndexMemory.Handle != 0)
                {
                    VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, specialIndexMemory, null);
                    m_NativeOmmSpecialIndexMemories[i] = default;
                }
            }

            for (int i = 0; i < m_NativeOmmUsageCounts.Length; ++i)
            {
                if (m_NativeOmmUsageCounts[i] != IntPtr.Zero)
                {
                    NativeMemory.Free((void*)m_NativeOmmUsageCounts[i]);
                    m_NativeOmmUsageCounts[i] = IntPtr.Zero;
                }
            }

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
        internal static VkBuildAccelerationStructureFlagsKHR ComposeTopLevelBuildFlags(
            in RHITopLevelAccelStructDescriptor descriptor)
        {
            VkBuildAccelerationStructureFlagsKHR flags =
                VkBuildAccelerationStructureFlagsKHR.PreferFastTrace |
                VkBuildAccelerationStructureFlagsKHR.AllowUpdate;
            if (RHIOpacityMicromapContract.AnyInstanceDisablesOmms(descriptor.Instances.Span))
            {
                flags |= VkBuildAccelerationStructureFlagsKHR.AllowDisableOpacityMicromapsEXT;
            }

            if (RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag))
            {
                flags |= VulkanRayTracingMotionNative.BuildMotionBit;
            }

            return flags;
        }

        internal static VkBuildAccelerationStructureFlagsKHR ComposeBottomLevelBuildFlags(
            in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            VkBuildAccelerationStructureFlagsKHR flags = VkBuildAccelerationStructureFlagsKHR.PreferFastTrace;
            if (RHIOpacityMicromapContract.BlasAllowsDisableOmms(descriptor))
            {
                flags |= VkBuildAccelerationStructureFlagsKHR.AllowDisableOpacityMicromapsEXT;
            }

            if (RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag))
            {
                flags |= VulkanRayTracingMotionNative.BuildMotionBit;
            }

            return flags;
        }

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
}
