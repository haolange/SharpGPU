using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalBuffer : RHIBuffer
    {
        public MetalDevice MetalDevice { get { ThrowIfDisposed(); return m_MetalDevice; } }
        public MTLBuffer NativeBuffer { get { ThrowIfDisposed(); return m_NativeBuffer; } }
        public override ulong GpuVirtualAddress
        {
            get
            {
                ThrowIfDisposed();
                ulong address = m_NativeBuffer.GpuAddress;
                if (address == 0)
                {
                    throw new NotSupportedException("Metal did not expose a GPU address for this buffer.");
                }
                return address;
            }
        }


        private readonly MetalDevice m_MetalDevice;
        private MTLBuffer m_NativeBuffer;
        private RHIHeapPlacement? m_Placement;

        public MetalBuffer(MetalDevice device, in RHIBufferDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = device.NativeDevice.NewBuffer(
                (ulong)descriptor.ByteSize,
                MetalMemoryUtility.GetBufferOptions(descriptor));

            if (m_NativeBuffer.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLDevice failed to create a committed buffer.",
                    ERHIDeviceState.Operational);
            }
        }

        internal MetalBuffer(
            MetalDevice device,
            in RHIBufferDescriptor descriptor,
            MetalHeap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;
            m_NativeBuffer = heap.NativeHeap.NewBuffer(
                (ulong)descriptor.ByteSize,
                MetalMemoryUtility.GetBufferOptions(descriptor),
                heapOffset);

            if (m_NativeBuffer.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLHeap failed to create a placed buffer.",
                    ERHIDeviceState.Operational);
            }
            m_Placement = placement;
        }

        internal MetalBuffer(MetalDevice device, in RHIBufferDescriptor descriptor, in MTLBuffer nativeBuffer)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_NativeBuffer = nativeBuffer;
            m_AllocationMode = ERHIResourceAllocationMode.External;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
            ThrowIfDisposed();
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPULocal || m_Descriptor.StorageMode == ERHIStorageMode.Memoryless)
            {
                throw new InvalidOperationException("GPULocal/Memoryless buffer cannot be mapped.");
            }
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (readBegin > byteSize || (readEnd != 0 && (readEnd < readBegin || readEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(readEnd), "The read range must be within the buffer.");
            }

            IntPtr basePtr = m_NativeBuffer.Contents;
            return basePtr == IntPtr.Zero ? IntPtr.Zero : IntPtr.Add(basePtr, (int)readBegin);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
            ThrowIfDisposed();
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (writeBegin > byteSize || (writeEnd != 0 && (writeEnd < writeBegin || writeEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(writeEnd), "The write range must be within the buffer.");
            }
            if (m_NativeBuffer.StorageMode == MTLStorageMode.Managed)
            {
                uint effectiveEnd = writeEnd == 0 ? byteSize : writeEnd;
                NSRange range = new NSRange
                {
                    location = writeBegin,
                    length = effectiveEnd - writeBegin
                };
                m_NativeBuffer.DidModifyRange(range);
            }
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new MetalBufferView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_NativeBuffer.NativePtr != IntPtr.Zero)
            {
                m_MetalDevice.RemoveResidencyAllocation(m_NativeBuffer);
                ObjectiveCRuntime.Release(m_NativeBuffer);
                m_NativeBuffer = default;
            }

            m_Placement?.Dispose();
            m_Placement = null;
        }
    }
}
