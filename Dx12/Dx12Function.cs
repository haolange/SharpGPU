using System;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
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
        public ulong RayGenSize => m_EntryStride;
        public ulong RayGeStride => m_EntryStride;
        public ulong RayGenAddress => m_NativeResource->GetGPUVirtualAddress();
        public ulong MissSize => (ulong)(m_EntryStride * m_MissPrograms.length);
        public ulong MissStride => m_EntryStride;
        public ulong MissAddress => m_NativeResource->GetGPUVirtualAddress() + m_EntryStride;
        public ulong HitGroupSize => (ulong)(m_EntryStride * m_HitGroupPrograms.length);
        public ulong HitGroupStride => m_EntryStride;
        public ulong HitGroupAddress => m_NativeResource->GetGPUVirtualAddress() + (ulong)(m_EntryStride * m_MissPrograms.length + 1);

        private uint m_EntryStride;
        private uint m_ProgramCount;
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

            m_EntryStride = (uint)(D3D12.D3D12_SHADER_IDENTIFIER_SIZE_IN_BYTES + (sizeof(ulong) * (int)dx12RaytracingPipelineState.MaxLocalRootParameters));
            m_ProgramCount = (uint)(1 + m_MissPrograms.length + m_HitGroupPrograms.length);

            D3D12_RESOURCE_DESC resourceDesc = D3D12_RESOURCE_DESC.Buffer(m_EntryStride * m_ProgramCount, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE);
            D3D12_HEAP_PROPERTIES heapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD, 0, 0);

            ID3D12Resource* dx12Resource;
            HRESULT hResult = m_Dx12Device.NativeDevice->CreateCommittedResource(&heapProperties, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &resourceDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ, null, __uuidof<ID3D12Resource>(), (void**)&dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeResource = dx12Resource;

            ID3D12StateObjectProperties* objectProperties;
            hResult = dx12RaytracingPipelineState.NativePipelineState->QueryInterface(__uuidof<ID3D12StateObjectProperties>(), (void**)&objectProperties);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
        }

        public override void Update(RHIRaytracingPipelineState pipelineState)
        {
            throw new NotImplementedException("To Do .....");
        }

        protected override void Release()
        {
            m_NativeResource->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
