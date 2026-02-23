using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIMLTensorDescriptor
    {
        public ERHIMLDataType DataType;
        public ERHITensorUsage UsageFlag;
        public ERHIStorageMode StorageMode;
        public Memory<uint> Dimensions;
        public Memory<uint>? Strides;
    }

    public abstract class RHITensor : Disposal
    {
        public RHIMLTensorDescriptor Descriptor => m_Descriptor;

        protected RHIMLTensorDescriptor m_Descriptor;
    }

    public struct RHIMLPipelineDescriptor
    {
        public string Name;
        public RHIFunction Function;
        public Memory<RHIMLTensorDescriptor> InputTensors;
    }

    public abstract class RHIMLPipeline : Disposal
    {
        public ulong IntermediatesHeapSize => m_IntermediatesHeapSize;

        protected ulong m_IntermediatesHeapSize;
    }
}
