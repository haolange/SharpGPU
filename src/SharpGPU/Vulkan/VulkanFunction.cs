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
        private VulkanDevice m_VulkanDevice;

        public VulkanFunctionTable(VulkanDevice device)
        {
            m_VulkanDevice = device;
        }

        public override void SetRayGenerationProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            // Stub: raytracing SBT not yet fully implemented
        }

        public override int AddMissProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            return 0;
        }

        public override int AddHitGroupProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            return 0;
        }

        public override void SetMissProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
        }

        public override void SetHitGroupProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
        }

        public override void ClearMissPrograms()
        {
        }

        public override void ClearHitGroupPrograms()
        {
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
        }

        public override void Update(RHIRaytracingPipeline pipeline)
        {
        }

        protected override void Release()
        {
        }
    }
#pragma warning restore CS8618
}
