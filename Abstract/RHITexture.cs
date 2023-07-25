using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHITextureDescriptor
    {
        public uint MipCount;
        public uint3 Extent;
        public EPixelFormat Format;
        public EStorageMode StorageMode;
        public ESampleCount SampleCount;
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
