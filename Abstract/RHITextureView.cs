using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHITextureViewDescriptor
    {
        public int MipCount;
        public int BaseMipLevel;
        public int ArrayLayerCount;
        public int BaseArrayLayer;
        public EPixelFormat Format;
        public ETextureViewType ViewType;
        public ETextureViewDimension Dimension;
    }

    public abstract class RHITextureView : Disposal
    {

    }
}
