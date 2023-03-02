using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602
    internal unsafe struct Dx12CommandQueueDescriptor
    {
        public EQueueType Type;
        public ID3D12CommandQueue* Queue;
    }

    internal unsafe class Dx12Queue : RHIQueue
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }
        public ID3D12CommandQueue* NativeCommandQueue
        {
            get
            {
                return m_NativeCommandQueue;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12CommandQueue* m_NativeCommandQueue;

        public Dx12Queue(Dx12Device device, in Dx12CommandQueueDescriptor descriptor)
        {
            m_Type = descriptor.Type;
            m_Dx12Device = device;
            m_NativeCommandQueue = descriptor.Queue;
        }

        public override RHICommandPool CreateCommandPool()
        {
            return new Dx12CommandPool(this);
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence fence)
        {
            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (fence != null)
            {
                Dx12Fence dx12Fence = fence as Dx12Fence;
                dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        protected override void Release()
        {
            m_NativeCommandQueue->Release();
        }
    }
#pragma warning restore CS8600, CS8602
}
