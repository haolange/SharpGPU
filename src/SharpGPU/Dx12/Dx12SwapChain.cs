using SharpGPU.Mathematics;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CA1416, CS8602, CS8604
    internal unsafe class Dx12SwapChain : RHISwapChain
    {
        public override int BackTextureIndex => m_NativeSwapChain3 != null ? (int)m_NativeSwapChain3.CurrentBackBufferIndex : m_FallbackBackTextureIndex;

        private Dx12Device m_Dx12Device;
        private Dx12Texture[] m_Textures;
        private Vortice.DXGI.IDXGISwapChain1 m_NativeSwapChain;
        private Vortice.DXGI.IDXGISwapChain3? m_NativeSwapChain3;
        private int m_FallbackBackTextureIndex;
        private RHISwapChainDescriptor m_Descriptor;

        public Dx12SwapChain(Dx12Device device, in RHISwapChainDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_Textures = new Dx12Texture[m_Descriptor.Count];
            CreateDX12SwapChain(descriptor);
            FetchDx12Textures(descriptor);
        }

        public override RHITexture AcquireBackBufferTexture()
        {
            return m_Textures[BackTextureIndex];
        }

        public override void Resize(in uint2 extent)
        {
            ReleaseBackBufferTextures();

            Vortice.DXGI.SwapChainDescription desc = m_NativeSwapChain.Description;
            SharpGen.Runtime.Result hResult = m_NativeSwapChain.ResizeBuffers(m_Descriptor.Count, extent.x, extent.y, desc.BufferDescription.Format/*Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(m_Descriptor.Format))*/, desc.Flags/*Vortice.DXGI.SwapChainFlags.AllowModeSwitch*/);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_Descriptor.Extent = extent;
            m_FallbackBackTextureIndex = 0;
            FetchDx12Textures(m_Descriptor);
        }

        public override void Present()
        {
            m_NativeSwapChain.Present(Dx12Utility.ConvertToDx12SyncInterval(m_Descriptor.PresentMode), 0);
            if (m_NativeSwapChain3 == null)
            {
                m_FallbackBackTextureIndex = (m_FallbackBackTextureIndex + 1) % m_Textures.Length;
            }
        }

        private void CreateDX12SwapChain(in RHISwapChainDescriptor descriptor) 
        {
            Dx12CommandQueue dx12Queue = (Dx12CommandQueue)descriptor.PresentQueue;
            Dx12Instance dx12Instance = m_Dx12Device.Dx12Instance;

#if true
            Vortice.DXGI.SwapChainDescription1 desc = new Vortice.DXGI.SwapChainDescription1();
            desc.BufferCount = descriptor.Count;
            desc.Width = descriptor.Extent.x;
            desc.Height = descriptor.Extent.y;
            //desc.Flags = (uint)Vortice.DXGI.SwapChainFlags.AllowModeSwitch;
            desc.Format = Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(descriptor.Format));
            //desc.Scaling = Vortice.DXGI.Scaling.None;
            desc.SampleDescription = new Vortice.DXGI.SampleDescription(1, 0);
            desc.SwapEffect = Dx12Utility.ConvertToDx12SwapEffect(m_Descriptor.PresentMode);
            desc.BufferUsage = descriptor.FrameBufferOnly ? Vortice.DXGI.Usage.RenderTargetOutput : (Vortice.DXGI.Usage.ShaderInput | Vortice.DXGI.Usage.RenderTargetOutput);

            Vortice.DXGI.IDXGISwapChain1 dx12SwapChain1 = dx12Instance.DXGIFactory.CreateSwapChainForHwnd(
                dx12Queue.NativeCommandQueue,
                descriptor.WindowHandle,
                desc,
                null,
                null);
            m_NativeSwapChain = dx12SwapChain1;
            m_NativeSwapChain3 = dx12SwapChain1.QueryInterfaceOrNull<Vortice.DXGI.IDXGISwapChain3>();
            m_FallbackBackTextureIndex = 0;
#else
            Vortice.DXGI.SwapChainDescription desc = new Vortice.DXGI.SwapChainDescription();
            //desc.Flags = (uint)Vortice.DXGI.SwapChainFlags.AllowModeSwitch;
            desc.Windowed = true;
            desc.BufferCount = descriptor.Count;
            desc.SampleDescription = new Vortice.DXGI.SampleDescription(1, 0);
            desc.SwapEffect = Dx12Utility.ConvertToDx12SwapEffect(m_Descriptor.PresentMode);
            desc.OutputWindow = descriptor.WindowHandle;
            desc.BufferDescription.Width = descriptor.Extent.x;
            desc.BufferDescription.Height = descriptor.Extent.y;
            desc.BufferDescription.Format = Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(descriptor.Format));
            //desc.BufferDescription.Scaling = Vortice.DXGI.ModeScaling.Unspecified;
            desc.BufferDescription.RefreshRate.Numerator = descriptor.FPS;
            desc.BufferDescription.RefreshRate.Denominator = 1;
            //desc.BufferDescription.ScanlineOrdering = Vortice.DXGI.ModeScanlineOrder.Unspecified;
            desc.BufferUsage = descriptor.FrameBufferOnly ? Vortice.DXGI.DXGI.RenderTargetOutput : (Vortice.DXGI.DXGI.ShaderInput | Vortice.DXGI.DXGI.RenderTargetOutput);

            Vortice.DXGI.IDXGISwapChain dx12SwapChain1;
            SharpGen.Runtime.Result hResult = dx12Instance.DXGIFactory.CreateSwapChain(dx12Queue.NativeCommandQueue, &desc, &dx12SwapChain1);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeSwapChain = dx12SwapChain1.QueryInterfaceOrNull<Vortice.DXGI.IDXGISwapChain1>();
            m_NativeSwapChain3 = m_NativeSwapChain?.QueryInterfaceOrNull<Vortice.DXGI.IDXGISwapChain3>();
            m_FallbackBackTextureIndex = 0;
            dx12SwapChain1.Release();
            if (m_NativeSwapChain == null)
            {
                throw new System.InvalidOperationException("Failed to query IDXGISwapChain1 from DXGI swap chain.");
            }
#endif
        }

        private void FetchDx12Textures(in RHISwapChainDescriptor descriptor)
        {
            RHITextureDescriptor textureDescriptor;
            {
                textureDescriptor.Extent = new uint3(descriptor.Extent.xy, 1);
                textureDescriptor.MipCount = 1;
                textureDescriptor.SampleCount = ERHISampleCount.None;
                textureDescriptor.Format = RHIUtility.ConvertToPixelFormat(descriptor.Format);
                textureDescriptor.UsageFlag = ERHITextureUsage.RenderTarget;
                textureDescriptor.Dimension = ERHITextureDimension.Texture2D;
                textureDescriptor.StorageMode = ERHIStorageMode.GPULocal;
            }

            for (int i = 0; i < descriptor.Count; ++i)
            {
                Vortice.Direct3D12.ID3D12Resource dx12Resource;
                SharpGen.Runtime.Result hResult = m_NativeSwapChain.GetBuffer((uint)i, out dx12Resource);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_Textures[i] = new Dx12Texture(m_Dx12Device, textureDescriptor, dx12Resource);
            }
        }

        private void ReleaseBackBufferTextures()
        {
            for (int i = 0; i < m_Textures.Length; ++i)
            {
                m_Textures[i]?.Dispose();
                m_Textures[i] = null!;
            }
        }

        protected override void Release()
        {
            ReleaseBackBufferTextures();

            if (m_NativeSwapChain3 != null)
            {
                m_NativeSwapChain3.Release();
                m_NativeSwapChain3 = null;
            }
            m_NativeSwapChain.Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416, CS8602, CS8604
}
