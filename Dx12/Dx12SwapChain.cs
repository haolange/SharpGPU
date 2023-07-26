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
        public override int BackTextureIndex => (int)m_NativeSwapChain->GetCurrentBackBufferIndex();

        private Dx12Device m_Dx12Device;
        private Dx12Texture[] m_Textures;
        private IDXGISwapChain4* m_NativeSwapChain;
        private RHISwapChainDescriptor m_Descriptor;

        public Dx12SwapChain(Dx12Device device, in RHISwapChainDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_Textures = new Dx12Texture[m_Descriptor.Count];
            CreateDX12SwapChain(descriptor);
            FetchDx12Textures(descriptor);
        }

        public override RHITexture AcquireBackTexture()
        {
            return m_Textures[BackTextureIndex];
        }


        public override void Resize(in uint2 extent)
        {
            for (int i = 0; i < m_Textures.Length; ++i)
            {
                if (m_Textures[i] != null)
                {
                    m_Textures[i].NativeResource->Release();
                }
            }
            bool success = SUCCEEDED(m_NativeSwapChain->ResizeBuffers(m_Descriptor.Count, extent.x, extent.y, Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(m_Descriptor.Format)), (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH));
            Debug.Assert(success);
            m_Descriptor.Extent = extent;
            FetchDx12Textures(m_Descriptor);
        }

        public override void Present()
        {
            m_NativeSwapChain->Present(Dx12Utility.ConvertToDx12SyncInterval(m_Descriptor.PresentMode), 0);
        }

        private void CreateDX12SwapChain(in RHISwapChainDescriptor descriptor) 
        {
            Dx12CommandQueue dx12Queue = (Dx12CommandQueue)descriptor.PresentQueue;
            Dx12Instance dx12Instance = m_Dx12Device.Dx12Instance;

#if true
            DXGI_SWAP_CHAIN_DESC1 desc = new DXGI_SWAP_CHAIN_DESC1();
            desc.BufferCount = descriptor.Count;
            desc.Width = descriptor.Extent.x;
            desc.Height = descriptor.Extent.y;
            //desc.Flags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
            desc.Format = Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(descriptor.Format));
            //desc.Scaling = DXGI_SCALING.DXGI_SCALING_NONE;
            desc.SampleDesc = new DXGI_SAMPLE_DESC(1, 0);
            desc.SwapEffect = Dx12Utility.ConvertToDx12SwapEffect(m_Descriptor.PresentMode);
            desc.BufferUsage = descriptor.FrameBufferOnly ? DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT : (DXGI.DXGI_USAGE_SHADER_INPUT | DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT);

            IDXGISwapChain1* dx12SwapChain1;
            bool success = SUCCEEDED(dx12Instance.DXGIFactory->CreateSwapChainForHwnd((IUnknown*)dx12Queue.NativeCommandQueue, new HWND(descriptor.Surface.ToPointer()), &desc, null, null, &dx12SwapChain1));
            Debug.Assert(success);
            m_NativeSwapChain = (IDXGISwapChain4*)dx12SwapChain1;
#else
            DXGI_SWAP_CHAIN_DESC desc = new DXGI_SWAP_CHAIN_DESC();
            //desc.Flags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
            desc.Windowed = true;
            desc.BufferCount = descriptor.Count;
            desc.SampleDesc = new DXGI_SAMPLE_DESC(1, 0);
            desc.SwapEffect = Dx12Utility.ConvertToDx12SwapEffect(m_Descriptor.PresentMode);
            desc.OutputWindow = new HWND(descriptor.Surface.ToPointer());
            desc.BufferDesc.Width = descriptor.Extent.x;
            desc.BufferDesc.Height = descriptor.Extent.y;
            desc.BufferDesc.Format = Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(descriptor.Format));
            //desc.BufferDesc.Scaling = DXGI_MODE_SCALING.DXGI_MODE_SCALING_UNSPECIFIED;
            desc.BufferDesc.RefreshRate.Numerator = descriptor.FPS;
            desc.BufferDesc.RefreshRate.Denominator = 1;
            //desc.BufferDesc.ScanlineOrdering = DXGI_MODE_SCANLINE_ORDER.DXGI_MODE_SCANLINE_ORDER_UNSPECIFIED;
            desc.BufferUsage = descriptor.FrameBufferOnly ? DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT : (DXGI.DXGI_USAGE_SHADER_INPUT | DXGI.DXGI_USAGE_RENDER_TARGET_OUTPUT);

            IDXGISwapChain* dx12SwapChain1;
            bool success = SUCCEEDED(dx12Instance.DXGIFactory->CreateSwapChain((IUnknown*)dx12Queue.NativeCommandQueue, &desc, &dx12SwapChain1));
            Debug.Assert(success);
            m_NativeSwapChain = (IDXGISwapChain4*)dx12SwapChain1;
#endif
        }

        private void FetchDx12Textures(in RHISwapChainDescriptor descriptor)
        {
            RHITextureDescriptor textureDescriptor;
            {
                textureDescriptor.Extent = new uint3(descriptor.Extent.xy, 1);
                textureDescriptor.MipCount = 1;
                textureDescriptor.Format = RHIUtility.ConvertToPixelFormat(descriptor.Format);
                textureDescriptor.Usage = ETextureUsage.RenderTarget;
                textureDescriptor.SampleCount = ESampleCount.None;
                textureDescriptor.Dimension = ETextureDimension.Texture2D;
                textureDescriptor.StorageMode = EStorageMode.GPULocal;
            }

            for (int i = 0; i < descriptor.Count; ++i)
            {
                ID3D12Resource* dx12Resource = null;
                bool success = SUCCEEDED(m_NativeSwapChain->GetBuffer((uint)i, __uuidof<ID3D12Resource>(), (void**)&dx12Resource));
                Debug.Assert(success);
                m_Textures[i] = new Dx12Texture(m_Dx12Device, textureDescriptor, dx12Resource);
            }
        }

        protected override void Release()
        {
            m_NativeSwapChain->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416, CS8602, CS8604
}
