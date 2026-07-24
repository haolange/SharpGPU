using System;
using System.Collections.Generic;

namespace SharpGPU
{
    internal readonly struct MetalTextureViewIndexLease
    {
        internal uint Index { get; }
        internal ulong Generation { get; }

        internal MetalTextureViewIndexLease(uint index, ulong generation)
        {
            Index = index;
            Generation = generation;
        }
    }

    internal sealed class MetalTextureViewIndexAllocator
    {
        internal const uint DefaultCapacity = 4096;

        internal uint Capacity { get; }

        private readonly object m_Sync = new();
        private readonly Stack<uint> m_FreeIndices = new();
        private readonly ulong[] m_Generations;
        private readonly bool[] m_IsAllocated;
        private uint m_NextIndex;

        internal MetalTextureViewIndexAllocator(uint capacity = DefaultCapacity)
        {
            if (capacity == 0 || capacity > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    $"Metal texture-view-pool capacity must be in [1, {int.MaxValue}].");
            }

            Capacity = capacity;
            m_Generations = new ulong[checked((int)capacity)];
            m_IsAllocated = new bool[checked((int)capacity)];
        }

        internal MetalTextureViewIndexLease Allocate()
        {
            lock (m_Sync)
            {
                uint index;
                if (m_FreeIndices.Count != 0)
                {
                    index = m_FreeIndices.Pop();
                }
                else
                {
                    if (m_NextIndex >= Capacity)
                    {
                        throw new InvalidOperationException(
                            $"Metal texture view pool is exhausted ({Capacity} live views). Dispose unused texture views before creating more.");
                    }

                    index = m_NextIndex++;
                }

                int arrayIndex = checked((int)index);
                if (m_Generations[arrayIndex] == ulong.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Metal texture view index {index} exhausted its lease generation space.");
                }

                ulong generation = ++m_Generations[arrayIndex];
                m_IsAllocated[arrayIndex] = true;
                return new MetalTextureViewIndexLease(index, generation);
            }
        }

        internal bool Release(in MetalTextureViewIndexLease lease)
        {
            if (lease.Index >= Capacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lease),
                    lease.Index,
                    $"Metal texture view index must be in [0, {Capacity}).");
            }

            lock (m_Sync)
            {
                int index = checked((int)lease.Index);
                if (!m_IsAllocated[index] || m_Generations[index] != lease.Generation)
                {
                    return false;
                }

                m_IsAllocated[index] = false;
                m_FreeIndices.Push(lease.Index);
                return true;
            }
        }
    }
}
