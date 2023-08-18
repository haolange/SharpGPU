using System.Diagnostics;
using System.Xml.Linq;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12BindTable : RHIBindTable
    {
        public Dx12BindTableLayout BindTableLayout
        {
            get
            {
                return m_BindTableLayout;
            }
        }
        public D3D12_GPU_DESCRIPTOR_HANDLE[] NativeGpuDescriptorHandles
        {
            get
            {
                return m_NativeGpuDescriptorHandles;
            }
        }

        private Dx12BindTableLayout m_BindTableLayout;
        private D3D12_GPU_DESCRIPTOR_HANDLE[] m_NativeGpuDescriptorHandles;

        public Dx12BindTable(in RHIBindTableDescriptor descriptor)
        {
            Dx12BindTableLayout bindTableLayout = descriptor.Layout as Dx12BindTableLayout;
#if DEBUG
            Debug.Assert(bindTableLayout != null, "BindTableLayout is null in descriptor");
#endif
            m_BindTableLayout = bindTableLayout;
            m_NativeGpuDescriptorHandles = new D3D12_GPU_DESCRIPTOR_HANDLE[descriptor.Elements.Length];

            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref Dx12BindInfo bindInfo = ref bindTableLayout.BindInfos[i];
                ref RHIBindTableElement element = ref descriptor.Elements.Span[i];

                ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[i];
                switch (bindInfo.Type)
                {
                    case EBindType.Buffer:
                    case EBindType.UniformBuffer:
                    case EBindType.StorageBuffer:
                        Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                        nativeGpuDescriptorHandle = bufferView.NativeGpuDescriptorHandle;
                        break;

                    case EBindType.Texture:
                    case EBindType.StorageTexture:
                        Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                        nativeGpuDescriptorHandle = textureView.NativeGpuDescriptorHandle;
                        break;

                    case EBindType.Sampler:
                        Dx12Sampler samplerState = element.Sampler as Dx12Sampler;
                        nativeGpuDescriptorHandle = samplerState.NativeGpuDescriptorHandle;
                        break;
                }

                if(bindInfo.IsBindless)
                {
                    //Todo Bindless
                }
            }
        }

        public override void SetBindElement(in RHIBindTableElement element, in EBindType bindType, in int slot)
        {
            ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[slot];
            switch (bindType)
            {
                case EBindType.Buffer:
                case EBindType.UniformBuffer:
                case EBindType.StorageBuffer:
                    Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                    nativeGpuDescriptorHandle = bufferView.NativeGpuDescriptorHandle;
                    break;

                case EBindType.Texture:
                case EBindType.StorageTexture:
                    Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                    nativeGpuDescriptorHandle = textureView.NativeGpuDescriptorHandle;
                    break;

                case EBindType.Sampler:
                    Dx12Sampler samplerState = element.Sampler as Dx12Sampler;
                    nativeGpuDescriptorHandle = samplerState.NativeGpuDescriptorHandle;
                    break;
            }
        }

        public override void SetBindElement(in RHIBindTableElement element, in RHIBindlessDescriptor bindlessDescriptor, in int slot)
        {
            ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[slot];
            //Todo Bindless
        }


        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
