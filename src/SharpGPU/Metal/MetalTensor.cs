using System;
using SharpGPU.Core;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal class MetalTensor : RHITensor
    {
        internal MTLTensor NativeTensor => m_NativeTensor;
        internal ulong BackingBufferOffset => m_BackingBufferOffset;
        internal ulong ByteLength => m_ByteLength;
        internal MetalBuffer? BackingBuffer => m_BackingBuffer;

        private readonly MetalDevice m_MetalDevice;
        private MTLTensor m_NativeTensor;
        private MetalBuffer? m_BackingBuffer;
        private readonly ulong m_BackingBufferOffset;
        private readonly ulong m_ByteLength;

        public MetalTensor(MetalDevice device, in RHIMLTensorDescriptor descriptor)
        {
            m_MetalDevice = device;
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
                nativeDimensions = CreateNativeTensorExtents(descriptor.Dimensions.Span);
                nativeDescriptor.Dimensions = nativeDimensions;
                if (descriptor.BackingBuffer != null || RHIMLHelpers.HasExplicitStrides(descriptor))
                {
                    nativeStrides = CreateNativeTensorExtentsFromNativeOrder(CalculateNativeTensorStrides(descriptor));
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

        internal static MTLTensorExtents CreateNativeTensorExtents(ReadOnlySpan<uint> values)
        {
            return CreateTensorExtents(values, reverseOrder: true);
        }

        internal static MTLTensorExtents CreateLogicalTensorExtents(ReadOnlySpan<uint> values)
        {
            return CreateTensorExtents(values, reverseOrder: false);
        }

        internal static uint[] CalculateNativeTensorStrides(in RHIMLTensorDescriptor descriptor)
        {
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            uint[] nativeDimensions = Reverse(dimensions);
            uint[] nativeStrides = new uint[nativeDimensions.Length];
            if (nativeStrides.Length == 0)
            {
                return nativeStrides;
            }

            if (RHIMLHelpers.HasExplicitStrides(descriptor))
            {
                nativeStrides = Reverse(descriptor.Strides!.Value.Span);
                ValidateMachineLearningStrides(descriptor, nativeDimensions, nativeStrides);
                return nativeStrides;
            }

            nativeStrides[0] = 1;
            uint elementSize = RHIMLHelpers.GetElementSize(descriptor.DataType);
            for (int i = 1; i < nativeStrides.Length; ++i)
            {
                ulong stride = (ulong)nativeStrides[i - 1] * nativeDimensions[i - 1];
                if (i == 1 && (descriptor.UsageFlag & ERHITensorUsage.MachineLearning) != 0)
                {
                    stride = AlignUp(stride * elementSize, 64UL) / elementSize;
                }

                nativeStrides[i] = checked((uint)stride);
            }

            ValidateMachineLearningStrides(descriptor, nativeDimensions, nativeStrides);
            return nativeStrides;
        }

        internal static ulong CalculateNativeBufferByteLength(in RHIMLTensorDescriptor descriptor)
        {
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            if (dimensions.Length == 0)
            {
                return 0;
            }

            uint[] nativeDimensions = Reverse(dimensions);
            uint[] nativeStrides = CalculateNativeTensorStrides(descriptor);
            for (int i = 0; i < nativeDimensions.Length; ++i)
            {
                if (nativeDimensions[i] == 0)
                {
                    return 0;
                }
            }

            int outermost = nativeDimensions.Length - 1;
            return (ulong)nativeStrides[outermost] * nativeDimensions[outermost] * RHIMLHelpers.GetElementSize(descriptor.DataType);
        }

        internal static void PackContiguousToNative(ReadOnlySpan<byte> source, Span<byte> destination, in RHIMLTensorDescriptor descriptor)
        {
            CopyBetweenContiguousAndNative(source, destination, descriptor, contiguousToNative: true);
        }

        internal static void UnpackNativeToContiguous(ReadOnlySpan<byte> source, Span<byte> destination, in RHIMLTensorDescriptor descriptor)
        {
            CopyBetweenContiguousAndNative(source, destination, descriptor, contiguousToNative: false);
        }

        internal static MTLTensorExtents CreateNativeTensorExtentsFromNativeOrder(ReadOnlySpan<uint> values)
        {
            return CreateTensorExtents(values, reverseOrder: false);
        }

        private static unsafe MTLTensorExtents CreateTensorExtents(ReadOnlySpan<uint> values, bool reverseOrder)
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
                int sourceIndex = reverseOrder ? values.Length - 1 - i : i;
                nativeValues[i] = checked((nint)values[sourceIndex]);
            }

            fixed (nint* nativeValuesPtr = nativeValues)
            {
                return MTLTensorExtents.New((ulong)nativeValues.Length, (IntPtr)nativeValuesPtr);
            }
        }

        private static uint[] Reverse(ReadOnlySpan<uint> values)
        {
            uint[] reversed = new uint[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                reversed[i] = values[values.Length - 1 - i];
            }

            return reversed;
        }

        private static ulong AlignUp(ulong value, ulong alignment)
        {
            return ((value + alignment - 1UL) / alignment) * alignment;
        }

        private static void ValidateMachineLearningStrides(in RHIMLTensorDescriptor descriptor, ReadOnlySpan<uint> nativeDimensions, ReadOnlySpan<uint> nativeStrides)
        {
            if ((descriptor.UsageFlag & ERHITensorUsage.MachineLearning) == 0 || nativeStrides.Length <= 1)
            {
                return;
            }

            uint elementSize = RHIMLHelpers.GetElementSize(descriptor.DataType);
            if ((nativeStrides[1] * elementSize) % 64 != 0)
            {
                throw new InvalidOperationException("Metal ML tensor stride[1] must be 64-byte aligned.");
            }

            for (int i = 2; i < nativeStrides.Length; ++i)
            {
                ulong expected = (ulong)nativeStrides[i - 1] * nativeDimensions[i - 1];
                if (nativeStrides[i] != expected)
                {
                    throw new InvalidOperationException($"Metal ML tensor stride[{i}] must equal stride[{i - 1}] * dimension[{i - 1}].");
                }
            }
        }

        private static void CopyBetweenContiguousAndNative(ReadOnlySpan<byte> source, Span<byte> destination, in RHIMLTensorDescriptor descriptor, bool contiguousToNative)
        {
            uint elementSize = RHIMLHelpers.GetElementSize(descriptor.DataType);
            ulong elementCount = RHIMLHelpers.CalculateElementCount(descriptor);
            ulong contiguousByteLength = elementCount * elementSize;
            ulong nativeByteLength = CalculateNativeBufferByteLength(descriptor);
            if (source.Length < (int)(contiguousToNative ? contiguousByteLength : nativeByteLength)
                || destination.Length < (int)(contiguousToNative ? nativeByteLength : contiguousByteLength))
            {
                throw new InvalidOperationException("Metal ML tensor pack/unpack buffer is too small.");
            }

            if (elementCount == 0)
            {
                return;
            }

            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            uint[] nativeStrides = CalculateNativeTensorStrides(descriptor);
            int rank = dimensions.Length;
            uint[] coords = new uint[rank];

            for (ulong elementIndex = 0; elementIndex < elementCount; ++elementIndex)
            {
                ulong remaining = elementIndex;
                for (int axis = rank - 1; axis >= 0; --axis)
                {
                    coords[axis] = (uint)(remaining % dimensions[axis]);
                    remaining /= dimensions[axis];
                }

                ulong nativeElementOffset = 0;
                for (int axis = 0; axis < rank; ++axis)
                {
                    int nativeAxis = rank - 1 - axis;
                    nativeElementOffset += coords[axis] * (ulong)nativeStrides[nativeAxis];
                }

                int contiguousByteOffset = checked((int)(elementIndex * elementSize));
                int nativeByteOffset = checked((int)(nativeElementOffset * elementSize));
                if (contiguousToNative)
                {
                    source.Slice(contiguousByteOffset, (int)elementSize).CopyTo(destination.Slice(nativeByteOffset, (int)elementSize));
                }
                else
                {
                    source.Slice(nativeByteOffset, (int)elementSize).CopyTo(destination.Slice(contiguousByteOffset, (int)elementSize));
                }
            }
        }

        internal static void ReleaseNativeObject(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            ObjectiveCRuntime.Release(nativePtr);
        }

        internal static MTLTensorDataType ConvertDataType(in ERHIMLDataType dataType)
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

        internal static MTLTensorUsage ConvertTensorUsage(in ERHITensorUsage usage)
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

        internal static MTLResourceOptions ConvertStorageMode(in ERHIStorageMode storageMode)
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

        public override RHITensorView CreateView(in RHITensorViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new MetalTensorView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_NativeTensor.NativePtr != IntPtr.Zero)
            {
                MTLBuffer nativeBuffer = m_NativeTensor.Buffer;
                if (nativeBuffer.NativePtr != IntPtr.Zero)
                {
                    m_MetalDevice.RemoveResidencyAllocation(nativeBuffer);
                }

                ObjectiveCRuntime.Release(m_NativeTensor);
                m_NativeTensor = default;
            }

            m_BackingBuffer = null;
        }
    }

    internal sealed class MetalTensorView : RHITensorView
    {
        internal MetalTensor ParentTensor => (MetalTensor)Parent;
        internal MTLTensor NativeTensor => m_NativeTensor;
        internal MetalBuffer? BackingBuffer => m_BackingBuffer;
        internal ulong BackingBufferOffset => m_AbsoluteOffset;
        internal ulong ByteLength => m_ByteLength;

        private MTLTensor m_NativeTensor;
        private MetalBuffer? m_BackingBuffer;
        private readonly ulong m_AbsoluteOffset;
        private readonly ulong m_ByteLength;

        internal MetalTensorView(MetalTensor parent, in RHITensorViewDescriptor descriptor)
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
            if (parent.BackingBuffer == null)
            {
                throw new InvalidOperationException("Metal tensor views require a buffer-backed parent tensor.");
            }

            m_Parent = parent;
            m_BackingBuffer = parent.BackingBuffer;
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
                    $"Metal tensor view range [{m_AbsoluteOffset}, {m_AbsoluteOffset + m_ByteLength}) exceeds backing buffer size {backingByteSize}.");
            }

            MTLTensorDescriptor nativeDescriptor = MTLTensorDescriptor.New();
            MTLTensorExtents nativeDimensions = default;
            MTLTensorExtents nativeStrides = default;
            try
            {
                nativeDescriptor.DataType = MetalTensor.ConvertDataType(layout.DataType);
                nativeDescriptor.Usage = MetalTensor.ConvertTensorUsage(layout.UsageFlag);
                nativeDescriptor.ResourceOptions = MetalTensor.ConvertStorageMode(layout.StorageMode);
                nativeDimensions = MetalTensor.CreateNativeTensorExtents(layout.Dimensions.Span);
                nativeDescriptor.Dimensions = nativeDimensions;
                nativeStrides = MetalTensor.CreateNativeTensorExtentsFromNativeOrder(
                    MetalTensor.CalculateNativeTensorStrides(layout));
                nativeDescriptor.Strides = nativeStrides;

                NSError tensorError = default;
                m_NativeTensor = parent.BackingBuffer.NativeBuffer.NewTensor(nativeDescriptor, m_AbsoluteOffset, ref tensorError);
                if (m_NativeTensor.NativePtr == IntPtr.Zero)
                {
                    string errorText = tensorError.NativePtr != IntPtr.Zero ? tensorError.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to create buffer-backed MTLTensor view: {errorText}");
                }
            }
            finally
            {
                MetalTensor.ReleaseNativeObject(nativeStrides);
                MetalTensor.ReleaseNativeObject(nativeDimensions);
                MetalTensor.ReleaseNativeObject(nativeDescriptor);
            }
        }

        protected override void Release()
        {
            if (m_NativeTensor.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeTensor);
                m_NativeTensor = default;
            }

            m_BackingBuffer = null;
            m_Parent = null;
        }
    }
}
