using System;
using System.Collections.Generic;
using System.Threading;
using SharpGPU.Core;

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
