using SharpGPU.Mathematics;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using System;

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

namespace SharpGPU
{
    /// <summary>
    /// Metal placement-sparse resource mechanics. Allocation and mapping policy remain caller-owned.
    /// </summary>
    internal static class MetalSparseMemoryUtility
    {
        internal const ulong PlacementSparseCompatibilityFlag = 1UL;
        internal const MTLSparsePageSize SparsePageSize =
            MTLSparsePageSize.Size16;

        internal static bool RequiresPlacementSparseCompatibility(
            in RHIResourceMemoryRequirements requirements)
        {
            return requirements.NativeAllocationFlags ==
                PlacementSparseCompatibilityFlag;
        }

        internal static void ValidateDescriptor(
            MetalDevice device,
            in RHITextureDescriptor descriptor)
        {
            if (!device.SupportsPlacementSparse)
            {
                throw new NotSupportedException(
                    "This Metal device does not support placement sparse resources.");
            }
            if (descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new NotSupportedException(
                    "Metal placement sparse textures require GPU-local storage.");
            }
            if (descriptor.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException(
                    "Metal placement sparse textures currently require one sample per texel.");
            }
            if (descriptor.MipCount == 0 || descriptor.MipCount > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Metal placement sparse textures require between 1 and 32 mip levels.");
            }
            if (descriptor.Dimension is not (
                    ERHITextureDimension.Texture2D or
                    ERHITextureDimension.Texture3D))
            {
                throw new NotSupportedException(
                    "Metal placement sparse mapping currently exposes exact 2D and 3D texture mechanics.");
            }
            if (RHIBarrierUtility.InferAspectMask(descriptor.Format) !=
                ERHITextureAspectMask.Color)
            {
                throw new NotSupportedException(
                    "Metal placement sparse mapping currently exposes exact color-aspect tiling only.");
            }

            MTLTextureDescriptor nativeDescriptor =
                MetalMemoryUtility.BuildTextureDescriptor(descriptor);
            try
            {
                nativeDescriptor.PlacementSparsePageSize = SparsePageSize;
                MTLSize nativeTileSize = device.NativeDevice.SparseTileSize(
                    nativeDescriptor.TextureType,
                    nativeDescriptor.PixelFormat,
                    nativeDescriptor.SampleCount,
                    SparsePageSize);
                if (nativeTileSize.width == 0 ||
                    nativeTileSize.height == 0 ||
                    nativeTileSize.depth == 0)
                {
                    throw new NotSupportedException(
                        $"The Metal device does not expose sparse tile geometry for {descriptor.Dimension} {descriptor.Format}.");
                }
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }
        }

        internal static MTLTexture CreateSparseTexture(
            MetalDevice device,
            in RHITextureDescriptor descriptor)
        {
            ValidateDescriptor(device, descriptor);
            MTLTextureDescriptor nativeDescriptor =
                MetalMemoryUtility.BuildTextureDescriptor(descriptor);
            MTLTexture nativeTexture;
            try
            {
                nativeDescriptor.PlacementSparsePageSize = SparsePageSize;
                nativeTexture =
                    device.NativeDevice.NewTexture(nativeDescriptor);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }

            if (nativeTexture.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLDevice failed to create a placement sparse texture.",
                    ERHIDeviceState.Operational);
            }

            try
            {
                if (!nativeTexture.IsSparse ||
                    nativeTexture.SparseTextureTier ==
                        MTLTextureSparseTier.None)
                {
                    throw new NotSupportedException(
                        "Metal created a texture that does not expose native sparse-texture semantics.");
                }
            }
            catch
            {
                ObjectiveCRuntime.Release(nativeTexture.NativePtr);
                throw;
            }

            return nativeTexture;
        }

        internal static RHISparseTextureMemoryRequirements QueryRequirements(
            MetalDevice device,
            in RHITextureDescriptor descriptor,
            in MTLTexture nativeTexture)
        {
            ValidateDescriptor(device, descriptor);
            if (nativeTexture.NativePtr == IntPtr.Zero ||
                !nativeTexture.IsSparse ||
                nativeTexture.SparseTextureTier ==
                    MTLTextureSparseTier.None)
            {
                throw new ArgumentException(
                    "The native Metal texture is not a placement sparse texture.",
                    nameof(nativeTexture));
            }

            ulong tileSizeBytes =
                device.NativeDevice.SparseTileSizeInBytes(SparsePageSize);
            if (tileSizeBytes == 0 ||
                (tileSizeBytes & (tileSizeBytes - 1)) != 0)
            {
                throw new NotSupportedException(
                    "The Metal device reported an invalid sparse page size.");
            }

            MTLSize nativeTileSize = device.NativeDevice.SparseTileSize(
                nativeTexture.TextureType,
                nativeTexture.PixelFormat,
                nativeTexture.SampleCount,
                SparsePageSize);
            uint3 tileExtent = new uint3(
                checked((uint)nativeTileSize.width),
                checked((uint)nativeTileSize.height),
                checked((uint)nativeTileSize.depth));
            if (tileExtent.x == 0 ||
                tileExtent.y == 0 ||
                tileExtent.z == 0)
            {
                throw new NotSupportedException(
                    "The Metal device reported invalid sparse tile geometry.");
            }

            uint firstMipInTail = checked((uint)Math.Min(
                nativeTexture.FirstMipmapInTail,
                descriptor.MipCount));
            RHISparseTextureSubresourceTiling[] subresources =
                new RHISparseTextureSubresourceTiling[
                    checked((int)firstMipInTail)];
            ulong standardTileCount = 0;
            for (uint mip = 0; mip < firstMipInTail; ++mip)
            {
                uint mipWidth = MipExtent(descriptor.Extent.x, mip);
                uint mipHeight = MipExtent(descriptor.Extent.y, mip);
                uint mipDepth = descriptor.Dimension ==
                    ERHITextureDimension.Texture3D
                        ? MipExtent(descriptor.Extent.z, mip)
                        : 1;
                uint3 tileCount = new uint3(
                    DivideRoundUp(mipWidth, tileExtent.x),
                    DivideRoundUp(mipHeight, tileExtent.y),
                    DivideRoundUp(mipDepth, tileExtent.z));
                subresources[checked((int)mip)] =
                    new RHISparseTextureSubresourceTiling(
                        ERHITextureAspectMask.Color,
                        mip,
                        0,
                        tileCount);
                standardTileCount = checked(
                    standardTileCount +
                    (ulong)tileCount.x * tileCount.y * tileCount.z);
            }

            ulong standardBytes =
                checked(standardTileCount * tileSizeBytes);
            RHISparseTextureMipTail[] mipTails;
            ulong tailSizeBytes = 0;
            if (firstMipInTail < descriptor.MipCount)
            {
                tailSizeBytes = nativeTexture.TailSizeInBytes;
                if (tailSizeBytes == 0 ||
                    (tailSizeBytes & (tileSizeBytes - 1)) != 0)
                {
                    throw new NotSupportedException(
                        "The Metal sparse mip tail is not aligned to the selected sparse page size.");
                }
                mipTails = new[]
                {
                    new RHISparseTextureMipTail(
                        0,
                        ERHITextureAspectMask.Color,
                        firstMipInTail,
                        0,
                        1,
                        standardBytes,
                        tailSizeBytes),
                };
            }
            else
            {
                mipTails = Array.Empty<RHISparseTextureMipTail>();
            }

            ulong virtualSizeBytes = checked(
                standardBytes + tailSizeBytes);
            RHIResourceMemoryRequirements heapCompatibility =
                new RHIResourceMemoryRequirements(
                    device,
                    tileSizeBytes,
                    tileSizeBytes,
                    descriptor.StorageMode,
                    1UL,
                    ERHIMemoryResourceKind.Texture,
                    PlacementSparseCompatibilityFlag);
            return new RHISparseTextureMemoryRequirements(
                device,
                descriptor,
                virtualSizeBytes,
                tileSizeBytes,
                tileExtent,
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
