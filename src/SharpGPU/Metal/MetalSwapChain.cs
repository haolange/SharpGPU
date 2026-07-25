using System;
using SharpMetal.Metal;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed partial class MetalSwapChain : RHISwapChain
    {
        private static readonly ObjectiveCClass s_NSWindowClass = new("NSWindow");
        private static readonly ObjectiveCClass s_NSViewClass = new("NSView");
        private static readonly ObjectiveCClass s_UIWindowClass = new("UIWindow");
        private static readonly ObjectiveCClass s_UIViewClass = new("UIView");
        private static readonly IntPtr s_ContentViewSelector = new Selector("contentView");
        private static readonly IntPtr s_IsKindOfClassSelector = new Selector("isKindOfClass:");
        private static readonly IntPtr s_RootViewControllerSelector = new Selector("rootViewController");
        private static readonly IntPtr s_ViewSelector = new Selector("view");
        private static readonly IntPtr s_BoundsSelector = new Selector("bounds");
        private static readonly IntPtr s_ContentScaleFactorSelector = new Selector("contentScaleFactor");
        private static readonly IntPtr s_SetContentsScaleSelector = new Selector("setContentsScale:");

        public override int BackTextureIndex
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return m_BackTextureIndex;
            }
        }

        public override int ImageCount
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return m_ImageCount;
            }
        }

        private readonly MetalDevice m_MetalDevice;
        private RHISwapChainDescriptor m_Descriptor;
        private CAMetalLayer m_Layer;
        private CAMetalDrawable m_CurrentDrawable;
        private MetalTexture? m_CurrentBackTexture;
        private int m_BackTextureIndex;
        private int m_ImageCount;
        private ERHISwapChainStatus m_TerminalStatus;
        private RHIException? m_TerminalDiagnostic;

        public MetalSwapChain(
            MetalDevice device,
            in RHISwapChainDescriptor descriptor)
            : base(device)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_CurrentDrawable = default;
            m_CurrentBackTexture = null;
            m_BackTextureIndex = -1;
            ValidateCreateDescriptor(in descriptor);
            m_Layer = CreateConfiguredLayer(in descriptor);
            try
            {
                AttachLayerToSurface(
                    m_Layer,
                    descriptor.WindowHandle);
                BindLayerResidencySet();
                uint2 createExtent = descriptor.Extent;
                ApplyExtent(in createExtent);
            }
            catch
            {
                ReleaseLayer(ref m_Layer);
                throw;
            }
        }

        private void BindLayerResidencySet()
        {
            MTLResidencySet layerResidency = m_Layer.ResidencySet;
            if (layerResidency.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_MetalDevice.GetCommandQueue(ERHIPipelineType.Graphics, 0)
                is MetalCommandQueue graphicsQueue)
            {
                graphicsQueue.AddExternalResidencySet(layerResidency);
            }
        }

        protected override RHISwapChainAcquireResult AcquireCore(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            return AcquireTyped(in descriptor);
        }

        protected override RHISwapChainOperationResult ResizeCore(
            in RHISwapChainResizeDescriptor descriptor)
        {
            return ResizeTyped(in descriptor);
        }

        protected override bool PresentCore(
            in RHISwapChainPresentDescriptor descriptor,
            out RHISwapChainOperationResult result)
        {
            return PresentTyped(in descriptor, out result);
        }

        private void AttachLayerToSurface(
            CAMetalLayer layer,
            in IntPtr surface)
        {
            if (surface == IntPtr.Zero)
            {
                throw new InvalidOperationException("SwapChain surface pointer is null.");
            }

            if (ResolveContentView(surface) is NSView appKitView)
            {
                appKitView.WantsLayer = true;
                appKitView.Layer = layer;
                layer.Frame = appKitView.Frame;
                return;
            }

            IntPtr uiKitView = ResolveUIKitView(surface);
            IntPtr baseLayer = ObjectiveCRuntime.IntPtr_objc_msgSend(uiKitView, new Selector("layer"));
            if (baseLayer == IntPtr.Zero)
            {
                throw new InvalidOperationException("UIView layer pointer is null while attaching CAMetalLayer.");
            }

            // UIKit frames are in points. Without mirroring bounds, CAMetalLayer stays at
            // CGRectZero and presents an invisible zero-size layer over a black UIView.
            SyncMetalLayerToUIKitView(layer, uiKitView);
            ObjectiveCRuntime.objc_msgSend(baseLayer, new Selector("addSublayer:"), layer.NativePtr);
        }

        private static void SyncLayerFrameFromSurface(CAMetalLayer layer, IntPtr surface)
        {
            if (surface == IntPtr.Zero)
            {
                return;
            }

            if (ResolveContentView(surface) is NSView appKitView)
            {
                layer.Frame = appKitView.Frame;
                return;
            }

            SyncMetalLayerToUIKitView(layer, ResolveUIKitView(surface));
        }

        private static void SyncMetalLayerToUIKitView(CAMetalLayer layer, IntPtr uiKitView)
        {
            CGRect bounds = ObjectiveCRuntime.CGRect_objc_msgSend(uiKitView, s_BoundsSelector);
            layer.Frame = bounds;

            double scale = ObjectiveCRuntime.double_objc_msgSend(uiKitView, s_ContentScaleFactorSelector);
            if (scale <= 0.0)
            {
                scale = 1.0;
            }

            ObjectiveCRuntime.objc_msgSend(layer.NativePtr, s_SetContentsScaleSelector, scale);
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
            ReleaseLayer(ref m_Layer);
        }
    }
}
