using System;
using System.Threading;
using System.Diagnostics;

namespace Infinity.Graphics
{
#pragma warning disable CA1416
    internal unsafe class Dx12Fence : RHIFence
    {
        public Vortice.Direct3D12.ID3D12Fence NativeFence
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
                return m_NativeFence.CompletedValue > 0 ? EFenceStatus.Success : EFenceStatus.NotReady;
            }
        }

        private Vortice.Direct3D12.ID3D12Fence m_NativeFence;
        private AutoResetEvent m_FenceEvent;

        public Dx12Fence(Dx12Device device)
        {
            Vortice.Direct3D12.ID3D12Fence fence;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateFence(0, Vortice.Direct3D12.FenceFlags.None, out fence);
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
            m_NativeFence.Signal(0);
        }

        public override void Wait()
        {
            IntPtr eventPtr = m_FenceEvent.SafeWaitHandle.DangerousGetHandle();
            System.IntPtr eventHandle = new System.IntPtr(eventPtr.ToPointer());
            m_NativeFence.SetEventOnCompletion(1, eventHandle);
            m_FenceEvent.WaitOne();
        }

        protected override void Release()
        {
            m_NativeFence.Release();
        }
    }

    internal unsafe class Dx12Semaphore : RHISemaphore
    {
        public Vortice.Direct3D12.ID3D12Fence NativeFence
        {
            get
            {
                return m_NativeFence;
            }
        }

        private Vortice.Direct3D12.ID3D12Fence m_NativeFence;

        public Dx12Semaphore(Dx12Device device)
        {
            Vortice.Direct3D12.ID3D12Fence fence;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateFence(0, Vortice.Direct3D12.FenceFlags.None, out fence);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeFence = fence;
        }

        protected override void Release()
        {
            m_NativeFence.Release();
        }
    }
#pragma warning restore CA1416
}
