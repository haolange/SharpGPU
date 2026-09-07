using SharpMath;
using System.Diagnostics;
using System;

namespace SharpGPU
{
    internal static class Dx12MemoryUtility
    {
        internal const ulong BufferCompatibility = 1UL << 0;
        internal const ulong NonRenderTargetTextureCompatibility = 1UL << 1;
        internal const ulong RenderTargetTextureCompatibility = 1UL << 2;

        internal static Vortice.Direct3D12.ResourceDescription BuildBufferDescription(
            in RHIBufferDescriptor descriptor)
        {
            if (descriptor.ByteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Buffer byte size must be greater than zero.");
            }

            ValidateStorageMode(descriptor.StorageMode, allowMemoryless: false);
            return Vortice.Direct3D12.ResourceDescription.Buffer(
                checked((ulong)descriptor.ByteSize),
                Dx12Utility.ConvertToDx12BufferFlag(descriptor.UsageFlag));
        }

        internal static Vortice.Direct3D12.ResourceDescription BuildTextureDescription(
            in RHITextureDescriptor descriptor)
        {
            ValidateStorageMode(descriptor.StorageMode, allowMemoryless: false);
            if (descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new NotSupportedException(
                    "DX12 textures require GPU-local storage in the current native resource contract.");
            }
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

            return new Vortice.Direct3D12.ResourceDescription
            {
                MipLevels = checked((ushort)descriptor.MipCount),
                Format = Dx12Utility.ConvertToDx12Format(descriptor.Format),
                Width = descriptor.Extent.x,
                Height = descriptor.Extent.y,
                DepthOrArraySize = checked((ushort)descriptor.Extent.z),
                Flags = Dx12Utility.ConvertToDx12TextureFlag(descriptor.UsageFlag),
                SampleDescription = Dx12Utility.ConvertToDx12SampleCount(descriptor.SampleCount),
                Dimension = Dx12Utility.ConvertToDx12TextureDimension(descriptor.Dimension),
            };
        }

        internal static ulong GetCompatibilityMask(
            in Vortice.Direct3D12.ResourceDescription descriptor)
        {
            if (descriptor.Dimension == Vortice.Direct3D12.ResourceDimension.Buffer)
            {
                return BufferCompatibility;
            }

            const Vortice.Direct3D12.ResourceFlags renderTargetFlags =
                Vortice.Direct3D12.ResourceFlags.AllowRenderTarget |
                Vortice.Direct3D12.ResourceFlags.AllowDepthStencil;
            return (descriptor.Flags & renderTargetFlags) != 0
                ? RenderTargetTextureCompatibility
                : NonRenderTargetTextureCompatibility;
        }

        internal static ulong SelectCompatibilityBit(ulong mask)
        {
            if (mask == BufferCompatibility ||
                mask == NonRenderTargetTextureCompatibility ||
                mask == RenderTargetTextureCompatibility)
            {
                return mask;
            }

            throw new ArgumentException("DX12 heap compatibility must identify exactly one resource class.", nameof(mask));
        }

        internal static Vortice.Direct3D12.HeapFlags GetHeapFlags(ulong compatibilityBit)
        {
            return compatibilityBit switch
            {
                BufferCompatibility => Vortice.Direct3D12.HeapFlags.AllowOnlyBuffers,
                NonRenderTargetTextureCompatibility => Vortice.Direct3D12.HeapFlags.AllowOnlyNonRenderTargetDepthStencilTextures,
                RenderTargetTextureCompatibility => Vortice.Direct3D12.HeapFlags.AllowOnlyRenderTargetDepthStencilTextures,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(compatibilityBit),
                    compatibilityBit,
                    "Unknown DX12 heap compatibility class."),
            };
        }

        private static void ValidateStorageMode(ERHIStorageMode storageMode, bool allowMemoryless)
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

namespace SharpGPU
{
    internal unsafe class Dx12Heap : RHIHeap
    {
        public Vortice.Direct3D12.ID3D12Heap NativeHeap
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeHeap;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Heap m_NativeHeap;

        public Dx12Heap(Dx12Device device, in RHIHeapDescription descriptor)
            : base(device, descriptor, Dx12MemoryUtility.SelectCompatibilityBit(descriptor.Compatibility.CompatibilityMask))
        {
            m_Dx12Device = device;

            Vortice.Direct3D12.HeapDescription heapDesc = new Vortice.Direct3D12.HeapDescription();
            heapDesc.SizeInBytes = descriptor.Size;
            heapDesc.Alignment = descriptor.Compatibility.Alignment;
            heapDesc.Properties.Type = Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode);
            heapDesc.Flags = Dx12MemoryUtility.GetHeapFlags(CompatibilityBit);

            Vortice.Direct3D12.ID3D12Heap? nativeHeap;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateHeap(heapDesc, out nativeHeap);
            m_NativeHeap = Dx12Utility.RequireCreatedObject(
                nativeHeap,
                hResult,
                "ID3D12Device.CreateHeap");
        }

        protected override void Release()
        {
            m_NativeHeap.Release();
        }
    }
}
