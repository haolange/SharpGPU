using System;
using Infinity.Core;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;
using Silk.NET.Core.Native;

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

        public Dx12CommandQueue(Dx12Device device, in ERHIPipelineType pipeline)
        {
            m_Dx12Device = device;
            m_PipelineType = pipeline;

            D3D12_COMMAND_QUEUE_DESC queueDesc = new D3D12_COMMAND_QUEUE_DESC();
            queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAGS.D3D12_COMMAND_QUEUE_FLAG_NONE;
            queueDesc.Type = Dx12Utility.ConvertToDx12QueueType(pipeline);

            ID3D12CommandQueue* commandQueue;
            HRESULT hResult = m_Dx12Device.NativeDevice->CreateCommandQueue(&queueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&commandQueue);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif

            m_NativeCommandQueue = commandQueue;
        }

        public override RHICommandBuffer CreateCommandBuffer()
        {
            return new Dx12CommandBuffer(this);
        }

        public override void MapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            throw new NotImplementedException();
        }

        public override void UnMapTiledTexture(in RHITiledTextureRegions tiledTextureRegions)
        {
            throw new NotImplementedException();
        }

        public override void MapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            throw new NotImplementedException();
        }

        public override void UnMapPackedMips(in RHITiledTexturePackedMips tiledTexturePackedMips)
        {
            throw new NotImplementedException();
        }

        public override void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore)
        {
            if (waitSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = waitSemaphore as Dx12Semaphore;
                m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (signalSemaphore != null)
            {
                Dx12Semaphore dx12Semaphore = signalSemaphore as Dx12Semaphore;
                dx12Semaphore.NativeFence->Signal(0);
                m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        public override void Submits(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
                }
            }

            if (cmdBuffer != null)
            {
                Dx12CommandBuffer dx12CommandBuffer = cmdBuffer as Dx12CommandBuffer;
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[1] { (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList };
                m_NativeCommandQueue->ExecuteCommandLists(1, ppCommandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    dx12Semaphore.NativeFence->Signal(0);
                    m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        public override void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores)
        {
            if (waitSemaphores != null)
            {
                for (int i = 0; i < waitSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = waitSemaphores[i] as Dx12Semaphore;
                    m_NativeCommandQueue->Wait(dx12Semaphore.NativeFence, 1);
                }
            }

            if (cmdBuffers != null)
            {
                ID3D12CommandList** ppCommandLists = stackalloc ID3D12CommandList*[cmdBuffers.Length];
                for (int i = 0; i < cmdBuffers.Length; ++i)
                {
                    Dx12CommandBuffer dx12CommandBuffer = cmdBuffers[i] as Dx12CommandBuffer;
                    ppCommandLists[i] = (ID3D12CommandList*)dx12CommandBuffer.NativeCommandList;
                }
                m_NativeCommandQueue->ExecuteCommandLists((uint)cmdBuffers.Length, ppCommandLists);
            }

            if (signalSemaphores != null)
            {
                for (int i = 0; i < signalSemaphores.Length; ++i)
                {
                    Dx12Semaphore dx12Semaphore = signalSemaphores[i] as Dx12Semaphore;
                    dx12Semaphore.NativeFence->Signal(0);
                    m_NativeCommandQueue->Signal(dx12Semaphore.NativeFence, 1);
                }
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                //dx12Fence.NativeFence->Signal(0); // dx12Fence.Reset();
                m_NativeCommandQueue->Signal(dx12Fence.NativeFence, 1);
            }
        }

        protected override void Release()
        {
            m_NativeCommandQueue->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
