using System;
using Infinity.Mathmatics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;
using System.Collections.Generic;

namespace Infinity.Graphics
{
#pragma warning disable CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindTypeAndParameterSlot
    {
        public int Slot;
        public ERHIBindType Type;
    }

    internal unsafe class Dx12PipelineLayout : RHIPipelineLayout
    {
        public int ParameterCount
        {
            get
            {
                return m_ParameterCount;
            }
        }
        public uint PushConstantRootParameterIndex
        {
            get
            {
                return m_PushConstantRootParameterIndex;
            }
        }
        public uint PushConstantSize => m_PushConstantSize;
        public ID3D12RootSignature* NativeRootSignature
        {
            get
            {
                return m_NativeRootSignature;
            }
        }

        private int m_ParameterCount;
        private uint m_PushConstantRootParameterIndex;
        private uint m_PushConstantSize;
        private ID3D12RootSignature* m_NativeRootSignature;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_AllParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_VertexParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_FragmentParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_ComputeParameterMap;

        public Dx12PipelineLayout(Dx12Device device, in RHIPipelineLayoutDescriptor descriptor)
        {
            m_ParameterCount = 0;
            m_PushConstantSize = descriptor.PushConstantSize;
            m_AllParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_VertexParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_FragmentParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_ComputeParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);

            for (int i = 0; i < descriptor.ArgumentTableLayouts.Length; ++i)
            {
                Dx12ArgumentTableLayout resourceTableLayout = descriptor.ArgumentTableLayouts[i] as Dx12ArgumentTableLayout;
                m_ParameterCount += resourceTableLayout.BindInfos.Length;
            }

            bool hasPushConstants = descriptor.PushConstantSize > 0;
            int totalRootParameters = m_ParameterCount + (hasPushConstants ? 1 : 0);

            D3D12_DESCRIPTOR_RANGE1* rootDescriptorRangePtr = stackalloc D3D12_DESCRIPTOR_RANGE1[m_ParameterCount];
            Span<D3D12_DESCRIPTOR_RANGE1> rootDescriptorRangeViews = new Span<D3D12_DESCRIPTOR_RANGE1>(rootDescriptorRangePtr, m_ParameterCount);

            D3D12_ROOT_PARAMETER1* rootParameterPtr = stackalloc D3D12_ROOT_PARAMETER1[totalRootParameters];
            Span<D3D12_ROOT_PARAMETER1> rootParameterViews = new Span<D3D12_ROOT_PARAMETER1>(rootParameterPtr, totalRootParameters);

            for (int i = 0; i < descriptor.ArgumentTableLayouts.Length; ++i)
            {
                Dx12ArgumentTableLayout resourceTableLayout = descriptor.ArgumentTableLayouts[i] as Dx12ArgumentTableLayout;

                for (int j = 0; j < resourceTableLayout.BindInfos.Length; ++j)
                {
                    ref Dx12BindInfo bindInfo = ref resourceTableLayout.BindInfos[j];

                    ref D3D12_DESCRIPTOR_RANGE1 rootDescriptorRange = ref rootDescriptorRangeViews[i + j];
                    rootDescriptorRange.Init(Dx12Utility.ConvertToDx12BindType(bindInfo.Type), bindInfo.IsBindless ? bindInfo.Count : 1, bindInfo.Slot, bindInfo.Index, Dx12Utility.GetDx12DescriptorRangeFalag(bindInfo.Type));

                    ref D3D12_ROOT_PARAMETER1 rootParameterView = ref rootParameterViews[i + j];
                    rootParameterView.InitAsDescriptorTable(1, rootDescriptorRangePtr + (i + j), Dx12Utility.ConvertToDx12ShaderType(bindInfo.Stage));

                    Dx12BindTypeAndParameterSlot parameter;
                    {
                        parameter.Slot = i + j;
                        parameter.Type = bindInfo.Type;
                    }

                    if ((bindInfo.Stage & ERHIShaderStage.All) == ERHIShaderStage.All)
                    {
                        m_AllParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Stage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex)
                    {
                        m_VertexParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Stage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment)
                    {
                        m_FragmentParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Stage & ERHIShaderStage.Compute) == ERHIShaderStage.Compute)
                    {
                        m_ComputeParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }
                }
            }

            if (hasPushConstants)
            {
                m_PushConstantRootParameterIndex = (uint)m_ParameterCount;
                ref D3D12_ROOT_PARAMETER1 pushConstantParam = ref rootParameterViews[m_ParameterCount];
                pushConstantParam.InitAsConstants(descriptor.PushConstantSize / 4, 0, 0, D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_ALL);
            }

            D3D12_ROOT_SIGNATURE_FLAGS rootSignatureFlag = D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_NONE;
            rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS;
            rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS;
            rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS;

            if (descriptor.bLocalSignature)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_LOCAL_ROOT_SIGNATURE;
            }
            if (descriptor.bUseVertexLayout)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
            }

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSignatureDesc = new D3D12_VERSIONED_ROOT_SIGNATURE_DESC();
            rootSignatureDesc.Init_1_1((uint)totalRootParameters, rootParameterPtr, 0, null, rootSignatureFlag);

            ID3DBlob* signature;
            Dx12Utility.CHECK_HR(DirectX.D3DX12SerializeVersionedRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1_1, &signature, null));

            ID3D12RootSignature* rootSignature;
            Dx12Utility.CHECK_HR(device.NativeDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)&rootSignature));
            m_NativeRootSignature = rootSignature;
        }

        public Dx12BindTypeAndParameterSlot? QueryRootDescriptorParameterIndex(in ERHIShaderStage shaderStage, in uint layoutIndex, in uint slot, in ERHIBindType Type)
        {
            if ((shaderStage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex)
            {
                //hasValue = m_VertexParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_VertexParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment)
            {
                //hasValue = m_FragmentParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_FragmentParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIShaderStage.Compute) == ERHIShaderStage.Compute)
            {
                //hasValue = m_ComputeParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_ComputeParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIShaderStage.All) == ERHIShaderStage.All)
            {
                bool hasValue = m_AllParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            return null;
        }

        protected override void Release()
        {
            m_NativeRootSignature->Release();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_CUSTOM_COMPUTE_PIPELINE_STATE_DESC
    {
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE RootSignature_Type;
        public ID3D12RootSignature* pRootSignature;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE CS_Type;
        public D3D12_SHADER_BYTECODE CS;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE Flags_Type;
        public D3D12_PIPELINE_STATE_FLAGS Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_MESH_PIPELINE_STATE_DESC
    {
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE RootSignature_Type;
        public ID3D12RootSignature* pRootSignature;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE PrimitiveTopology_Type; 
        public D3D12_PRIMITIVE_TOPOLOGY_TYPE PrimitiveTopologyType;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE TaskShader_Type; 
        public D3D12_SHADER_BYTECODE TaskShader;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE MeshShader_Type; 
        public D3D12_SHADER_BYTECODE MeshShader;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE PixelShader_Type; 
        public D3D12_SHADER_BYTECODE PixelShader;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE RasterizerState_Type; 
        public D3D12_RASTERIZER_DESC RasterizerState;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE DepthStencilState_Type; 
        public D3D12_DEPTH_STENCIL_DESC DepthStencilState;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE BlendState_Type; 
        public D3D12_BLEND_DESC BlendState;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE SampleDesc_Type; 
        public DXGI_SAMPLE_DESC SampleDesc;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE SampleMask_Type; 
        public uint SampleMask;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE RTVFormats_Type; 
        public D3D12_RT_FORMAT_ARRAY RTVFormats;
        public D3D12_PIPELINE_STATE_SUBOBJECT_TYPE DSVFormat_Type;
        public DXGI_FORMAT DSVFormat;
    }

    internal unsafe class Dx12ComputePipeline : RHIComputePipeline
    {
        public ID3D12PipelineState* NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }

        private ID3D12PipelineState* m_NativePipelineState;

        internal Dx12ComputePipeline(ID3D12PipelineState* nativePipelineState, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_NativePipelineState = nativePipelineState;
        }

        public Dx12ComputePipeline(Dx12Device device, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            Dx12Function computeFunction = descriptor.ComputeFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;

#if false
            D3D12_COMPUTE_PIPELINE_STATE_DESC description = new D3D12_COMPUTE_PIPELINE_STATE_DESC();
            description.pRootSignature = pipelineLayout.NativeRootSignature;
            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.CS.BytecodeLength = computeFunction.NativeShaderBytecode.BytecodeLength;
            description.CS.pShaderBytecode = computeFunction.NativeShaderBytecode.pShaderBytecode;

            ID3D12Pipeline* pipeline;
            HRESULT hResult = device.NativeDevice->CreateComputePipeline(&description, __uuidof<ID3D12Pipeline>(), (void**)&pipeline);
#else
            D3D12_CUSTOM_COMPUTE_PIPELINE_STATE_DESC description;
            description.RootSignature_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE;
            description.pRootSignature = pipelineLayout.NativeRootSignature;

            description.CS_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_CS;
            description.CS.BytecodeLength = computeFunction.NativeShaderBytecode.BytecodeLength;
            description.CS.pShaderBytecode = computeFunction.NativeShaderBytecode.pShaderBytecode;

            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.Flags_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_FLAGS;

            D3D12_PIPELINE_STATE_STREAM_DESC streamDesc;
            streamDesc.SizeInBytes = (uint)sizeof(D3D12_CUSTOM_COMPUTE_PIPELINE_STATE_DESC);
            streamDesc.pPipelineStateSubobjectStream = &description;

            ID3D12PipelineState* nativePipelineState;
            HRESULT hResult = device.NativeDevice->CreatePipelineState(&streamDesc, __uuidof<ID3D12PipelineState>(), (void**)&nativePipelineState);
#endif

#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineState = nativePipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12RaytracingPipeline : RHIRaytracingPipeline
    {
        public ID3D12StateObject* NativePipeline => m_NativePipeline;
        public ID3D12StateObjectProperties* NativeStateObjectProperties => m_NativeStateObjectProperties;

        private ID3D12RootSignature* m_LocalConstantsRootSignature;
        private ID3D12StateObject* m_NativePipeline;
        private ID3D12StateObjectProperties* m_NativeStateObjectProperties;
        private string m_RayGenerationExport;
        private string[] m_MissExports;
        private string[] m_HitGroupExports;
        private string[] m_CallableExports;

        public Dx12RaytracingPipeline(Dx12Device device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_LocalConstantsRootSignature = null;
            m_NativePipeline = null;
            m_NativeStateObjectProperties = null;
            m_RayGenerationExport = descriptor.RayGeneration.General.EntryName;
            m_MissExports = Array.Empty<string>();
            m_HitGroupExports = Array.Empty<string>();
            m_CallableExports = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(m_RayGenerationExport))
            {
                throw new InvalidOperationException("Ray generation entry name is empty.");
            }

            Span<RHIRayGeneralGroupDescriptor> missGroups = descriptor.RayMissGroups.Span;
            Span<RHIRayHitGroupDescriptor> hitGroups = descriptor.RayHitGroups.Span;
            Span<RHIRayGeneralGroupDescriptor> callableGroups = descriptor.RayCallableGroups.Span;

            m_MissExports = new string[missGroups.Length];
            for (int i = 0; i < missGroups.Length; ++i)
            {
                m_MissExports[i] = missGroups[i].General.EntryName;
            }

            m_HitGroupExports = new string[hitGroups.Length];
            for (int i = 0; i < hitGroups.Length; ++i)
            {
                m_HitGroupExports[i] = hitGroups[i].Name;
            }

            m_CallableExports = new string[callableGroups.Length];
            for (int i = 0; i < callableGroups.Length; ++i)
            {
                m_CallableExports[i] = callableGroups[i].General.EntryName;
            }

            int maxExportDescriptors = 1 + missGroups.Length + callableGroups.Length + hitGroups.Length * 3;
            int maxStateSubObjects = 1 + hitGroups.Length + 3 + (descriptor.LocalDataStrideInBytes > 0 ? 2 : 0);

            D3D12_EXPORT_DESC* exportDescriptors = stackalloc D3D12_EXPORT_DESC[Math.Max(maxExportDescriptors, 1)];
            D3D12_HIT_GROUP_DESC* hitGroupDescriptors = stackalloc D3D12_HIT_GROUP_DESC[Math.Max(hitGroups.Length, 1)];
            D3D12_STATE_SUBOBJECT* stateSubObjects = stackalloc D3D12_STATE_SUBOBJECT[Math.Max(maxStateSubObjects, 1)];

            List<IntPtr> allocatedStrings = new List<IntPtr>(maxExportDescriptors + hitGroups.Length * 4 + m_MissExports.Length + m_HitGroupExports.Length + m_CallableExports.Length);
            IntPtr associationExportBuffer = IntPtr.Zero;

            try
            {
                int exportCount = 0;
                AddLibraryExport(exportDescriptors, ref exportCount, m_RayGenerationExport, allocatedStrings);
                for (int i = 0; i < missGroups.Length; ++i)
                {
                    AddLibraryExport(exportDescriptors, ref exportCount, missGroups[i].General.EntryName, allocatedStrings);
                }

                for (int i = 0; i < callableGroups.Length; ++i)
                {
                    AddLibraryExport(exportDescriptors, ref exportCount, callableGroups[i].General.EntryName, allocatedStrings);
                }

                for (int i = 0; i < hitGroups.Length; ++i)
                {
                    ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                    if (hitGroup.AnyHit.HasValue)
                    {
                        AddLibraryExport(exportDescriptors, ref exportCount, hitGroup.AnyHit.Value.EntryName, allocatedStrings);
                    }

                    if (hitGroup.Intersect.HasValue)
                    {
                        AddLibraryExport(exportDescriptors, ref exportCount, hitGroup.Intersect.Value.EntryName, allocatedStrings);
                    }

                    if (hitGroup.ClosestHit.HasValue)
                    {
                        AddLibraryExport(exportDescriptors, ref exportCount, hitGroup.ClosestHit.Value.EntryName, allocatedStrings);
                    }
                }

                int subObjectCount = 0;

                D3D12_DXIL_LIBRARY_DESC dxilLibraryDesc = new D3D12_DXIL_LIBRARY_DESC
                {
                    DXILLibrary = new D3D12_SHADER_BYTECODE(descriptor.FunctionLibrary.Descriptor.ByteCode.ToPointer(), descriptor.FunctionLibrary.Descriptor.ByteSize),
                    NumExports = (uint)exportCount,
                    pExports = exportDescriptors,
                };
                stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_DXIL_LIBRARY;
                stateSubObjects[subObjectCount].pDesc = &dxilLibraryDesc;
                subObjectCount++;

                // Hit-group declarations
                for (int i = 0; i < hitGroups.Length; ++i)
                {
                    ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                    IntPtr hitGroupName = Marshal.StringToHGlobalUni(hitGroup.Name);
                    allocatedStrings.Add(hitGroupName);

                    hitGroupDescriptors[i] = new D3D12_HIT_GROUP_DESC
                    {
                        Type = Dx12Utility.ConverteToDx12HitGroupType(hitGroup.Type),
                        HitGroupExport = (char*)hitGroupName.ToPointer(),
                        AnyHitShaderImport = hitGroup.AnyHit.HasValue ? AllocateOptionalUnicode(hitGroup.AnyHit.Value.EntryName, allocatedStrings) : null,
                        ClosestHitShaderImport = hitGroup.ClosestHit.HasValue ? AllocateOptionalUnicode(hitGroup.ClosestHit.Value.EntryName, allocatedStrings) : null,
                        IntersectionShaderImport = hitGroup.Intersect.HasValue ? AllocateOptionalUnicode(hitGroup.Intersect.Value.EntryName, allocatedStrings) : null,
                    };

                    stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_HIT_GROUP;
                    stateSubObjects[subObjectCount].pDesc = &hitGroupDescriptors[i];
                    subObjectCount++;
                }

                D3D12_LOCAL_ROOT_SIGNATURE localRootSignatureDesc = default;
                D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION localRootAssociationDesc = default;
                nint* associationExportHandles = null;
                if (descriptor.LocalDataStrideInBytes > 0)
                {
                    m_LocalConstantsRootSignature = BuildLocalConstantsRootSignature(device, descriptor.LocalDataStrideInBytes);
                    localRootSignatureDesc.pLocalRootSignature = m_LocalConstantsRootSignature;
                    int localRootSubObjectIndex = subObjectCount;
                    stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_LOCAL_ROOT_SIGNATURE;
                    stateSubObjects[subObjectCount].pDesc = &localRootSignatureDesc;
                    subObjectCount++;

                    int associationCount = 1 + missGroups.Length + hitGroups.Length + callableGroups.Length;
                    int associationCapacity = associationCount > 0 ? associationCount : 1;
                    associationExportBuffer = Marshal.AllocHGlobal(IntPtr.Size * associationCapacity);
                    associationExportHandles = (nint*)associationExportBuffer.ToPointer();
                    int associationIndex = 0;
                    associationExportHandles[associationIndex++] = (IntPtr)AllocateRequiredUnicode(m_RayGenerationExport, allocatedStrings);
                    for (int i = 0; i < missGroups.Length; ++i)
                    {
                        associationExportHandles[associationIndex++] = (IntPtr)AllocateRequiredUnicode(missGroups[i].General.EntryName, allocatedStrings);
                    }

                    for (int i = 0; i < hitGroups.Length; ++i)
                    {
                        associationExportHandles[associationIndex++] = (IntPtr)AllocateRequiredUnicode(hitGroups[i].Name, allocatedStrings);
                    }

                    for (int i = 0; i < callableGroups.Length; ++i)
                    {
                        associationExportHandles[associationIndex++] = (IntPtr)AllocateRequiredUnicode(callableGroups[i].General.EntryName, allocatedStrings);
                    }

                    localRootAssociationDesc = new D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION
                    {
                        NumExports = (uint)associationIndex,
                        pExports = (char**)associationExportHandles,
                        pSubobjectToAssociate = &stateSubObjects[localRootSubObjectIndex],
                    };

                    stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                    stateSubObjects[subObjectCount].pDesc = &localRootAssociationDesc;
                    subObjectCount++;
                }

                D3D12_RAYTRACING_SHADER_CONFIG shaderConfigDesc = new D3D12_RAYTRACING_SHADER_CONFIG
                {
                    MaxPayloadSizeInBytes = descriptor.MaxPayloadSize,
                    MaxAttributeSizeInBytes = descriptor.MaxAttributeSize,
                };
                stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_RAYTRACING_SHADER_CONFIG;
                stateSubObjects[subObjectCount].pDesc = &shaderConfigDesc;
                subObjectCount++;

                D3D12_RAYTRACING_PIPELINE_CONFIG pipelineConfigDesc = new D3D12_RAYTRACING_PIPELINE_CONFIG
                {
                    MaxTraceRecursionDepth = descriptor.MaxRecursionDepth,
                };
                stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_RAYTRACING_PIPELINE_CONFIG;
                stateSubObjects[subObjectCount].pDesc = &pipelineConfigDesc;
                subObjectCount++;

                D3D12_GLOBAL_ROOT_SIGNATURE globalRootSignatureDesc = new D3D12_GLOBAL_ROOT_SIGNATURE
                {
                    pGlobalRootSignature = ((Dx12PipelineLayout)descriptor.PipelineLayout).NativeRootSignature,
                };
                stateSubObjects[subObjectCount].Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_GLOBAL_ROOT_SIGNATURE;
                stateSubObjects[subObjectCount].pDesc = &globalRootSignatureDesc;
                subObjectCount++;

                D3D12_STATE_OBJECT_DESC stateObjectDesc = new D3D12_STATE_OBJECT_DESC
                {
                    Type = D3D12_STATE_OBJECT_TYPE.D3D12_STATE_OBJECT_TYPE_RAYTRACING_PIPELINE,
                    NumSubobjects = (uint)subObjectCount,
                    pSubobjects = stateSubObjects,
                };

                ID3D12StateObject* nativePipeline = null;
                HRESULT hResult = device.NativeDevice->CreateStateObject(&stateObjectDesc, __uuidof<ID3D12StateObject>(), (void**)&nativePipeline);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_NativePipeline = nativePipeline;

                ID3D12StateObjectProperties* stateObjectProperties = null;
                hResult = m_NativePipeline->QueryInterface(__uuidof<ID3D12StateObjectProperties>(), (void**)&stateObjectProperties);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_NativeStateObjectProperties = stateObjectProperties;
            }
            finally
            {
                if (associationExportBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(associationExportBuffer);
                }

                for (int i = 0; i < allocatedStrings.Count; ++i)
                {
                    Marshal.FreeHGlobal(allocatedStrings[i]);
                }
            }
        }

        internal string GetExportName(in ERHIRayShaderTableSection section, in int groupIndex)
        {
            return section switch
            {
                ERHIRayShaderTableSection.RayGeneration => groupIndex == 0 ? m_RayGenerationExport : throw new ArgumentOutOfRangeException(nameof(groupIndex)),
                ERHIRayShaderTableSection.Miss => m_MissExports[groupIndex],
                ERHIRayShaderTableSection.Hit => m_HitGroupExports[groupIndex],
                ERHIRayShaderTableSection.Callable => m_CallableExports[groupIndex],
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };
        }

        private static void AddLibraryExport(D3D12_EXPORT_DESC* exportDescriptors, ref int exportCount, string entryName, List<IntPtr> allocatedStrings)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new InvalidOperationException("Ray-tracing library export entry name is empty.");
            }

            IntPtr namePtr = Marshal.StringToHGlobalUni(entryName);
            allocatedStrings.Add(namePtr);
            exportDescriptors[exportCount] = new D3D12_EXPORT_DESC
            {
                Name = (char*)namePtr.ToPointer(),
                ExportToRename = null,
                Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE,
            };
            exportCount++;
        }

        private static char* AllocateRequiredUnicode(string text, List<IntPtr> allocatedStrings)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Unexpected empty Unicode export name.");
            }

            IntPtr ptr = Marshal.StringToHGlobalUni(text);
            allocatedStrings.Add(ptr);
            return (char*)ptr.ToPointer();
        }

        private static char* AllocateOptionalUnicode(string? text, List<IntPtr> allocatedStrings)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            IntPtr ptr = Marshal.StringToHGlobalUni(text);
            allocatedStrings.Add(ptr);
            return (char*)ptr.ToPointer();
        }

        private static ID3D12RootSignature* BuildLocalConstantsRootSignature(Dx12Device device, in uint localDataStrideInBytes)
        {
            uint dwordCount = Math.Max(1u, (localDataStrideInBytes + 3u) / 4u);
            D3D12_ROOT_PARAMETER1 localRootParameter = default;
            localRootParameter.InitAsConstants(dwordCount, 0, 0, D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_ALL);

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSigDesc = default;
            rootSigDesc.Init_1_1(
                1,
                &localRootParameter,
                0,
                null,
                D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_LOCAL_ROOT_SIGNATURE);

            ID3DBlob* signatureBlob = null;
            Dx12Utility.CHECK_HR(DirectX.D3DX12SerializeVersionedRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1_1, &signatureBlob, null));

            ID3D12RootSignature* localRootSignature = null;
            HRESULT hResult = device.NativeDevice->CreateRootSignature(
                0,
                signatureBlob->GetBufferPointer(),
                signatureBlob->GetBufferSize(),
                __uuidof<ID3D12RootSignature>(),
                (void**)&localRootSignature);
            signatureBlob->Release();
            Dx12Utility.CHECK_HR(hResult);

            return localRootSignature;
        }

        protected override void Release()
        {
            if (m_NativeStateObjectProperties != null)
            {
                m_NativeStateObjectProperties->Release();
                m_NativeStateObjectProperties = null;
            }

            if (m_NativePipeline != null)
            {
                m_NativePipeline->Release();
                m_NativePipeline = null;
            }

            if (m_LocalConstantsRootSignature != null)
            {
                m_LocalConstantsRootSignature->Release();
                m_LocalConstantsRootSignature = null;
            }
        }
    }

    internal unsafe class Dx12RasterPipeline : RHIRasterPipeline
    {
        public uint[] VertexStrides
        {
            get
            {
                return m_VertexStrides;
            }
        }
        public ID3D12PipelineState* NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }
        public D3D_PRIMITIVE_TOPOLOGY PrimitiveTopology
        {
            get
            {
                return m_PrimitiveTopology;
            }
        }

        private uint[] m_VertexStrides;
        private ID3D12PipelineState* m_NativePipelineState;
        private D3D_PRIMITIVE_TOPOLOGY m_PrimitiveTopology;

        internal Dx12RasterPipeline(ID3D12PipelineState* nativePipelineState, in RHIRasterPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_NativePipelineState = nativePipelineState;
            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology);
            m_VertexStrides = Array.Empty<uint>();
        }

        public Dx12RasterPipeline(Dx12Device device, in RHIRasterPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology);

            Dx12Function fragmentFunction = descriptor.FragmentFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;
            D3D12_PRIMITIVE_TOPOLOGY_TYPE primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveAssembler.PrimitiveTopology);

            switch (descriptor.PrimitiveAssembler.PrimitiveType)
            {
                case ERHIPrimitiveType.Mesh:
                    if (descriptor.PrimitiveAssembler.MeshletAssembler.HasValue)
                    {
                        Dx12Function taskFunction = descriptor.PrimitiveAssembler.MeshletAssembler.Value.TaskFunction as Dx12Function;
                        Dx12Function meshFunction = descriptor.PrimitiveAssembler.MeshletAssembler.Value.MeshFunction as Dx12Function;

                        D3D12_MESH_PIPELINE_STATE_DESC nativeMeshPipelineDesc = new D3D12_MESH_PIPELINE_STATE_DESC
                        {
                            RootSignature_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_ROOT_SIGNATURE,
                            PrimitiveTopology_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PRIMITIVE_TOPOLOGY,
                            TaskShader_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_AS,
                            MeshShader_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_MS,
                            PixelShader_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_PS,
                            BlendState_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_BLEND,
                            RasterizerState_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RASTERIZER,
                            DepthStencilState_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL,
                            SampleDesc_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_DESC,
                            SampleMask_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_SAMPLE_MASK,
                            RTVFormats_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_RENDER_TARGET_FORMATS,
                            DSVFormat_Type = D3D12_PIPELINE_STATE_SUBOBJECT_TYPE.D3D12_PIPELINE_STATE_SUBOBJECT_TYPE_DEPTH_STENCIL_FORMAT,

                            pRootSignature = pipelineLayout.NativeRootSignature,
                            PrimitiveTopologyType = primitiveTopologyType,
                            SampleDesc = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount),
                            SampleMask = descriptor.RenderState.SampleMask.HasValue ? descriptor.RenderState.SampleMask.Value : uint.MaxValue,
                            BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderState.BlendState),
                            RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderState.RasterizerState, descriptor.SampleCount != ERHISampleCount.None),
                            DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderState.DepthStencilState)
                        };

                        if (descriptor.DepthFormat != ERHIPixelFormat.Unknown)
                        {
                            nativeMeshPipelineDesc.DSVFormat = Dx12Utility.ConvertToDx12Format(descriptor.DepthFormat);
                        }

                        DXGI_FORMAT* colorFormats = stackalloc DXGI_FORMAT[descriptor.ColorFormats.Length];
                        {
                            for (int i = 0; i < descriptor.ColorFormats.Length; ++i)
                            {
                                colorFormats[i] = Dx12Utility.ConvertToDx12ViewFormat(descriptor.ColorFormats[i]);
                            }
                            nativeMeshPipelineDesc.RTVFormats = new D3D12_RT_FORMAT_ARRAY(colorFormats, (uint)descriptor.ColorFormats.Length);
                        }

                        if (taskFunction != null)
                        {
                            nativeMeshPipelineDesc.TaskShader.BytecodeLength = taskFunction.NativeShaderBytecode.BytecodeLength;
                            nativeMeshPipelineDesc.TaskShader.pShaderBytecode = taskFunction.NativeShaderBytecode.pShaderBytecode;
                        }

                        if (meshFunction != null)
                        {
                            nativeMeshPipelineDesc.MeshShader.BytecodeLength = meshFunction.NativeShaderBytecode.BytecodeLength;
                            nativeMeshPipelineDesc.MeshShader.pShaderBytecode = meshFunction.NativeShaderBytecode.pShaderBytecode;
                        }

                        if (fragmentFunction != null)
                        {
                            nativeMeshPipelineDesc.PixelShader.BytecodeLength = fragmentFunction.NativeShaderBytecode.BytecodeLength;
                            nativeMeshPipelineDesc.PixelShader.pShaderBytecode = fragmentFunction.NativeShaderBytecode.pShaderBytecode;
                        }

                        D3D12_PIPELINE_STATE_STREAM_DESC streamDesc;
                        streamDesc.SizeInBytes = (uint)sizeof(D3D12_MESH_PIPELINE_STATE_DESC);
                        streamDesc.pPipelineStateSubobjectStream = &nativeMeshPipelineDesc;

                        ID3D12PipelineState* nativePipelineState;
                        HRESULT hResult = device.NativeDevice->CreatePipelineState(&streamDesc, __uuidof<ID3D12PipelineState>(), (void**)&nativePipelineState);

#if DEBUG
                        Dx12Utility.CHECK_HR(hResult);
#endif
                        m_NativePipelineState = nativePipelineState;
                    }
                    break;

                case ERHIPrimitiveType.Vertex:
                    if(descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
                    {
                        Dx12Function vertexFunction = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexFunction as Dx12Function;
                        Span<RHIVertexLayoutDescriptor> vertexLayouts = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexLayouts.Span;
                        if ((vertexFunction != null))
                        {
                            m_VertexStrides = new uint[vertexLayouts.Length];
                            for (int j = 0; j < vertexLayouts.Length; ++j)
                            {
                                m_VertexStrides[j] = vertexLayouts[j].Stride;
                            }
                        }

                        int inputElementCount = Dx12Utility.GetDx12VertexLayoutCount(vertexLayouts);
                        D3D12_INPUT_ELEMENT_DESC* inputElementsPtr = stackalloc D3D12_INPUT_ELEMENT_DESC[inputElementCount];
                        Span<D3D12_INPUT_ELEMENT_DESC> inputElementsView = new Span<D3D12_INPUT_ELEMENT_DESC>(inputElementsPtr, inputElementCount);

                        Dx12Utility.ConvertToDx12VertexLayout(vertexLayouts, inputElementsView);

                        D3D12_INPUT_LAYOUT_DESC vertexInputLayout;
                        vertexInputLayout.NumElements = (uint)inputElementCount;
                        vertexInputLayout.pInputElementDescs = inputElementsPtr;

                        D3D12_GRAPHICS_PIPELINE_STATE_DESC nativeGraphicsPipelineDesc = new D3D12_GRAPHICS_PIPELINE_STATE_DESC
                        {
                            InputLayout = vertexInputLayout,
                            pRootSignature = pipelineLayout.NativeRootSignature,
                            PrimitiveTopologyType = primitiveTopologyType,
                            SampleDesc = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount),
                            SampleMask = descriptor.RenderState.SampleMask.HasValue ? descriptor.RenderState.SampleMask.Value : uint.MaxValue,
                            //description.StreamOutput = new StreamOutputDescription(),
                            BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderState.BlendState),
                            RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderState.RasterizerState, descriptor.SampleCount != ERHISampleCount.None),
                            DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderState.DepthStencilState),
                            Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE,
                            NumRenderTargets = (uint)descriptor.ColorFormats.Length,
                        };

                        if (descriptor.DepthFormat != ERHIPixelFormat.Unknown)
                        {
                            nativeGraphicsPipelineDesc.DSVFormat = Dx12Utility.ConvertToDx12Format(descriptor.DepthFormat);
                        }

                        //fixed (ERHIPixelFormat* formatPtr = &descriptor.ColorFormat0)
                        for (int i = 0; i < descriptor.ColorFormats.Length; ++i)
                        {
                            nativeGraphicsPipelineDesc.RTVFormats[i] = Dx12Utility.ConvertToDx12ViewFormat(descriptor.ColorFormats[i]);
                        }

                        if (vertexFunction != null)
                        {
                            nativeGraphicsPipelineDesc.VS.BytecodeLength = vertexFunction.NativeShaderBytecode.BytecodeLength;
                            nativeGraphicsPipelineDesc.VS.pShaderBytecode = vertexFunction.NativeShaderBytecode.pShaderBytecode;
                        }

                        if (fragmentFunction != null)
                        {
                            nativeGraphicsPipelineDesc.PS.BytecodeLength = fragmentFunction.NativeShaderBytecode.BytecodeLength;
                            nativeGraphicsPipelineDesc.PS.pShaderBytecode = fragmentFunction.NativeShaderBytecode.pShaderBytecode;
                        }

                        ID3D12PipelineState* nativePipelineState;
                        HRESULT hResult = device.NativeDevice->CreateGraphicsPipelineState(&nativeGraphicsPipelineDesc, __uuidof<ID3D12PipelineState>(), (void**)&nativePipelineState);
#if DEBUG
                        Dx12Utility.CHECK_HR(hResult);
#endif
                        m_NativePipelineState = nativePipelineState;
                    }
                    break;
            }
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12PipelineLibrary : RHIPipelineLibrary
    {
        private Dx12Device m_Dx12Device;
        private ID3D12PipelineLibrary1* m_NativePipelineLibrary;

        private static string GetComputePipelineCacheKey(in RHIComputePipelineDescriptor descriptor)
        {
            string entry = descriptor.ComputeFunction?.Descriptor.EntryName ?? "compute";
            return $"compute:{entry}";
        }

        private static string GetRasterPipelineCacheKey(in RHIRasterPipelineDescriptor descriptor)
        {
            string vertex = descriptor.PrimitiveAssembler.VertexAssembler?.VertexFunction?.Descriptor.EntryName ?? "none";
            string task = descriptor.PrimitiveAssembler.MeshletAssembler?.TaskFunction?.Descriptor.EntryName ?? "none";
            string mesh = descriptor.PrimitiveAssembler.MeshletAssembler?.MeshFunction?.Descriptor.EntryName ?? "none";
            string fragment = descriptor.FragmentFunction?.Descriptor.EntryName ?? "none";

            return
                $"raster:{descriptor.PrimitiveAssembler.PrimitiveType}:" +
                $"{descriptor.PrimitiveAssembler.PrimitiveTopology}:" +
                $"{vertex}:{task}:{mesh}:{fragment}:" +
                $"{descriptor.SampleCount}:{descriptor.ColorFormats.Length}:{descriptor.DepthFormat}";
        }

        public Dx12PipelineLibrary(Dx12Device device, in RHIPipelineLibraryDescriptor descriptor) : base(descriptor)
        {
            m_Dx12Device = device;

            ID3D12PipelineLibrary1* pipelineLibrary;
            HRESULT hResult = device.NativeDevice->CreatePipelineLibrary(null, 0, __uuidof<ID3D12PipelineLibrary1>(), (void**)&pipelineLibrary);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineLibrary = pipelineLibrary;
        }

        public Dx12PipelineLibrary(Dx12Device device, in RHIPipelineLibraryResult pipelineLibraryResult) : base(pipelineLibraryResult)
        {
            m_Dx12Device = device;

            ID3D12PipelineLibrary1* pipelineLibrary;
            HRESULT hResult = device.NativeDevice->CreatePipelineLibrary((void*)pipelineLibraryResult.ByteCode, pipelineLibraryResult.ByteSize, __uuidof<ID3D12PipelineLibrary1>(), (void**)&pipelineLibrary);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineLibrary = pipelineLibrary;
        }

        public override void StoreComputePipeline(string name, RHIComputePipeline computePipeline)
        {
            Dx12ComputePipeline dx12Pipeline = computePipeline as Dx12ComputePipeline;
            fixed (char* pName = name)
            {
                m_NativePipelineLibrary->StorePipeline(pName, dx12Pipeline.NativePipelineState);
            }
        }

        public override void StoreRasterPipeline(string name, RHIRasterPipeline rasterPipeline)
        {
            Dx12RasterPipeline dx12Pipeline = rasterPipeline as Dx12RasterPipeline;
            fixed (char* pName = name)
            {
                m_NativePipelineLibrary->StorePipeline(pName, dx12Pipeline.NativePipelineState);
            }
        }

        public override void StoreRaytracingPipeline(string name, RHIRaytracingPipeline raytracingPipeline)
        {
            throw new System.NotSupportedException("D3D12 PipelineLibrary does not support storing raytracing state objects.");
        }

        public override RHIComputePipeline LoadComputePipeline(RHIComputePipelineDescriptor computePipelineDescriptor)
        {
            if (m_NativePipelineLibrary == null)
            {
                throw new System.InvalidOperationException("Dx12PipelineLibrary: Cannot load pipeline — ID3D12PipelineLibrary has not been initialized.");
            }

            Dx12Function computeFunction = computePipelineDescriptor.ComputeFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = computePipelineDescriptor.PipelineLayout as Dx12PipelineLayout;

            D3D12_COMPUTE_PIPELINE_STATE_DESC description = new D3D12_COMPUTE_PIPELINE_STATE_DESC();
            description.pRootSignature = pipelineLayout.NativeRootSignature;
            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.CS.BytecodeLength = computeFunction.NativeShaderBytecode.BytecodeLength;
            description.CS.pShaderBytecode = computeFunction.NativeShaderBytecode.pShaderBytecode;

            string pipelineName = GetComputePipelineCacheKey(computePipelineDescriptor);
            ID3D12PipelineState* nativePipelineState;
            fixed (char* pName = pipelineName)
            {
                HRESULT hResult = m_NativePipelineLibrary->LoadComputePipeline(pName, &description, __uuidof<ID3D12PipelineState>(), (void**)&nativePipelineState);

                if (hResult == DXGI.DXGI_ERROR_NOT_FOUND)
                {
                    // Cache miss: create the pipeline normally and store it for next time
                    Dx12ComputePipeline fallbackPipeline = new Dx12ComputePipeline(m_Dx12Device, computePipelineDescriptor);
                    StoreComputePipeline(pipelineName, fallbackPipeline);
                    return fallbackPipeline;
                }
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
            }

            Dx12ComputePipeline pipeline = new Dx12ComputePipeline(nativePipelineState, computePipelineDescriptor);
            return pipeline;
        }

        public override RHIRasterPipeline LoadRasterPipeline(RHIRasterPipelineDescriptor rasterPipelineDescriptor)
        {
            if (m_NativePipelineLibrary == null)
            {
                throw new System.InvalidOperationException("Dx12PipelineLibrary: Cannot load pipeline — ID3D12PipelineLibrary has not been initialized.");
            }

            Dx12PipelineLayout pipelineLayout = rasterPipelineDescriptor.PipelineLayout as Dx12PipelineLayout;

            // Build the graphics pipeline state description for library lookup
            D3D12_GRAPHICS_PIPELINE_STATE_DESC description = new D3D12_GRAPHICS_PIPELINE_STATE_DESC();
            description.pRootSignature = pipelineLayout.NativeRootSignature;
            description.PrimitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(rasterPipelineDescriptor.PrimitiveAssembler.PrimitiveTopology);
            description.SampleMask = uint.MaxValue;
            description.SampleDesc.Count = (uint)rasterPipelineDescriptor.SampleCount;
            description.SampleDesc.Quality = 0;
            description.NumRenderTargets = (uint)rasterPipelineDescriptor.ColorFormats.Length;

            string pipelineName = GetRasterPipelineCacheKey(rasterPipelineDescriptor);
            ID3D12PipelineState* nativePipelineState;
            fixed (char* pName = pipelineName)
            {
                HRESULT hResult = m_NativePipelineLibrary->LoadGraphicsPipeline(pName, &description, __uuidof<ID3D12PipelineState>(), (void**)&nativePipelineState);

                if (hResult == DXGI.DXGI_ERROR_NOT_FOUND)
                {
                    // Cache miss: create the pipeline normally and store it for next time
                    Dx12RasterPipeline fallbackPipeline = new Dx12RasterPipeline(m_Dx12Device, rasterPipelineDescriptor);
                    StoreRasterPipeline(pipelineName, fallbackPipeline);
                    return fallbackPipeline;
                }
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
            }

            Dx12RasterPipeline pipeline = new Dx12RasterPipeline(nativePipelineState, rasterPipelineDescriptor);
            return pipeline;
        }

        public override RHIRaytracingPipeline LoadRaytracingPipeline(RHIRaytracingPipelineDescriptor raytracingPipelineDescriptor)
        {
            throw new System.NotSupportedException("D3D12 PipelineLibrary does not support loading raytracing state objects. Raytracing pipelines use ID3D12StateObject which is incompatible with ID3D12PipelineLibrary.");
        }

        public override RHIPipelineLibraryResult Serialize()
        {
            nuint blobSize = m_NativePipelineLibrary->GetSerializedSize();
            RHIPipelineLibraryResult result;
            result.ByteSize = (uint)blobSize;
            result.ByteCode = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)blobSize);
            m_NativePipelineLibrary->Serialize(result.ByteCode.ToPointer(), blobSize);
            return result;
        }

        protected override void Release()
        {
            m_NativePipelineLibrary->Release();
        }
    }
#pragma warning restore CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal sealed class Dx12WorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal Dx12WorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
            // TODO(ROADMAP-P2-1): Release COM resources after TerraFX SDK upgrade.
        }
    }

    internal unsafe class Dx12MLPipeline : RHIMLPipeline
    {
        // DirectML integration: the ML pipeline wraps an IDMLCompiledOperator
        // obtained by compiling the DXIL compute shader payload via DirectML.
        // When DirectML is unavailable, falls back to a compute pipeline bridge.
        internal string Name => m_Name;
        internal RHIFunction Function => m_Function;
        internal Dx12ComputePipeline? ComputePipeline => m_ComputePipeline;

        private readonly string m_Name;
        private readonly Dx12Device m_Dx12Device;
        private readonly RHIFunction m_Function;
        private readonly RHIMLTensorDescriptor[] m_InputTensors;
        private Dx12ComputePipeline? m_ComputePipeline;

        public Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_Dx12Device = device;
            m_Name = descriptor.Name;
            m_Function = descriptor.Function;
            m_InputTensors = descriptor.InputTensors.ToArray();

            // Estimate intermediates heap size from input tensor dimensions
            ulong intermediatesSize = 0;
            for (int i = 0; i < m_InputTensors.Length; ++i)
            {
                ulong tensorSize = 1;
                Span<uint> dims = m_InputTensors[i].Dimensions.Span;
                for (int d = 0; d < dims.Length; ++d)
                {
                    tensorSize *= dims[d];
                }
                tensorSize *= GetElementSize(m_InputTensors[i].DataType);
                intermediatesSize += tensorSize;
            }
            m_IntermediatesHeapSize = intermediatesSize;
        }

        private static ulong GetElementSize(ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => 4,
                ERHIMLDataType.Float16 => 2,
                ERHIMLDataType.BFloat16 => 2,
                ERHIMLDataType.Int32 => 4,
                ERHIMLDataType.Int16 => 2,
                ERHIMLDataType.Int8 => 1,
                ERHIMLDataType.UInt32 => 4,
                ERHIMLDataType.UInt16 => 2,
                ERHIMLDataType.UInt8 => 1,
                _ => 4,
            };
        }

        protected override void Release()
        {
            m_ComputePipeline?.Dispose();
            m_ComputePipeline = null;
        }
    }
}
