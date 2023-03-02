using System;
using System.Diagnostics;
using Infinity.Mathmatics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416, CS8602, CS8604
    internal unsafe class Dx12SwapChain : RHISwapChain
    {
        public override int BackBufferIndex => m_BackBufferIndex;
        /*public override int BackBufferIndex
        {
            get
            {
                return (int)(m_NativeSwapChain->GetCurrentBackBufferIndex() + 1 % m_Descriptor.Count);
            }
        }*/

        public int m_BackBufferIndex;
        private Dx12Device m_Dx12Device;
        private Dx12Texture[] m_Textures;
        private Dx12TextureView[] m_TextureViews;
        private IDXGISwapChain4* m_NativeSwapChain;
        private RHISwapChainDescriptor m_Descriptor;

        public Dx12SwapChain(Dx12Device device, in RHISwapChainDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_Textures = new Dx12Texture[m_Descriptor.Count];
            m_TextureViews = new Dx12TextureView[m_Descriptor.Count];
            CreateDX12SwapChain(descriptor);
            FetchDx12Textures(descriptor);
        }

        public override RHITexture GetTexture(in int index)
        {
            return m_Textures[index];
        }

        public override RHITextureView GetTextureView(in int index)
        {
            return m_TextureViews[index];
        }

        public override void Resize(in int2 extent)
        {
            for (int i = 0; i < m_Textures.Length; ++i)
            {
                if (m_TextureViews[i] != null)
                {
                    m_TextureViews[i].Dispose();
                }

                if (m_Textures[i] != null)
                {
                    m_Textures[i].NativeResource->Release();
                }
            }
            bool success = SUCCEEDED(m_NativeSwapChain->ResizeBuffers((uint)m_Descriptor.Count, (uint)extent.x, (uint)extent.y, /*Dx12Utility.ConvertToDx12Format(descriptor.Format)*/DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH));
            Debug.Assert(success);
            m_Descriptor.Extent = extent;
            FetchDx12Textures(m_Descriptor);
        }

        public override void Present(in EPresentMode presentMode)
        {
            m_BackBufferIndex++;
            m_BackBufferIndex = m_BackBufferIndex % m_Descriptor.Count;
            m_NativeSwapChain->Present(Dx12Utility.ConvertToDx12SyncInterval(presentMode), 0);
        }

        private void CreateDX12SwapChain(in RHISwapChainDescriptor descriptor) 
        {
            Dx12Queue dx12Queue = (Dx12Queue)descriptor.PresentQueue;
            Dx12Instance dx12Instance = m_Dx12Device.Dx12Gpu.Dx12Instance;

            DXGI_SWAP_CHAIN_DESC1 desc = new DXGI_SWAP_CHAIN_DESC1();
            desc.Stereo = false;
            desc.BufferCount = (uint)descriptor.Count;
            desc.Width = (uint)descriptor.Extent.x;
            desc.Height = (uint)descriptor.Extent.y;
            desc.Flags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
            desc.Format = /*Dx12Utility.ConvertToDx12Format(descriptor.Format)*/DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.Scaling = DXGI_SCALING.DXGI_SCALING_NONE;
            desc.SampleDesc = new DXGI_SAMPLE_DESC(1, 0);
            desc.SwapEffect = /*Dx12Utility.ConvertToDx12SwapEffect(descriptor.PresentMode)*/ DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD;
            desc.BufferUsage = descriptor.FrameBufferOnly ? DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT : (DXGI.DXGI_USAGE_SHADER_INPUT | DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT);

            IDXGISwapChain1* dx12SwapChain1;
            bool success = SUCCEEDED(dx12Instance.DXGIFactory->CreateSwapChainForHwnd((IUnknown*)dx12Queue.NativeCommandQueue, new HWND(descriptor.Surface.ToPointer()), &desc, null, null, &dx12SwapChain1));
            Debug.Assert(success);
            m_NativeSwapChain = (IDXGISwapChain4*)dx12SwapChain1;
        }

        private void FetchDx12Textures(in RHISwapChainDescriptor descriptor)
        {
            RHITextureDescriptor textureDescriptor;
            {
                textureDescriptor.Extent = new int3(descriptor.Extent.xy, 1);
                textureDescriptor.Samples = 1;
                textureDescriptor.MipCount = 1;
                textureDescriptor.Format = descriptor.Format;
                textureDescriptor.State = ETextureState.Present;
                textureDescriptor.Usage = ETextureUsage.RenderTarget;
                textureDescriptor.Dimension = ETextureDimension.Texture2D;
                textureDescriptor.StorageMode = EStorageMode.Default;
            }

            RHITextureViewDescriptor viewDescriptor;
            {
                viewDescriptor.MipCount = 1;
                viewDescriptor.BaseMipLevel = 0;
                viewDescriptor.BaseArrayLayer = 0;
                viewDescriptor.ArrayLayerCount = 1;
                viewDescriptor.Format = descriptor.Format;
                viewDescriptor.ViewType = ETextureViewType.RenderTarget;
                viewDescriptor.Dimension = ETextureViewDimension.Texture2D;
            }

            for (int i = 0; i < descriptor.Count; ++i)
            {
                ID3D12Resource* dx12Resource = null;
                bool success = SUCCEEDED(m_NativeSwapChain->GetBuffer((uint)i, __uuidof<ID3D12Resource>(), (void**)&dx12Resource));
                Debug.Assert(success);
                m_Textures[i] = new Dx12Texture(m_Dx12Device, textureDescriptor, dx12Resource);
                m_TextureViews[i] = new Dx12TextureView(m_Textures[i], viewDescriptor);
            }

            m_BackBufferIndex = 0;
        }

        protected override void Release()
        {
            for (int i = 0; i < m_Descriptor.Count; ++i)
            {
                m_TextureViews[i].Dispose();
            }
            m_NativeSwapChain->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416, CS8602, CS8604
}
