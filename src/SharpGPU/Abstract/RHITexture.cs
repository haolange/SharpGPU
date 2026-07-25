using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public struct RHITextureDescriptor
    {
        public uint MipCount;
        public uint3 Extent;
        public ERHIPixelFormat Format;
        public ERHISampleCount SampleCount;
        public ERHIStorageMode StorageMode;
        public ERHITextureUsage UsageFlag;
        public ERHITextureDimension Dimension;
    }

    public abstract class RHITexture : Disposal
    {
        public RHITextureDescriptor Descriptor
        {
            get
            {
                ThrowIfDisposed();
                return m_Descriptor;
            }
        }
        public ERHIResourceAllocationMode AllocationMode
        {
            get
            {
                ThrowIfDisposed();
                return m_AllocationMode;
            }
        }

        protected RHITextureDescriptor m_Descriptor;
        protected ERHIResourceAllocationMode m_AllocationMode = ERHIResourceAllocationMode.Committed;

        public abstract RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor);
    }
}
