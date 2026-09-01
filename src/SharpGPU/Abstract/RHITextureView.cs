using SharpGPU.Core;

namespace SharpGPU
{
    public struct RHITextureViewDescriptor
    {
        public uint MipCount;
        public uint BaseMipLevel;
        public uint ArrayCount;
        public uint BaseArraySlice;
        //public ERHIPixelFormat Format;
        public ERHITextureViewType ViewType;
        //public ERHITextureDimension Dimension;
        // Sampler-feedback UAV views consume the map's create-time pairing.
        // The encoder cannot pair a feedback map with a sampled texture.
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
