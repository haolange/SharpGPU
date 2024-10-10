using System;
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

    public struct RHIDeviceInfo
    {
        public string Name;
        public RHIVendorId VendorId;
        public RHIDeviceId DeviceId;
        public ERHIDeviceType Type;
        public RHIDeviceLimit Limit;
        public RHIDeviceFeature Feature;
    }

    public abstract class RHIDevice : Disposal
    {
        public RHIDeviceInfo DeviceInfo => m_DeviceInfo;

        protected RHIDeviceInfo m_DeviceInfo;

        public abstract RHIFence CreateFence();
        public abstract RHISemaphore CreateSemaphore();
        public abstract RHIQuery CreateQuery(in RHIQueryDescriptor descriptor);
        public abstract RHIHeap CreateHeap(in RHIHeapDescription descriptor);
        public abstract RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor);
        public abstract RHITexture CreateTexture(in RHITextureDescriptor descriptor);
        public abstract RHISampler CreateSampler(in RHISamplerDescriptor descriptor);
        public abstract RHIStorageQueue CreateStorageQueue();
        public abstract RHICommandQueue CreateCommandQueue(in ERHIPipelineType pipeline);
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
