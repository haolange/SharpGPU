using SharpGPU.Collections;
using System.Collections.Generic;
using System.Text;
using System;
using Vortice.Direct3D12;

namespace SharpGPU
{
#pragma warning disable CA1416
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

    internal unsafe class Dx12Device : RHIDevice
    {
        private const int SamplerDescriptorCapacity = 2048;
        private const int CbvSrvUavDescriptorCapacity = 1000000;
        private const int StagingDescriptorPageCapacity = 65536;

        public override ERHIBackend BackendType => ERHIBackend.DirectX12;
        public Dx12Instance Dx12Instance
        {
            get
            {
                return m_Dx12Instance;
            }
        }
        public Vortice.DXGI.IDXGIAdapter1 DXGIAdapter =>
            m_DXGIAdapter ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D12.ID3D12Device10 NativeDevice =>
            m_NativeDevice ?? throw new ObjectDisposedException(GetType().FullName);
        internal Dx12NullDescriptorCache NullDescriptors =>
            m_NullDescriptors ?? throw new ObjectDisposedException(GetType().FullName);

        public Dx12DescriptorHeap DescriptorHeapDSV =>
            m_DescriptorHeapDSV ?? throw new ObjectDisposedException(GetType().FullName);
        public Dx12DescriptorHeap DescriptorHeapHeapRTV =>
            m_DescriptorHeapHeapRTV ?? throw new ObjectDisposedException(GetType().FullName);
        public Dx12DescriptorHeap DescriptorHeapSampler =>
            m_DescriptorHeapSampler ?? throw new ObjectDisposedException(GetType().FullName);
        public Dx12DescriptorHeap DescriptorHeapCbvSrvUav =>
            m_DescriptorHeapCbvSrvUav ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D12.ID3D12CommandSignature DrawIndirectSignature =>
            m_DrawIndirectSignature ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D12.ID3D12CommandSignature DrawIndexedIndirectSignature =>
            m_DrawIndexedIndirectSignature ?? throw new ObjectDisposedException(GetType().FullName);
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchRayIndirectSignature =>
            m_DispatchRayIndirectSignature ?? throw new InvalidOperationException("DX12 ray-tracing indirect command signature is unavailable on this device.");
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchMeshIndirectSignature =>
            m_DispatchMeshIndirectSignature ?? throw new InvalidOperationException("DX12 mesh-shader indirect command signature is unavailable on this device.");
        public Vortice.Direct3D12.ID3D12CommandSignature DispatchComputeIndirectSignature =>
            m_DispatchComputeIndirectSignature ?? throw new ObjectDisposedException(GetType().FullName);
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
        internal RHICapability EnhancedBarriers => Capabilities.Synchronization.EnhancedBarriers;
        internal RHICapability NativeRenderPass => Capabilities.Raster.NativeRenderPass;

        private Dx12Instance m_Dx12Instance;
        private Vortice.DXGI.IDXGIAdapter1? m_DXGIAdapter;
        private Vortice.Direct3D12.ID3D12Device10? m_NativeDevice;
        private Dx12NullDescriptorCache? m_NullDescriptors;
        private Dx12DescriptorHeap? m_DescriptorHeapDSV;
        private Dx12DescriptorHeap? m_DescriptorHeapHeapRTV;
        private Dx12DescriptorHeap? m_DescriptorHeapSampler;
        private Dx12DescriptorHeap? m_DescriptorHeapCbvSrvUav;
        internal Dx12CpuDescriptorPool StagingPoolCbvSrvUav =>
            m_StagingPoolCbvSrvUav ?? throw new ObjectDisposedException(GetType().FullName);
        internal Dx12CpuDescriptorPool StagingPoolSampler =>
            m_StagingPoolSampler ?? throw new ObjectDisposedException(GetType().FullName);

        private Dx12CpuDescriptorPool? m_StagingPoolCbvSrvUav;
        private Dx12CpuDescriptorPool? m_StagingPoolSampler;
        private Vortice.Direct3D12.ID3D12CommandSignature? m_DrawIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature? m_DrawIndexedIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature? m_DispatchRayIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature? m_DispatchMeshIndirectSignature;
        private Vortice.Direct3D12.ID3D12CommandSignature? m_DispatchComputeIndirectSignature;
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
            m_DriverVersion = QueryDriverVersion(m_DXGIAdapter);

            CreateDevice();
            CreateDirectMLObjects();
            CheckFeatureSupport();
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
            CreateDescriptorHeaps();
            CreateCommandSignatures();
            m_OwnsDXGIAdapter = true;
        }

        private static string QueryDriverVersion(Vortice.DXGI.IDXGIAdapter adapter)
        {
            // DXGI exposes the UMD package version through IDXGIDevice. Direct3D 11+
            // interface GUIDs are explicitly unsupported by CheckInterfaceSupport; since
            // WDDM 2.3 every D3D component in a driver package shares this version.
            if (!adapter.CheckInterfaceSupport<Vortice.DXGI.IDXGIDevice>(out long packedVersion))
            {
                throw new InvalidOperationException("DXGI did not expose the Direct3D 12 user-mode driver version.");
            }

            ulong version = unchecked((ulong)packedVersion);
            return String.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}.{1}.{2}.{3}",
                (version >> 48) & 0xffff,
                (version >> 32) & 0xffff,
                (version >> 16) & 0xffff,
                version & 0xffff);
        }

        public override RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index)
        {
            if (m_CommandQueueMap?.TryGetValue(pipeline, out var cmdQueue) == true)
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
            ThrowIfDeviceUnavailable();
            return new Dx12SwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            ThrowIfDeviceUnavailable();
            return new Dx12Fence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            ThrowIfDeviceUnavailable();
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
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require("DX12 heap creation");
            return new Dx12Heap(this, descriptor);
        }

        public override RHIResourceMemoryRequirements GetBufferMemoryRequirements(
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "DX12 buffer memory requirements");
            Vortice.Direct3D12.ResourceDescription nativeDescriptor =
                Dx12MemoryUtility.BuildBufferDescription(descriptor);
            Vortice.Direct3D12.ResourceAllocationInfo allocationInfo =
                NativeDevice.GetResourceAllocationInfo(nativeDescriptor);
            return CreateMemoryRequirements(
                allocationInfo,
                descriptor.StorageMode,
                Dx12MemoryUtility.GetCompatibilityMask(nativeDescriptor),
                ERHIMemoryResourceKind.Buffer);
        }

        public override RHIResourceMemoryRequirements GetTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "DX12 texture memory requirements");
            Vortice.Direct3D12.ResourceDescription nativeDescriptor =
                Dx12MemoryUtility.BuildTextureDescription(descriptor);
            Vortice.Direct3D12.ResourceAllocationInfo allocationInfo =
                NativeDevice.GetResourceAllocationInfo(nativeDescriptor);
            return CreateMemoryRequirements(
                allocationInfo,
                descriptor.StorageMode,
                Dx12MemoryUtility.GetCompatibilityMask(nativeDescriptor),
                ERHIMemoryResourceKind.Texture);
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12Buffer(this, descriptor);
        }

        public override RHIBuffer CreatePlacedBuffer(
            RHIHeap heap,
            ulong heapOffset,
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "DX12 placed buffer creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not Dx12Heap dx12Heap || !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException("Placed buffer heap was created by a different backend or device.", nameof(heap));
            }

            RHIResourceMemoryRequirements requirements = GetBufferMemoryRequirements(descriptor);
            RHIHeapPlacement placement = heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new Dx12Buffer(this, descriptor, dx12Heap, heapOffset, placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12Texture(this, descriptor);
        }

        public override RHITexture CreatePlacedTexture(
            RHIHeap heap,
            ulong heapOffset,
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "DX12 placed texture creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not Dx12Heap dx12Heap || !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException("Placed texture heap was created by a different backend or device.", nameof(heap));
            }

            RHIResourceMemoryRequirements requirements = GetTextureMemoryRequirements(descriptor);
            RHIHeapPlacement placement = heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new Dx12Texture(this, descriptor, dx12Heap, heapOffset, placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        private RHIResourceMemoryRequirements CreateMemoryRequirements(
            in Vortice.Direct3D12.ResourceAllocationInfo allocationInfo,
            ERHIStorageMode storageMode,
            ulong compatibilityMask,
            ERHIMemoryResourceKind resourceKind)
        {
            if (allocationInfo.SizeInBytes == 0 ||
                allocationInfo.SizeInBytes == ulong.MaxValue ||
                allocationInfo.Alignment == 0)
            {
                throw new ArgumentException("DX12 rejected the resource descriptor while querying allocation requirements.");
            }

            return new RHIResourceMemoryRequirements(
                this,
                allocationInfo.SizeInBytes,
                allocationInfo.Alignment,
                storageMode,
                compatibilityMask,
                resourceKind);
        }

        public override RHIMemoryBudget QueryMemoryBudget(
            ERHIStorageMode storageMode)
        {
            ThrowIfDisposed();
            Capabilities.Memory.BudgetQuery.Require("DX12 memory budget query");
            bool unifiedMemory =
                Capabilities.Memory.UnifiedMemory.Tier !=
                    ERHICapabilityTier.Unavailable;
            Vortice.DXGI.MemorySegmentGroup segmentGroup = storageMode switch
            {
                ERHIStorageMode.GPULocal or
                ERHIStorageMode.GPUUpload =>
                    Vortice.DXGI.MemorySegmentGroup.Local,
                ERHIStorageMode.HostUpload or
                ERHIStorageMode.Readback =>
                    unifiedMemory
                        ? Vortice.DXGI.MemorySegmentGroup.Local
                        : Vortice.DXGI.MemorySegmentGroup.NonLocal,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(storageMode),
                    storageMode,
                    "Unknown storage mode.")
            };

            using Vortice.DXGI.IDXGIAdapter3 adapter =
                DXGIAdapter.QueryInterface<Vortice.DXGI.IDXGIAdapter3>();
            Vortice.DXGI.QueryVideoMemoryInfo nativeBudget =
                adapter.QueryVideoMemoryInfo(0, segmentGroup);
            if (nativeBudget.Budget == 0)
            {
                throw new NotSupportedException(
                    $"DXGI reported no {segmentGroup} memory budget for this adapter.");
            }

            return new RHIMemoryBudget(
                storageMode,
                nativeBudget.Budget,
                nativeBudget.CurrentUsage,
                nativeBudget.CurrentReservation);
        }

        public override void RequestResidency(
            in RHIResidencyRequestDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.Residency.Require("DX12 residency request");
            if (!Enum.IsDefined(descriptor.Operation))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    descriptor.Operation,
                    "Unknown residency operation.");
            }

            ReadOnlySpan<RHIHeap> heaps = descriptor.Heaps.Span;
            if (heaps.Length == 0)
            {
                throw new ArgumentException(
                    "A residency request must contain at least one heap.",
                    nameof(descriptor));
            }

            if (descriptor.CompletionFence is not Dx12Fence completionFence ||
                !ReferenceEquals(descriptor.CompletionFence.OwnerDevice, this))
            {
                throw new ArgumentException(
                    "The residency completion fence was created by a different backend or device.",
                    nameof(descriptor));
            }

            Vortice.Direct3D12.ID3D12Pageable[] nativeHeaps =
                new Vortice.Direct3D12.ID3D12Pageable[heaps.Length];
            for (int i = 0; i < heaps.Length; ++i)
            {
                RHIHeap heap = heaps[i] ??
                    throw new ArgumentException(
                        $"Residency heap at index {i} is null.",
                        nameof(descriptor));
                if (heap is not Dx12Heap dx12Heap ||
                    !ReferenceEquals(heap.OwnerDevice, this))
                {
                    throw new ArgumentException(
                        $"Residency heap at index {i} was created by a different backend or device.",
                        nameof(descriptor));
                }

                nativeHeaps[i] = dx12Heap.NativeHeap;
            }

            completionFence.ReserveSignal();
            try
            {
                ulong signalValue = completionFence.PrepareSignalValue();
                if (descriptor.Operation == ERHIResidencyOperation.MakeResident)
                {
                    Dx12Utility.CHECK_HR(
                        NativeDevice.EnqueueMakeResident(
                            Vortice.Direct3D12.ResidencyFlags.None,
                            nativeHeaps,
                            completionFence.NativeFence,
                            signalValue));
                    return;
                }

                NativeDevice.Evict(nativeHeaps);
                completionFence.NativeFence.Signal(signalValue);
            }
            catch
            {
                completionFence.RollbackSignal();
                throw;
            }
        }

        public override RHISparseTextureMemoryRequirements GetSparseTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.SparseBinding.Require("DX12 sparse texture requirements");
            Vortice.Direct3D12.ID3D12Resource resource =
                Dx12SparseMemoryUtility.CreateReservedTexture(this, descriptor);
            try
            {
                return Dx12SparseMemoryUtility.QueryRequirements(
                    this,
                    descriptor,
                    resource);
            }
            finally
            {
                resource.Release();
            }
        }

        public override RHITexture CreateSparseTexture(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.SparseBinding.Require("DX12 sparse textures");
            return new Dx12Texture(
                this,
                descriptor,
                createSparse: true);
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
            return new Dx12ArgumentTableLayout(this, descriptor);
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
            Capabilities.RayTracing.Pipeline.Require("DX12 ray tracing");

            return new Dx12RaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            return new Dx12RasterPipeline(this, descriptor);
        }

        public override RHIPipelineCache CreatePipelineCache()
        {
            Capabilities.PipelineCache.NativeCache.Require("DX12 pipeline cache");
            return new Dx12PipelineCache(this);
        }

        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            Capabilities.MachineLearning.Execution.Require("DX12 machine learning");

            return new Dx12MLPipeline(this, descriptor);
        }

        public override RHIMLBindingSet CreateMLBindingSet(in RHIMLBindingSetDescriptor descriptor)
        {
            Capabilities.MachineLearning.Execution.Require("DX12 machine learning");

            return new Dx12MLBindingSet(this, descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            Capabilities.MachineLearning.Execution.Require("DX12 machine learning tensors");

            return new Dx12Tensor(this, descriptor);
        }

        public override RHIMLProgram CreateMLProgram(in RHIMLProgramDescriptor descriptor)
        {
            Capabilities.MachineLearning.Execution.Require("DX12 machine learning");

            return Dx12MLProgram.Create(descriptor);
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            Capabilities.WorkGraph.Execution.Require("DX12 Work Graph");

            return new Dx12WorkGraphPipeline(this, descriptor);
        }

        public Dx12DescriptorInfo AllocateDsvDescriptor(in int count)
        {
            return AllocateDescriptor(DescriptorHeapDSV, count, "DSV");
        }

        public Dx12DescriptorInfo AllocateRtvDescriptor(in int count)
        {
            return AllocateDescriptor(DescriptorHeapHeapRTV, count, "RTV");
        }

        public Dx12DescriptorInfo AllocateSamplerDescriptor(in int count)
        {
            return AllocateDescriptor(DescriptorHeapSampler, count, "shader-visible sampler");
        }

        public Dx12DescriptorInfo AllocateCbvSrvUavDescriptor(in int count)
        {
            return AllocateDescriptor(DescriptorHeapCbvSrvUav, count, "shader-visible CBV/SRV/UAV");
        }

        public Dx12DescriptorPair AllocateCbvSrvUavDescriptorPair()
        {
            return AllocateDescriptorPair(
                DescriptorHeapCbvSrvUav,
                StagingPoolCbvSrvUav,
                Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                "CBV/SRV/UAV");
        }

        public Dx12DescriptorPair AllocateSamplerDescriptorPair()
        {
            return AllocateDescriptorPair(
                DescriptorHeapSampler,
                StagingPoolSampler,
                Vortice.Direct3D12.DescriptorHeapType.Sampler,
                "sampler");
        }

        public void CopyDescriptorToShaderVisible(in Dx12DescriptorPair descriptors)
        {
            NativeDevice.CopyDescriptorsSimple(
                1,
                descriptors.ShaderVisible.CpuHandle,
                descriptors.Staging.CpuHandle,
                descriptors.NativeType);
        }

        public void FreeDescriptorPair(in Dx12DescriptorPair descriptors)
        {
            switch (descriptors.NativeType)
            {
                case Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView:
                    DescriptorHeapCbvSrvUav.Free(descriptors.ShaderVisible.Index);
                    StagingPoolCbvSrvUav.Free(descriptors.StagingHeap, descriptors.Staging.Index);
                    break;
                case Vortice.Direct3D12.DescriptorHeapType.Sampler:
                    DescriptorHeapSampler.Free(descriptors.ShaderVisible.Index);
                    StagingPoolSampler.Free(descriptors.StagingHeap, descriptors.Staging.Index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptors),
                        descriptors.NativeType,
                        "DX12 descriptor pairs are only supported for shader-visible CBV/SRV/UAV and sampler heaps.");
            }
        }

        private static Dx12DescriptorPair AllocateDescriptorPair(
            Dx12DescriptorHeap shaderVisibleHeap,
            Dx12CpuDescriptorPool stagingPool,
            in Vortice.Direct3D12.DescriptorHeapType nativeType,
            string heapName)
        {
            Dx12CpuDescriptorAllocation staging = stagingPool.Allocate(1, $"staging {heapName}");
            try
            {
                Dx12DescriptorInfo shaderVisible = AllocateDescriptor(shaderVisibleHeap, 1, $"shader-visible {heapName}");
                return new Dx12DescriptorPair(
                    shaderVisible,
                    staging.Descriptor,
                    staging.Heap,
                    nativeType);
            }
            catch
            {
                stagingPool.Free(staging.Heap, staging.Descriptor.Index);
                throw;
            }
        }

        private static Dx12DescriptorInfo AllocateDescriptor(Dx12DescriptorHeap heap, in int count, string heapName)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, $"DX12 {heapName} descriptor allocation count must be positive.");
            }

            int index = heap.Allocate(count);
            if (index < 0)
            {
                throw new InvalidOperationException($"DX12 {heapName} descriptor heap is exhausted: requested {count}, available {heap.AvailableDescriptorCount}, capacity {heap.Capacity}.");
            }

            return heap.GetDescriptorInfo(index);
        }

        public void FreeDsvDescriptor(in int index)
        {
            DescriptorHeapDSV.Free(index);
        }

        public void FreeRtvDescriptor(in int index)
        {
            DescriptorHeapHeapRTV.Free(index);
        }

        public void FreeSamplerDescriptor(in int index)
        {
            DescriptorHeapSampler.Free(index);
        }

        public void FreeCbvSrvUavDescriptor(in int index)
        {
            DescriptorHeapCbvSrvUav.Free(index);
        }

        public void FreeDsvDescriptor(in int index, in int count)
        {
            DescriptorHeapDSV.Free(index, count);
        }

        public void FreeRtvDescriptor(in int index, in int count)
        {
            DescriptorHeapHeapRTV.Free(index, count);
        }

        public void FreeSamplerDescriptor(in int index, in int count)
        {
            DescriptorHeapSampler.Free(index, count);
        }

        public void FreeCbvSrvUavDescriptor(in int index, in int count)
        {
            DescriptorHeapCbvSrvUav.Free(index, count);
        }

        private void CreateDevice()
        {
            Vortice.Direct3D12.ID3D12Device10? device;
            SharpGen.Runtime.Result hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_2, out device);
            if (hResult.Failure)
            {
                hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_1, out device);

                if (hResult.Failure)
                {
                    hResult = CreateNativeDevice(Vortice.Direct3D.FeatureLevel.Level_12_0, out device);
                }
            }
            if (hResult.Failure || device == null)
            {
                throw new InvalidOperationException($"DX12 device creation failed for '{m_Name}'. HRESULT=0x{hResult.Code:X8}. {Dx12Agility.Diagnostic}");
            }

            m_NativeDevice = device;
        }

        private SharpGen.Runtime.Result CreateNativeDevice(Vortice.Direct3D.FeatureLevel featureLevel, out Vortice.Direct3D12.ID3D12Device10? device)
        {
            device = null;

            if (Dx12Agility.TryGetDeviceFactory(out Vortice.Direct3D12.ID3D12DeviceFactory? deviceFactory))
            {
                SharpGen.Runtime.Result result = deviceFactory!.CreateDevice(m_DXGIAdapter!, featureLevel, out Vortice.Direct3D12.ID3D12Device? baseDevice);
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

            return Vortice.Direct3D12.D3D12.D3D12CreateDevice(m_DXGIAdapter!, featureLevel, out device);
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
            bool isFramebufferLocalReadLoweringSupported = true;
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
            ERHIMatrixMajorOrder matrixMajorOrder = ERHIMatrixMajorOrder.RowMajor;
            ERHIDepthValueRange depthValueRange = ERHIDepthValueRange.ZeroToOne;
            ERHIMultiviewStrategy multiviewStrategy = ERHIMultiviewStrategy.RenderTargetIndex;
            ERHIWaveOperationStrategy waveOperationStrategy = ERHIWaveOperationStrategy.Basic;
            bool isEnhancedBarriersSupported = false;
            bool isNativeRenderPassSupported = false;
            bool isSparseBindingSupported = false;
            ERHICapabilityTier sparseBindingTier = ERHICapabilityTier.Unavailable;

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
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.FeatureLevels, ref dLevels);
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

            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options, ref featureOptions0);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options1, ref featureOptions1);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options2, ref featureOptions2);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options3, ref featureOptions3);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options4, ref featureOptions4);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options5, ref featureOptions5);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options6, ref featureOptions6);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options7, ref featureOptions7);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options8, ref featureOptions8);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options9, ref featureOptions9);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options10, ref featureOptions10);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options11, ref featureOptions11);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options12, ref featureOptions12);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options13, ref featureOptions13);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options14, ref featureOptions14);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options15, ref featureOptions15);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options16, ref featureOptions16);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options17, ref featureOptions17);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options18, ref featureOptions18);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options19, ref featureOptions19);
            _ = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options20, ref featureOptions20);
            bool options21Supported = NativeDevice.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options21, ref featureOptions21);

            isRasterizerOrderedSupported = featureOptions0.ROVsSupported;
            isSparseBindingSupported =
                featureOptions0.TiledResourcesTier !=
                Vortice.Direct3D12.TiledResourcesTier.TierNotSupported;
            sparseBindingTier = featureOptions0.TiledResourcesTier switch
            {
                Vortice.Direct3D12.TiledResourcesTier.Tier1 => ERHICapabilityTier.Tier1,
                Vortice.Direct3D12.TiledResourcesTier.Tier2 => ERHICapabilityTier.Tier2,
                Vortice.Direct3D12.TiledResourcesTier.Tier3 or
                Vortice.Direct3D12.TiledResourcesTier.Tier4 => ERHICapabilityTier.Tier3,
                _ => ERHICapabilityTier.Unavailable,
            };

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

            static RHICapability Probe(
                bool available,
                string source,
                string unavailableReason,
                ERHICapabilityTier tier = ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy strategy = ERHICapabilityStrategy.NativeSpecialized,
                RHICapabilityLimits limits = default)
            {
                return RHICapability.FromProbe(
                    available,
                    tier,
                    strategy,
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    source,
                    unavailableReason,
                    limits);
            }

            RHICapabilityLimits rasterLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumSampleCount, (ulong)maxMSAACount),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumVertexInputBindings, (ulong)maxVertexInputBindings),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumColorAttachments, (ulong)maxColorAttachments),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTexture2DSize, (ulong)maxTexture2DSize),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTextureCubeSize, (ulong)maxTextureCubeSize));
            RHICapabilityLimits bindingLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.UniformBufferAlignment, (ulong)uniformBufferAlignment),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumBoundTextures, (ulong)maxBoundTexture));
            RHICapabilityLimits computeLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MinimumWavefrontSize, (ulong)minWavefrontSize),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumWavefrontSize, (ulong)maxWavefrontSize),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumComputeThreads, (ulong)maxComputeThreads),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumGroupSharedMemoryBytes, (ulong)maxGroupShareMemorySize));
            bool nativeStorageAvailable = Dx12StorageQueue.TryProbeNativeSupport(this, out string nativeStorageReason);
            bool nativePipelineCacheAvailable = Dx12PipelineCache.TryProbeNativeSupport(this, out string nativePipelineCacheReason);

            bool nativeMemoryBudgetAvailable;
            try
            {
                using Vortice.DXGI.IDXGIAdapter3 adapter =
                    DXGIAdapter.QueryInterface<Vortice.DXGI.IDXGIAdapter3>();
                nativeMemoryBudgetAvailable =
                    adapter.QueryVideoMemoryInfo(
                        0,
                        Vortice.DXGI.MemorySegmentGroup.Local).Budget != 0;
            }
            catch
            {
                nativeMemoryBudgetAvailable = false;
            }

            m_Capabilities = new RHIDeviceCapabilities(
                raster: new RHIRasterCapabilities(
                    projectionStrategy: isFlipProjection ? ERHIProjectionStrategy.FlipY : ERHIProjectionStrategy.Native,
                    matrixMajorOrder,
                    depthValueRange,
                    multiviewStrategy,
                    pixelShaderStorageWrites: Probe(
                        isPixelShaderUAVSupported,
                        "D3D12 pixel-shader UAV contract",
                        "Pixel-shader storage writes are unavailable.",
                        limits: rasterLimits),
                    rasterOrderedAccess: Probe(
                        isRasterizerOrderedSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS.ROVsSupported",
                        "Rasterizer-ordered views are unavailable or not lowered by SharpGPU."),
                    anisotropicSampling: Probe(
                        isAnisotropyTextureSupported,
                        "D3D12 sampler contract",
                        "Anisotropic sampling is unavailable."),
                    depthAttachmentRead: Probe(
                        isDepthbufferFetchSupported,
                        "D3D12 attachment-read lowering",
                        "Depth attachment reads are not exposed by the current DX12 lowering."),
                    framebufferLocalRead: Probe(
                        isFramebufferLocalReadLoweringSupported,
                        "DX12 OM multipass + private attachment SRV lowering",
                        "Framebuffer-local reads cannot be lowered by the current DX12 adapter."),
                    sampledFeedback: Probe(
                        false,
                        "SharpGPU DX12 sampled-feedback lowering",
                        "DX12 has no exact attachment feedback-loop layout mechanism exposed by this backend."),
                    drawIndirect: Probe(
                        isDrawIndirectSupported,
                        "ID3D12GraphicsCommandList.ExecuteIndirect",
                        "Indirect drawing is unavailable."),
                    multiDrawIndirect: Probe(
                        isDrawMultiIndirectSupported,
                        "ID3D12GraphicsCommandList.ExecuteIndirect",
                        "Multi-draw indirect is unavailable."),
                    variableRateShading: Probe(
                        isVariableRateShadingSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS6.VariableShadingRateTier",
                        "Variable-rate shading is unavailable."),
                    hiddenSurfaceRemoval: Probe(
                        isHiddenSurfaceRemovalSupported,
                        "SharpGPU DX12 raster lowering",
                        "Hidden-surface removal is renderer policy and is not a DX12 HAL capability."),
                    barycentricCoordinates: Probe(
                        isBarycentricCoordSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS3.BarycentricsSupported",
                        "Shader barycentric coordinates are unavailable."),
                    programmableSamplePositions: Probe(
                        isProgrammableSamplePositionSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS2.ProgrammableSamplePositionsTier",
                        "Programmable sample positions tier 2 is unavailable.",
                        tier: ERHICapabilityTier.Tier2),
                    nativeRenderPass: Probe(
                        isNativeRenderPassSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS5.RenderPassesTier",
                        "Native D3D12 render passes are unavailable.",
                        limits: rasterLimits)),
                binding: new RHIBindingCapabilities(
                    rootConstants: Probe(
                        isRootConstantSupport,
                        "D3D12 root signature contract",
                        "Root constants are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi,
                        limits: bindingLimits),
                    indirectRootConstants: Probe(
                        isIndirectRootConstantSupport,
                        "SharpGPU ExecuteIndirect lowering",
                        "Indirect root constants are not exposed by the current DX12 lowering."),
                    atomicUInt64: Probe(
                        isAtomicUInt64Supported,
                        "D3D12 64-bit atomic feature contract",
                        "64-bit shader atomics are unavailable."),
                    descriptorIndexing: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.BackendContract,
                        "D3D12 descriptor tables compiled from logical register namespaces",
                        bindingLimits),
                    partiallyBoundDescriptors: Probe(
                        false,
                        "D3D12 typed-null descriptor contract",
                        "D3D12 cannot represent an optional sampler binding without a typed dummy sampler."),
                    updateAfterBindDescriptors: Probe(
                        false,
                        "SharpGPU external argument-table synchronization contract",
                        "Argument-table mutation while GPU work is pending is intentionally not exposed."),
                    nullDescriptors: Probe(
                        false,
                        "D3D12 typed-null descriptor contract",
                        "D3D12 supports typed null resource descriptors but has no native null sampler descriptor.")),
                synchronization: new RHISynchronizationCapabilities(
                    timestampQueries: Probe(
                        isTimestampQueriesSupported,
                        "D3D12 timestamp query contract",
                        "Timestamp queries are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    occlusionQueries: Probe(
                        isOcclusionQueriesSupported,
                        "D3D12 occlusion query contract",
                        "Occlusion queries are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    pipelineStatisticsQueries: Probe(
                        isPipelineStatsQueriesSupported,
                        "D3D12 pipeline statistics query contract",
                        "Pipeline statistics queries are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    enhancedBarriers: Probe(
                        isEnhancedBarriersSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS12.EnhancedBarriersSupported",
                        "Enhanced barriers are unavailable.")),
                memory: new RHIMemoryCapabilities(
                    unifiedMemory: Probe(
                        isUnifiedMemorySupported,
                        "D3D12_FEATURE_ARCHITECTURE1.UMA",
                        "Unified-memory architecture was not reported for this adapter."),
                    placedResources: Probe(
                        true,
                        "GetResourceAllocationInfo + CreateHeap + CreatePlacedResource",
                        "DX12 placed resources are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    sparseBinding: Probe(
                        isSparseBindingSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS.TiledResourcesTier + CreateReservedResource + UpdateTileMappings",
                        "DX12 tiled resources are unavailable on this adapter.",
                        tier: sparseBindingTier,
                        strategy: ERHICapabilityStrategy.CoreApi),
                    residency: Probe(
                        true,
                        "ID3D12Device3.EnqueueMakeResident + ID3D12Device.Evict",
                        "Asynchronous explicit residency is unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    budgetQuery: Probe(
                        nativeMemoryBudgetAvailable,
                        "IDXGIAdapter3.QueryVideoMemoryInfo",
                        "DXGI memory-budget reporting is unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi)),
                storage: new RHIStorageCapabilities(
                    nativeGpuFileIo: RHICapability.FromProbe(
                        nativeStorageAvailable,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.NativeLibrary,
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        "DirectStorage native factory and file-queue probe",
                        nativeStorageReason)),
                pipelineCache: new RHIPipelineCacheCapabilities(
                    nativeCache: RHICapability.FromProbe(
                        nativePipelineCacheAvailable,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        "ID3D12Device2.CreatePipelineLibrary(empty) plus ID3D12PipelineLibrary1 query",
                        nativePipelineCacheReason)),
                presentation: new RHIPresentationCapabilities(
                    swapChain: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.BackendContract,
                        "DXGI flip-model swapchain"),
                    acquireSignal: RHICapability.Unavailable(
                        "DXGI acquisition does not signal a caller-owned native synchronization primitive.",
                        ERHICapabilityProbeKind.BackendContract,
                        "IDXGISwapChain current back-buffer index"),
                    presentWait: RHICapability.Unavailable(
                        "DXGI Present does not consume caller-owned GPU semaphores.",
                        ERHICapabilityProbeKind.BackendContract,
                        "IDXGISwapChain::Present"),
                    presentCompletion: RHICapability.Unavailable(
                        "A per-present native completion fence is not exposed by the current DXGI contract.",
                        ERHICapabilityProbeKind.BackendContract,
                        "IDXGISwapChain::Present"),
                    maintenance: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.BackendContract,
                        "explicit RHICommandQueue.WaitIdle before caller-owned IDXGISwapChain::ResizeBuffers"),
                    maintenanceStrategy:
                        ERHIPresentationMaintenanceStrategy.QueueIdle,
                    hdr: Probe(
                        isHDRPresentSupported,
                        "DXGI output/color-space probe",
                        "HDR presentation has not been proven for this adapter and surface.")),
                rayTracing: new RHIRayTracingCapabilities(
                    pipeline: Probe(
                        isRaytracingSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS5.RaytracingTier",
                        "DXR pipelines are unavailable."),
                    inline: Probe(
                        isRaytracingInlineSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS5.RaytracingTier",
                        "Inline ray queries require DXR tier 1.1.",
                        tier: ERHICapabilityTier.Tier2)),
                mesh: new RHIMeshCapabilities(
                    shader: Probe(
                        isMeshShadingSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS7.MeshShaderTier plus SharpGPU factory",
                        "Mesh shaders remain unavailable until the native SharpGPU pipeline path is implemented.")),
                machineLearning: new RHIMachineLearningCapabilities(
                    execution: RHICapability.FromProbe(
                        isMLSupported,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.NativeLibrary,
                        ERHICapabilityProbeKind.RuntimeObjectProbe,
                        "DirectML device and command-recorder creation",
                        "DirectML runtime objects could not be created.")),
                workGraph: new RHIWorkGraphCapabilities(
                    execution: Probe(
                        isWorkgraphSupported,
                        "D3D12_FEATURE_D3D12_OPTIONS21.WorkGraphsTier plus SharpGPU pipeline factory",
                        "Work Graphs are unavailable.")),
                compute: new RHIComputeCapabilities(
                    waveOperationStrategy,
                    waveOperations: Probe(
                        waveOperationStrategy is not (ERHIWaveOperationStrategy.None or ERHIWaveOperationStrategy.Pending),
                        "D3D12 wave-operation device contract",
                        "Wave operations are unavailable.",
                        limits: computeLimits)));
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
                directMLDevice = Vortice.DirectML.DML.DMLCreateDevice(NativeDevice, Vortice.DirectML.CreateDeviceFlags.None);
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
            m_DescriptorHeapDSV = new Dx12DescriptorHeap(NativeDevice, Vortice.Direct3D12.DescriptorHeapType.DepthStencilView, Vortice.Direct3D12.DescriptorHeapFlags.None, 4096);
            m_DescriptorHeapHeapRTV = new Dx12DescriptorHeap(NativeDevice, Vortice.Direct3D12.DescriptorHeapType.RenderTargetView, Vortice.Direct3D12.DescriptorHeapFlags.None, 4096);

            // Shader-visible heaps for GPU access - large enough for bindless resource arrays
            m_DescriptorHeapSampler = new Dx12DescriptorHeap(NativeDevice, Vortice.Direct3D12.DescriptorHeapType.Sampler, Vortice.Direct3D12.DescriptorHeapFlags.ShaderVisible, SamplerDescriptorCapacity);
            m_DescriptorHeapCbvSrvUav = new Dx12DescriptorHeap(NativeDevice, Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, Vortice.Direct3D12.DescriptorHeapFlags.ShaderVisible, CbvSrvUavDescriptorCapacity);

            // CPU-only staging pages provide legal descriptor-copy sources without a single oversized native heap.
            m_StagingPoolCbvSrvUav = new Dx12CpuDescriptorPool(
                NativeDevice,
                Vortice.Direct3D12.DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                StagingDescriptorPageCapacity);
            m_StagingPoolSampler = new Dx12CpuDescriptorPool(
                NativeDevice,
                Vortice.Direct3D12.DescriptorHeapType.Sampler,
                SamplerDescriptorCapacity);
            m_NullDescriptors = new Dx12NullDescriptorCache(this);
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
            SharpGen.Runtime.Result hResult = m_NativeDevice!.CreateCommandSignature(commandSignatureDesc, null, out Vortice.Direct3D12.ID3D12CommandSignature? commandSignature);
            m_DrawIndirectSignature = Dx12Utility.RequireCreatedObject(
                commandSignature,
                hResult,
                "ID3D12Device.CreateCommandSignature(draw indirect)");
            #endregion

            #region Create_DrawIndexedIndirect_Argument
            indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
            {
                Type = Vortice.Direct3D12.IndirectArgumentType.DrawIndexed,
            };
            commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
            commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DrawIndexedArguments);
            hResult = NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
            m_DrawIndexedIndirectSignature = Dx12Utility.RequireCreatedObject(
                commandSignature,
                hResult,
                "ID3D12Device.CreateCommandSignature(draw indexed indirect)");
            #endregion

            #region Create_DispatchComputeIndirect_Argument
            indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
            {
                Type = Vortice.Direct3D12.IndirectArgumentType.Dispatch,
            };
            commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
            commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchArguments);
            hResult = NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
            m_DispatchComputeIndirectSignature = Dx12Utility.RequireCreatedObject(
                commandSignature,
                hResult,
                "ID3D12Device.CreateCommandSignature(dispatch compute indirect)");
            #endregion

            #region Create_DispatchMeshIndirect_Argument
            if (Capabilities.Mesh.Shader.Tier != ERHICapabilityTier.Unavailable)
            {
                indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
                {
                    Type = Vortice.Direct3D12.IndirectArgumentType.DispatchMesh,
                };
                commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
                commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchMeshArguments);
                hResult = NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
                m_DispatchMeshIndirectSignature = Dx12Utility.RequireCreatedObject(
                    commandSignature,
                    hResult,
                    "ID3D12Device.CreateCommandSignature(dispatch mesh indirect)");
            }
            #endregion

            #region Create_DispatchRayIndirect_Argument
            if (Capabilities.RayTracing.Pipeline.Tier != ERHICapabilityTier.Unavailable)
            {
                indirectArgDesc = new Vortice.Direct3D12.IndirectArgumentDescription
                {
                    Type = Vortice.Direct3D12.IndirectArgumentType.DispatchRays,
                };
                commandSignatureDesc.IndirectArguments = new[] { indirectArgDesc };
                commandSignatureDesc.ByteStride = sizeof(Vortice.Direct3D12.DispatchRaysDescription);
                hResult = NativeDevice.CreateCommandSignature(commandSignatureDesc, null, out commandSignature);
                m_DispatchRayIndirectSignature = Dx12Utility.RequireCreatedObject(
                    commandSignature,
                    hResult,
                    "ID3D12Device.CreateCommandSignature(dispatch rays indirect)");
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
            DisposeCommandQueues();
            ReleaseComObject(ref m_DirectMLCommandRecorder);
            ReleaseComObject(ref m_DirectMLDevice1);
            ReleaseComObject(ref m_DirectMLDevice);

            DisposeResource(ref m_NullDescriptors);
            DisposeResource(ref m_DescriptorHeapDSV);
            DisposeResource(ref m_DescriptorHeapHeapRTV);
            DisposeResource(ref m_DescriptorHeapSampler);
            DisposeResource(ref m_DescriptorHeapCbvSrvUav);
            DisposeResource(ref m_StagingPoolCbvSrvUav);
            DisposeResource(ref m_StagingPoolSampler);

            ReleaseComObject(ref m_DrawIndirectSignature);
            ReleaseComObject(ref m_DrawIndexedIndirectSignature);
            if (Capabilities.Mesh.Shader.Tier != ERHICapabilityTier.Unavailable)
            {
                ReleaseComObject(ref m_DispatchMeshIndirectSignature);
            }
            if (Capabilities.RayTracing.Pipeline.Tier != ERHICapabilityTier.Unavailable)
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

        private void DisposeCommandQueues()
        {
            Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>? commandQueues = m_CommandQueueMap;
            m_CommandQueueMap = null;
            if (commandQueues == null)
            {
                return;
            }

            foreach (KeyValuePair<ERHIPipelineType, TArray<RHICommandQueue>> pair in commandQueues)
            {
                TArray<RHICommandQueue> queues = pair.Value;
                for (int i = 0; i < queues.length; ++i)
                {
                    queues[i]?.Dispose();
                }
                queues.Clear();
            }

            commandQueues.Clear();
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
#pragma warning restore CA1416
}

namespace SharpGPU
{
#pragma warning disable CA1416
    internal static class Dx12DeviceLossDiagnostics
    {
        internal static void Configure(bool forceEnable)
        {
            if (!forceEnable)
            {
                return;
            }

            SharpGen.Runtime.Result result =
                D3D12.D3D12GetDebugInterface(
                    out ID3D12DeviceRemovedExtendedDataSettings1? settings);
            if (result.Failure || settings == null)
            {
                return;
            }

            try
            {
                settings.SetAutoBreadcrumbsEnablement(
                    DredEnablement.ForcedOn);
                settings.SetPageFaultEnablement(
                    DredEnablement.ForcedOn);
                settings.SetBreadcrumbContextEnablement(
                    DredEnablement.ForcedOn);
            }
            finally
            {
                settings.Dispose();
            }
        }

        internal static RHIException Capture(
            Dx12Device device,
            int triggeringCode,
            string operation)
        {
            ArgumentNullException.ThrowIfNull(device);
            SharpGen.Runtime.Result removedReason =
                device.NativeDevice.DeviceRemovedReason;
            int nativeCode = removedReason.Failure
                ? removedReason.Code
                : triggeringCode;
            ERHIDeviceState state = nativeCode switch
            {
                DxgiErrorDeviceRemoved =>
                    ERHIDeviceState.Removed,
                DxgiErrorDeviceReset =>
                    ERHIDeviceState.Reset,
                _ => ERHIDeviceState.Lost
            };

            StringBuilder message = new StringBuilder(512);
            message.Append(operation);
            message.Append(" reported device loss. ");
            message.Append("DeviceRemovedReason=0x");
            message.Append(
                unchecked((uint)nativeCode).ToString("X8"));

            ID3D12DeviceRemovedExtendedData1? dred =
                device.NativeDevice.QueryInterfaceOrNull<
                    ID3D12DeviceRemovedExtendedData1>();
            if (dred != null)
            {
                try
                {
                    AppendBreadcrumbs(dred, message);
                    AppendPageFault(dred, message);
                }
                catch (Exception exception)
                {
                    message.Append("; DRED capture failed: ");
                    message.Append(exception.GetType().Name);
                }
                finally
                {
                    dred.Dispose();
                }
            }

            return new RHIException(
                ERHIErrorCode.DeviceLost,
                ERHIBackend.DirectX12,
                unchecked((uint)nativeCode),
                message.ToString(),
                state);
        }

        private static void AppendBreadcrumbs(
            ID3D12DeviceRemovedExtendedData1 dred,
            StringBuilder message)
        {
            if (dred.GetAutoBreadcrumbsOutput1(
                    out DredAutoBreadcrumbsOutput1? output).Failure ||
                output == null)
            {
                return;
            }

            AutoBreadcrumbNode1? node =
                output.HeadAutoBreadcrumbNode;
            int count = 0;
            while (node != null && count < MaximumReportedNodes)
            {
                int last = node.LastBreadcrumbValue ?? 0;
                if (last > 0)
                {
                    message.Append("; breadcrumb[");
                    message.Append(count);
                    message.Append("] queue='");
                    message.Append(
                        node.CommandQueueDebugName ?? "<unnamed>");
                    message.Append("' list='");
                    message.Append(
                        node.CommandListDebugName ?? "<unnamed>");
                    message.Append("' progress=");
                    message.Append(last);
                    message.Append('/');
                    message.Append(node.BreadcrumbCount);
                }
                node = node.Next;
                count++;
            }
        }

        private static void AppendPageFault(
            ID3D12DeviceRemovedExtendedData1 dred,
            StringBuilder message)
        {
            if (dred.GetPageFaultAllocationOutput1(
                    out DredPageFaultOutput1? output).Failure ||
                output == null ||
                output.PageFaultVA == 0)
            {
                return;
            }

            message.Append("; pageFaultVA=0x");
            message.Append(output.PageFaultVA.ToString("X"));
            DredAllocationNode1? allocation =
                output.HeadExistingAllocationNode ??
                output.HeadRecentFreedAllocationNode;
            if (allocation != null)
            {
                message.Append(" allocation='");
                message.Append(
                    allocation.ObjectName ?? "<unnamed>");
                message.Append("' type=");
                message.Append(allocation.AllocationType);
            }
        }

        private const int MaximumReportedNodes = 16;
        private const int DxgiErrorDeviceRemoved =
            unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset =
            unchecked((int)0x887A0007);
    }
#pragma warning restore CA1416
}
