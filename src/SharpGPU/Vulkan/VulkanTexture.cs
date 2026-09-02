using System;
using System.Collections.Generic;
using Vortice.Vulkan;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    internal unsafe class VulkanTexture : RHITexture
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                ThrowIfDisposed();
                return m_VulkanDevice;
            }
        }
        public VkImage NativeImage
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeImage;
            }
        }
        public VkDeviceMemory NativeMemory
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeMemory;
            }
        }
        internal RHISparseTextureMemoryRequirements? SparseRequirements { get; }

        private bool m_IsExternalImage;
        private VulkanDevice m_VulkanDevice;
        private VkImage m_NativeImage;
        private VkDeviceMemory m_NativeMemory;
        private bool m_OwnsMemory = true;
        private RHIHeapPlacement? m_Placement;

        public VulkanTexture(VulkanDevice device, in RHITextureDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_IsExternalImage = false;

            VkImageCreateInfo imageInfo =
                VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: false);

            try
            {
                fixed (VkImage* imagePtr = &m_NativeImage)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreateImage(
                            device.NativeDevice,
                            &imageInfo,
                            null,
                            imagePtr));
                }

                VkMemoryRequirements memRequirements;
                VulkanNative.vkGetImageMemoryRequirements(
                    device.NativeDevice,
                    m_NativeImage,
                    &memRequirements);

                VkMemoryPropertyFlags memProps =
                    VulkanUtility.ConvertToVkMemoryProperty(
                        descriptor.StorageMode);
                uint memTypeIndex = VulkanUtility.FindMemoryType(
                    device.MemoryProperties,
                    memRequirements.memoryTypeBits,
                    memProps);

                VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
                {
                    sType = VkStructureType.MemoryAllocateInfo,
                    allocationSize = memRequirements.size,
                    memoryTypeIndex = memTypeIndex,
                };

                fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkAllocateMemory(
                            device.NativeDevice,
                            &allocInfo,
                            null,
                            memPtr));
                }

                VulkanUtility.CheckErrors(
                    VulkanNative.vkBindImageMemory(
                        device.NativeDevice,
                        m_NativeImage,
                        m_NativeMemory,
                        0));
            }
            catch
            {
                ReleaseNativeAllocationAfterConstructionFailure();
                throw;
            }
        }

        internal VulkanTexture(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            VulkanHeap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_IsExternalImage = false;
            m_OwnsMemory = false;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;
            m_NativeMemory = heap.NativeMemory;

            VkImageCreateInfo imageInfo =
                VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: false);
            fixed (VkImage* imagePtr = &m_NativeImage)
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateImage(device.NativeDevice, &imageInfo, null, imagePtr));
            }

            try
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkBindImageMemory(
                        device.NativeDevice,
                        m_NativeImage,
                        m_NativeMemory,
                        heapOffset));
                m_Placement = placement;
            }
            catch
            {
                VulkanNative.vkDestroyImage(device.NativeDevice, m_NativeImage, null);
                m_NativeImage = default;
                throw;
            }
        }

        internal VulkanTexture(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            bool createSparse)
        {
            if (!createSparse)
            {
                throw new ArgumentException(
                    "The sparse texture constructor requires sparse creation.",
                    nameof(createSparse));
            }

            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_IsExternalImage = false;
            m_OwnsMemory = false;
            m_AllocationMode = ERHIResourceAllocationMode.Sparse;
            m_NativeImage = VulkanSparseMemoryUtility.CreateSparseImage(
                device,
                descriptor);
            try
            {
                SparseRequirements =
                    VulkanSparseMemoryUtility.QueryRequirements(
                        device,
                        descriptor,
                        m_NativeImage);
            }
            catch
            {
                VulkanNative.vkDestroyImage(
                    device.NativeDevice,
                    m_NativeImage,
                    null);
                m_NativeImage = default;
                throw;
            }
        }

        internal VulkanTexture(VulkanDevice device, in RHITextureDescriptor descriptor, VkImage existingImage)
        {
            if (!RHIFormatSupportQuery.IsKnownTextureUsage(descriptor.UsageFlag))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.UsageFlag,
                    "Texture usage must be a non-zero combination of known ERHITextureUsage bits.");
            }

            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_NativeImage = existingImage;
            m_IsExternalImage = true;
            m_OwnsMemory = false;
            m_AllocationMode = ERHIResourceAllocationMode.External;
        }

        public override RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new VulkanTextureView(this, descriptor);
        }

        protected override void Release()
        {
            if (!m_IsExternalImage)
            {
                VulkanNative.vkDestroyImage(m_VulkanDevice.NativeDevice, m_NativeImage, null);
                if (m_OwnsMemory)
                {
                    VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
                }
            }
            m_NativeImage = default;
            m_NativeMemory = default;
            m_Placement?.Dispose();
            m_Placement = null;
        }

        private void ReleaseNativeAllocationAfterConstructionFailure()
        {
            if (m_NativeMemory.Handle != 0)
            {
                VulkanNative.vkFreeMemory(
                    m_VulkanDevice.NativeDevice,
                    m_NativeMemory,
                    null);
                m_NativeMemory = default;
            }
            if (m_NativeImage.Handle != 0)
            {
                VulkanNative.vkDestroyImage(
                    m_VulkanDevice.NativeDevice,
                    m_NativeImage,
                    null);
                m_NativeImage = default;
            }
        }
    }
}


namespace SharpGPU
{
    internal static class VulkanTextureSubresourceRangeUtility
    {
        internal static RHITextureSubresourceRange Normalize(
            in RHITextureDescriptor descriptor,
            in RHITextureSubresourceRange range)
        {
            VkImageAspectFlags availableNativeAspects =
                VulkanUtility.GetVkImageAspect(descriptor.Format);
            ERHITextureAspectMask availableAspects = 0;
            if ((availableNativeAspects &
                 VkImageAspectFlags.Color) != 0)
            {
                availableAspects |=
                    ERHITextureAspectMask.Color;
            }
            if ((availableNativeAspects &
                 VkImageAspectFlags.Depth) != 0)
            {
                availableAspects |=
                    ERHITextureAspectMask.Depth;
            }
            if ((availableNativeAspects &
                 VkImageAspectFlags.Stencil) != 0)
            {
                availableAspects |=
                    ERHITextureAspectMask.Stencil;
            }

            ERHITextureAspectMask aspects =
                range.AspectMask == ERHITextureAspectMask.None
                    ? availableAspects
                    : range.AspectMask;
            if (aspects == ERHITextureAspectMask.None ||
                (aspects & ~availableAspects) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    "The texture subresource aspect mask is " +
                    "incompatible with the image format.");
            }

            uint totalMipCount =
                Math.Max(1u, descriptor.MipCount);
            uint totalLayerCount = descriptor.Dimension switch
            {
                ERHITextureDimension.Texture2D or
                    ERHITextureDimension.Texture2DMS => 1,
                ERHITextureDimension.TextureCube => 6,
                ERHITextureDimension.TextureCubeArray =>
                    checked(Math.Max(1u, descriptor.Extent.z) * 6),
                _ => Math.Max(1u, descriptor.Extent.z),
            };

            uint mipCount =
                range.MipLevelCount ==
                    RHITextureSubresourceRange.All
                    ? checked(
                        totalMipCount - range.BaseMipLevel)
                    : range.MipLevelCount;
            uint layerCount =
                range.ArrayLayerCount ==
                    RHITextureSubresourceRange.All
                    ? checked(
                        totalLayerCount -
                        range.BaseArrayLayer)
                    : range.ArrayLayerCount;
            if (range.BaseMipLevel >= totalMipCount ||
                mipCount == 0 ||
                (ulong)range.BaseMipLevel + mipCount >
                    totalMipCount ||
                range.BaseArrayLayer >= totalLayerCount ||
                layerCount == 0 ||
                (ulong)range.BaseArrayLayer + layerCount >
                    totalLayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(range),
                    "The texture subresource range is outside the " +
                    "image.");
            }

            return new RHITextureSubresourceRange
            {
                AspectMask = aspects,
                BaseMipLevel = range.BaseMipLevel,
                MipLevelCount = mipCount,
                BaseArrayLayer = range.BaseArrayLayer,
                ArrayLayerCount = layerCount,
            };
        }
    }
}

