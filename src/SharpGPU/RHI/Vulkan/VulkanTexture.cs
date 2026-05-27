using System;
using SharpGPU.Mathematics;
using Vortice.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanTexture : RHITexture
    {
        public VulkanDevice VulkanDevice
        {
            get
            {
                return m_VulkanDevice;
            }
        }
        public VkImage NativeImage
        {
            get
            {
                return m_NativeImage;
            }
        }
        public VkDeviceMemory NativeMemory
        {
            get
            {
                return m_NativeMemory;
            }
        }
        internal VkImageLayout CurrentLayout
        {
            get;
            set;
        }

        private bool m_IsExternalImage;
        private VulkanDevice m_VulkanDevice;
        private VkImage m_NativeImage;
        private VkDeviceMemory m_NativeMemory;

        public VulkanTexture(VulkanDevice device, in RHITextureDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_IsExternalImage = false;
            CurrentLayout = VkImageLayout.Undefined;

            VkImageCreateInfo imageInfo = new VkImageCreateInfo()
            {
                sType = VkStructureType.ImageCreateInfo,
                flags = VulkanUtility.ConvertToVkImageCreateFlags(descriptor.Dimension),
                imageType = VulkanUtility.ConvertToVkImageType(descriptor.Dimension),
                format = VulkanUtility.ConvertToVkFormat(descriptor.Format),
                extent = new VkExtent3D() { width = descriptor.Extent.x, height = descriptor.Extent.y, depth = descriptor.Extent.z },
                mipLevels = descriptor.MipCount,
                arrayLayers = VulkanUtility.GetArrayLayers(descriptor.Dimension, descriptor.Extent.z),
                samples = VulkanUtility.ConvertToVkSampleCount(descriptor.SampleCount),
                tiling = VkImageTiling.Optimal,
                usage = VulkanUtility.ConvertToVkImageUsage(descriptor.UsageFlag),
                sharingMode = VkSharingMode.Exclusive,
                initialLayout = VkImageLayout.Undefined,
            };

            fixed (VkImage* imagePtr = &m_NativeImage)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateImage(device.NativeDevice, &imageInfo, null, imagePtr));
            }

            VkMemoryRequirements memRequirements;
            VulkanNative.vkGetImageMemoryRequirements(device.NativeDevice, m_NativeImage, &memRequirements);

            VkMemoryPropertyFlags memProps = VulkanUtility.ConvertToVkMemoryProperty(descriptor.StorageMode);
            uint memTypeIndex = VulkanUtility.FindMemoryType(device.MemoryProperties, memRequirements.memoryTypeBits, memProps);

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.MemoryAllocateInfo,
                allocationSize = memRequirements.size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }

            VulkanUtility.CheckErrors(VulkanNative.vkBindImageMemory(device.NativeDevice, m_NativeImage, m_NativeMemory, 0));
        }

        internal VulkanTexture(VulkanDevice device, in RHITextureDescriptor descriptor, VkImage existingImage)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_NativeImage = existingImage;
            m_IsExternalImage = true;
            CurrentLayout = VkImageLayout.Undefined;
        }

        public override RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor)
        {
            return new VulkanTextureView(this, descriptor);
        }

        protected override void Release()
        {
            if (!m_IsExternalImage)
            {
                VulkanNative.vkDestroyImage(m_VulkanDevice.NativeDevice, m_NativeImage, null);
                VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            }
        }
    }
#pragma warning restore CS8618
}


