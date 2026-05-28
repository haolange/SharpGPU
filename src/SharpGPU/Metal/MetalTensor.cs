using System;
using SharpGPU.Core;
using SharpMetal.Metal;
using SharpMetal.Foundation;
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
            MTLTensorExtents nativeDimensions = default;
            MTLTensorExtents nativeStrides = default;

            try
            {
                nativeDescriptor.DataType = ConvertDataType(descriptor.DataType);
                nativeDescriptor.Usage = ConvertTensorUsage(descriptor.UsageFlag);
                nativeDescriptor.ResourceOptions = ConvertStorageMode(descriptor.StorageMode);
                nativeDimensions = CreateTensorExtents(descriptor.Dimensions.Span);
                nativeDescriptor.Dimensions = nativeDimensions;
                if (RHIMLHelpers.HasExplicitStrides(descriptor))
                {
                    nativeStrides = CreateTensorExtents(descriptor.Strides.Value.Span);
                    nativeDescriptor.Strides = nativeStrides;
                }

                NSError tensorError = default;
                if (descriptor.BackingBuffer != null)
                {
                    m_BackingBuffer = descriptor.BackingBuffer as MetalBuffer
                        ?? throw new InvalidOperationException($"Metal tensor requires a {nameof(MetalBuffer)} backing buffer when BackingBuffer is supplied.");

                    m_NativeTensor = m_BackingBuffer.NativeBuffer.NewTensor(nativeDescriptor, descriptor.BackingBufferOffset, ref tensorError);
                    if (m_NativeTensor.NativePtr == IntPtr.Zero)
                    {
                        string errorText = tensorError.NativePtr != IntPtr.Zero ? tensorError.LocalizedDescription.ToString() : "unknown error";
                        throw new InvalidOperationException($"Failed to create buffer-backed MTLTensor: {errorText}");
                    }
                }
                else
                {
                    m_NativeTensor = device.NativeDevice.NewTensor(nativeDescriptor, ref tensorError);
                    if (m_NativeTensor.NativePtr == IntPtr.Zero)
                    {
                        string errorText = tensorError.NativePtr != IntPtr.Zero ? tensorError.LocalizedDescription.ToString() : "unknown error";
                        throw new InvalidOperationException($"Failed to create MTLTensor: {errorText}");
                    }
                }
            }
            finally
            {
                ReleaseNativeObject(nativeStrides);
                ReleaseNativeObject(nativeDimensions);
                ReleaseNativeObject(nativeDescriptor);
            }
        }

        private static unsafe MTLTensorExtents CreateTensorExtents(ReadOnlySpan<uint> values)
        {
            if (values.Length > (int)MTLTensorExtents.MaxRank)
            {
                throw new ArgumentOutOfRangeException(nameof(values), $"Metal tensors support at most {MTLTensorExtents.MaxRank} dimensions.");
            }

            if (values.Length == 0)
            {
                return MTLTensorExtents.New(0, IntPtr.Zero);
            }

            nint[] nativeValues = new nint[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                nativeValues[i] = checked((nint)values[i]);
            }

            fixed (nint* nativeValuesPtr = nativeValues)
            {
                return MTLTensorExtents.New((ulong)nativeValues.Length, (IntPtr)nativeValuesPtr);
            }
        }

        private static void ReleaseNativeObject(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            ObjectiveCRuntime.Release(nativePtr);
        }

        private static MTLTensorDataType ConvertDataType(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => MTLTensorDataType.Float32,
                ERHIMLDataType.Float16 => MTLTensorDataType.Float16,
                ERHIMLDataType.BFloat16 => MTLTensorDataType.BFloat16,
                ERHIMLDataType.Int32 => MTLTensorDataType.Int32,
                ERHIMLDataType.Int16 => MTLTensorDataType.Int16,
                ERHIMLDataType.Int8 => MTLTensorDataType.Int8,
                ERHIMLDataType.UInt32 => MTLTensorDataType.UInt32,
                ERHIMLDataType.UInt16 => MTLTensorDataType.UInt16,
                ERHIMLDataType.UInt8 => MTLTensorDataType.UInt8,
                _ => MTLTensorDataType.Float32,
            };
        }

        private static MTLTensorUsage ConvertTensorUsage(in ERHITensorUsage usage)
        {
            MTLTensorUsage result = 0;
            if ((usage & ERHITensorUsage.MachineLearning) != 0)
            {
                result |= MTLTensorUsage.MachineLearning;
            }

            if ((usage & ERHITensorUsage.Compute) != 0)
            {
                result |= MTLTensorUsage.Compute;
            }

            if ((usage & ERHITensorUsage.Render) != 0)
            {
                result |= MTLTensorUsage.Render;
            }

            return result != 0 ? result : MTLTensorUsage.Compute;
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
