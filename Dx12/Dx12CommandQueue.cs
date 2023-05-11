using System;
using Infinity.Core;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe class Dx12CommandQueue : RHICommandQueue
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
        public override ulong Frequency
        {
            get
            {
                ulong result = 0;
                m_NativeCommandQueue->GetTimestampFrequency(&result);
                return result;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12CommandQueue* m_NativeCommandQueue;

        public Dx12CommandQueue(Dx12Device device, in EQueueType queueType)
        {
            m_Type = queueType;
            m_Dx12Device = device;

            D3D12_COMMAND_QUEUE_DESC queueDesc = new D3D12_COMMAND_QUEUE_DESC();
            queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE;
            queueDesc.Type = Dx12Utility.ConvertToDx12QueueType(queueType);

            ID3D12CommandQueue* commandQueue;
            bool success = SUCCEEDED(m_Dx12Device.NativeDevice->CreateCommandQueue(&queueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&commandQueue));
            Debug.Assert(success);

            m_NativeCommandQueue = commandQueue;
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new Dx12CommandBuffer(this);
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }

            if (signalSemaphore != null)
            {
                throw new NotImplementedException("ToDo : signal semaphore");
                //m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }

            if (waitSemaphore != null)
            {
                throw new NotImplementedException("ToDo : wait semaphore");
                //m_NativeCommandQueue->Wait(dx12Fence.NativeFence, 1);
            }
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence fence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            throw new NotImplementedException("ToDo : batch submit");
        }

        protected override void Release()
        {
            m_NativeCommandQueue->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
