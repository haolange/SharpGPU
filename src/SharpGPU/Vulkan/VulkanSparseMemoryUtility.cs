using System;
using SharpGPU.Mathematics;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal static unsafe class VulkanSparseMemoryUtility
    {
        internal static void ValidateDescriptor(
            VulkanDevice device,
            in RHITextureDescriptor descriptor)
        {
            _ = VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: true);
            if (descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new NotSupportedException(
                    "Vulkan sparse textures require GPU-local storage.");
            }
            if (descriptor.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException(
                    "Vulkan sparse textures currently require one sample per texel.");
            }
            if (descriptor.MipCount > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "A uint texture extent cannot contain more than 32 sparse mip levels.");
            }
            if (RHIBarrierUtility.InferAspectMask(descriptor.Format) !=
                ERHITextureAspectMask.Color)
            {
                throw new NotSupportedException(
                    "Vulkan sparse texture mapping currently exposes exact color-aspect tiling only.");
            }
            if (descriptor.Dimension is
                ERHITextureDimension.Texture2DMS or
                ERHITextureDimension.Texture2DArrayMS or
                ERHITextureDimension.TextureCube or
                ERHITextureDimension.TextureCubeArray)
            {
                throw new NotSupportedException(
                    "Vulkan sparse texture mapping supports 2D, 2D-array, and 3D color textures.");
            }
            bool dimensionSupported =
                descriptor.Dimension == ERHITextureDimension.Texture3D
                    ? device.SparseResidencyImage3DSupported
                    : device.SparseResidencyImage2DSupported;
            if (!dimensionSupported)
            {
                throw new NotSupportedException(
                    $"Vulkan sparse residency is unavailable for {descriptor.Dimension}.");
            }
        }

        internal static VkImage CreateSparseImage(
            VulkanDevice device,
            in RHITextureDescriptor descriptor)
        {
            ValidateDescriptor(device, descriptor);
            VkImageCreateInfo createInfo =
                VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: true);
            VkImage image = default;
            VkResult result = VulkanNative.vkCreateImage(
                device.NativeDevice,
                &createInfo,
                null,
                &image);
            if (result is VkResult.ErrorFormatNotSupported or
                VkResult.ErrorFeatureNotPresent)
            {
                throw new NotSupportedException(
                    $"The Vulkan device cannot create a sparse {descriptor.Dimension} {descriptor.Format} image.");
            }

            VulkanUtility.CheckErrors(result);
            return image;
        }

        internal static RHISparseTextureMemoryRequirements QueryRequirements(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            VkImage image)
        {
            ValidateDescriptor(device, descriptor);
            VkMemoryRequirements memoryRequirements;
            VulkanNative.vkGetImageMemoryRequirements(
                device.NativeDevice,
                image,
                &memoryRequirements);
            uint sparseRequirementCount = 0;
            VulkanNative.vkGetImageSparseMemoryRequirements(
                device.NativeDevice,
                image,
                &sparseRequirementCount,
                null);
            if (sparseRequirementCount != 1)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image must expose exactly one color-aspect memory requirement.");
            }

            VkSparseImageMemoryRequirements nativeRequirements;
            uint requestedCount = 1;
            VulkanNative.vkGetImageSparseMemoryRequirements(
                device.NativeDevice,
                image,
                &requestedCount,
                &nativeRequirements);
            if (requestedCount != 1 ||
                nativeRequirements.formatProperties.aspectMask !=
                    VkImageAspectFlags.Color)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image exposes an unsupported aspect or metadata requirement.");
            }

            VkExtent3D granularity =
                nativeRequirements.formatProperties.imageGranularity;
            if (granularity.width == 0 ||
                granularity.height == 0 ||
                granularity.depth == 0)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image reported an invalid image granularity.");
            }
            if (memoryRequirements.alignment == 0 ||
                (memoryRequirements.alignment &
                 (memoryRequirements.alignment - 1)) != 0)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image reported a non-power-of-two tile alignment.");
            }

            uint arrayLayerCount = VulkanUtility.GetArrayLayers(
                descriptor.Dimension,
                descriptor.Extent.z);
            uint standardMipCount = Math.Min(
                descriptor.MipCount,
                nativeRequirements.imageMipTailFirstLod);
            RHISparseTextureSubresourceTiling[] subresources =
                new RHISparseTextureSubresourceTiling[
                    checked((int)(standardMipCount * arrayLayerCount))];
            int destination = 0;
            for (uint layer = 0; layer < arrayLayerCount; ++layer)
            {
                for (uint mip = 0; mip < standardMipCount; ++mip)
                {
                    uint mipWidth = MipExtent(descriptor.Extent.x, mip);
                    uint mipHeight = MipExtent(descriptor.Extent.y, mip);
                    uint mipDepth = descriptor.Dimension ==
                        ERHITextureDimension.Texture3D
                            ? MipExtent(descriptor.Extent.z, mip)
                            : 1;
                    subresources[destination++] =
                        new RHISparseTextureSubresourceTiling(
                            ERHITextureAspectMask.Color,
                            mip,
                            layer,
                            new uint3(
                                DivideRoundUp(mipWidth, granularity.width),
                                DivideRoundUp(mipHeight, granularity.height),
                                DivideRoundUp(mipDepth, granularity.depth)));
                }
            }

            RHISparseTextureMipTail[] mipTails;
            if (nativeRequirements.imageMipTailSize == 0 ||
                nativeRequirements.imageMipTailFirstLod >= descriptor.MipCount)
            {
                mipTails = Array.Empty<RHISparseTextureMipTail>();
            }
            else if ((nativeRequirements.formatProperties.flags &
                      VkSparseImageFormatFlags.SingleMiptail) != 0)
            {
                mipTails = new[]
                {
                    new RHISparseTextureMipTail(
                        0,
                        ERHITextureAspectMask.Color,
                        nativeRequirements.imageMipTailFirstLod,
                        0,
                        arrayLayerCount,
                        nativeRequirements.imageMipTailOffset,
                        nativeRequirements.imageMipTailSize),
                };
            }
            else
            {
                mipTails = new RHISparseTextureMipTail[
                    checked((int)arrayLayerCount)];
                for (uint layer = 0; layer < arrayLayerCount; ++layer)
                {
                    mipTails[checked((int)layer)] =
                        new RHISparseTextureMipTail(
                            layer,
                            ERHITextureAspectMask.Color,
                            nativeRequirements.imageMipTailFirstLod,
                            layer,
                            1,
                            checked(
                                nativeRequirements.imageMipTailOffset +
                                layer * nativeRequirements.imageMipTailStride),
                            nativeRequirements.imageMipTailSize);
                }
            }

            ulong compatibilityMask =
                VulkanMemoryUtility.FilterCompatibleMemoryTypes(
                    device,
                    memoryRequirements.memoryTypeBits,
                    descriptor.StorageMode);
            RHIResourceMemoryRequirements heapCompatibility =
                new RHIResourceMemoryRequirements(
                    device,
                    memoryRequirements.alignment,
                    memoryRequirements.alignment,
                    descriptor.StorageMode,
                    compatibilityMask,
                    ERHIMemoryResourceKind.Texture);
            return new RHISparseTextureMemoryRequirements(
                device,
                descriptor,
                memoryRequirements.size,
                memoryRequirements.alignment,
                new uint3(
                    granularity.width,
                    granularity.height,
                    granularity.depth),
                heapCompatibility,
                subresources,
                mipTails);
        }

        private static uint MipExtent(uint value, uint mipLevel)
        {
            return Math.Max(1u, value >> checked((int)mipLevel));
        }

        private static uint DivideRoundUp(uint value, uint divisor)
        {
            return checked((uint)(((ulong)value + divisor - 1) / divisor));
        }
    }
}
