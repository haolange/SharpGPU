using System;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace SharpGPU
{
    internal sealed partial class MetalSwapChain
    {
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
