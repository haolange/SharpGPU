using System;
using Infinity.Mathmatics;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using static TerraFX.Interop.Windows.Windows;
using System.Reflection.Metadata;

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

            for (int i = 0; i < descriptor.BindTableLayouts.Length; ++i)
            {
                Dx12BindTableLayout bindTableLayout = descriptor.BindTableLayouts[i] as Dx12BindTableLayout;
                m_ParameterCount += bindTableLayout.BindInfos.Length;
            }

            D3D12_DESCRIPTOR_RANGE1* rootDescriptorRangePtr = stackalloc D3D12_DESCRIPTOR_RANGE1[m_ParameterCount];
            Span<D3D12_DESCRIPTOR_RANGE1> rootDescriptorRangeViews = new Span<D3D12_DESCRIPTOR_RANGE1>(rootDescriptorRangePtr, m_ParameterCount);

            D3D12_ROOT_PARAMETER1* rootParameterPtr = stackalloc D3D12_ROOT_PARAMETER1[m_ParameterCount];
            Span<D3D12_ROOT_PARAMETER1> rootParameterViews = new Span<D3D12_ROOT_PARAMETER1>(rootParameterPtr, m_ParameterCount);

            for (int i = 0; i < descriptor.BindTableLayouts.Length; ++i)
            {
                Dx12BindTableLayout bindTableLayout = descriptor.BindTableLayouts[i] as Dx12BindTableLayout;

                for (int j = 0; j < bindTableLayout.BindInfos.Length; ++j)
                {
                    ref Dx12BindInfo bindInfo = ref bindTableLayout.BindInfos[j];

                    ref D3D12_DESCRIPTOR_RANGE1 rootDescriptorRange = ref rootDescriptorRangeViews[i + j];
                    rootDescriptorRange.Init(Dx12Utility.ConvertToDx12BindType(bindInfo.Type), bindInfo.IsBindless ? bindInfo.Count : 1, bindInfo.Slot, bindInfo.Index, Dx12Utility.GetDx12DescriptorRangeFalag(bindInfo.Type));

                    ref D3D12_ROOT_PARAMETER1 rootParameterView = ref rootParameterViews[i + j];
                    rootParameterView.InitAsDescriptorTable(1, rootDescriptorRangePtr + (i + j), Dx12Utility.ConvertToDx12ShaderStage(bindInfo.Visible));

                    Dx12BindTypeAndParameterSlot parameter;
                    {
                        parameter.Slot = i + j;
                        parameter.Type = bindInfo.Type;
                    }

                    if ((bindInfo.Visible & ERHIPipelineStage.All) == ERHIPipelineStage.All)
                    {
                        m_AllParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Visible & ERHIPipelineStage.Vertex) == ERHIPipelineStage.Vertex)
                    {
                        m_VertexParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Visible & ERHIPipelineStage.Fragment) == ERHIPipelineStage.Fragment)
                    {
                        m_FragmentParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }

                    if ((bindInfo.Visible & ERHIPipelineStage.Compute) == ERHIPipelineStage.Compute)
                    {
                        m_ComputeParameterMap.TryAdd(new uint3(bindInfo.Index << 8, bindInfo.Slot, Dx12Utility.GetDx12BindKey(bindInfo.Type)).GetHashCode(), parameter);
                    }
                }
            }

            D3D12_ROOT_SIGNATURE_FLAGS rootSignatureFlag = D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_NONE;
            if (descriptor.bLocalSignature)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_LOCAL_ROOT_SIGNATURE;
            }
            if (descriptor.bUseVertexLayout)
            {
                rootSignatureFlag |= D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
            }
            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSignatureDesc = new D3D12_VERSIONED_ROOT_SIGNATURE_DESC();
            rootSignatureDesc.Init_1_1((uint)m_ParameterCount, rootParameterPtr, 0, null, rootSignatureFlag);

            ID3DBlob* signature;
            Dx12Utility.CHECK_HR(DirectX.D3DX12SerializeVersionedRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1_1, &signature, null));

            ID3D12RootSignature* rootSignature;
            Dx12Utility.CHECK_HR(device.NativeDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)&rootSignature));
            m_NativeRootSignature = rootSignature;
        }

        public Dx12BindTypeAndParameterSlot? QueryRootDescriptorParameterIndex(in ERHIPipelineStage shaderStage, in uint layoutIndex, in uint slot, in ERHIBindType Type)
        {
            if ((shaderStage & ERHIPipelineStage.Vertex) == ERHIPipelineStage.Vertex)
            {
                //hasValue = m_VertexParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_VertexParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIPipelineStage.Fragment) == ERHIPipelineStage.Fragment)
            {
                //hasValue = m_FragmentParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_FragmentParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIPipelineStage.Compute) == ERHIPipelineStage.Compute)
            {
                //hasValue = m_ComputeParameterMap.TryGetValue(new int2(slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                bool hasValue = m_ComputeParameterMap.TryGetValue(new uint3(layoutIndex << 8, slot, Dx12Utility.GetDx12BindKey(Type)).GetHashCode(), out Dx12BindTypeAndParameterSlot parameter);
                return hasValue ? parameter : null;
            }

            if ((shaderStage & ERHIPipelineStage.All) == ERHIPipelineStage.All)
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
#pragma warning restore CS0169, CS0649, CS8600, CS8601, CS8602, CS8604, CS8618, CA1416
}
