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


