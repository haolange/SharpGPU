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
                ThrowIfSynchronizationDisposed();
                return m_NativeFence;
            }
        }
        public override ERHIFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return ERHIFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return ERHIFenceStatus.NotReady;
                }

                ulong targetValue = (ulong)Volatile.Read(ref m_LastSignaledValue);
                bool complete = targetValue != 0 && m_NativeFence.CompletedValue >= targetValue;
                if (complete)
                {
                    MarkSignaled();
                }

                return complete ? ERHIFenceStatus.Success : ERHIFenceStatus.NotReady;
            }
        }

        private Vortice.Direct3D12.ID3D12Fence m_NativeFence;
        private AutoResetEvent m_FenceEvent;
        private long m_NextFenceValue;
        private long m_LastSignaledValue;

        public Dx12Fence(Dx12Device device) : base(device)
        {
            Vortice.Direct3D12.ID3D12Fence? fence;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateFence(0, Vortice.Direct3D12.FenceFlags.None, out fence);
            m_NativeFence = Dx12Utility.RequireCreatedObject(
                fence,
                hResult,
                "ID3D12Device.CreateFence(completion fence)");

            m_FenceEvent = new AutoResetEvent(false);
#if DEBUG
            Debug.Assert(m_FenceEvent != null, "SystemEvent is null");
#endif

            m_NextFenceValue = 1;
            m_LastSignaledValue = 0;
        }

        public override void Reset()
        {
            if (IsSignalPending)
            {
                _ = Status;
            }

            if (!BeginReset())
            {
                return;
            }

            CompleteReset();
        }

        public override ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return ERHIFenceStatus.Success;
            }

            ulong targetValue = (ulong)Volatile.Read(ref m_LastSignaledValue);
            if (targetValue == 0)
            {
                throw new InvalidOperationException("The fence has no native signal value.");
            }

            if (m_NativeFence.CompletedValue >= targetValue)
            {
                MarkSignaled();
                return ERHIFenceStatus.Success;
            }

            IntPtr eventPtr = m_FenceEvent.SafeWaitHandle.DangerousGetHandle();
            System.IntPtr eventHandle = new System.IntPtr(eventPtr.ToPointer());
            SharpGen.Runtime.Result setEventResult = m_NativeFence.SetEventOnCompletion(targetValue, eventHandle);
            Dx12Utility.CHECK_HR(setEventResult);
            bool completed;
            if (timeoutNanoseconds == ulong.MaxValue)
            {
                m_FenceEvent.WaitOne();
                completed = true;
            }
            else
            {
                ulong timeoutMilliseconds = timeoutNanoseconds / 1_000_000UL;
                if ((timeoutNanoseconds % 1_000_000UL) != 0)
                {
                    ++timeoutMilliseconds;
                }

                completed = m_FenceEvent.WaitOne((int)Math.Min(timeoutMilliseconds, int.MaxValue));
            }

            if (!completed && m_NativeFence.CompletedValue < targetValue)
            {
                return ERHIFenceStatus.NotReady;
            }

            MarkSignaled();
            return ERHIFenceStatus.Success;
        }

        internal ulong PrepareSignalValue()
        {
            if (!IsSignalPending)
            {
                throw new InvalidOperationException("The fence signal must be reserved before obtaining a native value.");
            }

            long signalValue = Interlocked.Increment(ref m_NextFenceValue) - 1;
            Volatile.Write(ref m_LastSignaledValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            m_FenceEvent.Dispose();
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

        public Dx12Semaphore(Dx12Device device) : base(device)
        {
            Vortice.Direct3D12.ID3D12Fence? fence;
            SharpGen.Runtime.Result hResult = device.NativeDevice.CreateFence(0, Vortice.Direct3D12.FenceFlags.None, out fence);
            m_NativeFence = Dx12Utility.RequireCreatedObject(
                fence,
                hResult,
                "ID3D12Device.CreateFence(binary semaphore)");
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

    internal sealed class Dx12ExternalFence64 : RHIExternalFence64
    {
        internal Vortice.Direct3D12.ID3D12Fence NativeFence => m_NativeFence;
        internal RHIAdapterIdentity Adapter => m_Adapter;

        private readonly Dx12Device m_Device;
        private readonly Vortice.Direct3D12.ID3D12Fence m_NativeFence;
        private readonly RHIAdapterIdentity m_Adapter;
        private IntPtr m_OwnedImportHandle;

        internal Dx12ExternalFence64(
            Dx12Device device,
            Vortice.Direct3D12.ID3D12Fence nativeFence,
            ERHIExternalFence64Direction direction,
            in RHIAdapterIdentity adapter,
            IntPtr ownedImportHandle = default)
            : base(device, direction)
        {
            m_Device = device;
            m_NativeFence = nativeFence ?? throw new ArgumentNullException(nameof(nativeFence));
            m_Adapter = adapter;
            m_OwnedImportHandle = ownedImportHandle;
        }

        public override ulong GetCompletedValue()
        {
            ThrowIfExternalDisposed();
            return m_NativeFence.CompletedValue;
        }

        public override void Signal(ulong value)
        {
            ThrowIfExternalDisposed();
            m_NativeFence.Signal(value);
        }

        protected override void Release()
        {
            m_NativeFence.Release();
            if (m_OwnedImportHandle != IntPtr.Zero)
            {
                RHIWin32NtHandle.Close(m_OwnedImportHandle);
                m_OwnedImportHandle = IntPtr.Zero;
            }
        }
    }
#pragma warning restore CA1416
}
