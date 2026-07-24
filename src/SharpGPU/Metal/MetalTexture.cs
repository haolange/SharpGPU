using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalTexture : RHITexture
    {
        public MetalDevice MetalDevice => m_MetalDevice;
        public MTLTexture NativeTexture => m_NativeTexture;
        internal CAMetalDrawable BackingDrawable => m_Drawable;
        internal bool HasBackingDrawable => m_Drawable.NativePtr != IntPtr.Zero;

        private readonly MetalDevice m_MetalDevice;
        private readonly bool m_OwnsTexture;
        private readonly CAMetalDrawable m_Drawable;
        private MTLTexture m_NativeTexture;

        public MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_OwnsTexture = true;
            m_Drawable = default;

            MTLTextureDescriptor nativeDescriptor = MTLTextureDescriptor.New();
            try
            {
                nativeDescriptor.TextureType = MetalUtility.ConvertToMetalTextureType(descriptor.Dimension);
                nativeDescriptor.PixelFormat = MetalUtility.ConvertToMetalPixelFormat(descriptor.Format);
                nativeDescriptor.Width = descriptor.Extent.x;
                nativeDescriptor.Height = descriptor.Extent.y;
                nativeDescriptor.Depth = descriptor.Dimension == ERHITextureDimension.Texture3D ? descriptor.Extent.z : 1;
                nativeDescriptor.ArrayLength = descriptor.Dimension switch
                {
                    ERHITextureDimension.Texture2DArray => descriptor.Extent.z,
                    ERHITextureDimension.Texture2DArrayMS => descriptor.Extent.z,
                    ERHITextureDimension.TextureCube => 6,
                    ERHITextureDimension.TextureCubeArray => descriptor.Extent.z,
                    _ => 1,
                };
                nativeDescriptor.MipmapLevelCount = descriptor.MipCount;
                nativeDescriptor.SampleCount = (ulong)descriptor.SampleCount;
                nativeDescriptor.Usage = MetalUtility.ConvertToMetalTextureUsage(descriptor.UsageFlag);
                nativeDescriptor.StorageMode = MetalUtility.ConvertToMetalStorageMode(descriptor.StorageMode);
                nativeDescriptor.CpuCacheMode = descriptor.StorageMode == ERHIStorageMode.HostUpload ? MTLCPUCacheMode.WriteCombined : MTLCPUCacheMode.DefaultCache;

                m_NativeTexture = device.NativeDevice.NewTexture(nativeDescriptor);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }
            if (m_NativeTexture.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLTexture.");
            }
        }

        internal MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor, in MTLTexture nativeTexture, in bool ownsTexture)
            : this(device, descriptor, nativeTexture, ownsTexture, default)
        {
        }

        internal MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor, in MTLTexture nativeTexture, in bool ownsTexture, in CAMetalDrawable drawable)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeTexture = nativeTexture;
            m_OwnsTexture = ownsTexture;
            m_Drawable = drawable;
        }

        internal static RHITextureDescriptor BuildDescriptorFromNative(in MTLTexture nativeTexture)
        {
            ERHITextureUsage usage = MetalUtility.ConvertToRhiTextureUsage(nativeTexture.Usage);
            if (usage == ERHITextureUsage.Pending)
            {
                usage = ERHITextureUsage.RenderTarget;
            }

            return new RHITextureDescriptor
            {
                MipCount = (uint)Math.Max(1, nativeTexture.MipmapLevelCount),
                Extent = new SharpGPU.Mathematics.uint3((uint)Math.Max(1, nativeTexture.Width), (uint)Math.Max(1, nativeTexture.Height), (uint)Math.Max(1, nativeTexture.ArrayLength)),
                Format = MetalUtility.ConvertToRhiPixelFormat(nativeTexture.PixelFormat),
                SampleCount = nativeTexture.SampleCount switch
                {
                    2 => ERHISampleCount.Count2,
                    4 => ERHISampleCount.Count4,
                    8 => ERHISampleCount.Count8,
                    _ => ERHISampleCount.None,
                },
                StorageMode = MetalUtility.ConvertToRhiStorageMode(nativeTexture.StorageMode),
                UsageFlag = usage,
                Dimension = MetalUtility.ConvertToRhiTextureDimension(nativeTexture.TextureType)
            };
        }

        public override RHITextureView CreateTextureView(in RHITextureViewDescriptor descriptor)
        {
            return new MetalTextureView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_OwnsTexture && m_NativeTexture.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeTexture);
            }

            m_NativeTexture = default;
        }
    }

    internal sealed class MetalTextureView : RHITextureView
    {
        public MetalTexture Texture => m_Texture;
        public MTLResourceID ResourceID => m_ResourceID;
        public MTLTexture ParentTexture => m_Texture.NativeTexture;
        public RHITextureViewDescriptor Descriptor => m_Descriptor;

        private readonly MetalTexture m_Texture;
        private readonly RHITextureViewDescriptor m_Descriptor;
        private readonly MetalTextureViewIndexLease m_PoolLease;
        private MTLResourceID m_ResourceID;

        public MetalTextureView(MetalTexture texture, in RHITextureViewDescriptor descriptor)
        {
            m_Texture = texture;
            m_Descriptor = descriptor;

            MetalDevice device = texture.MetalDevice;
            MTLTextureViewPool pool = device.TextureViewPool;
            m_PoolLease = device.AllocateTextureViewIndex();

            try
            {
                bool fullView = descriptor.BaseMipLevel == 0 &&
                                descriptor.BaseArraySlice == 0 &&
                                descriptor.MipCount >= texture.Descriptor.MipCount &&
                                descriptor.ArrayCount >= texture.Descriptor.Extent.z;

                if (fullView)
                {
                    m_ResourceID = pool.SetTextureView(texture.NativeTexture.NativePtr, m_PoolLease.Index);
                    return;
                }

                MTLTextureViewDescriptor viewDescriptor = MTLTextureViewDescriptor.New();
                try
                {
                    viewDescriptor.PixelFormat = MetalUtility.ConvertToMetalPixelFormat(texture.Descriptor.Format);
                    viewDescriptor.TextureType = MetalUtility.ConvertToMetalTextureType(texture.Descriptor.Dimension);
                    viewDescriptor.LevelRange = new NSRange { location = descriptor.BaseMipLevel, length = descriptor.MipCount };
                    viewDescriptor.SliceRange = new NSRange { location = descriptor.BaseArraySlice, length = descriptor.ArrayCount };
                    m_ResourceID = pool.SetTextureView(texture.NativeTexture.NativePtr, viewDescriptor.NativePtr, m_PoolLease.Index);
                }
                finally
                {
                    ObjectiveCRuntime.Release(viewDescriptor.NativePtr);
                }
            }
            catch
            {
                device.ReleaseTextureViewIndex(m_PoolLease);
                throw;
            }
        }

        protected override void Release()
        {
            m_Texture.MetalDevice.ReleaseTextureViewIndex(m_PoolLease);
            m_ResourceID = default;
        }
    }
}
