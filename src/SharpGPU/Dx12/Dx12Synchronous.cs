using System;
using System.Threading;
using System.Diagnostics;

namespace SharpGPU
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
                ulong targetValue = (ulong)Volatile.Read(ref m_LastSignaledValue);
                if (targetValue == 0)
                {
                    return EFenceStatus.NotReady;
                }

                return m_NativeFence.CompletedValue >= targetValue ? EFenceStatus.Success : EFenceStatus.NotReady;
            }
        }

        private Vortice.Direct3D12.ID3D12Fence m_NativeFence;
        private AutoResetEvent m_FenceEvent;
        private long m_NextFenceValue;
        private long m_PendingSignalValue;
        private long m_LastSignaledValue;

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

            m_NextFenceValue = 1;
            m_PendingSignalValue = 0;
            m_LastSignaledValue = 0;
        }

        public override void Reset()
        {
            long value = Interlocked.Increment(ref m_NextFenceValue) - 1;
            Volatile.Write(ref m_PendingSignalValue, value);
        }

        public override void Wait()
        {
            ulong targetValue = (ulong)Volatile.Read(ref m_LastSignaledValue);
            if (targetValue == 0)
            {
                return;
            }

            if (m_NativeFence.CompletedValue >= targetValue)
            {
                return;
            }

            IntPtr eventPtr = m_FenceEvent.SafeWaitHandle.DangerousGetHandle();
            System.IntPtr eventHandle = new System.IntPtr(eventPtr.ToPointer());
            m_NativeFence.SetEventOnCompletion(targetValue, eventHandle);
            m_FenceEvent.WaitOne();
        }

        internal ulong ConsumeSignalValue()
        {
            long pendingValue = Volatile.Read(ref m_PendingSignalValue);
            long lastSignaledValue = Volatile.Read(ref m_LastSignaledValue);
            if (pendingValue <= lastSignaledValue)
            {
                pendingValue = Interlocked.Increment(ref m_NextFenceValue) - 1;
                Volatile.Write(ref m_PendingSignalValue, pendingValue);
            }

            Volatile.Write(ref m_LastSignaledValue, pendingValue);
            return (ulong)pendingValue;
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
        private long m_NextSemaphoreValue;
        private long m_LastSignaledValue;

        public Dx12Semaphore(Dx12Device device)
        {
            Vortice.Direct3D12.ID3D12Fence fence;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateFence(0, Vortice.Direct3D12.FenceFlags.None, out fence);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeFence = fence;
            m_NextSemaphoreValue = 1;
            m_LastSignaledValue = 0;
        }

        internal ulong LastSignaledValue
        {
            get
            {
                return (ulong)Volatile.Read(ref m_LastSignaledValue);
            }
        }

        internal ulong PrepareSignalValue()
        {
            long signalValue = Interlocked.Increment(ref m_NextSemaphoreValue) - 1;
            Volatile.Write(ref m_LastSignaledValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            m_NativeFence.Release();
        }
    }
#pragma warning restore CA1416
}
