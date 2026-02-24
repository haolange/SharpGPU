using System;
using System.Text;
using Infinity.Core;
using NUnit.Framework;
using System.Diagnostics;
using Infinity.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal enum EOSPlatform
    {
        Windows,
        Linux,
        Android,
        MacOS,
        iOS
    }

    internal static unsafe class VulkanUtility
    {
        public static EOSPlatform GetCurrentOSPlatfom()
        {
            return !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? EOSPlatform.Windows : (!RuntimeInformation.OSDescription.Contains("Darwin") ? EOSPlatform.MacOS : EOSPlatform.iOS)) : (!RuntimeInformation.OSDescription.Contains("Unix") ? EOSPlatform.Linux : EOSPlatform.Android)) : EOSPlatform.Windows;
        }

        public static byte* ToPointer(this string text)
        {
            return (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(text);
        }

        public static uint Version(uint major, uint minor, uint patch)
        {
            return (major << 22) | (minor << 12) | patch;
        }

        public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
        {
            return (&memoryProperties.memoryTypes_0)[index];
        }

        public static unsafe string GetString(byte* stringStart)
        {
            int characters = 0;
            while (stringStart[characters] != 0)
            {
                characters++;
            }

            return System.Text.Encoding.UTF8.GetString(stringStart, characters);
        }

        [Conditional("DEBUG")]
        public static void CheckErrors(VkResult result)
        {
            if (result != VkResult.VK_SUCCESS)
            {
                throw new InvalidOperationException(result.ToString());
            }
        }

        public static uint FindMemoryType(VkPhysicalDeviceMemoryProperties memProperties, uint typeFilter, VkMemoryPropertyFlags properties)
        {
            for (uint i = 0; i < memProperties.memoryTypeCount; ++i)
            {
                if ((typeFilter & (1u << (int)i)) != 0 && (memProperties.GetMemoryType(i).propertyFlags & properties) == properties)
                {
                    return i;
                }
            }

            throw new InvalidOperationException("Failed to find suitable memory type.");
        }

        public static VkFormat ConvertToVkFormat(in ERHIPixelFormat format)
        {
            switch (format)
            {
                // 8-Bits
                case ERHIPixelFormat.R8_UInt:
                    return VkFormat.VK_FORMAT_R8_UINT;
                case ERHIPixelFormat.R8_SInt:
                    return VkFormat.VK_FORMAT_R8_SINT;
                case ERHIPixelFormat.R8_UNorm:
                    return VkFormat.VK_FORMAT_R8_UNORM;
                case ERHIPixelFormat.R8_SNorm:
                    return VkFormat.VK_FORMAT_R8_SNORM;
                // 16-Bits
                case ERHIPixelFormat.R16_UInt:
                    return VkFormat.VK_FORMAT_R16_UINT;
                case ERHIPixelFormat.R16_SInt:
                    return VkFormat.VK_FORMAT_R16_SINT;
                case ERHIPixelFormat.R16_Float:
                    return VkFormat.VK_FORMAT_R16_SFLOAT;
                case ERHIPixelFormat.R8G8_UInt:
                    return VkFormat.VK_FORMAT_R8G8_UINT;
                case ERHIPixelFormat.R8G8_SInt:
                    return VkFormat.VK_FORMAT_R8G8_SINT;
                case ERHIPixelFormat.R8G8_UNorm:
                    return VkFormat.VK_FORMAT_R8G8_UNORM;
                case ERHIPixelFormat.R8G8_SNorm:
                    return VkFormat.VK_FORMAT_R8G8_SNORM;
                // 32-Bits
                case ERHIPixelFormat.R32_UInt:
                    return VkFormat.VK_FORMAT_R32_UINT;
                case ERHIPixelFormat.R32_SInt:
                    return VkFormat.VK_FORMAT_R32_SINT;
                case ERHIPixelFormat.R32_Float:
                    return VkFormat.VK_FORMAT_R32_SFLOAT;
                case ERHIPixelFormat.R16G16_UInt:
                    return VkFormat.VK_FORMAT_R16G16_UINT;
                case ERHIPixelFormat.R16G16_SInt:
                    return VkFormat.VK_FORMAT_R16G16_SINT;
                case ERHIPixelFormat.R16G16_Float:
                    return VkFormat.VK_FORMAT_R16G16_SFLOAT;
                case ERHIPixelFormat.R8G8B8A8_UInt:
                    return VkFormat.VK_FORMAT_R8G8B8A8_UINT;
                case ERHIPixelFormat.R8G8B8A8_SInt:
                    return VkFormat.VK_FORMAT_R8G8B8A8_SINT;
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return VkFormat.VK_FORMAT_R8G8B8A8_UNORM;
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return VkFormat.VK_FORMAT_R8G8B8A8_SRGB;
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return VkFormat.VK_FORMAT_R8G8B8A8_SNORM;
                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return VkFormat.VK_FORMAT_B8G8R8A8_UNORM;
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return VkFormat.VK_FORMAT_B8G8R8A8_SRGB;
                case ERHIPixelFormat.R99GB99_E5_Float:
                    return VkFormat.VK_FORMAT_E5B9G9R9_UFLOAT_PACK32;
                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return VkFormat.VK_FORMAT_A2B10G10R10_UINT_PACK32;
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return VkFormat.VK_FORMAT_A2B10G10R10_UNORM_PACK32;
                case ERHIPixelFormat.R11G11B10_Float:
                    return VkFormat.VK_FORMAT_B10G11R11_UFLOAT_PACK32;
                // 64-Bits
                case ERHIPixelFormat.RG32_UInt:
                    return VkFormat.VK_FORMAT_R32G32_UINT;
                case ERHIPixelFormat.RG32_SInt:
                    return VkFormat.VK_FORMAT_R32G32_SINT;
                case ERHIPixelFormat.RG32_Float:
                    return VkFormat.VK_FORMAT_R32G32_SFLOAT;
                case ERHIPixelFormat.R16G16B16A16_UInt:
                    return VkFormat.VK_FORMAT_R16G16B16A16_UINT;
                case ERHIPixelFormat.R16G16B16A16_SInt:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SINT;
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT;
                // 128-Bits
                case ERHIPixelFormat.R32G32B32A32_UInt:
                    return VkFormat.VK_FORMAT_R32G32B32A32_UINT;
                case ERHIPixelFormat.R32G32B32A32_SInt:
                    return VkFormat.VK_FORMAT_R32G32B32A32_SINT;
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return VkFormat.VK_FORMAT_R32G32B32A32_SFLOAT;
                // Depth-Stencil
                case ERHIPixelFormat.D16_UNorm:
                    return VkFormat.VK_FORMAT_D16_UNORM;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return VkFormat.VK_FORMAT_D24_UNORM_S8_UINT;
                case ERHIPixelFormat.D32_Float:
                    return VkFormat.VK_FORMAT_D32_SFLOAT;
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return VkFormat.VK_FORMAT_D32_SFLOAT_S8_UINT;
                // Block-Compressed
                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return VkFormat.VK_FORMAT_BC1_RGBA_SRGB_BLOCK;
                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return VkFormat.VK_FORMAT_BC1_RGB_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return VkFormat.VK_FORMAT_BC1_RGBA_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return VkFormat.VK_FORMAT_BC2_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return VkFormat.VK_FORMAT_BC2_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return VkFormat.VK_FORMAT_BC3_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return VkFormat.VK_FORMAT_BC3_UNORM_BLOCK;
                case ERHIPixelFormat.R_BC4_UNorm:
                    return VkFormat.VK_FORMAT_BC4_UNORM_BLOCK;
                case ERHIPixelFormat.R_BC4_SNorm:
                    return VkFormat.VK_FORMAT_BC4_SNORM_BLOCK;
                case ERHIPixelFormat.RG_BC5_UNorm:
                    return VkFormat.VK_FORMAT_BC5_UNORM_BLOCK;
                case ERHIPixelFormat.RG_BC5_SNorm:
                    return VkFormat.VK_FORMAT_BC5_SNORM_BLOCK;
                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return VkFormat.VK_FORMAT_BC6H_UFLOAT_BLOCK;
                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return VkFormat.VK_FORMAT_BC6H_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return VkFormat.VK_FORMAT_BC7_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return VkFormat.VK_FORMAT_BC7_UNORM_BLOCK;
                // ASTC
                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_4x4_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_4x4_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_4x4_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_5x5_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_5x5_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_5x5_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_6x6_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_6x6_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_6x6_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_8x8_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_8x8_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_8x8_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_10x10_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_10x10_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_10x10_SFLOAT_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                    return VkFormat.VK_FORMAT_ASTC_12x12_SRGB_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                    return VkFormat.VK_FORMAT_ASTC_12x12_UNORM_BLOCK;
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return VkFormat.VK_FORMAT_ASTC_12x12_SFLOAT_BLOCK;
                default:
                    return VkFormat.VK_FORMAT_UNDEFINED;
            }
        }

        public static VkFormat ConvertToVkSwapChainFormat(in ERHISwapChainFormat format)
        {
            switch (format)
            {
                case ERHISwapChainFormat.R8G8B8A8_UNorm:
                    return VkFormat.VK_FORMAT_B8G8R8A8_UNORM;
                case ERHISwapChainFormat.R10G10B10A2_UNorm:
                    return VkFormat.VK_FORMAT_A2B10G10R10_UNORM_PACK32;
                case ERHISwapChainFormat.R16G16B16A16_Float:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT;
                default:
                    return VkFormat.VK_FORMAT_B8G8R8A8_UNORM;
            }
        }

        public static VkFormat ConvertToVkVertexFormat(in ERHISemanticFormat format)
        {
            switch (format)
            {
                case ERHISemanticFormat.Float:
                    return VkFormat.VK_FORMAT_R32_SFLOAT;
                case ERHISemanticFormat.Float2:
                    return VkFormat.VK_FORMAT_R32G32_SFLOAT;
                case ERHISemanticFormat.Float3:
                    return VkFormat.VK_FORMAT_R32G32B32_SFLOAT;
                case ERHISemanticFormat.Float4:
                    return VkFormat.VK_FORMAT_R32G32B32A32_SFLOAT;
                case ERHISemanticFormat.Half:
                    return VkFormat.VK_FORMAT_R16_SFLOAT;
                case ERHISemanticFormat.Half2:
                    return VkFormat.VK_FORMAT_R16G16_SFLOAT;
                case ERHISemanticFormat.Half4:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SFLOAT;
                case ERHISemanticFormat.Int:
                    return VkFormat.VK_FORMAT_R32_SINT;
                case ERHISemanticFormat.Int2:
                    return VkFormat.VK_FORMAT_R32G32_SINT;
                case ERHISemanticFormat.Int3:
                    return VkFormat.VK_FORMAT_R32G32B32_SINT;
                case ERHISemanticFormat.Int4:
                    return VkFormat.VK_FORMAT_R32G32B32A32_SINT;
                case ERHISemanticFormat.UInt:
                    return VkFormat.VK_FORMAT_R32_UINT;
                case ERHISemanticFormat.UInt2:
                    return VkFormat.VK_FORMAT_R32G32_UINT;
                case ERHISemanticFormat.UInt3:
                    return VkFormat.VK_FORMAT_R32G32B32_UINT;
                case ERHISemanticFormat.UInt4:
                    return VkFormat.VK_FORMAT_R32G32B32A32_UINT;
                case ERHISemanticFormat.Short:
                    return VkFormat.VK_FORMAT_R16_SINT;
                case ERHISemanticFormat.Short2:
                    return VkFormat.VK_FORMAT_R16G16_SINT;
                case ERHISemanticFormat.Short4:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SINT;
                case ERHISemanticFormat.UShort:
                    return VkFormat.VK_FORMAT_R16_UINT;
                case ERHISemanticFormat.UShort2:
                    return VkFormat.VK_FORMAT_R16G16_UINT;
                case ERHISemanticFormat.UShort4:
                    return VkFormat.VK_FORMAT_R16G16B16A16_UINT;
                case ERHISemanticFormat.ShortNormalized:
                    return VkFormat.VK_FORMAT_R16_SNORM;
                case ERHISemanticFormat.Short2Normalized:
                    return VkFormat.VK_FORMAT_R16G16_SNORM;
                case ERHISemanticFormat.Short4Normalized:
                    return VkFormat.VK_FORMAT_R16G16B16A16_SNORM;
                case ERHISemanticFormat.UShortNormalized:
                    return VkFormat.VK_FORMAT_R16_UNORM;
                case ERHISemanticFormat.UShort2Normalized:
                    return VkFormat.VK_FORMAT_R16G16_UNORM;
                case ERHISemanticFormat.UShort4Normalized:
                    return VkFormat.VK_FORMAT_R16G16B16A16_UNORM;
                case ERHISemanticFormat.Byte:
                    return VkFormat.VK_FORMAT_R8_SINT;
                case ERHISemanticFormat.Byte2:
                    return VkFormat.VK_FORMAT_R8G8_SINT;
                case ERHISemanticFormat.Byte4:
                    return VkFormat.VK_FORMAT_R8G8B8A8_SINT;
                case ERHISemanticFormat.UByte:
                    return VkFormat.VK_FORMAT_R8_UINT;
                case ERHISemanticFormat.UByte2:
                    return VkFormat.VK_FORMAT_R8G8_UINT;
                case ERHISemanticFormat.UByte4:
                    return VkFormat.VK_FORMAT_R8G8B8A8_UINT;
                case ERHISemanticFormat.ByteNormalized:
                    return VkFormat.VK_FORMAT_R8_SNORM;
                case ERHISemanticFormat.Byte2Normalized:
                    return VkFormat.VK_FORMAT_R8G8_SNORM;
                case ERHISemanticFormat.Byte4Normalized:
                    return VkFormat.VK_FORMAT_R8G8B8A8_SNORM;
                case ERHISemanticFormat.UByteNormalized:
                    return VkFormat.VK_FORMAT_R8_UNORM;
                case ERHISemanticFormat.UByte2Normalized:
                    return VkFormat.VK_FORMAT_R8G8_UNORM;
                case ERHISemanticFormat.UByte4Normalized:
                    return VkFormat.VK_FORMAT_R8G8B8A8_UNORM;
                default:
                    return VkFormat.VK_FORMAT_UNDEFINED;
            }
        }

        public static VkIndexType ConvertToVkIndexType(in ERHIBufferFormat format)
        {
            switch (format)
            {
                case ERHIBufferFormat.UInt16:
                    return VkIndexType.VK_INDEX_TYPE_UINT16;
                case ERHIBufferFormat.UInt32:
                    return VkIndexType.VK_INDEX_TYPE_UINT32;
                default:
                    return VkIndexType.VK_INDEX_TYPE_UINT32;
            }
        }

        public static VkBufferUsageFlags ConvertToVkBufferUsage(in ERHIBufferUsage usage)
        {
            VkBufferUsageFlags result = 0;

            if ((usage & ERHIBufferUsage.CopySrc) == ERHIBufferUsage.CopySrc)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
            if ((usage & ERHIBufferUsage.CopyDst) == ERHIBufferUsage.CopyDst)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_TRANSFER_DST_BIT;
            if ((usage & ERHIBufferUsage.IndexBuffer) == ERHIBufferUsage.IndexBuffer)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.VertexBuffer) == ERHIBufferUsage.VertexBuffer)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_VERTEX_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.UniformBuffer) == ERHIBufferUsage.UniformBuffer)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.IndirectBuffer) == ERHIBufferUsage.IndirectBuffer)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.ShaderResource) == ERHIBufferUsage.ShaderResource)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.UnorderedAccess) == ERHIBufferUsage.UnorderedAccess)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
            if ((usage & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct)
                result |= VkBufferUsageFlags.VK_BUFFER_USAGE_ACCELERATION_STRUCTURE_STORAGE_BIT_KHR;

            return result;
        }

        public static VkImageUsageFlags ConvertToVkImageUsage(in ERHITextureUsage usage)
        {
            VkImageUsageFlags result = 0;

            if ((usage & ERHITextureUsage.CopySrc) == ERHITextureUsage.CopySrc)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
            if ((usage & ERHITextureUsage.CopyDst) == ERHITextureUsage.CopyDst)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_TRANSFER_DST_BIT;
            if ((usage & ERHITextureUsage.DepthStencil) == ERHITextureUsage.DepthStencil)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT;
            if ((usage & ERHITextureUsage.RenderTarget) == ERHITextureUsage.RenderTarget)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
            if ((usage & ERHITextureUsage.ShaderResource) == ERHITextureUsage.ShaderResource)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_SAMPLED_BIT;
            if ((usage & ERHITextureUsage.UnorderedAccess) == ERHITextureUsage.UnorderedAccess)
                result |= VkImageUsageFlags.VK_IMAGE_USAGE_STORAGE_BIT;

            return result;
        }

        public static VkImageType ConvertToVkImageType(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture3D:
                    return VkImageType.VK_IMAGE_TYPE_3D;
                default:
                    return VkImageType.VK_IMAGE_TYPE_2D;
            }
        }

        public static VkImageViewType ConvertToVkImageViewType(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2D:
                case ERHITextureDimension.Texture2DMS:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.Texture2DArrayMS:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_2D_ARRAY;
                case ERHITextureDimension.TextureCube:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_CUBE;
                case ERHITextureDimension.TextureCubeArray:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_CUBE_ARRAY;
                case ERHITextureDimension.Texture3D:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_3D;
                default:
                    return VkImageViewType.VK_IMAGE_VIEW_TYPE_2D;
            }
        }

        public static VkSampleCountFlags ConvertToVkSampleCount(in ERHISampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case ERHISampleCount.None:
                    return VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT;
                case ERHISampleCount.Count2:
                    return VkSampleCountFlags.VK_SAMPLE_COUNT_2_BIT;
                case ERHISampleCount.Count4:
                    return VkSampleCountFlags.VK_SAMPLE_COUNT_4_BIT;
                case ERHISampleCount.Count8:
                    return VkSampleCountFlags.VK_SAMPLE_COUNT_8_BIT;
                default:
                    return VkSampleCountFlags.VK_SAMPLE_COUNT_1_BIT;
            }
        }

        public static VkImageCreateFlags ConvertToVkImageCreateFlags(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    return VkImageCreateFlags.VK_IMAGE_CREATE_CUBE_COMPATIBLE_BIT;
                default:
                    return 0;
            }
        }

        public static uint GetArrayLayers(in ERHITextureDimension dimension, uint extent)
        {
            switch (dimension)
            {
                case ERHITextureDimension.TextureCube:
                    return 6;
                case ERHITextureDimension.TextureCubeArray:
                    return 6 * extent;
                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.Texture2DArrayMS:
                    return extent;
                default:
                    return 1;
            }
        }

        public static VkImageAspectFlags GetVkImageAspect(in ERHIPixelFormat format)
        {
            switch (format)
            {
                case ERHIPixelFormat.D16_UNorm:
                case ERHIPixelFormat.D32_Float:
                    return VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return VkImageAspectFlags.VK_IMAGE_ASPECT_DEPTH_BIT | VkImageAspectFlags.VK_IMAGE_ASPECT_STENCIL_BIT;
                default:
                    return VkImageAspectFlags.VK_IMAGE_ASPECT_COLOR_BIT;
            }
        }

        public static VkMemoryPropertyFlags ConvertToVkMemoryProperty(in ERHIStorageMode storageMode)
        {
            switch (storageMode)
            {
                case ERHIStorageMode.GPULocal:
                    return VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
                case ERHIStorageMode.Readback:
                    return VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_CACHED_BIT;
                case ERHIStorageMode.GPUUpload:
                case ERHIStorageMode.HostUpload:
                    return VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_HOST_COHERENT_BIT;
                default:
                    return VkMemoryPropertyFlags.VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT;
            }
        }

        public static VkPipelineStageFlags ConvertToVkPipelineStage(in ERHIPipelineStage stage)
        {
            switch (stage)
            {
                case ERHIPipelineStage.Common:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
                case ERHIPipelineStage.Vertex:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_VERTEX_SHADER_BIT;
                case ERHIPipelineStage.Fragment:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
                case ERHIPipelineStage.Compute:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
                case ERHIPipelineStage.Task:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_TASK_SHADER_BIT_EXT;
                case ERHIPipelineStage.Mesh:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_MESH_SHADER_BIT_EXT;
                case ERHIPipelineStage.RayTracing:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_RAY_TRACING_SHADER_BIT_KHR;
                default:
                    return VkPipelineStageFlags.VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
            }
        }

        public static VkAccessFlags ConvertToVkBufferAccessFlag(in ERHIBufferState state)
        {
            VkAccessFlags result = 0;

            if ((state & ERHIBufferState.CopySrc) != 0)
                result |= VkAccessFlags.VK_ACCESS_TRANSFER_READ_BIT;
            if ((state & ERHIBufferState.CopyDst) != 0)
                result |= VkAccessFlags.VK_ACCESS_TRANSFER_WRITE_BIT;
            if ((state & ERHIBufferState.IndexBuffer) != 0)
                result |= VkAccessFlags.VK_ACCESS_INDEX_READ_BIT;
            if ((state & ERHIBufferState.VertexBuffer) != 0)
                result |= VkAccessFlags.VK_ACCESS_VERTEX_ATTRIBUTE_READ_BIT;
            if ((state & ERHIBufferState.ConstantBuffer) != 0)
                result |= VkAccessFlags.VK_ACCESS_UNIFORM_READ_BIT;
            if ((state & ERHIBufferState.IndirectArgument) != 0)
                result |= VkAccessFlags.VK_ACCESS_INDIRECT_COMMAND_READ_BIT;
            if ((state & ERHIBufferState.ShaderResource) != 0)
                result |= VkAccessFlags.VK_ACCESS_SHADER_READ_BIT;
            if ((state & ERHIBufferState.UnorderedAccess) != 0)
                result |= VkAccessFlags.VK_ACCESS_SHADER_READ_BIT | VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT;
            if ((state & ERHIBufferState.AccelStructRead) != 0)
                result |= VkAccessFlags.VK_ACCESS_ACCELERATION_STRUCTURE_READ_BIT_KHR;
            if ((state & ERHIBufferState.AccelStructWrite) != 0)
                result |= VkAccessFlags.VK_ACCESS_ACCELERATION_STRUCTURE_WRITE_BIT_KHR;

            return result;
        }

        public static VkAccessFlags ConvertToVkTextureAccessFlag(in ERHITextureState state)
        {
            VkAccessFlags result = 0;

            if ((state & ERHITextureState.CopySrc) != 0)
                result |= VkAccessFlags.VK_ACCESS_TRANSFER_READ_BIT;
            if ((state & ERHITextureState.CopyDst) != 0)
                result |= VkAccessFlags.VK_ACCESS_TRANSFER_WRITE_BIT;
            if ((state & ERHITextureState.DepthRead) != 0)
                result |= VkAccessFlags.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT;
            if ((state & ERHITextureState.DepthWrite) != 0)
                result |= VkAccessFlags.VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
            if ((state & ERHITextureState.RenderTarget) != 0)
                result |= VkAccessFlags.VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
            if ((state & ERHITextureState.ShaderResource) != 0)
                result |= VkAccessFlags.VK_ACCESS_SHADER_READ_BIT;
            if ((state & ERHITextureState.UnorderedAccess) != 0)
                result |= VkAccessFlags.VK_ACCESS_SHADER_READ_BIT | VkAccessFlags.VK_ACCESS_SHADER_WRITE_BIT;
            if ((state & ERHITextureState.Present) != 0)
                result |= VkAccessFlags.VK_ACCESS_MEMORY_READ_BIT;

            return result;
        }

        public static VkImageLayout ConvertToVkImageLayout(in ERHITextureState state)
        {
            if ((state & ERHITextureState.Present) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
            if ((state & ERHITextureState.RenderTarget) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
            if ((state & ERHITextureState.DepthWrite) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
            if ((state & ERHITextureState.DepthRead) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_DEPTH_STENCIL_READ_ONLY_OPTIMAL;
            if ((state & ERHITextureState.UnorderedAccess) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_GENERAL;
            if ((state & ERHITextureState.ShaderResource) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            if ((state & ERHITextureState.CopySrc) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
            if ((state & ERHITextureState.CopyDst) != 0)
                return VkImageLayout.VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;

            return VkImageLayout.VK_IMAGE_LAYOUT_UNDEFINED;
        }

        public static VkFilter ConvertToVkFilter(in ERHIFilterMode filter)
        {
            switch (filter)
            {
                case ERHIFilterMode.Point:
                    return VkFilter.VK_FILTER_NEAREST;
                case ERHIFilterMode.Linear:
                case ERHIFilterMode.Anisotropic:
                    return VkFilter.VK_FILTER_LINEAR;
                default:
                    return VkFilter.VK_FILTER_NEAREST;
            }
        }

        public static VkSamplerMipmapMode ConvertToVkMipmapMode(in ERHIFilterMode filter)
        {
            switch (filter)
            {
                case ERHIFilterMode.Point:
                    return VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_NEAREST;
                case ERHIFilterMode.Linear:
                case ERHIFilterMode.Anisotropic:
                    return VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_LINEAR;
                default:
                    return VkSamplerMipmapMode.VK_SAMPLER_MIPMAP_MODE_NEAREST;
            }
        }

        public static VkSamplerAddressMode ConvertToVkAddressMode(in ERHIAddressMode mode)
        {
            switch (mode)
            {
                case ERHIAddressMode.Repeat:
                    return VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT;
                case ERHIAddressMode.ClampToEdge:
                    return VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
                case ERHIAddressMode.MirrorRepeat:
                    return VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_MIRRORED_REPEAT;
                default:
                    return VkSamplerAddressMode.VK_SAMPLER_ADDRESS_MODE_REPEAT;
            }
        }

        public static VkCompareOp ConvertToVkCompareOp(in ERHIComparisonMode mode)
        {
            switch (mode)
            {
                case ERHIComparisonMode.Never:
                    return VkCompareOp.VK_COMPARE_OP_NEVER;
                case ERHIComparisonMode.Less:
                    return VkCompareOp.VK_COMPARE_OP_LESS;
                case ERHIComparisonMode.Equal:
                    return VkCompareOp.VK_COMPARE_OP_EQUAL;
                case ERHIComparisonMode.LessEqual:
                    return VkCompareOp.VK_COMPARE_OP_LESS_OR_EQUAL;
                case ERHIComparisonMode.Greater:
                    return VkCompareOp.VK_COMPARE_OP_GREATER;
                case ERHIComparisonMode.NotEqual:
                    return VkCompareOp.VK_COMPARE_OP_NOT_EQUAL;
                case ERHIComparisonMode.GreaterEqual:
                    return VkCompareOp.VK_COMPARE_OP_GREATER_OR_EQUAL;
                case ERHIComparisonMode.Always:
                    return VkCompareOp.VK_COMPARE_OP_ALWAYS;
                default:
                    return VkCompareOp.VK_COMPARE_OP_NEVER;
            }
        }

        public static VkStencilOp ConvertToVkStencilOp(in ERHIStencilOp op)
        {
            switch (op)
            {
                case ERHIStencilOp.Keep:
                    return VkStencilOp.VK_STENCIL_OP_KEEP;
                case ERHIStencilOp.Zero:
                    return VkStencilOp.VK_STENCIL_OP_ZERO;
                case ERHIStencilOp.Replace:
                    return VkStencilOp.VK_STENCIL_OP_REPLACE;
                case ERHIStencilOp.IncrementSaturation:
                    return VkStencilOp.VK_STENCIL_OP_INCREMENT_AND_CLAMP;
                case ERHIStencilOp.DecrementSaturation:
                    return VkStencilOp.VK_STENCIL_OP_DECREMENT_AND_CLAMP;
                case ERHIStencilOp.Invert:
                    return VkStencilOp.VK_STENCIL_OP_INVERT;
                case ERHIStencilOp.Increment:
                    return VkStencilOp.VK_STENCIL_OP_INCREMENT_AND_WRAP;
                case ERHIStencilOp.Decrement:
                    return VkStencilOp.VK_STENCIL_OP_DECREMENT_AND_WRAP;
                default:
                    return VkStencilOp.VK_STENCIL_OP_KEEP;
            }
        }

        public static VkBlendOp ConvertToVkBlendOp(in ERHIBlendOp op)
        {
            switch (op)
            {
                case ERHIBlendOp.Add:
                    return VkBlendOp.VK_BLEND_OP_ADD;
                case ERHIBlendOp.Substract:
                    return VkBlendOp.VK_BLEND_OP_SUBTRACT;
                case ERHIBlendOp.ReverseSubstract:
                    return VkBlendOp.VK_BLEND_OP_REVERSE_SUBTRACT;
                case ERHIBlendOp.Min:
                    return VkBlendOp.VK_BLEND_OP_MIN;
                case ERHIBlendOp.Max:
                    return VkBlendOp.VK_BLEND_OP_MAX;
                default:
                    return VkBlendOp.VK_BLEND_OP_ADD;
            }
        }

        public static VkBlendFactor ConvertToVkBlendFactor(in ERHIBlendMode mode)
        {
            switch (mode)
            {
                case ERHIBlendMode.Zero:
                    return VkBlendFactor.VK_BLEND_FACTOR_ZERO;
                case ERHIBlendMode.One:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE;
                case ERHIBlendMode.SrcColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_SRC_COLOR;
                case ERHIBlendMode.OneMinusSrcColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC_COLOR;
                case ERHIBlendMode.SrcAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_SRC_ALPHA;
                case ERHIBlendMode.OneMinusSrcAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
                case ERHIBlendMode.DstColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_DST_COLOR;
                case ERHIBlendMode.OneMinusDstColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_DST_COLOR;
                case ERHIBlendMode.DstAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_DST_ALPHA;
                case ERHIBlendMode.OneMinusDstAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_DST_ALPHA;
                case ERHIBlendMode.SrcAlphaSaturate:
                    return VkBlendFactor.VK_BLEND_FACTOR_SRC_ALPHA_SATURATE;
                case ERHIBlendMode.BlendFactor:
                    return VkBlendFactor.VK_BLEND_FACTOR_CONSTANT_COLOR;
                case ERHIBlendMode.InverseBlendFactor:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_CONSTANT_COLOR;
                case ERHIBlendMode.SecondarySourceColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_SRC1_COLOR;
                case ERHIBlendMode.InverseSecondarySourceColor:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC1_COLOR;
                case ERHIBlendMode.SecondarySourceAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_SRC1_ALPHA;
                case ERHIBlendMode.InverseSecondarySourceAlpha:
                    return VkBlendFactor.VK_BLEND_FACTOR_ONE_MINUS_SRC1_ALPHA;
                default:
                    return VkBlendFactor.VK_BLEND_FACTOR_ZERO;
            }
        }

        public static VkColorComponentFlags ConvertToVkColorWriteMask(in ERHIColorWriteChannel channel)
        {
            VkColorComponentFlags result = 0;
            if ((channel & ERHIColorWriteChannel.Red) != 0) result |= VkColorComponentFlags.VK_COLOR_COMPONENT_R_BIT;
            if ((channel & ERHIColorWriteChannel.Green) != 0) result |= VkColorComponentFlags.VK_COLOR_COMPONENT_G_BIT;
            if ((channel & ERHIColorWriteChannel.Blue) != 0) result |= VkColorComponentFlags.VK_COLOR_COMPONENT_B_BIT;
            if ((channel & ERHIColorWriteChannel.Alpha) != 0) result |= VkColorComponentFlags.VK_COLOR_COMPONENT_A_BIT;
            return result;
        }

        public static VkPolygonMode ConvertToVkPolygonMode(in ERHIFillMode fillMode)
        {
            switch (fillMode)
            {
                case ERHIFillMode.Solid:
                    return VkPolygonMode.VK_POLYGON_MODE_FILL;
                case ERHIFillMode.Wireframe:
                    return VkPolygonMode.VK_POLYGON_MODE_LINE;
                default:
                    return VkPolygonMode.VK_POLYGON_MODE_FILL;
            }
        }

        public static VkCullModeFlags ConvertToVkCullMode(in ERHICullMode cullMode)
        {
            switch (cullMode)
            {
                case ERHICullMode.None:
                    return VkCullModeFlags.VK_CULL_MODE_NONE;
                case ERHICullMode.Back:
                    return VkCullModeFlags.VK_CULL_MODE_BACK_BIT;
                case ERHICullMode.Front:
                    return VkCullModeFlags.VK_CULL_MODE_FRONT_BIT;
                default:
                    return VkCullModeFlags.VK_CULL_MODE_NONE;
            }
        }

        public static VkFrontFace ConvertToVkFrontFace(bool frontCounterClockwise)
        {
            return frontCounterClockwise ? VkFrontFace.VK_FRONT_FACE_COUNTER_CLOCKWISE : VkFrontFace.VK_FRONT_FACE_CLOCKWISE;
        }

        public static VkPrimitiveTopology ConvertToVkPrimitiveTopology(in ERHIPrimitiveTopology topology)
        {
            switch (topology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_POINT_LIST;
                case ERHIPrimitiveTopology.LineList:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_LINE_LIST;
                case ERHIPrimitiveTopology.LineStrip:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_LINE_STRIP;
                case ERHIPrimitiveTopology.TriangleList:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
                case ERHIPrimitiveTopology.TriangleStrip:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP;
                case ERHIPrimitiveTopology.LineListAdj:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_LINE_LIST_WITH_ADJACENCY;
                case ERHIPrimitiveTopology.LineStripAdj:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_LINE_STRIP_WITH_ADJACENCY;
                case ERHIPrimitiveTopology.TriangleListAdj:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST_WITH_ADJACENCY;
                case ERHIPrimitiveTopology.TriangleStripAdj:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_STRIP_WITH_ADJACENCY;
                default:
                    return VkPrimitiveTopology.VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
            }
        }

        public static VkAttachmentLoadOp ConvertToVkLoadOp(in ERHILoadAction action)
        {
            switch (action)
            {
                case ERHILoadAction.Load:
                    return VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_LOAD;
                case ERHILoadAction.Clear:
                    return VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_CLEAR;
                case ERHILoadAction.DontCare:
                    return VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
                default:
                    return VkAttachmentLoadOp.VK_ATTACHMENT_LOAD_OP_DONT_CARE;
            }
        }

        public static VkAttachmentStoreOp ConvertToVkStoreOp(in ERHIStoreAction action)
        {
            switch (action)
            {
                case ERHIStoreAction.Store:
                case ERHIStoreAction.Resolve:
                case ERHIStoreAction.StoreAndResolve:
                    return VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_STORE;
                case ERHIStoreAction.DontCare:
                    return VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
                default:
                    return VkAttachmentStoreOp.VK_ATTACHMENT_STORE_OP_DONT_CARE;
            }
        }

        public static VkPresentModeKHR ConvertToVkPresentMode(in ERHIPresentMode mode)
        {
            switch (mode)
            {
                case ERHIPresentMode.VSync:
                    return VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR;
                case ERHIPresentMode.Immediately:
                    return VkPresentModeKHR.VK_PRESENT_MODE_IMMEDIATE_KHR;
                default:
                    return VkPresentModeKHR.VK_PRESENT_MODE_FIFO_KHR;
            }
        }

        public static VkDescriptorType ConvertToVkDescriptorType(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Sampler:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLER;
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
                case ERHIBindType.UniformBuffer:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
                case ERHIBindType.AccelStruct:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR;
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE;
                default:
                    return VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            }
        }

        public static VkShaderStageFlags ConvertToVkShaderStage(in ERHIShaderStage stage)
        {
            VkShaderStageFlags result = 0;

            if ((stage & ERHIShaderStage.All) == ERHIShaderStage.All)
                return VkShaderStageFlags.VK_SHADER_STAGE_ALL;
            if ((stage & ERHIShaderStage.AllGraphics) == ERHIShaderStage.AllGraphics)
                return VkShaderStageFlags.VK_SHADER_STAGE_ALL_GRAPHICS;

            if ((stage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_VERTEX_BIT;
            if ((stage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_FRAGMENT_BIT;
            if ((stage & ERHIShaderStage.Compute) == ERHIShaderStage.Compute)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_COMPUTE_BIT;
            if ((stage & ERHIShaderStage.Task) == ERHIShaderStage.Task)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_TASK_BIT_EXT;
            if ((stage & ERHIShaderStage.Mesh) == ERHIShaderStage.Mesh)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_MESH_BIT_EXT;
            if ((stage & ERHIShaderStage.RayTracing) == ERHIShaderStage.RayTracing)
                result |= VkShaderStageFlags.VK_SHADER_STAGE_RAYGEN_BIT_KHR | VkShaderStageFlags.VK_SHADER_STAGE_MISS_BIT_KHR | VkShaderStageFlags.VK_SHADER_STAGE_CLOSEST_HIT_BIT_KHR | VkShaderStageFlags.VK_SHADER_STAGE_ANY_HIT_BIT_KHR | VkShaderStageFlags.VK_SHADER_STAGE_INTERSECTION_BIT_KHR;

            return result == 0 ? VkShaderStageFlags.VK_SHADER_STAGE_ALL : result;
        }

        public static VkQueryType ConvertToVkQueryType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return VkQueryType.VK_QUERY_TYPE_OCCLUSION;
                case ERHIQueryType.Statistics:
                    return VkQueryType.VK_QUERY_TYPE_PIPELINE_STATISTICS;
                case ERHIQueryType.TimestampTransfer:
                case ERHIQueryType.TimestampGenerice:
                    return VkQueryType.VK_QUERY_TYPE_TIMESTAMP;
                default:
                    return VkQueryType.VK_QUERY_TYPE_TIMESTAMP;
            }
        }

        public static VkShaderStageFlagBits ConvertToVkShaderStageBit(in ERHIFunctionType type)
        {
            switch (type)
            {
                case ERHIFunctionType.Vertex:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_VERTEX_BIT;
                case ERHIFunctionType.Fragment:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_FRAGMENT_BIT;
                case ERHIFunctionType.Compute:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_COMPUTE_BIT;
                case ERHIFunctionType.Task:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_TASK_BIT_EXT;
                case ERHIFunctionType.Mesh:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_MESH_BIT_EXT;
                case ERHIFunctionType.RayTracing:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_RAYGEN_BIT_KHR;
                default:
                    return VkShaderStageFlagBits.VK_SHADER_STAGE_ALL;
            }
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
