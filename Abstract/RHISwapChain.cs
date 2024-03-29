﻿using System;
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
        public ERHIPresentMode PresentMode;
        public ERHISwapChainFormat Format;
        public RHICommandQueue PresentQueue;
    }

    public abstract class RHISwapChain : Disposal
    {
        public abstract int BackTextureIndex
        {
            get;
        }

        public abstract RHITexture AcquireBackBufferTexture();
        public abstract void Resize(in uint2 extent);
        public abstract void Present();
    }
}
