namespace Infinity.Graphics
{
#pragma warning disable CA1416 
    internal unsafe class Dx12Sampler : RHISampler
    {
        public Vortice.Direct3D12.ID3D12DescriptorHeap NativeDescriptorHeap
        {
            get
            {
                return m_NativeDescriptorHeap;
            }
        }
        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle
        {
            get
            {
                return m_NativeCpuDescriptorHandle;
            }
        }
        public Vortice.Direct3D12.GpuDescriptorHandle NativeGpuDescriptorHandle
        {
            get
            {
                return m_NativeGpuDescriptorHandle;
            }
        }

        private int m_HeapIndex;
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12DescriptorHeap m_NativeDescriptorHeap;
        private Vortice.Direct3D12.CpuDescriptorHandle m_NativeCpuDescriptorHandle;
        private Vortice.Direct3D12.GpuDescriptorHandle m_NativeGpuDescriptorHandle;

        public Dx12Sampler(Dx12Device device, in RHISamplerDescriptor descriptor)
        {
            m_Dx12Device = device;

            Vortice.Direct3D12.SamplerDescription desc = new Vortice.Direct3D12.SamplerDescription();
            desc.MinLOD = descriptor.LodMin;
            desc.MaxLOD = descriptor.LodMax;
            desc.MipLODBias = descriptor.MipLODBias;
            desc.MaxAnisotropy = descriptor.Anisotropy;
            desc.Filter = Dx12Utility.ConvertToDx12Filter(descriptor);
            desc.AddressU = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeU);
            desc.AddressV = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeV);
            desc.AddressW = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeW);
            desc.ComparisonFunction = Dx12Utility.ConvertToDx12ComparisonMode(descriptor.ComparisonMode);

            Dx12DescriptorInfo allocation = device.AllocateSamplerDescriptor(1);
            m_HeapIndex = allocation.Index;
            m_NativeDescriptorHeap = allocation.DescriptorHeap;
            m_NativeCpuDescriptorHandle = allocation.CpuHandle;
            m_NativeGpuDescriptorHandle = allocation.GpuHandle;
            device.NativeDevice.CreateSampler(ref desc, m_NativeCpuDescriptorHandle);
        }

        protected override void Release()
        {
            m_Dx12Device.FreeSamplerDescriptor(m_HeapIndex);
        }
    }
#pragma warning restore CA1416
}
