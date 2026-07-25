namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12Sampler : RHISampler, IDx12DescriptorView
    {
        public Dx12Device Device => m_Dx12Device;
        public Dx12DescriptorClass DescriptorClass => Dx12DescriptorClass.Sampler;

        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle
        {
            get
            {
                return m_Descriptors.Staging.CpuHandle;
            }
        }
        public Vortice.Direct3D12.GpuDescriptorHandle NativeGpuDescriptorHandle
        {
            get
            {
                return m_Descriptors.ShaderVisible.GpuHandle;
            }
        }

        private bool m_HasDescriptors;
        private Dx12Device m_Dx12Device;
        private Dx12DescriptorPair m_Descriptors;

        public Dx12Sampler(Dx12Device device, in RHISamplerDescriptor descriptor)
        {
            m_HasDescriptors = false;
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

            m_Descriptors = device.AllocateSamplerDescriptorPair();
            m_HasDescriptors = true;
            device.NativeDevice.CreateSampler(ref desc, m_Descriptors.Staging.CpuHandle);
            device.CopyDescriptorToShaderVisible(m_Descriptors);
        }

        protected override void Release()
        {
            if (m_HasDescriptors)
            {
                m_Dx12Device.FreeDescriptorPair(m_Descriptors);
                m_HasDescriptors = false;
            }
        }
    }
#pragma warning restore CA1416
}
