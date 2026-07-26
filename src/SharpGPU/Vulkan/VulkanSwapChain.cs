using System;
using Vortice.Vulkan;
using SharpGPU.Mathematics;
#if !INFINITY_TARGET_ANDROID
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;
#endif
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8618, CA1416
    internal unsafe partial class VulkanSwapChain : RHISwapChain
    {
        public override int BackTextureIndex
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return checked((int)m_CurrentImageIndex);
            }
        }

        public override int ImageCount
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return m_Textures.Length;
            }
        }

        public VkSwapchainKHR NativeSwapChain
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return m_NativeSwapChain;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VulkanTexture[] m_Textures;
        private VkSwapchainKHR m_NativeSwapChain;
        private VkSurfaceKHR m_Surface;
        private RHISwapChainDescriptor m_Descriptor;
        private uint m_CurrentImageIndex;
        private bool m_HasAcquiredImageThisFrame;
        private IntPtr m_MetalLayerHandle;
        private ERHISwapChainStatus m_TerminalStatus;
        private RHIException? m_TerminalDiagnostic;

#if !INFINITY_TARGET_ANDROID
        private static ObjectiveCClass s_NSWindowClass;
        private static ObjectiveCClass s_NSViewClass;
        private static ObjectiveCClass s_CAMetalLayerClass;
        private static ObjectiveCClass s_UIWindowClass;
        private static ObjectiveCClass s_UIViewClass;
        private static bool s_AppKitClassesInitialized;
        private static bool s_UIKitClassesInitialized;
        private static bool s_MetalLayerClassInitialized;

        private static Selector s_IsKindOfClassSelector;
        private static Selector s_ContentViewSelector;
        private static Selector s_RootViewControllerSelector;
        private static Selector s_ViewSelector;
        private static Selector s_LayerSelector;
        private static Selector s_SetWantsLayerSelector;
        private static Selector s_SetLayerSelector;
        private static Selector s_AddSublayerSelector;
#endif

        public VulkanSwapChain(
            VulkanDevice device,
            in RHISwapChainDescriptor descriptor)
            : base(device)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_Textures = Array.Empty<VulkanTexture>();
            ValidateCreateDescriptor(in descriptor);

            try
            {
                m_Surface = CreateSurface(
                    in descriptor,
                    out m_MetalLayerHandle);
                VulkanSwapchainBuild build = BuildSwapchain(
                    in descriptor,
                    m_Surface,
                    default);
                CommitBuild(in build, in descriptor);
            }
            catch
            {
                DestroySurface(m_Surface, m_MetalLayerHandle);
                m_Surface = default;
                m_MetalLayerHandle = IntPtr.Zero;
                throw;
            }
        }

        private VkSurfaceKHR CreateSurface(
            in RHISwapChainDescriptor descriptor,
            out IntPtr metalLayerHandle)
        {
            VulkanInstance vkInstance = m_VulkanDevice.VulkanInstance;
            VkSurfaceKHR surface = default;
            metalLayerHandle = IntPtr.Zero;

            switch (descriptor.SurfaceKind)
            {
                case RHINativeSurfaceKind.Win32Hwnd:
                {
                    // TODO(UNVERIFIED): Validate on Windows runtime (x64/x86_64).
                    IntPtr moduleHandle = descriptor.InstanceHandle;
                    if (moduleHandle == IntPtr.Zero)
                    {
                        moduleHandle = GetModuleHandle(null);
                    }
                    if (moduleHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("A Win32 HINSTANCE is required for Vulkan surface creation.");
                    }

                    VkWin32SurfaceCreateInfoKHR surfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.Win32SurfaceCreateInfoKHR,
                        hwnd = descriptor.WindowHandle,
                        hinstance = moduleHandle,
                    };

                    VulkanUtility.CheckErrors(VulkanNative.vkCreateWin32SurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, &surface));
                    break;
                }
                case RHINativeSurfaceKind.X11Window:
                {
                    // TODO(UNVERIFIED): Validate on Linux/X11 runtime.
                    if (descriptor.DisplayHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("The SDL-owned X11 Display handle is required for Vulkan surface creation.");
                    }

                    VkXlibSurfaceCreateInfoKHR surfaceCreateInfo = new VkXlibSurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.XlibSurfaceCreateInfoKHR,
                        dpy = descriptor.DisplayHandle,
                        window = (ulong)descriptor.WindowHandle,
                    };

                    VulkanUtility.CheckErrors(VulkanNative.vkCreateXlibSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, &surface));
                    break;
                }
                case RHINativeSurfaceKind.WaylandSurface:
                {
                    // TODO(UNVERIFIED): Validate on Linux/Wayland runtime.
                    if (descriptor.DisplayHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("The SDL-owned wl_display handle is required for Vulkan surface creation.");
                    }

                    VkWaylandSurfaceCreateInfoKHR surfaceCreateInfo = new VkWaylandSurfaceCreateInfoKHR
                    {
                        sType = VkStructureTypeWaylandSurfaceCreateInfoKHR,
                        display = descriptor.DisplayHandle,
                        surface = descriptor.WindowHandle,
                    };

                    VulkanUtility.CheckErrors(VulkanNative.vkCreateWaylandSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, &surface));
                    break;
                }
                case RHINativeSurfaceKind.AndroidNativeWindow:
                {
                    // TODO(UNVERIFIED): Validate on Android runtime with ANativeWindow* surface handle.
                    VkAndroidSurfaceCreateInfoKHR surfaceCreateInfo = new VkAndroidSurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.AndroidSurfaceCreateInfoKHR,
                        window = descriptor.WindowHandle,
                    };

                    VulkanUtility.CheckErrors(VulkanNative.vkCreateAndroidSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, &surface));
                    break;
                }
                case RHINativeSurfaceKind.AppKitNsWindow:
                case RHINativeSurfaceKind.UIKitUiWindow:
                {
#if INFINITY_TARGET_ANDROID
                    throw new PlatformNotSupportedException(
                        "Apple Metal surfaces are unavailable on Android Vulkan builds.");
#else
                    // TODO(UNVERIFIED): iOS path requires device runtime verification.
                    EOSPlatform platform = descriptor.SurfaceKind == RHINativeSurfaceKind.AppKitNsWindow ? EOSPlatform.MacOS : EOSPlatform.iOS;
                    metalLayerHandle = ResolveMetalLayer(descriptor.WindowHandle, platform);
                    if (metalLayerHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to resolve CAMetalLayer for Vulkan surface creation.");
                    }

                    VkMetalSurfaceCreateInfoEXT metalSurfaceCreateInfo = new VkMetalSurfaceCreateInfoEXT()
                    {
                        sType = VkStructureType.MetalSurfaceCreateInfoEXT,
                        pLayer = metalLayerHandle,
                    };
                    try
                    {
                        surface = CreateMetalSurface(
                            vkInstance.NativeInstance,
                            in metalSurfaceCreateInfo);
                    }
                    catch
                    {
                        ObjectiveCRuntime.Release(
                            metalLayerHandle);
                        metalLayerHandle = IntPtr.Zero;
                        throw;
                    }
#endif
                    break;
                }
                default:
                    throw new PlatformNotSupportedException($"Vulkan swapchain surface kind '{descriptor.SurfaceKind}' is not supported.");
            }

            return surface;
        }

#if !INFINITY_TARGET_ANDROID
        private static void EnsureMetalSelectorsLoaded()
        {
            if (s_MetalLayerClassInitialized)
            {
                return;
            }

            s_CAMetalLayerClass = new ObjectiveCClass("CAMetalLayer");
            s_IsKindOfClassSelector = "isKindOfClass:";
            s_ContentViewSelector = "contentView";
            s_RootViewControllerSelector = "rootViewController";
            s_ViewSelector = "view";
            s_LayerSelector = "layer";
            s_SetWantsLayerSelector = "setWantsLayer:";
            s_SetLayerSelector = "setLayer:";
            s_AddSublayerSelector = "addSublayer:";
            s_MetalLayerClassInitialized = true;
        }

        private static bool IsObjectOfClass(IntPtr objectPtr, ObjectiveCClass cls)
        {
            EnsureMetalSelectorsLoaded();
            return objectPtr != IntPtr.Zero && cls.NativePtr != IntPtr.Zero && ObjectiveCRuntime.bool_objc_msgSend(objectPtr, (IntPtr)s_IsKindOfClassSelector, cls.NativePtr);
        }

        private VkSurfaceKHR CreateMetalSurface(
            VkInstance instance,
            in VkMetalSurfaceCreateInfoEXT surfaceCreateInfo)
        {
            VkSurfaceKHR surface = default;
            VkMetalSurfaceCreateInfoEXT createInfo = surfaceCreateInfo;
            VulkanUtility.CheckErrors(VulkanNative.vkCreateMetalSurfaceEXT(instance, &createInfo, null, &surface));
            return surface;
        }

        private static void EnsureUIKitClassesLoaded()
        {
            if (s_UIKitClassesInitialized)
            {
                return;
            }

            EnsureMetalSelectorsLoaded();
            s_UIWindowClass = new ObjectiveCClass("UIWindow");
            s_UIViewClass = new ObjectiveCClass("UIView");
            s_UIKitClassesInitialized = true;
        }

        private static void EnsureAppKitClassesLoaded()
        {
            if (s_AppKitClassesInitialized)
            {
                return;
            }

            EnsureMetalSelectorsLoaded();
            s_NSWindowClass = new ObjectiveCClass("NSWindow");
            s_NSViewClass = new ObjectiveCClass("NSView");
            s_AppKitClassesInitialized = true;
        }

        private static IntPtr ResolveMetalLayer(IntPtr surfaceHandle, EOSPlatform platform)
        {
            EnsureMetalSelectorsLoaded();
            if (surfaceHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("SwapChain surface pointer is null.");
            }

            if (IsObjectOfClass(surfaceHandle, s_CAMetalLayerClass))
            {
                ObjectiveCRuntime.Retain(surfaceHandle);
                return surfaceHandle;
            }

            if (platform == EOSPlatform.MacOS)
            {
                EnsureAppKitClassesLoaded();

                if (IsObjectOfClass(surfaceHandle, s_NSWindowClass))
                {
                    IntPtr contentView = ObjectiveCRuntime.IntPtr_objc_msgSend(surfaceHandle, s_ContentViewSelector);
                    return EnsureMetalLayerForMacView(contentView);
                }

                if (IsObjectOfClass(surfaceHandle, s_NSViewClass))
                {
                    return EnsureMetalLayerForMacView(surfaceHandle);
                }
            }

            if (platform == EOSPlatform.iOS)
            {
                EnsureUIKitClassesLoaded();

                if (IsObjectOfClass(surfaceHandle, s_UIWindowClass))
                {
                    IntPtr rootViewController = ObjectiveCRuntime.IntPtr_objc_msgSend(surfaceHandle, s_RootViewControllerSelector);
                    IntPtr rootView = rootViewController != IntPtr.Zero ? ObjectiveCRuntime.IntPtr_objc_msgSend(rootViewController, s_ViewSelector) : IntPtr.Zero;
                    return EnsureMetalLayerForUIKitView(rootView);
                }

                if (IsObjectOfClass(surfaceHandle, s_UIViewClass))
                {
                    return EnsureMetalLayerForUIKitView(surfaceHandle);
                }
            }

            throw new InvalidOperationException("Unsupported Apple surface handle type. Expected NSWindow/NSView/UIWindow/UIView/CAMetalLayer.");
        }

        private static IntPtr EnsureMetalLayerForMacView(IntPtr nsView)
        {
            if (nsView == IntPtr.Zero)
            {
                throw new InvalidOperationException("NSView pointer is null while resolving CAMetalLayer.");
            }

            ObjectiveCRuntime.objc_msgSend(nsView, s_SetWantsLayerSelector, true);
            IntPtr existingLayer = ObjectiveCRuntime.IntPtr_objc_msgSend(nsView, s_LayerSelector);
            if (IsObjectOfClass(existingLayer, s_CAMetalLayerClass))
            {
                ObjectiveCRuntime.Retain(existingLayer);
                return existingLayer;
            }

            CAMetalLayer newLayer = CAMetalLayer.New();
            if (newLayer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create CAMetalLayer for NSView.");
            }

            ObjectiveCRuntime.objc_msgSend(nsView, s_SetLayerSelector, newLayer.NativePtr);
            return newLayer.NativePtr;
        }

        private static IntPtr EnsureMetalLayerForUIKitView(IntPtr uiView)
        {
            if (uiView == IntPtr.Zero)
            {
                throw new InvalidOperationException("UIView pointer is null while resolving CAMetalLayer.");
            }

            IntPtr baseLayer = ObjectiveCRuntime.IntPtr_objc_msgSend(uiView, s_LayerSelector);
            if (baseLayer == IntPtr.Zero)
            {
                throw new InvalidOperationException("UIView layer pointer is null while resolving CAMetalLayer.");
            }

            if (IsObjectOfClass(baseLayer, s_CAMetalLayerClass))
            {
                ObjectiveCRuntime.Retain(baseLayer);
                return baseLayer;
            }

            CAMetalLayer newLayer = CAMetalLayer.New();
            if (newLayer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create CAMetalLayer for UIView.");
            }

            ObjectiveCRuntime.objc_msgSend(baseLayer, s_AddSublayerSelector, newLayer.NativePtr);
            return newLayer.NativePtr;
        }
#endif

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

        protected override void Release()
        {
            ReleasePresentationState();
        }

        private static ERHIPixelFormat ConvertSwapchainVkFormatToRhiPixelFormat(in VkFormat format)
        {
            return format switch
            {
                VkFormat.B8G8R8A8Unorm => ERHIPixelFormat.B8G8R8A8_UNorm,
                VkFormat.R8G8B8A8Unorm => ERHIPixelFormat.R8G8B8A8_UNorm,
                VkFormat.A2B10G10R10UnormPack32 => ERHIPixelFormat.R10G10B10A2_UNorm,
                VkFormat.R16G16B16A16Sfloat => ERHIPixelFormat.R16G16B16A16_Float,
                _ => throw new NotSupportedException(
                    $"Vulkan swapchain format '{format}' has no exact " +
                    "SharpGPU pixel-format representation."),
            };
        }

        [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr GetModuleHandle(string? moduleName);

        private const VkStructureType VkStructureTypeWaylandSurfaceCreateInfoKHR = (VkStructureType)1000006000;

        [StructLayout(LayoutKind.Sequential)]
        private struct VkWaylandSurfaceCreateInfoKHR
        {
            public VkStructureType sType;
            public IntPtr pNext;
            public uint flags;
            public IntPtr display;
            public IntPtr surface;
        }

        #region Presentation

        private readonly struct VulkanSwapchainBuild
        {
            internal VkSwapchainKHR Swapchain { get; }
            internal VulkanTexture[] Textures { get; }
            internal uint2 Extent { get; }

            internal VulkanSwapchainBuild(
                VkSwapchainKHR swapchain,
                VulkanTexture[] textures,
                in uint2 extent)
            {
                Swapchain = swapchain;
                Textures = textures;
                Extent = extent;
            }
        }

        private sealed class VulkanSwapchainRetiredDuringBuildException :
            Exception
        {
            internal VulkanSwapchainRetiredDuringBuildException(
                Exception innerException)
                : base(
                    "The replacement VkSwapchainKHR was created, so the " +
                    "old swapchain is retired, but replacement image " +
                    "wrapping failed.",
                    innerException)
            {
            }
        }

        private sealed class VulkanSwapchainOutOfDateException :
            Exception
        {
            internal VulkanSwapchainOutOfDateException(
                string operation)
                : base(
                    $"{operation} reported VK_ERROR_OUT_OF_DATE_KHR.")
            {
            }
        }

        private VulkanSwapchainBuild BuildSwapchain(
            in RHISwapChainDescriptor descriptor,
            VkSurfaceKHR surface,
            VkSwapchainKHR oldSwapchain)
        {
            VkSurfaceCapabilitiesKHR capabilities;
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    surface,
                    &capabilities),
                "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

            VulkanCommandQueue presentQueue =
                RequirePresentQueue(in descriptor);
            VkBool32 presentSupported;
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfaceSupportKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    presentQueue.QueueFamilyIndex,
                    surface,
                    &presentSupported),
                "vkGetPhysicalDeviceSurfaceSupportKHR");
            if (!presentSupported)
            {
                throw new NotSupportedException(
                    $"Vulkan queue family {presentQueue.QueueFamilyIndex} " +
                    "cannot present to the requested surface.");
            }

            VkSurfaceFormatKHR selectedFormat = SelectSurfaceFormat(
                surface,
                VulkanUtility.ConvertToVkSwapChainFormat(
                    descriptor.Format));
            VkPresentModeKHR selectedPresentMode = SelectPresentMode(
                surface,
                VulkanUtility.ConvertToVkPresentMode(
                    descriptor.PresentMode));

            uint imageCount = Math.Max(
                descriptor.Count,
                capabilities.minImageCount);
            if (capabilities.maxImageCount > 0)
            {
                imageCount = Math.Min(
                    imageCount,
                    capabilities.maxImageCount);
            }

            uint2 actualExtent = SelectExtent(
                descriptor.Extent,
                in capabilities);
            VkImageUsageFlags usage =
                VkImageUsageFlags.ColorAttachment;
            if (!descriptor.FrameBufferOnly)
            {
                usage |=
                    VkImageUsageFlags.Sampled |
                    VkImageUsageFlags.TransferDst;
            }
            if ((capabilities.supportedUsageFlags & usage) != usage)
            {
                throw new NotSupportedException(
                    $"The Vulkan surface does not support requested " +
                    $"swapchain usage '{usage}'.");
            }

            VkCompositeAlphaFlagsKHR compositeAlpha =
                SelectCompositeAlpha(
                    capabilities.supportedCompositeAlpha);
            VkSwapchainCreateInfoKHR createInfo = new()
            {
                sType = VkStructureType.SwapchainCreateInfoKHR,
                surface = surface,
                minImageCount = imageCount,
                imageFormat = selectedFormat.format,
                imageColorSpace = selectedFormat.colorSpace,
                imageExtent = new VkExtent2D
                {
                    width = actualExtent.x,
                    height = actualExtent.y,
                },
                imageArrayLayers = 1,
                imageUsage = usage,
                imageSharingMode = VkSharingMode.Exclusive,
                queueFamilyIndexCount = 0,
                pQueueFamilyIndices = null,
                preTransform = capabilities.currentTransform,
                compositeAlpha = compositeAlpha,
                presentMode = selectedPresentMode,
                clipped = true,
                oldSwapchain = oldSwapchain,
            };

            VkSwapchainKHR swapchain = default;
            CheckSurfaceResult(
                VulkanNative.vkCreateSwapchainKHR(
                    m_VulkanDevice.NativeDevice,
                    &createInfo,
                    null,
                    &swapchain),
                "vkCreateSwapchainKHR");

            try
            {
                VulkanTexture[] textures =
                    CreateTextureWrappers(
                        swapchain,
                        selectedFormat.format,
                        in actualExtent);
                return new VulkanSwapchainBuild(
                    swapchain,
                    textures,
                    in actualExtent);
            }
            catch (Exception exception)
            {
                VulkanNative.vkDestroySwapchainKHR(
                    m_VulkanDevice.NativeDevice,
                    swapchain,
                    null);
                if (oldSwapchain.Handle != 0)
                {
                    throw new VulkanSwapchainRetiredDuringBuildException(
                        exception);
                }
                throw;
            }
        }

        private VulkanTexture[] CreateTextureWrappers(
            VkSwapchainKHR swapchain,
            VkFormat format,
            in uint2 extent)
        {
            uint imageCount = 0;
            CheckSurfaceResult(
                VulkanNative.vkGetSwapchainImagesKHR(
                    m_VulkanDevice.NativeDevice,
                    swapchain,
                    &imageCount,
                    null),
                "vkGetSwapchainImagesKHR(count)");
            if (imageCount == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan reported a swapchain with no images.");
            }

            VkImage* images =
                stackalloc VkImage[checked((int)imageCount)];
            CheckSurfaceResult(
                VulkanNative.vkGetSwapchainImagesKHR(
                    m_VulkanDevice.NativeDevice,
                    swapchain,
                    &imageCount,
                    images),
                "vkGetSwapchainImagesKHR(images)");

            RHITextureDescriptor textureDescriptor = new()
            {
                Extent = new uint3(extent, 1),
                MipCount = 1,
                SampleCount = ERHISampleCount.None,
                Format =
                    ConvertSwapchainVkFormatToRhiPixelFormat(format),
                UsageFlag = ERHITextureUsage.RenderTarget,
                Dimension = ERHITextureDimension.Texture2D,
                StorageMode = ERHIStorageMode.GPULocal,
            };

            VulkanTexture[] textures =
                new VulkanTexture[checked((int)imageCount)];
            int createdCount = 0;
            try
            {
                for (; createdCount < textures.Length; ++createdCount)
                {
                    textures[createdCount] = new VulkanTexture(
                        m_VulkanDevice,
                        textureDescriptor,
                        images[createdCount]);
                }
                return textures;
            }
            catch
            {
                for (int index = 0; index < createdCount; ++index)
                {
                    textures[index].Dispose();
                }
                throw;
            }
        }

        private RHISwapChainAcquireResult AcquireTyped(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            if (m_TerminalStatus !=
                ERHISwapChainStatus.Undefined)
            {
                return TerminalAcquireResult();
            }
            if (m_HasAcquiredImageThisFrame)
            {
                throw new InvalidOperationException(
                    "The Vulkan swapchain already has an acquired image.");
            }
            if (descriptor.SignalSemaphore == null &&
                descriptor.CompletionFence == null)
            {
                throw new ArgumentException(
                    "Vulkan acquisition requires a signal semaphore, " +
                    "a completion fence, or both.",
                    nameof(descriptor));
            }

            VkSemaphore nativeSemaphore = descriptor.SignalSemaphore switch
            {
                null => default,
                VulkanSemaphore semaphore => semaphore.NativeSemaphore,
                _ => throw new ArgumentException(
                    "The acquire signal semaphore is not a Vulkan semaphore.",
                    nameof(descriptor)),
            };
            VkFence nativeFence = descriptor.CompletionFence switch
            {
                null => default,
                VulkanFence fence => fence.NativeFence,
                _ => throw new ArgumentException(
                    "The acquire completion fence is not a Vulkan fence.",
                    nameof(descriptor)),
            };

            uint imageIndex = 0;
            VkResult nativeResult =
                VulkanNative.vkAcquireNextImageKHR(
                    m_VulkanDevice.NativeDevice,
                    m_NativeSwapChain,
                    descriptor.TimeoutNanoseconds,
                    nativeSemaphore,
                    nativeFence,
                    &imageIndex);

            switch (nativeResult)
            {
                case VkResult.Success:
                case VkResult.SuboptimalKHR:
                    if (imageIndex >= (uint)m_Textures.Length)
                    {
                        throw new InvalidOperationException(
                            $"Vulkan acquired image {imageIndex}, but " +
                            $"the swapchain exposes {m_Textures.Length} images.");
                    }
                    m_CurrentImageIndex = imageIndex;
                    m_HasAcquiredImageThisFrame = true;
                    return RHISwapChainAcquireResult.Acquired(
                        m_Textures[(int)imageIndex],
                        (int)imageIndex,
                        nativeResult == VkResult.SuboptimalKHR);
                case VkResult.NotReady:
                    return RHISwapChainAcquireResult.Unavailable(
                        ERHISwapChainStatus.NotReady);
                case VkResult.Timeout:
                    return RHISwapChainAcquireResult.Unavailable(
                        ERHISwapChainStatus.Timeout);
                case VkResult.ErrorOutOfDateKHR:
                    SetTerminal(
                        ERHISwapChainStatus.OutOfDate,
                        diagnostic: null);
                    return TerminalAcquireResult();
                case VkResult.ErrorSurfaceLostKHR:
                    SetTerminal(
                        ERHISwapChainStatus.SurfaceLost,
                        CreateDiagnostic(
                            nativeResult,
                            "vkAcquireNextImageKHR"));
                    return TerminalAcquireResult();
                case VkResult.ErrorDeviceLost:
                    SetTerminal(
                        ERHISwapChainStatus.DeviceLost,
                        CreateDiagnostic(
                            nativeResult,
                            "vkAcquireNextImageKHR"));
                    return TerminalAcquireResult();
                default:
                    VulkanUtility.CheckErrors(nativeResult);
                    throw new InvalidOperationException(
                        $"Unhandled Vulkan acquire result '{nativeResult}'.");
            }
        }

        private RHISwapChainOperationResult ResizeTyped(
            in RHISwapChainResizeDescriptor descriptor)
        {
            if (m_HasAcquiredImageThisFrame)
            {
                throw new InvalidOperationException(
                    "A Vulkan swapchain cannot be resized while an image " +
                    "is acquired.");
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
                descriptor.DisplayHandle != m_Descriptor.DisplayHandle ||
                descriptor.InstanceHandle != m_Descriptor.InstanceHandle ||
                descriptor.SurfaceGeneration >
                    m_Descriptor.SurfaceGeneration;
            if (m_TerminalStatus ==
                    ERHISwapChainStatus.SurfaceLost &&
                !surfaceChanged)
            {
                return TerminalOperationResult();
            }
            if (m_TerminalStatus ==
                ERHISwapChainStatus.DeviceLost)
            {
                return TerminalOperationResult();
            }

            RHISwapChainDescriptor replacementDescriptor = m_Descriptor;
            replacementDescriptor.Extent = descriptor.Extent;
            replacementDescriptor.SurfaceKind =
                descriptor.SurfaceKind;
            replacementDescriptor.WindowHandle =
                descriptor.WindowHandle;
            replacementDescriptor.DisplayHandle =
                descriptor.DisplayHandle;
            replacementDescriptor.InstanceHandle =
                descriptor.InstanceHandle;
            replacementDescriptor.SurfaceGeneration =
                descriptor.SurfaceGeneration;

            VkSurfaceKHR replacementSurface = m_Surface;
            IntPtr replacementMetalLayer = m_MetalLayerHandle;
            bool ownsReplacementSurface = false;
            if (surfaceChanged)
            {
                ValidateSurfaceDescriptor(
                    descriptor.SurfaceKind,
                    descriptor.WindowHandle,
                    descriptor.DisplayHandle);
                replacementSurface = CreateSurface(
                    in replacementDescriptor,
                    out replacementMetalLayer);
                ownsReplacementSurface = true;
            }

            VkSwapchainKHR oldForCreate =
                surfaceChanged ? default : m_NativeSwapChain;
            try
            {
                VulkanSwapchainBuild build = BuildSwapchain(
                    in replacementDescriptor,
                    replacementSurface,
                    oldForCreate);

                VkSwapchainKHR oldSwapchain = m_NativeSwapChain;
                VkSurfaceKHR oldSurface = m_Surface;
                IntPtr oldMetalLayer = m_MetalLayerHandle;
                VulkanTexture[] oldTextures = m_Textures;

                CommitBuild(
                    in build,
                    in replacementDescriptor);
                m_Surface = replacementSurface;
                m_MetalLayerHandle = replacementMetalLayer;
                ownsReplacementSurface = false;
                m_TerminalStatus =
                    ERHISwapChainStatus.Undefined;
                m_TerminalDiagnostic = null;

                DisposeTextureWrappers(oldTextures);
                DestroyReplacedNativeState(
                    oldSwapchain,
                    oldSurface,
                    oldMetalLayer,
                    surfaceChanged);
                return RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.Success);
            }
            catch (VulkanSwapchainRetiredDuringBuildException exception)
            {
                SetTerminal(
                    ERHISwapChainStatus.OutOfDate,
                    diagnostic: null);
                ExceptionDispatchInfo.Capture(
                    exception.InnerException ?? exception).Throw();
                throw;
            }
            catch (VulkanSwapchainOutOfDateException)
            {
                SetTerminal(
                    ERHISwapChainStatus.OutOfDate,
                    diagnostic: null);
                return TerminalOperationResult();
            }
            catch (RHIException exception)
                when (exception.ErrorCode ==
                    ERHIErrorCode.SurfaceLost)
            {
                SetTerminal(
                    ERHISwapChainStatus.SurfaceLost,
                    exception);
                return TerminalOperationResult();
            }
            catch (RHIException exception)
                when (exception.ErrorCode ==
                    ERHIErrorCode.DeviceLost)
            {
                SetTerminal(
                    ERHISwapChainStatus.DeviceLost,
                    exception);
                return TerminalOperationResult();
            }
            finally
            {
                if (ownsReplacementSurface)
                {
                    DestroySurface(
                        replacementSurface,
                        replacementMetalLayer);
                }
            }
        }

        private bool PresentTyped(
            in RHISwapChainPresentDescriptor descriptor,
            out RHISwapChainOperationResult result)
        {
            if (m_TerminalStatus !=
                ERHISwapChainStatus.Undefined)
            {
                result = TerminalOperationResult();
                return false;
            }
            if (!m_HasAcquiredImageThisFrame)
            {
                throw new InvalidOperationException(
                    "Present requires one successfully acquired Vulkan image.");
            }
            if (descriptor.CompletionFence != null &&
                !m_VulkanDevice.SupportsSwapchainMaintenance)
            {
                throw new NotSupportedException(
                    "Vulkan present completion requires " +
                    "VK_KHR/EXT_swapchain_maintenance1.");
            }

            ReadOnlySpan<RHISemaphore> waits =
                descriptor.WaitSemaphores.Span;
            VkSemaphore* nativeWaits =
                stackalloc VkSemaphore[Math.Max(waits.Length, 1)];
            for (int index = 0; index < waits.Length; ++index)
            {
                if (waits[index] is not VulkanSemaphore semaphore)
                {
                    throw new ArgumentException(
                        $"Present wait semaphore at index {index} is not Vulkan.",
                        nameof(descriptor));
                }
                nativeWaits[index] = semaphore.NativeSemaphore;
            }

            VkSwapchainPresentFenceInfoKHR presentFenceInfo = default;
            VkFence nativePresentFence = default;
            void* presentNext = null;
            if (descriptor.CompletionFence != null)
            {
                if (descriptor.CompletionFence is not
                    VulkanFence completionFence)
                {
                    throw new ArgumentException(
                        "Present completion fence is not a Vulkan fence.",
                        nameof(descriptor));
                }
                nativePresentFence = completionFence.NativeFence;
                presentFenceInfo.sType =
                    VkStructureType.SwapchainPresentFenceInfoKHR;
                presentFenceInfo.swapchainCount = 1;
                presentFenceInfo.pFences = &nativePresentFence;
                presentNext = &presentFenceInfo;
            }

            VulkanCommandQueue queue =
                RequirePresentQueue(in m_Descriptor);
            VkSwapchainKHR swapchain = m_NativeSwapChain;
            uint imageIndex = m_CurrentImageIndex;
            VkResult perSwapchainResult = VkResult.Success;
            VkPresentInfoKHR presentInfo = new()
            {
                sType = VkStructureType.PresentInfoKHR,
                pNext = presentNext,
                waitSemaphoreCount = (uint)waits.Length,
                pWaitSemaphores =
                    waits.IsEmpty ? null : nativeWaits,
                swapchainCount = 1,
                pSwapchains = &swapchain,
                pImageIndices = &imageIndex,
                pResults = &perSwapchainResult,
            };

            VkResult queueResult = VulkanNative.vkQueuePresentKHR(
                queue.NativeQueue,
                &presentInfo);
            VkResult nativeResult = queueResult == VkResult.Success
                ? perSwapchainResult
                : queueResult;

            switch (nativeResult)
            {
                case VkResult.Success:
                    m_HasAcquiredImageThisFrame = false;
                    result = RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.Success);
                    return true;
                case VkResult.SuboptimalKHR:
                    m_HasAcquiredImageThisFrame = false;
                    result = RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.Suboptimal);
                    return true;
                case VkResult.ErrorOutOfDateKHR:
                    m_HasAcquiredImageThisFrame = false;
                    SetTerminal(
                        ERHISwapChainStatus.OutOfDate,
                        diagnostic: null);
                    result = TerminalOperationResult();
                    return true;
                case VkResult.ErrorSurfaceLostKHR:
                    m_HasAcquiredImageThisFrame = false;
                    SetTerminal(
                        ERHISwapChainStatus.SurfaceLost,
                        CreateDiagnostic(
                            nativeResult,
                            "vkQueuePresentKHR"));
                    result = TerminalOperationResult();
                    return true;
                case VkResult.ErrorDeviceLost:
                    m_HasAcquiredImageThisFrame = false;
                    SetTerminal(
                        ERHISwapChainStatus.DeviceLost,
                        CreateDiagnostic(
                            nativeResult,
                            "vkQueuePresentKHR"));
                    result = TerminalOperationResult();
                    return true;
                case VkResult.ErrorOutOfHostMemory:
                case VkResult.ErrorOutOfDeviceMemory:
                    VulkanUtility.CheckErrors(nativeResult);
                    break;
                default:
                    VulkanUtility.CheckErrors(nativeResult);
                    break;
            }

            throw new InvalidOperationException(
                $"Unhandled Vulkan present result '{nativeResult}'.");
        }

        private VkSurfaceFormatKHR SelectSurfaceFormat(
            VkSurfaceKHR surface,
            VkFormat requestedFormat)
        {
            uint formatCount = 0;
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfaceFormatsKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    surface,
                    &formatCount,
                    null),
                "vkGetPhysicalDeviceSurfaceFormatsKHR(count)");
            if (formatCount == 0)
            {
                throw new NotSupportedException(
                    "The Vulkan surface exposes no formats.");
            }

            VkSurfaceFormatKHR* formats =
                stackalloc VkSurfaceFormatKHR[
                    checked((int)formatCount)];
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfaceFormatsKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    surface,
                    &formatCount,
                    formats),
                "vkGetPhysicalDeviceSurfaceFormatsKHR(formats)");

            if (formatCount == 1 &&
                formats[0].format == VkFormat.Undefined)
            {
                VkSurfaceFormatKHR selected = formats[0];
                selected.format = requestedFormat;
                return selected;
            }

            // Prefer exact match, then same-bit layout channel order
            // (RGBA ↔ BGRA). Android commonly exposes only RGBA; Windows
            // Vulkan often prefers BGRA. Report the selected VkFormat via
            // ConvertSwapchainVkFormatToRhiPixelFormat — no silent fake.
            VkFormat channelOrderFallback =
                GetSwapchainChannelOrderFallback(requestedFormat);
            int fallbackIndex = -1;
            for (int index = 0; index < formatCount; ++index)
            {
                if (formats[index].format == requestedFormat)
                {
                    return formats[index];
                }
                if (fallbackIndex < 0 &&
                    channelOrderFallback != VkFormat.Undefined &&
                    formats[index].format == channelOrderFallback)
                {
                    fallbackIndex = index;
                }
            }

            if (fallbackIndex >= 0)
            {
                return formats[fallbackIndex];
            }

            throw new NotSupportedException(
                $"The Vulkan surface does not support requested format " +
                $"'{requestedFormat}'" +
                (channelOrderFallback == VkFormat.Undefined
                    ? "."
                    : $" or channel-order fallback '{channelOrderFallback}'."));
        }

        private static VkFormat GetSwapchainChannelOrderFallback(
            VkFormat requestedFormat)
        {
            return requestedFormat switch
            {
                VkFormat.R8G8B8A8Unorm => VkFormat.B8G8R8A8Unorm,
                VkFormat.B8G8R8A8Unorm => VkFormat.R8G8B8A8Unorm,
                VkFormat.R8G8B8A8Srgb => VkFormat.B8G8R8A8Srgb,
                VkFormat.B8G8R8A8Srgb => VkFormat.R8G8B8A8Srgb,
                _ => VkFormat.Undefined,
            };
        }

        private VkPresentModeKHR SelectPresentMode(
            VkSurfaceKHR surface,
            VkPresentModeKHR requestedMode)
        {
            uint modeCount = 0;
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfacePresentModesKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    surface,
                    &modeCount,
                    null),
                "vkGetPhysicalDeviceSurfacePresentModesKHR(count)");
            if (modeCount == 0)
            {
                throw new NotSupportedException(
                    "The Vulkan surface exposes no present modes.");
            }

            VkPresentModeKHR* modes =
                stackalloc VkPresentModeKHR[checked((int)modeCount)];
            CheckSurfaceResult(
                VulkanNative.vkGetPhysicalDeviceSurfacePresentModesKHR(
                    m_VulkanDevice.NativePhysicalDevice,
                    surface,
                    &modeCount,
                    modes),
                "vkGetPhysicalDeviceSurfacePresentModesKHR(modes)");
            for (int index = 0; index < modeCount; ++index)
            {
                if (modes[index] == requestedMode)
                {
                    return requestedMode;
                }
            }
            throw new NotSupportedException(
                $"The Vulkan surface does not support requested present " +
                $"mode '{requestedMode}'.");
        }

        private static uint2 SelectExtent(
            in uint2 requestedExtent,
            in VkSurfaceCapabilitiesKHR capabilities)
        {
            if (capabilities.currentExtent.width != uint.MaxValue)
            {
                return new uint2(
                    capabilities.currentExtent.width,
                    capabilities.currentExtent.height);
            }
            return new uint2(
                Math.Clamp(
                    requestedExtent.x,
                    capabilities.minImageExtent.width,
                    capabilities.maxImageExtent.width),
                Math.Clamp(
                    requestedExtent.y,
                    capabilities.minImageExtent.height,
                    capabilities.maxImageExtent.height));
        }

        private static VkCompositeAlphaFlagsKHR SelectCompositeAlpha(
            VkCompositeAlphaFlagsKHR supported)
        {
            VkCompositeAlphaFlagsKHR[] preferences =
            {
                VkCompositeAlphaFlagsKHR.Opaque,
                VkCompositeAlphaFlagsKHR.PreMultiplied,
                VkCompositeAlphaFlagsKHR.PostMultiplied,
                VkCompositeAlphaFlagsKHR.Inherit,
            };
            foreach (VkCompositeAlphaFlagsKHR candidate in preferences)
            {
                if ((supported & candidate) != 0)
                {
                    return candidate;
                }
            }
            throw new NotSupportedException(
                "The Vulkan surface exposes no supported composite-alpha mode.");
        }

        private void CommitBuild(
            in VulkanSwapchainBuild build,
            in RHISwapChainDescriptor descriptor)
        {
            m_NativeSwapChain = build.Swapchain;
            m_Textures = build.Textures;
            m_Descriptor = descriptor;
            m_Descriptor.Extent = build.Extent;
            m_CurrentImageIndex = 0;
            m_HasAcquiredImageThisFrame = false;
        }

        private void DestroyReplacedNativeState(
            VkSwapchainKHR oldSwapchain,
            VkSurfaceKHR oldSurface,
            IntPtr oldMetalLayer,
            bool surfaceChanged)
        {
            VulkanNative.vkDestroySwapchainKHR(
                m_VulkanDevice.NativeDevice,
                oldSwapchain,
                null);
            if (surfaceChanged)
            {
                DestroySurface(
                    oldSurface,
                    oldMetalLayer);
            }
        }

        private void SetTerminal(
            ERHISwapChainStatus status,
            RHIException? diagnostic)
        {
            m_TerminalStatus = status;
            m_TerminalDiagnostic = diagnostic;
        }

        private RHISwapChainAcquireResult TerminalAcquireResult()
        {
            return RHISwapChainAcquireResult.Unavailable(
                m_TerminalStatus,
                m_TerminalDiagnostic);
        }

        private RHISwapChainOperationResult TerminalOperationResult()
        {
            return RHISwapChainOperationResult.FromStatus(
                m_TerminalStatus,
                m_TerminalDiagnostic);
        }

        private RHIException CreateDiagnostic(
            VkResult result,
            string operation)
        {
            return new RHIException(
                result == VkResult.ErrorDeviceLost
                    ? ERHIErrorCode.DeviceLost
                    : ERHIErrorCode.SurfaceLost,
                ERHIBackend.Vulkan,
                (int)result,
                $"{operation} returned {result}.",
                result == VkResult.ErrorDeviceLost
                    ? ERHIDeviceState.Lost
                    : ERHIDeviceState.Operational);
        }

        private void CheckSurfaceResult(
            VkResult result,
            string operation)
        {
            if (result == VkResult.Success)
            {
                return;
            }
            if (result == VkResult.ErrorOutOfDateKHR)
            {
                throw new VulkanSwapchainOutOfDateException(
                    operation);
            }
            if (result is
                VkResult.ErrorSurfaceLostKHR or
                VkResult.ErrorDeviceLost)
            {
                throw CreateDiagnostic(result, operation);
            }
            VulkanUtility.CheckErrors(result);
        }

        private VulkanCommandQueue RequirePresentQueue(
            in RHISwapChainDescriptor descriptor)
        {
            if (descriptor.PresentQueue is not
                    VulkanCommandQueue queue ||
                !ReferenceEquals(
                    queue.VulkanDevice,
                    m_VulkanDevice))
            {
                throw new ArgumentException(
                    "Vulkan present queue must belong to the same device.",
                    nameof(descriptor));
            }
            if (queue.IsDisposed)
            {
                throw new ObjectDisposedException(
                    queue.GetType().FullName);
            }
            return queue;
        }

        private void ValidateCreateDescriptor(
            in RHISwapChainDescriptor descriptor)
        {
            ValidateSurfaceDescriptor(
                descriptor.SurfaceKind,
                descriptor.WindowHandle,
                descriptor.DisplayHandle);
            if (descriptor.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Vulkan swapchain image count must be positive.");
            }
            if (descriptor.Extent.x == 0 ||
                descriptor.Extent.y == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Vulkan swapchain extent must be non-zero.");
            }
            _ = RequirePresentQueue(in descriptor);
        }

        private static void ValidateSurfaceDescriptor(
            RHINativeSurfaceKind surfaceKind,
            IntPtr windowHandle,
            IntPtr displayHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "Vulkan native surface handle must not be null.",
                    nameof(windowHandle));
            }
            if (surfaceKind is
                RHINativeSurfaceKind.X11Window or
                RHINativeSurfaceKind.WaylandSurface &&
                displayHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "Vulkan X11/Wayland surfaces require a display handle.",
                    nameof(displayHandle));
            }
            if (surfaceKind is
                RHINativeSurfaceKind.Unknown or
                RHINativeSurfaceKind.Headless)
            {
                throw new NotSupportedException(
                    $"Vulkan presentation does not support surface kind " +
                    $"'{surfaceKind}'.");
            }
        }

        private static void DisposeTextureWrappers(
            VulkanTexture[] textures)
        {
            foreach (VulkanTexture texture in textures)
            {
                texture.Dispose();
            }
        }

        private void DestroySurface(
            VkSurfaceKHR surface,
            IntPtr metalLayerHandle)
        {
            if (surface.Handle != 0)
            {
                VulkanNative.vkDestroySurfaceKHR(
                    m_VulkanDevice.VulkanInstance.NativeInstance,
                    surface,
                    null);
            }
            if (metalLayerHandle != IntPtr.Zero)
            {
#if INFINITY_TARGET_ANDROID
                throw new InvalidOperationException(
                    "Android Vulkan swapchains must not own a Metal layer handle.");
#else
                ObjectiveCRuntime.Release(metalLayerHandle);
#endif
            }
        }

        private void ReleasePresentationState()
        {
            DisposeTextureWrappers(m_Textures);
            m_Textures = Array.Empty<VulkanTexture>();

            if (m_NativeSwapChain.Handle != 0)
            {
                VulkanNative.vkDestroySwapchainKHR(
                    m_VulkanDevice.NativeDevice,
                    m_NativeSwapChain,
                    null);
                m_NativeSwapChain = default;
            }

            DestroySurface(m_Surface, m_MetalLayerHandle);
            m_Surface = default;
            m_MetalLayerHandle = IntPtr.Zero;
        }
        #endregion
    }
#pragma warning restore CS8600, CS8602, CS8618, CA1416
}
