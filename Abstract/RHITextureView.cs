using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHITextureViewDescriptor
    {
        public uint MipCount;
        public uint BaseMipIndex;
        public uint ArrayCount;
        public uint BaseArraySlice;
        //public EPixelFormat Format;
        public ETextureViewType ViewType;
        //public ETextureDimension Dimension;
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
