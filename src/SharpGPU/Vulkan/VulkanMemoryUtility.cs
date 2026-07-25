using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal static class VulkanMemoryUtility
    {
        internal static VkBufferCreateInfo BuildBufferCreateInfo(in RHIBufferDescriptor descriptor)
        {
            if (descriptor.ByteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Buffer byte size must be greater than zero.");
            }
            ValidateStorageMode(descriptor.StorageMode);
            if (descriptor.StorageMode == ERHIStorageMode.Memoryless)
            {
                throw new NotSupportedException(
                    "Vulkan memoryless allocations are image-only.");
            }

            return new VkBufferCreateInfo
            {
                sType = VkStructureType.BufferCreateInfo,
                size = checked((ulong)descriptor.ByteSize),
                usage = VulkanUtility.ConvertToVkBufferUsage(descriptor.UsageFlag),
                sharingMode = VkSharingMode.Exclusive,
            };
        }

        internal static VkImageCreateInfo BuildImageCreateInfo(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            bool sparse)
        {
            ValidateStorageMode(descriptor.StorageMode);
            if (descriptor.MipCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture mip count must be non-zero.");
            }
            if (descriptor.Extent.x == 0 || descriptor.Extent.y == 0 || descriptor.Extent.z == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture extent must be non-zero on every axis.");
            }
            if (!Enum.IsDefined(descriptor.Dimension) || descriptor.Dimension == ERHITextureDimension.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture dimension is unknown.");
            }
            if (!Enum.IsDefined(descriptor.SampleCount) || descriptor.SampleCount == ERHISampleCount.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture sample count is unknown.");
            }
            if (!Enum.IsDefined(descriptor.Format) ||
                descriptor.Format is ERHIPixelFormat.Unknown or ERHIPixelFormat.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture format is unknown.");
            }

            VkImageCreateFlags flags = VulkanUtility.ConvertToVkImageCreateFlags(descriptor.Dimension);
            if (sparse)
            {
                flags |= VkImageCreateFlags.SparseBinding | VkImageCreateFlags.SparseResidency;
            }

            return new VkImageCreateInfo
            {
                sType = VkStructureType.ImageCreateInfo,
                flags = flags,
                imageType = VulkanUtility.ConvertToVkImageType(descriptor.Dimension),
                format = VulkanUtility.ConvertToVkFormat(descriptor.Format),
                extent = new VkExtent3D
                {
                    width = descriptor.Extent.x,
                    height = descriptor.Extent.y,
                    depth = descriptor.Dimension == ERHITextureDimension.Texture3D
                        ? descriptor.Extent.z
                        : 1,
                },
                mipLevels = descriptor.MipCount,
                arrayLayers = VulkanUtility.GetArrayLayers(descriptor.Dimension, descriptor.Extent.z),
                samples = VulkanUtility.ConvertToVkSampleCount(descriptor.SampleCount),
                tiling = VkImageTiling.Optimal,
                usage = VulkanUtility.ConvertToVkImageUsage(
                    descriptor.UsageFlag,
                    device.SupportsAttachmentFeedbackLoopLayout),
                sharingMode = VkSharingMode.Exclusive,
                initialLayout = VkImageLayout.Undefined,
            };
        }

        internal static ulong FilterCompatibleMemoryTypes(
            VulkanDevice device,
            uint nativeMemoryTypeBits,
            ERHIStorageMode storageMode)
        {
            VkMemoryPropertyFlags requiredProperties =
                VulkanUtility.ConvertToVkMemoryProperty(storageMode);
            ulong result = 0;
            VkPhysicalDeviceMemoryProperties properties = device.MemoryProperties;
            for (uint index = 0; index < properties.memoryTypeCount; ++index)
            {
                uint bit = 1U << checked((int)index);
                if ((nativeMemoryTypeBits & bit) != 0 &&
                    (properties.GetMemoryType(index).propertyFlags & requiredProperties) == requiredProperties)
                {
                    result |= 1UL << checked((int)index);
                }
            }

            if (result == 0)
            {
                throw new NotSupportedException(
                    $"No Vulkan memory type satisfies {storageMode} for the queried resource.");
            }

            return result;
        }

        private static void ValidateStorageMode(ERHIStorageMode storageMode)
        {
            if (!Enum.IsDefined(storageMode) || storageMode == ERHIStorageMode.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(storageMode), storageMode, "Unknown storage mode.");
            }
        }
    }
}
