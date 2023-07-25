using System.Diagnostics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
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
        public ID3D12Resource* NativeResource
        {
            get
            {
                return m_NativeResource;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12Resource* m_NativeResource;

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            D3D12_HEAP_PROPERTIES heapProperties = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT/*Dx12Utility.ConvertToDx12ResourceFlagByUsage(descriptor.StorageMode)*/);
            D3D12_RESOURCE_DESC textureDesc = new D3D12_RESOURCE_DESC();
            textureDesc.MipLevels = (ushort)descriptor.MipCount;
            textureDesc.Format = Dx12Utility.ConvertToDx12Format(descriptor.Format);
            textureDesc.Width = descriptor.Extent.x;
            textureDesc.Height = descriptor.Extent.y;
            textureDesc.DepthOrArraySize = (ushort)descriptor.Extent.z;
            textureDesc.Flags = Dx12Utility.ConvertToDx12TextureFlag(descriptor.Usage);
            textureDesc.SampleDesc = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount);
            textureDesc.Dimension = Dx12Utility.ConvertToDx12TextureDimension(descriptor.Dimension);

            ID3D12Resource* dx12Resource;
            bool success = SUCCEEDED(m_Dx12Device.NativeDevice->CreateCommittedResource(&heapProperties, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &textureDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON/*Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode)*/, null, __uuidof<ID3D12Resource>(), (void**)&dx12Resource));
#if DEBUG
            Debug.Assert(success);
#endif
            m_NativeResource = dx12Resource;
        }

        public Dx12Texture(Dx12Device device, in RHITextureDescriptor Descriptor, in ID3D12Resource* nativeResource)
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
            m_NativeResource->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
