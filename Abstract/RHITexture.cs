using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHITextureDescriptor
    {
        public int MipCount;
        public int3 Extent;
        public int2 Samples;
        public EPixelFormat Format;
        public EStorageMode StorageMode;
        public ETextureState State;
        public ETextureUsage Usage;
        public ETextureDimension Dimension;
    }

    public abstract class RHITexture : Disposal
    {
        public RHITextureDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        protected RHITextureDescriptor m_Descriptor;

        public abstract RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor);
    }
}
