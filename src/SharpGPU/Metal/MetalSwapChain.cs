using System;
using SharpMetal.Metal;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
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

        public override RHISwapChainAcquireResult AcquireBackBuffer(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            ValidateAcquire(in descriptor);

            bool semaphoreReserved = false;
            bool fenceReserved = false;
            try
            {
                if (descriptor.SignalSemaphore != null)
                {
                    descriptor.SignalSemaphore.ReserveSignal();
                    semaphoreReserved = true;
                }
                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    fenceReserved = true;
                }

                RHISwapChainAcquireResult result = AcquireTyped(in descriptor);
                bool signalSubmitted = result.Status is
                    ERHISwapChainStatus.Success or
                    ERHISwapChainStatus.Suboptimal;

                if (signalSubmitted)
                {
                    descriptor.SignalSemaphore?.CommitSignal();
                    semaphoreReserved = false;
                    fenceReserved = false;
                }
                else
                {
                    if (fenceReserved)
                    {
                        descriptor.CompletionFence!.RollbackSignal();
                        fenceReserved = false;
                    }
                    if (semaphoreReserved)
                    {
                        descriptor.SignalSemaphore!.RollbackSignal();
                        semaphoreReserved = false;
                    }
                }

                result.Validate(OwnerDevice.BackendType, ImageCount);
                InvalidateDeviceIfNeeded(in result);
                return result;
            }
            catch (RHIException exception)
            {
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (semaphoreReserved)
                {
                    descriptor.SignalSemaphore!.RollbackSignal();
                }
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
            catch
            {
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (semaphoreReserved)
                {
                    descriptor.SignalSemaphore!.RollbackSignal();
                }
                throw;
            }
        }

        public override RHISwapChainOperationResult Resize(
            in RHISwapChainResizeDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            try
            {
                RHISwapChainOperationResult result = ResizeTyped(in descriptor);
                result.Validate(OwnerDevice.BackendType);
                InvalidateDeviceIfNeeded(in result);
                return result;
            }
            catch (RHIException exception)
            {
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
        }

        public override RHISwapChainOperationResult Present(
            in RHISwapChainPresentDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            ValidatePresent(in descriptor);

            int reservedWaits = 0;
            bool fenceReserved = false;
            try
            {
                ReadOnlySpan<RHISemaphore> waits =
                    descriptor.WaitSemaphores.Span;
                for (; reservedWaits < waits.Length; ++reservedWaits)
                {
                    waits[reservedWaits].ReserveWait();
                }
                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    fenceReserved = true;
                }

                bool waitsConsumed = PresentTyped(
                    in descriptor,
                    out RHISwapChainOperationResult result);
                if (waitsConsumed)
                {
                    for (int index = 0; index < waits.Length; ++index)
                    {
                        waits[index].CommitWait();
                    }
                    fenceReserved = false;
                }
                else
                {
                    for (int index = waits.Length - 1; index >= 0; --index)
                    {
                        waits[index].RollbackWait();
                    }
                    if (fenceReserved)
                    {
                        descriptor.CompletionFence!.RollbackSignal();
                        fenceReserved = false;
                    }
                }
                reservedWaits = 0;

                result.Validate(OwnerDevice.BackendType);
                InvalidateDeviceIfNeeded(in result);
                return result;
            }
            catch (RHIException exception)
            {
                RollbackPresentWaits(
                    descriptor.WaitSemaphores.Span,
                    reservedWaits);
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
            catch
            {
                RollbackPresentWaits(
                    descriptor.WaitSemaphores.Span,
                    reservedWaits);
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                throw;
            }
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

        private static readonly Selector s_MaximumDrawableCountSelector =
            "maximumDrawableCount";
        private static readonly Selector s_AllowsNextDrawableTimeoutSelector =
            "allowsNextDrawableTimeout";

        private CAMetalLayer CreateConfiguredLayer(
            in RHISwapChainDescriptor descriptor)
        {
            m_MetalDevice.Capabilities.Presentation.SwapChain.Require(
                "Metal CAMetalLayer creation");
            ValidateFormat(in descriptor);
            CAMetalLayer layer = CAMetalLayer.New();
            if (layer.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.InitializationFailed,
                    ERHIBackend.Metal,
                    0,
                    "Failed to create CAMetalLayer.",
                    ERHIDeviceState.Operational);
            }

            try
            {
                NSObject layerObject = new(layer.NativePtr);
                if (!layerObject.RespondsToSelector(
                        s_MaximumDrawableCountSelector) ||
                    !layerObject.RespondsToSelector(
                        s_AllowsNextDrawableTimeoutSelector))
                {
                    throw new NotSupportedException(
                        "The active CAMetalLayer runtime does not expose " +
                        "maximumDrawableCount/allowsNextDrawableTimeout.");
                }
                if (descriptor.Count is < 2 or > 3)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        descriptor.Count,
                        "Metal drawable count must be 2 or 3.");
                }

                layer.Device = m_MetalDevice.NativeDevice;
                layer.PixelFormat =
                    MetalUtility.ConvertToMetalSwapchainFormat(
                        descriptor.Format);
                layer.FramebufferOnly = descriptor.FrameBufferOnly;
                layer.DisplaySyncEnabled =
                    descriptor.PresentMode !=
                        ERHIPresentMode.Immediately;
                layer.Opaque = true;
                layer.AllowsNextDrawableTimeout = true;
                layer.MaximumDrawableCount = descriptor.Count;

                ulong nativeCount = layer.MaximumDrawableCount;
                if (nativeCount is < 2 or > 3)
                {
                    throw new NotSupportedException(
                        $"CAMetalLayer reported invalid drawable count " +
                        $"'{nativeCount}'.");
                }
                m_ImageCount = checked((int)nativeCount);
                return layer;
            }
            catch
            {
                ObjectiveCRuntime.Release(layer);
                throw;
            }
        }

        private RHISwapChainAcquireResult AcquireTyped(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            if (m_TerminalStatus !=
                ERHISwapChainStatus.Undefined)
            {
                return RHISwapChainAcquireResult.Unavailable(
                    m_TerminalStatus,
                    m_TerminalDiagnostic);
            }
            if (descriptor.SignalSemaphore != null ||
                descriptor.CompletionFence != null)
            {
                throw new NotSupportedException(
                    "CAMetalLayer acquisition does not signal a " +
                    "caller-owned semaphore or fence.");
            }
            if (m_CurrentBackTexture != null)
            {
                throw new InvalidOperationException(
                    "The Metal drawable is already acquired.");
            }

            m_CurrentDrawable = m_Layer.NextDrawable();
            if (m_CurrentDrawable.NativePtr == IntPtr.Zero)
            {
                return RHISwapChainAcquireResult.Unavailable(
                    m_Descriptor.Extent.x == 0 ||
                    m_Descriptor.Extent.y == 0
                        ? ERHISwapChainStatus.NotReady
                        : ERHISwapChainStatus.Timeout);
            }

            RHITextureDescriptor backTextureDescriptor =
                MetalTexture.BuildDescriptorFromNative(
                    m_CurrentDrawable.Texture);
            backTextureDescriptor.StorageMode =
                ERHIStorageMode.GPULocal;
            backTextureDescriptor.UsageFlag =
                ERHITextureUsage.RenderTarget;
            if (!m_Descriptor.FrameBufferOnly)
            {
                backTextureDescriptor.UsageFlag |=
                    ERHITextureUsage.ShaderResource;
            }
            m_CurrentBackTexture = new MetalTexture(
                m_MetalDevice,
                backTextureDescriptor,
                m_CurrentDrawable.Texture,
                false,
                in m_CurrentDrawable);
            m_BackTextureIndex =
                (m_BackTextureIndex + 1) % m_ImageCount;
            return RHISwapChainAcquireResult.Acquired(
                m_CurrentBackTexture,
                m_BackTextureIndex);
        }

        private RHISwapChainOperationResult ResizeTyped(
            in RHISwapChainResizeDescriptor descriptor)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            m_MetalDevice.Capabilities.Presentation.Maintenance.Require("Metal swapchain resize");
            if (m_CurrentBackTexture != null)
            {
                throw new InvalidOperationException(
                    "A Metal swapchain cannot be resized while a " +
                    "drawable is acquired.");
            }
            if (descriptor.Extent.x == 0 ||
                descriptor.Extent.y == 0)
            {
                return RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.NotReady);
            }
            if (descriptor.SurfaceGeneration <
                m_Descriptor.SurfaceGeneration)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Surface generation cannot move backwards.");
            }

            bool surfaceChanged =
                descriptor.SurfaceKind != m_Descriptor.SurfaceKind ||
                descriptor.WindowHandle != m_Descriptor.WindowHandle ||
                descriptor.SurfaceGeneration >
                    m_Descriptor.SurfaceGeneration;
            if (m_TerminalStatus ==
                    ERHISwapChainStatus.SurfaceLost &&
                !surfaceChanged)
            {
                return TerminalOperationResult();
            }
            if (descriptor.SurfaceKind is not
                    (RHINativeSurfaceKind.AppKitNsWindow or
                     RHINativeSurfaceKind.UIKitUiWindow) ||
                descriptor.WindowHandle == IntPtr.Zero)
            {
                return EnterSurfaceLost(
                    "Metal resize requires a valid NSWindow/NSView/" +
                    "UIWindow/UIView surface.");
            }

            if (surfaceChanged)
            {
                RHISwapChainDescriptor replacement = m_Descriptor;
                replacement.Extent = descriptor.Extent;
                replacement.SurfaceKind =
                    descriptor.SurfaceKind;
                replacement.WindowHandle =
                    descriptor.WindowHandle;
                replacement.SurfaceGeneration =
                    descriptor.SurfaceGeneration;

                CAMetalLayer newLayer =
                    CreateConfiguredLayer(in replacement);
                try
                {
                    AttachLayerToSurface(
                        newLayer,
                        descriptor.WindowHandle);
                    uint2 replacementExtent = descriptor.Extent;
                    ApplyExtent(
                        in replacementExtent,
                        newLayer,
                        descriptor.WindowHandle);
                }
                catch
                {
                    ReleaseLayer(ref newLayer);
                    throw;
                }

                ReleaseLayer(ref m_Layer);
                m_Layer = newLayer;
            }
            else
            {
                uint2 resizeExtent = descriptor.Extent;
                ApplyExtent(in resizeExtent);
            }

            m_Descriptor.Extent = descriptor.Extent;
            m_Descriptor.SurfaceKind =
                descriptor.SurfaceKind;
            m_Descriptor.WindowHandle =
                descriptor.WindowHandle;
            m_Descriptor.SurfaceGeneration =
                descriptor.SurfaceGeneration;
            m_BackTextureIndex = -1;
            m_TerminalStatus =
                ERHISwapChainStatus.Undefined;
            m_TerminalDiagnostic = null;
            return RHISwapChainOperationResult.FromStatus(
                ERHISwapChainStatus.Success);
        }

        private bool PresentTyped(
            in RHISwapChainPresentDescriptor descriptor,
            out RHISwapChainOperationResult result)
        {
            m_MetalDevice.ThrowIfCommandQueueFailed();
            if (m_TerminalStatus !=
                ERHISwapChainStatus.Undefined)
            {
                result = TerminalOperationResult();
                return false;
            }
            if (m_CurrentBackTexture == null ||
                m_CurrentDrawable.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "Present requires one successfully acquired Metal drawable.");
            }
            if (!descriptor.WaitSemaphores.IsEmpty)
            {
                throw new NotSupportedException(
                    "CAMetalDrawable Present does not consume RHI semaphores.");
            }

            MetalFence? completionFence = null;
            ulong presentSignalValue = 0;
            if (descriptor.CompletionFence != null)
            {
                m_MetalDevice.Capabilities.Presentation.PresentCompletion.Require(
                    "Metal swapchain presentation completion");
                if (descriptor.CompletionFence is not
                    MetalFence metalCompletionFence)
                {
                    throw new ArgumentException(
                        "Present completion fence is not a Metal fence.",
                        nameof(descriptor));
                }

                // addPresentedHandler: needs a real ObjC block; the C# trampoline
                // still PAC-faults on Metal's completion queue. Signal the shared
                // event after Present() until that trampoline is fixed.
                completionFence = metalCompletionFence;
                presentSignalValue =
                    metalCompletionFence.PrepareSignalValue();
            }

            m_CurrentDrawable.Present();
            if (completionFence != null)
            {
                MTLSharedEvent nativeEvent = completionFence.NativeEvent;
                nativeEvent.SignaledValue = presentSignalValue;
            }

            ClearFrameState();
            result = RHISwapChainOperationResult.FromStatus(
                ERHISwapChainStatus.Success);
            return true;
        }

        private void ApplyExtent(in uint2 extent)
        {
            ApplyExtent(in extent, m_Layer, m_Descriptor.WindowHandle);
        }

        private void ApplyExtent(
            in uint2 extent,
            CAMetalLayer layer,
            IntPtr surfaceHandle)
        {
            // DrawableSize is in pixels. Keep layer.Frame in points via the UIKit/AppKit view.
            layer.DrawableSize = new CGSize(extent.x, extent.y);
            SyncLayerFrameFromSurface(layer, surfaceHandle);
        }

        private void ValidateCreateDescriptor(
            in RHISwapChainDescriptor descriptor)
        {
            if (descriptor.SurfaceKind is not
                (RHINativeSurfaceKind.AppKitNsWindow or
                 RHINativeSurfaceKind.UIKitUiWindow))
            {
                throw new NotSupportedException(
                    "Metal swapchains require an AppKit or UIKit surface.");
            }
            if (descriptor.WindowHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "Metal surface handle must not be null.",
                    nameof(descriptor));
            }
            if (descriptor.Extent.x == 0 ||
                descriptor.Extent.y == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Metal swapchain extent must be non-zero.");
            }
            if (!Enum.IsDefined(descriptor.PresentMode) ||
                descriptor.PresentMode == ERHIPresentMode.Pending)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.PresentMode,
                    "Metal swapchain present mode is unknown.");
            }
            ValidateFormat(in descriptor);
            if (descriptor.PresentQueue is not
                    MetalCommandQueue queue ||
                !ReferenceEquals(
                    queue.MetalDevice,
                    m_MetalDevice))
            {
                throw new ArgumentException(
                    "Metal present queue must belong to the same device.",
                    nameof(descriptor));
            }
        }

        private void ValidateFormat(
            in RHISwapChainDescriptor descriptor)
        {
            if (!Enum.IsDefined(descriptor.Format) ||
                descriptor.Format == ERHISwapChainFormat.Pending)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Format,
                    "Metal swapchain format is unknown.");
            }
            if (descriptor.Format !=
                ERHISwapChainFormat.R8G8B8A8_UNorm)
            {
                m_MetalDevice.Capabilities.Presentation.Hdr.Require(
                    "Metal HDR swapchain format creation");
            }
            if (MetalUtility.ConvertToMetalSwapchainFormat(
                    descriptor.Format) == MTLPixelFormat.Invalid)
            {
                throw new NotSupportedException(
                    $"Metal cannot express swapchain format '{descriptor.Format}'.");
            }
        }

        private RHISwapChainOperationResult EnterSurfaceLost(
            string message)
        {
            m_TerminalStatus =
                ERHISwapChainStatus.SurfaceLost;
            m_TerminalDiagnostic = new RHIException(
                ERHIErrorCode.SurfaceLost,
                ERHIBackend.Metal,
                0,
                message,
                ERHIDeviceState.Operational);
            return TerminalOperationResult();
        }

        private RHISwapChainOperationResult TerminalOperationResult()
        {
            return RHISwapChainOperationResult.FromStatus(
                m_TerminalStatus,
                m_TerminalDiagnostic);
        }

        private static void ReleaseLayer(
            ref CAMetalLayer layer)
        {
            if (layer.NativePtr == IntPtr.Zero)
            {
                return;
            }
            layer.RemoveFromSuperlayer();
            ObjectiveCRuntime.Release(layer);
            layer = default;
        }
    }
}
