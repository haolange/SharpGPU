using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12CommandBuffer : RHICommandBuffer
    {
        public Vortice.Direct3D12.ID3D12CommandAllocator NativeCommandAllocator
        {
            get
            {
                return m_NativeCommandAllocator;
            }
        }
        public Vortice.Direct3D12.ID3D12GraphicsCommandList7 NativeCommandList
        {
            get
            {
                return m_NativeCommandList;
            }
        }

        private Dx12TransferEncoder m_TransferEncoder;
        private Dx12ComputeEncoder m_ComputeEncoder;
        private Dx12RasterEncoder m_RasterEncoder;
        private Dx12RaytracingEncoder m_RaytracingEncoder;
        private Dx12MLEncoder m_MLEncoder;
        private Dx12WorkGraphEncoder m_WorkGraphEncoder;
        private Vortice.Direct3D12.ID3D12CommandAllocator m_NativeCommandAllocator;
        private Vortice.Direct3D12.ID3D12GraphicsCommandList7 m_NativeCommandList;
        private Vortice.Direct3D12.ID3D12DescriptorHeap[] m_DescriptorHeaps;

        public Dx12CommandBuffer(Dx12CommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;
            InitializeNativeObjects();
            m_TransferEncoder = new Dx12TransferEncoder(this);
            m_ComputeEncoder = new Dx12ComputeEncoder(this);
            m_RasterEncoder = new Dx12RasterEncoder(this);
            m_RaytracingEncoder = new Dx12RaytracingEncoder(this);
            m_MLEncoder = new Dx12MLEncoder(this);
            m_WorkGraphEncoder = new Dx12WorkGraphEncoder(this);

            Dx12Device device = commandQueue.Dx12Device;
            m_DescriptorHeaps = new Vortice.Direct3D12.ID3D12DescriptorHeap[]
            {
                device.DescriptorHeapSampler.NativeDescriptorHeap,
                device.DescriptorHeapCbvSrvUav.NativeDescriptorHeap,
            };
        }

        private void InitializeNativeObjects()
        {
            Debug.Assert(m_CommandQueue != null, "CommandQueue is null.");
            Dx12CommandQueue commandQueue = m_CommandQueue as Dx12CommandQueue;
            Vortice.Direct3D12.ID3D12CommandAllocator commandAllocator;
            SharpGen.Runtime.Result hResult = commandQueue.Dx12Device.NativeDevice.CreateCommandAllocator(
                Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType),
                out commandAllocator);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            if (commandAllocator == null)
            {
                throw new InvalidOperationException("Failed to create ID3D12CommandAllocator.");
            }
            m_NativeCommandAllocator = commandAllocator;

            Vortice.Direct3D12.ID3D12GraphicsCommandList7 commandList;
            hResult = commandQueue.Dx12Device.NativeDevice.CreateCommandList(
                0,
                Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType),
                m_NativeCommandAllocator,
                null!,
                out commandList);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            if (commandList == null)
            {
                throw new InvalidOperationException("Failed to create ID3D12GraphicsCommandList7.");
            }
            m_NativeCommandList = commandList;
            // D3D12 command lists are created in the recording state; close once so the first Begin() can Reset safely.
            m_NativeCommandList.Close();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            try
            {
                m_NativeCommandAllocator.Reset();
                m_NativeCommandList.Reset(m_NativeCommandAllocator, null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Dx12CommandBuffer.Begin failed to reset command allocator/list. " +
                    "The allocator is likely still in flight on GPU, or submission/fence sequencing is invalid.",
                    ex);
            }

#if DEBUG
            Dx12PixEventMarker.BeginEvent((nint)m_NativeCommandList, name);
#endif

            m_NativeCommandList.SetDescriptorHeaps(m_DescriptorHeaps);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
        {
            m_TransferEncoder.BeginPass(descriptor);
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndTransferPass()
        {
            m_TransferEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor)
        {
            m_ComputeEncoder.BeginPass(descriptor);
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndComputePass()
        {
            m_ComputeEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor)
        {
            m_RaytracingEncoder.BeginPass(descriptor);
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRaytracingPass()
        {
            m_RaytracingEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor)
        {
            m_RasterEncoder.BeginPass(descriptor);
            return m_RasterEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndRasterPass()
        {
            m_RasterEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor)
        {
            m_MLEncoder.BeginPass(descriptor);
            return m_MLEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndMLPass()
        {
            m_MLEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void End()
        {
#if DEBUG
            Dx12PixEventMarker.EndEvent((nint)m_NativeCommandList);
#endif
            try
            {
                m_NativeCommandList.Close();
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                if (m_CommandQueue is Dx12CommandQueue commandQueue)
                {
                    Dx12PipelineDebug.DumpDeviceMessages(commandQueue.Dx12Device, "[Dx12CommandBuffer.End]");
                }

                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHITransferEncoder GetTransferEncoder()
        {
            return m_TransferEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIComputeEncoder GetComputeEncoder()
        {
            return m_ComputeEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRaytracingEncoder GetRaytracingEncoder()
        {
            return m_RaytracingEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIRasterEncoder GetRasterEncoder()
        {
            return m_RasterEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIMLEncoder GetMLEncoder()
        {
            return m_MLEncoder;
        }

        public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor)
        {
            m_WorkGraphEncoder.BeginPass(descriptor);
            return m_WorkGraphEncoder;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndWorkGraphPass()
        {
            m_WorkGraphEncoder.EndPass();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override RHIWorkGraphEncoder GetWorkGraphEncoder()
        {
            return m_WorkGraphEncoder;
        }

        /*public override void Commit(RHIFence? fence)
        {
            Dx12CommandAllocator dx12CommandPool = m_CommandPool as Dx12CommandAllocator;
            Dx12CommandQueue commandQueue = m_CommandPool.CommandQueue as Dx12CommandQueue;

            Vortice.Direct3D12.ID3D12CommandList* ppCommandLists = stackalloc Vortice.Direct3D12.ID3D12CommandList[1] { (Vortice.Direct3D12.ID3D12CommandList)m_NativeCommandList };
            commandQueue.NativeCommandQueue.ExecuteCommandLists(1, ppCommandLists);

            if (fence != null)
            {
                Dx12Fence dx12Fence = fence as Dx12Fence;
                dx12Fence.Reset();
                commandQueue.NativeCommandQueue.Signal(dx12Fence.NativeFence, 1);
            }
        }*/

        protected override void Release()
        {
            m_NativeCommandList.Release();
            m_NativeCommandAllocator.Release();
        }
    }

    internal unsafe class Dx12ComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        public Vortice.Direct3D12.ID3D12CommandSignature NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeArgumentBuffer
        {
            get
            {
                return m_NativeArgumentBuffer;
            }
        }
        public uint MaxCommandCount
        {
            get
            {
                return m_MaxCommandCount;
            }
        }

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeArgumentBuffer;
        private Vortice.Direct3D12.ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12ComputeIndirectCommandBuffer(Dx12Device device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchComputeIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            Vortice.Direct3D12.ResourceDescription bufferDesc = Vortice.Direct3D12.ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(Vortice.Direct3D12.DispatchArguments));
            Vortice.Direct3D12.HeapProperties heapProps = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Default);

            Vortice.Direct3D12.ID3D12Resource resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                Vortice.Direct3D12.HeapFlags.None,
                bufferDesc,
                Vortice.Direct3D12.ResourceStates.Common,
                null,
                out resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            if (resource == null)
            {
                throw new InvalidOperationException("Failed to create compute indirect argument buffer resource.");
            }
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }

    internal unsafe class Dx12RayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        public Vortice.Direct3D12.ID3D12CommandSignature NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeArgumentBuffer
        {
            get
            {
                return m_NativeArgumentBuffer;
            }
        }
        public uint MaxCommandCount
        {
            get
            {
                return m_MaxCommandCount;
            }
        }

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeArgumentBuffer;
        private Vortice.Direct3D12.ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12RayTracingIndirectCommandBuffer(Dx12Device device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchRayIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            Vortice.Direct3D12.ResourceDescription bufferDesc = Vortice.Direct3D12.ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(Vortice.Direct3D12.DispatchRaysDescription));
            Vortice.Direct3D12.HeapProperties heapProps = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Default);

            Vortice.Direct3D12.ID3D12Resource resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                Vortice.Direct3D12.HeapFlags.None,
                bufferDesc,
                Vortice.Direct3D12.ResourceStates.Common,
                null,
                out resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            if (resource == null)
            {
                throw new InvalidOperationException("Failed to create ray tracing indirect argument buffer resource.");
            }
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }

    internal unsafe class Dx12RasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        public Vortice.Direct3D12.ID3D12CommandSignature NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeArgumentBuffer
        {
            get
            {
                return m_NativeArgumentBuffer;
            }
        }
        public uint MaxCommandCount
        {
            get
            {
                return m_MaxCommandCount;
            }
        }

        private uint m_MaxCommandCount;
        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeArgumentBuffer;
        private Vortice.Direct3D12.ID3D12CommandSignature m_NativeCommandSignature;

        public Dx12RasterIndirectCommandBuffer(Dx12Device device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DrawIndexedIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            Vortice.Direct3D12.ResourceDescription bufferDesc = Vortice.Direct3D12.ResourceDescription.Buffer(m_MaxCommandCount * (uint)sizeof(Vortice.Direct3D12.DrawIndexedArguments));
            Vortice.Direct3D12.HeapProperties heapProps = new Vortice.Direct3D12.HeapProperties(Vortice.Direct3D12.HeapType.Default);

            Vortice.Direct3D12.ID3D12Resource resource;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateCommittedResource(
                heapProps,
                Vortice.Direct3D12.HeapFlags.None,
                bufferDesc,
                Vortice.Direct3D12.ResourceStates.Common,
                null,
                out resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            if (resource == null)
            {
                throw new InvalidOperationException("Failed to create raster indirect argument buffer resource.");
            }
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer.Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
