using System;
using Infinity.Core;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal class MetalTensor : RHITensor
    {
        internal MTLTensor NativeTensor => m_NativeTensor;

        private MTLTensor m_NativeTensor;

        public MetalTensor(MetalDevice device, in RHIMLTensorDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            MTLTensorDescriptor nativeDescriptor = MTLTensorDescriptor.New();
            nativeDescriptor.DataType = ConvertDataType(descriptor.DataType);
            nativeDescriptor.Usage = ConvertTensorUsage(descriptor.UsageFlag);
            nativeDescriptor.ResourceOptions = ConvertStorageMode(descriptor.StorageMode);

            m_NativeTensor = device.NativeDevice.NewTensor(nativeDescriptor);

            ObjectiveCRuntime.Release(nativeDescriptor);
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
        }
    }
}
