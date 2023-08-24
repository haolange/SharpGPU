using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindTableElement
    {
        //public int Slot;
        //public EBindType BindType;
        public RHISampler Sampler;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHITopLevelAccelStruct AccelStruct;
    }

    public struct RHIBindTableDescriptor
    {
        public RHIBindTableLayout Layout;
        public Memory<RHIBindTableElement> Elements;
    }

    public abstract class RHIBindTable : Disposal
    {
        public abstract void SetBindElement(in RHIBindTableElement element, in EBindType bindType, in int slot);
    }
}
