using System;
using Vortice.Vulkan;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal static unsafe class VulkanOpacityMicromapNative
    {
        internal const string ExtensionName = "VK_EXT_opacity_micromap";
        // Values from vulkan_core.h (VK_EXT_opacity_micromap).
        internal const VkStructureType MicromapBuildInfoStructureType = (VkStructureType)1000396000;
        internal const VkStructureType CopyMicromapInfoStructureType = (VkStructureType)1000396002;
        internal const VkStructureType PhysicalDeviceFeaturesStructureType = (VkStructureType)1000396005;
        internal const VkStructureType MicromapCreateInfoStructureType = (VkStructureType)1000396007;
        internal const VkStructureType MicromapBuildSizesInfoStructureType = (VkStructureType)1000396008;
        internal const VkStructureType TrianglesOpacityMicromapStructureType = (VkStructureType)1000396009;
        internal const VkPipelineStageFlags2 MicromapBuildStage = (VkPipelineStageFlags2)0x40000000UL;
        internal const VkAccessFlags2 MicromapReadAccess = (VkAccessFlags2)0x100000000000UL;
        internal const VkAccessFlags2 MicromapWriteAccess = (VkAccessFlags2)0x200000000000UL;
        internal const VkBufferUsageFlags MicromapBuildInputReadOnly = (VkBufferUsageFlags)0x00800000;
        internal const VkBufferUsageFlags MicromapStorage = (VkBufferUsageFlags)0x01000000;
        internal const uint MicromapTypeOpacity = 0;
        internal const uint CopyModeCompact = 3;
        internal const uint BuildPreferFastTrace = 0x1;
        internal const uint BuildAllowCompaction = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkPhysicalDeviceOpacityMicromapFeaturesEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public VkBool32 micromap;
            public VkBool32 micromapCaptureReplay;
            public VkBool32 micromapHostCommands;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkMicromapEXT
        {
            public ulong Handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkMicromapUsageEXT
        {
            public uint count;
            public uint subdivisionLevel;
            public uint format;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkMicromapCreateInfoEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public uint createFlags;
            public VkBuffer buffer;
            public ulong offset;
            public ulong size;
            public uint type;
            public ulong deviceAddress;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkMicromapBuildInfoEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public uint type;
            public uint flags;
            public uint mode;
            public VkMicromapEXT dstMicromap;
            public uint usageCountsCount;
            public VkMicromapUsageEXT* pUsageCounts;
            public VkMicromapUsageEXT** ppUsageCounts;
            public VkDeviceOrHostAddressConstKHR data;
            public VkDeviceOrHostAddressKHR scratchData;
            public VkDeviceOrHostAddressConstKHR triangleArray;
            public ulong triangleArrayStride;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkMicromapBuildSizesInfoEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public ulong micromapSize;
            public ulong buildScratchSize;
            public VkBool32 discardable;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkCopyMicromapInfoEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public VkMicromapEXT src;
            public VkMicromapEXT dst;
            public uint mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct VkAccelerationStructureTrianglesOpacityMicromapEXT
        {
            public VkStructureType sType;
            public void* pNext;
            public VkIndexType indexType;
            public VkDeviceOrHostAddressConstKHR indexBuffer;
            public ulong indexStride;
            public uint baseTriangle;
            public uint usageCountsCount;
            public VkMicromapUsageEXT* pUsageCounts;
            public VkMicromapUsageEXT** ppUsageCounts;
            public VkMicromapEXT micromap;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate VkResult PFN_vkCreateMicromapEXT(
            VkDevice device,
            VkMicromapCreateInfoEXT* pCreateInfo,
            VkAllocationCallbacks* pAllocator,
            VkMicromapEXT* pMicromap);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PFN_vkDestroyMicromapEXT(
            VkDevice device,
            VkMicromapEXT micromap,
            VkAllocationCallbacks* pAllocator);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PFN_vkGetMicromapBuildSizesEXT(
            VkDevice device,
            VkAccelerationStructureBuildTypeKHR buildType,
            VkMicromapBuildInfoEXT* pBuildInfo,
            VkMicromapBuildSizesInfoEXT* pSizeInfo);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PFN_vkCmdBuildMicromapsEXT(
            VkCommandBuffer commandBuffer,
            uint infoCount,
            VkMicromapBuildInfoEXT* pInfos);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void PFN_vkCmdCopyMicromapEXT(
            VkCommandBuffer commandBuffer,
            VkCopyMicromapInfoEXT* pInfo);

        internal sealed class Api
        {
            public required PFN_vkCreateMicromapEXT Create;
            public required PFN_vkDestroyMicromapEXT Destroy;
            public required PFN_vkGetMicromapBuildSizesEXT GetBuildSizes;
            public required PFN_vkCmdBuildMicromapsEXT CmdBuild;
            public required PFN_vkCmdCopyMicromapEXT CmdCopy;
        }

        internal static bool TryLoad(VulkanDevice device, out Api? api, out string reason)
        {
            api = null;
            if (!TryLoadFunction(device, "vkCreateMicromapEXT", out PFN_vkCreateMicromapEXT? create, out reason) ||
                !TryLoadFunction(device, "vkDestroyMicromapEXT", out PFN_vkDestroyMicromapEXT? destroy, out reason) ||
                !TryLoadFunction(device, "vkGetMicromapBuildSizesEXT", out PFN_vkGetMicromapBuildSizesEXT? getSizes, out reason) ||
                !TryLoadFunction(device, "vkCmdBuildMicromapsEXT", out PFN_vkCmdBuildMicromapsEXT? cmdBuild, out reason) ||
                !TryLoadFunction(device, "vkCmdCopyMicromapEXT", out PFN_vkCmdCopyMicromapEXT? cmdCopy, out reason))
            {
                return false;
            }

            api = new Api
            {
                Create = create!,
                Destroy = destroy!,
                GetBuildSizes = getSizes!,
                CmdBuild = cmdBuild!,
                CmdCopy = cmdCopy!,
            };
            reason = string.Empty;
            return true;
        }

        private static bool TryLoadFunction<T>(
            VulkanDevice device,
            string name,
            out T? function,
            out string reason)
            where T : Delegate
        {
            function = null;
            IntPtr address = device.TryGetDeviceProcedure(name);
            if (address == IntPtr.Zero)
            {
                reason = $"{name} is not exported after enabling {ExtensionName}.";
                return false;
            }

            function = Marshal.GetDelegateForFunctionPointer<T>(address);
            reason = string.Empty;
            return true;
        }
    }

    internal unsafe class VulkanOpacityMicromap : RHIOpacityMicromap
    {
        public VulkanDevice Device => m_VulkanDevice;
        public VulkanOpacityMicromapNative.VkMicromapEXT NativeMicromap => m_NativeMicromap;
        public VkBuffer NativeScratchBuffer => m_NativeScratchBuffer;
        public RHIOpacityMicromapUsageCount[] UsageCounts => m_UsageCounts;

        private VulkanDevice m_VulkanDevice;
        private VulkanOpacityMicromapNative.VkMicromapEXT m_NativeMicromap;
        private VkBuffer m_NativeBuffer;
        private VkDeviceMemory m_NativeMemory;
        private VkBuffer m_NativeScratchBuffer;
        private VkDeviceMemory m_NativeScratchMemory;
        private RHIOpacityMicromapUsageCount[] m_UsageCounts;

        public VulkanOpacityMicromap(VulkanDevice device, in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_UsageCounts = (RHIOpacityMicromapUsageCount[])descriptor.UsageCounts.Clone();

            VulkanOpacityMicromapNative.VkMicromapBuildSizesInfoEXT sizes = QueryBuildSizes(device, in descriptor);
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                device,
                sizes.micromapSize,
                VulkanOpacityMicromapNative.MicromapStorage | VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
                out m_NativeBuffer,
                out m_NativeMemory);

            VulkanOpacityMicromapNative.VkMicromapCreateInfoEXT createInfo = new()
            {
                sType = VulkanOpacityMicromapNative.MicromapCreateInfoStructureType,
                buffer = m_NativeBuffer,
                size = sizes.micromapSize,
                type = VulkanOpacityMicromapNative.MicromapTypeOpacity,
            };
            VulkanOpacityMicromapNative.Api api = device.RequireOpacityMicromapApi();
            fixed (VulkanOpacityMicromapNative.VkMicromapEXT* micromapPtr = &m_NativeMicromap)
            {
                VulkanUtility.CheckErrors(api.Create(device.NativeDevice, &createInfo, null, micromapPtr));
            }

            VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                device,
                sizes.buildScratchSize,
                VkBufferUsageFlags.StorageBuffer | VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.DeviceLocal,
                out m_NativeScratchBuffer,
                out m_NativeScratchMemory);
        }

        public static RHIOpacityMicromapMemoryRequirements QueryMemoryRequirements(
            VulkanDevice device,
            in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            VulkanOpacityMicromapNative.VkMicromapBuildSizesInfoEXT sizes = QueryBuildSizes(device, in descriptor);
            return new RHIOpacityMicromapMemoryRequirements
            {
                ResultSizeInBytes = sizes.micromapSize,
                ScratchSizeInBytes = sizes.buildScratchSize,
                UpdateScratchSizeInBytes = 0,
            };
        }

        internal void FillBuildInfo(
            VulkanOpacityMicromapNative.VkMicromapUsageEXT* usageCounts,
            out VulkanOpacityMicromapNative.VkMicromapBuildInfoEXT buildInfo)
        {
            for (int i = 0; i < m_UsageCounts.Length; ++i)
            {
                usageCounts[i] = new VulkanOpacityMicromapNative.VkMicromapUsageEXT
                {
                    count = m_UsageCounts[i].Count,
                    subdivisionLevel = m_UsageCounts[i].SubdivisionLevel,
                    format = (uint)m_UsageCounts[i].Format,
                };
            }

            VulkanBuffer inputBuffer = m_Descriptor.InputBuffer as VulkanBuffer
                ?? throw new InvalidOperationException("Vulkan opacity micromap input buffer must be a VulkanBuffer.");
            VulkanBuffer triangleArrayBuffer = m_Descriptor.TriangleArrayBuffer as VulkanBuffer
                ?? throw new InvalidOperationException("Vulkan opacity micromap triangle-array buffer must be a VulkanBuffer.");

            buildInfo = new VulkanOpacityMicromapNative.VkMicromapBuildInfoEXT
            {
                sType = VulkanOpacityMicromapNative.MicromapBuildInfoStructureType,
                type = VulkanOpacityMicromapNative.MicromapTypeOpacity,
                flags = ComposeBuildFlags(in m_Descriptor),
                mode = 0,
                dstMicromap = m_NativeMicromap,
                usageCountsCount = (uint)m_UsageCounts.Length,
                pUsageCounts = usageCounts,
                data = new VkDeviceOrHostAddressConstKHR
                {
                    deviceAddress = inputBuffer.GetNativeDeviceAddress() + m_Descriptor.InputBufferOffset,
                },
                scratchData = new VkDeviceOrHostAddressKHR
                {
                    deviceAddress = GetBufferAddress(m_VulkanDevice, m_NativeScratchBuffer),
                },
                triangleArray = new VkDeviceOrHostAddressConstKHR
                {
                    deviceAddress = triangleArrayBuffer.GetNativeDeviceAddress() + m_Descriptor.TriangleArrayOffset,
                },
                triangleArrayStride = m_Descriptor.TriangleArrayStride == 0
                    ? 8UL
                    : m_Descriptor.TriangleArrayStride,
            };
        }

        private static uint ComposeBuildFlags(in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            uint flags = VulkanOpacityMicromapNative.BuildPreferFastTrace;
            if ((descriptor.Flag & ERHIAccelStructFlag.AllowCompaction) != 0)
            {
                flags |= VulkanOpacityMicromapNative.BuildAllowCompaction;
            }

            return flags;
        }

        private static VulkanOpacityMicromapNative.VkMicromapBuildSizesInfoEXT QueryBuildSizes(
            VulkanDevice device,
            in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            VulkanOpacityMicromapNative.Api api = device.RequireOpacityMicromapApi();
            VulkanOpacityMicromapNative.VkMicromapUsageEXT* usageCounts =
                stackalloc VulkanOpacityMicromapNative.VkMicromapUsageEXT[descriptor.UsageCounts.Length];
            for (int i = 0; i < descriptor.UsageCounts.Length; ++i)
            {
                usageCounts[i] = new VulkanOpacityMicromapNative.VkMicromapUsageEXT
                {
                    count = descriptor.UsageCounts[i].Count,
                    subdivisionLevel = descriptor.UsageCounts[i].SubdivisionLevel,
                    format = (uint)descriptor.UsageCounts[i].Format,
                };
            }

            VulkanOpacityMicromapNative.VkMicromapBuildInfoEXT buildInfo = new()
            {
                sType = VulkanOpacityMicromapNative.MicromapBuildInfoStructureType,
                type = VulkanOpacityMicromapNative.MicromapTypeOpacity,
                flags = ComposeBuildFlags(in descriptor),
                usageCountsCount = (uint)descriptor.UsageCounts.Length,
                pUsageCounts = usageCounts,
                triangleArrayStride = descriptor.TriangleArrayStride == 0
                    ? 8UL
                    : descriptor.TriangleArrayStride,
            };
            VulkanOpacityMicromapNative.VkMicromapBuildSizesInfoEXT sizes = new()
            {
                sType = VulkanOpacityMicromapNative.MicromapBuildSizesInfoStructureType,
            };
            api.GetBuildSizes(
                device.NativeDevice,
                VkAccelerationStructureBuildTypeKHR.Device,
                &buildInfo,
                &sizes);
            if (sizes.micromapSize == 0 || sizes.buildScratchSize == 0)
            {
                throw new InvalidOperationException(
                    "vkGetMicromapBuildSizesEXT returned a zero micromap or scratch size.");
            }

            return sizes;
        }

        private static ulong GetBufferAddress(VulkanDevice device, in VkBuffer buffer)
        {
            VkBufferDeviceAddressInfo info = new()
            {
                sType = VkStructureType.BufferDeviceAddressInfo,
                buffer = buffer,
            };
            return VulkanNative.vkGetBufferDeviceAddress(device.NativeDevice, &info);
        }

        protected override void Release()
        {
            if (m_NativeMicromap.Handle != 0)
            {
                m_VulkanDevice.RequireOpacityMicromapApi().Destroy(m_VulkanDevice.NativeDevice, m_NativeMicromap, null);
                m_NativeMicromap = default;
            }

            if (m_NativeBuffer.Handle != 0)
            {
                VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeBuffer, null);
                m_NativeBuffer = default;
            }

            if (m_NativeMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
                m_NativeMemory = default;
            }

            if (m_NativeScratchBuffer.Handle != 0)
            {
                VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_NativeScratchBuffer, null);
                m_NativeScratchBuffer = default;
            }

            if (m_NativeScratchMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeScratchMemory, null);
                m_NativeScratchMemory = default;
            }
        }
    }
}
