using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;
using TerraFX.Interop.Gdiplus;

namespace Infinity.Graphics
{
#pragma warning disable CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
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

#if false
            D3D12_COMPUTE_PIPELINE_STATE_DESC description = new D3D12_COMPUTE_PIPELINE_STATE_DESC();
            description.pRootSignature = pipelineLayout.NativeRootSignature;
            description.Flags = D3D12_PIPELINE_STATE_FLAGS.D3D12_PIPELINE_STATE_FLAG_NONE;
            description.CS.BytecodeLength = computeFunction.NativeShaderBytecode.BytecodeLength;
            description.CS.pShaderBytecode = computeFunction.NativeShaderBytecode.pShaderBytecode;

            ID3D12PipelineState* pipelineState;
            HRESULT hResult = device.NativeDevice->CreateComputePipelineState(&description, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState);
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

            ID3D12PipelineState* pipelineState;
            HRESULT hResult = device.NativeDevice->CreatePipelineState(&streamDesc, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState);
#endif

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
                else
                {
                    ++stateSubObjectCount;
                    D3D12_SUBOBJECT_TO_EXPORTS_ASSOCIATION missGroupDescriptor;
                    {
                        missGroupDescriptor.NumExports = 1;
                        missGroupDescriptor.pExports = (ushort**)Marshal.StringToHGlobalUni(rayMissGroupDescriptor.General.EntryName).ToPointer();
                        missGroupDescriptor.pSubobjectToAssociate = null;
                    }
                    ref D3D12_STATE_SUBOBJECT missGroupInfo = ref stateSubObjects[stateSubObjectCount];
                    missGroupInfo.Type = D3D12_STATE_SUBOBJECT_TYPE.D3D12_STATE_SUBOBJECT_TYPE_SUBOBJECT_TO_EXPORTS_ASSOCIATION;
                    missGroupInfo.pDesc = &missGroupDescriptor;
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
            m_PrimitiveTopology = Dx12Utility.ConvertToDx12PrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology);

            Dx12Function fragmentFunction = descriptor.FragmentFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;
            D3D12_PRIMITIVE_TOPOLOGY_TYPE primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveAssembler.PrimitiveTopology);

            switch (descriptor.PrimitiveAssembler.PrimitiveType)
            {
                case ERHIPrimitiveType.Mesh:
                    if (descriptor.PrimitiveAssembler.MeshAssembler.HasValue)
                    {
                        Dx12Function taskFunction = descriptor.PrimitiveAssembler.MeshAssembler.Value.TaskFunction as Dx12Function;
                        Dx12Function meshFunction = descriptor.PrimitiveAssembler.MeshAssembler.Value.MeshFunction as Dx12Function;

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

                        ID3D12PipelineState* pipelineState;
                        HRESULT hResult = device.NativeDevice->CreatePipelineState(&streamDesc, __uuidof<ID3D12PipelineState>(), (void**)&pipelineState);

#if DEBUG
                        Dx12Utility.CHECK_HR(hResult);
#endif
                        m_NativePipelineState = pipelineState;
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
#pragma warning restore CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
}
