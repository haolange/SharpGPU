using System;
using System.Runtime.CompilerServices;
using Infinity.Collections;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe class Dx12Function : RHIFunction
    {
        public D3D12_SHADER_BYTECODE NativeShaderBytecode
        {
            get
            {
                return m_NativeShaderBytecode;
            }
        }

        private D3D12_SHADER_BYTECODE m_NativeShaderBytecode;

        public Dx12Function(in RHIFunctionDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_NativeShaderBytecode = new D3D12_SHADER_BYTECODE(descriptor.ByteCode.ToPointer(), new UIntPtr(descriptor.ByteSize));
        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12FunctionLibrary : RHIFunctionLibrary
    {
        public D3D12_SHADER_BYTECODE NativeShaderBytecode
        {
            get
            {
                return m_NativeShaderBytecode;
            }
        }

        private D3D12_SHADER_BYTECODE m_NativeShaderBytecode;

        public Dx12FunctionLibrary(in RHIFunctionLibraryDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_NativeShaderBytecode = new D3D12_SHADER_BYTECODE(descriptor.ByteCode.ToPointer(), new UIntPtr(descriptor.ByteSize));
        }

        protected override void Release()
        {

        }
    }

    internal struct Dx12FunctionTableEntry
    {
        public string ShaderIdentifier;
        public RHIBindTable[]? BindTables;
    };

    internal unsafe class Dx12FunctionTable : RHIFunctionTable
    {
        public ulong RayGenSize => m_EntryStride * 1;
        public ulong RayGeStride => m_EntryStride;
        public ulong RayGenAddress => m_NativeResource->GetGPUVirtualAddress();
        public ulong MissSize => (ulong)(m_EntryStride * m_MissPrograms.length);
        public ulong MissStride => m_EntryStride;
        public ulong MissAddress => m_NativeResource->GetGPUVirtualAddress() + m_EntryStride;
        public ulong HitGroupSize => (ulong)(m_EntryStride * m_HitGroupPrograms.length);
        public ulong HitGroupStride => m_EntryStride;
        public ulong HitGroupAddress => m_NativeResource->GetGPUVirtualAddress() + (ulong)(m_EntryStride * m_MissPrograms.length);

        private uint m_EntryCount;
        private uint m_EntryStride;
        private Dx12Device m_Dx12Device;
        private ID3D12Resource* m_NativeResource;
        private Dx12FunctionTableEntry m_RayGenerationProgram;
        private TArray<Dx12FunctionTableEntry> m_MissPrograms;
        private TArray<Dx12FunctionTableEntry> m_HitGroupPrograms;

        public Dx12FunctionTable(Dx12Device device)
        {
            m_Dx12Device = device;
            m_MissPrograms = new TArray<Dx12FunctionTableEntry>(2);
            m_HitGroupPrograms = new TArray<Dx12FunctionTableEntry>(8);
        }

        public override void SetRayGenerationProgram(string exportName, RHIBindTable[]? bindTables = null)
        {
            m_RayGenerationProgram.BindTables = bindTables;
            m_RayGenerationProgram.ShaderIdentifier = exportName;
        }

        public override int AddMissProgram(string exportName, RHIBindTable[]? bindTables = null)
        {
            Dx12FunctionTableEntry missEntry;
            missEntry.BindTables = bindTables;
            missEntry.ShaderIdentifier = exportName;
            return m_MissPrograms.Add(missEntry);
        }

        public override int AddHitGroupProgram(string exportName, RHIBindTable[]? bindTables = null)
        {
            Dx12FunctionTableEntry hitGroupEntry;
            hitGroupEntry.BindTables = bindTables;
            hitGroupEntry.ShaderIdentifier = exportName;
            return m_HitGroupPrograms.Add(hitGroupEntry);
        }

        public override void SetMissProgram(in int index, string exportName, RHIBindTable[]? bindTables = null)
        {
            ref Dx12FunctionTableEntry missEntry = ref m_MissPrograms[index];
            missEntry.BindTables = bindTables;
            missEntry.ShaderIdentifier = exportName;
        }

        public override void SetHitGroupProgram(in int index, string exportName, RHIBindTable[]? bindTables = null)
        {
            ref Dx12FunctionTableEntry hitGroupEntry = ref m_HitGroupPrograms[index];
            hitGroupEntry.BindTables = bindTables;
            hitGroupEntry.ShaderIdentifier = exportName;
        }

        public override void ClearMissPrograms()
        {
            m_MissPrograms.Clear();
        }

        public override void ClearHitGroupPrograms()
        {
            m_HitGroupPrograms.Clear();
        }

        public override void Generate(RHIRaytracingPipelineState pipelineState)
        {
            Dx12RaytracingPipelineState dx12RaytracingPipelineState = pipelineState as Dx12RaytracingPipelineState;

            m_EntryCount = (uint)(1 + m_MissPrograms.length + m_HitGroupPrograms.length);
            m_EntryStride = (uint)(RHIUtility.AlignTo(0x20, D3D12.D3D12_SHADER_IDENTIFIER_SIZE_IN_BYTES) + (sizeof(ulong) * (int)dx12RaytracingPipelineState.MaxLocalRootParameters));

            ID3D12Resource* dx12Resource;
            D3D12_RESOURCE_DESC resourceDesc = D3D12_RESOURCE_DESC.Buffer(m_EntryCount * m_EntryStride, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE);
            D3D12_HEAP_PROPERTIES heapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD, 0, 0);
            HRESULT hResult = m_Dx12Device.NativeDevice->CreateCommittedResource(&heapProperties, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &resourceDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ, null, __uuidof<ID3D12Resource>(), (void**)&dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeResource = dx12Resource;

            void* pTableData;
            hResult = m_NativeResource->Map(0, null, &pTableData);
            IntPtr tableDataHandle = new IntPtr(pTableData);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            ID3D12StateObjectProperties* objectProperties = dx12RaytracingPipelineState.NativeStateObjectProperties;

            // copy ray generation shader identifier
            {
                char[] rayGenChar = m_RayGenerationProgram.ShaderIdentifier.ToCharArray();
                fixed (char* pRayGenChar = rayGenChar)
                {
                    void* pShaderIdentifier = objectProperties->GetShaderIdentifier(pRayGenChar);
                    Unsafe.CopyBlock(tableDataHandle.ToPointer(), pShaderIdentifier, m_EntryStride);
                    /*if (m_RayGenerationProgram.BindTables != null)
                    {
                        // To do local binding...
                    }*/
                }
                tableDataHandle += (int)m_EntryStride;
            }

            // copy miss shader identifiers
            for (int i = 0; i < m_MissPrograms.length; ++i)
            {
                ref Dx12FunctionTableEntry missEntry = ref m_MissPrograms[i];
                char[] missChar = missEntry.ShaderIdentifier.ToCharArray();
                fixed (char* pMissChar = missChar)
                {
                    void* pShaderIdentifier = objectProperties->GetShaderIdentifier(pMissChar);
                    Unsafe.CopyBlock(tableDataHandle.ToPointer(), pShaderIdentifier, m_EntryStride);
                    /*if (missEntry.BindTables != null)
                    {
                        // To do local binding...
                    }*/
                }
                tableDataHandle += (int)m_EntryStride;
            }

            // copy hit group shader identifiers
            for (int i = 0; i < m_HitGroupPrograms.length; ++i)
            {
                ref Dx12FunctionTableEntry hitGroupEntry = ref m_HitGroupPrograms[i];
                char[] hitGroupChar = hitGroupEntry.ShaderIdentifier.ToCharArray();
                fixed (char* pHitGroupChar = hitGroupChar)
                {
                    void* pShaderIdentifier = objectProperties->GetShaderIdentifier(pHitGroupChar);
                    Unsafe.CopyBlock(tableDataHandle.ToPointer(), pShaderIdentifier, m_EntryStride);
                    /*if (hitGroupEntry.BindTables != null)
                    {
                        // To do local binding...
                    }*/
                }
                tableDataHandle += (int)m_EntryStride;
            }

            m_NativeResource->Unmap(0, null);
        }

        public override void Update(RHIRaytracingPipelineState pipelineState)
        {
            throw new NotImplementedException("To do ...");
        }

        protected override void Release()
        {
            m_NativeResource->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
