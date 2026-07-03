using System;
using SharpGPU.Collections;
using System.Collections.Generic;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12DeviceLimit : RHIDeviceLimit
    {
        public readonly Vortice.Direct3D.FeatureLevel NativeMaxFeatureLevel;

        internal Dx12DeviceLimit(in int uniformBufferAlignment,
                                in int uploadBufferAlignment,
                                in int uploadBufferTextureAlignment,
                                in int uploadBufferTextureRowAlignment,
                                in int maxMSAACount,
                                in int maxBoundTexture,
                                in int minWavefrontSize,
                                in int maxWavefrontSize,
                                in int maxComputeThreads,
                                in int maxGroupShareMemorySize,
                                in int maxVertexInputBindings,
                                in int maxColorAttachments,
                                in int maxTexture2DSize,
                                in int maxTextureCubeSize,
                                in Vortice.Direct3D.FeatureLevel nativeMaxFeatureLevel) : base(uniformBufferAlignment,
                                                                                   uploadBufferAlignment,
                                                                                   uploadBufferTextureAlignment,
                                                                                   uploadBufferTextureRowAlignment,
                                                                                   maxMSAACount,
                                                                                   maxBoundTexture,
                                                                                   minWavefrontSize,
                                                                                   maxWavefrontSize,
                                                                                   maxComputeThreads,
                                                                                   maxGroupShareMemorySize,
                                                                                   maxVertexInputBindings,
                                                                                   maxColorAttachments,
                                                                                   maxTexture2DSize,
                                                                                   maxTextureCubeSize)
        {
            NativeMaxFeatureLevel = nativeMaxFeatureLevel;
        }
    }

    internal unsafe class Dx12DeviceFeature : RHIDeviceFeature
    {
        public readonly bool IsEnhancedBarriersSupported;
        public readonly bool IsNativeRenderPassSupported;

        internal Dx12DeviceFeature(in bool isFlipProjection,
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
                                in bool isMLSupported,
                                in ERHIMatrixMajorons matrixMajorons,
                                in ERHIDepthValueRange depthValueRange,
                                in ERHIMultiviewStrategy multiviewStrategy,
                                in ERHIWaveOperationStrategy waveOperationStrategy,
                                in bool isEnhancedBarriersSupported,
                                in bool isNativeRenderPassSupported) : base(isFlipProjection,
                                                                        isHDRPresentSupported,
                                                                        isUnifiedMemorySupported,
                                                                        isRootConstantSupport,
                                                                        isIndirectRootConstantSupport,
                                                                        isPixelShaderUAVSupported,
                                                                        isRasterizerOrderedSupported,
                                                                        isAnisotropyTextureSupported,
                                                                        isDepthbufferFetchSupported,
                                                                        isFramebufferFetchSupported,
                                                                        isTimestampQueriesSupported,
                                                                        isOcclusionQueriesSupported,
                                                                        isPipelineStatsQueriesSupported,
                                                                        isAtomicUInt64Supported,
                                                                        isWorkgraphSupported,
                                                                        isMeshShadingSupported,
                                                                        isDrawIndirectSupported,
                                                                        isDrawMultiIndirectSupported,
                                                                        isRaytracingSupported,
                                                                        isRaytracingInlineSupported,
                                                                        isVariableRateShadingSupported,
                                                                        isHiddenSurfaceRemovalSupported,
                                                                        isBarycentricCoordSupported,
                                                                        isProgrammableSamplePositionSupported,
                                                                        isMLSupported,
                                                                        matrixMajorons,
                                                                        depthValueRange,
                                                                        multiviewStrategy,
                                                                        waveOperationStrategy)
        {
            IsEnhancedBarriersSupported = isEnhancedBarriersSupported;
            IsNativeRenderPassSupported = isNativeRenderPassSupported;
        }
    }

    internal unsafe class Dx12Device : RHIDevice
    {
        public Dx12Instance Dx12Instance
        {
            get
            {
                return m_Dx12Instance;
            }
        }
        public Vortice.DXGI.IDXGIAdapter1 DXGIAdapter
        {
            get
            {
                return m_DXGIAdapter;
            }
        }
        public Vortice.Direct3D12.ID3D12Device10 NativeDevice
        {
            get
            {
                return m_NativeDevice;
            }
        }
        public Dx12DescriptorHeap DescriptorHeapDSV
        {
            get
            {
                return m_DescriptorHeapDSV;
            }
        }
        public Dx12DescriptorHeap DescriptorHeapHeapRTV
        {
            get
            {
                return m_DescriptorHeapHeapRTV;
            }
        }
        public Dx12DescriptorHeap DescriptorHeapSampler
        {
            get
            {
                return m_DescriptorHeapSampler;
            }
        }
        public Dx12DescriptorHeap DescriptorHeapCbvSrvUav
        {
            get
            {
                return m_DescriptorHeapCbvSrvUav;
            }
        }
        public Dx12DescriptorHeap StagingHeapCbvSrvUav
        {
            get
            {
                return m_StagingHeapCbvSrvUav;
            }
        }
        public Dx12DescriptorHeap StagingHeapSampler
        {
            get
            {
                return m_StagingHeapSampler;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandSignature DrawIndirectSignature
        {
            get
            {
                return m_DrawIndirectSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandSignature DrawIndexedIndirectSignature
        {
            get
            {
                return m_DrawIndexedIndirectSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchRayIndirectSignature
        {
            get
            {
                return m_DispatchRayIndirectSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchMeshIndirectSignature
        {
            get
            {
                return m_DispatchMeshIndirectSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchComputeIndirectSignature
        {
            get
            {
                return m_DispatchComputeIndirectSignature;
            }
        }
        internal Vortice.DirectML.IDMLDevice DirectMLDevice
        {
            get
            {
                return m_DirectMLDevice ?? throw new InvalidOperationException("DirectML device is unavailable on this DX12 device.");
            }
        }
        internal Vortice.DirectML.IDMLDevice1? DirectMLDevice1
        {
            get
            {
                return m_DirectMLDevice1;
            }
        }
        internal Vortice.DirectML.IDMLCommandRecorder DirectMLCommandRecorder
        {
            get
            {
                return m_DirectMLCommandRecorder ?? throw new InvalidOperationException("DirectML command recorder is unavailable on this DX12 device.");
            }
        }
        internal bool SupportsDirectML => m_DirectMLDevice != null && m_DirectMLCommandRecorder != null;
        internal bool IsEnhancedBarriersSupported
        {
            get
            {
                return (m_Feature as Dx12DeviceFeature)?.IsEnhancedBarriersSupported ?? false;
            }
        }
        internal bool IsNativeRenderPassSupported
        {
            get
            {
                return (m_Feature as Dx12DeviceFeature)?.IsNativeRenderPassSupported ?? false;
            }
        }

        private Dx12Instance m_Dx12Instance;
        private Vortice.DXGI.IDXGIAdapter1 m_DXGIAdapter;
        private Vortice.Direct3D12.ID3D12Device10 m_NativeDevice;
        private Dx12DescriptorHeap m_DescriptorHeapDSV;
        private Dx12DescriptorHeap m_DescriptorHeapHeapRTV;
        private Dx12DescriptorHeap m_DescriptorHeapSampler;
        private Dx12DescriptorHeap m_DescriptorHeapCbvSrvUav;
        private Dx12DescriptorHeap m_StagingHeapCbvSrvUav;
        private Dx12DescriptorHeap m_StagingHeapSampler;
        private Vortice.Direct3D12.ID3D12CommandSignature m_DrawIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature m_DrawIndexedIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature m_DispatchRayIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature m_DispatchMeshIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature m_DispatchComputeIndirectSignature;
        private Vortice.DirectML.IDMLDevice? m_DirectMLDevice;
        private Vortice.DirectML.IDMLDevice1? m_DirectMLDevice1;
        private Vortice.DirectML.IDMLCommandRecorder? m_DirectMLCommandRecorder;
        private bool m_OwnsDXGIAdapter;

        public Dx12Device(Dx12Instance instance, in Vortice.DXGI.IDXGIAdapter1 adapter, in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_DXGIAdapter = adapter;
            m_Dx12Instance = instance;

            Vortice.DXGI.AdapterDescription1 adapterDesc = m_DXGIAdapter.Description1;

            m_Name = adapterDesc.Description;
            m_Type = (adapterDesc.Flags & Vortice.DXGI.AdapterFlags.Software) != 0 ? ERHIDeviceType.Software : ERHIDeviceType.Hardware;
            m_VendorId.IntValue = adapterDesc.VendorId;
            m_DeviceId.IntValue = adapterDesc.DeviceId;

            CreateDevice();
            CreateDirectMLObjects();
            CheckFeatureSupport();
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
            CreateDescriptorHeaps();
            CreateCommandSignatures();
            m_OwnsDXGIAdapter = true;
        }

        public override RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index)
        {
            if (m_CommandQueueMap.TryGetValue(pipeline, out var cmdQueue))
            {
                if(index < cmdQueue.length)
                {
                    return cmdQueue[index];
                }
            }
            return null;
        }

        public override RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            return new Dx12SwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            return new Dx12Fence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            return new Dx12Semaphore(this);
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            return new Dx12StorageQueue(this);
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            return new Dx12Query(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            return new Dx12Heap(this, descriptor);
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            return new Dx12Buffer(this, descriptor);
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            return new Dx12Texture(this, descriptor);
        }

        public override RHISampler CreateSampler(in RHISamplerDescriptor descriptor)
        {
            return new Dx12Sampler(this, descriptor);
        }

        public override RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            return new Dx12TopLevelAccelStruct(this, descriptor);
        }

        public override RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            return new Dx12BottomLevelAccelStruct(this, descriptor);
        }

        public override RHIArgumentTableLayout CreateArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            return new Dx12ArgumentTableLayout(descriptor);
        }

        public override RHIArgumentTable CreateArgumentTable(in RHIArgumentTableDescriptor descriptor)
        {
            return new Dx12ArgumentTable(this, descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new Dx12PipelineLayout(this, descriptor);
        }

        public override RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor)
        {
            return new Dx12Function(descriptor);
        }

        public override RHIFunctionLibrary CreateFunctionLibrary(in RHIFunctionLibraryDescriptor descriptor)
        {
            return new Dx12FunctionLibrary(descriptor);
        }

        public override RHIFunctionTable CreateFunctionTable()
        {
            return new Dx12FunctionTable(this);
        }

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            return new Dx12ComputePipeline(this, descriptor);
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            if (Feature?.IsRaytracingSupported != true)
            {
                throw new NotSupportedException("DX12 raytracing is not supported by this adapter/driver.");
            }

            return new Dx12RaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            return new Dx12RasterPipeline(this, descriptor);
        }

        public override RHIPipelineLibrary CreatePipelineLibrary(in RHIPipelineLibraryDescriptor descriptor)
        {
            return new Dx12PipelineLibrary(this, descriptor);
        }

        public override RHIComputeIndirectCommandBuffer CreateComputeIndirectCommandBuffer(in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            return new Dx12ComputeIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRayTracingIndirectCommandBuffer CreateRayTracingIndirectCommandBuffer(in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            return new Dx12RayTracingIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRasterIndirectCommandBuffer CreateRasterIndirectCommandBuffer(in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            return new Dx12RasterIndirectCommandBuffer(this, descriptor);
        }

        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            if (Feature?.IsMLSupported != true)
            {
                throw new NotSupportedException("DX12 ML is not supported by this adapter/driver.");
            }

            return new Dx12MLPipeline(this, descriptor);
        }

        public override RHIMLBindingSet CreateMLBindingSet(in RHIMLBindingSetDescriptor descriptor)
        {
            if (Feature?.IsMLSupported != true)
            {
                throw new NotSupportedException("DX12 ML is not supported by this adapter/driver.");
            }

            return new Dx12MLBindingSet(this, descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            if (Feature?.IsMLSupported != true)
            {
                throw new NotSupportedException("DX12 ML tensors are not supported by this adapter/driver.");
            }

            return new Dx12Tensor(this, descriptor);
        }

        public override RHIMLProgram CreateMLProgram(in RHIMLProgramDescriptor descriptor)
        {
            if (Feature?.IsMLSupported != true)
            {
                throw new NotSupportedException("DX12 ML is not supported by this adapter/driver.");
            }

            return Dx12MLProgram.Create(descriptor);
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            if (Feature?.IsWorkgraphSupported != true)
            {
                throw new NotSupportedException("DX12 WorkGraph is not supported by this adapter/driver.");
            }

            return new Dx12WorkGraphPipeline(this, descriptor);
        }

        public Dx12DescriptorInfo AllocateDsvDescriptor(in int count)
        {
            int index = m_DescriptorHeapDSV.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapDSV.NativeCpuStartHandle.Offset(index, m_DescriptorHeapDSV.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapDSV.NativeGpuStartHandle.Offset(index, m_DescriptorHeapDSV.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapDSV.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateRtvDescriptor(in int count)
        {
            int index = m_DescriptorHeapHeapRTV.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapHeapRTV.NativeCpuStartHandle.Offset(index, m_DescriptorHeapHeapRTV.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapHeapRTV.NativeGpuStartHandle.Offset(index, m_DescriptorHeapHeapRTV.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapHeapRTV.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateSamplerDescriptor(in int count)
        {
            int index = m_DescriptorHeapSampler.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapSampler.NativeCpuStartHandle.Offset(index, m_DescriptorHeapSampler.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapSampler.NativeGpuStartHandle.Offset(index, m_DescriptorHeapSampler.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapSampler.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateCbvSrvUavDescriptor(in int count)
        {
            int index = m_DescriptorHeapCbvSrvUav.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapCbvSrvUav.NativeCpuStartHandle.Offset(index, m_DescriptorHeapCbvSrvUav.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapCbvSrvUav.NativeGpuStartHandle.Offset(index, m_DescriptorHeapCbvSrvUav.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapCbvSrvUav.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateStagingCbvSrvUavDescriptor(in int count)
        {
            int index = m_StagingHeapCbvSrvUav.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_StagingHeapCbvSrvUav.NativeCpuStartHandle.Offset(index, m_StagingHeapCbvSrvUav.DescriptorSize);
            descriptorInfo.GpuHandle = default;
            descriptorInfo.DescriptorHeap = m_StagingHeapCbvSrvUav.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateStagingSamplerDescriptor(in int count)
        {
            int index = m_StagingHeapSampler.Allocate(count);
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_StagingHeapSampler.NativeCpuStartHandle.Offset(index, m_StagingHeapSampler.DescriptorSize);
            descriptorInfo.GpuHandle = default;
            descriptorInfo.DescriptorHeap = m_StagingHeapSampler.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public void CopyDescriptors(Dx12DescriptorHeap srcHeap, in int srcIndex, Dx12DescriptorHeap dstHeap, in int dstIndex, in int count)
        {
            Vortice.Direct3D12.CpuDescriptorHandle srcHandle = srcHeap.NativeCpuStartHandle.Offset(srcIndex, srcHeap.DescriptorSize);
            Vortice.Direct3D12.CpuDescriptorHandle dstHandle = dstHeap.NativeCpuStartHandle.Offset(dstIndex, dstHeap.DescriptorSize);
            m_NativeDevice.CopyDescriptorsSimple((uint)count, dstHandle, srcHandle, dstHeap.NativeType);
        }

        public void FreeDsvDescriptor(in int index)
        {
            m_DescriptorHeapDSV.Free(index);
        }

        public void FreeRtvDescriptor(in int index)
        {
            m_DescriptorHeapHeapRTV.Free(index);
        }

        public void FreeSamplerDescriptor(in int index)
        {
            m_DescriptorHeapSampler.Free(index);
        }

        public void FreeCbvSrvUavDescriptor(in int index)
        {
            m_DescriptorHeapCbvSrvUav.Free(index);
        }

        public void FreeDsvDescriptor(in int index, in int count)
        {
            m_DescriptorHeapDSV.Free(index, count);
        }

        public void FreeRtvDescriptor(in int index, in int count)
        {
            m_DescriptorHeapHeapRTV.Free(index, count);
        }

        public void FreeSamplerDescriptor(in int index, in int count)
        {
            m_DescriptorHeapSampler.Free(index, count);
        }

        public void FreeCbvSrvUavDescriptor(in int index, in int count)
        {
            m_DescriptorHeapCbvSrvUav.Free(index, count);
        }

        public void FreeStagingCbvSrvUavDescriptor(in int index)
        {
            m_StagingHeapCbvSrvUav.Free(index);
        }

        public void FreeStagingCbvSrvUavDescriptor(in int index, in int count)
        {
            m_StagingHeapCbvSrvUav.Free(index, count);
        }

        public void FreeStagingSamplerDescriptor(in int index)
        {
            m_StagingHeapSampler.Free(index);
        }

        public void FreeStagingSamplerDescriptor(in int index, in int count)
        {
            m_StagingHeapSampler.Free(index, count);
        }

        private void CreateDevice()
        {
            Vortice.Direct3D12.ID3D12Device10 device;
            SharpGen.Runtime.Result hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_2, out device);
            if (hResult.Failure)
            {
                hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_1, out device);

                if (hResult.Failure)
                {
                    hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_0, out device);
                }
            }
            if (hResult.Failure)
            {
                throw new InvalidOperationException($"DX12 device creation failed for '{m_Name}'. HRESULT=0x{hResult.Code:X8}. {Dx12Agility.Diagnostic}");
            }

            m_NativeDevice = device;
        }

        private SharpGen.Runtime.Result CreateNativeDevice(Vortice.Direct3D.FeatureLevel featureLevel, out Vortice.Direct3D12.ID3D12Device10 device)
        {
            device = null!;

            if (Dx12Agility.TryGetDeviceFactory(out Vortice.Direct3D12.ID3D12DeviceFactory? deviceFactory))
            {
                SharpGen.Runtime.Result result = deviceFactory!.CreateDevice(m_DXGIAdapter, featureLevel, out Vortice.Direct3D12.ID3D12Device? baseDevice);
                if (result.Failure || baseDevice == null)
                {
                    return result;
                }

                try
                {
                    device = baseDevice.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12Device10>()
                        ?? throw new InvalidOperationException($"DX12 Agility device for '{m_Name}' does not expose ID3D12Device10 at feature level {featureLevel}. {Dx12Agility.Diagnostic}");
                    return SharpGen.Runtime.Result.Ok;
                }
                finally
                {
                    baseDevice.Release();
                }
            }

            return Vortice.Direct3D12.D3D12.D3D12CreateDevice(m_DXGIAdapter, featureLevel, out device);
        }

        private void CheckFeatureSupport()
        {
            int uniformBufferAlignment = 256;
            int uploadBufferAlignment = 256;
            int uploadBufferTextureAlignment = 256;
            int uploadBufferTextureRowAlignment = 256;
            int maxMSAACount = 16;
            int maxBoundTexture = 16;
            int minWavefrontSize = 32;
            int maxWavefrontSize = 32;
            int maxComputeThreads = 1024;
            int maxGroupShareMemorySize = 1024;
            int maxVertexInputBindings = 32;
            int maxColorAttachments = 8;
            int maxTexture2DSize = 16384;
            int maxTextureCubeSize = 8192;
            Vortice.Direct3D.FeatureLevel nativeMaxFeatureLevel;

            bool isFlipProjection = false;
            bool isHDRPresentSupported = false;
            bool isUnifiedMemorySupported = false;
            bool isRootConstantSupport = true;
            bool isIndirectRootConstantSupport = false;
            bool isPixelShaderUAVSupported = true;
            bool isRasterizerOrderedSupported = false;
            bool isAnisotropyTextureSupported = true;
            bool isDepthbufferFetchSupported = false;
            bool isFramebufferFetchSupported = false;
            bool isTimestampQueriesSupported = true;
            bool isOcclusionQueriesSupported = true;
            bool isPipelineStatsQueriesSupported = true;
            bool isAtomicUInt64Supported = true;
            bool isWorkgraphSupported = false;
            bool isMeshShadingSupported = false;
            bool isDrawIndirectSupported = true;
            bool isDrawMultiIndirectSupported = true;
            bool isRaytracingSupported = false;
            bool isRaytracingInlineSupported = false;
            bool isVariableRateShadingSupported = true;
            bool isHiddenSurfaceRemovalSupported = false;
            bool isBarycentricCoordSupported = false;
            bool isProgrammableSamplePositionSupported = false;
            bool isMLSupported = SupportsDirectML;
            ERHIMatrixMajorons matrixMajorons = ERHIMatrixMajorons.RowMajor;
            ERHIDepthValueRange depthValueRange = ERHIDepthValueRange.ZeroToOne;
            ERHIMultiviewStrategy multiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
            ERHIWaveOperationStrategy waveOperationStrategy = ERHIWaveOperationStrategy.Basic;
            bool isEnhancedBarriersSupported = false;
            bool isNativeRenderPassSupported = false;

            // check feature level
            Vortice.Direct3D.FeatureLevel* aLevels = stackalloc Vortice.Direct3D.FeatureLevel[3];
            {
                aLevels[0] = Vortice.Direct3D.FeatureLevel.Level_12_0;
                aLevels[1] = Vortice.Direct3D.FeatureLevel.Level_12_1;
                aLevels[2] = Vortice.Direct3D.FeatureLevel.Level_12_2;
            }
            Vortice.Direct3D12.FeatureDataFeatureLevels dLevels = default;
            dLevels.NumFeatureLevels = 3;
            dLevels.FeatureLevelsRequested = (IntPtr)aLevels;
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.FeatureLevels, ref dLevels);
            nativeMaxFeatureLevel = dLevels.MaxSupportedFeatureLevel;

            // check feature options
            Vortice.Direct3D12.FeatureDataD3D12Options featureOptions0 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options1 featureOptions1 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options2 featureOptions2 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options3 featureOptions3 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options4 featureOptions4 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options5 featureOptions5 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options6 featureOptions6 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options7 featureOptions7 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options8 featureOptions8 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options9 featureOptions9 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options10 featureOptions10 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options11 featureOptions11 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options12 featureOptions12 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options13 featureOptions13 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options14 featureOptions14 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options15 featureOptions15 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options16 featureOptions16 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options17 featureOptions17 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options18 featureOptions18 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options19 featureOptions19 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options20 featureOptions20 = default;
            Vortice.Direct3D12.FeatureDataD3D12Options21 featureOptions21 = default;

            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options, ref featureOptions0);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options1, ref featureOptions1);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options2, ref featureOptions2);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options3, ref featureOptions3);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options4, ref featureOptions4);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options5, ref featureOptions5);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options6, ref featureOptions6);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options7, ref featureOptions7);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options8, ref featureOptions8);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options9, ref featureOptions9);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options10, ref featureOptions10);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options11, ref featureOptions11);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options12, ref featureOptions12);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options13, ref featureOptions13);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options14, ref featureOptions14);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options15, ref featureOptions15);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options16, ref featureOptions16);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options17, ref featureOptions17);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options18, ref featureOptions18);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options19, ref featureOptions19);
            _ = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options20, ref featureOptions20);
            bool options21Supported = m_NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options21, ref featureOptions21);

            // check programmable msaa supported
            switch (featureOptions2.ProgrammableSamplePositionsTier)
            {
                case Vortice.Direct3D12.ProgrammableSamplePositionsTier.Tier1:
                    isProgrammableSamplePositionSupported = false;
                    break;

                case Vortice.Direct3D12.ProgrammableSamplePositionsTier.Tier2:
                    isProgrammableSamplePositionSupported = true;
                    break;

                case Vortice.Direct3D12.ProgrammableSamplePositionsTier.TierNOTSupported:
                    isProgrammableSamplePositionSupported = false;
                    break;
            }

            // check barycentric coord supported
            if(featureOptions3.BarycentricsSupported)
            {
                isBarycentricCoordSupported = true;
            }
            else
            {
                isBarycentricCoordSupported = false;
            }

            // check multi view instancing supported
            switch (featureOptions3.ViewInstancingTier)
            {
                case Vortice.Direct3D12.ViewInstancingTier.Tier1:
                    multiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
                    break;

                case Vortice.Direct3D12.ViewInstancingTier.Tier2:
                    multiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
                    break;

                case Vortice.Direct3D12.ViewInstancingTier.Tier3:
                    multiviewStrategy = ERHIMultiviewStrategy.ViewIndex;
                    break;

                case Vortice.Direct3D12.ViewInstancingTier.TierNotSupported:
                    multiviewStrategy = ERHIMultiviewStrategy.Unsupported;
                    break;
            }

            // check raytracing level
            switch (featureOptions5.RaytracingTier)
            {
                case Vortice.Direct3D12.RaytracingTier.Tier1_0:
                    isRaytracingSupported = true;
                    isRaytracingInlineSupported = false;
                    break;

                case Vortice.Direct3D12.RaytracingTier.Tier1_1:
                    isRaytracingSupported = true;
                    isRaytracingInlineSupported = true;
                    break;

                case Vortice.Direct3D12.RaytracingTier.NotSupported:
                    isRaytracingSupported = false;
                    isRaytracingInlineSupported = false;
                    break;
            }

            // check render pass level
            switch (featureOptions5.RenderPassesTier)
            {
                case Vortice.Direct3D12.RenderPassTier.Tier0:
                    isNativeRenderPassSupported = false;
                    break;

                case Vortice.Direct3D12.RenderPassTier.Tier1:
                    isNativeRenderPassSupported = true;
                    break;

                case Vortice.Direct3D12.RenderPassTier.Tier2:
                    isNativeRenderPassSupported = true;
                    break;
            }

            // check enhanced barrier support
            isEnhancedBarriersSupported = featureOptions12.EnhancedBarriersSupported;

            // check mesh shading level
            switch (featureOptions7.MeshShaderTier)
            {
                case Vortice.Direct3D12.MeshShaderTier.Tier1:
                    // The public flag must only turn true once the native mesh pipeline path is
                    // implemented and covered by conformance; the current create path throws.
                    isMeshShadingSupported = false;
                    break;

                case Vortice.Direct3D12.MeshShaderTier.NotSupported:
                    isMeshShadingSupported = false;
                    break;
            }

            isWorkgraphSupported =
                options21Supported &&
                featureOptions21.WorkGraphsTier != Vortice.Direct3D12.WorkGraphsTier.TierNOT_SUPPORTED;

            m_Limit = new Dx12DeviceLimit(uniformBufferAlignment,
                                          uploadBufferAlignment,
                                          uploadBufferTextureAlignment,
                                          uploadBufferTextureRowAlignment,
                                          maxMSAACount,
                                          maxBoundTexture,
                                          minWavefrontSize,
                                          maxWavefrontSize,
                                          maxComputeThreads,
                                          maxGroupShareMemorySize,
                                          maxVertexInputBindings,
                                          maxColorAttachments,
                                          maxTexture2DSize,
                                          maxTextureCubeSize,
                                          nativeMaxFeatureLevel);

            m_Feature = new Dx12DeviceFeature(isFlipProjection,
                                              isHDRPresentSupported,
                                              isUnifiedMemorySupported,
                                              isRootConstantSupport,
                                              isIndirectRootConstantSupport,
                                              isPixelShaderUAVSupported,
                                              isRasterizerOrderedSupported,
                                              isAnisotropyTextureSupported,
                                              isDepthbufferFetchSupported,
                                              isFramebufferFetchSupported,
                                              isTimestampQueriesSupported,
                                              isOcclusionQueriesSupported,
                                              isPipelineStatsQueriesSupported,
                                              isAtomicUInt64Supported,
                                              isWorkgraphSupported,
                                              isMeshShadingSupported,
                                              isDrawIndirectSupported,
                                              isDrawMultiIndirectSupported,
                                              isRaytracingSupported,
                                              isRaytracingInlineSupported,
                                              isVariableRateShadingSupported,
                                              isHiddenSurfaceRemovalSupported,
                                              isBarycentricCoordSupported,
                                              isProgrammableSamplePositionSupported,
                                              isMLSupported,
                                              matrixMajorons,
                                              depthValueRange,
                                              multiviewStrategy,
                                              waveOperationStrategy,
                                              isEnhancedBarriersSupported,
                                              isNativeRenderPassSupported);
        }

        private void CreateDirectMLObjects()
        {
            if (!ThirdPartyNativeLibraryResolver.TryResolve("DirectML.dll", out _, out _))
            {
                return;
            }

            Vortice.DirectML.IDMLDevice? directMLDevice = null;
            Vortice.DirectML.IDMLDevice1? directMLDevice1 = null;
            Vortice.DirectML.IDMLCommandRecorder? commandRecorder = null;

            try
            {
                directMLDevice = Vortice.DirectML.DML.DMLCreateDevice((Vortice.Direct3D12.ID3D12Device)m_NativeDevice, Vortice.DirectML.CreateDeviceFlags.None);
                directMLDevice1 = directMLDevice.QueryInterfaceOrNull<Vortice.DirectML.IDMLDevice1>();
                commandRecorder = directMLDevice.CreateCommandRecorder();
                m_DirectMLDevice = directMLDevice;
                m_DirectMLDevice1 = directMLDevice1;
                m_DirectMLCommandRecorder = commandRecorder;
            }
            catch
            {
                commandRecorder?.Release();
                directMLDevice1?.Release();
                directMLDevice?.Release();
                m_DirectMLDevice = null;
                m_DirectMLDevice1 = null;
                m_DirectMLCommandRecorder = null;
            }
        }

        private void CreateDescriptorHeaps()
        {
            // Non-shader-visible heaps for RTV/DSV (these cannot be shader-visible on DX12)
            m_DescriptorHeapDSV = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.DepthStencilView, Vortice.Direct3D12.DescriptorHeapFlags.None, 4096);
            m_DescriptorHeapHeapRTV = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.RenderTargetView, Vortice.Direct3D12.DescriptorHeapFlags.None, 4096);

            // Shader-visible heaps for GPU access - large enough for bindless resource arrays
            m_DescriptorHeapSampler = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.Sampler, Vortice.Direct3D12.DescriptorHeapFlags.ShaderVisible, 2048);
            m_DescriptorHeapCbvSrvUav = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, Vortice.Direct3D12.DescriptorHeapFlags.ShaderVisible, 1000000);

            // CPU-only staging heaps for building descriptors before copying to GPU-visible heaps
            m_StagingHeapCbvSrvUav = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, Vortice.Direct3D12.DescriptorHeapFlags.None, 65536);
            m_StagingHeapSampler = new Dx12DescriptorHeap(m_NativeDevice, Vortice.Direct3D12.DescriptorHeapType.Sampler, Vortice.Direct3D12.DescriptorHeapFlags.None, 2048);
        }

        private void CreateCommandSignatures()
        {
            Vortice.Direct3D12.CommandSignatureDescription commandSignatureDesc = new Vortice.Direct3D12.CommandSignatureDescription
            {
                NodeMask = 0,
            };

            #region Create_DrawIndirect_Argument
            Vortice.Direct3D12.IndirectArgumentDescription indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
            {
                Type = Vortice.Direct3D12.IndirectArgumentType.Draw,
            };
            commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
            commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DrawArguments);
            SharpGen.Runtime.Result hResult = m_NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out Vortice.Direct3D12.ID3D12CommandSignature commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DrawIndirectSignature = commandSignature;
            #endregion

            #region Create_DrawIndexedIndirect_Argument
            indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
            {
                Type = Vortice.Direct3D12.IndirectArgumentType.DrawIndexed,
            };
            commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
            commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DrawIndexedArguments);
            hResult = m_NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DrawIndexedIndirectSignature = commandSignature;
            #endregion

            #region Create_DispatchComputeIndirect_Argument
            indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
            {
                Type = Vortice.Direct3D12.IndirectArgumentType.Dispatch,
            };
            commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
            commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchArguments);
            hResult = m_NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DispatchComputeIndirectSignature = commandSignature;
            #endregion

            #region Create_DispatchMeshIndirect_Argument
            if (m_Feature.IsMeshShadingSupported)
            {
                indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
                {
                    Type = Vortice.Direct3D12.IndirectArgumentType.DispatchMesh,
                };
                commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
                commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchMeshArguments);
                hResult = m_NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_DispatchMeshIndirectSignature = commandSignature;
            }
            #endregion

            #region Create_DispatchRayIndirect_Argument
            if (m_Feature.IsRaytracingSupported)
            {
                indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
                {
                    Type = Vortice.Direct3D12.IndirectArgumentType.DispatchRays,
                };
                commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
                commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchRaysDescription);
                hResult = m_NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_DispatchRayIndirectSignature = commandSignature;
            }
            #endregion
        }

        private void CreateCommandQueues(in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_ComputeQueueCount = computeQueueCount;
            m_TransferQueueCount = transferQueueCount;
            m_GraphicsQueueCount = graphicsQueueCount;
            m_CommandQueueMap = new Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>(3);

            if (computeQueueCount > 0)
            {
                TArray<RHICommandQueue> computeQueueArray = new TArray<RHICommandQueue>(computeQueueCount);
                for (int j = 0; j < computeQueueCount; ++j)
                {
                    computeQueueArray.Add(new Dx12CommandQueue(this, ERHIPipelineType.Compute));
            }
                m_CommandQueueMap.Add(ERHIPipelineType.Compute, computeQueueArray);
            }

            if (transferQueueCount > 0)
            {
                TArray<RHICommandQueue> transferQueueArray = new TArray<RHICommandQueue>(transferQueueCount);
                for (int k = 0; k < transferQueueCount; ++k)
                {
                    transferQueueArray.Add(new Dx12CommandQueue(this, ERHIPipelineType.Transfer));
                }
                m_CommandQueueMap.Add(ERHIPipelineType.Transfer, transferQueueArray);
            }

            if (graphicsQueueCount > 0)
            {
                TArray<RHICommandQueue> graphicsQueueArray = new TArray<RHICommandQueue>(graphicsQueueCount);
                for (int i = 0; i < graphicsQueueCount; ++i)
                {
                    graphicsQueueArray.Add(new Dx12CommandQueue(this, ERHIPipelineType.Graphics));
            }
                m_CommandQueueMap.Add(ERHIPipelineType.Graphics, graphicsQueueArray);
            }
        }

        protected override void Release()
        {
            ReleaseComObject(ref m_DirectMLCommandRecorder);
            ReleaseComObject(ref m_DirectMLDevice1);
            ReleaseComObject(ref m_DirectMLDevice);

            DisposeResource(ref m_DescriptorHeapDSV);
            DisposeResource(ref m_DescriptorHeapHeapRTV);
            DisposeResource(ref m_DescriptorHeapSampler);
            DisposeResource(ref m_DescriptorHeapCbvSrvUav);
            DisposeResource(ref m_StagingHeapCbvSrvUav);
            DisposeResource(ref m_StagingHeapSampler);

            ReleaseComObject(ref m_DrawIndirectSignature);
            ReleaseComObject(ref m_DrawIndexedIndirectSignature);
            if (m_Feature?.IsMeshShadingSupported == true)
            {
                ReleaseComObject(ref m_DispatchMeshIndirectSignature);
            }
            if (m_Feature?.IsRaytracingSupported == true)
            {
                ReleaseComObject(ref m_DispatchRayIndirectSignature);
            }
            ReleaseComObject(ref m_DispatchComputeIndirectSignature);

            ReleaseComObject(ref m_NativeDevice);
            if (m_OwnsDXGIAdapter)
            {
                m_OwnsDXGIAdapter = false;
                ReleaseComObject(ref m_DXGIAdapter);
            }
        }

        private static void DisposeResource<T>(ref T? resource) where T : IDisposable
        {
            T? local = resource;
            resource = default;
            local?.Dispose();
        }

        private static void ReleaseComObject<T>(ref T? comObject) where T : SharpGen.Runtime.ComObject
        {
            T? local = comObject;
            comObject = default;
            if (local == null)
            {
                return;
            }

            try
            {
                if (local.NativePointer != IntPtr.Zero)
                {
                    local.Release();
                }
            }
            catch (NullReferenceException)
            {
                // SharpGen wrappers can be finalized after their native pointer has already been cleared.
            }
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
