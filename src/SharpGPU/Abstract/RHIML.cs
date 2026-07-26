using SharpGPU.Core;
using System;

namespace SharpGPU
{
    public enum ERHIMLTensorBindingKind : byte
    {
        Input = 0,
        Output = 1,
        Pending = 255
    }

    public struct RHIMLTensorBindingInfo
    {
        public string Name;
        public uint Index;
        public ERHIMLTensorBindingKind Kind;
        public RHIMLTensorDescriptor Descriptor;
    }

    public abstract class RHIMLProgram : Disposal
    {
        public string? Name => m_Name;

        protected string? m_Name;
    }

    public struct RHIMLPipelineDescriptor
    {
        public string Name;
        public RHIMLBinary Binary;
    }

    public abstract class RHIMLPipeline : Disposal
    {
        public RHIMLPipelineDescriptor Descriptor => m_Descriptor;

        public ulong TemporaryResourceSize => m_TemporaryResourceSize;
        public ulong PersistentResourceSize => m_PersistentResourceSize;
        public uint InputCount => m_InputCount;
        public uint OutputCount => m_OutputCount;
        public ReadOnlyMemory<RHIMLTensorBindingInfo> BindingInfos => m_BindingInfos ?? ReadOnlyMemory<RHIMLTensorBindingInfo>.Empty;

        protected RHIMLPipelineDescriptor m_Descriptor;
        protected ulong m_TemporaryResourceSize;
        protected ulong m_PersistentResourceSize;
        protected uint m_InputCount;
        protected uint m_OutputCount;
        protected RHIMLTensorBindingInfo[]? m_BindingInfos;
    }

    public struct RHIMLBindingSetDescriptor
    {
        public RHIMLPipeline Pipeline;
        public Memory<RHITensor> Inputs;
        public Memory<RHITensor> Outputs;
    }

    public abstract class RHIMLBindingSet : Disposal
    {
        public RHIMLPipeline? Pipeline => m_Pipeline;

        protected RHIMLPipeline? m_Pipeline;
    }

    internal static class RHIMLHelpers
    {
        internal static bool HasExplicitStrides(in RHIMLTensorDescriptor descriptor)
        {
            return descriptor.Strides is Memory<uint> strides && strides.Length > 0;
        }

        public static uint GetElementSize(in ERHIMLDataType dataType)
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
                _ => throw new ArgumentOutOfRangeException(nameof(dataType), $"Unsupported ML data type '{dataType}'."),
            };
        }

        public static ulong CalculateElementCount(in RHIMLTensorDescriptor descriptor)
        {
            ulong elementCount = 1;
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            for (int i = 0; i < dimensions.Length; ++i)
            {
                elementCount *= dimensions[i];
            }

            return elementCount;
        }

        public static ulong CalculateMinimumByteLength(in RHIMLTensorDescriptor descriptor)
        {
            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            if (dimensions.Length == 0)
            {
                return 0;
            }

            uint elementSize = GetElementSize(descriptor.DataType);
            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                ReadOnlySpan<uint> strides = explicitStrides.Span;
                if (strides.Length != dimensions.Length)
                {
                    throw new InvalidOperationException($"Tensor stride rank mismatch. dimensions={dimensions.Length}, strides={strides.Length}.");
                }

                ulong offsetInElements = 0;
                for (int i = 0; i < dimensions.Length; ++i)
                {
                    if (dimensions[i] == 0)
                    {
                        return 0;
                    }

                    offsetInElements += (ulong)(dimensions[i] - 1) * strides[i];
                }

                return (offsetInElements + 1) * elementSize;
            }

            return CalculateElementCount(descriptor) * elementSize;
        }

        public static uint[] GetEffectiveStrides(in RHIMLTensorDescriptor descriptor)
        {
            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                return explicitStrides.ToArray();
            }

            ReadOnlySpan<uint> dimensions = descriptor.Dimensions.Span;
            uint[] strides = new uint[dimensions.Length];
            ulong runningStride = 1;
            for (int i = dimensions.Length - 1; i >= 0; --i)
            {
                strides[i] = checked((uint)runningStride);
                runningStride *= dimensions[i];
            }

            return strides;
        }

        public static RHIMLTensorDescriptor CloneLayoutDescriptor(in RHIMLTensorDescriptor descriptor)
        {
            RHIMLTensorDescriptor clone = descriptor;
            clone.Dimensions = descriptor.Dimensions.ToArray();
            clone.Strides =
                descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0
                    ? explicitStrides.ToArray() : null;
            clone.BackingBuffer = null;
            clone.BackingBufferOffset = 0;
            return clone;
        }

        public static bool HasCompatibleLayout(in RHIMLTensorDescriptor expected, in RHIMLTensorDescriptor actual)
        {
            if (expected.DataType != actual.DataType)
            {
                return false;
            }

            ReadOnlySpan<uint> expectedDimensions = expected.Dimensions.Span;
            ReadOnlySpan<uint> actualDimensions = actual.Dimensions.Span;
            if (expectedDimensions.Length != actualDimensions.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedDimensions.Length; ++i)
            {
                if (expectedDimensions[i] != actualDimensions[i])
                {
                    return false;
                }
            }

            ReadOnlySpan<uint> expectedStrides = GetEffectiveStrides(expected);
            ReadOnlySpan<uint> actualStrides = GetEffectiveStrides(actual);
            if (expectedStrides.Length != actualStrides.Length)
            {
                return false;
            }

            for (int i = 0; i < expectedStrides.Length; ++i)
            {
                if (expectedStrides[i] != actualStrides[i])
                {
                    return false;
                }
            }

            return true;
        }

        public static string DescribeLayout(in RHIMLTensorDescriptor descriptor)
        {
            string dimensions = string.Join("x", descriptor.Dimensions.ToArray());
            string strides = string.Join(",", GetEffectiveStrides(descriptor));
            return $"{descriptor.DataType}[{dimensions}] strides=[{strides}]";
        }
    }
}

