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
                ThrowIfDisposed(); return m_Descriptor;
            }
        }
        public ERHIResourceAllocationMode AllocationMode
        {
            get
            {
                ThrowIfDisposed();
                return m_AllocationMode;
            }
        }

        protected RHIBufferDescriptor m_Descriptor;
        protected ERHIResourceAllocationMode m_AllocationMode = ERHIResourceAllocationMode.Committed;

        public abstract IntPtr Map(in uint readBegin, in uint readEnd);
        public abstract void UnMap(in uint writeBegin, in uint writeEnd);
        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
    }
}
