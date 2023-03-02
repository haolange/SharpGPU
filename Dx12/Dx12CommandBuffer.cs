using System;
using System.Diagnostics;
using TerraFX.Interop.DirectX;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12CommandBuffer : RHICommandBuffer
    {
        public ID3D12GraphicsCommandList5* NativeCommandList
        {
            get
            {
                return m_NativeCommandList;
            }
        }

        private Dx12BlitEncoder m_BlitEncoder;
        private Dx12ComputeEncoder m_ComputeEncoder;
        private Dx12GraphicsEncoder m_GraphicsEncoder;
        private ID3D12GraphicsCommandList5* m_NativeCommandList;

        public Dx12CommandBuffer(Dx12CommandPool cmdPool)
        {
            m_CommandPool = cmdPool;
            Dx12Queue queue = cmdPool.Queue as Dx12Queue;
            Dx12CommandPool dx12CommandPool = m_CommandPool as Dx12CommandPool;

            ID3D12GraphicsCommandList5* commandList;
            bool success = SUCCEEDED(queue.Dx12Device.NativeDevice->CreateCommandList(0, Dx12Utility.ConvertToDx12QueueType(queue.Type), dx12CommandPool.NativeCommandAllocator, null, __uuidof<ID3D12GraphicsCommandList5>(), (void**)&commandList));
            Debug.Assert(success);
            m_NativeCommandList = commandList;

            m_BlitEncoder = new Dx12BlitEncoder(this);
            m_ComputeEncoder = new Dx12ComputeEncoder(this);
            m_GraphicsEncoder = new Dx12GraphicsEncoder(this);
        }

        public override void Begin(string name)
        {
            Dx12CommandPool dx12CommandPool = m_CommandPool as Dx12CommandPool;
            Dx12Queue queue = m_CommandPool.Queue as Dx12Queue;

            m_NativeCommandList->Reset(dx12CommandPool.NativeCommandAllocator, null);

            IntPtr namePtr = Marshal.StringToHGlobalUni(name);
            m_NativeCommandList->BeginEvent(0, namePtr.ToPointer(), (uint)name.Length * 2);
            Marshal.FreeHGlobal(namePtr);

            ID3D12DescriptorHeap** resourceBarriers = stackalloc ID3D12DescriptorHeap*[2];
            resourceBarriers[0] = queue.Dx12Device.SamplerHeap.DescriptorHeap;
            resourceBarriers[1] = queue.Dx12Device.CbvSrvUavHeap.DescriptorHeap;
            m_NativeCommandList->SetDescriptorHeaps(2, &*resourceBarriers);
        }

        public override RHIBlitEncoder GetBlitEncoder()
        {
            return m_BlitEncoder;
        }

        public override RHIComputeEncoder GetComputeEncoder()
        {
            return m_ComputeEncoder;
        }

        public override RHIGraphicsEncoder GetGraphicsEncoder()
        {
            return m_GraphicsEncoder;
        }

        public override void End()
        {
            m_NativeCommandList->EndEvent();
            m_NativeCommandList->Close();
        }

        /*public override void Commit(RHIFence? fence)
        {
            Dx12CommandPool dx12CommandPool = m_CommandPool as Dx12CommandPool;
            Dx12Queue queue = m_CommandPool.Queue as Dx12Queue;

            ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)m_NativeCommandList };
            queue.NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);

            if (fence != null)
            {
                Dx12Fence dx12Fence = fence as Dx12Fence;
                dx12Fence.Reset();
                queue.NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }*/

        protected override void Release()
        {
            m_NativeCommandList->Release();
        }
    }

    internal unsafe class Dx12IndirectCommandBuffer : RHIIndirectCommandBuffer
    {
        public Dx12IndirectCommandBuffer(Dx12Device device, in RHIIndirectCommandBufferDescription descriptor)
        {

        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
