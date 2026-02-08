using System;
using Infinity.Mathmatics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal sealed class MetalSwapChain : RHISwapChain
    {
        public override int BackTextureIndex => m_BackTextureIndex;

        private readonly MetalDevice m_MetalDevice;
        private RHISwapChainDescriptor m_Descriptor;
        private CAMetalLayer m_Layer;
        private CAMetalDrawable m_CurrentDrawable;
        private MetalTexture? m_CurrentBackTexture;
        private int m_BackTextureIndex;

        public MetalSwapChain(MetalDevice device, in RHISwapChainDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_Layer = CAMetalLayer.New();
            m_CurrentDrawable = default;
            m_CurrentBackTexture = null;
            m_BackTextureIndex = -1;

            if (m_Layer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create CAMetalLayer.");
            }

            m_Layer.Device = device.NativeDevice;
            m_Layer.PixelFormat = MetalUtility.ConvertToMetalSwapchainFormat(descriptor.Format);
            m_Layer.FramebufferOnly = descriptor.FrameBufferOnly;
            m_Layer.DisplaySyncEnabled = descriptor.PresentMode != ERHIPresentMode.Immediately;
            m_Layer.Opaque = true;

            AttachLayerToSurface(descriptor.Surface);
            Resize(descriptor.Extent);
        }

        public override RHITexture AcquireBackBufferTexture()
        {
            if (m_CurrentBackTexture != null)
            {
                return m_CurrentBackTexture;
            }

            m_CurrentDrawable = m_Layer.NextDrawable();
            if (m_CurrentDrawable.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to acquire CAMetalDrawable.");
            }

            RHITextureDescriptor backTextureDescriptor = MetalTexture.BuildDescriptorFromNative(m_CurrentDrawable.Texture);
            backTextureDescriptor.StorageMode = ERHIStorageMode.GPULocal;
            backTextureDescriptor.UsageFlag = ERHITextureUsage.RenderTarget;
            if (!m_Descriptor.FrameBufferOnly)
            {
                backTextureDescriptor.UsageFlag |= ERHITextureUsage.ShaderResource;
            }
            m_CurrentBackTexture = new MetalTexture(m_MetalDevice, backTextureDescriptor, m_CurrentDrawable.Texture, false, m_CurrentDrawable);

            int count = Math.Max(1, (int)m_Descriptor.Count);
            m_BackTextureIndex = (m_BackTextureIndex + 1) % count;
            return m_CurrentBackTexture;
        }

        public override void Resize(in uint2 extent)
        {
            m_Descriptor.Extent = extent;
            m_Layer.DrawableSize = new CGSize(extent.x, extent.y);
            m_Layer.Frame = new CGRect(new CGPoint(0, 0), new CGSize(extent.x, extent.y));
            ClearFrameState();
        }

        public override void Present()
        {
            ClearFrameState();
        }

        private void AttachLayerToSurface(in IntPtr surface)
        {
            if (surface == IntPtr.Zero)
            {
                throw new InvalidOperationException("SwapChain surface pointer is null.");
            }

            NSWindow window = new NSWindow(surface);
            NSView contentView = window.ContentView;
            contentView.WantsLayer = true;
            contentView.Layer = m_Layer;

            CGRect frame = contentView.Frame;
            m_Layer.Frame = frame;
        }

        private void ClearFrameState()
        {
            m_CurrentBackTexture?.Dispose();
            m_CurrentBackTexture = null;
            m_CurrentDrawable = default;
        }

        protected override void Release()
        {
            ClearFrameState();

            if (m_Layer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_Layer);
                m_Layer = default;
            }
        }
    }
}
