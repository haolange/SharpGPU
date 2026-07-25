using System;
using SharpGPU.Mathematics;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal static unsafe class Dx12SparseMemoryUtility
    {
        internal const ulong TileSizeBytes = 64UL * 1024UL;

        internal static void ValidateDescriptor(in RHITextureDescriptor descriptor)
        {
            _ = Dx12MemoryUtility.BuildTextureDescription(descriptor);
            if (descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new NotSupportedException("DX12 sparse textures require GPU-local storage.");
            }
            if (descriptor.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException("DX12 sparse textures currently require one sample per texel.");
            }
            if (RHIBarrierUtility.InferAspectMask(descriptor.Format) !=
                ERHITextureAspectMask.Color)
            {
                throw new NotSupportedException(
                    "DX12 sparse texture mapping currently exposes exact color-plane tiling only.");
            }
            if (descriptor.Dimension is
                ERHITextureDimension.Texture2DMS or
                ERHITextureDimension.Texture2DArrayMS or
                ERHITextureDimension.TextureCube or
                ERHITextureDimension.TextureCubeArray)
            {
                throw new NotSupportedException(
                    "DX12 sparse texture mapping supports 2D, 2D-array, and 3D color textures.");
            }
        }

        internal static Vortice.Direct3D12.ID3D12Resource CreateReservedTexture(
            Dx12Device device,
            in RHITextureDescriptor descriptor)
        {
            ValidateDescriptor(descriptor);
            Vortice.Direct3D12.ResourceDescription nativeDescriptor =
                Dx12MemoryUtility.BuildTextureDescription(descriptor);
            nativeDescriptor.Layout =
                Vortice.Direct3D12.TextureLayout.UndefinedSwizzle64kb;
            SharpGen.Runtime.Result result = device.NativeDevice.CreateReservedResource(
                nativeDescriptor,
                Vortice.Direct3D12.ResourceStates.Common,
                out Vortice.Direct3D12.ID3D12Resource? resource);
            if (result.Failure || resource == null)
            {
                resource?.Release();
                throw new RHIException(
                    result.Code == unchecked((int)0x8007000E)
                        ? ERHIErrorCode.OutOfMemory
                        : ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    result.Code,
                    "ID3D12Device::CreateReservedResource failed for the sparse texture.",
                    ERHIDeviceState.Operational);
            }

            return resource;
        }

        internal static RHISparseTextureMemoryRequirements QueryRequirements(
            Dx12Device device,
            in RHITextureDescriptor descriptor,
            Vortice.Direct3D12.ID3D12Resource resource)
        {
            ValidateDescriptor(descriptor);
            uint arrayLayerCount = GetArrayLayerCount(descriptor);
            uint subresourceCount = checked(descriptor.MipCount * arrayLayerCount);
            Vortice.Direct3D12.SubresourceTiling[] nativeTilings =
                new Vortice.Direct3D12.SubresourceTiling[checked((int)subresourceCount)];
            uint tilingCount = subresourceCount;
            uint totalTileCount;
            Vortice.Direct3D12.PackedMipInfo packedMips;
            Vortice.Direct3D12.TileShape tileShape;
            uint* tilingCountPointer = &tilingCount;
            device.NativeDevice.GetResourceTiling(
                resource,
                out totalTileCount,
                out packedMips,
                out tileShape,
                (IntPtr)tilingCountPointer,
                0,
                nativeTilings);

            if (totalTileCount == 0)
            {
                throw new NotSupportedException(
                    "DX12 returned no physical tiles for the reserved texture.");
            }
            if (tileShape.WidthInTexels == 0 ||
                tileShape.HeightInTexels == 0 ||
                tileShape.DepthInTexels == 0)
            {
                throw new NotSupportedException(
                    "The DX12 texture has no standard sparse mip and therefore no exact public tile shape.");
            }

            int standardCount = 0;
            for (int i = 0; i < nativeTilings.Length; ++i)
            {
                if (nativeTilings[i].StartTileIndexInOverallResource != uint.MaxValue)
                {
                    ++standardCount;
                }
            }

            RHISparseTextureSubresourceTiling[] subresources =
                new RHISparseTextureSubresourceTiling[standardCount];
            int destination = 0;
            for (uint subresource = 0; subresource < subresourceCount; ++subresource)
            {
                Vortice.Direct3D12.SubresourceTiling tiling =
                    nativeTilings[checked((int)subresource)];
                if (tiling.StartTileIndexInOverallResource == uint.MaxValue)
                {
                    continue;
                }

                subresources[destination++] =
                    new RHISparseTextureSubresourceTiling(
                        ERHITextureAspectMask.Color,
                        subresource % descriptor.MipCount,
                        subresource / descriptor.MipCount,
                        new uint3(
                            tiling.WidthInTiles,
                            tiling.HeightInTiles,
                            tiling.DepthInTiles));
            }

            RHISparseTextureMipTail[] mipTails;
            if (packedMips.NumPackedMips == 0)
            {
                mipTails = Array.Empty<RHISparseTextureMipTail>();
            }
            else
            {
                mipTails = new[]
                {
                    new RHISparseTextureMipTail(
                        0,
                        ERHITextureAspectMask.Color,
                        packedMips.NumStandardMips,
                        0,
                        arrayLayerCount,
                        checked((ulong)packedMips.StartTileIndexInOverallResource * TileSizeBytes),
                        checked((ulong)packedMips.NumTilesForPackedMips * TileSizeBytes)),
                };
            }

            Vortice.Direct3D12.ResourceDescription nativeDescriptor =
                Dx12MemoryUtility.BuildTextureDescription(descriptor);
            RHIResourceMemoryRequirements heapCompatibility =
                new RHIResourceMemoryRequirements(
                    device,
                    TileSizeBytes,
                    TileSizeBytes,
                    descriptor.StorageMode,
                    Dx12MemoryUtility.GetCompatibilityMask(nativeDescriptor),
                    ERHIMemoryResourceKind.Texture);
            return new RHISparseTextureMemoryRequirements(
                device,
                descriptor,
                checked((ulong)totalTileCount * TileSizeBytes),
                TileSizeBytes,
                new uint3(
                    tileShape.WidthInTexels,
                    tileShape.HeightInTexels,
                    tileShape.DepthInTexels),
                heapCompatibility,
                subresources,
                mipTails);
        }

        internal static uint GetArrayLayerCount(in RHITextureDescriptor descriptor)
        {
            return descriptor.Dimension switch
            {
                ERHITextureDimension.Texture2DArray => descriptor.Extent.z,
                _ => 1,
            };
        }
    }
#pragma warning restore CA1416
}
