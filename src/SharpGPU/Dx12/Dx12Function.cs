using System;
using SharpGPU.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe class Dx12Function : RHIFunction
    {
        public Vortice.Direct3D12.ShaderBytecode NativeShaderBytecode
        {
            get
            {
                return m_NativeShaderBytecode;
            }
        }

        private Vortice.Direct3D12.ShaderBytecode m_NativeShaderBytecode;
        private IntPtr m_OwnedByteCode;

        public Dx12Function(in RHIFunctionDescriptor descriptor)
        {
            m_OwnedByteCode = CloneShaderByteCode(descriptor.ByteCode, descriptor.ByteSize, nameof(Dx12Function));
            m_Descriptor = descriptor;
            m_Descriptor.ByteCode = m_OwnedByteCode;
            m_NativeShaderBytecode = new Vortice.Direct3D12.ShaderBytecode(m_OwnedByteCode, checked((int)descriptor.ByteSize));
        }

        private static IntPtr CloneShaderByteCode(in IntPtr source, in uint byteSize, string context)
        {
            if (source == IntPtr.Zero)
            {
                throw new ArgumentException($"{context} received null shader bytecode pointer.", nameof(source));
            }

            if (byteSize == 0)
            {
                throw new ArgumentException($"{context} received zero-sized shader bytecode.", nameof(byteSize));
            }

            if (byteSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), $"{context} bytecode is larger than supported allocation size.");
            }

            IntPtr destination = Marshal.AllocHGlobal((int)byteSize);
            Buffer.MemoryCopy(source.ToPointer(), destination.ToPointer(), byteSize, byteSize);
            return destination;
        }

        protected override void Release()
        {
            if (m_OwnedByteCode != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_OwnedByteCode);
                m_OwnedByteCode = IntPtr.Zero;
            }

            m_NativeShaderBytecode = default;
        }
    }

    internal unsafe class Dx12FunctionLibrary : RHIFunctionLibrary
    {
        public Vortice.Direct3D12.ShaderBytecode NativeShaderBytecode
        {
            get
            {
                return m_NativeShaderBytecode;
            }
        }

        private Vortice.Direct3D12.ShaderBytecode m_NativeShaderBytecode;
        private IntPtr m_OwnedByteCode;

        public Dx12FunctionLibrary(in RHIFunctionLibraryDescriptor descriptor)
        {
            m_OwnedByteCode = CloneShaderByteCode(descriptor.ByteCode, descriptor.ByteSize, nameof(Dx12FunctionLibrary));
            m_Descriptor = descriptor;
            m_Descriptor.ByteCode = m_OwnedByteCode;
            m_NativeShaderBytecode = new Vortice.Direct3D12.ShaderBytecode(m_OwnedByteCode, checked((int)descriptor.ByteSize));
        }

        protected override void Release()
        {
            if (m_OwnedByteCode != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_OwnedByteCode);
                m_OwnedByteCode = IntPtr.Zero;
            }

            m_NativeShaderBytecode = default;
        }

        private static IntPtr CloneShaderByteCode(in IntPtr source, in uint byteSize, string context)
        {
            if (source == IntPtr.Zero)
            {
                throw new ArgumentException($"{context} received null shader bytecode pointer.", nameof(source));
            }

            if (byteSize == 0)
            {
                throw new ArgumentException($"{context} received zero-sized shader bytecode.", nameof(byteSize));
            }

            if (byteSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), $"{context} bytecode is larger than supported allocation size.");
            }

            IntPtr destination = Marshal.AllocHGlobal((int)byteSize);
            Buffer.MemoryCopy(source.ToPointer(), destination.ToPointer(), byteSize, byteSize);
            return destination;
        }
    }

    internal struct Dx12FunctionTableEntry
    {
        public int GroupIndex;
        public byte[] LocalData;

        public Dx12FunctionTableEntry(in int groupIndex, in byte[] localData)
        {
            GroupIndex = groupIndex;
            LocalData = localData;
        }
    }

    internal unsafe class Dx12FunctionTable : RHIFunctionTable
    {
        internal bool IsGenerated => m_NativeResource != null;
        public ulong RayGenSize => m_EntryStride;
        public ulong RayGeStride => m_EntryStride;
        public ulong RayGenAddress => m_NativeResource != null ? m_NativeResource.GPUVirtualAddress : 0;
        public ulong MissSize => (ulong)(m_EntryStride * m_MissPrograms.length);
        public ulong MissStride => m_EntryStride;
        public ulong MissAddress => m_NativeResource != null ? m_NativeResource.GPUVirtualAddress + m_EntryStride : 0;
        public ulong HitGroupSize => (ulong)(m_EntryStride * m_HitGroupPrograms.length);
        public ulong HitGroupStride => m_EntryStride;
        public ulong HitGroupAddress => m_NativeResource != null ? m_NativeResource.GPUVirtualAddress + (ulong)(m_EntryStride * (1 + m_MissPrograms.length)) : 0;
        public ulong CallableSize => (ulong)(m_EntryStride * m_CallablePrograms.length);
        public ulong CallableStride => m_EntryStride;
        public ulong CallableAddress => m_NativeResource != null ? m_NativeResource.GPUVirtualAddress + (ulong)(m_EntryStride * (1 + m_MissPrograms.length + m_HitGroupPrograms.length)) : 0;

        private uint m_EntryCount;
        private uint m_EntryStride;
        private uint m_LocalDataStrideInBytes;
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResource;
        private Dx12RaytracingPipeline m_CachedPipeline;
        private Dx12FunctionTableEntry m_RayGenerationProgram;
        private bool m_HasRayGenerationRecord;
        private TArray<Dx12FunctionTableEntry> m_MissPrograms;
        private TArray<Dx12FunctionTableEntry> m_HitGroupPrograms;
        private TArray<Dx12FunctionTableEntry> m_CallablePrograms;

        public Dx12FunctionTable(Dx12Device device)
        {
            m_Dx12Device = device;
            m_MissPrograms = new TArray<Dx12FunctionTableEntry>(2);
            m_HitGroupPrograms = new TArray<Dx12FunctionTableEntry>(8);
            m_CallablePrograms = new TArray<Dx12FunctionTableEntry>(2);
            m_RayGenerationProgram = default;
            m_HasRayGenerationRecord = false;
            m_NativeResource = null;
            m_CachedPipeline = null;
        }

        public override void SetRayGenerationRecord(in RHIRayRecordDescriptor record)
        {
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            m_RayGenerationProgram = CreateEntry(record);
            m_HasRayGenerationRecord = true;
        }

        public override int AddMissRecord(in RHIRayRecordDescriptor record)
        {
            Dx12FunctionTableEntry missEntry = CreateEntry(record);
            return m_MissPrograms.Add(missEntry);
        }

        public override int AddHitGroupRecord(in RHIRayRecordDescriptor record)
        {
            Dx12FunctionTableEntry hitGroupEntry = CreateEntry(record);
            return m_HitGroupPrograms.Add(hitGroupEntry);
        }

        public override int AddCallableRecord(in RHIRayRecordDescriptor record)
        {
            Dx12FunctionTableEntry callableEntry = CreateEntry(record);
            return m_CallablePrograms.Add(callableEntry);
        }

        public override void SetMissRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateEntryIndex(index, m_MissPrograms.length, nameof(index));
            m_MissPrograms[index] = CreateEntry(record);
        }

        public override void SetHitGroupRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateEntryIndex(index, m_HitGroupPrograms.length, nameof(index));
            m_HitGroupPrograms[index] = CreateEntry(record);
        }

        public override void SetCallableRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ValidateEntryIndex(index, m_CallablePrograms.length, nameof(index));
            m_CallablePrograms[index] = CreateEntry(record);
        }

        public override void ClearMissRecords()
        {
            m_MissPrograms.Clear();
        }

        public override void ClearHitGroupRecords()
        {
            m_HitGroupPrograms.Clear();
        }

        public override void ClearCallableRecords()
        {
            m_CallablePrograms.Clear();
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
                    if (m_NativeResource != null && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(m_CachedPipeline, section, index, m_RayGenerationProgram);
                    }
                    break;
                case ERHIRayShaderTableSection.Miss:
                    SetMissRecord(index, record);
                    if (m_NativeResource != null && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(m_CachedPipeline, section, index, m_MissPrograms[index]);
                    }
                    break;
                case ERHIRayShaderTableSection.Hit:
                    SetHitGroupRecord(index, record);
                    if (m_NativeResource != null && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(m_CachedPipeline, section, index, m_HitGroupPrograms[index]);
                    }
                    break;
                case ERHIRayShaderTableSection.Callable:
                    SetCallableRecord(index, record);
                    if (m_NativeResource != null && m_CachedPipeline != null)
                    {
                        WriteSingleRecord(m_CachedPipeline, section, index, m_CallablePrograms[index]);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section));
            }
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            Dx12RaytracingPipeline dx12RaytracingPipeline = pipeline as Dx12RaytracingPipeline
                ?? throw new ArgumentException("DX12 function table requires a Dx12RaytracingPipeline.", nameof(pipeline));

            if (!m_HasRayGenerationRecord)
            {
                throw new InvalidOperationException("Ray generation record is not set.");
            }

            m_CachedPipeline = dx12RaytracingPipeline;
            m_LocalDataStrideInBytes = dx12RaytracingPipeline.Descriptor.LocalDataStrideInBytes;
            ValidateAllRecords(dx12RaytracingPipeline);

            m_EntryCount = (uint)(1 + m_MissPrograms.length + m_HitGroupPrograms.length + m_CallablePrograms.length);
            m_EntryStride = RHIUtility.AlignTo(0x20, (uint)Vortice.Direct3D12.D3D12.ShaderIdentifierSizeInBytes + m_LocalDataStrideInBytes);

            ReleaseNativeResource();

            Vortice.Direct3D12.ID3D12Resource dx12Resource;
            Vortice.Direct3D12.ResourceDescription resourceDesc = Vortice.Direct3D12.ResourceDescription.Buffer(m_EntryCount * m_EntryStride, Vortice.Direct3D12.ResourceFlags.None);
            Vortice.Direct3D12.HeapProperties heapProperties = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Upload, 0, 0);
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                resourceDesc,
                Vortice.Direct3D12.ResourceStates.GenericRead,
                null,
                out dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeResource = dx12Resource;

            void* pTableData;
            hResult = m_NativeResource.Map(0, null, &pTableData);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif

            byte* tableData = (byte*)pTableData;
            WriteRecord(dx12RaytracingPipeline, tableData + 0 * m_EntryStride, ERHIRayShaderTableSection.RayGeneration, m_RayGenerationProgram);
            for (int i = 0; i < m_MissPrograms.length; ++i)
            {
                WriteRecord(dx12RaytracingPipeline, tableData + (1 + i) * m_EntryStride, ERHIRayShaderTableSection.Miss, m_MissPrograms[i]);
            }

            for (int i = 0; i < m_HitGroupPrograms.length; ++i)
            {
                WriteRecord(dx12RaytracingPipeline, tableData + (1 + m_MissPrograms.length + i) * m_EntryStride, ERHIRayShaderTableSection.Hit, m_HitGroupPrograms[i]);
            }

            for (int i = 0; i < m_CallablePrograms.length; ++i)
            {
                WriteRecord(dx12RaytracingPipeline, tableData + (1 + m_MissPrograms.length + m_HitGroupPrograms.length + i) * m_EntryStride, ERHIRayShaderTableSection.Callable, m_CallablePrograms[i]);
            }

            m_NativeResource.Unmap(0, null);
        }

        public override void Update()
        {
#if DEBUG
            System.Diagnostics.Debug.Assert(m_NativeResource != null, "SBT buffer not initialized. Call Generate() before Update().");
#endif
            if (m_NativeResource == null || m_CachedPipeline == null)
            {
                throw new InvalidOperationException("SBT buffer not initialized. Call Generate() before Update().");
            }

            void* pTableData;
            SharpGen.Runtime.Result hResult = m_NativeResource.Map(0, null, &pTableData);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            byte* tableData = (byte*)pTableData;
            WriteRecord(m_CachedPipeline, tableData + 0 * m_EntryStride, ERHIRayShaderTableSection.RayGeneration, m_RayGenerationProgram);
            for (int i = 0; i < m_MissPrograms.length; ++i)
            {
                WriteRecord(m_CachedPipeline, tableData + (1 + i) * m_EntryStride, ERHIRayShaderTableSection.Miss, m_MissPrograms[i]);
            }

            for (int i = 0; i < m_HitGroupPrograms.length; ++i)
            {
                WriteRecord(m_CachedPipeline, tableData + (1 + m_MissPrograms.length + i) * m_EntryStride, ERHIRayShaderTableSection.Hit, m_HitGroupPrograms[i]);
            }

            for (int i = 0; i < m_CallablePrograms.length; ++i)
            {
                WriteRecord(m_CachedPipeline, tableData + (1 + m_MissPrograms.length + m_HitGroupPrograms.length + i) * m_EntryStride, ERHIRayShaderTableSection.Callable, m_CallablePrograms[i]);
            }

            m_NativeResource.Unmap(0, null);
        }

        protected override void Release()
        {
            ReleaseNativeResource();
        }

        private Dx12FunctionTableEntry CreateEntry(in RHIRayRecordDescriptor record)
        {
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            byte[] localData = CloneLocalData(record.LocalData);
            return new Dx12FunctionTableEntry(record.GroupIndex, localData);
        }

        private void ValidateAllRecords(Dx12RaytracingPipeline pipeline)
        {
            ValidateRecordAgainstSection(pipeline, ERHIRayShaderTableSection.RayGeneration, m_RayGenerationProgram);
            for (int i = 0; i < m_MissPrograms.length; ++i)
            {
                ValidateRecordAgainstSection(pipeline, ERHIRayShaderTableSection.Miss, m_MissPrograms[i]);
            }

            for (int i = 0; i < m_HitGroupPrograms.length; ++i)
            {
                ValidateRecordAgainstSection(pipeline, ERHIRayShaderTableSection.Hit, m_HitGroupPrograms[i]);
            }

            for (int i = 0; i < m_CallablePrograms.length; ++i)
            {
                ValidateRecordAgainstSection(pipeline, ERHIRayShaderTableSection.Callable, m_CallablePrograms[i]);
            }
        }

        private void ValidateRecordAgainstSection(Dx12RaytracingPipeline pipeline, ERHIRayShaderTableSection section, in Dx12FunctionTableEntry entry)
        {
            if ((uint)entry.LocalData.Length > m_LocalDataStrideInBytes)
            {
                throw new InvalidOperationException($"Local data ({entry.LocalData.Length} bytes) exceeds LocalDataStrideInBytes ({m_LocalDataStrideInBytes}).");
            }

            int sectionCount = GetSectionGroupCount(pipeline, section);
            if ((uint)entry.GroupIndex >= (uint)sectionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(entry.GroupIndex), $"Group index {entry.GroupIndex} is out of range for {section} section (count={sectionCount}).");
            }
        }

        private static int GetSectionGroupCount(Dx12RaytracingPipeline pipeline, ERHIRayShaderTableSection section)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => 1,
                ERHIRayShaderTableSection.Miss => pipeline.Descriptor.RayMissGroups.Length,
                ERHIRayShaderTableSection.Hit => pipeline.Descriptor.RayHitGroups.Length,
                ERHIRayShaderTableSection.Callable => pipeline.Descriptor.RayCallableGroups.Length,
                _ => 0,
            };
        }

        private void WriteSingleRecord(Dx12RaytracingPipeline pipeline, ERHIRayShaderTableSection section, in int index, in Dx12FunctionTableEntry entry)
        {
            void* pTableData;
            SharpGen.Runtime.Result hResult = m_NativeResource.Map(0, null, &pTableData);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            int absoluteIndex = GetAbsoluteEntryIndex(section, index);
            byte* destination = (byte*)pTableData + absoluteIndex * m_EntryStride;
            WriteRecord(pipeline, destination, section, entry);
            m_NativeResource.Unmap(0, null);
        }

        private int GetAbsoluteEntryIndex(ERHIRayShaderTableSection section, in int sectionIndex)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => 0,
                ERHIRayShaderTableSection.Miss => 1 + sectionIndex,
                ERHIRayShaderTableSection.Hit => 1 + m_MissPrograms.length + sectionIndex,
                ERHIRayShaderTableSection.Callable => 1 + m_MissPrograms.length + m_HitGroupPrograms.length + sectionIndex,
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };
        }

        private void WriteRecord(Dx12RaytracingPipeline pipeline, byte* destination, ERHIRayShaderTableSection section, in Dx12FunctionTableEntry entry)
        {
            ValidateRecordAgainstSection(pipeline, section, entry);
            Unsafe.InitBlock(destination, 0, m_EntryStride);

            string exportName = pipeline.GetExportName(section, entry.GroupIndex);
            void* shaderIdentifier = pipeline.NativeStateObjectProperties.GetShaderIdentifier(exportName).ToPointer();
            if (shaderIdentifier == null)
            {
                throw new InvalidOperationException($"Failed to resolve shader identifier for export '{exportName}'.");
            }

            Unsafe.CopyBlock(destination, shaderIdentifier, (uint)Vortice.Direct3D12.D3D12.ShaderIdentifierSizeInBytes);

            if (entry.LocalData.Length > 0)
            {
                fixed (byte* localDataPtr = entry.LocalData)
                {
                    Unsafe.CopyBlock(destination + Vortice.Direct3D12.D3D12.ShaderIdentifierSizeInBytes, localDataPtr, (uint)entry.LocalData.Length);
                }
            }
        }

        private static void ValidateGroupIndex(in int groupIndex, string parameterName)
        {
            if (groupIndex < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "GroupIndex must be non-negative.");
            }
        }

        private static void ValidateEntryIndex(in int index, in int count, string parameterName)
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

        private void ReleaseNativeResource()
        {
            if (m_NativeResource != null)
            {
                m_NativeResource.Release();
                m_NativeResource = null;
            }
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
