using SharpGPU.Core;
using SharpGPU.Mathematics;
using System.Collections.Generic;
using System.Threading;
using System;

namespace SharpGPU
{
    public enum ERHIResourceAllocationMode : byte
    {
        Committed,
        Placed,
        Sparse,
        External,
    }

    internal enum ERHIMemoryResourceKind : byte
    {
        Buffer,
        Texture,
    }

    /// <summary>
    /// Backend-produced allocation facts for one resource descriptor. The compatibility token is
    /// intentionally opaque: callers can size and align allocations without learning a Vulkan
    /// memory-type index or another backend-private memory class.
    /// </summary>
    public readonly struct RHIResourceMemoryRequirements
    {
        public ulong Size { get; }
        public ulong Alignment { get; }
        public ERHIStorageMode StorageMode { get; }

        internal object? OwnerDevice { get; }
        internal ulong CompatibilityMask { get; }
        internal ERHIMemoryResourceKind ResourceKind { get; }
        internal ulong NativeAllocationFlags { get; }
        internal bool IsValid =>
            OwnerDevice != null &&
            Size != 0 &&
            Alignment != 0 &&
            (Alignment & (Alignment - 1)) == 0 &&
            CompatibilityMask != 0;

        internal RHIResourceMemoryRequirements(
            object ownerDevice,
            ulong size,
            ulong alignment,
            ERHIStorageMode storageMode,
            ulong compatibilityMask,
            ERHIMemoryResourceKind resourceKind,
            ulong nativeAllocationFlags = 0)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            if (size == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Resource allocation size must be non-zero.");
            }

            if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Resource alignment must be a non-zero power of two.");
            }

            if (compatibilityMask == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(compatibilityMask), "A resource must expose at least one compatible native memory class.");
            }

            Size = size;
            Alignment = alignment;
            StorageMode = storageMode;
            CompatibilityMask = compatibilityMask;
            ResourceKind = resourceKind;
            NativeAllocationFlags = nativeAllocationFlags;
        }
    }

    /// <summary>
    /// Describes a caller-owned native heap. Compatibility is obtained from a real resource
    /// requirements query; this keeps backend memory classes opaque while making heap selection
    /// deterministic.
    /// </summary>
    public readonly struct RHIHeapDescription
    {
        public ulong Size { get; }
        public RHIResourceMemoryRequirements Compatibility { get; }
        public ERHIStorageMode StorageMode => Compatibility.StorageMode;

        public RHIHeapDescription(ulong size, in RHIResourceMemoryRequirements compatibility)
        {
            if (!compatibility.IsValid)
            {
                throw new ArgumentException(
                    "Heap compatibility must come from a successful resource memory-requirements query.",
                    nameof(compatibility));
            }

            if (size < compatibility.Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    "Heap size must be at least the queried resource allocation size.");
            }

            if ((size & (compatibility.Alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    $"Heap size must be aligned to {compatibility.Alignment} bytes.");
            }

            Size = size;
            Compatibility = compatibility;
        }
    }

    public readonly struct RHIMemoryBudget
    {
        public ERHIStorageMode StorageMode { get; }
        public ulong BudgetBytes { get; }
        public ulong UsageBytes { get; }
        public ulong CurrentReservationBytes { get; }
        public ulong AvailableBytes => UsageBytes >= BudgetBytes ? 0 : BudgetBytes - UsageBytes;

        internal RHIMemoryBudget(
            ERHIStorageMode storageMode,
            ulong budgetBytes,
            ulong usageBytes,
            ulong currentReservationBytes = 0)
        {
            if (budgetBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes), "A native memory budget must be non-zero.");
            }

            StorageMode = storageMode;
            BudgetBytes = budgetBytes;
            UsageBytes = usageBytes;
            CurrentReservationBytes = currentReservationBytes;
        }
    }

    public enum ERHIResidencyOperation : byte
    {
        MakeResident,
        Evict,
    }

    public readonly struct RHIResidencyRequestDescriptor
    {
        public ERHIResidencyOperation Operation { get; }
        public ReadOnlyMemory<RHIHeap> Heaps { get; }
        public RHIFence CompletionFence { get; }

        public RHIResidencyRequestDescriptor(
            ERHIResidencyOperation operation,
            ReadOnlyMemory<RHIHeap> heaps,
            RHIFence completionFence)
        {
            Operation = operation;
            Heaps = heaps;
            CompletionFence = completionFence ?? throw new ArgumentNullException(nameof(completionFence));
        }
    }

    /// <summary>
    /// Owns one native memory allocation. Disposing a heap while placed resources still reference
    /// it fails before native destruction and leaves the heap usable. Sparse bindings are not
    /// retained by the HAL; callers must complete queue-ordered unbinds before disposing their
    /// physical heaps.
    /// </summary>
    public abstract class RHIHeap : Disposal
    {
        public RHIHeapDescription Descriptor
        {
            get
            {
                ThrowIfDisposed();
                return m_Descriptor;
            }
        }

        internal object OwnerDevice => m_OwnerDevice;
        internal ulong CompatibilityBit => m_CompatibilityBit;

        private readonly object m_OwnerDevice;
        private readonly ulong m_CompatibilityBit;
        private readonly RHIHeapDescription m_Descriptor;
        private readonly RHIHeapPlacementRegistry m_Placements;

        protected RHIHeap(
            object ownerDevice,
            in RHIHeapDescription descriptor,
            ulong compatibilityBit)
        {
            m_OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            if (!ReferenceEquals(descriptor.Compatibility.OwnerDevice, ownerDevice))
            {
                throw new ArgumentException(
                    "Heap compatibility requirements were produced by a different device.",
                    nameof(descriptor));
            }

            if (compatibilityBit == 0 ||
                (descriptor.Compatibility.CompatibilityMask & compatibilityBit) == 0)
            {
                throw new ArgumentException(
                    "The selected native memory class is incompatible with the heap requirements.",
                    nameof(compatibilityBit));
            }

            m_Descriptor = descriptor;
            m_CompatibilityBit = compatibilityBit;
            m_Placements = new RHIHeapPlacementRegistry(descriptor.Size);
        }

        internal RHIHeapPlacement ReservePlacement(
            ulong offset,
            in RHIResourceMemoryRequirements requirements)
        {
            ThrowIfDisposed();
            if (!requirements.IsValid ||
                !ReferenceEquals(requirements.OwnerDevice, m_OwnerDevice))
            {
                throw new ArgumentException(
                    "Placed-resource requirements were not produced by this heap's device.",
                    nameof(requirements));
            }

            if (requirements.StorageMode != m_Descriptor.StorageMode)
            {
                throw new ArgumentException(
                    "Placed-resource storage mode does not match the heap storage mode.",
                    nameof(requirements));
            }
            if (requirements.ResourceKind !=
                m_Descriptor.Compatibility.ResourceKind)
            {
                throw new ArgumentException(
                    "Placed resources cannot change the heap's native resource class.",
                    nameof(requirements));
            }
            if ((requirements.NativeAllocationFlags &
                 ~m_Descriptor.Compatibility.NativeAllocationFlags) != 0)
            {
                throw new ArgumentException(
                    "The heap was not created with all native allocation flags required by the resource.",
                    nameof(requirements));
            }

            if ((requirements.CompatibilityMask & m_CompatibilityBit) == 0)
            {
                throw new ArgumentException(
                    "The resource is incompatible with the heap's native memory class.",
                    nameof(requirements));
            }

            if ((offset & (requirements.Alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Heap offset must be aligned to {requirements.Alignment} bytes.");
            }

            return m_Placements.Reserve(offset, requirements.Size);
        }

        internal void ValidateSparseSpan(
            ulong offset,
            ulong size,
            in RHIResourceMemoryRequirements requirements)
        {
            ThrowIfDisposed();
            if (!requirements.IsValid ||
                !ReferenceEquals(requirements.OwnerDevice, m_OwnerDevice))
            {
                throw new ArgumentException(
                    "Sparse requirements were not produced by this heap's device.",
                    nameof(requirements));
            }
            if (requirements.ResourceKind != ERHIMemoryResourceKind.Texture)
            {
                throw new ArgumentException(
                    "Sparse texture mappings require texture-compatible physical memory.",
                    nameof(requirements));
            }
            if (requirements.StorageMode != m_Descriptor.StorageMode ||
                (requirements.CompatibilityMask & m_CompatibilityBit) == 0)
            {
                throw new ArgumentException(
                    "The sparse texture is incompatible with the heap's native memory class.",
                    nameof(requirements));
            }
            if ((requirements.NativeAllocationFlags &
                 ~m_Descriptor.Compatibility.NativeAllocationFlags) != 0)
            {
                throw new ArgumentException(
                    "The heap lacks native allocation flags required by the sparse texture.",
                    nameof(requirements));
            }
            if (size == 0 || (offset & (requirements.Alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Sparse heap spans must be non-empty and aligned to {requirements.Alignment} bytes.");
            }

            ulong end;
            try
            {
                end = checked(offset + size);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    offset,
                    "Sparse heap span overflows the address space.");
            }
            if (end > m_Descriptor.Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Sparse heap span [{offset}, {end}) exceeds heap size {m_Descriptor.Size}.");
            }
        }

        protected override void ValidateCanDispose()
        {
            m_Placements.PrepareForOwnerDisposal();
        }
    }

    internal sealed class RHIHeapPlacement : IDisposable
    {
        private RHIHeapPlacementRegistry? m_Owner;
        private readonly long m_Id;

        internal RHIHeapPlacement(RHIHeapPlacementRegistry owner, long id)
        {
            m_Owner = owner;
            m_Id = id;
        }

        public void Dispose()
        {
            RHIHeapPlacementRegistry? owner = Interlocked.Exchange(ref m_Owner, null);
            owner?.Release(m_Id);
        }
    }

    internal sealed class RHIHeapPlacementRegistry
    {
        private readonly struct Placement
        {
            internal long Id { get; }
            internal ulong Start { get; }
            internal ulong End { get; }

            internal Placement(long id, ulong start, ulong end)
            {
                Id = id;
                Start = start;
                End = end;
            }
        }

        private readonly object m_Sync = new object();
        private readonly ulong m_HeapSize;
        private readonly List<Placement> m_Placements = new List<Placement>();
        private long m_NextId;
        private bool m_IsClosed;

        internal RHIHeapPlacementRegistry(ulong heapSize)
        {
            m_HeapSize = heapSize;
        }

        internal RHIHeapPlacement Reserve(ulong offset, ulong size)
        {
            ulong end;
            try
            {
                end = checked(offset + size);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    offset,
                    "Placed-resource range overflows the address space.");
            }

            if (end > m_HeapSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    $"Placed-resource range [{offset}, {end}) exceeds heap size {m_HeapSize}.");
            }

            lock (m_Sync)
            {
                if (m_IsClosed)
                {
                    throw new ObjectDisposedException(
                        nameof(RHIHeap),
                        "The heap is being disposed and cannot accept new placed resources.");
                }
                for (int i = 0; i < m_Placements.Count; ++i)
                {
                    Placement existing = m_Placements[i];
                    if (offset < existing.End && existing.Start < end)
                    {
                        throw new InvalidOperationException(
                            $"Placed-resource range [{offset}, {end}) overlaps active range [{existing.Start}, {existing.End}).");
                    }
                }

                long id = checked(++m_NextId);
                m_Placements.Add(new Placement(id, offset, end));
                return new RHIHeapPlacement(this, id);
            }
        }

        internal void Release(long id)
        {
            lock (m_Sync)
            {
                for (int i = 0; i < m_Placements.Count; ++i)
                {
                    if (m_Placements[i].Id == id)
                    {
                        m_Placements.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        internal void PrepareForOwnerDisposal()
        {
            lock (m_Sync)
            {
                if (m_Placements.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"The heap cannot be disposed while {m_Placements.Count} placed resource(s) remain active.");
                }

                // Reserve and close share this lock: either a placement becomes active first and
                // disposal fails, or disposal closes the registry before another reserve can win.
                m_IsClosed = true;
            }
        }

    }
}

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
