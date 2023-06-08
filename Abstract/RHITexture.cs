using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHITextureDescriptor
    {
        public uint Samples;
        public uint MipCount;
        public uint3 Extent;
        public EPixelFormat Format;
        public EStorageMode StorageMode;
        public ETextureUsage Usage;
        public ETextureDimension Dimension;
    }

    public abstract class RHITexture : Disposal
    {
        public ETextureState State
        {
            get
            {
                return m_State;
            }
        }
        public RHITextureDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        protected ETextureState m_State;
        protected RHITextureDescriptor m_Descriptor;

        public void SetState(in ETextureState state)
        {
            m_State = state;
        }

        public abstract RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor);
    }
}
