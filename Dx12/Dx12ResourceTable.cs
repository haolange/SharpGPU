using System;
using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindInfo
    {
        public uint Slot;
        public uint Index;
        public uint Count;
        public ERHIBindType Type;
        public ERHIShaderStage Stage;

        internal bool IsBindless => Count > 1;
    }

    internal unsafe class Dx12ResourceTableLayout : RHIResourceTableLayout
    {
        public uint Index
        {
            get
            {
                return m_Index;
            }
        }
        public Dx12BindInfo[] BindInfos
        {
            get
            {
                return m_BindInfos;
            }
        }

        private uint m_Index;
        private Dx12BindInfo[] m_BindInfos;

        public Dx12ResourceTableLayout(in RHIResourceTableLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new Dx12BindInfo[descriptor.Elements.Length];

            Span<RHIResourceTableLayoutElement> elements = descriptor.Elements.Span;
            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref RHIResourceTableLayoutElement element = ref elements[i];
                ref Dx12BindInfo bindInfo = ref m_BindInfos[i];
                bindInfo.Index = descriptor.Index;
                bindInfo.Slot = element.Slot;
                bindInfo.Type = element.Type;
                bindInfo.Count = element.Count;
                bindInfo.Stage = element.Stage;
            }
        }

        protected override void Release()
        {
            m_Index = 0;
        }
    }

    internal unsafe class Dx12ResourceTable : RHIResourceTable
    {
        public Dx12ResourceTableLayout ResourceTableLayout
        {
            get
            {
                return m_ResourceTableLayout;
            }
        }
        public D3D12_GPU_DESCRIPTOR_HANDLE[] NativeGpuDescriptorHandles
        {
            get
            {
                return m_NativeGpuDescriptorHandles;
            }
        }

        private Dx12ResourceTableLayout m_ResourceTableLayout;
        private D3D12_GPU_DESCRIPTOR_HANDLE[] m_NativeGpuDescriptorHandles;

        public Dx12ResourceTable(in RHIResourceTableDescriptor descriptor)
        {
            Dx12ResourceTableLayout resourceTableLayout = descriptor.Layout as Dx12ResourceTableLayout;
#if DEBUG
            Debug.Assert(resourceTableLayout != null, "ResourceTableLayout is null in descriptor");
#endif
            m_ResourceTableLayout = resourceTableLayout;
            m_NativeGpuDescriptorHandles = new D3D12_GPU_DESCRIPTOR_HANDLE[descriptor.Elements.Length];

            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref Dx12BindInfo bindInfo = ref resourceTableLayout.BindInfos[i];
                ref RHIResourceTableElement element = ref descriptor.Elements.Span[i];

                ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[i];
                switch (bindInfo.Type)
                {
                    case ERHIBindType.Buffer:
                    case ERHIBindType.StorageBuffer:
                    case ERHIBindType.UniformBuffer:
                        Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                        nativeGpuDescriptorHandle = bufferView.NativeGpuDescriptorHandle;
                        break;

                    case ERHIBindType.Texture2D:
                    case ERHIBindType.Texture2DMS:
                    case ERHIBindType.Texture2DArray:
                    case ERHIBindType.Texture2DArrayMS:
                    case ERHIBindType.TextureCube:
                    case ERHIBindType.TextureCubeArray:
                    case ERHIBindType.Texture3D:
                    case ERHIBindType.StorageTexture2D:
                    case ERHIBindType.StorageTexture2DArray:
                    case ERHIBindType.StorageTexture2DArrayMS:
                    case ERHIBindType.StorageTextureCube:
                    case ERHIBindType.StorageTextureCubeArray:
                    case ERHIBindType.StorageTexture3D:
                        Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                        nativeGpuDescriptorHandle = textureView.NativeGpuDescriptorHandle;
                        break;

                    case ERHIBindType.Sampler:
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

        public override void SetBindElement(in RHIResourceTableElement element, in ERHIBindType bindType, in int slot)
        {
            ref D3D12_GPU_DESCRIPTOR_HANDLE nativeGpuDescriptorHandle = ref m_NativeGpuDescriptorHandles[slot];
            switch (bindType)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.StorageBuffer:
                    Dx12BufferView bufferView = element.BufferView as Dx12BufferView;
                    nativeGpuDescriptorHandle = bufferView.NativeGpuDescriptorHandle;
                    break;

                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    Dx12TextureView textureView = element.TextureView as Dx12TextureView;
                    nativeGpuDescriptorHandle = textureView.NativeGpuDescriptorHandle;
                    break;

                case ERHIBindType.Sampler:
                    Dx12Sampler samplerState = element.Sampler as Dx12Sampler;
                    nativeGpuDescriptorHandle = samplerState.NativeGpuDescriptorHandle;
                    break;
            }
        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
