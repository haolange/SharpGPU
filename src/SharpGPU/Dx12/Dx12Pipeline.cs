using System;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
        public Vortice.Direct3D12.ID3D12RootSignature NativeRootSignature
        {
            get
            {
                return m_NativeRootSignature;
            }
        }

        private int m_ParameterCount;
        private uint m_PushConstantRootParameterIndex;
        private uint m_PushConstantSize;
        private Vortice.Direct3D12.ID3D12RootSignature m_NativeRootSignature;
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

            Vortice.Direct3D12.DescriptorRange1* rootDescriptorRangePtr = stackalloc Vortice.Direct3D12.DescriptorRange1[m_ParameterCount];
            Span<Vortice.Direct3D12.DescriptorRange1> rootDescriptorRangeViews = new Span<Vortice.Direct3D12.DescriptorRange1>(rootDescriptorRangePtr, m_ParameterCount);

            Vortice.Direct3D12.RootParameter1* rootParameterPtr = stackalloc Vortice.Direct3D12.RootParameter1[totalRootParameters];
            Span<Vortice.Direct3D12.RootParameter1> rootParameterViews = new Span<Vortice.Direct3D12.RootParameter1>(rootParameterPtr, totalRootParameters);

            for (int i = 0; i < descriptor.ArgumentTableLayouts.Length; ++i)
            {
                Dx12ArgumentTableLayout resourceTableLayout = descriptor.ArgumentTableLayouts[i] as Dx12ArgumentTableLayout;

                for (int j = 0; j < resourceTableLayout.BindInfos.Length; ++j)
                {
                    ref Dx12BindInfo bindInfo = ref resourceTableLayout.BindInfos[j];

                    ref Vortice.Direct3D12.DescriptorRange1 rootDescriptorRange = ref rootDescriptorRangeViews[i + j];
                    rootDescriptorRange.RangeType = Dx12Utility.ConvertToDx12BindType(bindInfo.Type);
                    rootDescriptorRange.NumDescriptors = bindInfo.IsBindless ? bindInfo.Count : 1;
                    rootDescriptorRange.BaseShaderRegister = bindInfo.Slot;
                    rootDescriptorRange.RegisterSpace = bindInfo.Index;
                    rootDescriptorRange.Flags = Dx12Utility.GetDx12DescriptorRangeFalag(bindInfo.Type);
                    rootDescriptorRange.OffsetInDescriptorsFromTableStart = Vortice.Direct3D12.D3D12.DescriptorRangeOffsetAppend;

                    ref Vortice.Direct3D12.RootParameter1 rootParameterView = ref rootParameterViews[i + j];
                    Vortice.Direct3D12.DescriptorRange1[] descriptorRanges = new Vortice.Direct3D12.DescriptorRange1[] { rootDescriptorRangePtr[i + j] };
                    rootParameterView = new Vortice.Direct3D12.RootParameter1(new Vortice.Direct3D12.RootDescriptorTable1(descriptorRanges), Dx12Utility.ConvertToDx12ShaderType(bindInfo.Stage));

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

                    if ((bindInfo.Stage & ERHIShaderStage.Compute) == ERHIShaderStage.Compute
                        || (bindInfo.Stage & ERHIShaderStage.RayTracing) == ERHIShaderStage.RayTracing)
                    {
                        // DX12 RT pass uses SetComputeRoot* APIs; map RT-stage descriptors into compute parameter map.
                        m_ComputeParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }
                }
            }

            if (hasPushConstants)
            {
                m_PushConstantRootParameterIndex = (uint)m_ParameterCount;
                ref Vortice.Direct3D12.RootParameter1 pushConstantParam = ref rootParameterViews[m_ParameterCount];
                pushConstantParam = new Vortice.Direct3D12.RootParameter1(new Vortice.Direct3D12.RootConstants(0, 0, descriptor.PushConstantSize / 4), Vortice.Direct3D12.ShaderVisibility.All);
            }

            Vortice.Direct3D12.RootSignatureFlags rootSignatureFlag = Vortice.Direct3D12.RootSignatureFlags.None;
            rootSignatureFlag |= Vortice.Direct3D12.RootSignatureFlags.DenyHullShaderRootAccess;
            rootSignatureFlag |= Vortice.Direct3D12.RootSignatureFlags.DenyDomainShaderRootAccess;
            rootSignatureFlag |= Vortice.Direct3D12.RootSignatureFlags.DenyGeometryShaderRootAccess;

            if (descriptor.bLocalSignature)
            {
                rootSignatureFlag |= Vortice.Direct3D12.RootSignatureFlags.LocalRootSignature;
            }
            if (descriptor.bUseVertexLayout)
            {
                rootSignatureFlag |= Vortice.Direct3D12.RootSignatureFlags.AllowInputAssemblerInputLayout;
            }

            Vortice.Direct3D12.RootParameter1[] rootParameters = new Vortice.Direct3D12.RootParameter1[totalRootParameters];
            for (int i = 0; i < totalRootParameters; ++i)
            {
                rootParameters[i] = rootParameterPtr[i];
            }

            Vortice.Direct3D12.VersionedRootSignatureDescription rootSignatureDesc = new Vortice.Direct3D12.VersionedRootSignatureDescription(
                new Vortice.Direct3D12.RootSignatureDescription1(
                    rootSignatureFlag,
                    rootParameters,
                    Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob signature;
            string rootSigError = Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(rootSignatureDesc, out signature);
            Dx12Utility.CHECK_BOOL(string.IsNullOrEmpty(rootSigError));

            Vortice.Direct3D12.ID3D12RootSignature rootSignature;
            Dx12Utility.CHECK_HR(device.NativeDevice.CreateRootSignature(0, signature.BufferPointer, signature.BufferSize, out rootSignature));
            signature.Release();
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

            if ((shaderStage & ERHIShaderStage.RayTracing) == ERHIShaderStage.RayTracing)
            {
                bool hasValue = m_ComputeParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                if (hasValue)
                {
                    return parameter;
                }

                hasValue = m_AllParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out parameter);
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
            m_NativeRootSignature.Release();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_RT_FORMAT_ARRAY
    {
        private fixed int m_Formats[8];
        public uint NumRenderTargets;

        public D3D12_RT_FORMAT_ARRAY(Vortice.DXGI.Format* formats, uint numRenderTargets)
        {
            NumRenderTargets = Math.Min(8u, numRenderTargets);
            fixed (int* dst = m_Formats)
            {
                for (int i = 0; i < 8; ++i)
                {
                    dst[i] = 0;
                }

                for (int i = 0; i < NumRenderTargets; ++i)
                {
                    dst[i] = (int)formats[i];
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_CUSTOM_COMPUTE_PIPELINE_STATE_DESC
    {
        public Vortice.Direct3D12.PipelineStateSubObjectType RootSignature_Type;
        public Vortice.Direct3D12.ID3D12RootSignature pRootSignature;
        public Vortice.Direct3D12.PipelineStateSubObjectType CS_Type;
        public Vortice.Direct3D12.ShaderBytecode CS;
        public Vortice.Direct3D12.PipelineStateSubObjectType Flags_Type;
        public Vortice.Direct3D12.PipelineStateFlags Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct D3D12_MESH_PIPELINE_STATE_DESC
    {
        public Vortice.Direct3D12.PipelineStateSubObjectType RootSignature_Type;
        public Vortice.Direct3D12.ID3D12RootSignature pRootSignature;
        public Vortice.Direct3D12.PipelineStateSubObjectType PrimitiveTopology_Type; 
        public Vortice.Direct3D12.PrimitiveTopologyType PrimitiveTopologyType;
        public Vortice.Direct3D12.PipelineStateSubObjectType TaskShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode TaskShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType MeshShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode MeshShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType PixelShader_Type; 
        public Vortice.Direct3D12.ShaderBytecode PixelShader;
        public Vortice.Direct3D12.PipelineStateSubObjectType RasterizerState_Type; 
        public Vortice.Direct3D12.RasterizerDescription RasterizerState;
        public Vortice.Direct3D12.PipelineStateSubObjectType DepthStencilState_Type; 
        public Vortice.Direct3D12.DepthStencilDescription DepthStencilState;
        public Vortice.Direct3D12.PipelineStateSubObjectType BlendState_Type; 
        public Vortice.Direct3D12.BlendDescription BlendState;
        public Vortice.Direct3D12.PipelineStateSubObjectType SampleDesc_Type; 
        public Vortice.DXGI.SampleDescription SampleDesc;
        public Vortice.Direct3D12.PipelineStateSubObjectType SampleMask_Type; 
        public uint SampleMask;
        public Vortice.Direct3D12.PipelineStateSubObjectType RTVFormats_Type; 
        public D3D12_RT_FORMAT_ARRAY RTVFormats;
        public Vortice.Direct3D12.PipelineStateSubObjectType DSVFormat_Type;
        public Vortice.DXGI.Format DSVFormat;
    }

    internal static unsafe class Dx12PipelineDebug
    {
        private const ulong MaxMessagesToDump = 32;

        public static void DumpDeviceMessages(Dx12Device device, string scope)
        {
            Vortice.Direct3D12.Debug.ID3D12InfoQueue infoQueue = device.NativeDevice.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12InfoQueue>();
            if (infoQueue == null)
            {
                Console.WriteLine($"{scope} Failed to query Vortice.Direct3D12.Debug.ID3D12InfoQueue.");
                return;
            }

            try
            {
                ulong messageCount = infoQueue.NumStoredMessagesAllowedByRetrievalFilter;
                if (messageCount == 0)
                {
                    Console.WriteLine($"{scope} Vortice.Direct3D12.D3D12 info queue has no stored messages.");
                    return;
                }

                ulong startIndex = messageCount > MaxMessagesToDump ? messageCount - MaxMessagesToDump : 0;
                for (ulong messageIndex = startIndex; messageIndex < messageCount; ++messageIndex)
                {
                    Vortice.Direct3D12.Debug.Message message = infoQueue.GetMessage(messageIndex);
                    string text = message.Description ?? string.Empty;
                    Console.WriteLine($"{scope} [Vortice.Direct3D12.D3D12 {message.Severity}] {text}");
                }

                infoQueue.ClearStoredMessages();
            }
            finally
            {
                infoQueue.Release();
            }
        }
    }

    internal unsafe class Dx12ComputePipeline : RHIComputePipeline
    {
        public Vortice.Direct3D12.ID3D12PipelineState NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }

        private Vortice.Direct3D12.ID3D12PipelineState m_NativePipelineState;

        internal Dx12ComputePipeline(Vortice.Direct3D12.ID3D12PipelineState nativePipelineState, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_NativePipelineState = nativePipelineState;
        }

        public Dx12ComputePipeline(Dx12Device device, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            Dx12Function computeFunction = descriptor.ComputeFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;
            if (computeFunction == null)
            {
                throw new InvalidOperationException("Dx12ComputePipeline requires a Dx12Function compute shader.");
            }
            if (pipelineLayout == null)
            {
                throw new InvalidOperationException("Dx12ComputePipeline requires a Dx12PipelineLayout.");
            }

            Vortice.Direct3D12.ComputePipelineStateDescription description = new Vortice.Direct3D12.ComputePipelineStateDescription
            {
                RootSignature = pipelineLayout.NativeRootSignature,
                ComputeShader = computeFunction.NativeShaderBytecode.Data,
                Flags = Vortice.Direct3D12.PipelineStateFlags.None,
            };

            Vortice.Direct3D12.ID3D12PipelineState nativePipelineState;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateComputePipelineState(description, out nativePipelineState);

#if DEBUG
            if (hResult.Failure)
            {
                Console.WriteLine($"[Dx12ComputePipeline] CreateComputePipelineState failed. SharpGen.Runtime.Result=0x{hResult:X8}");
                Dx12PipelineDebug.DumpDeviceMessages(device, "[Dx12ComputePipeline]");
            }
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineState = nativePipelineState;
        }

        protected override void Release()
        {
            m_NativePipelineState.Release();
        }
    }

    internal unsafe class Dx12RaytracingPipeline : RHIRaytracingPipeline
    {
        public Vortice.Direct3D12.ID3D12StateObject NativePipeline => m_NativePipeline;
        public Vortice.Direct3D12.ID3D12StateObjectProperties NativeStateObjectProperties => m_NativeStateObjectProperties;

        private Vortice.Direct3D12.ID3D12RootSignature m_LocalConstantsRootSignature;
        private Vortice.Direct3D12.ID3D12StateObject m_NativePipeline;
        private Vortice.Direct3D12.ID3D12StateObjectProperties m_NativeStateObjectProperties;
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

            Dx12FunctionLibrary functionLibrary = descriptor.FunctionLibrary as Dx12FunctionLibrary;
            if (functionLibrary == null)
            {
                throw new InvalidOperationException("Dx12RaytracingPipeline requires a Dx12FunctionLibrary.");
            }

            Dx12PipelineLayout globalPipelineLayout = descriptor.PipelineLayout as Dx12PipelineLayout;
            if (globalPipelineLayout == null)
            {
                throw new InvalidOperationException("Dx12RaytracingPipeline requires a Dx12PipelineLayout.");
            }

            List<Vortice.Direct3D12.ExportDescription> exportDescriptors = new List<Vortice.Direct3D12.ExportDescription>(1 + missGroups.Length + callableGroups.Length + hitGroups.Length * 3);
            static void AddLibraryExport(List<Vortice.Direct3D12.ExportDescription> exports, string entryName)
            {
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException("Ray-tracing library export entry name is empty.");
                }

                exports.Add(new Vortice.Direct3D12.ExportDescription(entryName, null, Vortice.Direct3D12.ExportFlags.None));
            }

            AddLibraryExport(exportDescriptors, m_RayGenerationExport);
            for (int i = 0; i < missGroups.Length; ++i)
            {
                AddLibraryExport(exportDescriptors, missGroups[i].General.EntryName);
            }

            for (int i = 0; i < callableGroups.Length; ++i)
            {
                AddLibraryExport(exportDescriptors, callableGroups[i].General.EntryName);
            }

            for (int i = 0; i < hitGroups.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                if (hitGroup.AnyHit.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.AnyHit.Value.EntryName);
                }

                if (hitGroup.Intersect.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.Intersect.Value.EntryName);
                }

                if (hitGroup.ClosestHit.HasValue)
                {
                    AddLibraryExport(exportDescriptors, hitGroup.ClosestHit.Value.EntryName);
                }
            }

            Vortice.Direct3D12.DxilLibraryDescription dxilLibraryDescription = new Vortice.Direct3D12.DxilLibraryDescription(functionLibrary.NativeShaderBytecode.Data, exportDescriptors.ToArray());
            List<Vortice.Direct3D12.StateSubObject> stateSubObjects = new List<Vortice.Direct3D12.StateSubObject>(1 + hitGroups.Length + 3 + (descriptor.LocalDataStrideInBytes > 0 ? 2 : 0));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(dxilLibraryDescription));

            for (int i = 0; i < hitGroups.Length; ++i)
            {
                ref RHIRayHitGroupDescriptor hitGroup = ref hitGroups[i];
                Vortice.Direct3D12.HitGroupDescription hitGroupDescription = new Vortice.Direct3D12.HitGroupDescription(
                    hitGroup.Name,
                    Dx12Utility.ConverteToDx12HitGroupType(hitGroup.Type),
                    hitGroup.AnyHit.HasValue ? hitGroup.AnyHit.Value.EntryName : null,
                    hitGroup.ClosestHit.HasValue ? hitGroup.ClosestHit.Value.EntryName : null,
                    hitGroup.Intersect.HasValue ? hitGroup.Intersect.Value.EntryName : null);
                stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(hitGroupDescription));
            }

            if (descriptor.LocalDataStrideInBytes > 0)
            {
                m_LocalConstantsRootSignature = BuildLocalConstantsRootSignature(device, descriptor.LocalDataStrideInBytes);
                Vortice.Direct3D12.StateSubObject localRootSignatureSubObject = new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.LocalRootSignature(m_LocalConstantsRootSignature));
                stateSubObjects.Add(localRootSignatureSubObject);

                List<string> associationExports = new List<string>(1 + missGroups.Length + hitGroups.Length + callableGroups.Length)
                {
                    m_RayGenerationExport,
                };
                for (int i = 0; i < missGroups.Length; ++i)
                {
                    associationExports.Add(missGroups[i].General.EntryName);
                }
                for (int i = 0; i < hitGroups.Length; ++i)
                {
                    associationExports.Add(hitGroups[i].Name);
                }
                for (int i = 0; i < callableGroups.Length; ++i)
                {
                    associationExports.Add(callableGroups[i].General.EntryName);
                }

                Vortice.Direct3D12.SubObjectToExportsAssociation associationDescription = new Vortice.Direct3D12.SubObjectToExportsAssociation(localRootSignatureSubObject, associationExports.ToArray());
                stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(associationDescription));
            }

            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.RaytracingShaderConfig(descriptor.MaxPayloadSize, descriptor.MaxAttributeSize)));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.RaytracingPipelineConfig(descriptor.MaxRecursionDepth)));
            stateSubObjects.Add(new Vortice.Direct3D12.StateSubObject(new Vortice.Direct3D12.GlobalRootSignature(globalPipelineLayout.NativeRootSignature)));

            Vortice.Direct3D12.StateObjectDescription stateObjectDesc = new Vortice.Direct3D12.StateObjectDescription(Vortice.Direct3D12.StateObjectType.RaytracingPipeline, stateSubObjects.ToArray());

            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateStateObject(stateObjectDesc, out Vortice.Direct3D12.ID3D12StateObject nativePipeline);
#if DEBUG
            if (hResult.Failure)
            {
                Console.WriteLine($"[Dx12RaytracingPipeline] CreateStateObject failed. SharpGen.Runtime.Result=0x{hResult:X8}");
                Dx12PipelineDebug.DumpDeviceMessages(device, "[Dx12RaytracingPipeline]");
            }
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipeline = nativePipeline;
            m_NativeStateObjectProperties = m_NativePipeline.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12StateObjectProperties>();
            if (m_NativeStateObjectProperties == null)
            {
                throw new InvalidOperationException("Failed to query Vortice.Direct3D12.ID3D12StateObjectProperties from raytracing pipeline state object.");
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

        private static Vortice.Direct3D12.ID3D12RootSignature BuildLocalConstantsRootSignature(Dx12Device device, in uint localDataStrideInBytes)
        {
            uint dwordCount = Math.Max(1u, (localDataStrideInBytes + 3u) / 4u);
            Vortice.Direct3D12.RootParameter1 localRootParameter = default;
            localRootParameter = new Vortice.Direct3D12.RootParameter1(new Vortice.Direct3D12.RootConstants(0, 0, dwordCount), Vortice.Direct3D12.ShaderVisibility.All);

            Vortice.Direct3D12.VersionedRootSignatureDescription rootSigDesc = new Vortice.Direct3D12.VersionedRootSignatureDescription(
                new Vortice.Direct3D12.RootSignatureDescription1(
                    Vortice.Direct3D12.RootSignatureFlags.LocalRootSignature,
                    new[] { localRootParameter },
                    Array.Empty<Vortice.Direct3D12.StaticSamplerDescription>()));

            Vortice.Direct3D.Blob signatureBlob;
            string rootSigError = Vortice.Direct3D12.D3D12.D3D12SerializeVersionedRootSignature(rootSigDesc, out signatureBlob);
            Dx12Utility.CHECK_BOOL(string.IsNullOrEmpty(rootSigError));

            Vortice.Direct3D12.ID3D12RootSignature localRootSignature = null;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateRootSignature(0, signatureBlob.BufferPointer, signatureBlob.BufferSize, out localRootSignature);
            signatureBlob.Release();
            Dx12Utility.CHECK_HR(hResult);

            return localRootSignature;
        }

        protected override void Release()
        {
            if (m_NativeStateObjectProperties != null)
            {
                m_NativeStateObjectProperties.Release();
                m_NativeStateObjectProperties = null;
            }

            if (m_NativePipeline != null)
            {
                m_NativePipeline.Release();
                m_NativePipeline = null;
            }

            if (m_LocalConstantsRootSignature != null)
            {
                m_LocalConstantsRootSignature.Release();
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
        public Vortice.Direct3D12.ID3D12PipelineState NativePipelineState
        {
            get
            {
                return m_NativePipelineState;
            }
        }
        public Vortice.Direct3D.PrimitiveTopology PrimitiveTopology
        {
            get
            {
                return m_PrimitiveTopology;
            }
        }

        private uint[] m_VertexStrides;
        private Vortice.Direct3D12.ID3D12PipelineState m_NativePipelineState;
        private Vortice.Direct3D.PrimitiveTopology m_PrimitiveTopology;

        internal Dx12RasterPipeline(Vortice.Direct3D12.ID3D12PipelineState nativePipelineState, in RHIRasterPipelineDescriptor descriptor)
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
            if (pipelineLayout == null)
            {
                throw new InvalidOperationException("Dx12RasterPipeline requires a Dx12PipelineLayout.");
            }
            Vortice.Direct3D12.PrimitiveTopologyType primitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(descriptor.PrimitiveAssembler.PrimitiveTopology);

            switch (descriptor.PrimitiveAssembler.PrimitiveType)
            {
                case ERHIPrimitiveType.Mesh:
                    throw new NotSupportedException("TODO(UNVERIFIED): DX12 mesh pipeline path must be migrated to Vortice pipeline-state-stream API.");

                case ERHIPrimitiveType.Vertex:
                    if (!descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
                    {
                        throw new InvalidOperationException("Vertex pipeline descriptor is missing VertexAssembler.");
                    }

                    Dx12Function vertexFunction = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexFunction as Dx12Function;
                    Span<RHIVertexLayoutDescriptor> vertexLayouts = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexLayouts.Span;
                    if (vertexFunction != null)
                    {
                        m_VertexStrides = new uint[vertexLayouts.Length];
                        for (int j = 0; j < vertexLayouts.Length; ++j)
                        {
                            m_VertexStrides[j] = vertexLayouts[j].Stride;
                        }
                    }

                    int inputElementCount = Dx12Utility.GetDx12VertexLayoutCount(vertexLayouts);
                    Vortice.Direct3D12.InputElementDescription[] inputElements = new Vortice.Direct3D12.InputElementDescription[inputElementCount];
                    Dx12Utility.ConvertToDx12VertexLayout(vertexLayouts, inputElements);

                    Vortice.Direct3D12.GraphicsPipelineStateDescription nativeGraphicsPipelineDesc = new Vortice.Direct3D12.GraphicsPipelineStateDescription
                    {
                        InputLayout = new Vortice.Direct3D12.InputLayoutDescription(inputElements),
                        RootSignature = pipelineLayout.NativeRootSignature,
                        PrimitiveTopologyType = primitiveTopologyType,
                        SampleDescription = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount),
                        SampleMask = descriptor.RenderState.SampleMask.HasValue ? descriptor.RenderState.SampleMask.Value : uint.MaxValue,
                        BlendState = Dx12Utility.CreateDx12BlendState(descriptor.RenderState.BlendState),
                        RasterizerState = Dx12Utility.CreateDx12RasterizerState(descriptor.RenderState.RasterizerState, descriptor.SampleCount != ERHISampleCount.None),
                        DepthStencilState = Dx12Utility.CreateDx12DepthStencilState(descriptor.RenderState.DepthStencilState),
                        Flags = Vortice.Direct3D12.PipelineStateFlags.None,
                        RenderTargetFormats = new Vortice.DXGI.Format[descriptor.ColorFormats.Length],
                    };

                    if (descriptor.DepthFormat != ERHIPixelFormat.Unknown)
                    {
                        nativeGraphicsPipelineDesc.DepthStencilFormat = Dx12Utility.ConvertToDx12Format(descriptor.DepthFormat);
                    }

                    for (int i = 0; i < descriptor.ColorFormats.Length; ++i)
                    {
                        nativeGraphicsPipelineDesc.RenderTargetFormats[i] = Dx12Utility.ConvertToDx12ViewFormat(descriptor.ColorFormats[i]);
                    }

                    if (vertexFunction != null)
                    {
                        nativeGraphicsPipelineDesc.VertexShader = vertexFunction.NativeShaderBytecode.Data;
                    }

                    if (fragmentFunction != null)
                    {
                        nativeGraphicsPipelineDesc.PixelShader = fragmentFunction.NativeShaderBytecode.Data;
                    }

                    SharpGen.Runtime.Result hResult = device.NativeDevice.CreateGraphicsPipelineState(nativeGraphicsPipelineDesc, out Vortice.Direct3D12.ID3D12PipelineState nativePipelineState);
#if DEBUG
                    Dx12Utility.CHECK_HR(hResult);
#endif
                    m_NativePipelineState = nativePipelineState;
                    break;
            }
        }

        protected override void Release()
        {
            m_NativePipelineState.Release();
        }
    }

    internal unsafe class Dx12PipelineLibrary : RHIPipelineLibrary
    {
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12PipelineLibrary1 m_NativePipelineLibrary;

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

            SharpGen.Runtime.Result hResult = ((Vortice.Direct3D12.ID3D12Device2)device.NativeDevice).CreatePipelineLibrary(Span<byte>.Empty, out Vortice.Direct3D12.ID3D12PipelineLibrary basePipelineLibrary);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineLibrary = basePipelineLibrary.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
            if (m_NativePipelineLibrary == null)
            {
                throw new InvalidOperationException("Failed to query Vortice.Direct3D12.ID3D12PipelineLibrary1 from created pipeline library.");
            }
            if (basePipelineLibrary != null)
            {
                basePipelineLibrary.Release();
            }
        }

        public Dx12PipelineLibrary(Dx12Device device, in RHIPipelineLibraryResult pipelineLibraryResult) : base(pipelineLibraryResult)
        {
            m_Dx12Device = device;

            Span<byte> blob = pipelineLibraryResult.ByteCode == IntPtr.Zero || pipelineLibraryResult.ByteSize == 0
                ? Span<byte>.Empty
                : new Span<byte>((void*)pipelineLibraryResult.ByteCode, checked((int)pipelineLibraryResult.ByteSize));

            SharpGen.Runtime.Result hResult = ((Vortice.Direct3D12.ID3D12Device2)device.NativeDevice).CreatePipelineLibrary(blob, out Vortice.Direct3D12.ID3D12PipelineLibrary basePipelineLibrary);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativePipelineLibrary = basePipelineLibrary.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
            if (m_NativePipelineLibrary == null)
            {
                throw new InvalidOperationException("Failed to query Vortice.Direct3D12.ID3D12PipelineLibrary1 from serialized pipeline library blob.");
            }
            if (basePipelineLibrary != null)
            {
                basePipelineLibrary.Release();
            }
        }

        public override void StoreComputePipeline(string name, RHIComputePipeline computePipeline)
        {
            Dx12ComputePipeline dx12Pipeline = computePipeline as Dx12ComputePipeline;
            m_NativePipelineLibrary.StorePipeline(name, dx12Pipeline.NativePipelineState);
        }

        public override void StoreRasterPipeline(string name, RHIRasterPipeline rasterPipeline)
        {
            Dx12RasterPipeline dx12Pipeline = rasterPipeline as Dx12RasterPipeline;
            m_NativePipelineLibrary.StorePipeline(name, dx12Pipeline.NativePipelineState);
        }

        public override void StoreRaytracingPipeline(string name, RHIRaytracingPipeline raytracingPipeline)
        {
            throw new System.NotSupportedException("Vortice.Direct3D12.D3D12 PipelineLibrary does not support storing raytracing state objects.");
        }

        public override RHIComputePipeline LoadComputePipeline(RHIComputePipelineDescriptor computePipelineDescriptor)
        {
            if (m_NativePipelineLibrary == null)
            {
                throw new System.InvalidOperationException("Dx12PipelineLibrary: Cannot load pipeline — Vortice.Direct3D12.ID3D12PipelineLibrary has not been initialized.");
            }

            Dx12Function computeFunction = computePipelineDescriptor.ComputeFunction as Dx12Function;
            Dx12PipelineLayout pipelineLayout = computePipelineDescriptor.PipelineLayout as Dx12PipelineLayout;
            if (computeFunction == null || pipelineLayout == null)
            {
                throw new InvalidOperationException("Dx12PipelineLibrary.LoadComputePipeline requires DX12 function/layout descriptors.");
            }

            Vortice.Direct3D12.ComputePipelineStateDescription description = new Vortice.Direct3D12.ComputePipelineStateDescription
            {
                RootSignature = pipelineLayout.NativeRootSignature,
                Flags = Vortice.Direct3D12.PipelineStateFlags.None,
                ComputeShader = computeFunction.NativeShaderBytecode.Data,
            };

            string pipelineName = GetComputePipelineCacheKey(computePipelineDescriptor);
            try
            {
                Vortice.Direct3D12.ID3D12PipelineState nativePipelineState = m_NativePipelineLibrary.LoadComputePipeline(pipelineName, description);
                return new Dx12ComputePipeline(nativePipelineState, computePipelineDescriptor);
            }
            catch
            {
                // Cache miss or incompatible blob: create pipeline and populate cache.
                Dx12ComputePipeline fallbackPipeline = new Dx12ComputePipeline(m_Dx12Device, computePipelineDescriptor);
                StoreComputePipeline(pipelineName, fallbackPipeline);
                return fallbackPipeline;
            }
        }

        public override RHIRasterPipeline LoadRasterPipeline(RHIRasterPipelineDescriptor rasterPipelineDescriptor)
        {
            if (m_NativePipelineLibrary == null)
            {
                throw new System.InvalidOperationException("Dx12PipelineLibrary: Cannot load pipeline — Vortice.Direct3D12.ID3D12PipelineLibrary has not been initialized.");
            }

            Dx12PipelineLayout pipelineLayout = rasterPipelineDescriptor.PipelineLayout as Dx12PipelineLayout;
            if (pipelineLayout == null)
            {
                throw new InvalidOperationException("Dx12PipelineLibrary.LoadRasterPipeline requires a Dx12PipelineLayout.");
            }

            // Build the graphics pipeline state description for library lookup
            Vortice.Direct3D12.GraphicsPipelineStateDescription description = new Vortice.Direct3D12.GraphicsPipelineStateDescription
            {
                RootSignature = pipelineLayout.NativeRootSignature,
                PrimitiveTopologyType = Dx12Utility.ConvertToDx12PrimitiveTopologyType(rasterPipelineDescriptor.PrimitiveAssembler.PrimitiveTopology),
                SampleMask = uint.MaxValue,
                SampleDescription = Dx12Utility.ConvertToDx12SampleCount(rasterPipelineDescriptor.SampleCount),
                RenderTargetFormats = new Vortice.DXGI.Format[rasterPipelineDescriptor.ColorFormats.Length],
            };
            for (int i = 0; i < rasterPipelineDescriptor.ColorFormats.Length; ++i)
            {
                description.RenderTargetFormats[i] = Dx12Utility.ConvertToDx12ViewFormat(rasterPipelineDescriptor.ColorFormats[i]);
            }
            if (rasterPipelineDescriptor.DepthFormat != ERHIPixelFormat.Unknown)
            {
                description.DepthStencilFormat = Dx12Utility.ConvertToDx12Format(rasterPipelineDescriptor.DepthFormat);
            }

            string pipelineName = GetRasterPipelineCacheKey(rasterPipelineDescriptor);
            try
            {
                Vortice.Direct3D12.ID3D12PipelineState nativePipelineState = m_NativePipelineLibrary.LoadGraphicsPipeline(pipelineName, description);
                return new Dx12RasterPipeline(nativePipelineState, rasterPipelineDescriptor);
            }
            catch
            {
                // Cache miss or incompatible blob: create pipeline and populate cache.
                Dx12RasterPipeline fallbackPipeline = new Dx12RasterPipeline(m_Dx12Device, rasterPipelineDescriptor);
                StoreRasterPipeline(pipelineName, fallbackPipeline);
                return fallbackPipeline;
            }
        }

        public override RHIRaytracingPipeline LoadRaytracingPipeline(RHIRaytracingPipelineDescriptor raytracingPipelineDescriptor)
        {
            throw new System.NotSupportedException("Vortice.Direct3D12.D3D12 PipelineLibrary does not support loading raytracing state objects. Raytracing pipelines use Vortice.Direct3D12.ID3D12StateObject which is incompatible with Vortice.Direct3D12.ID3D12PipelineLibrary.");
        }

        public override RHIPipelineLibraryResult Serialize()
        {
            SharpGen.Runtime.PointerUSize nativeBlobSize = m_NativePipelineLibrary.SerializedSize;
            nuint blobSize = nativeBlobSize;
            RHIPipelineLibraryResult result;
            result.ByteSize = (uint)blobSize;
            result.ByteCode = System.Runtime.InteropServices.Marshal.AllocHGlobal((int)blobSize);
            m_NativePipelineLibrary.Serialize(result.ByteCode, nativeBlobSize);
            return result;
        }

        protected override void Release()
        {
            if (m_NativePipelineLibrary != null)
            {
                m_NativePipelineLibrary.Release();
                m_NativePipelineLibrary = null;
            }
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
            // TODO(ROADMAP-P2-1): Release WorkGraph resources when implementation is enabled.
        }
    }

    internal unsafe class Dx12MLPipeline : RHIMLPipeline
    {
        internal string Name => m_Name;
        internal Dx12MLProgram Program => m_Program;
        internal int StageCount => m_CompiledOperators.Length;
        internal Vortice.DirectML.IDMLOperatorInitializer OperatorInitializer => m_OperatorInitializer ?? throw new InvalidOperationException("DX12 ML operator initializer is unavailable.");
        internal Vortice.DirectML.BindingProperties InitializerBindingProperties => m_InitializerBindingProperties;
        internal uint RequiredDescriptorCount => m_RequiredDescriptorCount;
        internal ulong ProgramIntermediateTensorSize => m_ProgramIntermediateTensorSize;

        private readonly string m_Name;
        private readonly Dx12Device m_Dx12Device;
        private readonly Dx12MLProgram m_Program;
        private Vortice.DirectML.IDMLOperator[] m_NativeOperators;
        private Vortice.DirectML.IDMLCompiledOperator[] m_CompiledOperators;
        private Vortice.DirectML.IDMLOperatorInitializer? m_OperatorInitializer;
        private Vortice.DirectML.BindingProperties[] m_CompiledBindingProperties;
        private ulong[] m_PersistentResourceOffsets;
        private Vortice.DirectML.BindingProperties m_InitializerBindingProperties;
        private uint m_RequiredDescriptorCount;
        private ulong m_ProgramIntermediateTensorSize;

        public Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_Dx12Device = device;
            m_Name = descriptor.Name;
            m_Program = descriptor.Program as Dx12MLProgram
                ?? throw new InvalidOperationException("DX12 ML pipeline requires a Dx12MLProgram.");
            m_Program.ValidateDeviceSupport(device);
            m_BindingInfos = m_Program.BindingInfos;

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            try
            {
                Vortice.DirectML.OperatorDescription[] operatorDescriptions = m_Program.CreateOperatorDescriptions();
                m_NativeOperators = new Vortice.DirectML.IDMLOperator[operatorDescriptions.Length];
                m_CompiledOperators = new Vortice.DirectML.IDMLCompiledOperator[operatorDescriptions.Length];
                m_CompiledBindingProperties = new Vortice.DirectML.BindingProperties[operatorDescriptions.Length];
                m_PersistentResourceOffsets = new ulong[operatorDescriptions.Length];

                for (int i = 0; i < operatorDescriptions.Length; ++i)
                {
                    m_NativeOperators[i] = device.DirectMLDevice.CreateOperator(operatorDescriptions[i]);
                    m_CompiledOperators[i] = device.DirectMLDevice.CompileOperator(m_NativeOperators[i], Vortice.DirectML.ExecutionFlags.None);
                    m_CompiledBindingProperties[i] = m_CompiledOperators[i].GetBindingProperties();
                    m_RequiredDescriptorCount = Math.Max(m_RequiredDescriptorCount, m_CompiledBindingProperties[i].RequiredDescriptorCount);
                    m_TemporaryResourceSize = Math.Max(m_TemporaryResourceSize, m_CompiledBindingProperties[i].TemporaryResourceSize);
                    m_PersistentResourceOffsets[i] = m_PersistentResourceSize;
                    m_PersistentResourceSize += m_CompiledBindingProperties[i].PersistentResourceSize;
                }

                m_OperatorInitializer = device.DirectMLDevice.CreateOperatorInitializer(m_CompiledOperators);
                m_InitializerBindingProperties = m_OperatorInitializer.GetBindingProperties();
                m_RequiredDescriptorCount = Math.Max(m_RequiredDescriptorCount, m_InitializerBindingProperties.RequiredDescriptorCount);
                m_TemporaryResourceSize = Math.Max(m_TemporaryResourceSize, m_InitializerBindingProperties.TemporaryResourceSize);
                m_ProgramIntermediateTensorSize = RHIMLHelpers.CalculateMinimumByteLength(m_Program.IntermediateTensorDescriptor);
            }
            catch
            {
                Release();
                throw;
            }
        }

        internal Vortice.DirectML.IDMLCompiledOperator GetCompiledOperator(int stageIndex)
        {
            return m_CompiledOperators[stageIndex];
        }

        internal ulong GetPersistentResourceOffset(int stageIndex)
        {
            return m_PersistentResourceOffsets[stageIndex];
        }

        internal ulong GetPersistentResourceSize(int stageIndex)
        {
            return m_CompiledBindingProperties[stageIndex].PersistentResourceSize;
        }

        internal uint GetRequiredDescriptorCount(int stageIndex)
        {
            return m_CompiledBindingProperties[stageIndex].RequiredDescriptorCount;
        }

        protected override void Release()
        {
            if (m_OperatorInitializer != null)
            {
                m_OperatorInitializer.Release();
                m_OperatorInitializer = null;
            }

            if (m_CompiledOperators != null)
            {
                for (int i = 0; i < m_CompiledOperators.Length; ++i)
                {
                    m_CompiledOperators[i]?.Release();
                }
                m_CompiledOperators = Array.Empty<Vortice.DirectML.IDMLCompiledOperator>();
            }

            if (m_NativeOperators != null)
            {
                for (int i = 0; i < m_NativeOperators.Length; ++i)
                {
                    m_NativeOperators[i]?.Release();
                }
                m_NativeOperators = Array.Empty<Vortice.DirectML.IDMLOperator>();
            }
        }
    }
}
