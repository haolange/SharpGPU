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

        public override void ResourceBarrier(in RHIBarrier barrier)
        {
            ID3D12Resource* resource = null;
            D3D12_RESOURCE_BARRIER resourceBarrier;

            switch (barrier.BarrierType)
            {
                case ERHIBarrierType.UAV:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                    break;

                case ERHIBarrierType.Aliasing:
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif
                        resource = buffer.NativeResource;
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif
                        resource = texture.NativeResource;
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitAliasing(null, resource);
                    break;

                case ERHIBarrierType.Triansition:
                    D3D12_RESOURCE_STATES srcState;
                    D3D12_RESOURCE_STATES dstState;
                    if (barrier.ResourceType == ERHIResourceType.Buffer)
                    {
                        Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                        Debug.Assert(buffer != null, "Barrier Buffer is null");
#endif

                        resource = buffer.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                    }
                    else
                    {
                        Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                        Debug.Assert(texture != null, "Barrier Texture is null");
#endif

                        resource = texture.NativeResource;
                        srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                        dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                    }
                    resourceBarrier = D3D12_RESOURCE_BARRIER.InitTransition(resource, srcState, dstState);
                    break;
            }

            m_NativeCommandList->ResourceBarrier(1, &resourceBarrier);
        }

        public override void ResourceBarriers(in Memory<RHIBarrier> barriers)
        {
            ID3D12Resource* resource;
            D3D12_RESOURCE_STATES srcState;
            D3D12_RESOURCE_STATES dstState;
            D3D12_RESOURCE_BARRIER* resourceBarriers = stackalloc D3D12_RESOURCE_BARRIER[barriers.Length];

            for (int i = 0; i < barriers.Length; ++i)
            {
                ref RHIBarrier barrier = ref barriers.Span[i];

                switch (barrier.BarrierType)
                {
                    case ERHIBarrierType.UAV:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitUAV(resource);
                        break;

                    case ERHIBarrierType.Aliasing:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif
                            resource = buffer.NativeResource;
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif
                            resource = texture.NativeResource;
                        }
                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitAliasing(null, resource);
                        break;

                    case ERHIBarrierType.Triansition:
                        if (barrier.ResourceType == ERHIResourceType.Buffer)
                        {
                            Dx12Buffer buffer = barrier.BufferBarrierInfo.Handle as Dx12Buffer;
#if DEBUG
                            Debug.Assert(buffer != null, String.Format("Barrier Buffer is null at index {0}.", i));
#endif

                            resource = buffer.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12BufferState(barrier.BufferBarrierInfo.DstState);
                        }
                        else
                        {
                            Dx12Texture texture = barrier.TextureBarrierInfo.Handle as Dx12Texture;
#if DEBUG
                            Debug.Assert(texture != null, String.Format("Barrier Texture is null at index {0}.", i));
#endif

                            resource = texture.NativeResource;
                            srcState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.SrcState);
                            dstState = Dx12Utility.ConvertToDx12TextureState(barrier.TextureBarrierInfo.DstState);
                        }
                        resourceBarriers[i] = D3D12_RESOURCE_BARRIER.InitTransition(resource, srcState, dstState);
                        break;
                }
            }

            m_NativeCommandList->ResourceBarrier((uint)barriers.Length, resourceBarriers);
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

    internal unsafe class Dx12IndirectComputeCommandBuffer : RHIIndirectComputeCommandBuffer
    {
        public Dx12IndirectComputeCommandBuffer(Dx12Device device, in RHIIndirectComputeCommandBufferDescription descriptor)
        {

        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12IndirectRayTracingCommandBuffer : RHIIndirectRayTracingCommandBuffer
    {
        public Dx12IndirectRayTracingCommandBuffer(Dx12Device device, in RHIIndirectRayTracingCommandBufferDescription descriptor)
        {

        }

        protected override void Release()
        {

        }
    }

    internal unsafe class Dx12IndirectRasterCommandBuffer : RHIIndirectRasterCommandBuffer
    {
        public Dx12IndirectRasterCommandBuffer(Dx12Device device, in RHIIndirectRasterCommandBufferDescription descriptor)
        {

        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
