using Infinity.Core;

namespace Infinity.Graphics
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
