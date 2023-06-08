using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBufferDescriptor
    {
        public int Size;
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

        public EBufferState State
        {
            get
            {
                return m_State;
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
        protected EBufferState m_State;
        protected RHIBufferDescriptor m_Descriptor;

        public void SetState(in EBufferState state)
        {
            m_State = state;
        }

        public abstract IntPtr Map(in int length, in int offset);
        public abstract void UnMap();
        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
    }
}
