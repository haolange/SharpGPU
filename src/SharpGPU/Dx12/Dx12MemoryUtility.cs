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
