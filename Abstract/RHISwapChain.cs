using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHISwapChainDescriptor
    {
        public bool FrameBufferOnly;
        public uint FPS;
        public uint Count;
        public uint2 Extent;
        public IntPtr Surface;
        public EPresentMode PresentMode;
        public ESwapChainFormat Format;
        public RHIQueue PresentQueue;
    }

    public abstract class RHISwapChain : Disposal
    {
        public abstract int BackTextureIndex
        {
            get;
        }

        public abstract RHITexture AcquireBackTexture();
        public abstract void Resize(in uint2 extent);
        public abstract void Present();
    }
}
