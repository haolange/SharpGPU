using System;
using Infinity.Mathmatics;
using Evergine.Bindings.Vulkan;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanSwapChain : RHISwapChain
    {
        public override int BackTextureIndex => (int)m_CurrentImageIndex;
        public VkSwapchainKHR NativeSwapChain => m_NativeSwapChain;

        private VulkanDevice m_VulkanDevice;
        private VulkanTexture[] m_Textures;
        private VkSwapchainKHR m_NativeSwapChain;
        private VkSurfaceKHR m_Surface;
        private RHISwapChainDescriptor m_Descriptor;
        private uint m_CurrentImageIndex;
        private VkSemaphore m_ImageAvailableSemaphore;

        public VulkanSwapChain(VulkanDevice device, in RHISwapChainDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            CreateSurface(descriptor);
            CreateSwapChain(descriptor);
            CreateImageAvailableSemaphore();
        }

        private void CreateSurface(in RHISwapChainDescriptor descriptor)
        {
            VulkanInstance vkInstance = m_VulkanDevice.VulkanInstance;

            switch (VulkanUtility.GetCurrentOSPlatfom())
            {
                case EOSPlatform.Windows:
                {
                    VkWin32SurfaceCreateInfoKHR surfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR,
                        hwnd = descriptor.Surface,
                        hinstance = System.Diagnostics.Process.GetCurrentProcess().Handle,
                    };

                    fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
                    {
                        VulkanUtility.CheckErrors(VulkanNative.vkCreateWin32SurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, surfacePtr));
                    }
                    break;
                }
                case EOSPlatform.Linux:
                {
                    VkXlibSurfaceCreateInfoKHR surfaceCreateInfo = new VkXlibSurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR,
                        window = (nint)descriptor.Surface.ToInt64(),
                    };

                    fixed (VkSurfaceKHR* surfacePtr = &m_Surface)
                    {
                        VulkanUtility.CheckErrors(VulkanNative.vkCreateXlibSurfaceKHR(vkInstance.NativeInstance, &surfaceCreateInfo, null, surfacePtr));
                    }
                    break;
                }
            }
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

            VkImageUsageFlags usage = VkImageUsageFlags.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
            if (!descriptor.FrameBufferOnly)
            {
                usage |= VkImageUsageFlags.VK_IMAGE_USAGE_SAMPLED_BIT | VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            }

            VulkanCommandQueue vkQueue = descriptor.PresentQueue as VulkanCommandQueue;
            uint queueFamilyIndex = vkQueue.QueueFamilyIndex;

            VkSwapchainCreateInfoKHR swapChainInfo = new VkSwapchainCreateInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR,
                surface = m_Surface,
                minImageCount = imageCount,
                imageFormat = selectedFormat.format,
                imageColorSpace = selectedFormat.colorSpace,
                imageExtent = extent,
                imageArrayLayers = 1,
                imageUsage = usage,
                imageSharingMode = VkSharingMode.VK_SHARING_MODE_EXCLUSIVE,
                queueFamilyIndexCount = 1,
                pQueueFamilyIndices = &queueFamilyIndex,
                preTransform = capabilities.currentTransform,
                compositeAlpha = VkCompositeAlphaFlagsKHR.VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR,
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
            textureDescriptor.Format = RHIUtility.ConvertToPixelFormat(m_Descriptor.Format);
            textureDescriptor.UsageFlag = ERHITextureUsage.RenderTarget;
            textureDescriptor.Dimension = ERHITextureDimension.Texture2D;
            textureDescriptor.StorageMode = ERHIStorageMode.GPULocal;

            for (int i = 0; i < imageCount; ++i)
            {
                m_Textures[i] = new VulkanTexture(m_VulkanDevice, textureDescriptor, images[i]);
            }
        }

        private void CreateImageAvailableSemaphore()
        {
            VkSemaphoreCreateInfo semaphoreInfo = new VkSemaphoreCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO,
            };

            fixed (VkSemaphore* semPtr = &m_ImageAvailableSemaphore)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSemaphore(m_VulkanDevice.NativeDevice, &semaphoreInfo, null, semPtr));
            }
        }

        public override RHITexture AcquireBackBufferTexture()
        {
            fixed (uint* imageIndexPtr = &m_CurrentImageIndex)
            {
                VulkanNative.vkAcquireNextImageKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, ulong.MaxValue, m_ImageAvailableSemaphore, default, imageIndexPtr);
            }
            return m_Textures[m_CurrentImageIndex];
        }

        public override void Resize(in uint2 extent)
        {
            VulkanNative.vkDeviceWaitIdle(m_VulkanDevice.NativeDevice);

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
            VulkanCommandQueue vkQueue = m_Descriptor.PresentQueue as VulkanCommandQueue;
            VkSwapchainKHR swapChain = m_NativeSwapChain;
            uint imageIndex = m_CurrentImageIndex;

            VkPresentInfoKHR presentInfo = new VkPresentInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PRESENT_INFO_KHR,
                waitSemaphoreCount = 0,
                swapchainCount = 1,
                pSwapchains = &swapChain,
                pImageIndices = &imageIndex,
            };

            VulkanNative.vkQueuePresentKHR(vkQueue.NativeQueue, &presentInfo);
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
            VulkanNative.vkDestroySemaphore(m_VulkanDevice.NativeDevice, m_ImageAvailableSemaphore, null);
            VulkanNative.vkDestroySwapchainKHR(m_VulkanDevice.NativeDevice, m_NativeSwapChain, null);
            VulkanNative.vkDestroySurfaceKHR(m_VulkanDevice.VulkanInstance.NativeInstance, m_Surface, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
