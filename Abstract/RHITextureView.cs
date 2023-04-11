using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHITextureViewDescriptor
    {
        public uint MipCount;
        public uint BaseMipLevel;
        public uint BaseArrayLayer;
        public uint ArrayLayerCount;
        public EPixelFormat Format;
        public ETextureViewType ViewType;
        public ETextureDimension Dimension;
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
