using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    internal unsafe class Dx12Tensor : RHITensor
    {
        // DirectML does not have a native tensor resource type.
        // Tensors are backed by D3D12 buffers; the tensor descriptor
        // is used to interpret the buffer layout during binding.
        internal RHIBuffer? BackingBuffer => m_BackingBuffer;

        private RHIBuffer? m_BackingBuffer;

        public Dx12Tensor(Dx12Device device, in RHIMLTensorDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            // Calculate buffer size from tensor dimensions and data type
            ulong elementCount = 1;
            Span<uint> dims = descriptor.Dimensions.Span;
            for (int i = 0; i < dims.Length; ++i)
            {
                elementCount *= dims[i];
            }

            ulong elementSize = GetElementSize(descriptor.DataType);
            ulong bufferSize = elementCount * elementSize;

            // Create a backing buffer for the tensor data
            RHIBufferDescriptor bufferDescriptor = new RHIBufferDescriptor
            {
                ByteSize = (int)bufferSize,
                Format = ERHIBufferFormat.Undefine,
                StorageMode = descriptor.StorageMode,
                UsageFlag = ERHIBufferUsage.UnorderedAccess | ERHIBufferUsage.ShaderResource,
            };
            m_BackingBuffer = device.CreateBuffer(bufferDescriptor);
        }

        private static ulong GetElementSize(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => 4,
                ERHIMLDataType.Float16 => 2,
                ERHIMLDataType.BFloat16 => 2,
                ERHIMLDataType.Int32 => 4,
                ERHIMLDataType.Int16 => 2,
                ERHIMLDataType.Int8 => 1,
                ERHIMLDataType.UInt32 => 4,
                ERHIMLDataType.UInt16 => 2,
                ERHIMLDataType.UInt8 => 1,
                _ => 4,
            };
        }

        protected override void Release()
        {
            m_BackingBuffer?.Dispose();
            m_BackingBuffer = null;
        }
    }
}
