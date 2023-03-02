using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CA1416 
    internal unsafe class Dx12SamplerState : RHISamplerState
    {
        public ID3D12DescriptorHeap* NativeDescriptorHeap
        {
            get
            {
                return m_NativeDescriptorHeap;
            }
        }
        public D3D12_CPU_DESCRIPTOR_HANDLE NativeCpuDescriptorHandle
        {
            get
            {
                return m_NativeCpuDescriptorHandle;
            }
        }
        public D3D12_GPU_DESCRIPTOR_HANDLE NativeGpuDescriptorHandle
        {
            get
            {
                return m_NativeGpuDescriptorHandle;
            }
        }

        private int m_HeapIndex;
        private Dx12Device m_Dx12Device;
        private ID3D12DescriptorHeap* m_NativeDescriptorHeap;
        private D3D12_CPU_DESCRIPTOR_HANDLE m_NativeCpuDescriptorHandle;
        private D3D12_GPU_DESCRIPTOR_HANDLE m_NativeGpuDescriptorHandle;

        public Dx12SamplerState(Dx12Device device, in RHISamplerStateDescriptor descriptor)
        {
            m_Dx12Device = device;

            D3D12_SAMPLER_DESC desc = new D3D12_SAMPLER_DESC();
            desc.MinLOD = descriptor.LodMinClamp;
            desc.MaxLOD = descriptor.LodMaxClamp;
            desc.Filter = Dx12Utility.ConvertToDx12Filter(descriptor);
            desc.AddressU = /*Dx12Utility.GetNativeAddressMode(Descriptor->AddressModeU)*/D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            desc.AddressV = /*Dx12Utility.GetNativeAddressMode(Descriptor->AddressModeV)*/D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            desc.AddressW = /*Dx12Utility.GetNativeAddressMode(Descriptor->AddressModeW)*/D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            desc.MaxAnisotropy = (uint)descriptor.Anisotropy;
            desc.ComparisonFunc = /*Dx12Utility.GetNativeComparisonFunc(Descriptor->ComparisonFunc)*/D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_NEVER;

            Dx12DescriptorInfo allocation = device.AllocateSamplerDescriptor(1);
            m_HeapIndex = allocation.Index;
            m_NativeDescriptorHeap = allocation.DescriptorHeap;
            m_NativeCpuDescriptorHandle = allocation.CpuHandle;
            m_NativeGpuDescriptorHandle = allocation.GpuHandle;
            device.NativeDevice->CreateSampler(&desc, m_NativeCpuDescriptorHandle);
        }

        protected override void Release()
        {
            m_Dx12Device.FreeSamplerDescriptor(m_HeapIndex);
        }
    }
#pragma warning restore CA1416
}
