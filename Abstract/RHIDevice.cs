using System;
using Infinity.Collections;
using System.Collections.Generic;
using Infinity.Core;

namespace Infinity.Graphics
{
    public class RHIDeviceLimit
    {
        public readonly int MaxMSAACount;
        public readonly int MaxBoundTexture;
        public readonly int MinWavefrontSize;
        public readonly int MaxWavefrontSize;
        public readonly int MaxComputeThreads;
        public readonly int UniformBufferAlignment;
        public readonly int UploadBufferAlignment;
        public readonly int UploadBufferTextureAlignment;
        public readonly int UploadBufferTextureRowAlignment;
        public readonly int MaxVertexInputBindings;

        internal RHIDeviceLimit(in int maxMSAACount,
                                in int maxBoundTexture,
                                in int minWavefrontSize,
                                in int maxWavefrontSize,
                                in int maxComputeThreads,
                                in int uniformBufferAlignment,
                                in int uploadBufferAlignment,
                                in int uploadBufferTextureAlignment,
                                in int uploadBufferTextureRowAlignment,
                                in int maxVertexInputBindings)
        {
            MaxMSAACount = maxMSAACount;
            MaxBoundTexture = maxBoundTexture;
            MinWavefrontSize = minWavefrontSize;
            MaxWavefrontSize = maxWavefrontSize;
            MaxComputeThreads = maxComputeThreads;
            UniformBufferAlignment = uniformBufferAlignment;
            UploadBufferAlignment = uploadBufferAlignment;
            UploadBufferTextureAlignment = uploadBufferTextureAlignment;
            UploadBufferTextureRowAlignment = uploadBufferTextureRowAlignment;
            MaxVertexInputBindings = maxVertexInputBindings;
        }
    }

    public class RHIDeviceFeature
    {
        public readonly bool IsFlipProjection;
        public readonly bool IsHDRPresentSupported;
        public readonly bool IsUnifiedMemorySupported;
        public readonly bool IsRootConstantSupport;
        public readonly bool IsIndirectRootConstantSupport;
        public readonly bool IsPixelShaderUAVSupported;
        public readonly bool IsRasterizerOrderedSupported;
        public readonly bool IsAnisotropyTextureSupported;
        public readonly bool IsDepthbufferFetchSupported;
        public readonly bool IsFramebufferFetchSupported;
        public readonly bool IsTimestampQueriesSupported;
        public readonly bool IsOcclusionQueriesSupported;
        public readonly bool IsPipelineStatsQueriesSupported;
        public readonly bool IsAtomicUInt64Supported;
        public readonly bool IsWorkgraphSupported;
        public readonly bool IsMeshShadingSupported;
        public readonly bool IsDrawIndirectSupported;
        public readonly bool IsDrawMultiIndirectSupported;
        public readonly bool IsRaytracingSupported;
        public readonly bool IsRaytracingInlineSupported;
        public readonly bool IsVariableRateShadingSupported;
        public readonly bool IsHiddenSurfaceRemovalSupported;
        public readonly bool IsBarycentricCoordSupported;
        public readonly bool IsProgrammableSamplePositionSupported;
        public readonly ERHIMatrixMajorons MatrixMajorons;
        public readonly ERHIDepthValueRange DepthValueRange;
        public readonly ERHIMultiviewStrategy MultiviewStrategy;
        public readonly ERHIWaveOperationStrategy WaveOperationStrategy;

        internal RHIDeviceFeature(in bool isFlipProjection,
                                in bool isHDRPresentSupported,
                                in bool isUnifiedMemorySupported,
                                in bool isRootConstantSupport,
                                in bool isIndirectRootConstantSupport,
                                in bool isPixelShaderUAVSupported,
                                in bool isRasterizerOrderedSupported,
                                in bool isAnisotropyTextureSupported,
                                in bool isDepthbufferFetchSupported,
                                in bool isFramebufferFetchSupported,
                                in bool isTimestampQueriesSupported,
                                in bool isOcclusionQueriesSupported,
                                in bool isPipelineStatsQueriesSupported,
                                in bool isAtomicUInt64Supported,
                                in bool isWorkgraphSupported,
                                in bool isMeshShadingSupported,
                                in bool isDrawIndirectSupported,
                                in bool isDrawMultiIndirectSupported,
                                in bool isRaytracingSupported,
                                in bool isRaytracingInlineSupported,
                                in bool isVariableRateShadingSupported,
                                in bool isHiddenSurfaceRemovalSupported,
                                in bool isBarycentricCoordSupported,
                                in bool isProgrammableSamplePositionSupported,
                                in ERHIMatrixMajorons matrixMajorons,
                                in ERHIDepthValueRange depthValueRange,
                                in ERHIMultiviewStrategy multiviewStrategy,
                                in ERHIWaveOperationStrategy waveOperationStrategy)
        {
            IsFlipProjection = isFlipProjection;
            IsHDRPresentSupported = isHDRPresentSupported;
            IsUnifiedMemorySupported = isUnifiedMemorySupported;
            IsRootConstantSupport = isRootConstantSupport;
            IsIndirectRootConstantSupport = isIndirectRootConstantSupport;
            IsPixelShaderUAVSupported = isPixelShaderUAVSupported;
            IsRasterizerOrderedSupported = isRasterizerOrderedSupported;
            IsAnisotropyTextureSupported = isAnisotropyTextureSupported;
            IsDepthbufferFetchSupported = isDepthbufferFetchSupported;
            IsFramebufferFetchSupported = isFramebufferFetchSupported;
            IsTimestampQueriesSupported = isTimestampQueriesSupported;
            IsOcclusionQueriesSupported = isOcclusionQueriesSupported;
            IsPipelineStatsQueriesSupported = isPipelineStatsQueriesSupported;
            IsAtomicUInt64Supported = isAtomicUInt64Supported;
            IsWorkgraphSupported = isWorkgraphSupported;
            IsMeshShadingSupported = isMeshShadingSupported;
            IsDrawIndirectSupported = isDrawIndirectSupported;
            IsDrawMultiIndirectSupported = isDrawMultiIndirectSupported;
            IsRaytracingSupported = isRaytracingSupported;
            IsRaytracingInlineSupported = isRaytracingInlineSupported;
            IsVariableRateShadingSupported = isVariableRateShadingSupported;
            IsHiddenSurfaceRemovalSupported = isHiddenSurfaceRemovalSupported;
            IsBarycentricCoordSupported = isBarycentricCoordSupported;
            IsProgrammableSamplePositionSupported = isProgrammableSamplePositionSupported;
            MatrixMajorons = matrixMajorons;
            DepthValueRange = depthValueRange;
            MultiviewStrategy = multiviewStrategy;
            WaveOperationStrategy = waveOperationStrategy;
        }
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
