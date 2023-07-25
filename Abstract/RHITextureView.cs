using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHITextureViewDescriptor
    {
        public uint MipCount;
        public uint BaseMipLevel;
        public uint SliceCount;
        public uint BaseSliceLevel;
        public EPixelFormat Format;
        public ETextureViewType ViewType;
        //public ETextureDimension Dimension;
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
