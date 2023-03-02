using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindGroupElement
    {
        //public int Slot;
        //public EBindType BindType;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHISamplerState SamplerState;
    }

    public struct RHIBindGroupDescriptor
    {
        public RHIBindGroupLayout Layout;
        public Memory<RHIBindGroupElement> Elements;
    }

    public abstract class RHIBindGroup : Disposal
    {
        public abstract void SetBindElement(in RHIBindGroupElement element, in EBindType bindType, in int slot);
    }
}
