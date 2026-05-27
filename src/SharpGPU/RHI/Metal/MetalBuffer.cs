using System;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using SharpMetal.Metal;

namespace Infinity.Graphics
{
    internal sealed class MetalBuffer : RHIBuffer
    {
        public MetalDevice MetalDevice => m_MetalDevice;
        public MTLBuffer NativeBuffer => m_NativeBuffer;

        private readonly MetalDevice m_MetalDevice;
        private MTLBuffer m_NativeBuffer;

        public MetalBuffer(MetalDevice device, in RHIBufferDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = device.NativeDevice.NewBuffer((ulong)descriptor.ByteSize, MetalUtility.ConvertToMetalResourceOptions(descriptor.StorageMode));

            if (m_NativeBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLBuffer.");
            }
        }

        internal MetalBuffer(MetalDevice device, in RHIBufferDescriptor descriptor, in MTLBuffer nativeBuffer)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = nativeBuffer;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPULocal || m_Descriptor.StorageMode == ERHIStorageMode.Memoryless)
            {
                throw new InvalidOperationException("GPULocal/Memoryless buffer cannot be mapped.");
            }

            IntPtr basePtr = m_NativeBuffer.Contents;
            return basePtr == IntPtr.Zero ? IntPtr.Zero : IntPtr.Add(basePtr, (int)readBegin);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
            if (m_NativeBuffer.StorageMode == MTLStorageMode.Managed)
            {
                NSRange range = new NSRange
                {
                    location = writeBegin,
                    length = Math.Min((ulong)(writeEnd - writeBegin), (ulong)Math.Max(0, m_Descriptor.ByteSize - (int)writeBegin))
                };
                m_NativeBuffer.DidModifyRange(range);
            }
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return new MetalBufferView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_NativeBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeBuffer);
                m_NativeBuffer = default;
            }
        }
    }
}
