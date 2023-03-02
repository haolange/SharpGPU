using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHISwapChainDescriptor
    {
        public bool FrameBufferOnly;
        public int Count;
        public int2 Extent;
        public IntPtr Surface;
        public EPixelFormat Format;
        public RHIQueue PresentQueue;
    }

    public abstract class RHISwapChain : Disposal
    {
        public abstract int BackBufferIndex
        {
            get;
        }

        public abstract RHITexture GetTexture(in int index);
        public abstract RHITextureView GetTextureView(in int index);
        public abstract void Resize(in int2 extent);
        public abstract void Present(in EPresentMode presentMode);
    }
}
