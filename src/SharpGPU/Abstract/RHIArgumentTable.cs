using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIArgumentTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIShaderStage Stage;
    }

    public struct RHIArgumentTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIArgumentTableLayoutElement> Elements;
    }

    public abstract class RHIArgumentTableLayout : Disposal
    {

    }

    public struct RHIArgumentTableElement
    {
        //public int Slot;
        //public ERHIBindType BindType;
        public RHISampler Sampler;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHITopLevelAccelStruct AccelStruct;
    }

    public struct RHIArgumentTableDescriptor
    {
        public RHIArgumentTableLayout Layout;
        public Memory<RHIArgumentTableElement> Elements;
    }

    public abstract class RHIArgumentTable : Disposal
    {
        public abstract void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot);
        public abstract void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex);
    }
}
