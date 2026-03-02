using System;
using Infinity.Mathmatics;
using Vortice.Vulkan;
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
        private VkFence m_ImageAcquireFence;
        private bool m_HasAcquiredImageThisFrame;

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
                    VkWin32SurfaceCreateInfoKHR surfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR()
                    {
                        sType = VkStructureType.Win32SurfaceCreateInfoKHR,
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
                        sType = VkStructureType.XlibSurfaceCreateInfoKHR,
                        window = (ulong)descriptor.Surface,
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
    }
#pragma warning restore CS8600, CS8602, CS8618
}


