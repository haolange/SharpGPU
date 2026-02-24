using System;
using Evergine.Bindings.Vulkan;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanFunction : RHIFunction
    {
        public VkShaderModule NativeShaderModule => m_NativeShaderModule;

        private VulkanDevice m_VulkanDevice;
        private VkShaderModule m_NativeShaderModule;

        public VulkanFunction(VulkanDevice device, in RHIFunctionDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            VkShaderModuleCreateInfo createInfo = new VkShaderModuleCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,
                codeSize = (nuint)descriptor.ByteSize,
                pCode = (uint*)descriptor.ByteCode,
            };

            fixed (VkShaderModule* modulePtr = &m_NativeShaderModule)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateShaderModule(device.NativeDevice, &createInfo, null, modulePtr));
            }
        }

        public VkPipelineShaderStageCreateInfo GetShaderStageCreateInfo()
        {
            return new VkPipelineShaderStageCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO,
                stage = VulkanUtility.ConvertToVkShaderStageBit(m_Descriptor.Type),
                module = m_NativeShaderModule,
                pName = m_Descriptor.EntryName.ToPointer(),
            };
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyShaderModule(m_VulkanDevice.NativeDevice, m_NativeShaderModule, null);
        }
    }

    internal unsafe class VulkanFunctionLibrary : RHIFunctionLibrary
    {
        public VkShaderModule NativeShaderModule => m_NativeShaderModule;

        private VulkanDevice m_VulkanDevice;
        private VkShaderModule m_NativeShaderModule;

        public VulkanFunctionLibrary(VulkanDevice device, in RHIFunctionLibraryDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            VkShaderModuleCreateInfo createInfo = new VkShaderModuleCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO,
                codeSize = (nuint)descriptor.ByteSize,
                pCode = (uint*)descriptor.ByteCode,
            };

            fixed (VkShaderModule* modulePtr = &m_NativeShaderModule)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateShaderModule(device.NativeDevice, &createInfo, null, modulePtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyShaderModule(m_VulkanDevice.NativeDevice, m_NativeShaderModule, null);
        }
    }

    internal unsafe class VulkanFunctionTable : RHIFunctionTable
    {
        public VkStridedDeviceAddressRegionKHR RayGenRegion => m_RayGenRegion;
        public VkStridedDeviceAddressRegionKHR MissRegion => m_MissRegion;
        public VkStridedDeviceAddressRegionKHR HitGroupRegion => m_HitGroupRegion;
        public VkStridedDeviceAddressRegionKHR CallableRegion => m_CallableRegion;

        private VulkanDevice m_VulkanDevice;
        private VkBuffer m_SbtBuffer;
        private VkDeviceMemory m_SbtMemory;
        private VkStridedDeviceAddressRegionKHR m_RayGenRegion;
        private VkStridedDeviceAddressRegionKHR m_MissRegion;
        private VkStridedDeviceAddressRegionKHR m_HitGroupRegion;
        private VkStridedDeviceAddressRegionKHR m_CallableRegion;

        private int m_RayGenGroupIndex;
        private List<int> m_MissGroupIndices;
        private List<int> m_HitGroupIndices;

        public VulkanFunctionTable(VulkanDevice device)
        {
            m_VulkanDevice = device;
            m_RayGenGroupIndex = 0;
            m_MissGroupIndices = new List<int>(2);
            m_HitGroupIndices = new List<int>(8);
            m_CallableRegion = default;
        }

        public override void SetRayGenerationProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            m_RayGenGroupIndex = 0; // Raygen is always group 0
        }

        public override int AddMissProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            int index = m_MissGroupIndices.Count;
            m_MissGroupIndices.Add(1 + index); // Miss groups start after raygen
            return index;
        }

        public override int AddHitGroupProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            int index = m_HitGroupIndices.Count;
            m_HitGroupIndices.Add(1 + m_MissGroupIndices.Count + index); // Hit groups start after raygen + miss
            return index;
        }

        public override void SetMissProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            m_MissGroupIndices[index] = 1 + index;
        }

        public override void SetHitGroupProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            m_HitGroupIndices[index] = 1 + m_MissGroupIndices.Count + index;
        }

        public override void ClearMissPrograms()
        {
            m_MissGroupIndices.Clear();
        }

        public override void ClearHitGroupPrograms()
        {
            m_HitGroupIndices.Clear();
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            BuildSBT(pipeline);
        }

        public override void Update(RHIRaytracingPipeline pipeline)
        {
            // Release old SBT and rebuild
            ReleaseSBT();
            BuildSBT(pipeline);
        }

        protected override void Release()
        {
            ReleaseSBT();
        }

        private void BuildSBT(RHIRaytracingPipeline pipeline)
        {
            VulkanRaytracingPipeline vkPipeline = pipeline as VulkanRaytracingPipeline;

            // Query physical device properties for alignment requirements
            VkPhysicalDeviceRayTracingPipelinePropertiesKHR rtProperties = new VkPhysicalDeviceRayTracingPipelinePropertiesKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_RAY_TRACING_PIPELINE_PROPERTIES_KHR,
            };
            VkPhysicalDeviceProperties2 properties2 = new VkPhysicalDeviceProperties2()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2,
                pNext = &rtProperties,
            };
            VulkanNative.vkGetPhysicalDeviceProperties2(m_VulkanDevice.NativePhysicalDevice, &properties2);

            uint handleSize = rtProperties.shaderGroupHandleSize;
            uint handleAlignment = rtProperties.shaderGroupHandleAlignment;
            uint baseAlignment = rtProperties.shaderGroupBaseAlignment;
            uint alignedHandleSize = AlignUp(handleSize, handleAlignment);

            uint groupCount = vkPipeline.ShaderGroupCount;

            // Get all shader group handles
            uint handleStorageSize = groupCount * handleSize;
            byte* handles = stackalloc byte[(int)handleStorageSize];
            VulkanUtility.CheckErrors(VulkanNative.vkGetRayTracingShaderGroupHandlesKHR(
                m_VulkanDevice.NativeDevice, vkPipeline.NativePipeline, 0, groupCount, (nuint)handleStorageSize, handles));

            // Calculate region sizes
            uint missCount = (uint)m_MissGroupIndices.Count;
            uint hitGroupCount = (uint)m_HitGroupIndices.Count;

            ulong rayGenSize = AlignUp(alignedHandleSize, baseAlignment);
            ulong missSize = AlignUp(missCount * alignedHandleSize, baseAlignment);
            ulong hitGroupSize = AlignUp(hitGroupCount * alignedHandleSize, baseAlignment);
            ulong totalSize = rayGenSize + missSize + hitGroupSize;

            // Allocate SBT buffer
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(m_VulkanDevice, totalSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_BINDING_TABLE_BIT_KHR | VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                out m_SbtBuffer, out m_SbtMemory);

            // Map and write handles
            void* pData;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory, 0, totalSize, 0, &pData));
            byte* dst = (byte*)pData;

            // Raygen
            Buffer.MemoryCopy(handles + m_RayGenGroupIndex * handleSize, dst, handleSize, handleSize);
            dst += rayGenSize;

            // Miss
            for (int i = 0; i < missCount; ++i)
            {
                Buffer.MemoryCopy(handles + m_MissGroupIndices[i] * handleSize, dst + i * alignedHandleSize, handleSize, handleSize);
            }
            dst += missSize;

            // Hit groups
            for (int i = 0; i < hitGroupCount; ++i)
            {
                Buffer.MemoryCopy(handles + m_HitGroupIndices[i] * handleSize, dst + i * alignedHandleSize, handleSize, handleSize);
            }

            VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory);

            // Get device address
            VkBufferDeviceAddressInfo addrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                buffer = m_SbtBuffer,
            };
            ulong sbtAddress = VulkanNative.vkGetBufferDeviceAddress(m_VulkanDevice.NativeDevice, &addrInfo);

            // Setup regions
            m_RayGenRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = sbtAddress,
                stride = rayGenSize,
                size = rayGenSize,
            };

            m_MissRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = sbtAddress + rayGenSize,
                stride = alignedHandleSize,
                size = missSize,
            };

            m_HitGroupRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = sbtAddress + rayGenSize + missSize,
                stride = alignedHandleSize,
                size = hitGroupSize,
            };

            m_CallableRegion = default;
        }

        private void ReleaseSBT()
        {
            if (m_SbtBuffer.Handle != 0)
            {
                VulkanNative.vkDestroyBuffer(m_VulkanDevice.NativeDevice, m_SbtBuffer, null);
                m_SbtBuffer = default;
            }
            if (m_SbtMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_SbtMemory, null);
                m_SbtMemory = default;
            }
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static ulong AlignUp(ulong value, uint alignment)
        {
            return (value + alignment - 1) & ~((ulong)alignment - 1);
        }
    }
#pragma warning restore CS8618
}
