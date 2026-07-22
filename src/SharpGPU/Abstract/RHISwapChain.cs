using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public enum RHINativeSurfaceKind : byte
    {
        Unknown = 0,
        Win32Hwnd,
        AppKitNsWindow,
        X11Window,
        WaylandSurface,
        UIKitUiWindow,
        AndroidNativeWindow,
        Headless,
    }

    public struct RHISwapChainDescriptor
    {
        public bool FrameBufferOnly;
        public uint FPS;
        public uint Count;
        public uint2 Extent;
        public RHINativeSurfaceKind SurfaceKind;
        public IntPtr WindowHandle;
        public IntPtr DisplayHandle;
        public IntPtr InstanceHandle;
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
