using System;
using Vortice.Vulkan;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal static unsafe class VulkanRayTracingMotionNative
    {
        internal const string ExtensionName = "VK_NV_ray_tracing_motion_blur";
        // Values from vulkan_core.h (VK_NV_ray_tracing_motion_blur).
        internal const VkStructureType GeometryMotionTrianglesStructureType = (VkStructureType)1000327000;
        internal const VkStructureType PhysicalDeviceFeaturesStructureType = (VkStructureType)1000327001;
        internal const VkStructureType MotionInfoStructureType = (VkStructureType)1000327002;
        internal const VkBuildAccelerationStructureFlagsKHR BuildMotionBit =
            (VkBuildAccelerationStructureFlagsKHR)0x00000020;
        internal const VkAccelerationStructureCreateFlagsKHR CreateMotionBit =
            (VkAccelerationStructureCreateFlagsKHR)0x00000004;
        internal const VkPipelineCreateFlags PipelineAllowMotionBit =
            (VkPipelineCreateFlags)0x00100000;
        internal const uint MotionInstanceTypeStatic = 0;
        internal const uint MotionInstanceTypeMatrix = 1;
        internal const uint MotionInstanceTypeSrt = 2;
        // Spec array stride is 160. The C layout without trailing padding is 152.
        internal const int MotionInstanceByteCount = 160;

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkPhysicalDeviceRayTracingMotionBlurFeaturesNV
        {
            public VkStructureType sType;
            public void* pNext;
            public VkBool32 rayTracingMotionBlur;
            public VkBool32 rayTracingMotionBlurPipelineTraceRaysIndirect;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureMotionInfoNV
        {
            public VkStructureType sType;
            public void* pNext;
            public uint maxInstances;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureGeometryMotionTrianglesDataNV
        {
            public VkStructureType sType;
            public void* pNext;
            public VkDeviceOrHostAddressConstKHR vertexData;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureMatrixMotionInstanceNV
        {
            public fixed float transformT0[12];
            public fixed float transformT1[12];
            public uint instanceCustomIndexAndMask;
            public uint instanceSbtRecordOffsetAndFlags;
            public ulong accelerationStructureReference;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkSRTDataNV
        {
            public float sx;
            public float a;
            public float b;
            public float pvx;
            public float sy;
            public float c;
            public float pvy;
            public float sz;
            public float pvz;
            public float qx;
            public float qy;
            public float qz;
            public float qw;
            public float tx;
            public float ty;
            public float tz;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureSRTMotionInstanceNV
        {
            public VkSRTDataNV transformT0;
            public VkSRTDataNV transformT1;
            public uint instanceCustomIndexAndMask;
            public uint instanceSbtRecordOffsetAndFlags;
            public ulong accelerationStructureReference;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureInstanceRaw
        {
            public fixed float transform[12];
            public uint instanceCustomIndexAndMask;
            public uint instanceSbtRecordOffsetAndFlags;
            public ulong accelerationStructureReference;
        }

        [StructLayout(LayoutKind.Explicit, Size = 144)]
        internal struct VkAccelerationStructureMotionInstanceDataNV
        {
            [FieldOffset(0)]
            public VkAccelerationStructureInstanceRaw staticInstance;
            [FieldOffset(0)]
            public VkAccelerationStructureMatrixMotionInstanceNV matrixMotionInstance;
            [FieldOffset(0)]
            public VkAccelerationStructureSRTMotionInstanceNV srtMotionInstance;
        }

        [StructLayout(LayoutKind.Sequential, Size = 160)]
        internal struct VkAccelerationStructureMotionInstanceNV
        {
            public uint type;
            public uint flags;
            public VkAccelerationStructureMotionInstanceDataNV data;
            public ulong arrayStridePadding;
        }

        internal static bool TryLoad(VulkanDevice device, out string reason)
        {
            if (device.TryGetDeviceProcedure("vkGetAccelerationStructureBuildSizesKHR") == IntPtr.Zero)
            {
                reason =
                    "vkGetAccelerationStructureBuildSizesKHR is not exported after enabling VK_NV_ray_tracing_motion_blur.";
                return false;
            }

            if (device.TryGetDeviceProcedure("vkCmdBuildAccelerationStructuresKHR") == IntPtr.Zero)
            {
                reason =
                    "vkCmdBuildAccelerationStructuresKHR is not exported after enabling VK_NV_ray_tracing_motion_blur.";
                return false;
            }

            if (device.TryGetDeviceProcedure("vkCreateAccelerationStructureKHR") == IntPtr.Zero)
            {
                reason =
                    "vkCreateAccelerationStructureKHR is not exported after enabling VK_NV_ray_tracing_motion_blur.";
                return false;
            }

            if (device.TryGetDeviceProcedure("vkCreateRayTracingPipelinesKHR") == IntPtr.Zero)
            {
                reason =
                    "vkCreateRayTracingPipelinesKHR is not exported after enabling VK_NV_ray_tracing_motion_blur.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
