using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHISamplerStateDescriptor
    {
        public uint Anisotropy;
        public float LodMinClamp;
        public float LodMaxClamp;
        public EFilterMode MagFilter;
        public EFilterMode MinFilter;
        public EFilterMode MipFilter;
        public EAddressMode AddressModeU;
        public EAddressMode AddressModeV;
        public EAddressMode AddressModeW;
        public EComparisonMode ComparisonMode;
    }

    public struct RHIStaticSamplerStateElement
    {
        public uint BindSlot;
        public RHISamplerStateDescriptor SamplerStateDescriptor;
    }

    public struct RHIStaticSamplerStateDescriptor
    {
        public uint Index;
        public Memory<RHIStaticSamplerStateElement> Elements;
    }

    public abstract class RHISamplerState : Disposal
    {

    }
}
