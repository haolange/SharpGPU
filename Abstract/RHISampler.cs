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
        public ERHIFilterMode MagFilter;
        public ERHIFilterMode MinFilter;
        public ERHIFilterMode MipFilter;
        public ERHIAddressMode AddressModeU;
        public ERHIAddressMode AddressModeV;
        public ERHIAddressMode AddressModeW;
        public ERHIComparisonMode ComparisonMode;
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
