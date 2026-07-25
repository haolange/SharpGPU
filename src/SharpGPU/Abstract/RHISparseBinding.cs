using System;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public enum ERHISparseBindingOperation : byte
    {
        Bind,
        Unbind,
    }

    public readonly struct RHISparseTextureSubresourceTiling
    {
        public ERHITextureAspectMask Aspect { get; }
        public uint MipLevel { get; }
        public uint ArrayLayer { get; }
        public uint3 TileCount { get; }

        internal RHISparseTextureSubresourceTiling(
            ERHITextureAspectMask aspect,
            uint mipLevel,
            uint arrayLayer,
            in uint3 tileCount)
        {
            Aspect = aspect;
            MipLevel = mipLevel;
            ArrayLayer = arrayLayer;
            TileCount = tileCount;
        }
    }

    public readonly struct RHISparseTextureMipTail
    {
        public uint Index { get; }
        public ERHITextureAspectMask Aspect { get; }
        public uint FirstMipLevel { get; }
        public uint FirstArrayLayer { get; }
        public uint ArrayLayerCount { get; }
        public ulong VirtualOffsetBytes { get; }
        public ulong SizeBytes { get; }

        internal RHISparseTextureMipTail(
            uint index,
            ERHITextureAspectMask aspect,
            uint firstMipLevel,
            uint firstArrayLayer,
            uint arrayLayerCount,
            ulong virtualOffsetBytes,
            ulong sizeBytes)
        {
            Index = index;
            Aspect = aspect;
            FirstMipLevel = firstMipLevel;
            FirstArrayLayer = firstArrayLayer;
            ArrayLayerCount = arrayLayerCount;
            VirtualOffsetBytes = virtualOffsetBytes;
            SizeBytes = sizeBytes;
        }
    }

    /// <summary>
    /// Exact native sparse-image allocation facts for one texture descriptor. The physical heap
    /// compatibility describes one sparse tile, while <see cref="VirtualSizeBytes"/> describes
    /// the complete virtual resource. Callers own physical allocation and mapping policy.
    /// </summary>
    public sealed class RHISparseTextureMemoryRequirements
    {
        private readonly RHISparseTextureSubresourceTiling[] m_Subresources;
        private readonly RHISparseTextureMipTail[] m_MipTails;

        public ulong VirtualSizeBytes { get; }
        public ulong TileSizeBytes { get; }
        public uint3 TileExtent { get; }
        public RHIResourceMemoryRequirements HeapCompatibility { get; }
        public ReadOnlyMemory<RHISparseTextureSubresourceTiling> Subresources =>
            m_Subresources;
        public ReadOnlyMemory<RHISparseTextureMipTail> MipTails => m_MipTails;

        internal object OwnerDevice { get; }
        internal RHITextureDescriptor TextureDescriptor { get; }

        internal RHISparseTextureMemoryRequirements(
            object ownerDevice,
            in RHITextureDescriptor textureDescriptor,
            ulong virtualSizeBytes,
            ulong tileSizeBytes,
            in uint3 tileExtent,
            in RHIResourceMemoryRequirements heapCompatibility,
            RHISparseTextureSubresourceTiling[] subresources,
            RHISparseTextureMipTail[] mipTails)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            if (virtualSizeBytes == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(virtualSizeBytes),
                    "Sparse virtual size must be non-zero.");
            }
            if (tileSizeBytes == 0 ||
                (tileSizeBytes & (tileSizeBytes - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileSizeBytes),
                    "Sparse tile size must be a non-zero power of two.");
            }
            if (tileExtent.x == 0 || tileExtent.y == 0 || tileExtent.z == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileExtent),
                    "Sparse tile texel extent must be non-zero.");
            }
            if (!heapCompatibility.IsValid ||
                !ReferenceEquals(heapCompatibility.OwnerDevice, ownerDevice) ||
                heapCompatibility.ResourceKind != ERHIMemoryResourceKind.Texture ||
                heapCompatibility.Size != tileSizeBytes ||
                heapCompatibility.Alignment != tileSizeBytes)
            {
                throw new ArgumentException(
                    "Sparse heap compatibility must describe one texture tile on the same device.",
                    nameof(heapCompatibility));
            }

            ArgumentNullException.ThrowIfNull(subresources);
            ArgumentNullException.ThrowIfNull(mipTails);
            ValidateNativeTilingFacts(
                textureDescriptor,
                virtualSizeBytes,
                tileSizeBytes,
                subresources,
                mipTails);
            TextureDescriptor = textureDescriptor;
            VirtualSizeBytes = virtualSizeBytes;
            TileSizeBytes = tileSizeBytes;
            TileExtent = tileExtent;
            HeapCompatibility = heapCompatibility;
            m_Subresources = (RHISparseTextureSubresourceTiling[])subresources.Clone();
            m_MipTails = (RHISparseTextureMipTail[])mipTails.Clone();
        }

        private static void ValidateNativeTilingFacts(
            in RHITextureDescriptor textureDescriptor,
            ulong virtualSizeBytes,
            ulong tileSizeBytes,
            RHISparseTextureSubresourceTiling[] subresources,
            RHISparseTextureMipTail[] mipTails)
        {
            uint arrayLayerCount = textureDescriptor.Dimension switch
            {
                ERHITextureDimension.Texture2DArray or
                ERHITextureDimension.Texture2DArrayMS or
                ERHITextureDimension.TextureCubeArray =>
                    textureDescriptor.Extent.z,
                ERHITextureDimension.TextureCube => 6,
                _ => 1,
            };

            for (int i = 0; i < subresources.Length; ++i)
            {
                RHISparseTextureSubresourceTiling subresource =
                    subresources[i];
                RHISparseTextureTileBinding.ValidateSingleAspect(
                    subresource.Aspect);
                if (subresource.MipLevel >= textureDescriptor.MipCount ||
                    subresource.ArrayLayer >= arrayLayerCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(subresources),
                        $"Sparse subresource {i} exceeds the texture descriptor.");
                }
                if (subresource.TileCount.x == 0 ||
                    subresource.TileCount.y == 0 ||
                    subresource.TileCount.z == 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(subresources),
                        $"Sparse subresource {i} has an empty tile grid.");
                }
                for (int prior = 0; prior < i; ++prior)
                {
                    RHISparseTextureSubresourceTiling candidate =
                        subresources[prior];
                    if (candidate.Aspect == subresource.Aspect &&
                        candidate.MipLevel == subresource.MipLevel &&
                        candidate.ArrayLayer == subresource.ArrayLayer)
                    {
                        throw new ArgumentException(
                            $"Sparse subresource {i} duplicates subresource {prior}.",
                            nameof(subresources));
                    }
                }
            }

            for (int i = 0; i < mipTails.Length; ++i)
            {
                RHISparseTextureMipTail tail = mipTails[i];
                RHISparseTextureTileBinding.ValidateSingleAspect(
                    tail.Aspect);
                if (tail.Index != (uint)i)
                {
                    throw new ArgumentException(
                        "Sparse mip-tail indices must be dense and ordered.",
                        nameof(mipTails));
                }
                if (tail.FirstMipLevel >= textureDescriptor.MipCount ||
                    tail.ArrayLayerCount == 0 ||
                    (ulong)tail.FirstArrayLayer +
                        tail.ArrayLayerCount >
                        arrayLayerCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mipTails),
                        $"Sparse mip tail {i} exceeds the texture descriptor.");
                }
                if (tail.SizeBytes == 0 ||
                    (tail.VirtualOffsetBytes &
                        (tileSizeBytes - 1)) != 0 ||
                    (tail.SizeBytes &
                        (tileSizeBytes - 1)) != 0)
                {
                    throw new ArgumentException(
                        $"Sparse mip tail {i} is not tile aligned.",
                        nameof(mipTails));
                }

                ulong tailEnd;
                try
                {
                    tailEnd = checked(
                        tail.VirtualOffsetBytes +
                        tail.SizeBytes);
                }
                catch (OverflowException)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mipTails),
                        $"Sparse mip tail {i} overflows the virtual address space.");
                }
                if (tailEnd > virtualSizeBytes)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(mipTails),
                        $"Sparse mip tail {i} exceeds the virtual resource size.");
                }
                for (int prior = 0; prior < i; ++prior)
                {
                    RHISparseTextureMipTail candidate = mipTails[prior];
                    ulong candidateEnd = checked(
                        candidate.VirtualOffsetBytes +
                        candidate.SizeBytes);
                    if (tail.VirtualOffsetBytes < candidateEnd &&
                        candidate.VirtualOffsetBytes < tailEnd)
                    {
                        throw new ArgumentException(
                            $"Sparse mip tail {i} overlaps mip tail {prior}.",
                            nameof(mipTails));
                    }
                }
            }
        }

        internal RHISparseTextureSubresourceTiling GetSubresource(
            ERHITextureAspectMask aspect,
            uint mipLevel,
            uint arrayLayer)
        {
            for (int i = 0; i < m_Subresources.Length; ++i)
            {
                RHISparseTextureSubresourceTiling candidate = m_Subresources[i];
                if (candidate.Aspect == aspect &&
                    candidate.MipLevel == mipLevel &&
                    candidate.ArrayLayer == arrayLayer)
                {
                    return candidate;
                }
            }

            throw new ArgumentOutOfRangeException(
                nameof(mipLevel),
                "The requested subresource is not a standard sparse-tile subresource.");
        }

        internal RHISparseTextureMipTail GetMipTail(uint index)
        {
            if (index >= (uint)m_MipTails.Length ||
                m_MipTails[index].Index != index)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    "The requested sparse mip tail does not exist.");
            }

            return m_MipTails[index];
        }
    }

    public readonly struct RHISparseTextureTileBinding
    {
        public ERHISparseBindingOperation Operation { get; }
        public RHITexture Texture { get; }
        public ERHITextureAspectMask Aspect { get; }
        public uint MipLevel { get; }
        public uint ArrayLayer { get; }
        public uint3 TileOffset { get; }
        public uint3 TileExtent { get; }
        public RHIHeap? Heap { get; }
        public ulong HeapOffset { get; }

        public RHISparseTextureTileBinding(
            ERHISparseBindingOperation operation,
            RHITexture texture,
            ERHITextureAspectMask aspect,
            uint mipLevel,
            uint arrayLayer,
            in uint3 tileOffset,
            in uint3 tileExtent,
            RHIHeap? heap = null,
            ulong heapOffset = 0)
        {
            if (!Enum.IsDefined(operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown sparse binding operation.");
            }
            Texture = texture ?? throw new ArgumentNullException(nameof(texture));
            ValidateSingleAspect(aspect);
            if (tileExtent.x == 0 || tileExtent.y == 0 || tileExtent.z == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tileExtent),
                    "Sparse tile extent must be non-zero.");
            }
            if (operation == ERHISparseBindingOperation.Bind && heap == null)
            {
                throw new ArgumentNullException(nameof(heap), "A sparse bind requires a physical heap.");
            }
            if (operation == ERHISparseBindingOperation.Unbind &&
                (heap != null || heapOffset != 0))
            {
                throw new ArgumentException(
                    "A sparse unbind must not specify a heap or heap offset.",
                    nameof(heap));
            }

            Operation = operation;
            Aspect = aspect;
            MipLevel = mipLevel;
            ArrayLayer = arrayLayer;
            TileOffset = tileOffset;
            TileExtent = tileExtent;
            Heap = heap;
            HeapOffset = heapOffset;
        }

        internal static void ValidateSingleAspect(ERHITextureAspectMask aspect)
        {
            const ERHITextureAspectMask known =
                ERHITextureAspectMask.Color |
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil;
            if (aspect == ERHITextureAspectMask.None ||
                (aspect & ~known) != 0 ||
                (((byte)aspect & ((byte)aspect - 1)) != 0))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(aspect),
                    aspect,
                    "A sparse binding must identify exactly one known texture aspect.");
            }
        }
    }

    public readonly struct RHISparseTextureMipTailBinding
    {
        public ERHISparseBindingOperation Operation { get; }
        public RHITexture Texture { get; }
        public uint MipTailIndex { get; }
        public RHIHeap? Heap { get; }
        public ulong HeapOffset { get; }

        public RHISparseTextureMipTailBinding(
            ERHISparseBindingOperation operation,
            RHITexture texture,
            uint mipTailIndex,
            RHIHeap? heap = null,
            ulong heapOffset = 0)
        {
            if (!Enum.IsDefined(operation))
            {
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown sparse binding operation.");
            }
            Texture = texture ?? throw new ArgumentNullException(nameof(texture));
            if (operation == ERHISparseBindingOperation.Bind && heap == null)
            {
                throw new ArgumentNullException(nameof(heap), "A sparse mip-tail bind requires a physical heap.");
            }
            if (operation == ERHISparseBindingOperation.Unbind &&
                (heap != null || heapOffset != 0))
            {
                throw new ArgumentException(
                    "A sparse mip-tail unbind must not specify a heap or heap offset.",
                    nameof(heap));
            }

            Operation = operation;
            MipTailIndex = mipTailIndex;
            Heap = heap;
            HeapOffset = heapOffset;
        }
    }

    public readonly struct RHISparseBindDescriptor
    {
        public ReadOnlyMemory<RHISparseTextureTileBinding> TileBindings { get; }
        public ReadOnlyMemory<RHISparseTextureMipTailBinding> MipTailBindings { get; }
        public ReadOnlyMemory<RHISemaphore> WaitSemaphores { get; }
        public ReadOnlyMemory<RHISemaphore> SignalSemaphores { get; }
        public RHIFence? CompletionFence { get; }

        public RHISparseBindDescriptor(
            ReadOnlyMemory<RHISparseTextureTileBinding> tileBindings = default,
            ReadOnlyMemory<RHISparseTextureMipTailBinding> mipTailBindings = default,
            ReadOnlyMemory<RHISemaphore> waitSemaphores = default,
            ReadOnlyMemory<RHISemaphore> signalSemaphores = default,
            RHIFence? completionFence = null)
        {
            TileBindings = tileBindings;
            MipTailBindings = mipTailBindings;
            WaitSemaphores = waitSemaphores;
            SignalSemaphores = signalSemaphores;
            CompletionFence = completionFence;
        }
    }
}
