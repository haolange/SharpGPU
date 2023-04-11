using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHISamplerDescriptor
    {
        public float LodMin;
        public float LodMax;
        public float MipLODBias;
        public uint Anisotropy;
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
