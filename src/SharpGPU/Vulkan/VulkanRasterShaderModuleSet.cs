using System;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe sealed class VulkanRasterShaderModuleSet :
        IDisposable
    {
        private readonly VulkanDevice m_Device;
        private readonly VkShaderModule[] m_Modules;
        private readonly VkShaderStageFlags[] m_Stages;
        private readonly IntPtr[] m_EntryNames;
        private bool m_Disposed;

        private VulkanRasterShaderModuleSet(
            VulkanDevice device,
            int stageCount)
        {
            m_Device = device;
            m_Modules = new VkShaderModule[stageCount];
            m_Stages = new VkShaderStageFlags[stageCount];
            m_EntryNames = new IntPtr[stageCount];
        }

        internal int StageCount => m_Modules.Length;

        internal static VulkanRasterShaderModuleSet Create(
            VulkanDevice device,
            in RHIRasterPipelineDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            int stageCount = 0;
            if (descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                ++stageCount;
            }
            if (descriptor.PrimitiveAssembler.MeshletAssembler.HasValue)
            {
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .Value.TaskFunction != null)
                {
                    ++stageCount;
                }
                ++stageCount;
            }
            if (descriptor.FragmentFunction != null)
            {
                ++stageCount;
            }
            if (stageCount == 0)
            {
                throw new ArgumentException(
                    "A Vulkan raster pipeline requires at least one shader stage.",
                    nameof(descriptor));
            }

            VulkanRasterShaderModuleSet result =
                new(device, stageCount);
            try
            {
                int index = 0;
                if (descriptor.PrimitiveAssembler.VertexAssembler
                    .HasValue)
                {
                    result.Add(
                        index++,
                        descriptor.PrimitiveAssembler.VertexAssembler
                            .Value.VertexFunction);
                }
                if (descriptor.PrimitiveAssembler.MeshletAssembler
                    .HasValue)
                {
                    RHIMeshletAssemblerDescriptor meshlet =
                        descriptor.PrimitiveAssembler.MeshletAssembler
                            .Value;
                    if (meshlet.TaskFunction != null)
                    {
                        result.Add(index++, meshlet.TaskFunction);
                    }
                    if (meshlet.MeshFunction == null)
                    {
                        throw new ArgumentException(
                            "Vulkan mesh pipeline requires a mesh shader function.",
                            nameof(descriptor));
                    }
                    result.Add(index++, meshlet.MeshFunction);
                }
                if (descriptor.FragmentFunction != null)
                {
                    result.Add(index, descriptor.FragmentFunction);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        internal void Populate(
            VkPipelineShaderStageCreateInfo* stages)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }
            for (int index = 0; index < m_Modules.Length; ++index)
            {
                stages[index] =
                    new VkPipelineShaderStageCreateInfo
                    {
                        sType = VkStructureType
                            .PipelineShaderStageCreateInfo,
                        stage = m_Stages[index],
                        module = m_Modules[index],
                        pName = (byte*)m_EntryNames[index],
                    };
            }
        }

        private void Add(int index, RHIFunction function)
        {
            ArgumentNullException.ThrowIfNull(function);
            if (function.IsDisposed)
            {
                throw new ObjectDisposedException(
                    function.GetType().FullName);
            }
            VulkanFunction vulkanFunction =
                function as VulkanFunction ??
                throw new ArgumentException(
                    "Vulkan raster pipelines require Vulkan shader functions.",
                    nameof(function));
            if (!ReferenceEquals(vulkanFunction.VulkanDevice, m_Device))
            {
                throw new ArgumentException(
                    "Vulkan shader function belongs to another device.",
                    nameof(function));
            }
            RHIFunctionDescriptor descriptor = function.Descriptor;
            if (descriptor.PayloadKind !=
                ERHIShaderPayloadKind.SpirV)
            {
                throw new ArgumentException(
                    "Vulkan raster shaders require SPIR-V payloads.",
                    nameof(function));
            }
            ReadOnlySpan<byte> bytecode = vulkanFunction.Bytecode;
            if (bytecode.IsEmpty)
            {
                throw new ArgumentException(
                    "Vulkan raster shader bytecode is empty.",
                    nameof(function));
            }

            VkShaderModule module = default;
            fixed (byte* bytecodePointer = bytecode)
            {
                VkShaderModuleCreateInfo createInfo = new()
                {
                    sType = VkStructureType.ShaderModuleCreateInfo,
                    codeSize = (nuint)bytecode.Length,
                    pCode = (uint*)bytecodePointer,
                };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateShaderModule(
                        m_Device.NativeDevice,
                        &createInfo,
                        null,
                        &module));
            }
            m_Modules[index] = module;
            m_Stages[index] =
                VulkanUtility.ConvertToVkShaderStageBit(
                    descriptor.Type);
            m_EntryNames[index] =
                Marshal.StringToCoTaskMemUTF8(
                    descriptor.EntryName ??
                    throw new ArgumentException(
                        "Vulkan shader entry name is null.",
                        nameof(function)));
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            for (int index = m_Modules.Length - 1;
                 index >= 0;
                 --index)
            {
                if (m_Modules[index].Handle != 0)
                {
                    VulkanNative.vkDestroyShaderModule(
                        m_Device.NativeDevice,
                        m_Modules[index],
                        null);
                    m_Modules[index] = default;
                }
                if (m_EntryNames[index] != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(
                        m_EntryNames[index]);
                    m_EntryNames[index] = IntPtr.Zero;
                }
            }
            m_Disposed = true;
        }
    }
}
