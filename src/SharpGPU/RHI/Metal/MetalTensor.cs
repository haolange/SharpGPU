using System;
using SharpGPU.Core;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal class MetalTensor : RHITensor
    {
        internal MTLTensor NativeTensor => m_NativeTensor;
        internal ulong BackingBufferOffset => m_BackingBufferOffset;
        internal ulong ByteLength => m_ByteLength;
        internal MetalBuffer? BackingBuffer => m_BackingBuffer;

        private MTLTensor m_NativeTensor;
        private MetalBuffer? m_BackingBuffer;
        private readonly ulong m_BackingBufferOffset;
        private readonly ulong m_ByteLength;

        public MetalTensor(MetalDevice device, in RHIMLTensorDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_BackingBufferOffset = descriptor.BackingBufferOffset;
            m_ByteLength = RHIMLHelpers.CalculateMinimumByteLength(descriptor);

            MTLTensorDescriptor nativeDescriptor = MTLTensorDescriptor.New();
            nativeDescriptor.DataType = ConvertDataType(descriptor.DataType);
            nativeDescriptor.Usage = ConvertTensorUsage(descriptor.UsageFlag);
            nativeDescriptor.ResourceOptions = ConvertStorageMode(descriptor.StorageMode);
            nativeDescriptor.Dimensions = CreateDimensionsArray(descriptor.Dimensions.Span);
            if (RHIMLHelpers.HasExplicitStrides(descriptor))
            {
                nativeDescriptor.Strides = CreateDimensionsArray(descriptor.Strides.Value.Span);
            }

            if (descriptor.BackingBuffer != null)
            {
                m_BackingBuffer = descriptor.BackingBuffer as MetalBuffer
                    ?? throw new InvalidOperationException($"Metal tensor requires a {nameof(MetalBuffer)} backing buffer when BackingBuffer is supplied.");

                NSError tensorError = default;
                m_NativeTensor = m_BackingBuffer.NativeBuffer.NewTensor(nativeDescriptor, descriptor.BackingBufferOffset, ref tensorError);
                if (m_NativeTensor.NativePtr == IntPtr.Zero)
                {
                    string errorText = tensorError.NativePtr != IntPtr.Zero ? tensorError.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to create buffer-backed MTLTensor: {errorText}");
                }
            }
            else
            {
                m_NativeTensor = device.NativeDevice.NewTensor(nativeDescriptor);
                if (m_NativeTensor.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTLTensor.");
                }
            }

            ReleaseNSArray(nativeDescriptor.Dimensions);
            ReleaseNSArray(nativeDescriptor.Strides);
            ObjectiveCRuntime.Release(nativeDescriptor);
        }

        private static NSArray CreateDimensionsArray(ReadOnlySpan<uint> values)
        {
            if (values.Length == 0)
            {
                return default;
            }

            IntPtr[] pointers = new IntPtr[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                pointers[i] = NSNumber.Number(values[i]).NativePtr;
            }

            return MetalArrayHelper.CreateNSArrayFromPointers(pointers);
        }

        private static void ReleaseNSArray(in NSArray array)
        {
            if (array.NativePtr == IntPtr.Zero)
            {
                return;
            }

            for (ulong i = 0; i < array.Count; ++i)
            {
                IntPtr objectPtr = array.Object(i);
                if (objectPtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(objectPtr);
                }
            }

            ObjectiveCRuntime.Release(array.NativePtr);
        }

        private static MTLDataType ConvertDataType(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => MTLDataType.Float,
                ERHIMLDataType.Float16 => MTLDataType.Half,
                ERHIMLDataType.BFloat16 => MTLDataType.BFloat,
                ERHIMLDataType.Int32 => MTLDataType.Int,
                ERHIMLDataType.Int16 => MTLDataType.Short,
                ERHIMLDataType.Int8 => MTLDataType.Char,
                ERHIMLDataType.UInt32 => MTLDataType.UInt,
                ERHIMLDataType.UInt16 => MTLDataType.UShort,
                ERHIMLDataType.UInt8 => MTLDataType.UChar,
                _ => MTLDataType.Float,
            };
        }

        private static MTLTensorUsage ConvertTensorUsage(in ERHITensorUsage usage)
        {
            MTLTensorUsage result = 0;
            if ((usage & ERHITensorUsage.MachineLearning) != 0) result |= MTLTensorUsage.MachineLearning;
            if ((usage & ERHITensorUsage.Compute) != 0) result |= MTLTensorUsage.Compute;
            if ((usage & ERHITensorUsage.Render) != 0) result |= MTLTensorUsage.Render;
            if ((usage & ERHITensorUsage.Read) != 0) result |= MTLTensorUsage.Read;
            if ((usage & ERHITensorUsage.Write) != 0) result |= MTLTensorUsage.Write;
            return result;
        }

        private static MTLResourceOptions ConvertStorageMode(in ERHIStorageMode storageMode)
        {
            return storageMode switch
            {
                ERHIStorageMode.GPULocal => MTLResourceOptions.ResourceStorageModePrivate,
                ERHIStorageMode.GPUUpload => MTLResourceOptions.ResourceStorageModeShared,
                ERHIStorageMode.HostUpload => MTLResourceOptions.ResourceStorageModeShared,
                ERHIStorageMode.Readback => MTLResourceOptions.ResourceStorageModeShared,
                ERHIStorageMode.Memoryless => MTLResourceOptions.ResourceStorageModeMemoryless,
                _ => MTLResourceOptions.ResourceStorageModePrivate,
            };
        }

        protected override void Release()
        {
            if (m_NativeTensor.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeTensor);
                m_NativeTensor = default;
            }

            m_BackingBuffer = null;
        }
    }
}
