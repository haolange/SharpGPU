using System;
using SharpMath;

namespace SharpGPU
{
    internal unsafe class Dx12TextureView : RHITextureView, IDx12DescriptorView
    {
        public Dx12Device Device => m_Dx12Texture.Dx12Device;
        public ERHITextureViewType ViewType => m_ViewType;
        public Dx12DescriptorClass DescriptorClass => m_ViewType switch
        {
            ERHITextureViewType.ShaderResource => Dx12DescriptorClass.ShaderResource,
            ERHITextureViewType.UnorderedAccess => Dx12DescriptorClass.UnorderedAccess,
            _ => throw new InvalidOperationException($"DX12 texture view has unsupported descriptor class {m_ViewType}."),
        };
        public ERHITextureDimension Dimension => m_Dimension;

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
        private ERHITextureViewType m_ViewType;
        private ERHITextureDimension m_Dimension;
        private Dx12Texture m_Dx12Texture;
        private Dx12DescriptorPair m_Descriptors;

        public Dx12TextureView(Dx12Texture texture, in RHITextureViewDescriptor descriptor)
        {
            m_HasDescriptors = false;
            m_ViewType = descriptor.ViewType;
            m_Dimension = texture.Descriptor.Dimension;
            m_Dx12Texture = texture;

            if (descriptor.ViewType == ERHITextureViewType.ShaderResource)
            {
                if (texture.IsSamplerFeedbackMap)
                {
                    throw new ArgumentException(
                        "Opaque sampler-feedback maps are not shader-readable. Decode them into an R8_UINT texture first.",
                        nameof(descriptor));
                }

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

                    m_Descriptors = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Texture.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Texture.NativeResource, desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Texture.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }
            else if (descriptor.ViewType == ERHITextureViewType.UnorderedAccess)
            {
                if (texture.IsSamplerFeedbackMap)
                {
                    if (texture.PairedSamplerFeedbackTexture is not Dx12Texture paired ||
                        paired.IsDisposed)
                    {
                        throw new ArgumentException(
                            "A sampler-feedback UAV requires the create-time paired sampled texture to still be alive.",
                            nameof(descriptor));
                    }

                    m_Descriptors = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Texture.Dx12Device.CreateSamplerFeedbackUnorderedAccessView(
                        paired.NativeResource,
                        m_Dx12Texture.NativeResource,
                        m_Descriptors.Staging.CpuHandle);
                    m_Dx12Texture.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
                else if(Dx12Utility.IsUnorderedAccessTexture(texture.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.UnorderedAccessViewDescription desc = new Vortice.Direct3D12.UnorderedAccessViewDescription();
                    desc.Format = Dx12Utility.ConvertToDx12ViewFormat(texture.Descriptor.Format);
                    desc.ViewDimension = Dx12Utility.ConvertToDx12TextureUAVDimension(texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DUAV(ref desc.Texture2D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture3DUAV(ref desc.Texture3D, descriptor, texture.Descriptor.Dimension);
                    Dx12Utility.FillTexture2DArrayUAV(ref desc.Texture2DArray, descriptor, texture.Descriptor.Dimension);

                    m_Descriptors = m_Dx12Texture.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Texture.Dx12Device.NativeDevice.CreateUnorderedAccessView(m_Dx12Texture.NativeResource, null, desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Texture.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }

            if (!m_HasDescriptors)
            {
                throw new ArgumentException($"DX12 cannot create {descriptor.ViewType} {texture.Descriptor.Dimension} texture view from usage {texture.Descriptor.UsageFlag}.", nameof(descriptor));
            }
        }

        protected override void Release()
        {
            if (m_HasDescriptors)
            {
                m_Dx12Texture.Dx12Device.FreeDescriptorPair(m_Descriptors);
                m_HasDescriptors = false;
            }
        }
    }
}
