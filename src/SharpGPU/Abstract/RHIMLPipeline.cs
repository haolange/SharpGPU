using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIMLTensorDescriptor
    {
        public ERHIMLDataType DataType;
        public Memory<uint> Dimensions;
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
