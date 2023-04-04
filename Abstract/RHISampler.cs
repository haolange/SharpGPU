using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHISamplerDescriptor
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

    public struct RHIStaticSamplerElement
    {
        public uint BindSlot;
        public RHISamplerDescriptor SamplerDescriptor;
    }

    public struct RHIStaticSamplerDescriptor
    {
        public uint Index;
        public Memory<RHIStaticSamplerElement> Elements;
    }

    public abstract class RHISampler : Disposal
    {

    }
}
