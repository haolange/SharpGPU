using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CA1416 
    internal unsafe class Dx12Sampler : RHISampler
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

        public Dx12Sampler(Dx12Device device, in RHISamplerDescriptor descriptor)
        {
            m_Dx12Device = device;

            D3D12_SAMPLER_DESC desc = new D3D12_SAMPLER_DESC();
            desc.MinLOD = descriptor.LodMin;
            desc.MaxLOD = descriptor.LodMax;
            desc.MipLODBias = descriptor.MipLODBias;
            desc.MaxAnisotropy = descriptor.Anisotropy;
            desc.Filter = Dx12Utility.ConvertToDx12Filter(descriptor);
            desc.AddressU = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeU);
            desc.AddressV = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeV);
            desc.AddressW = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeW);
            desc.ComparisonFunc = Dx12Utility.ConvertToDx12ComparisonMode(descriptor.ComparisonMode);

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
