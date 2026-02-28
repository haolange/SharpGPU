using System;
using System.Collections.Generic;
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
        private struct VulkanSbtRecord
        {
            public int GroupIndex;
            public byte[] LocalData;

            public VulkanSbtRecord(in int groupIndex, in byte[] localData)
            {
                GroupIndex = groupIndex;
                LocalData = localData;
            }
        }

        public VkStridedDeviceAddressRegionKHR RayGenRegion => m_RayGenRegion;
        public VkStridedDeviceAddressRegionKHR MissRegion => m_MissRegion;
        public VkStridedDeviceAddressRegionKHR HitGroupRegion => m_HitGroupRegion;
        public VkStridedDeviceAddressRegionKHR CallableRegion => m_CallableRegion;

        private readonly VulkanDevice m_VulkanDevice;
        private readonly List<VulkanSbtRecord> m_MissRecords;
        private readonly List<VulkanSbtRecord> m_HitRecords;
        private readonly List<VulkanSbtRecord> m_CallableRecords;

        private VulkanRaytracingPipeline m_CachedPipeline;
        private VulkanSbtRecord m_RayGenerationRecord;
        private bool m_HasRayGenerationRecord;

        private VkBuffer m_SbtBuffer;
        private VkDeviceMemory m_SbtMemory;
        private VkStridedDeviceAddressRegionKHR m_RayGenRegion;
        private VkStridedDeviceAddressRegionKHR m_MissRegion;
        private VkStridedDeviceAddressRegionKHR m_HitGroupRegion;
        private VkStridedDeviceAddressRegionKHR m_CallableRegion;

        private byte[] m_GroupHandles;
        private uint m_HandleSize;
        private uint m_HandleAlignment;
        private uint m_BaseAlignment;
        private uint m_AlignedHandleSize;
        private uint m_EntryStride;
        private uint m_LocalDataStrideInBytes;
        private ulong m_RayGenRegionOffset;
        private ulong m_MissRegionOffset;
        private ulong m_HitRegionOffset;
        private ulong m_CallableRegionOffset;
        private ulong m_TotalSbtSize;

        public VulkanFunctionTable(VulkanDevice device)
        {
            m_VulkanDevice = device;
            m_MissRecords = new List<VulkanSbtRecord>(2);
            m_HitRecords = new List<VulkanSbtRecord>(8);
            m_CallableRecords = new List<VulkanSbtRecord>(2);

            m_CachedPipeline = null;
            m_RayGenerationRecord = default;
            m_HasRayGenerationRecord = false;
            m_SbtBuffer = default;
            m_SbtMemory = default;
            m_RayGenRegion = default;
            m_MissRegion = default;
            m_HitGroupRegion = default;
            m_CallableRegion = default;
            m_GroupHandles = Array.Empty<byte>();
        }

        public override void SetRayGenerationRecord(in RHIRayRecordDescriptor record)
        {
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            m_RayGenerationRecord = CreateRecord(record);
            m_HasRayGenerationRecord = true;
        }

        public override int AddMissRecord(in RHIRayRecordDescriptor record)
        {
            VulkanSbtRecord entry = CreateRecord(record);
            m_MissRecords.Add(entry);
            return m_MissRecords.Count - 1;
        }

        public override int AddHitGroupRecord(in RHIRayRecordDescriptor record)
        {
            VulkanSbtRecord entry = CreateRecord(record);
            m_HitRecords.Add(entry);
            return m_HitRecords.Count - 1;
        }

        public override int AddCallableRecord(in RHIRayRecordDescriptor record)
        {
            VulkanSbtRecord entry = CreateRecord(record);
            m_CallableRecords.Add(entry);
            return m_CallableRecords.Count - 1;
        }

        public override void SetMissRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateRecordIndex(index, m_MissRecords.Count, nameof(index));
            m_MissRecords[index] = CreateRecord(record);
        }

        public override void SetHitGroupRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateRecordIndex(index, m_HitRecords.Count, nameof(index));
            m_HitRecords[index] = CreateRecord(record);
        }

        public override void SetCallableRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateRecordIndex(index, m_CallableRecords.Count, nameof(index));
            m_CallableRecords[index] = CreateRecord(record);
        }

        public override void ClearMissRecords()
        {
            m_MissRecords.Clear();
        }

        public override void ClearHitGroupRecords()
        {
            m_HitRecords.Clear();
        }

        public override void ClearCallableRecords()
        {
            m_CallableRecords.Clear();
        }

        public override void UpdateRecord(in ERHIRayShaderTableSection section, in int index, in RHIRayRecordDescriptor record)
        {
            switch (section)
            {
                case ERHIRayShaderTableSection.RayGeneration:
                    if (index != 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index), "RayGeneration section only supports index 0.");
                    }

                    SetRayGenerationRecord(record);
                    if (m_SbtBuffer.Handle != 0 && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(section, 0, m_RayGenerationRecord);
                    }
                    break;

                case ERHIRayShaderTableSection.Miss:
                    SetMissRecord(index, record);
                    if (m_SbtBuffer.Handle != 0 && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(section, index, m_MissRecords[index]);
                    }
                    break;

                case ERHIRayShaderTableSection.Hit:
                    SetHitGroupRecord(index, record);
                    if (m_SbtBuffer.Handle != 0 && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(section, index, m_HitRecords[index]);
                    }
                    break;

                case ERHIRayShaderTableSection.Callable:
                    SetCallableRecord(index, record);
                    if (m_SbtBuffer.Handle != 0 && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(section, index, m_CallableRecords[index]);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(section));
            }
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            VulkanRaytracingPipeline vkPipeline = pipeline as VulkanRaytracingPipeline
                ?? throw new ArgumentException("Vulkan function table requires a VulkanRaytracingPipeline.", nameof(pipeline));
            m_CachedPipeline = vkPipeline;
            RebuildSbt();
        }

        public override void Update()
        {
            if (m_CachedPipeline == null)
            {
                throw new InvalidOperationException("Function table has not been generated yet.");
            }

            RebuildSbt();
        }

        protected override void Release()
        {
            ReleaseSBT();
        }

        private VulkanSbtRecord CreateRecord(in RHIRayRecordDescriptor record)
        {
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            return new VulkanSbtRecord(record.GroupIndex, CloneLocalData(record.LocalData));
        }

        private void RebuildSbt()
        {
            if (!m_HasRayGenerationRecord)
            {
                throw new InvalidOperationException("Ray generation record is not set.");
            }

            QueryRtProperties(out m_HandleSize, out m_HandleAlignment, out m_BaseAlignment);
            m_AlignedHandleSize = AlignUp(m_HandleSize, m_HandleAlignment);
            m_LocalDataStrideInBytes = m_CachedPipeline.Descriptor.LocalDataStrideInBytes;
            m_EntryStride = AlignUp(m_AlignedHandleSize + m_LocalDataStrideInBytes, m_HandleAlignment);

            ValidateAllRecords();
            FetchShaderGroupHandles();

            ulong rayGenSize = (ulong)m_EntryStride;
            ulong missUsedSize = (ulong)m_EntryStride * (ulong)m_MissRecords.Count;
            ulong hitUsedSize = (ulong)m_EntryStride * (ulong)m_HitRecords.Count;
            ulong callableUsedSize = (ulong)m_EntryStride * (ulong)m_CallableRecords.Count;

            ulong rayGenRegionSizeAligned = AlignUp(rayGenSize, m_BaseAlignment);
            ulong missRegionSizeAligned = AlignUp(missUsedSize, m_BaseAlignment);
            ulong hitRegionSizeAligned = AlignUp(hitUsedSize, m_BaseAlignment);
            ulong callableRegionSizeAligned = AlignUp(callableUsedSize, m_BaseAlignment);

            m_RayGenRegionOffset = 0;
            m_MissRegionOffset = m_RayGenRegionOffset + rayGenRegionSizeAligned;
            m_HitRegionOffset = m_MissRegionOffset + missRegionSizeAligned;
            m_CallableRegionOffset = m_HitRegionOffset + hitRegionSizeAligned;
            m_TotalSbtSize = m_CallableRegionOffset + callableRegionSizeAligned;
            if (m_TotalSbtSize == 0)
            {
                m_TotalSbtSize = rayGenRegionSizeAligned;
            }

            ReleaseSBT();
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                m_VulkanDevice,
                m_TotalSbtSize,
                VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_BINDING_TABLE_BIT_KHR | VkBufferUsageFlags.VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT,
                VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT,
                out m_SbtBuffer,
                out m_SbtMemory);

            void* mapped;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory, 0, m_TotalSbtSize, 0, &mapped));
            try
            {
                byte* basePtr = (byte*)mapped;
                UnsafeFill(basePtr, 0, m_TotalSbtSize);

                WriteRecordToMemory(basePtr + m_RayGenRegionOffset + 0 * m_EntryStride, ERHIRayShaderTableSection.RayGeneration, m_RayGenerationRecord);
                for (int i = 0; i < m_MissRecords.Count; ++i)
                {
                    WriteRecordToMemory(basePtr + m_MissRegionOffset + (ulong)i * m_EntryStride, ERHIRayShaderTableSection.Miss, m_MissRecords[i]);
                }

                for (int i = 0; i < m_HitRecords.Count; ++i)
                {
                    WriteRecordToMemory(basePtr + m_HitRegionOffset + (ulong)i * m_EntryStride, ERHIRayShaderTableSection.Hit, m_HitRecords[i]);
                }

                for (int i = 0; i < m_CallableRecords.Count; ++i)
                {
                    WriteRecordToMemory(basePtr + m_CallableRegionOffset + (ulong)i * m_EntryStride, ERHIRayShaderTableSection.Callable, m_CallableRecords[i]);
                }
            }
            finally
            {
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory);
            }

            VkBufferDeviceAddressInfo addrInfo = new VkBufferDeviceAddressInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_BUFFER_DEVICE_ADDRESS_INFO,
                buffer = m_SbtBuffer,
            };
            ulong baseAddress = VulkanNative.vkGetBufferDeviceAddress(m_VulkanDevice.NativeDevice, &addrInfo);

            m_RayGenRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = baseAddress + m_RayGenRegionOffset,
                stride = m_EntryStride,
                size = rayGenSize,
            };
            m_MissRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = baseAddress + m_MissRegionOffset,
                stride = m_EntryStride,
                size = missUsedSize,
            };
            m_HitGroupRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = baseAddress + m_HitRegionOffset,
                stride = m_EntryStride,
                size = hitUsedSize,
            };
            m_CallableRegion = new VkStridedDeviceAddressRegionKHR()
            {
                deviceAddress = baseAddress + m_CallableRegionOffset,
                stride = m_EntryStride,
                size = callableUsedSize,
            };
        }

        private void FetchShaderGroupHandles()
        {
            uint groupCount = m_CachedPipeline.ShaderGroupCount;
            uint handleStorageSize = groupCount * m_HandleSize;
            m_GroupHandles = new byte[handleStorageSize];
            fixed (byte* handlesPtr = m_GroupHandles)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkGetRayTracingShaderGroupHandlesKHR(
                    m_VulkanDevice.NativeDevice,
                    m_CachedPipeline.NativePipeline,
                    0,
                    groupCount,
                    (nuint)handleStorageSize,
                    handlesPtr));
            }
        }

        private void QueryRtProperties(out uint handleSize, out uint handleAlignment, out uint baseAlignment)
        {
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

            handleSize = rtProperties.shaderGroupHandleSize;
            handleAlignment = rtProperties.shaderGroupHandleAlignment;
            baseAlignment = rtProperties.shaderGroupBaseAlignment;
        }

        private void ValidateAllRecords()
        {
            ValidateRecordAgainstSection(ERHIRayShaderTableSection.RayGeneration, m_RayGenerationRecord);
            for (int i = 0; i < m_MissRecords.Count; ++i)
            {
                ValidateRecordAgainstSection(ERHIRayShaderTableSection.Miss, m_MissRecords[i]);
            }

            for (int i = 0; i < m_HitRecords.Count; ++i)
            {
                ValidateRecordAgainstSection(ERHIRayShaderTableSection.Hit, m_HitRecords[i]);
            }

            for (int i = 0; i < m_CallableRecords.Count; ++i)
            {
                ValidateRecordAgainstSection(ERHIRayShaderTableSection.Callable, m_CallableRecords[i]);
            }
        }

        private void ValidateRecordAgainstSection(ERHIRayShaderTableSection section, in VulkanSbtRecord record)
        {
            if ((uint)record.LocalData.Length > m_LocalDataStrideInBytes)
            {
                throw new InvalidOperationException($"Local data ({record.LocalData.Length} bytes) exceeds LocalDataStrideInBytes ({m_LocalDataStrideInBytes}).");
            }

            int sectionCount = GetSectionGroupCount(section);
            if ((uint)record.GroupIndex >= (uint)sectionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(record.GroupIndex), $"Group index {record.GroupIndex} is out of range for {section} section (count={sectionCount}).");
            }
        }

        private int GetSectionGroupCount(in ERHIRayShaderTableSection section)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => m_CachedPipeline.RayGenerationGroupCount,
                ERHIRayShaderTableSection.Miss => m_CachedPipeline.MissGroupCount,
                ERHIRayShaderTableSection.Hit => m_CachedPipeline.HitGroupCount,
                ERHIRayShaderTableSection.Callable => m_CachedPipeline.CallableGroupCount,
                _ => 0,
            };
        }

        private uint GetAbsoluteGroupIndex(in ERHIRayShaderTableSection section, in int sectionGroupIndex)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => (uint)(m_CachedPipeline.RayGenerationGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Miss => (uint)(m_CachedPipeline.MissGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Hit => (uint)(m_CachedPipeline.HitGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Callable => (uint)(m_CachedPipeline.CallableGroupBase + sectionGroupIndex),
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };
        }

        private void WriteSingleRecord(in ERHIRayShaderTableSection section, in int sectionIndex, in VulkanSbtRecord record)
        {
            ValidateRecordAgainstSection(section, record);

            void* mapped;
            VulkanUtility.CheckErrors(VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory, 0, m_TotalSbtSize, 0, &mapped));
            try
            {
                ulong sectionOffset = GetSectionOffset(section);
                byte* dst = (byte*)mapped + sectionOffset + (ulong)sectionIndex * m_EntryStride;
                WriteRecordToMemory(dst, section, record);
            }
            finally
            {
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, m_SbtMemory);
            }
        }

        private ulong GetSectionOffset(in ERHIRayShaderTableSection section)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => m_RayGenRegionOffset,
                ERHIRayShaderTableSection.Miss => m_MissRegionOffset,
                ERHIRayShaderTableSection.Hit => m_HitRegionOffset,
                ERHIRayShaderTableSection.Callable => m_CallableRegionOffset,
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };
        }

        private void WriteRecordToMemory(byte* destination, in ERHIRayShaderTableSection section, in VulkanSbtRecord record)
        {
            UnsafeFill(destination, 0, m_EntryStride);
            uint absoluteGroupIndex = GetAbsoluteGroupIndex(section, record.GroupIndex);
            int handleByteOffset = checked((int)(absoluteGroupIndex * m_HandleSize));

            fixed (byte* handlesPtr = m_GroupHandles)
            {
                Buffer.MemoryCopy(handlesPtr + handleByteOffset, destination, m_HandleSize, m_HandleSize);
            }

            if (record.LocalData.Length > 0)
            {
                fixed (byte* localPtr = record.LocalData)
                {
                    Buffer.MemoryCopy(localPtr, destination + m_AlignedHandleSize, record.LocalData.Length, record.LocalData.Length);
                }
            }
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

            m_RayGenRegion = default;
            m_MissRegion = default;
            m_HitGroupRegion = default;
            m_CallableRegion = default;
            m_TotalSbtSize = 0;
        }

        private static void ValidateGroupIndex(in int groupIndex, string parameterName)
        {
            if (groupIndex < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "GroupIndex must be non-negative.");
            }
        }

        private static void ValidateRecordIndex(in int index, in int count, string parameterName)
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static byte[] CloneLocalData(in ReadOnlyMemory<byte> localData)
        {
            if (localData.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            byte[] cloned = new byte[localData.Length];
            localData.Span.CopyTo(cloned);
            return cloned;
        }

        private static uint AlignUp(uint value, uint alignment)
        {
            if (alignment == 0)
            {
                return value;
            }

            return (value + alignment - 1) & ~(alignment - 1);
        }

        private static ulong AlignUp(ulong value, uint alignment)
        {
            if (alignment == 0)
            {
                return value;
            }

            return (value + alignment - 1) & ~((ulong)alignment - 1);
        }

        private static void UnsafeFill(byte* ptr, byte value, ulong byteCount)
        {
            for (ulong i = 0; i < byteCount; ++i)
            {
                ptr[i] = value;
            }
        }
    }
#pragma warning restore CS8618
}
