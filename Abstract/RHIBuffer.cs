using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBufferDescriptor
    {
        public int Size;
        public EBufferState State;
        public EBufferUsage Usage;
        public EStorageMode StorageMode;
    }

    public abstract class RHIBuffer : Disposal
    {
        public uint SizeInBytes
        {
            get
            {
                return m_SizeInBytes;
            }
        }

        public RHIBufferDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        protected uint m_SizeInBytes;
        protected RHIBufferDescriptor m_Descriptor;

        public abstract IntPtr Map(in int length, in int offset);
        public abstract void UnMap();
        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
    }
}
