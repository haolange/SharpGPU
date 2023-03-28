using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindTypeAndParameterSlot
    {
        public int Slot;
        public EBindType BindType;
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
        public ID3D12RootSignature* NativeRootSignature
        {
            get
            {
                return m_NativeRootSignature;
            }
        }

        private int m_ParameterCount;
        private ID3D12RootSignature* m_NativeRootSignature;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_AllParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_VertexParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_FragmentParameterMap;
        private Dictionary<int, Dx12BindTypeAndParameterSlot> m_ComputeParameterMap;

        public Dx12PipelineLayout(Dx12Device device, in RHIPipelineLayoutDescriptor descriptor)
        {
            m_ParameterCount = 0;
            m_AllParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_VertexParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_FragmentParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);
            m_ComputeParameterMap = new Dictionary<int, Dx12BindTypeAndParameterSlot>(5);

            for (int i = 0; i < descriptor.BindGroupLayouts.Length; ++i)
            {
                Dx12BindGroupLayout bindGroupLayout = descriptor.BindGroupLayouts[i] as Dx12BindGroupLayout;
                m_ParameterCount += bindGroupLayout.BindInfos.Length;
            }

            D3D12_DESCRIPTOR_RANGE1* rootDescriptorRangePtr = stackalloc D3D12_DESCRIPTOR_RANGE1[m_ParameterCount];
            Span<D3D12_DESCRIPTOR_RANGE1> rootDescriptorRangeViews = new Span<D3D12_DESCRIPTOR_RANGE1>(rootDescriptorRangePtr, m_ParameterCount);

            D3D12_ROOT_PARAMETER1* rootParameterPtr = stackalloc D3D12_ROOT_PARAMETER1[m_ParameterCount];
            Span<D3D12_ROOT_PARAMETER1> rootParameterViews = new Span<D3D12_ROOT_PARAMETER1>(rootParameterPtr, m_ParameterCount);

            for (int i = 0; i < descriptor.BindGroupLayouts.Length; ++i)
            {
                Dx12BindGroupLayout bindGroupLayout = descriptor.BindGroupLayouts[i] as Dx12BindGroupLayout;

                for (int j = 0; j < bindGroupLayout.BindInfos.Length; ++j)
                {
                    ref Dx12BindInfo bindInfo = ref bindGroupLayout.BindInfos[j];

                    ref D3D12_DESCRIPTOR_RANGE1 rootDescriptorRange = ref rootDescriptorRangeViews[i + j];
                    rootDescriptorRange.Init(Dx12Utility.ConvertToDx12BindType(bindInfo.BindType), bindInfo.IsBindless ? bindInfo.Count : 1, bindInfo.BindSlot, bindInfo.Index, Dx12Utility.GetDx12DescriptorRangeFalag(bindInfo.BindType));

                    ref D3D12_ROOT_PARAMETER1 rootParameterView = ref rootParameterViews[i + j];
                    rootParameterView.InitAsDescriptorTable(1, rootDescriptorRangePtr + (i + j), Dx12Utility.ConvertToDx12ShaderStage(bindInfo.FunctionStage));

                    Dx12BindTypeAndParameterSlot parameter;
                    {
                        parameter.Slot = i + j;
                        parameter.BindType = bindInfo.BindType;
                    }

                    if ((bindInfo.FunctionStage & EFunctionStage.All) == EFunctionStage.All)
                    {
                        m_AllParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.BindSlot, Dx12Utility.GetDx12BindKey(bindInfo.BindType)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.FunctionStage & EFunctionStage.Vertex) == EFunctionStage.Vertex)
                    {
                        m_VertexParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.BindSlot, Dx12Utility.GetDx12BindKey(bindInfo.BindType)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.FunctionStage & EFunctionStage.Fragment) == EFunctionStage.Fragment)
                    {
                        m_FragmentParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.BindSlot, Dx12Utility.GetDx12BindKey(bindInfo.BindType)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.FunctionStage & EFunctionStage.Compute) == EFunctionStage.Compute)
                    {
                        m_ComputeParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.BindSlot, Dx12Utility.GetDx12BindKey(bindInfo.BindType)).GetHashCode(), parameter);
                    }
                }
            }

            D3D12_ROOT_SIGNATURE_FLAGS rootSignatureFlag = D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_NONE;
            if (descriptor.IsLocalSignature)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_LOCAL_ROOT_SIGNATURE;
            }
            if (descriptor.AllowVertexLayout)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
            }
            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSignatureDesc = new D3D12_VERSIONED_ROOT_SIGNATURE_DESC();
            rootSignatureDesc.Init_1_1((uint)m_ParameterCount, rootParameterPtr, 0, null, rootSignatureFlag);

            ID3DBlob* signature;
            Dx12Utility.CHECK_HR(DirectX.D3D12SerializeVersionedRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1_1, &signature, null));

            ID3D12RootSignature* rootSignature;
            Dx12Utility.CHECK_HR(device.NativeDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)&rootSignature));
            m_NativeRootSignature = rootSignature;
        }

        public Dx12BindTypeAndParameterSlot? QueryRootDescriptorParameterIndex(in EFunctionStage shaderStage, in uint layoutIndex, in uint slot, in EBindType bindType)
        {
            bool hasValue = false;
            Dx12BindTypeAndParameterSlot? outParameter = null;

            if ((shaderStage & EFunctionStage.All) == EFunctionStage.All)
            {
                hasValue = m_AllParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                outParameter = parameter;
            }

            if ((shaderStage & EFunctionStage.Vertex) == EFunctionStage.Vertex)
            {
                //hasValue = m_VertexParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                hasValue = m_VertexParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                outParameter = parameter;
            }

            if ((shaderStage & EFunctionStage.Fragment) == EFunctionStage.Fragment)
            {
                //hasValue = m_FragmentParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                hasValue = m_FragmentParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                outParameter = parameter;
            }

            if ((shaderStage & EFunctionStage.Compute) == EFunctionStage.Compute)
            {
                //hasValue = m_ComputeParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                hasValue = m_ComputeParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(bindType)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                outParameter = parameter;
            }

            return hasValue ? outParameter : null;
        }

        protected override void Release()
        {
            m_NativeRootSignature->Release();
        }
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

        public Dx12ComputePipeline(Dx12Device device, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            Dx12Function computeFunction = descriptor.ComputeFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;

            D3D12_COMPUTE_PIPELINE_STATE_DESC description = new D3D12_COMPUTE_PIPELINE_STATE_DESC();
            description.pRootSignature = pipelineLayout.NativeRootSignature;
            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.CS.BytecodeLength = computeFunction.NativeShaderBytecode.BytecodeLength;
            description.CS.pShaderBytecode = computeFunction.NativeShaderBytecode.pShaderBytecode;

            ID3D12PipelineState* pipelineState;
            bool success = SUCCEEDED(device.NativeDevice->CreateComputePipelineState(&description, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState));
            Debug.Assert(success);
            m_NativePipelineState = pipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12RaytracingPipeline : RHIRaytracingPipeline
    {
        public uint MaxLocalRootParameters
        {
            get
            {
                return m_MaxLocalRootParameters;
            }
        }
        public ID3D12StateObject* NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }

        private uint m_MaxLocalRootParameters;
        private ID3D12StateObject* m_NativePipelineState;

        public Dx12RaytracingPipeline(Dx12Device device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            m_MaxLocalRootParameters = 0;
            Span<RHIRayHitGroupDescriptor> rayHitGroupSpan = descriptor.RayHitGroupDescriptors.Span;
            Span<RHIRayGeneralGroupDescriptor> rayMissGroupSpan = descriptor.RayMissGroupDescriptors.Span;
            D3D12_STATE_SUBOBJECT* stateSubObjects = stackalloc D3D12_STATE_SUBOBJECT[descriptor.RayMissGroupDescriptors.Length * 2 + descriptor.RayHitGroupDescriptors.Length * 3 + 6];

            #region ExportDescriptors
            D3D12_EXPORT_DESC* exports = stackalloc D3D12_EXPORT_DESC[descriptor.RayHitGroupDescriptors.Length * 3 + descriptor.RayMissGroupDescriptors.Length];

            int exportCount = 0;
            ref D3D12_EXPORT_DESC rayGenerationExport = ref exports[exportCount];
            {
                rayGenerationExport.Name = (ushort*)Marshal.StringToHGlobalUni(descriptor.RayGenerationDescriptor.GeneralDescriptor.EntryName).ToPointer();
                rayGenerationExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                rayGenerationExport.ExportToRename = null;
            }

            for (int i = 0; i < descriptor.RayMissGroupDescriptors.Length; ++i) 
            {
                ref RHIRayGeneralGroupDescriptor rayMissGroupDescriptor = ref rayMissGroupSpan[i];

                ++exportCount;
                ref D3D12_EXPORT_DESC rayMissExport = ref exports[exportCount];
                rayMissExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayMissGroupDescriptor.GeneralDescriptor.EntryName).ToPointer();
                rayMissExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                rayMissExport.ExportToRename = null;
            }

            for (int i = 0; i < descriptor.RayHitGroupDescriptors.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor rayHitGroupDescriptor = ref rayHitGroupSpan[i];

                if(rayHitGroupDescriptor.AnyHitDescriptor.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC anyHitExport = ref exports[exportCount];
                    anyHitExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.AnyHitDescriptor?.EntryName).ToPointer();
                    anyHitExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                    anyHitExport.ExportToRename = null;
                }

                if (rayHitGroupDescriptor.IntersectDescriptor.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC intersectExport = ref exports[exportCount];
                    intersectExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.IntersectDescriptor?.EntryName).ToPointer();
                    intersectExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                    intersectExport.ExportToRename = null;
                }

                if (rayHitGroupDescriptor.ClosestHitDescriptor.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC closestHitExport = ref exports[exportCount];
                    closestHitExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.ClosestHitDescriptor?.EntryName).ToPointer();
                    closestHitExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                    closestHitExport.ExportToRename = null;
                }
            }
            #endregion ExportDescriptors

            #region DxilLibrary
            int stateSubObjectCount = 0;
            D3D12_DXIL_LIBRARY_DESC dxilLibraryDescriptor;
            {
                dxilLibraryDescriptor.pExports = exports;
                dxilLibraryDescriptor.NumExports = (uint)exportCount;
                dxilLibraryDescriptor.DXILLibrary = new D3D12_SHADER_BYTECODE(descriptor.FunctionLibrary.Descriptor.ByteCode.ToPointer(), descriptor.FunctionLibrary.Descriptor.ByteSize);
            }
            ref D3D12_STATE_SUBOBJECT dxilLibrary = ref stateSubObjects[stateSubObjectCount];
            dxilLibrary.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_DXIL_LIBRARY;
            dxilLibrary.pDesc = &dxilLibraryDescriptor;
            #endregion DxilLibrary

            #region RayGeneration
            Dx12PipelineLayout rayGenPipelineLayout = descriptor.RayGenerationDescriptor.PipelineLayout as Dx12PipelineLayout;
            if (rayGenPipelineLayout != null)
            {
                ++stateSubObjectCount;
                D3D12_LOCAL_ROOT_SIGNATURE rayGenRootSiganture;
                {
                    rayGenRootSiganture.pLocalRootSignature = rayGenPipelineLayout.NativeRootSignature;
                }
                ref D3D12_STATE_SUBOBJECT rayGenSignatureInfo = ref stateSubObjects[stateSubObjectCount];
                rayGenSignatureInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_LOCAL_ROOT_SIGNATURE;
                rayGenSignatureInfo.pDesc = &rayGenRootSiganture;

                ++stateSubObjectCount;
                D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION rayGenGroupDescriptor;
                {
                    rayGenGroupDescriptor.NumExports = 1;
                    rayGenGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(descriptor.RayGenerationDescriptor.GeneralDescriptor.EntryName).ToPointer();
                    rayGenGroupDescriptor.pSubobjectToAssociate = &stateSubObjects[stateSubObjectCount - 1];
                }
                ref D3D12_STATE_SUBOBJECT rayGenGroupInfo = ref stateSubObjects[stateSubObjectCount];
                rayGenGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                rayGenGroupInfo.pDesc = &rayGenGroupDescriptor;

                m_MaxLocalRootParameters = math.max(m_MaxLocalRootParameters, (uint)rayGenPipelineLayout.ParameterCount); 
            }
            else
            {
                ++stateSubObjectCount;
                D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION rayGenGroupDescriptor;
                {
                    rayGenGroupDescriptor.NumExports = 1;
                    rayGenGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(descriptor.RayGenerationDescriptor.GeneralDescriptor.EntryName).ToPointer();
                    rayGenGroupDescriptor.pSubobjectToAssociate = null;
                }
                ref D3D12_STATE_SUBOBJECT rayGenGroupInfo = ref stateSubObjects[stateSubObjectCount];
                rayGenGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                rayGenGroupInfo.pDesc = &rayGenGroupDescriptor;
            }
            #endregion RayGeneration

            #region MissGroup
            for (int i = 0; i < descriptor.RayMissGroupDescriptors.Length; ++i)
            {
                ref RHIRayGeneralGroupDescriptor rayMissGroupDescriptor = ref rayMissGroupSpan[i];
                Dx12PipelineLayout rayMissPipelineLayout = rayMissGroupDescriptor.PipelineLayout as Dx12PipelineLayout;

                if (rayMissPipelineLayout != null)
                {
                    ++stateSubObjectCount;
                    D3D12_LOCAL_ROOT_SIGNATURE rayMissRootSiganture;
                    {
                        rayMissRootSiganture.pLocalRootSignature = rayMissPipelineLayout.NativeRootSignature;
                    }
                    ref D3D12_STATE_SUBOBJECT rayMissSignatureInfo = ref stateSubObjects[stateSubObjectCount];
                    rayMissSignatureInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_LOCAL_ROOT_SIGNATURE;
                    rayMissSignatureInfo.pDesc = &rayMissRootSiganture;

                    ++stateSubObjectCount;
                    D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION missGroupDescriptor;
                    {
                        missGroupDescriptor.NumExports = 1;
                        missGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(rayMissGroupDescriptor.GeneralDescriptor.EntryName).ToPointer();
                        missGroupDescriptor.pSubobjectToAssociate = &stateSubObjects[stateSubObjectCount - 1];
                    }
                    ref D3D12_STATE_SUBOBJECT missGroupInfo = ref stateSubObjects[stateSubObjectCount];
                    missGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                    missGroupInfo.pDesc = &missGroupDescriptor;

                    m_MaxLocalRootParameters = math.max(m_MaxLocalRootParameters, (uint)rayMissPipelineLayout.ParameterCount);
                }
            }
            #endregion MissGroup

            #region HitGroup
            for (int i = 0; i < descriptor.RayHitGroupDescriptors.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor rayHitGroupDescriptor = ref rayHitGroupSpan[i];
                Dx12PipelineLayout rayHitPipelineLayout = rayHitGroupDescriptor.PipelineLayout as Dx12PipelineLayout;
                ushort* exportName = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.Name).ToPointer();

                // HitGroup 
                ++stateSubObjectCount;
                D3D12_HIT_GROUP_DESC hitGroupDescriptor;
                {
                    hitGroupDescriptor.Type = Dx12Utility.ConverteToDx12HitGroupType(rayHitGroupDescriptor.Type);
                    hitGroupDescriptor.HitGroupExport = exportName;
                    hitGroupDescriptor.AnyHitShaderImport = rayHitGroupDescriptor.AnyHitDescriptor.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.AnyHitDescriptor?.EntryName).ToPointer() : null;
                    hitGroupDescriptor.ClosestHitShaderImport = rayHitGroupDescriptor.ClosestHitDescriptor.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.ClosestHitDescriptor?.EntryName).ToPointer() : null;
                    hitGroupDescriptor.IntersectionShaderImport = rayHitGroupDescriptor.IntersectDescriptor.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.IntersectDescriptor?.EntryName).ToPointer() : null;
                }
                ref D3D12_STATE_SUBOBJECT hitGroupInfo = ref stateSubObjects[stateSubObjectCount];
                hitGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_HIT_GROUP;
                hitGroupInfo.pDesc = &hitGroupDescriptor;

                // Local RootSignature
                if (rayHitPipelineLayout != null)
                {
                    ++stateSubObjectCount;
                    D3D12_LOCAL_ROOT_SIGNATURE rayHitRootSiganture;
                    {
                        rayHitRootSiganture.pLocalRootSignature = rayHitPipelineLayout.NativeRootSignature;
                    }
                    ref D3D12_STATE_SUBOBJECT rayHitSignatureInfo = ref stateSubObjects[stateSubObjectCount];
                    rayHitSignatureInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_LOCAL_ROOT_SIGNATURE;
                    rayHitSignatureInfo.pDesc = &rayHitRootSiganture;

                    ++stateSubObjectCount;
                    D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION hitGroupRootDescriptor;
                    {
                        hitGroupRootDescriptor.NumExports = 1;
                        hitGroupRootDescriptor.pExports = (ushort**)exportName;
                        hitGroupRootDescriptor.pSubobjectToAssociate = &stateSubObjects[stateSubObjectCount - 1];
                    }
                    ref D3D12_STATE_SUBOBJECT missGroupInfo = ref stateSubObjects[stateSubObjectCount];
                    missGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                    missGroupInfo.pDesc = &hitGroupRootDescriptor;

                    m_MaxLocalRootParameters = math.max(m_MaxLocalRootParameters, (uint)rayHitPipelineLayout.ParameterCount);
                }
            }
            #endregion HitGroup

            #region ShaderConfig
            ++stateSubObjectCount;
            D3D12_RAYTRACING_SHADER_CONFIG shaderConfigDescriptor;
            {
                shaderConfigDescriptor.MaxPayloadSizeInBytes = descriptor.MaxPayloadSize;
                shaderConfigDescriptor.MaxAttributeSizeInBytes = descriptor.MaxAttributeSize;
            }
            ref D3D12_STATE_SUBOBJECT shaderConfig = ref stateSubObjects[stateSubObjectCount];
            shaderConfig.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_RAYTRACING_SHADER_CONFIG;
            shaderConfig.pDesc = &shaderConfigDescriptor;
            #endregion ShaderConfig

            #region PipelineConfig
            ++stateSubObjectCount;
            D3D12_RAYTRACING_PIPELINE_CONFIG pipelineConfigDescriptor;
            {
                pipelineConfigDescriptor.MaxTraceRecursionDepth = descriptor.MaxRecursionDepth;
            }
            ref D3D12_STATE_SUBOBJECT pipelineConfig = ref stateSubObjects[stateSubObjectCount];
            pipelineConfig.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_RAYTRACING_PIPELINE_CONFIG;
            pipelineConfig.pDesc = &pipelineConfigDescriptor;
            #endregion PipelineConfig

            #region GlobalRootSignature
            ++stateSubObjectCount;
            D3D12_GLOBAL_ROOT_SIGNATURE globalRootSignatureDescriptor;
            {
                globalRootSignatureDescriptor.pGlobalRootSignature = ((Dx12PipelineLayout)descriptor.PipelineLayout).NativeRootSignature;
            }
            ref D3D12_STATE_SUBOBJECT globalRootSignature = ref stateSubObjects[stateSubObjectCount];
            globalRootSignature.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_GLOBAL_ROOT_SIGNATURE;
            globalRootSignature.pDesc = &globalRootSignatureDescriptor;
            #endregion GlobalRootSignature

            #region PipelineState
            D3D12_STATE_OBJECT_DESC stateObjectDescriptor;
            {
                stateObjectDescriptor.pSubobjects = stateSubObjects;
                stateObjectDescriptor.NumSubobjects = (uint)stateSubObjectCount;
                stateObjectDescriptor.Type = D3D12_STATE_OBJECT_TYPE.D3D12_STATE_OBJECT_TYPE_RAYTRACING_PIPELINE;
            }
            ID3D12StateObject* pipelineState;
            bool success = SUCCEEDED(device.NativeDevice->CreateStateObject(&stateObjectDescriptor, __uuidof<ID3D12StateObject>(), (void**)&pipelineState));
            Debug.Assert(success);
            m_NativePipelineState = pipelineState;
            #endregion PipelineState
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12MeshletPipeline : RHIMeshletPipeline
    {
        public int StencilRef
        {
            get
            {
                return m_StencilRef;
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

        private int m_StencilRef;
        private ID3D12PipelineState* m_NativePipelineState;
        private D3D_PRIMITIVE_TOPOLOGY m_PrimitiveTopology;

        public Dx12MeshletPipeline(Dx12Device device, in RHIMeshletPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12GraphicsPipeline : RHIGraphicsPipeline
    {
        public uint StencilRef
        {
            get
            {
                return m_StencilRef;
            }
        }
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

        private uint m_StencilRef;
        private uint[] m_VertexStrides;
        private ID3D12PipelineState* m_NativePipelineState;
        private D3D_PRIMITIVE_TOPOLOGY m_PrimitiveTopology;

        public Dx12GraphicsPipeline(Dx12Device device, in RHIGraphicsPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            Dx12Function vertexFunction = descriptor.VertexFunction as Dx12Function;
            Dx12Function fragmentFunction = descriptor.FragmentFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;

            Span<RHIVertexLayoutDescriptor> vertexLayoutDescriptors = descriptor.VertexLayoutDescriptors.Span;

            if ((vertexFunction != null))
            {
                m_VertexStrides = new uint[vertexLayoutDescriptors.Length];
                for (int j = 0; j < vertexLayoutDescriptors.Length; ++j)
                {
                    m_VertexStrides[j] = vertexLayoutDescriptors[j].Stride;
                }
            }

            m_StencilRef = descriptor.RenderStateDescriptor.DepthStencilStateDescriptor.StencilReference;
            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveTopology);

            D3D12_PRIMITIVE_TOPOLOGY_TYPE primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveTopology);

            int inputElementCount = Dx12Utility.GetDx12VertexLayoutCount(vertexLayoutDescriptors);
            D3D12_INPUT_ELEMENT_DESC* inputElementsPtr = stackalloc D3D12_INPUT_ELEMENT_DESC[inputElementCount];
            Span<D3D12_INPUT_ELEMENT_DESC> inputElementsView = new Span<D3D12_INPUT_ELEMENT_DESC>(inputElementsPtr, inputElementCount);

            Dx12Utility.ConvertToDx12VertexLayout(vertexLayoutDescriptors, inputElementsView);

            D3D12_INPUT_LAYOUT_DESC outputLayout;
            outputLayout.NumElements = (uint)inputElementCount;
            outputLayout.pInputElementDescs = inputElementsPtr;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC description = new D3D12_GRAPHICS_PIPELINE_STATE_DESC
            {
                InputLayout = outputLayout,
                pRootSignature = pipelineLayout.NativeRootSignature,
                PrimitiveTopologyType = primitiveTopologyType,

                SampleMask = descriptor.RenderStateDescriptor.SampleMask.HasValue ? ((uint)descriptor.RenderStateDescriptor.SampleMask.Value) : uint.MaxValue,
                BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderStateDescriptor.BlendStateDescriptor),
                RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderStateDescriptor.RasterizerStateDescriptor, descriptor.OutputStateDescriptor.SampleCount != ESampleCount.None),
                DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderStateDescriptor.DepthStencilStateDescriptor)
            };

            if (descriptor.OutputStateDescriptor.OutputDepthAttachmentDescriptor.HasValue)
            {
                description.DSVFormat = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;
                //description.DSVFormat = Dx12Utility.ConvertToDx12Format(descriptor.outputState.depthAttachment.Value.format);
            }

            for (int i = 0; i < descriptor.OutputStateDescriptor.OutputColorAttachmentDescriptors.Length; ++i)
            {
                description.RTVFormats[i] = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
                //description.RTVFormats[i] = Dx12Utility.ConvertToDx12Format(descriptor.outputState.colorAttachments.Span[i].format);
            }

            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.NumRenderTargets = (uint)descriptor.OutputStateDescriptor.OutputColorAttachmentDescriptors.Length;
            description.SampleDesc = Dx12Utility.ConvertToDx12SampleCount(descriptor.OutputStateDescriptor.SampleCount);
            //description.StreamOutput = new StreamOutputDescription();

            if (descriptor.VertexFunction != null)
            {
                description.VS.BytecodeLength = vertexFunction.NativeShaderBytecode.BytecodeLength;
                description.VS.pShaderBytecode = vertexFunction.NativeShaderBytecode.pShaderBytecode;
            }

            if (descriptor.FragmentFunction != null)
            {
                description.PS.BytecodeLength = fragmentFunction.NativeShaderBytecode.BytecodeLength;
                description.PS.pShaderBytecode = fragmentFunction.NativeShaderBytecode.pShaderBytecode;
            }

            ID3D12PipelineState* pipelineState;
            bool success = SUCCEEDED(device.NativeDevice->CreateGraphicsPipelineState(&description, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState));
            Debug.Assert(success);
            m_NativePipelineState = pipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }
#pragma warning restore CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
}
