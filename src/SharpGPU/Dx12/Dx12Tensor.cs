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
        internal Dx12Device Device
        {
            get
            {
                ThrowIfDisposed();
                return BackingBuffer.Dx12Device;
            }
        }

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
                if (descriptor.BackingBuffer.IsDisposed)
                {
                    throw new ObjectDisposedException(descriptor.BackingBuffer.GetType().FullName);
                }
                if (descriptor.BackingBuffer is not Dx12Buffer dx12BackingBuffer)
                {
                    throw new ArgumentException($"DX12 tensor requires a {nameof(Dx12Buffer)} backing buffer when BackingBuffer is supplied.", nameof(descriptor));
                }
                if (!ReferenceEquals(dx12BackingBuffer.Dx12Device, device))
                {
                    throw new ArgumentException(
                        "DX12 tensor backing buffer belongs to a different device.",
                        nameof(descriptor));
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

        public override RHITensorView CreateView(in RHITensorViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12TensorView(this, descriptor);
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

    internal sealed class Dx12TensorView : RHITensorView
    {
        internal Dx12Tensor ParentTensor => (Dx12Tensor)Parent;
        internal Dx12Buffer BackingBuffer => ParentTensor.BackingBuffer;
        internal ulong BackingBufferOffset => m_AbsoluteOffset;
        internal ulong ByteLength => m_ByteLength;
        internal Dx12Device Device => ParentTensor.Device;

        private readonly ulong m_AbsoluteOffset;
        private readonly ulong m_ByteLength;

        internal Dx12TensorView(Dx12Tensor parent, in RHITensorViewDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(parent);
            if (parent.IsDisposed)
            {
                throw new ObjectDisposedException(parent.GetType().FullName);
            }
            if (descriptor.Dimensions.Length == 0)
            {
                throw new ArgumentException("Tensor view requires a non-empty subshape.", nameof(descriptor));
            }

            m_Parent = parent;
            m_ViewDescriptor = descriptor;
            m_AbsoluteOffset = checked(parent.BackingBufferOffset + descriptor.Offset);

            RHIMLTensorDescriptor layout = parent.Descriptor;
            layout.Dimensions = descriptor.Dimensions.ToArray();
            layout.Strides =
                descriptor.Strides is Memory<uint> strides && strides.Length > 0
                    ? strides.ToArray()
                    : null;
            layout.BackingBuffer = parent.BackingBuffer;
            layout.BackingBufferOffset = m_AbsoluteOffset;
            m_Descriptor = layout;
            m_ByteLength = RHIMLHelpers.CalculateMinimumByteLength(layout);

            ulong backingByteSize = checked((ulong)parent.BackingBuffer.Descriptor.ByteSize);
            if (m_AbsoluteOffset + m_ByteLength > backingByteSize)
            {
                throw new InvalidOperationException(
                    $"DX12 tensor view range [{m_AbsoluteOffset}, {m_AbsoluteOffset + m_ByteLength}) exceeds backing buffer size {backingByteSize}.");
            }
        }

        internal RHIMLTensorDescriptor ToBackingTensorDescriptor()
        {
            return m_Descriptor;
        }

        protected override void Release()
        {
            m_Parent = null;
        }
    }
}
