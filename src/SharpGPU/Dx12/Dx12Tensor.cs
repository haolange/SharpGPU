using System;

namespace SharpGPU
{
    internal unsafe class Dx12Tensor : RHITensor
    {
        // DirectML does not have a native tensor resource type.
        // Tensors are backed by Vortice.Direct3D12.D3D12 buffers; the tensor descriptor
        // is used to interpret the buffer layout during binding.
        internal Dx12Buffer BackingBuffer => m_BackingBuffer ?? throw new InvalidOperationException("DX12 tensor backing buffer is unavailable.");
        internal ulong BackingBufferOffset => m_BackingBufferOffset;
        internal ulong ByteLength => m_ByteLength;

        private Dx12Buffer? m_BackingBuffer;
        private readonly ulong m_ByteLength;
        private readonly ulong m_BackingBufferOffset;
        private readonly bool m_OwnsBackingBuffer;

        public Dx12Tensor(Dx12Device device, in RHIMLTensorDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_ByteLength = RHIMLHelpers.CalculateMinimumByteLength(descriptor);
            m_BackingBufferOffset = descriptor.BackingBufferOffset;

            if (descriptor.BackingBuffer != null)
            {
                if (descriptor.BackingBuffer is not Dx12Buffer dx12BackingBuffer)
                {
                    throw new InvalidOperationException($"DX12 tensor requires a {nameof(Dx12Buffer)} backing buffer when BackingBuffer is supplied.");
                }

                ulong backingByteSize = checked((ulong)dx12BackingBuffer.Descriptor.ByteSize);
                if (m_BackingBufferOffset + m_ByteLength > backingByteSize)
                {
                    throw new InvalidOperationException($"DX12 tensor range [{m_BackingBufferOffset}, {m_BackingBufferOffset + m_ByteLength}) exceeds backing buffer size {backingByteSize}.");
                }

                m_BackingBuffer = dx12BackingBuffer;
                m_OwnsBackingBuffer = false;
                return;
            }

            // Create a backing buffer for the tensor data
            RHIBufferDescriptor bufferDescriptor = new RHIBufferDescriptor
            {
                ByteSize = checked((int)m_ByteLength),
                Format = ERHIBufferFormat.Undefine,
                StorageMode = descriptor.StorageMode,
                UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst | ERHIBufferUsage.UnorderedAccess | ERHIBufferUsage.ShaderResource,
            };
            m_BackingBuffer = new Dx12Buffer(device, bufferDescriptor);
            m_OwnsBackingBuffer = true;
        }

        protected override void Release()
        {
            if (m_OwnsBackingBuffer)
            {
                m_BackingBuffer?.Dispose();
            }
            m_BackingBuffer = null;
        }
    }
}
