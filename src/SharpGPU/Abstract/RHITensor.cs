using System;
using SharpGPU.Core;

namespace SharpGPU
{
    public struct RHIMLTensorDescriptor
    {
        public ERHIMLDataType DataType;
        public ERHITensorUsage UsageFlag;
        public ERHIStorageMode StorageMode;
        public Memory<uint> Dimensions;
        public Memory<uint>? Strides;
        public RHIBuffer? BackingBuffer;
        public ulong BackingBufferOffset;
    }

    public abstract class RHITensor : Disposal
    {
        public RHIMLTensorDescriptor Descriptor => m_Descriptor;

        protected RHIMLTensorDescriptor m_Descriptor;
    }
}
