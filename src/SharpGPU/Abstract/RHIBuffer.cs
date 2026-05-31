using System;
using SharpGPU.Core;

namespace SharpGPU
{
    public struct RHIBufferDescriptor
    {
        public int ByteSize;
        public ERHIBufferFormat Format;
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
