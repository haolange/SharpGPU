using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public enum ERHISamplerFeedbackMode : byte
    {
        MinMip,
        MipRegionUsed,
        Pending
    }

    [Flags]
    public enum ERHISamplerFeedbackOperation : byte
    {
        None = 0,
        Clear = 1 << 0,
        Resolve = 1 << 1,
        Decode = 1 << 2,
        Copy = 1 << 3,
        All = Clear | Resolve | Decode | Copy
    }

    /// <summary>
    /// Create-time pairing of a sampler-feedback map with the sampled texture
    /// it records. The encoder cannot establish this pairing.
    /// The returned map does not own <see cref="PairedTexture"/>; disposing
    /// the map does not dispose the paired texture. Destroy views first, then
    /// the map. The paired sampled texture may outlive the map.
    /// </summary>
    public struct RHISamplerFeedbackMapDescriptor
    {
        public RHITexture? PairedTexture;
        public ERHISamplerFeedbackMode Mode;
        public uint3 MipRegion;
    }

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

        /// <summary>
        /// True when this texture is an opaque sampler-feedback map created by
        /// <see cref="RHIDevice.CreateSamplerFeedbackMap"/>.
        /// </summary>
        public bool IsSamplerFeedbackMap
        {
            get
            {
                ThrowIfDisposed();
                return m_IsSamplerFeedbackMap;
            }
        }

        /// <summary>
        /// The sampled texture paired at create time. The map does not own
        /// this texture; disposing the map does not dispose the pair.
        /// </summary>
        public RHITexture? PairedSamplerFeedbackTexture
        {
            get
            {
                ThrowIfDisposed();
                return m_PairedSamplerFeedbackTexture;
            }
        }

        public ERHISamplerFeedbackMode SamplerFeedbackMode
        {
            get
            {
                ThrowIfDisposed();
                return m_SamplerFeedbackMode;
            }
        }

        public uint3 SamplerFeedbackMipRegion
        {
            get
            {
                ThrowIfDisposed();
                return m_SamplerFeedbackMipRegion;
            }
        }

        protected RHITextureDescriptor m_Descriptor;
        protected ERHIResourceAllocationMode m_AllocationMode = ERHIResourceAllocationMode.Committed;
        protected bool m_IsSamplerFeedbackMap;
        protected RHITexture? m_PairedSamplerFeedbackTexture;
        protected ERHISamplerFeedbackMode m_SamplerFeedbackMode = ERHISamplerFeedbackMode.Pending;
        protected uint3 m_SamplerFeedbackMipRegion;

        public abstract RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor);

        public static bool IsSamplerFeedbackOpaqueFormat(ERHIPixelFormat format)
        {
            return format is
                ERHIPixelFormat.SamplerFeedbackMinMipOpaque or
                ERHIPixelFormat.SamplerFeedbackMipRegionUsedOpaque;
        }

        protected void BindSamplerFeedbackPairing(
            RHITexture pairedTexture,
            ERHISamplerFeedbackMode mode,
            uint3 mipRegion)
        {
            m_IsSamplerFeedbackMap = true;
            m_PairedSamplerFeedbackTexture = pairedTexture;
            m_SamplerFeedbackMode = mode;
            m_SamplerFeedbackMipRegion = mipRegion;
        }
    }
}
