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
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
