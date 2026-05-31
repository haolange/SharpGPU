using System;
using System.Text;
using SharpGPU.Core;
using Vortice.Vulkan;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
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
            if (result != VkResult.Success)
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
                    return VkFormat.B8G8R8A8Unorm;
                case ERHISwapChainFormat.R10G10B10A2_UNorm:
                    return VkFormat.A2B10G10R10UnormPack32;
                case ERHISwapChainFormat.R16G16B16A16_Float:
                    return VkFormat.R16G16B16A16Sfloat;
                default:
                    return VkFormat.B8G8R8A8Unorm;
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

        public static VkImageUsageFlags ConvertToVkImageUsage(in ERHITextureUsage usage)
        {
            VkImageUsageFlags result = 0;

            if ((usage & ERHITextureUsage.CopySrc) == ERHITextureUsage.CopySrc)
                result |= VkImageUsageFlags.TransferSrc;
            if ((usage & ERHITextureUsage.CopyDst) == ERHITextureUsage.CopyDst)
                result |= VkImageUsageFlags.TransferDst;
            if ((usage & ERHITextureUsage.DepthStencil) == ERHITextureUsage.DepthStencil)
                result |= VkImageUsageFlags.DepthStencilAttachment;
            if ((usage & ERHITextureUsage.RenderTarget) == ERHITextureUsage.RenderTarget)
                result |= VkImageUsageFlags.ColorAttachment;
            if ((usage & ERHITextureUsage.ShaderResource) == ERHITextureUsage.ShaderResource)
                result |= VkImageUsageFlags.Sampled;
            if ((usage & ERHITextureUsage.UnorderedAccess) == ERHITextureUsage.UnorderedAccess)
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
            switch (storageMode)
            {
                case ERHIStorageMode.GPULocal:
                    return VkMemoryPropertyFlags.DeviceLocal;
                case ERHIStorageMode.Readback:
                    return VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCached;
                case ERHIStorageMode.GPUUpload:
                case ERHIStorageMode.HostUpload:
                    return VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent;
                default:
                    return VkMemoryPropertyFlags.DeviceLocal;
            }
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

        public static VkPipelineStageFlags ConvertToVkPipelineStage(in ERHISyncStageMask stages, in ERHIPipelineType queuePipeline)
        {
            if (stages == ERHISyncStageMask.None)
            {
                return GetSync1QueueDefaultStages(queuePipeline);
            }

            VkPipelineStageFlags result = 0;
            if ((stages & ERHISyncStageMask.Transfer) != 0) result |= VkPipelineStageFlags.Transfer;
            if ((stages & ERHISyncStageMask.Indirect) != 0) result |= VkPipelineStageFlags.DrawIndirect;
            if ((stages & ERHISyncStageMask.IndexInput) != 0) result |= VkPipelineStageFlags.VertexInput;
            if ((stages & ERHISyncStageMask.VertexInput) != 0) result |= VkPipelineStageFlags.VertexInput;
            if ((stages & ERHISyncStageMask.Vertex) != 0) result |= VkPipelineStageFlags.VertexShader;
            if ((stages & ERHISyncStageMask.Fragment) != 0) result |= VkPipelineStageFlags.FragmentShader;
            if ((stages & ERHISyncStageMask.Compute) != 0) result |= VkPipelineStageFlags.ComputeShader;
            if ((stages & ERHISyncStageMask.MachineLearning) != 0) result |= VkPipelineStageFlags.ComputeShader;
            if ((stages & ERHISyncStageMask.Task) != 0) result |= VkPipelineStageFlags.TaskShaderEXT;
            if ((stages & ERHISyncStageMask.Mesh) != 0) result |= VkPipelineStageFlags.MeshShaderEXT;
            if ((stages & ERHISyncStageMask.RayTracing) != 0) result |= VkPipelineStageFlags.RayTracingShaderKHR;
            if ((stages & ERHISyncStageMask.AccelStructBuild) != 0) result |= VkPipelineStageFlags.AccelerationStructureBuildKHR;
            if ((stages & ERHISyncStageMask.AccelStructCopy) != 0)
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

        public static VkPipelineStageFlags2 ConvertToVkPipelineStage2(in ERHISyncStageMask stages, in ERHIPipelineType queuePipeline)
        {
            if (stages == ERHISyncStageMask.None)
            {
                return GetSync2QueueDefaultStages(queuePipeline);
            }

            VkPipelineStageFlags2 result = VkPipelineStageFlags2.None;
            if ((stages & ERHISyncStageMask.Transfer) != 0) result |= VkPipelineStageFlags2.AllTransfer;
            if ((stages & ERHISyncStageMask.Indirect) != 0) result |= VkPipelineStageFlags2.DrawIndirect;
            if ((stages & ERHISyncStageMask.IndexInput) != 0) result |= VkPipelineStageFlags2.IndexInput;
            if ((stages & ERHISyncStageMask.VertexInput) != 0) result |= VkPipelineStageFlags2.VertexAttributeInput;
            if ((stages & ERHISyncStageMask.Vertex) != 0) result |= VkPipelineStageFlags2.VertexShader;
            if ((stages & ERHISyncStageMask.Fragment) != 0) result |= VkPipelineStageFlags2.FragmentShader;
            if ((stages & ERHISyncStageMask.Compute) != 0) result |= VkPipelineStageFlags2.ComputeShader;
            if ((stages & ERHISyncStageMask.MachineLearning) != 0) result |= VkPipelineStageFlags2.ComputeShader;
            if ((stages & ERHISyncStageMask.Task) != 0) result |= VkPipelineStageFlags2.TaskShaderEXT;
            if ((stages & ERHISyncStageMask.Mesh) != 0) result |= VkPipelineStageFlags2.MeshShaderEXT;
            if ((stages & ERHISyncStageMask.RayTracing) != 0) result |= VkPipelineStageFlags2.RayTracingShaderKHR;
            if ((stages & ERHISyncStageMask.AccelStructBuild) != 0) result |= VkPipelineStageFlags2.AccelerationStructureBuildKHR;
            if ((stages & ERHISyncStageMask.AccelStructCopy) != 0) result |= VkPipelineStageFlags2.AccelerationStructureCopyKHR;

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
                    return VkDescriptorType.SampledImage;
            }
        }

        public static VkShaderStageFlags ConvertToVkShaderStage(in ERHIShaderStage stage)
        {
            VkShaderStageFlags result = 0;

            if ((stage & ERHIShaderStage.All) == ERHIShaderStage.All)
                return VkShaderStageFlags.All;
            if ((stage & ERHIShaderStage.AllGraphics) == ERHIShaderStage.AllGraphics)
                return VkShaderStageFlags.AllGraphics;

            if ((stage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex)
                result |= VkShaderStageFlags.Vertex;
            if ((stage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment)
                result |= VkShaderStageFlags.Fragment;
            if ((stage & ERHIShaderStage.Compute) == ERHIShaderStage.Compute)
                result |= VkShaderStageFlags.Compute;
            if ((stage & ERHIShaderStage.Task) == ERHIShaderStage.Task)
                result |= VkShaderStageFlags.TaskEXT;
            if ((stage & ERHIShaderStage.Mesh) == ERHIShaderStage.Mesh)
                result |= VkShaderStageFlags.MeshEXT;
            if ((stage & ERHIShaderStage.RayTracing) == ERHIShaderStage.RayTracing)
                result |= VkShaderStageFlags.RaygenKHR | VkShaderStageFlags.MissKHR | VkShaderStageFlags.ClosestHitKHR | VkShaderStageFlags.AnyHitKHR | VkShaderStageFlags.IntersectionKHR;
            if ((stage & ERHIShaderStage.MachineLearning) == ERHIShaderStage.MachineLearning)
                result |= VkShaderStageFlags.Compute;

            return result == 0 ? VkShaderStageFlags.All : result;
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
                case ERHIQueryType.TimestampGenerice:
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
                    return new VkExtent2D() { width = 1, height = 1 };
            }
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
                default:
                    return VkFragmentShadingRateCombinerOpKHR.Keep;
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
#pragma warning restore CS8600, CS8602, CA1416
}
