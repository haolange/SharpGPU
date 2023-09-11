﻿using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12ComputePipelineState : RHIComputePipelineState
    {
        public ID3D12PipelineState* NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }

        private ID3D12PipelineState* m_NativePipelineState;

        public Dx12ComputePipelineState(Dx12Device device, in RHIComputePipelineStateDescriptor descriptor)
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
            HRESULT hResult = device.NativeDevice->CreateComputePipelineState(&description, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineState = pipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12RaytracingPipelineState : RHIRaytracingPipelineState
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

        public Dx12RaytracingPipelineState(Dx12Device device, in RHIRaytracingPipelineStateDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            m_MaxLocalRootParameters = 0;
            Span<RHIRayHitGroupDescriptor> rayHitGroupSpan = descriptor.RayHitGroups.Span;
            Span<RHIRayGeneralGroupDescriptor> rayMissGroupSpan = descriptor.RayMissGroups.Span;
            D3D12_STATE_SUBOBJECT* stateSubObjects = stackalloc D3D12_STATE_SUBOBJECT[descriptor.RayMissGroups.Length * 2 + descriptor.RayHitGroups.Length * 3 + 6];

            #region ExportDescriptors
            D3D12_EXPORT_DESC* exports = stackalloc D3D12_EXPORT_DESC[descriptor.RayHitGroups.Length * 3 + descriptor.RayMissGroups.Length];

            int exportCount = 0;
            ref D3D12_EXPORT_DESC rayGenerationExport = ref exports[exportCount];
            {
                rayGenerationExport.Name = (ushort*)Marshal.StringToHGlobalUni(descriptor.RayGeneration.General.EntryName).ToPointer();
                rayGenerationExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                rayGenerationExport.ExportToRename = null;
            }

            for (int i = 0; i < descriptor.RayMissGroups.Length; ++i) 
            {
                ref RHIRayGeneralGroupDescriptor rayMissGroupDescriptor = ref rayMissGroupSpan[i];

                ++exportCount;
                ref D3D12_EXPORT_DESC rayMissExport = ref exports[exportCount];
                rayMissExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayMissGroupDescriptor.General.EntryName).ToPointer();
                rayMissExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                rayMissExport.ExportToRename = null;
            }

            for (int i = 0; i < descriptor.RayHitGroups.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor rayHitGroupDescriptor = ref rayHitGroupSpan[i];

                if(rayHitGroupDescriptor.AnyHit.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC anyHitExport = ref exports[exportCount];
                    anyHitExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.AnyHit?.EntryName).ToPointer();
                    anyHitExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                    anyHitExport.ExportToRename = null;
                }

                if (rayHitGroupDescriptor.Intersect.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC intersectExport = ref exports[exportCount];
                    intersectExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.Intersect?.EntryName).ToPointer();
                    intersectExport.Flags = D3D12_EXPORT_FLAGS.D3D12_EXPORT_FLAG_NONE;
                    intersectExport.ExportToRename = null;
                }

                if (rayHitGroupDescriptor.ClosestHit.HasValue)
                {
                    ++exportCount;
                    ref D3D12_EXPORT_DESC closestHitExport = ref exports[exportCount];
                    closestHitExport.Name = (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.ClosestHit?.EntryName).ToPointer();
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
            Dx12PipelineLayout rayGenPipelineLayout = descriptor.RayGeneration.PipelineLayout as Dx12PipelineLayout;
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
                    rayGenGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(descriptor.RayGeneration.General.EntryName).ToPointer();
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
                    rayGenGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(descriptor.RayGeneration.General.EntryName).ToPointer();
                    rayGenGroupDescriptor.pSubobjectToAssociate = null;
                }
                ref D3D12_STATE_SUBOBJECT rayGenGroupInfo = ref stateSubObjects[stateSubObjectCount];
                rayGenGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                rayGenGroupInfo.pDesc = &rayGenGroupDescriptor;
            }
            #endregion RayGeneration

            #region MissGroup
            for (int i = 0; i < descriptor.RayMissGroups.Length; ++i)
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
                        missGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(rayMissGroupDescriptor.General.EntryName).ToPointer();
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
            for (int i = 0; i < descriptor.RayHitGroups.Length; ++i)
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
                    hitGroupDescriptor.AnyHitShaderImport = rayHitGroupDescriptor.AnyHit.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.AnyHit?.EntryName).ToPointer() : null;
                    hitGroupDescriptor.ClosestHitShaderImport = rayHitGroupDescriptor.ClosestHit.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.ClosestHit?.EntryName).ToPointer() : null;
                    hitGroupDescriptor.IntersectionShaderImport = rayHitGroupDescriptor.Intersect.HasValue ? (ushort*)Marshal.StringToHGlobalUni(rayHitGroupDescriptor.Intersect?.EntryName).ToPointer() : null;
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
            HRESULT hResult = device.NativeDevice->CreateStateObject(&stateObjectDescriptor, __uuidof<ID3D12StateObject>(), (void**)&pipelineState);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineState = pipelineState;
            #endregion PipelineState
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12MeshletPipelineState : RHIMeshletPipelineState
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

        public Dx12MeshletPipelineState(Dx12Device device, in RHIMeshletPipelineStateDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }

    internal unsafe class Dx12GraphicsPipelineState : RHIGraphicsPipelineState
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

        public Dx12GraphicsPipelineState(Dx12Device device, in RHIGraphicsPipelineStateDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            Dx12Function vertexFunction = descriptor.VertexFunction as Dx12Function;
            Dx12Function fragmentFunction = descriptor.FragmentFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;

            Span<RHIVertexLayoutDescriptor> vertexLayouts = descriptor.VertexLayouts.Span;

            if ((vertexFunction != null))
            {
                m_VertexStrides = new uint[vertexLayouts.Length];
                for (int j = 0; j < vertexLayouts.Length; ++j)
                {
                    m_VertexStrides[j] = vertexLayouts[j].Stride;
                }
            }

            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveTopology);

            D3D12_PRIMITIVE_TOPOLOGY_TYPE primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveTopology);

            int inputElementCount = Dx12Utility.GetDx12VertexLayoutCount(vertexLayouts);
            D3D12_INPUT_ELEMENT_DESC* inputElementsPtr = stackalloc D3D12_INPUT_ELEMENT_DESC[inputElementCount];
            Span<D3D12_INPUT_ELEMENT_DESC> inputElementsView = new Span<D3D12_INPUT_ELEMENT_DESC>(inputElementsPtr, inputElementCount);

            Dx12Utility.ConvertToDx12VertexLayout(vertexLayouts, inputElementsView);

            D3D12_INPUT_LAYOUT_DESC outputLayout;
            outputLayout.NumElements = (uint)inputElementCount;
            outputLayout.pInputElementDescs = inputElementsPtr;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC description = new D3D12_GRAPHICS_PIPELINE_STATE_DESC
            {
                InputLayout = outputLayout,
                pRootSignature = pipelineLayout.NativeRootSignature,
                PrimitiveTopologyType = primitiveTopologyType,

                SampleMask = descriptor.RenderState.SampleMask.HasValue ? ((uint)descriptor.RenderState.SampleMask.Value) : uint.MaxValue,
                BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderState.BlendState),
                RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderState.RasterizerState, descriptor.OutputState.SampleCount != ERHISampleCount.None),
                DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderState.DepthStencilState)
            };

            if (descriptor.OutputState.DepthStencilFormat != ERHIPixelFormat.Unknown)
            {
                description.DSVFormat = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT;
                description.DSVFormat = Dx12Utility.ConvertToDx12Format(descriptor.OutputState.DepthStencilFormat);
            }

            fixed (ERHIPixelFormat* formatPtr = &descriptor.OutputState.ColorFormat0)
            {
                for (int i = 0; i < descriptor.OutputState.OutputCount; ++i)
                {
                    description.RTVFormats[i] = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
                    description.RTVFormats[i] = Dx12Utility.ConvertToDx12ViewFormat(formatPtr[i]);
                }
            }

            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.NumRenderTargets = descriptor.OutputState.OutputCount;
            description.SampleDesc = Dx12Utility.ConvertToDx12SampleCount(descriptor.OutputState.SampleCount);
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
            HRESULT hResult = device.NativeDevice->CreateGraphicsPipelineState(&description, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineState = pipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState->Release();
        }
    }
#pragma warning restore CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
}