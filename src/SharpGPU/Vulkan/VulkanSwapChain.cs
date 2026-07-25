using System;
using Vortice.Vulkan;
using SharpGPU.Mathematics;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;
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

        private static ObjectiveCClass s_NSWindowClass;
        private static ObjectiveCClass s_NSViewClass;
        private static readonly ObjectiveCClass s_CAMetalLayerClass = new ObjectiveCClass("CAMetalLayer");
        private static ObjectiveCClass s_UIWindowClass;
        private static ObjectiveCClass s_UIViewClass;
        private static bool s_AppKitClassesInitialized;
        private static bool s_UIKitClassesInitialized;

        private static readonly Selector s_IsKindOfClassSelector = "isKindOfClass:";
        private static readonly Selector s_ContentViewSelector = "contentView";
        private static readonly Selector s_RootViewControllerSelector = "rootViewController";
        private static readonly Selector s_ViewSelector = "view";
        private static readonly Selector s_LayerSelector = "layer";
        private static readonly Selector s_SetWantsLayerSelector = "setWantsLayer:";
        private static readonly Selector s_SetLayerSelector = "setLayer:";
        private static readonly Selector s_AddSublayerSelector = "addSublayer:";

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
                    // TODO(UNVERIFIED): iOS path requires device runtime verification.
                    EOSPlatform platform = descriptor.SurfaceKind == RHINativeSurfaceKind.AppKitNsWindow ? EOSPlatform.MacOS : EOSPlatform.iOS;
                    metalLayerHandle = ResolveMetalLayer(descriptor.WindowHandle, platform);
                    if (metalLayerHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to resolve CAMetalLayer for Vulkan surface creation.");
                    }

                    VkMetalSurfaceCreateInfoEXT surfaceCreateInfo = new VkMetalSurfaceCreateInfoEXT()
                    {
                        sType = VkStructureType.MetalSurfaceCreateInfoEXT,
                        pLayer = metalLayerHandle,
                    };
                    try
                    {
                        surface = CreateMetalSurface(
                            vkInstance.NativeInstance,
                            in surfaceCreateInfo);
                    }
                    catch
                    {
                        ObjectiveCRuntime.Release(
                            metalLayerHandle);
                        metalLayerHandle = IntPtr.Zero;
                        throw;
                    }
                    break;
                }
                default:
                    throw new PlatformNotSupportedException($"Vulkan swapchain surface kind '{descriptor.SurfaceKind}' is not supported.");
            }

            return surface;
        }

        private static bool IsObjectOfClass(IntPtr objectPtr, ObjectiveCClass cls)
        {
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

            s_NSWindowClass = new ObjectiveCClass("NSWindow");
            s_NSViewClass = new ObjectiveCClass("NSView");
            s_AppKitClassesInitialized = true;
        }

        private static IntPtr ResolveMetalLayer(IntPtr surfaceHandle, EOSPlatform platform)
        {
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

        private VulkanSwapchainBuild BuildSwapchain(
            in RHISwapChainDescriptor descriptor,
            VkSurfaceKHR surface,
            VkSwapchainKHR oldSwapchain)
        {
            return BuildSwapchainCore(
                in descriptor,
                surface,
                oldSwapchain);
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
    }
#pragma warning restore CS8600, CS8602, CS8618, CA1416
}
