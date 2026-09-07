using System;
using SharpMath;

namespace SharpGPU.Builders
{
    public static class RHIDescriptorDefaults
    {
        public static RHIBufferDescriptor Buffer(
            int byteSize,
            ERHIBufferUsage usage,
            ERHIStorageMode storageMode = ERHIStorageMode.GPULocal,
            ERHIBufferFormat format = ERHIBufferFormat.Undefine)
        {
            if (byteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteSize), "Buffer byte size must be greater than zero.");
            }

            return new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = format,
                UsageFlag = usage,
                StorageMode = storageMode,
            };
        }

        public static RHITextureDescriptor Texture2D(
            uint width,
            uint height,
            ERHIPixelFormat format,
            ERHITextureUsage usage,
            ERHIStorageMode storageMode = ERHIStorageMode.GPULocal,
            uint mipCount = 1,
            ERHISampleCount sampleCount = ERHISampleCount.None)
        {
            if (width == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Texture width must be greater than zero.");
            }

            if (height == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), "Texture height must be greater than zero.");
            }

            return new RHITextureDescriptor
            {
                MipCount = mipCount == 0 ? 1 : mipCount,
                Extent = new uint3(width, height, 1),
                Format = format,
                SampleCount = sampleCount,
                StorageMode = storageMode,
                UsageFlag = usage,
                Dimension = ERHITextureDimension.Texture2D,
            };
        }

        public static RHISamplerDescriptor LinearClampSampler()
        {
            return new RHISamplerDescriptor
            {
                LodMin = 0.0f,
                LodMax = float.MaxValue,
                MipLODBias = 0.0f,
                Anisotropy = 1,
                MinFilter = ERHIFilterMode.Linear,
                MagFilter = ERHIFilterMode.Linear,
                MipFilter = ERHIFilterMode.Linear,
                AddressModeU = ERHIAddressMode.ClampToEdge,
                AddressModeV = ERHIAddressMode.ClampToEdge,
                AddressModeW = ERHIAddressMode.ClampToEdge,
                ComparisonMode = ERHIComparisonMode.Never,
            };
        }

        public static RHITransferPassDescriptor TransferPass(string name)
        {
            return new RHITransferPassDescriptor
            {
                Name = name,
                Timestamp = null,
            };
        }

        public static RHIComputePassDescriptor ComputePass(string name)
        {
            return new RHIComputePassDescriptor
            {
                Name = name,
                Timestamp = null,
                Statistics = null,
            };
        }

        public static RHIMLPassDescriptor MLPass(string name)
        {
            return new RHIMLPassDescriptor
            {
                Name = name,
                Timestamp = null,
            };
        }
    }
}
