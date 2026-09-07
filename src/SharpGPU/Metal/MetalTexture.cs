using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.QuartzCore;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalTexture : RHITexture
    {
        public MetalDevice MetalDevice { get { ThrowIfDisposed(); return m_MetalDevice; } }
        public MTLTexture NativeTexture { get { ThrowIfDisposed(); return m_NativeTexture; } }
        internal CAMetalDrawable BackingDrawable => m_Drawable;
        internal bool HasBackingDrawable => m_Drawable.NativePtr != IntPtr.Zero;
        internal RHISparseTextureMemoryRequirements? SparseRequirements { get; }

        private readonly MetalDevice m_MetalDevice;
        private readonly bool m_OwnsTexture;
        private readonly CAMetalDrawable m_Drawable;
        private RHIHeapPlacement? m_Placement;
        private MTLTexture m_NativeTexture;

        public MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_OwnsTexture = true;
            m_Drawable = default;

            MTLTextureDescriptor nativeDescriptor = MetalMemoryUtility.BuildTextureDescriptor(descriptor);
            try
            {
                m_NativeTexture = device.NativeDevice.NewTexture(nativeDescriptor);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }
            if (m_NativeTexture.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLDevice failed to create a committed texture.",
                    ERHIDeviceState.Operational);
            }
        }

        internal MetalTexture(
            MetalDevice device,
            in RHITextureDescriptor descriptor,
            MetalHeap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_OwnsTexture = true;
            m_Drawable = default;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;

            MTLTextureDescriptor nativeDescriptor =
                MetalMemoryUtility.BuildTextureDescriptor(descriptor);
            try
            {
                m_NativeTexture = heap.NativeHeap.NewTexture(nativeDescriptor, heapOffset);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }
            if (m_NativeTexture.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLHeap failed to create a placed texture.",
                    ERHIDeviceState.Operational);
            }
            m_Placement = placement;
        }

        internal MetalTexture(
            MetalDevice device,
            in RHITextureDescriptor descriptor,
            bool createSparse)
        {
            if (!createSparse)
            {
                throw new ArgumentException(
                    "The sparse texture constructor requires sparse creation.",
                    nameof(createSparse));
            }

            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_OwnsTexture = true;
            m_Drawable = default;
            m_AllocationMode = ERHIResourceAllocationMode.Sparse;
            m_NativeTexture =
                MetalSparseMemoryUtility.CreateSparseTexture(
                    device,
                    descriptor);
            try
            {
                SparseRequirements =
                    MetalSparseMemoryUtility.QueryRequirements(
                        device,
                        descriptor,
                        m_NativeTexture);
            }
            catch
            {
                ObjectiveCRuntime.Release(m_NativeTexture.NativePtr);
                m_NativeTexture = default;
                throw;
            }
        }

        internal MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor, in MTLTexture nativeTexture, in bool ownsTexture)
            : this(device, descriptor, nativeTexture, ownsTexture, default)
        {
        }

        internal MetalTexture(MetalDevice device, in RHITextureDescriptor descriptor, in MTLTexture nativeTexture, in bool ownsTexture, in CAMetalDrawable drawable)
        {
            if (!RHIFormatSupportQuery.IsKnownTextureUsage(descriptor.UsageFlag))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.UsageFlag,
                    "Metal native texture wrap requires an explicit non-zero known ERHITextureUsage.");
            }

            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeTexture = nativeTexture;
            m_OwnsTexture = ownsTexture;
            m_Drawable = drawable;
            if (!ownsTexture)
            {
                m_AllocationMode = ERHIResourceAllocationMode.External;
            }
        }

        internal static RHITextureDescriptor BuildDescriptorFromNative(
            in MTLTexture nativeTexture,
            ERHITextureUsage usage)
        {
            if (!RHIFormatSupportQuery.IsKnownTextureUsage(usage))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(usage),
                    usage,
                    "Metal native texture wrap requires an explicit non-zero known ERHITextureUsage.");
            }

            return new RHITextureDescriptor
            {
                MipCount = (uint)Math.Max(1, nativeTexture.MipmapLevelCount),
                Extent = new SharpMath.uint3((uint)Math.Max(1, nativeTexture.Width), (uint)Math.Max(1, nativeTexture.Height), (uint)Math.Max(1, nativeTexture.ArrayLength)),
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
            ThrowIfDisposed();
            return new MetalTextureView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_OwnsTexture && m_NativeTexture.NativePtr != IntPtr.Zero)
            {
                m_MetalDevice.RemoveResidencyAllocation(m_NativeTexture);
                ObjectiveCRuntime.Release(m_NativeTexture);
            }

            m_NativeTexture = default;
            m_Placement?.Dispose();
            m_Placement = null;
        }
    }
}