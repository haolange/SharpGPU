using Infinity.Mathmatics;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.DirectX.D3D12;

namespace Infinity.Graphics
{
    internal unsafe class Dx12TextureView : RHITextureView
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
        //private bool2 m_LifeState;
        private Dx12Texture m_Dx12Texture;
        private ID3D12DescriptorHeap* m_NativeDescriptorHeap;
        private D3D12_CPU_DESCRIPTOR_HANDLE m_NativeCpuDescriptorHandle;
        private D3D12_GPU_DESCRIPTOR_HANDLE m_NativeGpuDescriptorHandle;

        public Dx12TextureView(Dx12Texture texture, in RHITextureViewDescriptor descriptor)
        {
            //m_LifeState = false;
            m_Dx12Texture = texture;

            /*if (descriptor.ViewType == ETextureViewType.DepthStencil)
            {
                if(Dx12Utility.IsDepthStencilTexture(texture.Descriptor.Usage))
                {
                    m_LifeState.x = true;

                    D3D12_DEPTH_STENCIL_DESC desc = new D3D12_DEPTH_STENCIL_DESC();
                    desc.Format = Dx12Utility.GetNativeFormat(descriptor.format);
                    desc.ViewDimension = Dx12Utility.GetNativeViewDimension(descriptor.Dimension);
                    Dx12Utility.FillTexture2DDSV(ref desc.Texture2D, descriptor);
                    Dx12Utility.FillTexture3DDSV(ref desc.Texture3D, descriptor);
                    Dx12Utility.FillTexture2DArrayDSV(ref desc.Texture2DArray, descriptor);

                    Dx12DescriptorInfo allocation = m_Dx12Texture.Dx12Device.AllocateRtvDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Texture.Dx12Device.NativeDevice->CreateRenderTargetView(texture.NativeResource, &desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ETextureViewType.RenderTarget)
            {
                if (Dx12Utility.IsRenderTargetTexture(texture.Descriptor.Usage))
                {
                    m_LifeState.y = true;

                    D3D12_RENDER_TARGET_VIEW_DESC desc = new D3D12_RENDER_TARGET_VIEW_DESC();
                    desc.Format = Dx12Utility.GetNativeFormat(descriptor.format);
                    desc.ViewDimension = Dx12Utility.GetNativeViewDimension(descriptor.Dimension);
                    Dx12Utility.FillTexture2DRTV(ref desc.Texture2D, descriptor);
                    Dx12Utility.FillTexture3DRTV(ref desc.Texture3D, descriptor);
                    Dx12Utility.FillTexture2DArrayRTV(ref desc.Texture2DArray, descriptor);

                    Dx12DescriptorInfo allocation = m_Dx12Texture.Dx12Device.AllocateRtvDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Texture.Dx12Device.NativeDevice->CreateRenderTargetView(m_Dx12Texture.NativeResource, &desc, m_NativeCpuDescriptorHandle);
                }
            } 
            else*/ if (descriptor.ViewType == ETextureViewType.ShaderResource)
            {
                if(Dx12Utility.IsShaderResourceTexture(texture.Descriptor.Usage))
                {
                    D3D12_SHADER_RESOURCE_VIEW_DESC desc = new D3D12_SHADER_RESOURCE_VIEW_DESC();
                    desc.Format = Dx12Utility.ConvertToDx12ViewFormat(descriptor.Format);
                    desc.ViewDimension = Dx12Utility.ConvertToDx12TextureSRVDimension(texture.Descriptor.Dimension);
                    desc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
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
                    m_Dx12Texture.Dx12Device.NativeDevice->CreateShaderResourceView(m_Dx12Texture.NativeResource, &desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ETextureViewType.UnorderedAccess)
            {
                if(Dx12Utility.IsUnorderedAccessTexture(texture.Descriptor.Usage))
                {
                    D3D12_UNORDERED_ACCESS_VIEW_DESC desc = new D3D12_UNORDERED_ACCESS_VIEW_DESC();
                    desc.Format = Dx12Utility.ConvertToDx12ViewFormat(descriptor.Format);
                    desc.ViewDimension = Dx12Utility.ConvertToDx12TextureUAVDimension(texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DUAV(ref desc.Texture2D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture3DUAV(ref desc.Texture3D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DArrayUAV(ref desc.Texture2DArray, descriptor, texture.Descriptor.Dimension);

                    Dx12DescriptorInfo allocation = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Texture.Dx12Device.NativeDevice->CreateUnorderedAccessView(m_Dx12Texture.NativeResource, null, &desc, m_NativeCpuDescriptorHandle);
                }
            }
        }

        protected override void Release()
        {
            m_Dx12Texture.Dx12Device.FreeCbvSrvUavDescriptor(m_HeapIndex);
        }
    }
}
