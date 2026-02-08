using System;
using SharpMetal.ObjectiveCCore;
using SharpMetal.Metal;

namespace Infinity.Graphics
{
    internal sealed class MetalSampler : RHISampler
    {
        public MTLSamplerState NativeSampler => m_NativeSampler;

        private MTLSamplerState m_NativeSampler;

        public MetalSampler(MetalDevice device, in RHISamplerDescriptor descriptor)
        {
            MTLSamplerDescriptor nativeDescriptor = MTLSamplerDescriptor.New();
            nativeDescriptor.MinFilter = MetalUtility.ConvertToMetalFilter(descriptor.MinFilter);
            nativeDescriptor.MagFilter = MetalUtility.ConvertToMetalFilter(descriptor.MagFilter);
            nativeDescriptor.MipFilter = MetalUtility.ConvertToMetalMipFilter(descriptor.MipFilter);
            nativeDescriptor.SAddressMode = MetalUtility.ConvertToMetalAddressMode(descriptor.AddressModeU);
            nativeDescriptor.TAddressMode = MetalUtility.ConvertToMetalAddressMode(descriptor.AddressModeV);
            nativeDescriptor.RAddressMode = MetalUtility.ConvertToMetalAddressMode(descriptor.AddressModeW);
            nativeDescriptor.MaxAnisotropy = Math.Max(1u, descriptor.Anisotropy);
            nativeDescriptor.LodMinClamp = descriptor.LodMin;
            nativeDescriptor.LodMaxClamp = descriptor.LodMax;
            nativeDescriptor.CompareFunction = MetalUtility.ConvertToMetalCompareFunction(descriptor.ComparisonMode);

            m_NativeSampler = device.NativeDevice.NewSamplerState(nativeDescriptor);
            if (m_NativeSampler.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLSamplerState.");
            }
        }

        protected override void Release()
        {
            if (m_NativeSampler.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeSampler);
                m_NativeSampler = default;
            }
        }
    }
}
