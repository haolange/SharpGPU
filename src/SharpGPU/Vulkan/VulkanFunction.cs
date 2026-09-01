using System;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal unsafe class VulkanFunction : RHIFunction
    {
        public VkShaderModule NativeShaderModule => m_NativeShaderModule;
        internal VulkanDevice VulkanDevice => m_VulkanDevice;
        internal ReadOnlySpan<byte> Bytecode => m_Bytecode;

        private VulkanDevice m_VulkanDevice;
        private VkShaderModule m_NativeShaderModule;
        private readonly byte[] m_Bytecode;

        public VulkanFunction(VulkanDevice device, in RHIFunctionDescriptor descriptor)
        {
            m_VulkanDevice = device;
            BindDirectBytecodeSource(descriptor);
            if (descriptor.ByteCode == IntPtr.Zero || descriptor.ByteSize == 0)
            {
                throw new ArgumentException(
                    "Vulkan shader bytecode is empty.",
                    nameof(descriptor));
            }
            int byteCount = checked((int)descriptor.ByteSize);
            m_Bytecode = new byte[byteCount];
            Marshal.Copy(descriptor.ByteCode, m_Bytecode, 0, byteCount);
            m_OwnsNativeModule = true;
            CreateOwnedModule(device, m_Bytecode);
        }

        internal VulkanFunction(
            VulkanFunctionLibrary library,
            in RHIFunctionViewDescriptor view)
        {
            library.ThrowIfViewSourceUnavailable();
            library.ValidateViewEntry(view);
            m_VulkanDevice = library.VulkanDevice;
            m_SourceLibrary = library;
            m_NativeShaderModule = library.NativeShaderModule;
            m_OwnsNativeModule = false;
            m_Bytecode = Array.Empty<byte>();
            BindLibraryViewSource(library, view, library.Descriptor.PayloadKind, library.ContentDigest);
        }

        private VulkanFunctionLibrary? m_SourceLibrary;
        private bool m_OwnsNativeModule;

        public VkPipelineShaderStageCreateInfo GetShaderStageCreateInfo()
        {
            ThrowIfViewSourceUnavailable();
            return new VkPipelineShaderStageCreateInfo()
            {
                sType = VkStructureType.PipelineShaderStageCreateInfo,
                stage = VulkanUtility.ConvertToVkShaderStageBit(m_Descriptor.Type),
                module = m_NativeShaderModule,
                pName = m_Descriptor.EntryName.ToPointer(),
            };
        }

        internal void ThrowIfViewSourceUnavailable()
        {
            ThrowIfSourceUnavailable();
            if (m_SourceLibrary != null && m_SourceLibrary.IsDisposed)
            {
                throw new ObjectDisposedException(
                    m_SourceLibrary.GetType().FullName,
                    "The function library that owns this view has been disposed.");
            }
        }

        private void CreateOwnedModule(VulkanDevice device, byte[] bytecode)
        {
            fixed (byte* bytecodePointer = bytecode)
            {
                VkShaderModuleCreateInfo createInfo = new VkShaderModuleCreateInfo()
                {
                    sType = VkStructureType.ShaderModuleCreateInfo,
                    codeSize = (nuint)bytecode.Length,
                    pCode = (uint*)bytecodePointer,
                };

                fixed (VkShaderModule* modulePtr = &m_NativeShaderModule)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreateShaderModule(
                            device.NativeDevice,
                            &createInfo,
                            null,
                            modulePtr));
                }
            }
        }

        protected override void Release()
        {
            if (m_OwnsNativeModule && m_NativeShaderModule.Handle != 0)
            {
                VulkanNative.vkDestroyShaderModule(m_VulkanDevice.NativeDevice, m_NativeShaderModule, null);
            }

            m_NativeShaderModule = default;
            m_OwnsNativeModule = false;
        }
    }

    internal unsafe class VulkanFunctionLibrary : RHIFunctionLibrary
    {
        public VkShaderModule NativeShaderModule => m_NativeShaderModule;
        internal VulkanDevice VulkanDevice => m_VulkanDevice;
        internal ReadOnlySpan<byte> Bytecode => m_Bytecode;

        private VulkanDevice m_VulkanDevice;
        private VkShaderModule m_NativeShaderModule;
        private readonly byte[] m_Bytecode;

        public VulkanFunctionLibrary(VulkanDevice device, in RHIFunctionLibraryDescriptor descriptor)
        {
            m_VulkanDevice = device;
            if (!device.Capabilities.FunctionLibrary.SupportsPayloadKind(descriptor.PayloadKind))
            {
                throw new NotSupportedException(
                    $"Vulkan function libraries require SpirV payload, received {descriptor.PayloadKind}.");
            }
            if (descriptor.ByteCode == IntPtr.Zero || descriptor.ByteSize == 0)
            {
                throw new ArgumentException(
                    "Vulkan shader bytecode is empty.",
                    nameof(descriptor));
            }
            int byteCount = checked((int)descriptor.ByteSize);
            m_Bytecode = new byte[byteCount];
            Marshal.Copy(descriptor.ByteCode, m_Bytecode, 0, byteCount);
            BindLibraryPayload(descriptor);

            fixed (byte* bytecode = m_Bytecode)
            {
                VkShaderModuleCreateInfo createInfo = new VkShaderModuleCreateInfo()
                {
                    sType = VkStructureType.ShaderModuleCreateInfo,
                    codeSize = (nuint)m_Bytecode.Length,
                    pCode = (uint*)bytecode,
                };

                fixed (VkShaderModule* modulePtr = &m_NativeShaderModule)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreateShaderModule(
                            device.NativeDevice,
                            &createInfo,
                            null,
                            modulePtr));
                }
            }
        }

        internal void ThrowIfViewSourceUnavailable()
        {
            ThrowIfDisposed();
        }

        internal void ValidateViewEntry(in RHIFunctionViewDescriptor view)
        {
            ThrowIfDisposed();
            VulkanSpirvEntryPoint.Require(
                m_Bytecode,
                view.EntryName,
                view.Type);
        }

        public override RHIFunction CreateFunction(in RHIFunctionViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            m_VulkanDevice.Capabilities.FunctionLibrary.NativeLibrary.Require(
                "FunctionLibrary.NativeLibrary");
            ERHIFunctionLibraryReusablePipelineClass pipelineClass =
                m_VulkanDevice.Capabilities.FunctionLibrary.ClassifyFunctionType(descriptor.Type);
            m_VulkanDevice.Capabilities.FunctionLibrary.RequireReusableClass(
                pipelineClass,
                $"Vulkan function-library views for {pipelineClass}");
            return new VulkanFunction(this, descriptor);
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
        internal bool IsGenerated =>
            m_SbtBuffer.Handle != 0 &&
            m_RayGenRegion.deviceAddress != 0 &&
            m_CachedPipeline != null;
        internal VulkanDevice Device => m_VulkanDevice;
        internal VulkanRaytracingPipeline? GeneratedPipeline => m_CachedPipeline;

        private readonly VulkanDevice m_VulkanDevice;
        private readonly List<VulkanSbtRecord> m_MissRecords;
        private readonly List<VulkanSbtRecord> m_HitRecords;
        private readonly List<VulkanSbtRecord> m_CallableRecords;

        private VulkanRaytracingPipeline? m_CachedPipeline;
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
            ThrowIfDisposed();
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            m_RayGenerationRecord = CreateRecord(record);
            m_HasRayGenerationRecord = true;
        }

        public override int AddMissRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            VulkanSbtRecord entry = CreateRecord(record);
            m_MissRecords.Add(entry);
            return m_MissRecords.Count - 1;
        }

        public override int AddHitGroupRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            VulkanSbtRecord entry = CreateRecord(record);
            m_HitRecords.Add(entry);
            return m_HitRecords.Count - 1;
        }

        public override int AddCallableRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            VulkanSbtRecord entry = CreateRecord(record);
            m_CallableRecords.Add(entry);
            return m_CallableRecords.Count - 1;
        }

        public override void SetMissRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_MissRecords.Count, nameof(index));
            m_MissRecords[index] = CreateRecord(record);
        }

        public override void SetHitGroupRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_HitRecords.Count, nameof(index));
            m_HitRecords[index] = CreateRecord(record);
        }

        public override void SetCallableRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_CallableRecords.Count, nameof(index));
            m_CallableRecords[index] = CreateRecord(record);
        }

        public override void ClearMissRecords()
        {
            ThrowIfDisposed();
            m_MissRecords.Clear();
        }

        public override void ClearHitGroupRecords()
        {
            ThrowIfDisposed();
            m_HitRecords.Clear();
        }

        public override void ClearCallableRecords()
        {
            ThrowIfDisposed();
            m_CallableRecords.Clear();
        }

        public override void UpdateRecord(in ERHIRayShaderTableSection section, in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
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
            ThrowIfDisposed();
            VulkanRaytracingPipeline vkPipeline = pipeline as VulkanRaytracingPipeline
                ?? throw new ArgumentException("Vulkan function table requires a VulkanRaytracingPipeline.", nameof(pipeline));
            m_CachedPipeline = vkPipeline;
            RebuildSbt();
        }

        public override void Update()
        {
            ThrowIfDisposed();
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

        private VulkanRaytracingPipeline RequireCachedPipeline()
        {
            return m_CachedPipeline
                ?? throw new InvalidOperationException("Function table has not been generated yet.");
        }

        private void RebuildSbt()
        {
            if (!m_HasRayGenerationRecord)
            {
                throw new InvalidOperationException("Ray generation record is not set.");
            }

            VulkanRaytracingPipeline pipeline = RequireCachedPipeline();
            QueryRtProperties(out m_HandleSize, out m_HandleAlignment, out m_BaseAlignment);
            m_AlignedHandleSize = AlignUp(m_HandleSize, m_HandleAlignment);
            m_LocalDataStrideInBytes = pipeline.Descriptor.LocalDataStrideInBytes;
            m_EntryStride = AlignUp(m_AlignedHandleSize + m_LocalDataStrideInBytes, m_HandleAlignment);
            if (m_EntryStride == 0)
            {
                throw new InvalidOperationException(
                    $"Invalid Vulkan SBT entry stride (0). handleSize={m_HandleSize}, handleAlignment={m_HandleAlignment}, localDataStride={m_LocalDataStrideInBytes}.");
            }

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
            ulong totalSbtSize = m_CallableRegionOffset + callableRegionSizeAligned;
            if (totalSbtSize == 0)
            {
                totalSbtSize = rayGenRegionSizeAligned;
            }

            if (totalSbtSize == 0)
            {
                throw new InvalidOperationException("Invalid Vulkan SBT total size (0).");
            }

            ReleaseSBT();
            m_TotalSbtSize = totalSbtSize;
            VulkanAccelStructHelper.CreateDeviceAddressBuffer(
                m_VulkanDevice,
                m_TotalSbtSize,
                VkBufferUsageFlags.ShaderBindingTableKHR | VkBufferUsageFlags.ShaderDeviceAddress,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
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
                sType = VkStructureType.BufferDeviceAddressInfo,
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
            VulkanRaytracingPipeline pipeline = RequireCachedPipeline();
            uint groupCount = pipeline.ShaderGroupCount;
            uint handleStorageSize = groupCount * m_HandleSize;
            m_GroupHandles = new byte[handleStorageSize];
            fixed (byte* handlesPtr = m_GroupHandles)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkGetRayTracingShaderGroupHandlesKHR(
                    m_VulkanDevice.NativeDevice,
                    pipeline.NativePipeline,
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
                sType = VkStructureType.PhysicalDeviceRayTracingPipelinePropertiesKHR,
            };
            VkPhysicalDeviceProperties2 properties2 = new VkPhysicalDeviceProperties2()
            {
                sType = VkStructureType.PhysicalDeviceProperties2,
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
            VulkanRaytracingPipeline pipeline = RequireCachedPipeline();
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => pipeline.RayGenerationGroupCount,
                ERHIRayShaderTableSection.Miss => pipeline.MissGroupCount,
                ERHIRayShaderTableSection.Hit => pipeline.HitGroupCount,
                ERHIRayShaderTableSection.Callable => pipeline.CallableGroupCount,
                _ => 0,
            };
        }

        private uint GetAbsoluteGroupIndex(in ERHIRayShaderTableSection section, in int sectionGroupIndex)
        {
            VulkanRaytracingPipeline pipeline = RequireCachedPipeline();
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => (uint)(pipeline.RayGenerationGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Miss => (uint)(pipeline.MissGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Hit => (uint)(pipeline.HitGroupBase + sectionGroupIndex),
                ERHIRayShaderTableSection.Callable => (uint)(pipeline.CallableGroupBase + sectionGroupIndex),
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

    internal static class VulkanSpirvEntryPoint
    {
        private const uint Magic = 0x07230203;
        private const uint OpEntryPoint = 15;
        private const uint ExecutionModelVertex = 0;
        private const uint ExecutionModelFragment = 4;
        private const uint ExecutionModelGLCompute = 5;
        private const uint ExecutionModelTaskNV = 5267;
        private const uint ExecutionModelMeshNV = 5268;
        private const uint ExecutionModelRayGenerationKHR = 5313;
        private const uint ExecutionModelIntersectionKHR = 5314;
        private const uint ExecutionModelAnyHitKHR = 5315;
        private const uint ExecutionModelClosestHitKHR = 5316;
        private const uint ExecutionModelMissKHR = 5317;
        private const uint ExecutionModelCallableKHR = 5318;
        private const uint ExecutionModelTaskEXT = 5364;
        private const uint ExecutionModelMeshEXT = 5365;

        public static void Require(
            ReadOnlySpan<byte> spirv,
            string entryName,
            ERHIFunctionType type)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new ArgumentException("Vulkan function view entry name is empty.", nameof(entryName));
            }
            if (spirv.Length < 20 || (spirv.Length & 3) != 0)
            {
                throw new ArgumentException("Vulkan function library payload is not valid SPIR-V.");
            }

            ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(spirv);
            if (words[0] != Magic)
            {
                throw new ArgumentException("Vulkan function library payload is not SPIR-V.");
            }

            bool found = false;
            uint matchedModel = 0;
            int index = 5;
            while (index < words.Length)
            {
                uint header = words[index];
                int wordCount = (int)(header >> 16);
                uint opcode = header & 0xffff;
                if (wordCount == 0 || index + wordCount > words.Length)
                {
                    throw new ArgumentException("SPIR-V instruction stream is truncated.");
                }

                if (opcode == OpEntryPoint && wordCount >= 3)
                {
                    uint model = words[index + 1];
                    string name = ReadString(words.Slice(index + 3, wordCount - 3));
                    if (string.Equals(name, entryName, StringComparison.Ordinal))
                    {
                        found = true;
                        matchedModel = model;
                        break;
                    }
                }

                index += wordCount;
            }

            if (!found)
            {
                throw new InvalidOperationException(
                    $"SPIR-V entry '{entryName}' is not present in the function library.");
            }

            if (!Matches(type, matchedModel))
            {
                throw new InvalidOperationException(
                    $"SPIR-V entry '{entryName}' execution model {matchedModel} does not match stage {type}.");
            }
        }

        private static bool Matches(ERHIFunctionType type, uint model)
        {
            return type switch
            {
                ERHIFunctionType.Vertex => model == ExecutionModelVertex,
                ERHIFunctionType.Fragment => model == ExecutionModelFragment,
                ERHIFunctionType.Compute => model == ExecutionModelGLCompute,
                ERHIFunctionType.Task =>
                    model == ExecutionModelTaskEXT || model == ExecutionModelTaskNV,
                ERHIFunctionType.Mesh =>
                    model == ExecutionModelMeshEXT || model == ExecutionModelMeshNV,
                ERHIFunctionType.RayTracing =>
                    model == ExecutionModelRayGenerationKHR ||
                    model == ExecutionModelIntersectionKHR ||
                    model == ExecutionModelAnyHitKHR ||
                    model == ExecutionModelClosestHitKHR ||
                    model == ExecutionModelMissKHR ||
                    model == ExecutionModelCallableKHR,
                _ => false,
            };
        }

        private static string ReadString(ReadOnlySpan<uint> words)
        {
            if (words.IsEmpty)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[words.Length * 4];
            MemoryMarshal.AsBytes(words).CopyTo(bytes);
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0)
            {
                length = bytes.Length;
            }

            return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
        }
    }
}


