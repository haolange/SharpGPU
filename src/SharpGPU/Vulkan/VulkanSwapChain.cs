using System;
using Infinity.Mathmatics;
using Vortice.Vulkan;
using System.Runtime.InteropServices;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618, CA1416
    internal unsafe partial class VulkanSwapChain : RHISwapChain
    {
        public override int BackTextureIndex => (int)m_CurrentImageIndex;
        public VkSwapchainKHR NativeSwapChain => m_NativeSwapChain;

        private VulkanDevice m_VulkanDevice;
        private VulkanTexture[] m_Textures;
        private VkSwapchainKHR m_NativeSwapChain;
        private VkSurfaceKHR m_Surface;
        private RHISwapChainDescriptor m_Descriptor;
        private uint m_CurrentImageIndex;
        private VkFence m_ImageAcquireFence;
        private bool m_HasAcquiredImageThisFrame;
        private IntPtr m_X11Display;
        private IntPtr m_MetalLayerHandle;

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

        public VulkanSwapChain(VulkanDevice device, in RHISwapChainDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            CreateSurface(descriptor);
            CreateSwapChain(descriptor);
            CreateImageAcquireFence();
        }

        private void CreateSurface(in RHISwapChainDescriptor descriptor)
        {
            VulkanInstance vkInstance = m_VulkanDevice.VulkanInstance;

            switch (VulkanUtility.GetCurrentOSPlatfom())
            {
                case EOSPlatform.Windows:
                {
                    // TODO(UNVERIFIED): Validate on Windows runtime (x64/x86_64).
                    IntPtr moduleHandle = GetModuleHandle(null);
                    if (moduleHandle == IntPtr.Zero)
                    {
                        moduleHandle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                    }

                    VkWin32SurfaceCreateInfoKHR surfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.Win32SurfaceCreateInfoKHR,
                        hwnd = descriptor.Surface,
                        hinstance = moduleHandle,
                    };

                    fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
                    {
                        VulkanUtility.CheckErrors(VulkanNative.vkCreateWin32SurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, surfacePtr));
                    }
                    break;
                }
                case EOSPlatform.Linux:
                {
                    // TODO(UNVERIFIED): Validate on Linux/X11 runtime.
                    m_X11Display = XOpenDisplay(IntPtr.Zero);
                    if (m_X11Display == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to open X11 display via XOpenDisplay(null).");
                    }

                    VkXlibSurfaceCreateInfoKHR surfaceCreateInfo = new VkXlibSurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.XlibSurfaceCreateInfoKHR,
                        dpy = m_X11Display,
                        window = (ulong)descriptor.Surface,
                    };

                    fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
                    {
                        VulkanUtility.CheckErrors(VulkanNative.vkCreateXlibSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, surfacePtr));
                    }
                    break;
                }
                case EOSPlatform.Android:
                {
                    // TODO(UNVERIFIED): Validate on Android runtime with ANativeWindow* surface handle.
                    VkAndroidSurfaceCreateInfoKHR surfaceCreateInfo = new VkAndroidSurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.AndroidSurfaceCreateInfoKHR,
                        window = descriptor.Surface,
                    };

                    fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
                    {
                        VulkanUtility.CheckErrors(VulkanNative.vkCreateAndroidSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, surfacePtr));
                    }
                    break;
                }
                case EOSPlatform.MacOS:
                case EOSPlatform.iOS:
                {
                    // TODO(UNVERIFIED): iOS path requires device runtime verification.
                    m_MetalLayerHandle = ResolveMetalLayer(descriptor.Surface, VulkanUtility.GetCurrentOSPlatfom());
                    if (m_MetalLayerHandle == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to resolve CAMetalLayer for Vulkan surface creation.");
                    }

                    VkMetalSurfaceCreateInfoEXT surfaceCreateInfo = new VkMetalSurfaceCreateInfoEXT()
                    {
                        sType = VkStructureType.MetalSurfaceCreateInfoEXT,
                        pLayer = m_MetalLayerHandle,
                    };
                    CreateMetalSurface(vkInstance.NativeInstance, in surfaceCreateInfo);
                    break;
                }
                default:
                    throw new PlatformNotSupportedException("Vulkan swapchain surface creation is not supported on the current platform.");
            }
        }

        private static bool IsObjectOfClass(IntPtr objectPtr, ObjectiveCClass cls)
        {
            return objectPtr != IntPtr.Zero && cls.NativePtr != IntPtr.Zero && ObjectiveCRuntime.bool_objc_msgSend(objectPtr, (IntPtr)s_IsKindOfClassSelector, cls.NativePtr);
        }

        private void CreateMetalSurface(VkInstance instance, in VkMetalSurfaceCreateInfoEXT surfaceCreateInfo)
        {
            fixed (VkMetalSurfaceCreateInfoEXT* createInfoPtr = &surfaceCreateInfo)
            fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateMetalSurfaceEXT(instance, createInfoPtr, null, surfacePtr));
            }
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

        private void CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            VkSurfaceCapabilitiesKHR capabilities;
            VulkanNative.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(m_VulkanDevice.NativePhysicalDevice, m_Surface, &capabilities);

            VkFormat desiredFormat = VulkanUtility.ConvertToVkSwapChainFormat(descriptor.Format);
            VkPresentModeKHR presentMode = VulkanUtility.ConvertToVkPresentMode(descriptor.PresentMode);

            // Check surface format support
            uint formatCount;
            VulkanNative.vkGetPhysicalDeviceSurfaceFormatsKHR(m_VulkanDevice.NativePhysicalDevice, m_Surface, &formatCount, null);
            VkSurfaceFormatKHR* formats = stackalloc VkSurfaceFormatKHR[(int)formatCount];
            VulkanNative.vkGetPhysicalDeviceSurfaceFormatsKHR(m_VulkanDevice.NativePhysicalDevice, m_Surface, &formatCount, formats);

            VkSurfaceFormatKHR selectedFormat = formats[0];
            for (int i = 0; i < formatCount; ++i)
            {
                if (formats[i].format == desiredFormat)
                {
                    selectedFormat = formats[i];
                    break;
                }
            }

            uint imageCount = descriptor.Count;
            if (imageCount < capabilities.minImageCount) imageCount = capabilities.minImageCount;
            if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount) imageCount = capabilities.maxImageCount;

            VkExtent2D extent;
            extent.width = descriptor.Extent.x;
            extent.height = descriptor.Extent.y;

            VkImageUsageFlags usage = VkImageUsageFlags.ColorAttachment;
            if (!descriptor.FrameBufferOnly)
            {
                usage |= VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst;
            }

            VulkanCommandQueue vkQueue = descriptor.PresentQueue as VulkanCommandQueue;
            uint queueFamilyIndex = vkQueue.QueueFamilyIndex;

            VkSwapchainCreateInfoKHR swapChainInfo = new VkSwapchainCreateInfoKHR()
            {
                sType = VkStructureType.SwapchainCreateInfoKHR,
                surface = m_Surface,
                minImageCount = imageCount,
                imageFormat = selectedFormat.format,
                imageColorSpace = selectedFormat.colorSpace,
                imageExtent = extent,
                imageArrayLayers = 1,
                imageUsage = usage,
                imageSharingMode = VkSharingMode.Exclusive,
                queueFamilyIndexCount = 1,
                pQueueFamilyIndices = &queueFamilyIndex,
                preTransform = capabilities.currentTransform,
                compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
                presentMode = presentMode,
                clipped = true,
            };

            fixed (VkSwapchainKHR* swapChainPtr = &m_NativeSwapChain)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSwapchainKHR(m_VulkanDevice.NativeDevice, &swapChainInfo, null, swapChainPtr));
            }

            FetchSwapChainTextures(selectedFormat.format);
        }

        private void FetchSwapChainTextures(VkFormat format)
        {
            uint imageCount = 0;
            VulkanNative.vkGetSwapchainImagesKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, &imageCount, null);
            VkImage* images = stackalloc VkImage[(int)imageCount];
            VulkanNative.vkGetSwapchainImagesKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, &imageCount, images);

            m_Textures = new VulkanTexture[(int)imageCount];

            RHITextureDescriptor textureDescriptor;
            textureDescriptor.Extent = new uint3(m_Descriptor.Extent.xy, 1);
            textureDescriptor.MipCount = 1;
            textureDescriptor.SampleCount = ERHISampleCount.None;
            textureDescriptor.Format = ConvertSwapchainVkFormatToRhiPixelFormat(format);
            textureDescriptor.UsageFlag = ERHITextureUsage.RenderTarget;
            textureDescriptor.Dimension = ERHITextureDimension.Texture2D;
            textureDescriptor.StorageMode = ERHIStorageMode.GPULocal;

            for (int i = 0; i < imageCount; ++i)
            {
                m_Textures[i] = new VulkanTexture(m_VulkanDevice, textureDescriptor, images[i]);
            }
        }

        private void CreateImageAcquireFence()
        {
            VkFenceCreateInfo fenceInfo = new VkFenceCreateInfo()
            {
                sType = VkStructureType.FenceCreateInfo,
                flags = VkFenceCreateFlags.Signaled,
            };

            fixed (VkFence* fencePtr = &m_ImageAcquireFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateFence(m_VulkanDevice.NativeDevice, &fenceInfo, null, fencePtr));
            }
        }

        public override RHITexture AcquireBackBufferTexture()
        {
            if (m_HasAcquiredImageThisFrame)
            {
                return m_Textures[m_CurrentImageIndex];
            }

            fixed (VkFence* fencePtr = &m_ImageAcquireFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkWaitForFences(m_VulkanDevice.NativeDevice, 1, fencePtr, true, ulong.MaxValue));
                VulkanUtility.CheckErrors(VulkanNative.vkResetFences(m_VulkanDevice.NativeDevice, 1, fencePtr));
            }

            const ulong acquireTimeoutNs = 1_000_000_000UL;
            const int maxAcquireRetries = 8;
            VkResult acquireResult = VkResult.Timeout;

            for (int retry = 0; retry < maxAcquireRetries; ++retry)
            {
                fixed (uint* imageIndexPtr = &m_CurrentImageIndex)
                {
                    acquireResult = VulkanNative.vkAcquireNextImageKHR(
                        m_VulkanDevice.NativeDevice,
                        m_NativeSwapChain,
                        acquireTimeoutNs,
                        default,
                        m_ImageAcquireFence,
                        imageIndexPtr);
                }

                if (acquireResult == VkResult.Success || acquireResult == VkResult.SuboptimalKHR)
                {
                    break;
                }

                if (acquireResult != VkResult.Timeout && acquireResult != VkResult.NotReady)
                {
                    VulkanUtility.CheckErrors(acquireResult);
                }

                System.Threading.Thread.Sleep(1);
            }

            fixed (VkFence* fencePtr = &m_ImageAcquireFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkWaitForFences(m_VulkanDevice.NativeDevice, 1, fencePtr, true, ulong.MaxValue));
            }

            if (acquireResult != VkResult.Success && acquireResult != VkResult.SuboptimalKHR)
            {
                VulkanUtility.CheckErrors(acquireResult);
            }

            m_HasAcquiredImageThisFrame = true;
            return m_Textures[m_CurrentImageIndex];
        }

        public override void Resize(in uint2 extent)
        {
            VulkanNative.vkDeviceWaitIdle(m_VulkanDevice.NativeDevice);
            m_HasAcquiredImageThisFrame = false;

            // Release old textures (external image wrappers)
            ReleaseTextures();

            // Destroy old swap chain
            VulkanNative.vkDestroySwapchainKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, null);

            // Update descriptor
            m_Descriptor.Extent = extent;

            // Recreate
            CreateSwapChain(m_Descriptor);
        }

        public override void Present()
        {
            if (!m_HasAcquiredImageThisFrame)
            {
                return;
            }

            VulkanCommandQueue vkQueue = m_Descriptor.PresentQueue as VulkanCommandQueue;
            VkSwapchainKHR swapChain = m_NativeSwapChain;
            uint imageIndex = m_CurrentImageIndex;

            VkPresentInfoKHR presentInfo = new VkPresentInfoKHR()
            {
                sType = VkStructureType.PresentInfoKHR,
                waitSemaphoreCount = 0,
                swapchainCount = 1,
                pSwapchains = &swapChain,
                pImageIndices = &imageIndex,
            };

            VulkanNative.vkQueuePresentKHR(vkQueue.NativeQueue, &presentInfo);
            m_HasAcquiredImageThisFrame = false;
        }

        private void ReleaseTextures()
        {
            if (m_Textures != null)
            {
                for (int i = 0; i < m_Textures.Length; ++i)
                {
                    m_Textures[i]?.Dispose();
                }
                m_Textures = null;
            }
        }

        protected override void Release()
        {
            ReleaseTextures();
            VulkanNative.vkDestroyFence(m_VulkanDevice.NativeDevice, m_ImageAcquireFence, null);
            VulkanNative.vkDestroySwapchainKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, null);
            VulkanNative.vkDestroySurfaceKHR(m_VulkanDevice.VulkanInstance.NativeInstance, m_Surface, null);

            if (m_X11Display != IntPtr.Zero)
            {
                XCloseDisplay(m_X11Display);
                m_X11Display = IntPtr.Zero;
            }

            if (m_MetalLayerHandle != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_MetalLayerHandle);
                m_MetalLayerHandle = IntPtr.Zero;
            }
        }

        private static ERHIPixelFormat ConvertSwapchainVkFormatToRhiPixelFormat(in VkFormat format)
        {
            return format switch
            {
                VkFormat.B8G8R8A8Unorm => ERHIPixelFormat.B8G8R8A8_UNorm,
                VkFormat.R8G8B8A8Unorm => ERHIPixelFormat.R8G8B8A8_UNorm,
                VkFormat.A2B10G10R10UnormPack32 => ERHIPixelFormat.R10G10B10A2_UNorm,
                VkFormat.R16G16B16A16Sfloat => ERHIPixelFormat.R16G16B16A16_Float,
                _ => ERHIPixelFormat.B8G8R8A8_UNorm,
            };
        }

        [LibraryImport("kernel32", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr GetModuleHandle(string? moduleName);

        [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
        private static partial IntPtr XOpenDisplay(IntPtr displayName);

        [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
        private static partial int XCloseDisplay(IntPtr display);
    }
#pragma warning restore CS8600, CS8602, CS8618, CA1416
}
