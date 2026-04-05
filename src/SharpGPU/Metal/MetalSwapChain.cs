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
        private static readonly ObjectiveCClass s_NSWindowClass = new("NSWindow");
        private static readonly ObjectiveCClass s_NSViewClass = new("NSView");
        private static readonly ObjectiveCClass s_UIWindowClass = new("UIWindow");
        private static readonly ObjectiveCClass s_UIViewClass = new("UIView");
        private static readonly IntPtr s_ContentViewSelector = new Selector("contentView");
        private static readonly IntPtr s_IsKindOfClassSelector = new Selector("isKindOfClass:");
        private static readonly IntPtr s_RootViewControllerSelector = new Selector("rootViewController");
        private static readonly IntPtr s_ViewSelector = new Selector("view");

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

            if (ResolveContentView(surface) is NSView appKitView)
            {
                appKitView.WantsLayer = true;
                appKitView.Layer = m_Layer;
                m_Layer.Frame = appKitView.Frame;
                return;
            }

            IntPtr uiKitView = ResolveUIKitView(surface);
            IntPtr baseLayer = ObjectiveCRuntime.IntPtr_objc_msgSend(uiKitView, new Selector("layer"));
            if (baseLayer == IntPtr.Zero)
            {
                throw new InvalidOperationException("UIView layer pointer is null while attaching CAMetalLayer.");
            }

            ObjectiveCRuntime.objc_msgSend(baseLayer, new Selector("addSublayer:"), m_Layer.NativePtr);
        }

        private static NSView? ResolveContentView(IntPtr surface)
        {
            if (IsObjectOfClass(surface, s_NSViewClass))
            {
                return new NSView(surface);
            }

            if (IsObjectOfClass(surface, s_NSWindowClass))
            {
                NSWindow window = new NSWindow(surface);
                return window.ContentView;
            }

            return null;
        }

        private static IntPtr ResolveUIKitView(IntPtr surface)
        {
            if (IsObjectOfClass(surface, s_UIViewClass))
            {
                return surface;
            }

            if (IsObjectOfClass(surface, s_UIWindowClass))
            {
                IntPtr rootViewController = ObjectiveCRuntime.IntPtr_objc_msgSend(surface, s_RootViewControllerSelector);
                IntPtr rootView = rootViewController != IntPtr.Zero
                    ? ObjectiveCRuntime.IntPtr_objc_msgSend(rootViewController, s_ViewSelector)
                    : IntPtr.Zero;
                if (rootView != IntPtr.Zero)
                {
                    return rootView;
                }
            }

            throw new InvalidOperationException("Metal swapchain expected NSWindow/NSView/UIWindow/UIView surface handle.");
        }

        private static bool IsObjectOfClass(IntPtr objectPtr, ObjectiveCClass cls)
        {
            return objectPtr != IntPtr.Zero &&
                cls.NativePtr != IntPtr.Zero &&
                ObjectiveCRuntime.bool_objc_msgSend(objectPtr, s_IsKindOfClassSelector, cls.NativePtr);
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
