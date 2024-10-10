using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIResourceTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIPipelineStage Visible;
    }

    public struct RHIResourceTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIResourceTableLayoutElement> Elements;
    }

    public abstract class RHIResourceTableLayout : Disposal
    {

    }

    public struct RHIResourceTableElement
    {
        //public int Slot;
        //public ERHIBindType BindType;
        public RHISampler Sampler;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHITopLevelAccelStruct AccelStruct;
    }

    public struct RHIResourceTableDescriptor
    {
        public RHIResourceTableLayout Layout;
        public Memory<RHIResourceTableElement> Elements;
    }

    public abstract class RHIResourceTable : Disposal
    {
        public abstract void SetBindElement(in RHIResourceTableElement element, in ERHIBindType bindType, in int slot);
    }
}
