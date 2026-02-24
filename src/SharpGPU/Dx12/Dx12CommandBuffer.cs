using System;
using System.Diagnostics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12CommandBuffer : RHICommandBuffer
    {
        public ID3D12CommandAllocator* NativeCommandAllocator
        {
            get
            {
                return m_NativeCommandAllocator;
            }
        }
        public ID3D12GraphicsCommandList7* NativeCommandList
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
        private ID3D12CommandAllocator* m_NativeCommandAllocator;
        private ID3D12GraphicsCommandList7* m_NativeCommandList;

        public Dx12CommandBuffer(Dx12CommandQueue commandQueue)
        {
            m_CommandQueue = commandQueue;

            ID3D12CommandAllocator* commandAllocator;
            HRESULT hResult = commandQueue.Dx12Device.NativeDevice->CreateCommandAllocator(Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType), __uuidof<ID3D12CommandAllocator>(), (void**)&commandAllocator);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeCommandAllocator = commandAllocator;

            ID3D12GraphicsCommandList7* commandList;
            hResult = commandQueue.Dx12Device.NativeDevice->CreateCommandList(0, Dx12Utility.ConvertToDx12QueueType(commandQueue.PipelineType), m_NativeCommandAllocator, null, __uuidof<ID3D12GraphicsCommandList7>(), (void**)&commandList);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeCommandList = commandList;

            m_TransferEncoder = new Dx12TransferEncoder(this);
            m_ComputeEncoder = new Dx12ComputeEncoder(this);
            m_RasterEncoder = new Dx12RasterEncoder(this);
            m_RaytracingEncoder = new Dx12RaytracingEncoder(this);
            m_MLEncoder = new Dx12MLEncoder(this);
            m_WorkGraphEncoder = new Dx12WorkGraphEncoder(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Begin(string name)
        {
            m_NativeCommandAllocator->Reset();
            m_NativeCommandList->Reset(m_NativeCommandAllocator, null);

#if DEBUG
            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            m_NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);
#endif

            Dx12CommandQueue commandQueue = m_CommandQueue as Dx12CommandQueue;
            ID3D12DescriptorHeap** resourceBarriers = stackalloc ID3D12DescriptorHeap*[2];
            {
                resourceBarriers[0] = commandQueue.Dx12Device.DescriptorHeapSampler.NativeDescriptorHeap;
                resourceBarriers[1] = commandQueue.Dx12Device.DescriptorHeapCbvSrvUav.NativeDescriptorHeap;
            }
            m_NativeCommandList->SetDescriptorHeaps(2, &*resourceBarriers);
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
            m_NativeCommandList->EndEvent();
            m_NativeCommandList->Close();
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

            ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)m_NativeCommandList };
            commandQueue.NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);

            if (fence != null)
            {
                Dx12Fence dx12Fence = fence as Dx12Fence;
                dx12Fence.Reset();
                commandQueue.NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }*/

        protected override void Release()
        {
            m_NativeCommandList->Release();
            m_NativeCommandAllocator->Release();
        }
    }

    internal unsafe class Dx12ComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        public ID3D12CommandSignature* NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public ID3D12Resource* NativeArgumentBuffer
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
        private ID3D12Resource* m_NativeArgumentBuffer;
        private ID3D12CommandSignature* m_NativeCommandSignature;

        public Dx12ComputeIndirectCommandBuffer(Dx12Device device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchComputeIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(m_MaxCommandCount * (uint)sizeof(D3D12_DISPATCH_ARGUMENTS));
            D3D12_HEAP_PROPERTIES heapProps = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT);

            ID3D12Resource* resource;
            HRESULT hResult = device.NativeDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON, null, __uuidof<ID3D12Resource>(), (void**)&resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer->Release();
        }
    }

    internal unsafe class Dx12RayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        public ID3D12CommandSignature* NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public ID3D12Resource* NativeArgumentBuffer
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
        private ID3D12Resource* m_NativeArgumentBuffer;
        private ID3D12CommandSignature* m_NativeCommandSignature;

        public Dx12RayTracingIndirectCommandBuffer(Dx12Device device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DispatchRayIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(m_MaxCommandCount * (uint)sizeof(D3D12_DISPATCH_RAYS_DESC));
            D3D12_HEAP_PROPERTIES heapProps = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT);

            ID3D12Resource* resource;
            HRESULT hResult = device.NativeDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON, null, __uuidof<ID3D12Resource>(), (void**)&resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer->Release();
        }
    }

    internal unsafe class Dx12RasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        public ID3D12CommandSignature* NativeCommandSignature
        {
            get
            {
                return m_NativeCommandSignature;
            }
        }
        public ID3D12Resource* NativeArgumentBuffer
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
        private ID3D12Resource* m_NativeArgumentBuffer;
        private ID3D12CommandSignature* m_NativeCommandSignature;

        public Dx12RasterIndirectCommandBuffer(Dx12Device device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_Dx12Device = device;
            m_NativeCommandSignature = device.DrawIndexedIndirectSignature;
            m_MaxCommandCount = descriptor.MaxCommandCount;

            D3D12_RESOURCE_DESC bufferDesc = D3D12_RESOURCE_DESC.Buffer(m_MaxCommandCount * (uint)sizeof(D3D12_DRAW_INDEXED_ARGUMENTS));
            D3D12_HEAP_PROPERTIES heapProps = new D3D12_HEAP_PROPERTIES(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT);

            ID3D12Resource* resource;
            HRESULT hResult = device.NativeDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &bufferDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON, null, __uuidof<ID3D12Resource>(), (void**)&resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeArgumentBuffer = resource;
        }

        protected override void Release()
        {
            m_NativeArgumentBuffer->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
