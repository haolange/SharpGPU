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

    public struct RHITensorViewDescriptor
    {
        /// <summary>
        /// Additional byte offset relative to the parent tensor's absolute backing offset.
        /// </summary>
        public ulong Offset;
        public Memory<uint> Dimensions;
        public Memory<uint>? Strides;
    }

    public abstract class RHITensor : Disposal
    {
        public RHIMLTensorDescriptor Descriptor => m_Descriptor;

        protected RHIMLTensorDescriptor m_Descriptor;

        public abstract RHITensorView CreateView(in RHITensorViewDescriptor descriptor);
    }

    public abstract class RHITensorView : Disposal
    {
        public RHITensor Parent => m_Parent ?? throw new InvalidOperationException("Tensor view parent is unavailable.");
        public RHIMLTensorDescriptor Descriptor => m_Descriptor;
        public RHITensorViewDescriptor ViewDescriptor => m_ViewDescriptor;

        protected RHITensor? m_Parent;
        protected RHIMLTensorDescriptor m_Descriptor;
        protected RHITensorViewDescriptor m_ViewDescriptor;
    }
}
