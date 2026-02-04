using System;
using System.Threading;
using System.Diagnostics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;
using Silk.NET.Core.Native;

namespace Infinity.Graphics
{
#pragma warning disable CA1416
    internal unsafe class Dx12Fence : RHIFence
    {
        public ID3D12Fence* NativeFence
        {
            get
            {
                return m_NativeFence;
            }
        }
        public override EFenceStatus Status
        {
            get
            {
                return m_NativeFence->GetCompletedValue() > 0 ? EFenceStatus.Success : EFenceStatus.NotReady;
            }
        }

        private ID3D12Fence* m_NativeFence;
        private AutoResetEvent m_FenceEvent;

        public Dx12Fence(Dx12Device device)
        {
            ID3D12Fence* fence;
            HRESULT hResult = device.NativeDevice->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, __uuidof<ID3D12Fence>(), (void**)&fence);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeFence = fence;

            m_FenceEvent = new AutoResetEvent(false);
#if DEBUG
            Debug.Assert(m_FenceEvent != null, "SystemEvent is null");
#endif
        }

        public override void Reset()
        {
            m_NativeFence->Signal(0);
        }

        public override void Wait()
        {
            IntPtr eventPtr = m_FenceEvent.SafeWaitHandle.DangerousGetHandle();
            HANDLE eventHandle = new HANDLE(eventPtr.ToPointer());
            m_NativeFence->SetEventOnCompletion(1, eventHandle);
            m_FenceEvent.WaitOne();
        }

        protected override void Release()
        {
            m_NativeFence->Release();
        }
    }

    internal unsafe class Dx12Semaphore : RHISemaphore
    {
        public ID3D12Fence* NativeFence
        {
            get
            {
                return m_NativeFence;
            }
        }

        private ID3D12Fence* m_NativeFence;

        public Dx12Semaphore(Dx12Device device)
        {
            ID3D12Fence* fence;
            HRESULT hResult = device.NativeDevice->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, __uuidof<ID3D12Fence>(), (void**)&fence);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeFence = fence;
        }

        protected override void Release()
        {
            m_NativeFence->Release();
        }
    }
#pragma warning restore CA1416
}
