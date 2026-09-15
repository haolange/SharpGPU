using System;
using System.Collections.Generic;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

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

    internal static class MetalTextureViewPoolCapacity
    {
        internal static readonly uint[] Ladder = { 262144, 65536, 16384, 4096 };

        internal static uint SelectLockedCapacity(Func<uint, bool> tryCreate)
        {
            ArgumentNullException.ThrowIfNull(tryCreate);
            for (int i = 0; i < Ladder.Length; ++i)
            {
                uint capacity = Ladder[i];
                if (tryCreate(capacity))
                {
                    return capacity;
                }
            }

            throw new InvalidOperationException(
                "Failed to create MTLTextureViewPool at any locked capacity "
                + $"[{string.Join(", ", Ladder)}].");
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

    internal sealed class MetalTextureView : RHITextureView
    {
        public MetalTexture Texture => m_Texture;
        public MTLResourceID ResourceID => m_ResourceID;
        public MTLTexture ParentTexture => m_Texture.NativeTexture;
        public RHITextureViewDescriptor Descriptor => m_Descriptor;

        private readonly MetalTexture m_Texture;
        private readonly RHITextureViewDescriptor m_Descriptor;
        private readonly MetalTextureViewIndexLease m_PoolLease;
        private MTLResourceID m_ResourceID;

        public MetalTextureView(MetalTexture texture, in RHITextureViewDescriptor descriptor)
        {
            m_Texture = texture;
            m_Descriptor = descriptor;

            MetalDevice device = texture.MetalDevice;
            MTLTextureViewPool pool = device.TextureViewPool;
            m_PoolLease = device.AllocateTextureViewIndex();

            try
            {
                bool fullView = descriptor.BaseMipLevel == 0 &&
                                descriptor.BaseArraySlice == 0 &&
                                descriptor.MipCount >= texture.Descriptor.MipCount &&
                                descriptor.ArrayCount >= texture.Descriptor.Extent.z;

                if (fullView)
                {
                    m_ResourceID = pool.SetTextureView(texture.NativeTexture.NativePtr, m_PoolLease.Index);
                    return;
                }

                MTLTextureViewDescriptor viewDescriptor = MTLTextureViewDescriptor.New();
                try
                {
                    viewDescriptor.PixelFormat = MetalUtility.ConvertToMetalPixelFormat(texture.Descriptor.Format);
                    viewDescriptor.TextureType = MetalUtility.ConvertToMetalTextureType(texture.Descriptor.Dimension);
                    viewDescriptor.LevelRange = new NSRange { location = descriptor.BaseMipLevel, length = descriptor.MipCount };
                    viewDescriptor.SliceRange = new NSRange { location = descriptor.BaseArraySlice, length = descriptor.ArrayCount };
                    m_ResourceID = pool.SetTextureView(texture.NativeTexture.NativePtr, viewDescriptor.NativePtr, m_PoolLease.Index);
                }
                finally
                {
                    ObjectiveCRuntime.Release(viewDescriptor.NativePtr);
                }
            }
            catch
            {
                device.ReleaseTextureViewIndex(m_PoolLease);
                throw;
            }
        }

        protected override void Release()
        {
            m_Texture.MetalDevice.ReleaseTextureView(m_PoolLease);
            m_ResourceID = default;
        }
    }
}
