using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12BindGroup : RHIBindGroup
    {
        public Dx12BindGroupLayout BindGroupLayout
        {
            get
            {
                return m_BindGroupLayout;
            }
        }
        public D3D12_GPU_DESCRIPTOR_HANDLE[] NativeGpuDescriptorHandles
        {
            get
            {
                return m_NativeGpuDescriptorHandles;
            }
        }

        private Dx12BindGroupLayout m_BindGroupLayout;
        private D3D12_GPU_DESCRIPTOR_HANDLE[] m_NativeGpuDescriptorHandles;

        public Dx12BindGroup(in RHIBindGroupDescriptor descriptor)
        {
            Dx12BindGroupLayout bindGroupLayout = descriptor.Layout as Dx12BindGroupLayout;
            Debug.Assert(bindGroupLayout != null);

            m_BindGroupLayout = bindGroupLayout;
            m_NativeGpuDescriptorHandles = new D3D12_GPU_DESCRIPTOR_HANDLE[descriptor.Elements.Length];

            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref Dx12BindInfo bindInfo = ref bindGroupLayout.BindInfos[i];
                ref RHIBindGroupElement element = ref descriptor.Elements.Span[i];

                ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[i];
                switch (bindInfo.BindType)
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

                    case EBindType.SamplerState:
                        Dx12SamplerState samplerState = element.SamplerState as Dx12SamplerState;
                        nativeGpuDescriptorHandle = samplerState.NativeGpuDescriptorHandle;
                        break;

                    case EBindType.Bindless:
                        //Todo Bindless
                        break;
                }
            }
        }

        public override void SetBindElement(in RHIBindGroupElement element, in EBindType bindType, in int slot)
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

                case EBindType.SamplerState:
                    Dx12SamplerState samplerState = element.SamplerState as Dx12SamplerState;
                    nativeGpuDescriptorHandle = samplerState.NativeGpuDescriptorHandle;
                    break;

                case EBindType.Bindless:
                    //Todo Bindless
                    break;
            }
        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
