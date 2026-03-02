using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    internal unsafe class Dx12TextureView : RHITextureView
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
        private Dx12Texture m_Dx12Texture;
        private Vortice.Direct3D12.ID3D12DescriptorHeap m_NativeDescriptorHeap;
        private Vortice.Direct3D12.CpuDescriptorHandle m_NativeCpuDescriptorHandle;
        private Vortice.Direct3D12.GpuDescriptorHandle m_NativeGpuDescriptorHandle;

        public Dx12TextureView(Dx12Texture texture, in RHITextureViewDescriptor descriptor)
        {
            m_Dx12Texture = texture;

            if (descriptor.ViewType == ERHITextureViewType.ShaderResource)
            {
                if(Dx12Utility.IsShaderResourceTexture(texture.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.ShaderResourceViewDescription desc = new Vortice.Direct3D12.ShaderResourceViewDescription();
                    desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                    desc.ViewDimension = Dx12Utility.ConvertToDx12TextureSRVDimension(texture.Descriptor.Dimension);
                    desc.Shader4ComponentMapping = 5768;
                    Dx12Utility.FillTexture2DSRV(ref desc.Texture2D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DArraySRV(ref desc.Texture2DArray, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTextureCubeSRV(ref desc.TextureCube, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTextureCubeArraySRV(ref desc.TextureCubeArray, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture3DSRV(ref desc.Texture3D, descriptor, texture.Descriptor.Dimension);

                    Dx12DescriptorInfo allocation = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Texture.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Texture.NativeResource, desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ERHITextureViewType.UnorderedAccess)
            {
                if(Dx12Utility.IsUnorderedAccessTexture(texture.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.UnorderedAccessViewDescription desc = new Vortice.Direct3D12.UnorderedAccessViewDescription();
                    desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                    desc.ViewDimension = Dx12Utility.ConvertToDx12TextureUAVDimension(texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DUAV(ref desc.Texture2D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture3DUAV(ref desc.Texture3D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DArrayUAV(ref desc.Texture2DArray, descriptor, texture.Descriptor.Dimension);

                    Dx12DescriptorInfo allocation = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Texture.Dx12Device.NativeDevice.CreateUnorderedAccessView(m_Dx12Texture.NativeResource, null, desc, m_NativeCpuDescriptorHandle);
                }
            }
        }

        protected override void Release()
        {
            m_Dx12Texture.Dx12Device.FreeCbvSrvUavDescriptor(m_HeapIndex);
        }
    }
}
