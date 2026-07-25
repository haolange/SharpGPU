using System;
using Vortice.Vulkan;

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
