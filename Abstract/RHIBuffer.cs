using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBufferDescriptor
    {
        public int ByteSize;
        public ERHIBufferUsage UsageFlag;
        public ERHIStorageMode StorageMode;
    }

    public abstract class RHIBuffer : Disposal
    {
        public RHIBufferDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        protected RHIBufferDescriptor m_Descriptor;

        public abstract IntPtr Map(in uint readBegin, in uint readEnd);
        public abstract void UnMap(in uint writeBegin, in uint writeEnd);
        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
    }
}
