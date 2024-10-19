using System;
using Infinity.Collections;
using System.Collections.Generic;
using Infinity.Core;

namespace Infinity.Graphics
{
    public class RHIDeviceLimit
    {

    }

    public class RHIDeviceFeature
    {
        public bool IsFlipProjection;
        public bool IsRaytracingSupported;
        public bool IsMeshShadingSupported;
        public bool IsAtomicUInt64Supported;
        public bool IsDrawIndirectSupported;
        public bool IsWaveOperationSupported;
        public bool IsDrawMultiIndirectSupported;
        public bool IsPixelShaderUAVSupported;
        public bool IsDepthbufferFetchSupported;
        public bool IsFramebufferFetchSupported;
        public bool IsRaytracingInlineSupported;
        public bool IsHiddenSurfaceRemovalSupported;
        public bool IsShaderBarycentricCoordSupported;
        public bool IsProgrammableSamplePositionSupported;
        public ERHIMatrixMajorons MatrixMajorons;
        public ERHIDepthValueRange DepthValueRange;
        public ERHIMultiviewStrategy MultiviewStrategy;
    }

    public struct RHIVendorId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
        public ERHIVendorType StringValue => (ERHIVendorType)IntValue;
    }

    public struct RHIDeviceId
    {
        public uint IntValue;
        public string DecimalValue => String.Format("0x{0:X}", IntValue);
    }

    public abstract class RHIDevice : Disposal
    {
        public string? Name => m_Name;
        public RHIVendorId VendorId => m_VendorId;
        public RHIDeviceId DeviceId => m_DeviceId;
        public ERHIDeviceType Type => m_Type;
        public RHIDeviceLimit? Limit => m_Limit;
        public RHIDeviceFeature? Feature => m_Feature;
        public int ComputeQueueCount => m_ComputeQueueCount;
        public int TransferQueueCount => m_TransferQueueCount;
        public int GraphicsQueueCount => m_GraphicsQueueCount;

        protected string? m_Name;
        protected RHIVendorId m_VendorId;
        protected RHIDeviceId m_DeviceId;
        protected ERHIDeviceType m_Type;
        protected RHIDeviceLimit? m_Limit;
        protected RHIDeviceFeature? m_Feature;
        protected int m_ComputeQueueCount;
        protected int m_TransferQueueCount;
        protected int m_GraphicsQueueCount;
        protected Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>? m_CommandQueueMap;

        public abstract RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index);
        public abstract RHIFence CreateFence();
        public abstract RHISemaphore CreateSemaphore();
        public abstract RHIQuery CreateQuery(in RHIQueryDescriptor descriptor);
        public abstract RHIHeap CreateHeap(in RHIHeapDescription descriptor);
        public abstract RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor);
        public abstract RHITexture CreateTexture(in RHITextureDescriptor descriptor);
        public abstract RHISampler CreateSampler(in RHISamplerDescriptor descriptor);
        public abstract RHIStorageQueue CreateStorageQueue();
        public abstract RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
        public abstract RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor);
        public abstract RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor);
        public abstract RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor);
        public abstract RHIResourceTableLayout CreateResourceTableLayout(in RHIResourceTableLayoutDescriptor descriptor);
        public abstract RHIResourceTable CreateResourceTable(in RHIResourceTableDescriptor descriptor);
        public abstract RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor);
        public abstract RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor);
        public abstract RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor);
        public abstract RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor);
    }
}
