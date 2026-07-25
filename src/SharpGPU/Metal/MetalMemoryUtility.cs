using System;
using SharpMetal.Metal;

namespace SharpGPU
{
    internal static class MetalMemoryUtility
    {
        internal static MTLResourceOptions GetBufferOptions(in RHIBufferDescriptor descriptor)
        {
            if (descriptor.ByteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Buffer byte size must be greater than zero.");
            }
            ValidateStorageMode(descriptor.StorageMode, allowMemoryless: false);
            return MetalUtility.ConvertToMetalResourceOptions(descriptor.StorageMode);
        }

        internal static MTLTextureDescriptor BuildTextureDescriptor(
            in RHITextureDescriptor descriptor)
        {
            ValidateStorageMode(descriptor.StorageMode, allowMemoryless: true);
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

            MTLTextureDescriptor nativeDescriptor = MTLTextureDescriptor.New();
            nativeDescriptor.TextureType = MetalUtility.ConvertToMetalTextureType(descriptor.Dimension);
            nativeDescriptor.PixelFormat = MetalUtility.ConvertToMetalPixelFormat(descriptor.Format);
            nativeDescriptor.Width = descriptor.Extent.x;
            nativeDescriptor.Height = descriptor.Extent.y;
            nativeDescriptor.Depth =
                descriptor.Dimension == ERHITextureDimension.Texture3D
                    ? descriptor.Extent.z
                    : 1;
            nativeDescriptor.ArrayLength = descriptor.Dimension switch
            {
                ERHITextureDimension.Texture2DArray => descriptor.Extent.z,
                ERHITextureDimension.Texture2DArrayMS => descriptor.Extent.z,
                ERHITextureDimension.TextureCube => 6,
                ERHITextureDimension.TextureCubeArray => descriptor.Extent.z,
                _ => 1,
            };
            nativeDescriptor.MipmapLevelCount = descriptor.MipCount;
            nativeDescriptor.SampleCount = (ulong)descriptor.SampleCount;
            nativeDescriptor.Usage = MetalUtility.ConvertToMetalTextureUsage(descriptor.UsageFlag);
            nativeDescriptor.StorageMode = MetalUtility.ConvertToMetalStorageMode(descriptor.StorageMode);
            nativeDescriptor.CpuCacheMode =
                descriptor.StorageMode == ERHIStorageMode.HostUpload
                    ? MTLCPUCacheMode.WriteCombined
                    : MTLCPUCacheMode.DefaultCache;
            return nativeDescriptor;
        }

        private static void ValidateStorageMode(
            ERHIStorageMode storageMode,
            bool allowMemoryless)
        {
            if (!Enum.IsDefined(storageMode) ||
                storageMode == ERHIStorageMode.Pending ||
                (!allowMemoryless && storageMode == ERHIStorageMode.Memoryless))
            {
                throw new ArgumentOutOfRangeException(nameof(storageMode), storageMode, "Unsupported storage mode.");
            }
        }
    }
}
