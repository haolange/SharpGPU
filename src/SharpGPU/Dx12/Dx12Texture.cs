using System.Diagnostics;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12Texture : RHITexture
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeResource
        {
            get
            {
                return m_NativeResource;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResource;

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            Vortice.Direct3D12.HeapProperties heapProperties = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Default/*Dx12Utility.ConvertToDx12ResourceFlagByUsage(descriptor.StorageMode)*/);
            Vortice.Direct3D12.ResourceDescription textureDesc = new Vortice.Direct3D12.ResourceDescription();
            textureDesc.MipLevels = (ushort)descriptor.MipCount;
            textureDesc.Format = Dx12Utility.ConvertToDx12Format(descriptor.Format);
            textureDesc.Width = descriptor.Extent.x;
            textureDesc.Height = descriptor.Extent.y;
            textureDesc.DepthOrArraySize = (ushort)descriptor.Extent.z;
            textureDesc.Flags = Dx12Utility.ConvertToDx12TextureFlag(descriptor.UsageFlag);
            textureDesc.SampleDescription = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount);
            textureDesc.Dimension = Dx12Utility.ConvertToDx12TextureDimension(descriptor.Dimension);

            Vortice.Direct3D12.ID3D12Resource dx12Resource;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                textureDesc,
                Vortice.Direct3D12.ResourceStates.Common/*Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode)*/,
                null,
                out dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeResource = dx12Resource;
        }

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor Descriptor, in Vortice.Direct3D12.ID3D12Resource nativeResource)
        {
            m_Dx12Device = device;
            m_Descriptor = Descriptor;
            m_NativeResource = nativeResource;
        }

        public override RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor)
        {
            return new Dx12TextureView(this, descriptor);
        }

        protected override void Release()
        {
            m_NativeResource.Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
