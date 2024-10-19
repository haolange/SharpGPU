using System;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;
using IUnknown = TerraFX.Interop.Windows.IUnknown;
using Infinity.Collections;
using System.Collections;
using System.Collections.Generic;
using Silk.NET.Vulkan;

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
        public IDXGIAdapter1* DXGIAdapter
        {
            get
            {
                return m_DXGIAdapter;
            }
        }
        public ID3D12Device10* NativeDevice
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
        public ID3D12CommandSignature* DispatchMeshIndirectSignature
        {
            get
            {
                return m_DispatchMeshIndirectSignature;
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
        private IDXGIAdapter1* m_DXGIAdapter;
        private ID3D12Device10* m_NativeDevice;
        private Dx12DescriptorHeap m_DescriptorHeapDSV;
        private Dx12DescriptorHeap m_DescriptorHeapHeapRTV;
        private Dx12DescriptorHeap m_DescriptorHeapSampler;
        private Dx12DescriptorHeap m_DescriptorHeapCbvSrvUav;
        private ID3D12CommandSignature* m_DrawIndirectSignature;
        private ID3D12CommandSignature* m_DrawIndexedIndirectSignature;
        private ID3D12CommandSignature* m_DispatchRayIndirectSignature;
        private ID3D12CommandSignature* m_DispatchMeshIndirectSignature;
        private ID3D12CommandSignature* m_DispatchComputeIndirectSignature;

        public Dx12Device(Dx12Instance instance, in IDXGIAdapter1* adapter, in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_DXGIAdapter = adapter;
            m_Dx12Instance = instance;

            DXGI_ADAPTER_DESC1 adapterDesc;
            m_DXGIAdapter->GetDesc1(&adapterDesc);

            m_Name = SharpGen.Runtime.StringHelpers.PtrToStringUni(new IntPtr(&adapterDesc.Description.e0), 128);
            m_Type = (adapterDesc.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) == 1 ? ERHIDeviceType.Software : ERHIDeviceType.Hardware;
            m_VendorId.IntValue = adapterDesc.VendorId;
            m_DeviceId.IntValue = adapterDesc.DeviceId;

            CreateDevice();
            CheckFeatureSupport();
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
            CreateDescriptorHeaps();
            CreateCommandSignatures();
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

        public override RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            return new Dx12TopLevelAccelStruct(this, descriptor);
        }

        public override RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
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

        public override RHIResourceTableLayout CreateResourceTableLayout(in RHIResourceTableLayoutDescriptor descriptor)
        {
            return new Dx12ResourceTableLayout(descriptor);
        }

        public override RHIResourceTable CreateResourceTable(in RHIResourceTableDescriptor descriptor)
        {
            return new Dx12ResourceTable(descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new Dx12PipelineLayout(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            return new Dx12RasterPipeline(this, descriptor);
        }

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            return new Dx12ComputePipeline(this, descriptor);
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            return new Dx12RaytracingPipeline(this, descriptor);
        }

        public Dx12DescriptorInfo AllocateDsvDescriptor(in int count)
        {
            int index = m_DescriptorHeapDSV.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapDSV.NativeCpuStartHandle.Offset(index, m_DescriptorHeapDSV.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapDSV.NativeGpuStartHandle.Offset(index, m_DescriptorHeapDSV.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapDSV.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateRtvDescriptor(in int count)
        {
            int index = m_DescriptorHeapHeapRTV.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapHeapRTV.NativeCpuStartHandle.Offset(index, m_DescriptorHeapHeapRTV.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapHeapRTV.NativeGpuStartHandle.Offset(index, m_DescriptorHeapHeapRTV.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapHeapRTV.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateSamplerDescriptor(in int count)
        {
            int index = m_DescriptorHeapSampler.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapSampler.NativeCpuStartHandle.Offset(index, m_DescriptorHeapSampler.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapSampler.NativeGpuStartHandle.Offset(index, m_DescriptorHeapSampler.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapSampler.NativeDescriptorHeap;
            return descriptorInfo;
        }

        public Dx12DescriptorInfo AllocateCbvSrvUavDescriptor(in int count)
        {
            int index = m_DescriptorHeapCbvSrvUav.Allocate();
            Dx12DescriptorInfo descriptorInfo;
            descriptorInfo.Index = index;
            descriptorInfo.CpuHandle = m_DescriptorHeapCbvSrvUav.NativeCpuStartHandle.Offset(index, m_DescriptorHeapCbvSrvUav.DescriptorSize);
            descriptorInfo.GpuHandle = m_DescriptorHeapCbvSrvUav.NativeGpuStartHandle.Offset(index, m_DescriptorHeapCbvSrvUav.DescriptorSize);
            descriptorInfo.DescriptorHeap = m_DescriptorHeapCbvSrvUav.NativeDescriptorHeap;
            return descriptorInfo;
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

        private void CreateDevice()
        {
            ID3D12Device10* device;
            HRESULT hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_2, __uuidof<ID3D12Device10>(), (void**)&device);
            if (FAILED(hResult))
            {
                hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_1, __uuidof<ID3D12Device10>(), (void**)&device);

                if (FAILED(hResult))
                {
                    hResult = DirectX.D3D12CreateDevice((IUnknown*)m_DXGIAdapter, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_12_0, __uuidof<ID3D12Device10>(), (void**)&device);
                }
            }
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeDevice = device;
        }

        private void CheckFeatureSupport()
        {
            m_Feature = new Dx12DeviceFeature();

            m_Feature.IsFlipProjection = false;
            m_Feature.MatrixMajorons = ERHIMatrixMajorons.RowMajor;
            m_Feature.DepthValueRange = ERHIDepthValueRange.ZeroToOne;

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
            ((Dx12DeviceFeature)m_Feature).MaxNativeFeatureLevel = dLevels.MaxSupportedFeatureLevel;

            // check feature options
            D3D12_FEATURE_DATA_D3D12_OPTIONS featureOptions0;
            D3D12_FEATURE_DATA_D3D12_OPTIONS1 featureOptions1;
            D3D12_FEATURE_DATA_D3D12_OPTIONS2 featureOptions2;
            D3D12_FEATURE_DATA_D3D12_OPTIONS3 featureOptions3;
            D3D12_FEATURE_DATA_D3D12_OPTIONS4 featureOptions4;
            D3D12_FEATURE_DATA_D3D12_OPTIONS5 featureOptions5;
            D3D12_FEATURE_DATA_D3D12_OPTIONS6 featureOptions6;
            D3D12_FEATURE_DATA_D3D12_OPTIONS7 featureOptions7;
            D3D12_FEATURE_DATA_D3D12_OPTIONS8 featureOptions8;
            D3D12_FEATURE_DATA_D3D12_OPTIONS9 featureOptions9;
            D3D12_FEATURE_DATA_D3D12_OPTIONS10 featureOptions10;
            D3D12_FEATURE_DATA_D3D12_OPTIONS11 featureOptions11;
            D3D12_FEATURE_DATA_D3D12_OPTIONS12 featureOptions12;
            D3D12_FEATURE_DATA_D3D12_OPTIONS13 featureOptions13;
            D3D12_FEATURE_DATA_D3D12_OPTIONS14 featureOptions14;
            D3D12_FEATURE_DATA_D3D12_OPTIONS15 featureOptions15;
            D3D12_FEATURE_DATA_D3D12_OPTIONS16 featureOptions16;
            D3D12_FEATURE_DATA_D3D12_OPTIONS17 featureOptions17;
            D3D12_FEATURE_DATA_D3D12_OPTIONS18 featureOptions18;
            D3D12_FEATURE_DATA_D3D12_OPTIONS19 featureOptions19;
            D3D12_FEATURE_DATA_D3D12_OPTIONS20 featureOptions20;

            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS, &featureOptions0, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS1, &featureOptions1, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS1));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS2, &featureOptions2, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS2));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS3, &featureOptions3, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS3));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS4, &featureOptions4, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS4));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS5, &featureOptions5, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS5));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS6, &featureOptions6, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS6));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS7, &featureOptions7, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS7));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS8, &featureOptions8, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS8));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS9, &featureOptions9, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS9));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS10, &featureOptions10, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS10));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS11, &featureOptions11, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS11));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS12, &featureOptions12, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS12));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS13, &featureOptions13, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS13));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS14, &featureOptions14, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS14));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS15, &featureOptions15, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS15));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS16, &featureOptions16, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS16));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS17, &featureOptions17, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS17));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS18, &featureOptions18, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS18));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS19, &featureOptions19, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS19));
            m_NativeDevice->CheckFeatureSupport(D3D12_FEATURE.D3D12_FEATURE_D3D12_OPTIONS20, &featureOptions20, (uint)sizeof(D3D12_FEATURE_DATA_D3D12_OPTIONS20));

            // check programmable msaa supported
            switch (featureOptions2.ProgrammableSamplePositionsTier)
            {
                case D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER.D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER_1:
                    m_Feature.IsProgrammableSamplePositionSupported = false;
                    break;

                case D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER.D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER_2:
                    m_Feature.IsProgrammableSamplePositionSupported = true;
                    break;

                case D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER.D3D12_PROGRAMMABLE_SAMPLE_POSITIONS_TIER_NOT_SUPPORTED:
                    m_Feature.IsProgrammableSamplePositionSupported = false;
                    break;
            }

            // check barycentric coord supported
            if(featureOptions3.BarycentricsSupported)
            {
                m_Feature.IsShaderBarycentricCoordSupported = true;
            }
            else
            {
                m_Feature.IsShaderBarycentricCoordSupported = false;
            }

            // check multi view instancing supported
            switch (featureOptions3.ViewInstancingTier)
            {
                case D3D12_VIEW_INSTANCING_TIER.D3D12_VIEW_INSTANCING_TIER_1:
                    m_Feature.MultiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
                    break;

                case D3D12_VIEW_INSTANCING_TIER.D3D12_VIEW_INSTANCING_TIER_2:
                    m_Feature.MultiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
                    break;

                case D3D12_VIEW_INSTANCING_TIER.D3D12_VIEW_INSTANCING_TIER_3:
                    m_Feature.MultiviewStrategy = ERHIMultiviewStrategy.ViewIndex;
                    break;

                case D3D12_VIEW_INSTANCING_TIER.D3D12_VIEW_INSTANCING_TIER_NOT_SUPPORTED:
                    m_Feature.MultiviewStrategy = ERHIMultiviewStrategy.Unsupported;
                    break;
            }

            // check raytracing level
            switch (featureOptions5.RaytracingTier)
            {
                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_1_0:
                    m_Feature.IsRaytracingSupported = true;
                    m_Feature.IsRaytracingInlineSupported = false;
                    break;

                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_1_1:
                    m_Feature.IsRaytracingSupported = true;
                    m_Feature.IsRaytracingInlineSupported = true;
                    break;

                case D3D12_RAYTRACING_TIER.D3D12_RAYTRACING_TIER_NOT_SUPPORTED:
                    m_Feature.IsRaytracingSupported = false;
                    m_Feature.IsRaytracingInlineSupported = false;
                    break;
            }

            // check render pass level
            switch (featureOptions5.RenderPassesTier)
            {
                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_0:
                    ((Dx12DeviceFeature)m_Feature).IsRenderPassSupported = false;
                    break;

                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_1:
                    ((Dx12DeviceFeature)m_Feature).IsRenderPassSupported = true;
                    break;

                case D3D12_RENDER_PASS_TIER.D3D12_RENDER_PASS_TIER_2:
                    ((Dx12DeviceFeature)m_Feature).IsRenderPassSupported = true;
                    break;
            }

            // check mesh shading level
            switch (featureOptions7.MeshShaderTier)
            {
                case D3D12_MESH_SHADER_TIER.D3D12_MESH_SHADER_TIER_1:
                    m_Feature.IsMeshShadingSupported = true;
                    break;

                case D3D12_MESH_SHADER_TIER.D3D12_MESH_SHADER_TIER_NOT_SUPPORTED:
                    m_Feature.IsMeshShadingSupported = false;
                    break;
            }
        }

        private void CreateDescriptorHeaps()
        {
            m_DescriptorHeapDSV = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_DSV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 1024);
            m_DescriptorHeapHeapRTV = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 1024);
            m_DescriptorHeapSampler = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 2048);
            m_DescriptorHeapCbvSrvUav = new Dx12DescriptorHeap(m_NativeDevice, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 32768);
        }

        private void CreateCommandSignatures()
        {
            ID3D12CommandSignature* commandSignature;
            D3D12_INDIRECT_ARGUMENT_DESC indirectArgDesc;
            D3D12_COMMAND_SIGNATURE_DESC commandSignatureDesc;

            #region Create_DrawIndirect_Argument
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
            #endregion

            #region Create_DrawIndexedIndirect_Argument
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
            #endregion

            #region Create_DispatchComputeIndirect_Argument
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
            #endregion

            #region Create_DispatchMeshIndirect_Argument
            if (m_Feature.IsMeshShadingSupported)
            {
                indirectArgDesc.Type = D3D12_INDIRECT_ARGUMENT_TYPE.D3D12_INDIRECT_ARGUMENT_TYPE_DISPATCH_MESH;
                //commandSignatureDesc.NodeMask = nodeMask;
                commandSignatureDesc.pArgumentDescs = &indirectArgDesc;
                commandSignatureDesc.ByteStride = (uint)sizeof(D3D12_DISPATCH_MESH_ARGUMENTS);
                commandSignatureDesc.NumArgumentDescs = 1;
                hResult = m_NativeDevice->CreateCommandSignature(&commandSignatureDesc, null, __uuidof<ID3D12CommandSignature>(), (void**)&commandSignature);
#if DEBUG
                Dx12Utility.CHECK_HR(hResult);
#endif
                m_DispatchMeshIndirectSignature = commandSignature;
            }
            #endregion

            #region Create_DispatchRayIndirect_Argument
            if (m_Feature.IsRaytracingSupported)
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
            m_DescriptorHeapDSV.Dispose();
            m_DescriptorHeapHeapRTV.Dispose();
            m_DescriptorHeapSampler.Dispose();
            m_DescriptorHeapCbvSrvUav.Dispose();

            m_DrawIndirectSignature->Release();
            m_DrawIndexedIndirectSignature->Release();
            if (m_Feature.IsMeshShadingSupported)
            {
                DispatchMeshIndirectSignature->Release();
            }
            if (m_Feature.IsRaytracingSupported)
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
