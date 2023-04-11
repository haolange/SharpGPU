using System.Diagnostics;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12CommandAllocator : RHICommandAllocator
    {
        public ID3D12CommandAllocator* NativeCommandAllocator
        {
            get
            {
                return m_NativeCommandAllocator;
            }
        }

        private ID3D12CommandAllocator* m_NativeCommandAllocator;

        public Dx12CommandAllocator(Dx12Queue queue)
        {
            m_Queue = queue;

            ID3D12CommandAllocator* commandAllocator;
            bool success = SUCCEEDED(queue.Dx12Device.NativeDevice->CreateCommandAllocator(Dx12Utility.ConvertToDx12QueueType(queue.Type), __uuidof<ID3D12CommandAllocator>(), (void**)&commandAllocator));
            Debug.Assert(success);
            m_NativeCommandAllocator = commandAllocator;
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new Dx12CommandBuffer(this);
        }

        public override void Reset()
        {
            m_NativeCommandAllocator->Reset();
        }

        protected override void Release()
        {
            m_NativeCommandAllocator->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
