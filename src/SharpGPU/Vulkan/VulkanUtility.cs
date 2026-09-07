using System;
using System.Text;
using SharpGPU.Core;
using Vortice.Vulkan;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// TODO: follow-up — split VulkanUtility by domain; left intact in layout convergence.

namespace SharpGPU
{
#pragma warning disable CA1416
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
            if (OperatingSystem.IsAndroid() || RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID")))
            {
                return EOSPlatform.Android;
            }

            if (OperatingSystem.IsIOS() || RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")))
            {
                return EOSPlatform.iOS;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return EOSPlatform.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return EOSPlatform.MacOS;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return EOSPlatform.Linux;
            }

            throw new PlatformNotSupportedException($"Unsupported OS platform: '{RuntimeInformation.OSDescription}'.");
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
            return memoryProperties.memoryTypes[(int)index];
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

        public static void CheckErrors(VkResult result)
        {
            if (result == VkResult.Success)
            {
                return;
            }

            ERHIErrorCode errorCode = result switch
            {
                VkResult.ErrorOutOfHostMemory or VkResult.ErrorOutOfDeviceMemory => ERHIErrorCode.OutOfMemory,
                VkResult.ErrorDeviceLost => ERHIErrorCode.DeviceLost,
                VkResult.ErrorSurfaceLostKHR => ERHIErrorCode.SurfaceLost,
                _ => ERHIErrorCode.NativeFailure
            };
            ERHIDeviceState deviceState = result == VkResult.ErrorDeviceLost
                ? ERHIDeviceState.Lost
                : ERHIDeviceState.Operational;
            throw new RHIException(
                errorCode,
                ERHIBackend.Vulkan,
                (int)result,
                result.ToString(),
                deviceState);
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
                    return VkFormat.R8Uint;
                case ERHIPixelFormat.R8_SInt:
                    return VkFormat.R8Sint;
                case ERHIPixelFormat.R8_UNorm:
                    return VkFormat.R8Unorm;
                case ERHIPixelFormat.R8_SNorm:
                    return VkFormat.R8Snorm;
                // 16-Bits
                case ERHIPixelFormat.R16_UInt:
                    return VkFormat.R16Uint;
                case ERHIPixelFormat.R16_SInt:
                    return VkFormat.R16Sint;
                case ERHIPixelFormat.R16_Float:
                    return VkFormat.R16Sfloat;
                case ERHIPixelFormat.R8G8_UInt:
                    return VkFormat.R8G8Uint;
                case ERHIPixelFormat.R8G8_SInt:
                    return VkFormat.R8G8Sint;
                case ERHIPixelFormat.R8G8_UNorm:
                    return VkFormat.R8G8Unorm;
                case ERHIPixelFormat.R8G8_SNorm:
                    return VkFormat.R8G8Snorm;
                // 32-Bits
                case ERHIPixelFormat.R32_UInt:
                    return VkFormat.R32Uint;
                case ERHIPixelFormat.R32_SInt:
                    return VkFormat.R32Sint;
                case ERHIPixelFormat.R32_Float:
                    return VkFormat.R32Sfloat;
                case ERHIPixelFormat.R16G16_UInt:
                    return VkFormat.R16G16Uint;
                case ERHIPixelFormat.R16G16_SInt:
                    return VkFormat.R16G16Sint;
                case ERHIPixelFormat.R16G16_Float:
                    return VkFormat.R16G16Sfloat;
                case ERHIPixelFormat.R8G8B8A8_UInt:
                    return VkFormat.R8G8B8A8Uint;
                case ERHIPixelFormat.R8G8B8A8_SInt:
                    return VkFormat.R8G8B8A8Sint;
                case ERHIPixelFormat.R8G8B8A8_UNorm:
                    return VkFormat.R8G8B8A8Unorm;
                case ERHIPixelFormat.R8G8B8A8_UNorm_Srgb:
                    return VkFormat.R8G8B8A8Srgb;
                case ERHIPixelFormat.R8G8B8A8_SNorm:
                    return VkFormat.R8G8B8A8Snorm;
                case ERHIPixelFormat.B8G8R8A8_UNorm:
                    return VkFormat.B8G8R8A8Unorm;
                case ERHIPixelFormat.B8G8R8A8_UNorm_Srgb:
                    return VkFormat.B8G8R8A8Srgb;
                case ERHIPixelFormat.R99GB99_E5_Float:
                    return VkFormat.E5B9G9R9UfloatPack32;
                case ERHIPixelFormat.R10G10B10A2_UInt:
                    return VkFormat.A2B10G10R10UintPack32;
                case ERHIPixelFormat.R10G10B10A2_UNorm:
                    return VkFormat.A2B10G10R10UnormPack32;
                case ERHIPixelFormat.R11G11B10_Float:
                    return VkFormat.B10G11R11UfloatPack32;
                // 64-Bits
                case ERHIPixelFormat.RG32_UInt:
                    return VkFormat.R32G32Uint;
                case ERHIPixelFormat.RG32_SInt:
                    return VkFormat.R32G32Sint;
                case ERHIPixelFormat.RG32_Float:
                    return VkFormat.R32G32Sfloat;
                case ERHIPixelFormat.R16G16B16A16_UInt:
                    return VkFormat.R16G16B16A16Uint;
                case ERHIPixelFormat.R16G16B16A16_SInt:
                    return VkFormat.R16G16B16A16Sint;
                case ERHIPixelFormat.R16G16B16A16_Float:
                    return VkFormat.R16G16B16A16Sfloat;
                // 128-Bits
                case ERHIPixelFormat.R32G32B32A32_UInt:
                    return VkFormat.R32G32B32A32Uint;
                case ERHIPixelFormat.R32G32B32A32_SInt:
                    return VkFormat.R32G32B32A32Sint;
                case ERHIPixelFormat.R32G32B32A32_Float:
                    return VkFormat.R32G32B32A32Sfloat;
                // Depth-Stencil
                case ERHIPixelFormat.D16_UNorm:
                    return VkFormat.D16Unorm;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                    return VkFormat.D24UnormS8Uint;
                case ERHIPixelFormat.D32_Float:
                    return VkFormat.D32Sfloat;
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return VkFormat.D32SfloatS8Uint;
                // Block-Compressed
                case ERHIPixelFormat.RGBA_DXT1_SRGB:
                    return VkFormat.Bc1RgbaSrgbBlock;
                case ERHIPixelFormat.RGB_DXT1_UNorm:
                    return VkFormat.Bc1RgbUnormBlock;
                case ERHIPixelFormat.RGBA_DXT1_UNorm:
                    return VkFormat.Bc1RgbaUnormBlock;
                case ERHIPixelFormat.RGBA_DXT3_SRGB:
                    return VkFormat.Bc2SrgbBlock;
                case ERHIPixelFormat.RGBA_DXT3_UNorm:
                    return VkFormat.Bc2UnormBlock;
                case ERHIPixelFormat.RGBA_DXT5_SRGB:
                    return VkFormat.Bc3SrgbBlock;
                case ERHIPixelFormat.RGBA_DXT5_UNorm:
                    return VkFormat.Bc3UnormBlock;
                case ERHIPixelFormat.R_BC4_UNorm:
                    return VkFormat.Bc4UnormBlock;
                case ERHIPixelFormat.R_BC4_SNorm:
                    return VkFormat.Bc4SnormBlock;
                case ERHIPixelFormat.RG_BC5_UNorm:
                    return VkFormat.Bc5UnormBlock;
                case ERHIPixelFormat.RG_BC5_SNorm:
                    return VkFormat.Bc5SnormBlock;
                case ERHIPixelFormat.RGB_BC6H_UFloat:
                    return VkFormat.Bc6hUfloatBlock;
                case ERHIPixelFormat.RGB_BC6H_SFloat:
                    return VkFormat.Bc6hSfloatBlock;
                case ERHIPixelFormat.RGBA_BC7_SRGB:
                    return VkFormat.Bc7SrgbBlock;
                case ERHIPixelFormat.RGBA_BC7_UNorm:
                    return VkFormat.Bc7UnormBlock;
                // ASTC
                case ERHIPixelFormat.RGBA_ASTC4X4_SRGB:
                    return VkFormat.Astc4x4SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC4X4_UNorm:
                    return VkFormat.Astc4x4UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC4X4_UFloat:
                    return VkFormat.Astc4x4SfloatBlock;
                case ERHIPixelFormat.RGBA_ASTC5X5_SRGB:
                    return VkFormat.Astc5x5SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC5X5_UNorm:
                    return VkFormat.Astc5x5UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC5X5_UFloat:
                    return VkFormat.Astc5x5SfloatBlock;
                case ERHIPixelFormat.RGBA_ASTC6X6_SRGB:
                    return VkFormat.Astc6x6SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC6X6_UNorm:
                    return VkFormat.Astc6x6UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC6X6_UFloat:
                    return VkFormat.Astc6x6SfloatBlock;
                case ERHIPixelFormat.RGBA_ASTC8X8_SRGB:
                    return VkFormat.Astc8x8SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC8X8_UNorm:
                    return VkFormat.Astc8x8UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC8X8_UFloat:
                    return VkFormat.Astc8x8SfloatBlock;
                case ERHIPixelFormat.RGBA_ASTC10X10_SRGB:
                    return VkFormat.Astc10x10SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC10X10_UNorm:
                    return VkFormat.Astc10x10UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC10X10_UFloat:
                    return VkFormat.Astc10x10SfloatBlock;
                case ERHIPixelFormat.RGBA_ASTC12X12_SRGB:
                    return VkFormat.Astc12x12SrgbBlock;
                case ERHIPixelFormat.RGBA_ASTC12X12_UNorm:
                    return VkFormat.Astc12x12UnormBlock;
                case ERHIPixelFormat.RGBA_ASTC12X12_UFloat:
                    return VkFormat.Astc12x12SfloatBlock;
                default:
                    return VkFormat.Undefined;
            }
        }

        public static VkFormat ConvertToVkSwapChainFormat(in ERHISwapChainFormat format)
        {
            switch (format)
            {
                case ERHISwapChainFormat.R8G8B8A8_UNorm:
                    return VkFormat.R8G8B8A8Unorm;
                case ERHISwapChainFormat.R10G10B10A2_UNorm:
                    return VkFormat.A2B10G10R10UnormPack32;
                case ERHISwapChainFormat.R16G16B16A16_Float:
                    return VkFormat.R16G16B16A16Sfloat;
                default:
                    throw new NotSupportedException(
                        $"Vulkan swapchain format '{format}' is not supported.");
            }
        }

        public static VkFormat ConvertToVkVertexFormat(in ERHISemanticFormat format)
        {
            switch (format)
            {
                case ERHISemanticFormat.Float:
                    return VkFormat.R32Sfloat;
                case ERHISemanticFormat.Float2:
                    return VkFormat.R32G32Sfloat;
                case ERHISemanticFormat.Float3:
                    return VkFormat.R32G32B32Sfloat;
                case ERHISemanticFormat.Float4:
                    return VkFormat.R32G32B32A32Sfloat;
                case ERHISemanticFormat.Half:
                    return VkFormat.R16Sfloat;
                case ERHISemanticFormat.Half2:
                    return VkFormat.R16G16Sfloat;
                case ERHISemanticFormat.Half4:
                    return VkFormat.R16G16B16A16Sfloat;
                case ERHISemanticFormat.Int:
                    return VkFormat.R32Sint;
                case ERHISemanticFormat.Int2:
                    return VkFormat.R32G32Sint;
                case ERHISemanticFormat.Int3:
                    return VkFormat.R32G32B32Sint;
                case ERHISemanticFormat.Int4:
                    return VkFormat.R32G32B32A32Sint;
                case ERHISemanticFormat.UInt:
                    return VkFormat.R32Uint;
                case ERHISemanticFormat.UInt2:
                    return VkFormat.R32G32Uint;
                case ERHISemanticFormat.UInt3:
                    return VkFormat.R32G32B32Uint;
                case ERHISemanticFormat.UInt4:
                    return VkFormat.R32G32B32A32Uint;
                case ERHISemanticFormat.Short:
                    return VkFormat.R16Sint;
                case ERHISemanticFormat.Short2:
                    return VkFormat.R16G16Sint;
                case ERHISemanticFormat.Short4:
                    return VkFormat.R16G16B16A16Sint;
                case ERHISemanticFormat.UShort:
                    return VkFormat.R16Uint;
                case ERHISemanticFormat.UShort2:
                    return VkFormat.R16G16Uint;
                case ERHISemanticFormat.UShort4:
                    return VkFormat.R16G16B16A16Uint;
                case ERHISemanticFormat.ShortNormalized:
                    return VkFormat.R16Snorm;
                case ERHISemanticFormat.Short2Normalized:
                    return VkFormat.R16G16Snorm;
                case ERHISemanticFormat.Short4Normalized:
                    return VkFormat.R16G16B16A16Snorm;
                case ERHISemanticFormat.UShortNormalized:
                    return VkFormat.R16Unorm;
                case ERHISemanticFormat.UShort2Normalized:
                    return VkFormat.R16G16Unorm;
                case ERHISemanticFormat.UShort4Normalized:
                    return VkFormat.R16G16B16A16Unorm;
                case ERHISemanticFormat.Byte:
                    return VkFormat.R8Sint;
                case ERHISemanticFormat.Byte2:
                    return VkFormat.R8G8Sint;
                case ERHISemanticFormat.Byte4:
                    return VkFormat.R8G8B8A8Sint;
                case ERHISemanticFormat.UByte:
                    return VkFormat.R8Uint;
                case ERHISemanticFormat.UByte2:
                    return VkFormat.R8G8Uint;
                case ERHISemanticFormat.UByte4:
                    return VkFormat.R8G8B8A8Uint;
                case ERHISemanticFormat.ByteNormalized:
                    return VkFormat.R8Snorm;
                case ERHISemanticFormat.Byte2Normalized:
                    return VkFormat.R8G8Snorm;
                case ERHISemanticFormat.Byte4Normalized:
                    return VkFormat.R8G8B8A8Snorm;
                case ERHISemanticFormat.UByteNormalized:
                    return VkFormat.R8Unorm;
                case ERHISemanticFormat.UByte2Normalized:
                    return VkFormat.R8G8Unorm;
                case ERHISemanticFormat.UByte4Normalized:
                    return VkFormat.R8G8B8A8Unorm;
                default:
                    return VkFormat.Undefined;
            }
        }

        public static VkIndexType ConvertToVkIndexType(in ERHIBufferFormat format)
        {
            switch (format)
            {
                case ERHIBufferFormat.UInt16:
                    return VkIndexType.Uint16;
                case ERHIBufferFormat.UInt32:
                    return VkIndexType.Uint32;
                default:
                    return VkIndexType.Uint32;
            }
        }

        public static VkFormat ConvertToVkAccelerationStructureVertexFormat(in ERHIPixelFormat format)
        {
            VkFormat vkFormat = ConvertToVkFormat(format);
            if (vkFormat == VkFormat.R32G32B32A32Sfloat)
            {
                // RT triangle geometry consumes xyz; keep float4 layout by using xyz format + explicit stride.
                return VkFormat.R32G32B32Sfloat;
            }

            return vkFormat;
        }

        public static VkBufferUsageFlags ConvertToVkBufferUsage(in ERHIBufferUsage usage)
        {
            // Keep transfer usage enabled by default because the current RHI upload path
            // copies between staging/device buffers regardless of explicit usage bits.
            VkBufferUsageFlags result = VkBufferUsageFlags.TransferSrc | VkBufferUsageFlags.TransferDst;

            if ((usage & ERHIBufferUsage.CopySrc) == ERHIBufferUsage.CopySrc)
                result |= VkBufferUsageFlags.TransferSrc;
            if ((usage & ERHIBufferUsage.CopyDst) == ERHIBufferUsage.CopyDst)
                result |= VkBufferUsageFlags.TransferDst;
            if ((usage & ERHIBufferUsage.IndexBuffer) == ERHIBufferUsage.IndexBuffer)
                result |= VkBufferUsageFlags.IndexBuffer;
            if ((usage & ERHIBufferUsage.VertexBuffer) == ERHIBufferUsage.VertexBuffer)
                result |= VkBufferUsageFlags.VertexBuffer;
            if ((usage & ERHIBufferUsage.UniformBuffer) == ERHIBufferUsage.UniformBuffer)
                result |= VkBufferUsageFlags.UniformBuffer;
            if ((usage & ERHIBufferUsage.IndirectBuffer) == ERHIBufferUsage.IndirectBuffer)
                result |= VkBufferUsageFlags.IndirectBuffer;
            if ((usage & ERHIBufferUsage.ShaderResource) == ERHIBufferUsage.ShaderResource)
                result |= VkBufferUsageFlags.StorageBuffer;
            if ((usage & ERHIBufferUsage.UnorderedAccess) == ERHIBufferUsage.UnorderedAccess)
                result |= VkBufferUsageFlags.StorageBuffer;
            if ((usage & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct)
                result |= VkBufferUsageFlags.AccelerationStructureStorageKHR
                          | VkBufferUsageFlags.AccelerationStructureBuildInputReadOnlyKHR;
            if ((usage & ERHIBufferUsage.ShaderResource) == ERHIBufferUsage.ShaderResource
                || (usage & ERHIBufferUsage.UnorderedAccess) == ERHIBufferUsage.UnorderedAccess
                || (usage & ERHIBufferUsage.AccelStruct) == ERHIBufferUsage.AccelStruct)
            {
                result |= VkBufferUsageFlags.ShaderDeviceAddress;
            }

            return result;
        }

        public static VkImageUsageFlags ConvertToVkImageUsage(
            in ERHITextureUsage usage)
        {
            VkImageUsageFlags result = 0;

            if ((usage & ERHITextureUsage.CopySrc) == ERHITextureUsage.CopySrc)
                result |= VkImageUsageFlags.TransferSrc;
            if ((usage & ERHITextureUsage.CopyDst) == ERHITextureUsage.CopyDst)
                result |= VkImageUsageFlags.TransferDst;
            if ((usage & ERHITextureUsage.DepthStencil) == ERHITextureUsage.DepthStencil)
                result |= VkImageUsageFlags.DepthStencilAttachment;
            if ((usage & ERHITextureUsage.RenderTarget) == ERHITextureUsage.RenderTarget)
                result |= VkImageUsageFlags.ColorAttachment |
                          VkImageUsageFlags.InputAttachment;
            if ((usage & ERHITextureUsage.ResolveTarget) == ERHITextureUsage.ResolveTarget)
                result |= VkImageUsageFlags.ColorAttachment;
            if ((usage & ERHITextureUsage.ShaderResource) == ERHITextureUsage.ShaderResource)
                result |= VkImageUsageFlags.Sampled;
            if ((usage & ERHITextureUsage.UnorderedAccess) != 0)
                result |= VkImageUsageFlags.Storage;

            return result;
        }

        public static VkImageType ConvertToVkImageType(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture3D:
                    return VkImageType.Image3D;
                default:
                    return VkImageType.Image2D;
            }
        }

        public static VkImageViewType ConvertToVkImageViewType(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.Texture2D:
                case ERHITextureDimension.Texture2DMS:
                    return VkImageViewType.Image2D;
                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.Texture2DArrayMS:
                    return VkImageViewType.Image2DArray;
                case ERHITextureDimension.TextureCube:
                    return VkImageViewType.ImageCube;
                case ERHITextureDimension.TextureCubeArray:
                    return VkImageViewType.ImageCubeArray;
                case ERHITextureDimension.Texture3D:
                    return VkImageViewType.Image3D;
                default:
                    return VkImageViewType.Image2D;
            }
        }

        public static VkSampleCountFlags ConvertToVkSampleCount(in ERHISampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case ERHISampleCount.None:
                    return VkSampleCountFlags.Count1;
                case ERHISampleCount.Count2:
                    return VkSampleCountFlags.Count2;
                case ERHISampleCount.Count4:
                    return VkSampleCountFlags.Count4;
                case ERHISampleCount.Count8:
                    return VkSampleCountFlags.Count8;
                default:
                    return VkSampleCountFlags.Count1;
            }
        }

        public static VkImageCreateFlags ConvertToVkImageCreateFlags(in ERHITextureDimension dimension)
        {
            switch (dimension)
            {
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    return VkImageCreateFlags.CubeCompatible;
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
                    return VkImageAspectFlags.Depth;
                case ERHIPixelFormat.D24_UNorm_S8_UInt:
                case ERHIPixelFormat.D32_Float_S8_UInt:
                    return VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil;
                default:
                    return VkImageAspectFlags.Color;
            }
        }

        public static VkMemoryPropertyFlags ConvertToVkMemoryProperty(in ERHIStorageMode storageMode)
        {
            return storageMode switch
            {
                ERHIStorageMode.GPULocal =>
                    VkMemoryPropertyFlags.DeviceLocal,
                ERHIStorageMode.Readback =>
                    VkMemoryPropertyFlags.HostVisible |
                    VkMemoryPropertyFlags.HostCached,
                ERHIStorageMode.GPUUpload or
                ERHIStorageMode.HostUpload =>
                    VkMemoryPropertyFlags.HostVisible |
                    VkMemoryPropertyFlags.HostCoherent,
                ERHIStorageMode.Memoryless =>
                    VkMemoryPropertyFlags.DeviceLocal |
                    VkMemoryPropertyFlags.LazilyAllocated,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(storageMode),
                    storageMode,
                    "Unknown Vulkan storage mode."),
            };
        }

        private static VkPipelineStageFlags GetSync1QueueDefaultStages(in ERHIPipelineType queuePipeline)
        {
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    return VkPipelineStageFlags.Transfer;
                case ERHIPipelineType.Compute:
                    return VkPipelineStageFlags.ComputeShader | VkPipelineStageFlags.Transfer;
                case ERHIPipelineType.Graphics:
                    return VkPipelineStageFlags.AllCommands;
                default:
                    return VkPipelineStageFlags.AllCommands;
            }
        }

        private static VkPipelineStageFlags2 GetSync2QueueDefaultStages(in ERHIPipelineType queuePipeline)
        {
            switch (queuePipeline)
            {
                case ERHIPipelineType.Transfer:
                    return VkPipelineStageFlags2.AllTransfer;
                case ERHIPipelineType.Compute:
                    return VkPipelineStageFlags2.ComputeShader | VkPipelineStageFlags2.AllTransfer;
                case ERHIPipelineType.Graphics:
                    return VkPipelineStageFlags2.AllCommands;
                default:
                    return VkPipelineStageFlags2.AllCommands;
            }
        }

        public static VkPipelineStageFlags ConvertToVkPipelineStage(in ERHIStageMask stages, in ERHIPipelineType queuePipeline)
        {
            if (stages == ERHIStageMask.None)
            {
                return GetSync1QueueDefaultStages(queuePipeline);
            }

            VkPipelineStageFlags result = 0;
            if ((stages & ERHIStageMask.Transfer) != 0) result |= VkPipelineStageFlags.Transfer;
            if ((stages & ERHIStageMask.Indirect) != 0) result |= VkPipelineStageFlags.DrawIndirect;
            if ((stages & ERHIStageMask.IndexInput) != 0) result |= VkPipelineStageFlags.VertexInput;
            if ((stages & ERHIStageMask.VertexInput) != 0) result |= VkPipelineStageFlags.VertexInput;
            if ((stages & ERHIStageMask.Vertex) != 0) result |= VkPipelineStageFlags.VertexShader;
            if ((stages & ERHIStageMask.Fragment) != 0) result |= VkPipelineStageFlags.FragmentShader;
            if ((stages & ERHIStageMask.Compute) != 0) result |= VkPipelineStageFlags.ComputeShader;
            if ((stages & ERHIStageMask.MachineLearning) != 0) result |= VkPipelineStageFlags.ComputeShader;
            if ((stages & ERHIStageMask.Task) != 0) result |= VkPipelineStageFlags.TaskShaderEXT;
            if ((stages & ERHIStageMask.Mesh) != 0) result |= VkPipelineStageFlags.MeshShaderEXT;
            if ((stages & ERHIStageMask.RayTracing) != 0) result |= VkPipelineStageFlags.RayTracingShaderKHR;
            if ((stages & ERHIStageMask.AccelStructBuild) != 0) result |= VkPipelineStageFlags.AccelerationStructureBuildKHR;
            if ((stages & ERHIStageMask.AccelStructCopy) != 0)
            {
                // TODO: sync1 cannot represent dedicated AS-copy stage; conservatively include transfer/build.
                result |= VkPipelineStageFlags.Transfer | VkPipelineStageFlags.AccelerationStructureBuildKHR;
            }

            if (result == 0)
            {
                return GetSync1QueueDefaultStages(queuePipeline);
            }

            return result;
        }

        public static VkPipelineStageFlags2 ConvertToVkPipelineStage2(in ERHIStageMask stages, in ERHIPipelineType queuePipeline)
        {
            if (stages == ERHIStageMask.None)
            {
                return GetSync2QueueDefaultStages(queuePipeline);
            }

            VkPipelineStageFlags2 result = VkPipelineStageFlags2.None;
            if ((stages & ERHIStageMask.Transfer) != 0) result |= VkPipelineStageFlags2.AllTransfer;
            if ((stages & ERHIStageMask.Indirect) != 0) result |= VkPipelineStageFlags2.DrawIndirect;
            if ((stages & ERHIStageMask.IndexInput) != 0) result |= VkPipelineStageFlags2.IndexInput;
            if ((stages & ERHIStageMask.VertexInput) != 0) result |= VkPipelineStageFlags2.VertexAttributeInput;
            if ((stages & ERHIStageMask.Vertex) != 0) result |= VkPipelineStageFlags2.VertexShader;
            if ((stages & ERHIStageMask.Fragment) != 0) result |= VkPipelineStageFlags2.FragmentShader;
            if ((stages & ERHIStageMask.Compute) != 0) result |= VkPipelineStageFlags2.ComputeShader;
            if ((stages & ERHIStageMask.MachineLearning) != 0) result |= VkPipelineStageFlags2.ComputeShader;
            if ((stages & ERHIStageMask.Task) != 0) result |= VkPipelineStageFlags2.TaskShaderEXT;
            if ((stages & ERHIStageMask.Mesh) != 0) result |= VkPipelineStageFlags2.MeshShaderEXT;
            if ((stages & ERHIStageMask.RayTracing) != 0) result |= VkPipelineStageFlags2.RayTracingShaderKHR;
            if ((stages & ERHIStageMask.AccelStructBuild) != 0) result |= VkPipelineStageFlags2.AccelerationStructureBuildKHR;
            if ((stages & ERHIStageMask.AccelStructCopy) != 0) result |= VkPipelineStageFlags2.AccelerationStructureCopyKHR;

            if (result == VkPipelineStageFlags2.None)
            {
                return GetSync2QueueDefaultStages(queuePipeline);
            }

            return result;
        }

        public static VkAccessFlags ConvertToVkAccessFlags(in ERHIAccessMask accessMask)
        {
            VkAccessFlags result = 0;
            if ((accessMask & ERHIAccessMask.IndirectCommandRead) != 0) result |= VkAccessFlags.IndirectCommandRead;
            if ((accessMask & ERHIAccessMask.IndexRead) != 0) result |= VkAccessFlags.IndexRead;
            if ((accessMask & ERHIAccessMask.VertexRead) != 0) result |= VkAccessFlags.VertexAttributeRead;
            if ((accessMask & ERHIAccessMask.ConstantRead) != 0) result |= VkAccessFlags.UniformRead;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0) result |= VkAccessFlags.ShaderRead;
            if ((accessMask & ERHIAccessMask.ShaderWrite) != 0) result |= VkAccessFlags.ShaderWrite;
            if ((accessMask & ERHIAccessMask.RenderTargetRead) != 0) result |= VkAccessFlags.ColorAttachmentRead;
            if ((accessMask & ERHIAccessMask.RenderTargetWrite) != 0) result |= VkAccessFlags.ColorAttachmentWrite;
            if ((accessMask & ERHIAccessMask.DepthStencilRead) != 0) result |= VkAccessFlags.DepthStencilAttachmentRead;
            if ((accessMask & ERHIAccessMask.DepthStencilWrite) != 0) result |= VkAccessFlags.DepthStencilAttachmentWrite;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= VkAccessFlags.TransferRead;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= VkAccessFlags.TransferWrite;
            if ((accessMask & ERHIAccessMask.ResolveRead) != 0) result |= VkAccessFlags.TransferRead;
            if ((accessMask & ERHIAccessMask.ResolveWrite) != 0) result |= VkAccessFlags.TransferWrite;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) result |= VkAccessFlags.FragmentShadingRateAttachmentReadKHR;
            if ((accessMask & ERHIAccessMask.AccelStructRead) != 0) result |= VkAccessFlags.AccelerationStructureReadKHR;
            if ((accessMask & ERHIAccessMask.AccelStructWrite) != 0) result |= VkAccessFlags.AccelerationStructureWriteKHR;
            if ((accessMask & ERHIAccessMask.Present) != 0) result |= VkAccessFlags.MemoryRead;
            return result;
        }

        public static VkAccessFlags2 ConvertToVkAccessFlags2(in ERHIAccessMask accessMask)
        {
            VkAccessFlags2 result = VkAccessFlags2.None;
            if ((accessMask & ERHIAccessMask.IndirectCommandRead) != 0) result |= VkAccessFlags2.IndirectCommandRead;
            if ((accessMask & ERHIAccessMask.IndexRead) != 0) result |= VkAccessFlags2.IndexRead;
            if ((accessMask & ERHIAccessMask.VertexRead) != 0) result |= VkAccessFlags2.VertexAttributeRead;
            if ((accessMask & ERHIAccessMask.ConstantRead) != 0) result |= VkAccessFlags2.UniformRead;
            if ((accessMask & ERHIAccessMask.ShaderRead) != 0) result |= VkAccessFlags2.ShaderRead;
            if ((accessMask & ERHIAccessMask.ShaderWrite) != 0) result |= VkAccessFlags2.ShaderWrite;
            if ((accessMask & ERHIAccessMask.RenderTargetRead) != 0) result |= VkAccessFlags2.ColorAttachmentRead;
            if ((accessMask & ERHIAccessMask.RenderTargetWrite) != 0) result |= VkAccessFlags2.ColorAttachmentWrite;
            if ((accessMask & ERHIAccessMask.DepthStencilRead) != 0) result |= VkAccessFlags2.DepthStencilAttachmentRead;
            if ((accessMask & ERHIAccessMask.DepthStencilWrite) != 0) result |= VkAccessFlags2.DepthStencilAttachmentWrite;
            if ((accessMask & ERHIAccessMask.TransferRead) != 0) result |= VkAccessFlags2.TransferRead;
            if ((accessMask & ERHIAccessMask.TransferWrite) != 0) result |= VkAccessFlags2.TransferWrite;
            if ((accessMask & ERHIAccessMask.ResolveRead) != 0) result |= VkAccessFlags2.TransferRead;
            if ((accessMask & ERHIAccessMask.ResolveWrite) != 0) result |= VkAccessFlags2.TransferWrite;
            if ((accessMask & ERHIAccessMask.ShadingRateRead) != 0) result |= VkAccessFlags2.FragmentShadingRateAttachmentReadKHR;
            if ((accessMask & ERHIAccessMask.AccelStructRead) != 0) result |= VkAccessFlags2.AccelerationStructureReadKHR;
            if ((accessMask & ERHIAccessMask.AccelStructWrite) != 0) result |= VkAccessFlags2.AccelerationStructureWriteKHR;
            if ((accessMask & ERHIAccessMask.Present) != 0) result |= VkAccessFlags2.MemoryRead;
            return result;
        }

        public static VkAccessFlags ConvertToVkBufferAccessFlag(in ERHIBufferState state)
        {
            VkAccessFlags result = 0;

            if ((state & ERHIBufferState.CopySrc) != 0)
                result |= VkAccessFlags.TransferRead;
            if ((state & ERHIBufferState.CopyDst) != 0)
                result |= VkAccessFlags.TransferWrite;
            if ((state & ERHIBufferState.IndexBuffer) != 0)
                result |= VkAccessFlags.IndexRead;
            if ((state & ERHIBufferState.VertexBuffer) != 0)
                result |= VkAccessFlags.VertexAttributeRead;
            if ((state & ERHIBufferState.ConstantBuffer) != 0)
                result |= VkAccessFlags.UniformRead;
            if ((state & ERHIBufferState.IndirectArgument) != 0)
                result |= VkAccessFlags.IndirectCommandRead;
            if ((state & ERHIBufferState.ShaderResource) != 0)
                result |= VkAccessFlags.ShaderRead;
            if ((state & ERHIBufferState.UnorderedAccess) != 0)
                result |= VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
            if ((state & ERHIBufferState.AccelStructRead) != 0)
                result |= VkAccessFlags.AccelerationStructureReadKHR;
            if ((state & ERHIBufferState.AccelStructWrite) != 0)
                result |= VkAccessFlags.AccelerationStructureWriteKHR;

            return result;
        }

        public static VkAccessFlags ConvertToVkTextureAccessFlag(in ERHITextureState state)
        {
            VkAccessFlags result = 0;

            if ((state & ERHITextureState.CopySrc) != 0)
                result |= VkAccessFlags.TransferRead;
            if ((state & ERHITextureState.CopyDst) != 0)
                result |= VkAccessFlags.TransferWrite;
            if ((state & ERHITextureState.DepthRead) != 0)
                result |= VkAccessFlags.DepthStencilAttachmentRead;
            if ((state & ERHITextureState.DepthWrite) != 0)
                result |= VkAccessFlags.DepthStencilAttachmentWrite;
            if ((state & ERHITextureState.RenderTarget) != 0)
                result |= VkAccessFlags.ColorAttachmentWrite;
            if ((state & ERHITextureState.ShaderResource) != 0)
                result |= VkAccessFlags.ShaderRead;
            if ((state & ERHITextureState.UnorderedAccess) != 0)
                result |= VkAccessFlags.ShaderRead | VkAccessFlags.ShaderWrite;
            if ((state & ERHITextureState.Present) != 0)
                result |= VkAccessFlags.MemoryRead;

            return result;
        }

        public static VkImageLayout ConvertToVkImageLayout(in ERHITextureState state)
        {
            if ((state & ERHITextureState.Present) != 0)
                return VkImageLayout.PresentSrcKHR;
            if ((state & ERHITextureState.RenderTarget) != 0)
                return VkImageLayout.ColorAttachmentOptimal;
            if ((state & ERHITextureState.DepthWrite) != 0)
                return VkImageLayout.DepthStencilAttachmentOptimal;
            if ((state & ERHITextureState.DepthRead) != 0)
                return VkImageLayout.DepthStencilReadOnlyOptimal;
            if ((state & ERHITextureState.UnorderedAccess) != 0)
                return VkImageLayout.General;
            if ((state & ERHITextureState.ShaderResource) != 0)
                return VkImageLayout.ShaderReadOnlyOptimal;
            if ((state & ERHITextureState.CopySrc) != 0)
                return VkImageLayout.TransferSrcOptimal;
            if ((state & ERHITextureState.CopyDst) != 0)
                return VkImageLayout.TransferDstOptimal;

            return VkImageLayout.Undefined;
        }

        public static VkImageLayout ConvertToVkImageLayout(in ERHITextureLayout layout)
        {
            switch (layout)
            {
                case ERHITextureLayout.Common:
                    return VkImageLayout.General;
                case ERHITextureLayout.Present:
                    return VkImageLayout.PresentSrcKHR;
                case ERHITextureLayout.RenderTarget:
                    return VkImageLayout.ColorAttachmentOptimal;
                case ERHITextureLayout.DepthStencilWrite:
                    return VkImageLayout.DepthStencilAttachmentOptimal;
                case ERHITextureLayout.DepthStencilReadOnly:
                    return VkImageLayout.DepthStencilReadOnlyOptimal;
                case ERHITextureLayout.General:
                    return VkImageLayout.General;
                case ERHITextureLayout.ShaderReadOnly:
                    return VkImageLayout.ShaderReadOnlyOptimal;
                case ERHITextureLayout.CopySource:
                    return VkImageLayout.TransferSrcOptimal;
                case ERHITextureLayout.CopyDestination:
                    return VkImageLayout.TransferDstOptimal;
                case ERHITextureLayout.ResolveSource:
                    return VkImageLayout.TransferSrcOptimal;
                case ERHITextureLayout.ResolveDestination:
                    return VkImageLayout.TransferDstOptimal;
                case ERHITextureLayout.ShadingRateSurface:
                    return VkImageLayout.FragmentShadingRateAttachmentOptimalKHR;
                case ERHITextureLayout.Undefined:
                default:
                    return VkImageLayout.Undefined;
            }
        }

        public static VkImageAspectFlags ConvertToVkImageAspect(in ERHITextureAspectMask aspectMask, in ERHIPixelFormat format)
        {
            if (aspectMask == ERHITextureAspectMask.None)
            {
                return GetVkImageAspect(format);
            }

            VkImageAspectFlags result = 0;
            if ((aspectMask & ERHITextureAspectMask.Color) != 0) result |= VkImageAspectFlags.Color;
            if ((aspectMask & ERHITextureAspectMask.Depth) != 0) result |= VkImageAspectFlags.Depth;
            if ((aspectMask & ERHITextureAspectMask.Stencil) != 0) result |= VkImageAspectFlags.Stencil;

            if (result == 0)
            {
                return GetVkImageAspect(format);
            }

            return result;
        }

        public static VkFilter ConvertToVkFilter(in ERHIFilterMode filter)
        {
            switch (filter)
            {
                case ERHIFilterMode.Point:
                    return VkFilter.Nearest;
                case ERHIFilterMode.Linear:
                case ERHIFilterMode.Anisotropic:
                    return VkFilter.Linear;
                default:
                    return VkFilter.Nearest;
            }
        }

        public static VkSamplerMipmapMode ConvertToVkMipmapMode(in ERHIFilterMode filter)
        {
            switch (filter)
            {
                case ERHIFilterMode.Point:
                    return VkSamplerMipmapMode.Nearest;
                case ERHIFilterMode.Linear:
                case ERHIFilterMode.Anisotropic:
                    return VkSamplerMipmapMode.Linear;
                default:
                    return VkSamplerMipmapMode.Nearest;
            }
        }

        public static VkSamplerAddressMode ConvertToVkAddressMode(in ERHIAddressMode mode)
        {
            switch (mode)
            {
                case ERHIAddressMode.Repeat:
                    return VkSamplerAddressMode.Repeat;
                case ERHIAddressMode.ClampToEdge:
                    return VkSamplerAddressMode.ClampToEdge;
                case ERHIAddressMode.MirrorRepeat:
                    return VkSamplerAddressMode.MirroredRepeat;
                default:
                    return VkSamplerAddressMode.Repeat;
            }
        }

        public static VkCompareOp ConvertToVkCompareOp(in ERHIComparisonMode mode)
        {
            switch (mode)
            {
                case ERHIComparisonMode.Never:
                    return VkCompareOp.Never;
                case ERHIComparisonMode.Less:
                    return VkCompareOp.Less;
                case ERHIComparisonMode.Equal:
                    return VkCompareOp.Equal;
                case ERHIComparisonMode.LessEqual:
                    return VkCompareOp.LessOrEqual;
                case ERHIComparisonMode.Greater:
                    return VkCompareOp.Greater;
                case ERHIComparisonMode.NotEqual:
                    return VkCompareOp.NotEqual;
                case ERHIComparisonMode.GreaterEqual:
                    return VkCompareOp.GreaterOrEqual;
                case ERHIComparisonMode.Always:
                    return VkCompareOp.Always;
                default:
                    return VkCompareOp.Never;
            }
        }

        public static VkStencilOp ConvertToVkStencilOp(in ERHIStencilOp op)
        {
            switch (op)
            {
                case ERHIStencilOp.Keep:
                    return VkStencilOp.Keep;
                case ERHIStencilOp.Zero:
                    return VkStencilOp.Zero;
                case ERHIStencilOp.Replace:
                    return VkStencilOp.Replace;
                case ERHIStencilOp.IncrementSaturation:
                    return VkStencilOp.IncrementAndClamp;
                case ERHIStencilOp.DecrementSaturation:
                    return VkStencilOp.DecrementAndClamp;
                case ERHIStencilOp.Invert:
                    return VkStencilOp.Invert;
                case ERHIStencilOp.Increment:
                    return VkStencilOp.IncrementAndWrap;
                case ERHIStencilOp.Decrement:
                    return VkStencilOp.DecrementAndWrap;
                default:
                    return VkStencilOp.Keep;
            }
        }

        public static VkBlendOp ConvertToVkBlendOp(in ERHIBlendOp op)
        {
            switch (op)
            {
                case ERHIBlendOp.Add:
                    return VkBlendOp.Add;
                case ERHIBlendOp.Substract:
                    return VkBlendOp.Subtract;
                case ERHIBlendOp.ReverseSubstract:
                    return VkBlendOp.ReverseSubtract;
                case ERHIBlendOp.Min:
                    return VkBlendOp.Min;
                case ERHIBlendOp.Max:
                    return VkBlendOp.Max;
                default:
                    return VkBlendOp.Add;
            }
        }

        public static VkBlendFactor ConvertToVkBlendFactor(in ERHIBlendMode mode)
        {
            switch (mode)
            {
                case ERHIBlendMode.Zero:
                    return VkBlendFactor.Zero;
                case ERHIBlendMode.One:
                    return VkBlendFactor.One;
                case ERHIBlendMode.SrcColor:
                    return VkBlendFactor.SrcColor;
                case ERHIBlendMode.OneMinusSrcColor:
                    return VkBlendFactor.OneMinusSrcColor;
                case ERHIBlendMode.SrcAlpha:
                    return VkBlendFactor.SrcAlpha;
                case ERHIBlendMode.OneMinusSrcAlpha:
                    return VkBlendFactor.OneMinusSrcAlpha;
                case ERHIBlendMode.DstColor:
                    return VkBlendFactor.DstColor;
                case ERHIBlendMode.OneMinusDstColor:
                    return VkBlendFactor.OneMinusDstColor;
                case ERHIBlendMode.DstAlpha:
                    return VkBlendFactor.DstAlpha;
                case ERHIBlendMode.OneMinusDstAlpha:
                    return VkBlendFactor.OneMinusDstAlpha;
                case ERHIBlendMode.SrcAlphaSaturate:
                    return VkBlendFactor.SrcAlphaSaturate;
                case ERHIBlendMode.BlendFactor:
                    return VkBlendFactor.ConstantColor;
                case ERHIBlendMode.InverseBlendFactor:
                    return VkBlendFactor.OneMinusConstantColor;
                case ERHIBlendMode.SecondarySourceColor:
                    return VkBlendFactor.Src1Color;
                case ERHIBlendMode.InverseSecondarySourceColor:
                    return VkBlendFactor.OneMinusSrc1Color;
                case ERHIBlendMode.SecondarySourceAlpha:
                    return VkBlendFactor.Src1Alpha;
                case ERHIBlendMode.InverseSecondarySourceAlpha:
                    return VkBlendFactor.OneMinusSrc1Alpha;
                default:
                    return VkBlendFactor.Zero;
            }
        }

        public static VkColorComponentFlags ConvertToVkColorWriteMask(in ERHIColorWriteChannel channel)
        {
            VkColorComponentFlags result = 0;
            if ((channel & ERHIColorWriteChannel.Red) != 0) result |= VkColorComponentFlags.R;
            if ((channel & ERHIColorWriteChannel.Green) != 0) result |= VkColorComponentFlags.G;
            if ((channel & ERHIColorWriteChannel.Blue) != 0) result |= VkColorComponentFlags.B;
            if ((channel & ERHIColorWriteChannel.Alpha) != 0) result |= VkColorComponentFlags.A;
            return result;
        }

        public static VkPolygonMode ConvertToVkPolygonMode(in ERHIFillMode fillMode)
        {
            switch (fillMode)
            {
                case ERHIFillMode.Solid:
                    return VkPolygonMode.Fill;
                case ERHIFillMode.Wireframe:
                    return VkPolygonMode.Line;
                default:
                    return VkPolygonMode.Fill;
            }
        }

        public static VkCullModeFlags ConvertToVkCullMode(in ERHICullMode cullMode)
        {
            switch (cullMode)
            {
                case ERHICullMode.None:
                    return VkCullModeFlags.None;
                case ERHICullMode.Back:
                    return VkCullModeFlags.Back;
                case ERHICullMode.Front:
                    return VkCullModeFlags.Front;
                default:
                    return VkCullModeFlags.None;
            }
        }

        public static VkFrontFace ConvertToVkFrontFace(bool frontCounterClockwise)
        {
            return frontCounterClockwise ? VkFrontFace.CounterClockwise : VkFrontFace.Clockwise;
        }

        public static VkPrimitiveTopology ConvertToVkPrimitiveTopology(in ERHIPrimitiveTopology topology)
        {
            switch (topology)
            {
                case ERHIPrimitiveTopology.PointList:
                    return VkPrimitiveTopology.PointList;
                case ERHIPrimitiveTopology.LineList:
                    return VkPrimitiveTopology.LineList;
                case ERHIPrimitiveTopology.LineStrip:
                    return VkPrimitiveTopology.LineStrip;
                case ERHIPrimitiveTopology.TriangleList:
                    return VkPrimitiveTopology.TriangleList;
                case ERHIPrimitiveTopology.TriangleStrip:
                    return VkPrimitiveTopology.TriangleStrip;
                case ERHIPrimitiveTopology.LineListAdj:
                    return VkPrimitiveTopology.LineListWithAdjacency;
                case ERHIPrimitiveTopology.LineStripAdj:
                    return VkPrimitiveTopology.LineStripWithAdjacency;
                case ERHIPrimitiveTopology.TriangleListAdj:
                    return VkPrimitiveTopology.TriangleListWithAdjacency;
                case ERHIPrimitiveTopology.TriangleStripAdj:
                    return VkPrimitiveTopology.TriangleStripWithAdjacency;
                default:
                    return VkPrimitiveTopology.TriangleList;
            }
        }

        public static VkAttachmentLoadOp ConvertToVkLoadOp(in ERHILoadAction action)
        {
            switch (action)
            {
                case ERHILoadAction.Load:
                    return VkAttachmentLoadOp.Load;
                case ERHILoadAction.Clear:
                    return VkAttachmentLoadOp.Clear;
                case ERHILoadAction.DontCare:
                    return VkAttachmentLoadOp.DontCare;
                default:
                    return VkAttachmentLoadOp.DontCare;
            }
        }

        public static VkAttachmentStoreOp ConvertToVkStoreOp(in ERHIStoreAction action)
        {
            switch (action)
            {
                case ERHIStoreAction.Store:
                case ERHIStoreAction.Resolve:
                case ERHIStoreAction.StoreAndResolve:
                    return VkAttachmentStoreOp.Store;
                case ERHIStoreAction.DontCare:
                    return VkAttachmentStoreOp.DontCare;
                default:
                    return VkAttachmentStoreOp.DontCare;
            }
        }

        public static VkPresentModeKHR ConvertToVkPresentMode(in ERHIPresentMode mode)
        {
            switch (mode)
            {
                case ERHIPresentMode.VSync:
                    return VkPresentModeKHR.Fifo;
                case ERHIPresentMode.Immediately:
                    return VkPresentModeKHR.Immediate;
                default:
                    return VkPresentModeKHR.Fifo;
            }
        }

        public static VkDescriptorType ConvertToVkDescriptorType(in ERHIBindType bindType)
        {
            switch (bindType)
            {
                case ERHIBindType.Sampler:
                    return VkDescriptorType.Sampler;
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                    return VkDescriptorType.StorageBuffer;
                case ERHIBindType.UniformBuffer:
                    return VkDescriptorType.UniformBuffer;
                case ERHIBindType.AccelStruct:
                    return VkDescriptorType.AccelerationStructureKHR;
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                    return VkDescriptorType.SampledImage;
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return VkDescriptorType.StorageImage;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(bindType),
                        bindType,
                        "Vulkan descriptor bind type is unsupported.");
            }
        }

        public static VkShaderStageFlags ConvertToVkShaderStages(in ERHIShaderStageMask stages)
        {
            if (stages == ERHIShaderStageMask.None || (stages & ~ERHIShaderStageMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stages),
                    stages,
                    "Vulkan argument-table shader-stage visibility must be a non-empty known mask.");
            }

            VkShaderStageFlags result = 0;

            if (stages == ERHIShaderStageMask.All)
                return VkShaderStageFlags.All;
            if (stages == ERHIShaderStageMask.AllGraphics)
                return VkShaderStageFlags.AllGraphics;

            if ((stages & ERHIShaderStageMask.Vertex) != 0)
                result |= VkShaderStageFlags.Vertex;
            if ((stages & ERHIShaderStageMask.Fragment) != 0)
                result |= VkShaderStageFlags.Fragment;
            if ((stages & ERHIShaderStageMask.Compute) != 0)
                result |= VkShaderStageFlags.Compute;
            if ((stages & ERHIShaderStageMask.Task) != 0)
                result |= VkShaderStageFlags.TaskEXT;
            if ((stages & ERHIShaderStageMask.Mesh) != 0)
                result |= VkShaderStageFlags.MeshEXT;
            if ((stages & ERHIShaderStageMask.RayTracing) != 0)
                result |= VkShaderStageFlags.RaygenKHR | VkShaderStageFlags.MissKHR | VkShaderStageFlags.ClosestHitKHR | VkShaderStageFlags.AnyHitKHR | VkShaderStageFlags.IntersectionKHR;
            if ((stages & ERHIShaderStageMask.MachineLearning) != 0)
                result |= VkShaderStageFlags.Compute;

            return result;
        }

        public static VkQueryType ConvertToVkQueryType(in ERHIQueryType queryType)
        {
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    return VkQueryType.Occlusion;
                case ERHIQueryType.Statistics:
                    return VkQueryType.PipelineStatistics;
                case ERHIQueryType.TimestampTransfer:
                case ERHIQueryType.Timestamp:
                    return VkQueryType.Timestamp;
                default:
                    return VkQueryType.Timestamp;
            }
        }

        public static VkExtent2D ConvertToVkFragmentExtent(in ERHIShadingRate shadingRate)
        {
            switch (shadingRate)
            {
                case ERHIShadingRate.Rate1x1:
                    return new VkExtent2D() { width = 1, height = 1 };
                case ERHIShadingRate.Rate1x2:
                    return new VkExtent2D() { width = 1, height = 2 };
                case ERHIShadingRate.Rate2x1:
                    return new VkExtent2D() { width = 2, height = 1 };
                case ERHIShadingRate.Rate2x2:
                    return new VkExtent2D() { width = 2, height = 2 };
                case ERHIShadingRate.Rate2x4:
                    return new VkExtent2D() { width = 2, height = 4 };
                case ERHIShadingRate.Rate4x2:
                    return new VkExtent2D() { width = 4, height = 2 };
                case ERHIShadingRate.Rate4x4:
                    return new VkExtent2D() { width = 4, height = 4 };
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(shadingRate),
                        shadingRate,
                        "Unsupported Vulkan shading rate.");
            }
        }

        public static bool TryMapVkFragmentSizeToShadingRate(
            in VkExtent2D fragmentSize,
            out ERHIShadingRate shadingRate)
        {
            if (fragmentSize.width == 1 && fragmentSize.height == 1)
            {
                shadingRate = ERHIShadingRate.Rate1x1;
                return true;
            }
            if (fragmentSize.width == 1 && fragmentSize.height == 2)
            {
                shadingRate = ERHIShadingRate.Rate1x2;
                return true;
            }
            if (fragmentSize.width == 2 && fragmentSize.height == 1)
            {
                shadingRate = ERHIShadingRate.Rate2x1;
                return true;
            }
            if (fragmentSize.width == 2 && fragmentSize.height == 2)
            {
                shadingRate = ERHIShadingRate.Rate2x2;
                return true;
            }
            if (fragmentSize.width == 2 && fragmentSize.height == 4)
            {
                shadingRate = ERHIShadingRate.Rate2x4;
                return true;
            }
            if (fragmentSize.width == 4 && fragmentSize.height == 2)
            {
                shadingRate = ERHIShadingRate.Rate4x2;
                return true;
            }
            if (fragmentSize.width == 4 && fragmentSize.height == 4)
            {
                shadingRate = ERHIShadingRate.Rate4x4;
                return true;
            }

            shadingRate = default;
            return false;
        }

        public static VkFragmentShadingRateCombinerOpKHR ConvertToVkShadingRateCombiner(in ERHIShadingRateCombiner combiner)
        {
            switch (combiner)
            {
                case ERHIShadingRateCombiner.Min:
                    return VkFragmentShadingRateCombinerOpKHR.Min;
                case ERHIShadingRateCombiner.Max:
                    return VkFragmentShadingRateCombinerOpKHR.Max;
                case ERHIShadingRateCombiner.Override:
                    return VkFragmentShadingRateCombinerOpKHR.Replace;
                case ERHIShadingRateCombiner.Passthrough:
                    return VkFragmentShadingRateCombinerOpKHR.Keep;
                case ERHIShadingRateCombiner.Sum:
                    throw new NotSupportedException(
                        "Vulkan fragment shading rate has no Sum combiner.");
                default:
                    throw new NotSupportedException(
                        $"Shading rate combiner {combiner} is not supported by Vulkan.");
            }
        }

        public static VkShaderStageFlags ConvertToVkShaderStageBit(in ERHIFunctionType type)
        {
            switch (type)
            {
                case ERHIFunctionType.Vertex:
                    return VkShaderStageFlags.Vertex;
                case ERHIFunctionType.Fragment:
                    return VkShaderStageFlags.Fragment;
                case ERHIFunctionType.Compute:
                    return VkShaderStageFlags.Compute;
                case ERHIFunctionType.Task:
                    return VkShaderStageFlags.TaskEXT;
                case ERHIFunctionType.Mesh:
                    return VkShaderStageFlags.MeshEXT;
                case ERHIFunctionType.RayTracing:
                    return VkShaderStageFlags.RaygenKHR;
                case ERHIFunctionType.MachineLearning:
                    return VkShaderStageFlags.Compute;
                default:
                    return VkShaderStageFlags.All;
            }
        }
    }
    #region NativeLoader
internal static unsafe class VulkanNative
    {
        static VulkanNative()
        {
            Vulkan.vkInitialize();
        }

        private static readonly ConcurrentDictionary<nint, VkInstanceApi> s_InstanceApis = new();
        private static readonly ConcurrentDictionary<nint, VkDeviceApi> s_DeviceApis = new();
        private static readonly ConcurrentDictionary<nint, nint> s_PhysicalToInstance = new();
        private static readonly ConcurrentDictionary<nint, nint> s_QueueToDevice = new();
        private static readonly ConcurrentDictionary<nint, nint> s_CommandBufferToDevice = new();

        // Vortice.Vulkan 3.2.1 keeps dispatch tables in private static
        // ConcurrentDictionaries and does not evict entries after native
        // destruction. SharpGPU pins this integration boundary and evicts
        // each handle after vkDestroy* has returned. A changed Vortice
        // layout fails loudly during type initialization instead of reusing
        // a stale dispatch table.
        private static readonly FieldInfo s_VorticeInstanceTablesField =
            GetRequiredVorticeCacheField(
                "s_instanceTables",
                typeof(ConcurrentDictionary<VkInstance, VkInstanceApi>));
        private static readonly FieldInfo s_VorticeDeviceTablesField =
            GetRequiredVorticeCacheField(
                "s_deviceTables",
                typeof(ConcurrentDictionary<VkDevice, VkDeviceApi>));

        private static FieldInfo GetRequiredVorticeCacheField(
            string fieldName,
            Type expectedType)
        {
            FieldInfo? field = typeof(Vulkan).GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field is null || field.FieldType != expectedType)
            {
                throw new InvalidOperationException(
                    $"SharpGPU requires pinned Vortice.Vulkan field {fieldName} " +
                    $"with type {expectedType.FullName}.");
            }

            return field;
        }

        private static void EvictVorticeInstance(VkInstance instance)
        {
            object? cache = s_VorticeInstanceTablesField.GetValue(null);
            if (cache is ConcurrentDictionary<VkInstance, VkInstanceApi> table)
            {
                table.TryRemove(instance, out _);
                return;
            }

            throw new InvalidOperationException(
                "Pinned Vortice.Vulkan instance dispatch table has an unexpected runtime type.");
        }

        private static void EvictVorticeDevice(VkDevice device)
        {
            object? cache = s_VorticeDeviceTablesField.GetValue(null);
            if (cache is ConcurrentDictionary<VkDevice, VkDeviceApi> table)
            {
                table.TryRemove(device, out _);
                return;
            }

            throw new InvalidOperationException(
                "Pinned Vortice.Vulkan device dispatch table has an unexpected runtime type.");
        }
        private static readonly ConcurrentDictionary<nint, PFN_vkCreateWaylandSurfaceKHR> s_CreateWaylandSurface = new();
        private static nint s_VulkanLoader;
        private static PFN_vkGetInstanceProcAddr? s_GetInstanceProcAddr;

        private static VkInstanceApi GetInstanceApi(VkInstance instance) => s_InstanceApis.GetOrAdd(instance.Handle, _ => Vulkan.GetApi(instance));
        private static VkInstanceApi GetPrimaryInstanceApi() { foreach (VkInstanceApi api in s_InstanceApis.Values) return api; throw new InvalidOperationException("No Vulkan instance API registered."); }
        private static VkInstanceApi GetInstanceApi(VkPhysicalDevice physicalDevice)
        {
            if (s_PhysicalToInstance.TryGetValue(physicalDevice.Handle, out nint instanceHandle) && s_InstanceApis.TryGetValue(instanceHandle, out VkInstanceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkPhysicalDevice is not registered to any VkInstance.");
        }
        private static VkDeviceApi GetDeviceApi(VkDevice device)
        {
            if (s_DeviceApis.TryGetValue(device.Handle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkDevice API not registered.");
        }
        private static VkDeviceApi GetPrimaryDeviceApi() { foreach (VkDeviceApi api in s_DeviceApis.Values) return api; throw new InvalidOperationException("No Vulkan device API registered."); }
        private static VkDeviceApi GetDeviceApi(VkQueue queue)
        {
            if (s_QueueToDevice.TryGetValue(queue.Handle, out nint deviceHandle) && s_DeviceApis.TryGetValue(deviceHandle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkQueue is not registered to any VkDevice.");
        }
        private static VkDeviceApi GetDeviceApi(VkCommandBuffer commandBuffer)
        {
            if (s_CommandBufferToDevice.TryGetValue(commandBuffer.Handle, out nint deviceHandle) && s_DeviceApis.TryGetValue(deviceHandle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkCommandBuffer is not registered to any VkDevice.");
        }
        private static void RegisterInstance(VkInstance instance) => GetInstanceApi(instance);
        private static void UnregisterInstance(VkInstance instance)
        {
            try
            {
                EvictVorticeInstance(instance);
            }
            finally
            {
                s_InstanceApis.TryRemove(instance.Handle, out _);
                foreach (nint physicalHandle in s_PhysicalToInstance.Where(kv => kv.Value == instance.Handle).Select(kv => kv.Key).ToArray()) s_PhysicalToInstance.TryRemove(physicalHandle, out _);
            }
        }
        private static void RegisterPhysicalDevice(VkInstance instance, VkPhysicalDevice physicalDevice) => s_PhysicalToInstance[physicalDevice.Handle] = instance.Handle;
        private static void RegisterDevice(VkPhysicalDevice physicalDevice, VkDevice device)
        {
            if (!s_PhysicalToInstance.TryGetValue(physicalDevice.Handle, out nint instanceHandle)) throw new InvalidOperationException("VkPhysicalDevice is not registered to any VkInstance.");
            VkInstance instance = new VkInstance(instanceHandle);
            s_DeviceApis[device.Handle] = Vulkan.GetApi(instance, device);
        }
        private static void UnregisterDevice(VkDevice device)
        {
            try
            {
                EvictVorticeDevice(device);
            }
            finally
            {
                s_DeviceApis.TryRemove(device.Handle, out _);
                foreach (nint queueHandle in s_QueueToDevice.Where(kv => kv.Value == device.Handle).Select(kv => kv.Key).ToArray()) s_QueueToDevice.TryRemove(queueHandle, out _);
                foreach (nint commandBufferHandle in s_CommandBufferToDevice.Where(kv => kv.Value == device.Handle).Select(kv => kv.Key).ToArray()) s_CommandBufferToDevice.TryRemove(commandBufferHandle, out _);
            }
        }
        private static void RegisterQueue(VkDevice device, VkQueue queue) => s_QueueToDevice[queue.Handle] = device.Handle;
        private static void RegisterCommandBuffers(VkDevice device, uint count, VkCommandBuffer* commandBuffers)
        {
            if (commandBuffers == null) return;
            for (uint i = 0; i < count; i++) s_CommandBufferToDevice[commandBuffers[i].Handle] = device.Handle;
        }

        public static VkResult vkAcquireNextImageKHR(VkDevice device, VkSwapchainKHR swapchain, ulong timeout, VkSemaphore semaphore, VkFence fence, uint* imageIndex)
        {
            VkResult result = GetDeviceApi(device).vkAcquireNextImageKHR(swapchain, timeout, semaphore, fence, imageIndex);
            return result;
        }

        public static VkResult vkAllocateCommandBuffers(VkDevice device, VkCommandBufferAllocateInfo* allocateInfo, VkCommandBuffer* commandBuffers)
        {
            VkResult result = GetDeviceApi(device).vkAllocateCommandBuffers(allocateInfo, commandBuffers);
            if (result == VkResult.Success && allocateInfo != null) RegisterCommandBuffers(device, allocateInfo->commandBufferCount, commandBuffers);
            return result;
        }

        public static VkResult vkAllocateDescriptorSets(VkDevice device, VkDescriptorSetAllocateInfo* allocateInfo, VkDescriptorSet* descriptorSets)
        {
            VkResult result = GetDeviceApi(device).vkAllocateDescriptorSets(allocateInfo, descriptorSets);
            return result;
        }

        public static VkResult vkAllocateMemory(VkDevice device, VkMemoryAllocateInfo* allocateInfo, VkAllocationCallbacks* allocator, VkDeviceMemory* memory)
        {
            VkResult result = GetDeviceApi(device).vkAllocateMemory(allocateInfo, allocator, memory);
            return result;
        }

        public static VkResult vkBeginCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferBeginInfo* beginInfo)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkBeginCommandBuffer(commandBuffer, beginInfo);
            return result;
        }

        public static VkResult vkBindBufferMemory(VkDevice device, VkBuffer buffer, VkDeviceMemory memory, ulong memoryOffset)
        {
            VkResult result = GetDeviceApi(device).vkBindBufferMemory(buffer, memory, memoryOffset);
            return result;
        }

        public static VkResult vkBindImageMemory(VkDevice device, VkImage image, VkDeviceMemory memory, ulong memoryOffset)
        {
            VkResult result = GetDeviceApi(device).vkBindImageMemory(image, memory, memoryOffset);
            return result;
        }

        public static void vkCmdBeginQuery(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint query, VkQueryControlFlags flags)
        {
            GetDeviceApi(commandBuffer).vkCmdBeginQuery(commandBuffer, queryPool, query, flags);
        }

        public static void vkCmdBeginRendering(
            VkCommandBuffer commandBuffer,
            VkRenderingInfo* renderingInfo,
            bool useKhrCommand = false)
        {
            if (useKhrCommand)
            {
                GetDeviceApi(commandBuffer).vkCmdBeginRenderingKHR(
                    commandBuffer,
                    renderingInfo);
            }
            else
            {
                GetDeviceApi(commandBuffer).vkCmdBeginRendering(
                    commandBuffer,
                    renderingInfo);
            }
        }

        public static void vkCmdBeginRenderPass2(
            VkCommandBuffer commandBuffer,
            VkRenderPassBeginInfo* renderPassBegin,
            VkSubpassBeginInfo* subpassBegin,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdBeginRenderPass2KHR(
                    commandBuffer,
                    renderPassBegin,
                    subpassBegin);
            }
            else
            {
                api.vkCmdBeginRenderPass2(
                    commandBuffer,
                    renderPassBegin,
                    subpassBegin);
            }
        }

        public static void vkCmdBindDescriptorSets(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipelineLayout layout, uint firstSet, uint descriptorSetCount, VkDescriptorSet* descriptorSets, uint dynamicOffsetCount, uint* dynamicOffsets)
        {
            GetDeviceApi(commandBuffer).vkCmdBindDescriptorSets(commandBuffer, pipelineBindPoint, layout, firstSet, descriptorSetCount, descriptorSets, dynamicOffsetCount, dynamicOffsets);
        }

        public static void vkCmdBindIndexBuffer(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, VkIndexType indexType)
        {
            GetDeviceApi(commandBuffer).vkCmdBindIndexBuffer(commandBuffer, buffer, offset, indexType);
        }

        public static void vkCmdBindPipeline(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipeline pipeline)
        {
            GetDeviceApi(commandBuffer).vkCmdBindPipeline(commandBuffer, pipelineBindPoint, pipeline);
        }

        public static void vkCmdBindVertexBuffers(VkCommandBuffer commandBuffer, uint firstBinding, uint bindingCount, VkBuffer* buffers, ulong* offsets)
        {
            GetDeviceApi(commandBuffer).vkCmdBindVertexBuffers(commandBuffer, firstBinding, bindingCount, buffers, offsets);
        }

        public static void vkCmdBuildAccelerationStructuresKHR(VkCommandBuffer commandBuffer, uint infoCount, VkAccelerationStructureBuildGeometryInfoKHR* infos, VkAccelerationStructureBuildRangeInfoKHR** buildRangeInfos)
        {
            GetDeviceApi(commandBuffer).vkCmdBuildAccelerationStructuresKHR(commandBuffer, infoCount, infos, buildRangeInfos);
        }

        public static void vkCmdCopyBuffer(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkBuffer dstBuffer, uint regionCount, VkBufferCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, regionCount, regions);
        }

        public static void vkCmdCopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkImage dstImage, VkImageLayout dstImageLayout, uint regionCount, VkBufferImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyBufferToImage(commandBuffer, srcBuffer, dstImage, dstImageLayout, regionCount, regions);
        }

        public static void vkCmdCopyImage(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkImage dstImage, VkImageLayout dstImageLayout, uint regionCount, VkImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyImage(commandBuffer, srcImage, srcImageLayout, dstImage, dstImageLayout, regionCount, regions);
        }

        public static void vkCmdCopyImageToBuffer(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkBuffer dstBuffer, uint regionCount, VkBufferImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyImageToBuffer(commandBuffer, srcImage, srcImageLayout, dstBuffer, regionCount, regions);
        }

        public static void vkCmdDispatch(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            GetDeviceApi(commandBuffer).vkCmdDispatch(commandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public static void vkCmdDispatchIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset)
        {
            GetDeviceApi(commandBuffer).vkCmdDispatchIndirect(commandBuffer, buffer, offset);
        }

        public static void vkCmdDraw(VkCommandBuffer commandBuffer, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            GetDeviceApi(commandBuffer).vkCmdDraw(commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public static void vkCmdDrawIndexed(VkCommandBuffer commandBuffer, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndexed(commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public static void vkCmdDrawIndexedIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndexedIndirect(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdDrawIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndirect(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdDrawMeshTasksEXT(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawMeshTasksEXT(commandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public static void vkCmdDrawMeshTasksIndirectEXT(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawMeshTasksIndirectEXT(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdEndQuery(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint query)
        {
            GetDeviceApi(commandBuffer).vkCmdEndQuery(commandBuffer, queryPool, query);
        }

        public static void vkCmdEndRendering(
            VkCommandBuffer commandBuffer,
            bool useKhrCommand = false)
        {
            if (useKhrCommand)
            {
                GetDeviceApi(commandBuffer).vkCmdEndRenderingKHR(
                    commandBuffer);
            }
            else
            {
                GetDeviceApi(commandBuffer).vkCmdEndRendering(
                    commandBuffer);
            }
        }

        public static void vkCmdEndRenderPass2(
            VkCommandBuffer commandBuffer,
            VkSubpassEndInfo* subpassEnd,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdEndRenderPass2KHR(commandBuffer, subpassEnd);
            }
            else
            {
                api.vkCmdEndRenderPass2(commandBuffer, subpassEnd);
            }
        }

        public static void vkCmdNextSubpass2(
            VkCommandBuffer commandBuffer,
            VkSubpassBeginInfo* subpassBegin,
            VkSubpassEndInfo* subpassEnd,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdNextSubpass2KHR(
                    commandBuffer,
                    subpassBegin,
                    subpassEnd);
            }
            else
            {
                api.vkCmdNextSubpass2(
                    commandBuffer,
                    subpassBegin,
                    subpassEnd);
            }
        }

        public static void vkCmdPipelineBarrier(VkCommandBuffer commandBuffer, VkPipelineStageFlags srcStageMask, VkPipelineStageFlags dstStageMask, VkDependencyFlags dependencyFlags, uint memoryBarrierCount, VkMemoryBarrier* memoryBarriers, uint bufferMemoryBarrierCount, VkBufferMemoryBarrier* bufferMemoryBarriers, uint imageMemoryBarrierCount, VkImageMemoryBarrier* imageMemoryBarriers)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier(commandBuffer, srcStageMask, dstStageMask, dependencyFlags, memoryBarrierCount, memoryBarriers, bufferMemoryBarrierCount, bufferMemoryBarriers, imageMemoryBarrierCount, imageMemoryBarriers);
        }

        public static void vkCmdPipelineBarrier2(VkCommandBuffer commandBuffer, VkDependencyInfo* dependencyInfo)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier2(commandBuffer, dependencyInfo);
        }

        public static void vkCmdPipelineBarrier2KHR(VkCommandBuffer commandBuffer, VkDependencyInfo* dependencyInfo)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier2KHR(commandBuffer, dependencyInfo);
        }

        public static void vkCmdPushConstants(VkCommandBuffer commandBuffer, VkPipelineLayout layout, VkShaderStageFlags stageFlags, uint offset, uint size, void* values)
        {
            GetDeviceApi(commandBuffer).vkCmdPushConstants(commandBuffer, layout, stageFlags, offset, size, values);
        }

        public static void vkCmdResetQueryPool(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint firstQuery, uint queryCount)
        {
            GetDeviceApi(commandBuffer).vkCmdResetQueryPool(commandBuffer, queryPool, firstQuery, queryCount);
        }

        public static void vkCmdSetBlendConstants(VkCommandBuffer commandBuffer, float* blendConstants)
        {
            GetDeviceApi(commandBuffer).vkCmdSetBlendConstants(commandBuffer, blendConstants);
        }

        public static void vkCmdSetRenderingAttachmentLocations(
            VkCommandBuffer commandBuffer,
            VkRenderingAttachmentLocationInfo* locationInfo,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdSetRenderingAttachmentLocationsKHR(
                    commandBuffer,
                    locationInfo);
            }
            else
            {
                api.vkCmdSetRenderingAttachmentLocations(
                    commandBuffer,
                    locationInfo);
            }
        }

        public static void vkCmdSetRenderingInputAttachmentIndices(
            VkCommandBuffer commandBuffer,
            VkRenderingInputAttachmentIndexInfo* inputIndexInfo,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdSetRenderingInputAttachmentIndicesKHR(
                    commandBuffer,
                    inputIndexInfo);
            }
            else
            {
                api.vkCmdSetRenderingInputAttachmentIndices(
                    commandBuffer,
                    inputIndexInfo);
            }
        }

        public static void vkCmdSetFragmentShadingRateKHR(VkCommandBuffer commandBuffer, VkExtent2D* fragmentSize, VkFragmentShadingRateCombinerOpKHR* combinerOps)
        {
            GetDeviceApi(commandBuffer).vkCmdSetFragmentShadingRateKHR(commandBuffer, fragmentSize, combinerOps);
        }

        public static void vkCmdSetScissor(VkCommandBuffer commandBuffer, uint firstScissor, uint scissorCount, VkRect2D* scissors)
        {
            GetDeviceApi(commandBuffer).vkCmdSetScissor(commandBuffer, firstScissor, scissorCount, scissors);
        }

        public static void vkCmdSetStencilReference(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, uint reference)
        {
            GetDeviceApi(commandBuffer).vkCmdSetStencilReference(commandBuffer, faceMask, reference);
        }

        public static void vkCmdSetViewport(VkCommandBuffer commandBuffer, uint firstViewport, uint viewportCount, VkViewport* viewports)
        {
            GetDeviceApi(commandBuffer).vkCmdSetViewport(commandBuffer, firstViewport, viewportCount, viewports);
        }

        public static void vkCmdTraceRaysIndirectKHR(VkCommandBuffer commandBuffer, VkStridedDeviceAddressRegionKHR* raygenShaderBindingTable, VkStridedDeviceAddressRegionKHR* missShaderBindingTable, VkStridedDeviceAddressRegionKHR* hitShaderBindingTable, VkStridedDeviceAddressRegionKHR* callableShaderBindingTable, ulong indirectDeviceAddress)
        {
            GetDeviceApi(commandBuffer).vkCmdTraceRaysIndirectKHR(commandBuffer, raygenShaderBindingTable, missShaderBindingTable, hitShaderBindingTable, callableShaderBindingTable, indirectDeviceAddress);
        }

        public static void vkCmdTraceRaysKHR(VkCommandBuffer commandBuffer, VkStridedDeviceAddressRegionKHR* raygenShaderBindingTable, VkStridedDeviceAddressRegionKHR* missShaderBindingTable, VkStridedDeviceAddressRegionKHR* hitShaderBindingTable, VkStridedDeviceAddressRegionKHR* callableShaderBindingTable, uint width, uint height, uint depth)
        {
            GetDeviceApi(commandBuffer).vkCmdTraceRaysKHR(commandBuffer, raygenShaderBindingTable, missShaderBindingTable, hitShaderBindingTable, callableShaderBindingTable, width, height, depth);
        }

        public static void vkCmdWriteTimestamp(VkCommandBuffer commandBuffer, VkPipelineStageFlags pipelineStage, VkQueryPool queryPool, uint query)
        {
            GetDeviceApi(commandBuffer).vkCmdWriteTimestamp(commandBuffer, pipelineStage, queryPool, query);
        }

        public static VkResult vkCreateAccelerationStructureKHR(VkDevice device, VkAccelerationStructureCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkAccelerationStructureKHR* accelerationStructure)
        {
            VkResult result = GetDeviceApi(device).vkCreateAccelerationStructureKHR(createInfo, allocator, accelerationStructure);
            return result;
        }

        public static VkResult vkCreateBuffer(VkDevice device, VkBufferCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkBuffer* buffer)
        {
            VkResult result = GetDeviceApi(device).vkCreateBuffer(createInfo, allocator, buffer);
            return result;
        }

        public static VkResult vkCreateCommandPool(VkDevice device, VkCommandPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkCommandPool* commandPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateCommandPool(createInfo, allocator, commandPool);
            return result;
        }

        public static VkResult vkCreateComputePipelines(VkDevice device, VkPipelineCache pipelineCache, uint createInfoCount, VkComputePipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateComputePipelines(pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateDescriptorPool(VkDevice device, VkDescriptorPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDescriptorPool* descriptorPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateDescriptorPool(createInfo, allocator, descriptorPool);
            return result;
        }

        public static VkResult vkCreateDescriptorSetLayout(VkDevice device, VkDescriptorSetLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDescriptorSetLayout* setLayout)
        {
            VkResult result = GetDeviceApi(device).vkCreateDescriptorSetLayout(createInfo, allocator, setLayout);
            return result;
        }

        public static VkResult vkCreateDevice(VkPhysicalDevice physicalDevice, VkDeviceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDevice* device)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkCreateDevice(physicalDevice, createInfo, allocator, device);
            if (result == VkResult.Success && device != null) RegisterDevice(physicalDevice, *device);
            return result;
        }

        public static VkResult vkCreateFramebuffer(
            VkDevice device,
            VkFramebufferCreateInfo* createInfo,
            VkAllocationCallbacks* allocator,
            VkFramebuffer* framebuffer)
        {
            return GetDeviceApi(device).vkCreateFramebuffer(
                createInfo,
                allocator,
                framebuffer);
        }

        public static VkResult vkCreateRenderPass2(
            VkDevice device,
            VkRenderPassCreateInfo2* createInfo,
            VkAllocationCallbacks* allocator,
            VkRenderPass* renderPass,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(device);
            return useKhrEntryPoints
                ? api.vkCreateRenderPass2KHR(
                    createInfo,
                    allocator,
                    renderPass)
                : api.vkCreateRenderPass2(
                    createInfo,
                    allocator,
                    renderPass);
        }

        public static VkResult vkCreateFence(VkDevice device, VkFenceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkFence* fence)
        {
            VkResult result = GetDeviceApi(device).vkCreateFence(createInfo, allocator, fence);
            return result;
        }

        public static VkResult vkCreateGraphicsPipelines(VkDevice device, VkPipelineCache pipelineCache, uint createInfoCount, VkGraphicsPipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateGraphicsPipelines(pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateImage(VkDevice device, VkImageCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkImage* image)
        {
            VkResult result = GetDeviceApi(device).vkCreateImage(createInfo, allocator, image);
            return result;
        }

        public static VkResult vkCreateImageView(VkDevice device, VkImageViewCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkImageView* view)
        {
            VkResult result = GetDeviceApi(device).vkCreateImageView(createInfo, allocator, view);
            return result;
        }

        public static VkResult vkCreateInstance(VkInstanceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkInstance* instance)
        {
            VkResult result = Vulkan.vkCreateInstance(createInfo, allocator, instance);
            if (result == VkResult.Success && instance != null) RegisterInstance(*instance);
            return result;
        }

        public static VkResult vkCreatePipelineCache(VkDevice device, VkPipelineCacheCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkPipelineCache* pipelineCache)
        {
            VkResult result = GetDeviceApi(device).vkCreatePipelineCache(createInfo, allocator, pipelineCache);
            return result;
        }

        public static VkResult vkCreatePipelineLayout(VkDevice device, VkPipelineLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkPipelineLayout* pipelineLayout)
        {
            VkResult result = GetDeviceApi(device).vkCreatePipelineLayout(createInfo, allocator, pipelineLayout);
            return result;
        }

        public static VkResult vkCreateQueryPool(VkDevice device, VkQueryPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkQueryPool* queryPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateQueryPool(createInfo, allocator, queryPool);
            return result;
        }

        public static VkResult vkCreateRayTracingPipelinesKHR(VkDevice device, VkDeferredOperationKHR deferredOperation, VkPipelineCache pipelineCache, uint createInfoCount, VkRayTracingPipelineCreateInfoKHR* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateRayTracingPipelinesKHR(deferredOperation, pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateSampler(VkDevice device, VkSamplerCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkSampler* sampler)
        {
            VkResult result = GetDeviceApi(device).vkCreateSampler(createInfo, allocator, sampler);
            return result;
        }

        public static VkResult vkCreateSemaphore(VkDevice device, VkSemaphoreCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkSemaphore* semaphore)
        {
            VkResult result = GetDeviceApi(device).vkCreateSemaphore(createInfo, allocator, semaphore);
            return result;
        }

        public static VkResult vkCreateShaderModule(VkDevice device, VkShaderModuleCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkShaderModule* shaderModule)
        {
            VkResult result = GetDeviceApi(device).vkCreateShaderModule(createInfo, allocator, shaderModule);
            return result;
        }

        public static VkResult vkCreateSwapchainKHR(VkDevice device, VkSwapchainCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSwapchainKHR* swapchain)
        {
            VkResult result = GetDeviceApi(device).vkCreateSwapchainKHR(createInfo, allocator, swapchain);
            return result;
        }

        public static VkResult vkCreateAndroidSurfaceKHR(VkInstance instance, VkAndroidSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateAndroidSurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateMetalSurfaceEXT(VkInstance instance, VkMetalSurfaceCreateInfoEXT* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateMetalSurfaceEXT(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateWin32SurfaceKHR(VkInstance instance, VkWin32SurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateWin32SurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateXlibSurfaceKHR(VkInstance instance, VkXlibSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateXlibSurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateWaylandSurfaceKHR(VkInstance instance, void* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            PFN_vkCreateWaylandSurfaceKHR create = s_CreateWaylandSurface.GetOrAdd(instance.Handle, _ =>
            {
                PFN_vkGetInstanceProcAddr getProc = GetInstanceProcAddr();
                nint name = Marshal.StringToCoTaskMemUTF8("vkCreateWaylandSurfaceKHR");
                try
                {
                    nint address = getProc(instance, (byte*)name);
                    if (address == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("vkCreateWaylandSurfaceKHR is unavailable on the active Vulkan instance.");
                    }
                    return Marshal.GetDelegateForFunctionPointer<PFN_vkCreateWaylandSurfaceKHR>(address);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            });
            return create(instance, createInfo, allocator, surface);
        }

        private static PFN_vkGetInstanceProcAddr GetInstanceProcAddr()
        {
            if (s_GetInstanceProcAddr != null)
            {
                return s_GetInstanceProcAddr;
            }

            if (!NativeLibrary.TryLoad("libvulkan.so.1", out s_VulkanLoader) &&
                !NativeLibrary.TryLoad("libvulkan.so", out s_VulkanLoader))
            {
                throw new InvalidOperationException("Unable to load the Vulkan loader for Wayland surface creation.");
            }
            nint address = NativeLibrary.GetExport(s_VulkanLoader, "vkGetInstanceProcAddr");
            s_GetInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<PFN_vkGetInstanceProcAddr>(address);
            return s_GetInstanceProcAddr;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate nint PFN_vkGetInstanceProcAddr(VkInstance instance, byte* name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate VkResult PFN_vkCreateWaylandSurfaceKHR(VkInstance instance, void* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface);

        public static void vkDestroyAccelerationStructureKHR(VkDevice device, VkAccelerationStructureKHR accelerationStructure, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyAccelerationStructureKHR(accelerationStructure, allocator);
        }

        public static void vkDestroyBuffer(VkDevice device, VkBuffer buffer, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyBuffer(buffer, allocator);
        }

        public static void vkDestroyCommandPool(VkDevice device, VkCommandPool commandPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyCommandPool(commandPool, allocator);
        }

        public static void vkDestroyDescriptorPool(VkDevice device, VkDescriptorPool descriptorPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyDescriptorPool(descriptorPool, allocator);
        }

        public static void vkDestroyDescriptorSetLayout(VkDevice device, VkDescriptorSetLayout descriptorSetLayout, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyDescriptorSetLayout(descriptorSetLayout, allocator);
        }

        public static void vkDestroyFramebuffer(VkDevice device, VkFramebuffer framebuffer, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyFramebuffer(framebuffer, allocator);
        }

        public static void vkDestroyDevice(VkDevice device, VkAllocationCallbacks* allocator)
        {
            VkDevice destroyedDevice = device;
            GetDeviceApi(device).vkDestroyDevice(allocator);
            UnregisterDevice(destroyedDevice);
        }

        public static void vkDestroyFence(VkDevice device, VkFence fence, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyFence(fence, allocator);
        }

        public static void vkDestroyImage(VkDevice device, VkImage image, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyImage(image, allocator);
        }

        public static void vkDestroyImageView(VkDevice device, VkImageView imageView, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyImageView(imageView, allocator);
        }

        public static void vkDestroyInstance(VkInstance instance, VkAllocationCallbacks* allocator)
        {
            VkInstance destroyedInstance = instance;
            GetInstanceApi(instance).vkDestroyInstance(allocator);
            UnregisterInstance(destroyedInstance);
        }

        public static void vkDestroyPipeline(VkDevice device, VkPipeline pipeline, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipeline(pipeline, allocator);
        }

        public static void vkDestroyPipelineCache(VkDevice device, VkPipelineCache pipelineCache, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipelineCache(pipelineCache, allocator);
        }

        public static void vkDestroyPipelineLayout(VkDevice device, VkPipelineLayout pipelineLayout, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipelineLayout(pipelineLayout, allocator);
        }

        public static void vkDestroyRenderPass(VkDevice device, VkRenderPass renderPass, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyRenderPass(renderPass, allocator);
        }

        public static void vkDestroyQueryPool(VkDevice device, VkQueryPool queryPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyQueryPool(queryPool, allocator);
        }

        public static void vkDestroySampler(VkDevice device, VkSampler sampler, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySampler(sampler, allocator);
        }

        public static void vkDestroySemaphore(VkDevice device, VkSemaphore semaphore, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySemaphore(semaphore, allocator);
        }

        public static void vkDestroyShaderModule(VkDevice device, VkShaderModule shaderModule, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyShaderModule(shaderModule, allocator);
        }

        public static void vkDestroySurfaceKHR(VkInstance instance, VkSurfaceKHR surface, VkAllocationCallbacks* allocator)
        {
            GetInstanceApi(instance).vkDestroySurfaceKHR(surface, allocator);
        }

        public static void vkDestroySwapchainKHR(VkDevice device, VkSwapchainKHR swapchain, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySwapchainKHR(swapchain, allocator);
        }

        public static VkResult vkDeviceWaitIdle(VkDevice device)
        {
            VkResult result = GetDeviceApi(device).vkDeviceWaitIdle();
            return result;
        }

        public static VkResult vkEndCommandBuffer(VkCommandBuffer commandBuffer)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkEndCommandBuffer(commandBuffer);
            return result;
        }

        public static VkResult vkEnumerateDeviceExtensionProperties(VkPhysicalDevice physicalDevice, byte* layerName, uint* propertyCount, VkExtensionProperties* properties)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkEnumerateDeviceExtensionProperties(physicalDevice, layerName, propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumerateInstanceExtensionProperties(byte* layerName, uint* propertyCount, VkExtensionProperties* properties)
        {
            VkResult result = Vulkan.vkEnumerateInstanceExtensionProperties(layerName, propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumerateInstanceLayerProperties(uint* propertyCount, VkLayerProperties* properties)
        {
            VkResult result = Vulkan.vkEnumerateInstanceLayerProperties(propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumeratePhysicalDevices(VkInstance instance, uint* physicalDeviceCount, VkPhysicalDevice* physicalDevices)
        {
            VkResult result = GetInstanceApi(instance).vkEnumeratePhysicalDevices(physicalDeviceCount, physicalDevices);
            if (result == VkResult.Success && physicalDevices != null)
            {
                uint count = physicalDeviceCount != null ? *physicalDeviceCount : 0u;
                for (uint idx = 0; idx < count; idx++) RegisterPhysicalDevice(instance, physicalDevices[idx]);
            }
            return result;
        }

        public static VkResult vkFlushMappedMemoryRanges(VkDevice device, uint memoryRangeCount, VkMappedMemoryRange* memoryRanges)
        {
            VkResult result = GetDeviceApi(device).vkFlushMappedMemoryRanges(memoryRangeCount, memoryRanges);
            return result;
        }

        public static VkResult vkInvalidateMappedMemoryRanges(VkDevice device, uint memoryRangeCount, VkMappedMemoryRange* memoryRanges)
        {
            VkResult result = GetDeviceApi(device).vkInvalidateMappedMemoryRanges(memoryRangeCount, memoryRanges);
            return result;
        }

        public static VkResult vkFreeDescriptorSets(VkDevice device, VkDescriptorPool descriptorPool, uint descriptorSetCount, VkDescriptorSet* descriptorSets)
        {
            VkResult result = GetDeviceApi(device).vkFreeDescriptorSets(descriptorPool, descriptorSetCount, descriptorSets);
            return result;
        }

        public static void vkFreeMemory(VkDevice device, VkDeviceMemory memory, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkFreeMemory(memory, allocator);
        }

        public static void vkGetAccelerationStructureBuildSizesKHR(VkDevice device, VkAccelerationStructureBuildTypeKHR buildType, VkAccelerationStructureBuildGeometryInfoKHR* buildInfo, uint* maxPrimitiveCounts, VkAccelerationStructureBuildSizesInfoKHR* sizeInfo)
        {
            GetDeviceApi(device).vkGetAccelerationStructureBuildSizesKHR(buildType, buildInfo, maxPrimitiveCounts, sizeInfo);
        }

        public static ulong vkGetAccelerationStructureDeviceAddressKHR(VkDevice device, VkAccelerationStructureDeviceAddressInfoKHR* info)
        {
            ulong result = GetDeviceApi(device).vkGetAccelerationStructureDeviceAddressKHR(info);
            return result;
        }

        public static ulong vkGetBufferDeviceAddress(VkDevice device, VkBufferDeviceAddressInfo* info)
        {
            ulong result = GetDeviceApi(device).vkGetBufferDeviceAddress(info);
            return result;
        }

        public static void vkGetBufferMemoryRequirements(VkDevice device, VkBuffer buffer, VkMemoryRequirements* memoryRequirements)
        {
            GetDeviceApi(device).vkGetBufferMemoryRequirements(buffer, memoryRequirements);
        }

        public static void vkGetDeviceQueue(VkDevice device, uint queueFamilyIndex, uint queueIndex, VkQueue* queue)
        {
            GetDeviceApi(device).vkGetDeviceQueue(queueFamilyIndex, queueIndex, queue);
            if (queue != null) RegisterQueue(device, *queue);
        }

        public static VkResult vkGetFenceStatus(VkDevice device, VkFence fence)
        {
            VkResult result = GetDeviceApi(device).vkGetFenceStatus(fence);
            return result;
        }

        public static void vkGetImageMemoryRequirements(VkDevice device, VkImage image, VkMemoryRequirements* memoryRequirements)
        {
            GetDeviceApi(device).vkGetImageMemoryRequirements(image, memoryRequirements);
        }

        public static void vkGetImageSparseMemoryRequirements(VkDevice device, VkImage image, uint* sparseMemoryRequirementCount, VkSparseImageMemoryRequirements* sparseMemoryRequirements)
        {
            GetDeviceApi(device).vkGetImageSparseMemoryRequirements(image, sparseMemoryRequirementCount, sparseMemoryRequirements);
        }

        public static void vkGetPhysicalDeviceFeatures(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures* features)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceFeatures(physicalDevice, features);
        }

        public static void vkGetPhysicalDeviceFeatures2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures2* features)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceFeatures2(physicalDevice, features);
        }

        public static VkResult vkGetPhysicalDeviceFragmentShadingRatesKHR(
            VkPhysicalDevice physicalDevice,
            uint* fragmentShadingRateCount,
            VkPhysicalDeviceFragmentShadingRateKHR* fragmentShadingRates)
        {
            return GetInstanceApi(physicalDevice).vkGetPhysicalDeviceFragmentShadingRatesKHR(
                physicalDevice,
                fragmentShadingRateCount,
                fragmentShadingRates);
        }

        public static void vkGetPhysicalDeviceFormatProperties(
            VkPhysicalDevice physicalDevice,
            VkFormat format,
            VkFormatProperties* formatProperties)
        {
            GetInstanceApi(physicalDevice)
                .vkGetPhysicalDeviceFormatProperties(
                    physicalDevice,
                    format,
                    formatProperties);
        }

        public static void vkGetPhysicalDeviceFormatProperties2(
            VkPhysicalDevice physicalDevice,
            VkFormat format,
            VkFormatProperties2* formatProperties)
        {
            GetInstanceApi(physicalDevice)
                .vkGetPhysicalDeviceFormatProperties2(
                    physicalDevice,
                    format,
                    formatProperties);
        }

        public static VkResult vkGetPhysicalDeviceImageFormatProperties2(
            VkPhysicalDevice physicalDevice,
            VkPhysicalDeviceImageFormatInfo2* imageFormatInfo,
            VkImageFormatProperties2* imageFormatProperties)
        {
            return GetInstanceApi(physicalDevice)
                .vkGetPhysicalDeviceImageFormatProperties2(
                    physicalDevice,
                    imageFormatInfo,
                    imageFormatProperties);
        }

        public static VkResult
            vkGetPhysicalDeviceImageFormatProperties(
                VkPhysicalDevice physicalDevice,
                VkFormat format,
                VkImageType imageType,
                VkImageTiling tiling,
                VkImageUsageFlags usage,
                VkImageCreateFlags flags,
                VkImageFormatProperties* imageFormatProperties)
        {
            return GetInstanceApi(physicalDevice)
                .vkGetPhysicalDeviceImageFormatProperties(
                    physicalDevice,
                    format,
                    imageType,
                    tiling,
                    usage,
                    flags,
                    imageFormatProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties2* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties2(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties2KHR(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties2* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties2KHR(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties* properties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceProperties(physicalDevice, properties);
        }

        public static void vkGetPhysicalDeviceProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties2* properties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceProperties2(physicalDevice, properties);
        }

        public static void vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice physicalDevice, uint* queueFamilyPropertyCount, VkQueueFamilyProperties* queueFamilyProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, queueFamilyPropertyCount, queueFamilyProperties);
        }

        public static VkResult vkGetPhysicalDeviceSurfaceCapabilitiesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, VkSurfaceCapabilitiesKHR* surfaceCapabilities)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, surfaceCapabilities);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfaceFormatsKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* surfaceFormatCount, VkSurfaceFormatKHR* surfaceFormats)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, surfaceFormatCount, surfaceFormats);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* presentModeCount, VkPresentModeKHR* presentModes)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface, presentModeCount, presentModes);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfaceSupportKHR(VkPhysicalDevice physicalDevice, uint queueFamilyIndex, VkSurfaceKHR surface, VkBool32* supported)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, queueFamilyIndex, surface, supported);
            return result;
        }

        public static VkResult vkGetPipelineCacheData(VkDevice device, VkPipelineCache pipelineCache, nuint* dataSize, void* data)
        {
            VkResult result = GetDeviceApi(device).vkGetPipelineCacheData(pipelineCache, dataSize, data);
            return result;
        }

        public static VkResult vkGetQueryPoolResults(VkDevice device, VkQueryPool queryPool, uint firstQuery, uint queryCount, nuint dataSize, void* data, ulong stride, VkQueryResultFlags flags)
        {
            VkResult result = GetDeviceApi(device).vkGetQueryPoolResults(queryPool, firstQuery, queryCount, dataSize, data, stride, flags);
            return result;
        }

        public static VkResult vkGetRayTracingShaderGroupHandlesKHR(VkDevice device, VkPipeline pipeline, uint firstGroup, uint groupCount, nuint dataSize, void* data)
        {
            VkResult result = GetDeviceApi(device).vkGetRayTracingShaderGroupHandlesKHR(pipeline, firstGroup, groupCount, dataSize, data);
            return result;
        }

        public static VkResult vkGetSwapchainImagesKHR(VkDevice device, VkSwapchainKHR swapchain, uint* swapchainImageCount, VkImage* swapchainImages)
        {
            VkResult result = GetDeviceApi(device).vkGetSwapchainImagesKHR(swapchain, swapchainImageCount, swapchainImages);
            return result;
        }

        public static VkResult vkMapMemory(VkDevice device, VkDeviceMemory memory, ulong offset, ulong size, VkMemoryMapFlags flags, void** data)
        {
            VkResult result = GetDeviceApi(device).vkMapMemory(memory, offset, size, flags, data);
            return result;
        }

        public static VkResult vkQueueBindSparse(VkQueue queue, uint bindInfoCount, VkBindSparseInfo* bindInfo, VkFence fence)
        {
            VkResult result = GetDeviceApi(queue).vkQueueBindSparse(queue, bindInfoCount, bindInfo, fence);
            return result;
        }

        public static VkResult vkQueuePresentKHR(VkQueue queue, VkPresentInfoKHR* presentInfo)
        {
            VkResult result = GetDeviceApi(queue).vkQueuePresentKHR(queue, presentInfo);
            return result;
        }

        public static VkResult vkQueueSubmit(VkQueue queue, uint submitCount, VkSubmitInfo* submits, VkFence fence)
        {
            VkResult result = GetDeviceApi(queue).vkQueueSubmit(queue, submitCount, submits, fence);
            return result;
        }

        public static VkResult vkQueueWaitIdle(VkQueue queue)
        {
            VkResult result = GetDeviceApi(queue).vkQueueWaitIdle(queue);
            return result;
        }

        public static VkResult vkResetCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferResetFlags flags)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkResetCommandBuffer(commandBuffer, flags);
            return result;
        }

        public static VkResult vkResetFences(VkDevice device, uint fenceCount, VkFence* fences)
        {
            VkResult result = GetDeviceApi(device).vkResetFences(fenceCount, fences);
            return result;
        }

        public static void vkUnmapMemory(VkDevice device, VkDeviceMemory memory)
        {
            GetDeviceApi(device).vkUnmapMemory(memory);
        }

        public static void vkUpdateDescriptorSets(VkDevice device, uint descriptorWriteCount, VkWriteDescriptorSet* descriptorWrites, uint descriptorCopyCount, VkCopyDescriptorSet* descriptorCopies)
        {
            GetDeviceApi(device).vkUpdateDescriptorSets(descriptorWriteCount, descriptorWrites, descriptorCopyCount, descriptorCopies);
        }

        public static VkResult vkWaitForFences(VkDevice device, uint fenceCount, VkFence* fences, VkBool32 waitAll, ulong timeout)
        {
            VkResult result = GetDeviceApi(device).vkWaitForFences(fenceCount, fences, waitAll, timeout);
            return result;
        }

    }
    #endregion

    internal enum VkTimeDomainKHR : int
    {
        Device = 0,
        ClockMonotonic = 1,
        ClockMonotonicRaw = 2,
        QueryPerformanceCounter = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VkCalibratedTimestampInfoKHR
    {
        public VkStructureType sType;
        public unsafe void* pNext;
        public VkTimeDomainKHR timeDomain;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal unsafe delegate VkResult PFN_vkGetPhysicalDeviceCalibrateableTimeDomainsKHR(
        VkPhysicalDevice physicalDevice,
        uint* timeDomainCount,
        VkTimeDomainKHR* timeDomains);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal unsafe delegate VkResult PFN_vkGetCalibratedTimestampsKHR(
        VkDevice device,
        uint timestampCount,
        VkCalibratedTimestampInfoKHR* timestampInfos,
        ulong* timestamps,
        ulong* maxDeviation);

    internal static unsafe class VulkanCalibratedTimestampNative
    {
        internal const string KhrExtensionName = "VK_KHR_calibrated_timestamps";
        internal const string ExtExtensionName = "VK_EXT_calibrated_timestamps";
        internal const VkStructureType CalibratedTimestampInfoStructureType =
            (VkStructureType)1000184000;

        internal static bool TryLoad(
            VulkanInstance instance,
            out PFN_vkGetPhysicalDeviceCalibrateableTimeDomainsKHR? getTimeDomains,
            out PFN_vkGetCalibratedTimestampsKHR? getTimestamps,
            out string extensionName,
            out string getTimestampsFunctionName)
        {
            getTimeDomains = null;
            getTimestamps = null;
            extensionName = KhrExtensionName;
            getTimestampsFunctionName = "vkGetCalibratedTimestampsKHR";

            IntPtr domainsKhr = instance.TryGetInstanceProcedure(
                "vkGetPhysicalDeviceCalibrateableTimeDomainsKHR");
            IntPtr timestampsKhr = instance.TryGetInstanceProcedure(
                "vkGetCalibratedTimestampsKHR");
            if (domainsKhr != IntPtr.Zero && timestampsKhr != IntPtr.Zero)
            {
                getTimeDomains =
                    Marshal.GetDelegateForFunctionPointer<
                        PFN_vkGetPhysicalDeviceCalibrateableTimeDomainsKHR>(
                        domainsKhr);
                getTimestamps =
                    Marshal.GetDelegateForFunctionPointer<
                        PFN_vkGetCalibratedTimestampsKHR>(
                        timestampsKhr);
                return true;
            }

            IntPtr domainsExt = instance.TryGetInstanceProcedure(
                "vkGetPhysicalDeviceCalibrateableTimeDomainsEXT");
            IntPtr timestampsExt = instance.TryGetInstanceProcedure(
                "vkGetCalibratedTimestampsEXT");
            if (domainsExt != IntPtr.Zero && timestampsExt != IntPtr.Zero)
            {
                extensionName = ExtExtensionName;
                getTimestampsFunctionName = "vkGetCalibratedTimestampsEXT";
                getTimeDomains =
                    Marshal.GetDelegateForFunctionPointer<
                        PFN_vkGetPhysicalDeviceCalibrateableTimeDomainsKHR>(
                        domainsExt);
                getTimestamps =
                    Marshal.GetDelegateForFunctionPointer<
                        PFN_vkGetCalibratedTimestampsKHR>(
                        timestampsExt);
                return true;
            }

            return false;
        }
    }

#pragma warning restore CA1416
}
