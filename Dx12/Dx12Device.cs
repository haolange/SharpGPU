using System;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;
using IUnknown = TerraFX.Interop.Windows.IUnknown;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12DeviceLimit : RHIDeviceLimit
    {

    }

    internal unsafe class Dx12DeviceFeature : RHIDeviceFeature
    {
        public bool IsRenderPassSupported;
        public D3D_FEATURE_LEVEL MaxNativeFeatureLevel;
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
        public Dx12DescriptorHeap DsvHeap
        {
            get
            {
                return m_DsvHeap;
            }
        }
        public Dx12DescriptorHeap RtvHeap
        {
            get
            {
                return m_RtvHeap;
            }
        }
        public Dx12DescriptorHeap SamplerHeap
        {
            get
            {
                return m_SamplerHeap;
            }
        }
        public Dx12DescriptorHeap CbvSrvUavHeap
        {
            get
            {
                return m_CbvSrvUavHeap;
            }
        }
        public IDXGIAdapter1* DXGIAdapter
        {
            get
            {
                return m_DXGIAdapter;
            }
        }
        public ID3D12Device8* NativeDevice
        {
            get
            {
                return m_NativeDevice;
            }
        }
        public ID3D12CommandSignature* DrawIndirectSignature
        {
            get
            {
                return m_DrawIndirectSignature;
            }
        }
        public ID3D12CommandSignature* DrawIndexedIndirectSignature
        {
            get
            {
                return m_DrawIndexedIndirectSignature;
            }
        }
        public ID3D12CommandSignature* DispatchRayIndirectSignature
        {
            get
            {
                return m_DispatchRayIndirectSignature;
            }
        }
        public ID3D12CommandSignature* DispatchComputeIndirectSignature
        {
            get
            {
                return m_DispatchComputeIndirectSignature;
            }
        }

        private Dx12Instance m_Dx12Instance;
        private Dx12DescriptorHeap m_DsvHeap;
        private Dx12DescriptorHeap m_RtvHeap;
        private Dx12DescriptorHeap m_SamplerHeap;
        private Dx12DescriptorHeap m_CbvSrvUavHeap;
        private IDXGIAdapter1* m_DXGIAdapter;
        private ID3D12Device8* m_NativeDevice;
        private ID3D12CommandSignature* m_DrawIndirectSignature;
        private ID3D12CommandSignature* m_DrawIndexedIndirectSignature;
        private ID3D12CommandSignature* m_DispatchRayIndirectSignature;
        private ID3D12CommandSignature* m_DispatchComputeIndirectSignature;

        public Dx12Device(Dx12Instance instance, in IDXGIAdapter1* adapter) 
        {
            m_DXGIAdapter = adapter;
            m_Dx12Instance = instance;

            DXGI_ADAPTER_DESC1 adapterDesc;
            m_DXGIAdapter->GetDesc1(&adapterDesc);

            m_DeviceInfo.Name = SharpGen.Runtime.StringHelpers.PtrToStringUni(new IntPtr(&adapterDesc.Description.e0), 128);
            m_DeviceInfo.Type = (adapterDesc.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) == 1 ? ERHIDeviceType.Software : ERHIDeviceType.Hardware;
            m_DeviceInfo.VendorId.IntValue = adapterDesc.VendorId;
            m_DeviceInfo.DeviceId.IntValue = adapterDesc.DeviceId;

            CreateDevice();
            CheckFeatureSupport();
            CreateDescriptorHeaps();
            CreateCommandSignatures();
        }

        public override RHIFence CreateFence()
        {
            return new Dx12Fence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            return new Dx12Semaphore(this);
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            return new Dx12Query(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            throw new NotImplementedException();
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

        public override RHIStorageQueue CreateStorageQueue()
        {
            throw new NotImplementedException();
        }

        public override RHICommandQueue CreateCommandQueue(in ERHIPipelineType pipeline)
        {
            return new Dx12CommandQueue(this, pipeline);
        }

        public override RHITopLevelAccelStruct CreateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            return new Dx12TopLevelAccelStruct(this, descriptor);
        }

        public override RHIBottomLevelAccelStruct CreateAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            return new Dx12BottomLevelAccelStruct(this, descriptor);
        }

        public override RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor)
        {
            return new Dx12Function(descriptor);
        }

        public override RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            return new Dx12SwapChain(this, descriptor);
        }

        public override RHIBindTableLayout CreateBindTableLayout(in RHIBindTableLayoutDescriptor descriptor)
        {
            return new Dx12BindTableLayout(descriptor);
        }

        public override RHIBindTable CreateBindTable(in RHIBindTableDescriptor descriptor)
        {
            return new Dx12BindTable(descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new Dx12PipelineLayout(this, descriptor);
        }

        public override RHIComputePipelineState CreateComputePipelineState(in RHIComputePipelineStateDescriptor descriptor)
        {
            return new Dx12ComputePipelineState(this, descriptor);
        }

        public override RHIRaytracingPipelineState CreateRaytracingPipelineState(in RHIRaytracingPipelineStateDescriptor descriptor)
        {
            return new Dx12RaytracingPipelineState(this, descriptor);
        }

        public override RHIRasterPipelineState CreateRasterPipelineState(in RHIRasterPipelineStateDescriptor descriptor)
        {
            return new Dx12RasterPipelineState(this, descriptor);
        }

        public Dx12DescriptorInfo AllocateDsvDescriptor(in int count)
        {
            int index = m_DsvHeap.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DsvHeap.CpuStartHandle.Offset(index, m_DsvHeap.DescriptorSize);
            descriptorInfo.GpuHandle = m_DsvHeap.GpuStartHandle.Offset(index, m_DsvHeap.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DsvHeap.DescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateRtvDescriptor(in int count)
        {
            int index = m_RtvHeap.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_RtvHeap.CpuStartHandle.Offset(index, m_RtvHeap.DescriptorSize);
            descriptorInfo.GpuHandle = m_RtvHeap.GpuStartHandle.Offset(index, m_RtvHeap.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_RtvHeap.DescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateSamplerDescriptor(in int count)
        {
            int index = m_SamplerHeap.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_SamplerHeap.CpuStartHandle.Offset(index, m_SamplerHeap.DescriptorSize);
            descriptorInfo.GpuHandle = m_SamplerHeap.GpuStartHandle.Offset(index, m_SamplerHeap.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_SamplerHeap.DescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateCbvSrvUavDescriptor(in int count)
        {
            int index = m_CbvSrvUavHeap.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_CbvSrvUavHeap.CpuStartHandle.Offset(index, m_CbvSrvUavHeap.DescriptorSize);
            descriptorInfo.GpuHandle = m_CbvSrvUavHeap.GpuStartHandle.Offset(index, m_CbvSrvUavHeap.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_CbvSrvUavHeap.DescriptorHeap;
            return descriptorInfo;
        }

        public void FreeDsvDescriptor(in int index)
        {
            m_DsvHeap.Free(index);
        }

        public void FreeRtvDescriptor(in int index)
        {
            m_RtvHeap.Free(index);
        }

        public void FreeSamplerDescriptor(in int index)
        {
            m_SamplerHeap.Free(index);
        }

        public void FreeCbvSrvUavDescriptor(in int index)
        {
            m_CbvSrvUavHeap.Free(index);
        }

        private void CreateDevice()
        {
            ID3D12Device8* device;
            HRESULT hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_2, __uuidof<ID3D12Device8>(), (void**)&device);
            if (FAILED(hResult))
            {
                hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1, __uuidof<ID3D12Device8>(), (void**)&device);

                if (FAILED(hResult))
                {
                    hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0, __uuidof<ID3D12Device8>(), (void**)&device);
                }
            }
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeDevice = device;
        }

        private void CheckFeatureSupport()
        {
            m_DeviceInfo.Feature = new Dx12DeviceFeature();

            m_DeviceInfo.Feature.IsFlipProjection = false;
            m_DeviceInfo.Feature.MatrixMajorons = ERHIMatrixMajorons.RowMajor;
            m_DeviceInfo.Feature.DepthValueRange = ERHIDepthValueRange.ZeroToOne;
            m_DeviceInfo.Feature.MultiviewStrategy = ERHIMultiviewStrategy.Unsupported;

            // check feature level
            D3D_FEATURE_LEVEL* aLevels = stackalloc D3D_FEATURE_LEVEL[3];
            {
                aLevels[0] = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0;
                aLevels[1] = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1;
                aLevels[2] = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_2;
            }
            D3D12_FEATURE_DATA_FEATURE_LEVELS dLevels;
            dLevels.NumFeatureLevels = 3;
            dLevels.pFeatureLevelsRequested = aLevels;
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_FEATURE_LEVELS, &dLevels, (uint)sizeof(D3D12_FEATURE_DATA_FEATURE_LEVELS));
            ((Dx12DeviceFeature)m_DeviceInfo.Feature).MaxNativeFeatureLevel = dLevels.MaxSupportedFeatureLevel;

            // check feature options
            D3D12_FEATURE_DATA_D3D12_OPTIONS5 options5;
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS5, &options5, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS5));

            // check raytracing level
            switch (options5.RaytracingTier)
            {
                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_1_0:
                    m_DeviceInfo.Feature.IsRaytracingSupported = true;
                    m_DeviceInfo.Feature.IsRaytracingInlineSupported = false;
                    break;

                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_1_1:
                    m_DeviceInfo.Feature.IsRaytracingSupported = true;
                    m_DeviceInfo.Feature.IsRaytracingInlineSupported = true;
                    break;

                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_NOT_SUPPORTED:
                    m_DeviceInfo.Feature.IsRaytracingSupported = false;
                    m_DeviceInfo.Feature.IsRaytracingInlineSupported = false;
                    break;
            }

            // check render pass level
            switch (options5.RenderPassesTier)
            {
                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_0:
                    ((Dx12DeviceFeature)m_DeviceInfo.Feature).IsRenderPassSupported = false;
                    break;

                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_1:
                    ((Dx12DeviceFeature)m_DeviceInfo.Feature).IsRenderPassSupported = true;
                    break;

                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_2:
                    ((Dx12DeviceFeature)m_DeviceInfo.Feature).IsRenderPassSupported = true;
                    break;
            }
        }

        private void CreateDescriptorHeaps()
        {
            m_DsvHeap = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_DSV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 1024);
            m_RtvHeap = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 1024);
            m_SamplerHeap = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 2048);
            m_CbvSrvUavHeap = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 32768);
        }

        private void CreateCommandSignatures()
        {
            ID3D12CommandSignature* commandSignature;
            D3D12_INDIRECT_ARGUMENT_DESC indirectArgDesc;
            D3D12_COMMAND_SIGNATURE_DESC commandSignatureDesc;

            indirectArgDesc.Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DRAW;
            //commandSignatureDesc.NodeMask = nodeMask;
            commandSignatureDesc.pArgumentDescs = &indirectArgDesc;
            commandSignatureDesc.ByteStride = (uint)sizeof(D3D12_DRAW_ARGUMENTS);
            commandSignatureDesc.NumArgumentDescs = 1;
            HRESULT hResult = m_NativeDevice->CreateCommandSignature(&commandSignatureDesc, null, __uuidof<ID3D12CommandSignature>(), (void**)&commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DrawIndirectSignature = commandSignature;

            indirectArgDesc.Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DRAW_INDEXED;
            //commandSignatureDesc.NodeMask = nodeMask;
            commandSignatureDesc.pArgumentDescs = &indirectArgDesc;
            commandSignatureDesc.ByteStride = (uint)sizeof(D3D12_DRAW_INDEXED_ARGUMENTS);
            commandSignatureDesc.NumArgumentDescs = 1;
            hResult = m_NativeDevice->CreateCommandSignature(&commandSignatureDesc, null, __uuidof<ID3D12CommandSignature>(), (void**)&commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DrawIndexedIndirectSignature = commandSignature;

            indirectArgDesc.Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH;
            //commandSignatureDesc.NodeMask = nodeMask;
            commandSignatureDesc.pArgumentDescs = &indirectArgDesc;
            commandSignatureDesc.ByteStride = (uint)sizeof(D3D12_DISPATCH_ARGUMENTS);
            commandSignatureDesc.NumArgumentDescs = 1;
            hResult = m_NativeDevice->CreateCommandSignature(&commandSignatureDesc, null, __uuidof<ID3D12CommandSignature>(), (void**)&commandSignature);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DispatchComputeIndirectSignature = commandSignature;

            if (m_DeviceInfo.Feature.IsRaytracingSupported)
            {
                indirectArgDesc.Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH_RAYS;
                //commandSignatureDesc.NodeMask = nodeMask;
                commandSignatureDesc.pArgumentDescs = &indirectArgDesc;
                commandSignatureDesc.ByteStride = (uint)sizeof(D3D12_DISPATCH_RAYS_DESC);
                commandSignatureDesc.NumArgumentDescs = 1;
                hResult = m_NativeDevice->CreateCommandSignature(&commandSignatureDesc, null, __uuidof<ID3D12CommandSignature>(), (void**)&commandSignature);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_DispatchRayIndirectSignature = commandSignature;
            }
        }

        protected override void Release()
        {
            m_DsvHeap.Dispose();
            m_RtvHeap.Dispose();
            m_SamplerHeap.Dispose();
            m_CbvSrvUavHeap.Dispose();
            m_DrawIndirectSignature->Release();
            m_DrawIndexedIndirectSignature->Release();
            if (m_DeviceInfo.Feature.IsRaytracingSupported)
            {
                m_DispatchRayIndirectSignature->Release();
            }
            m_DispatchComputeIndirectSignature->Release();
            m_NativeDevice->Release();
            m_DXGIAdapter->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
