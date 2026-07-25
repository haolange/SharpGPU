using System;
using System.Threading;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum EFenceStatus : byte
    {
        Success = 0,
        NotReady = 1,
        Undefined
    };

    internal enum ERHIBinarySynchronizationState : byte
    {
        Ready = 0,
        Pending = 1,
        Signaled = 2,
        Resetting = 3
    }

    public abstract class RHIFence : Disposal
    {
        internal object OwnerDevice
        {
            get;
        }

        private int m_State;

        protected RHIFence(object ownerDevice)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            m_State = (int)ERHIBinarySynchronizationState.Ready;
        }

        public abstract EFenceStatus Status
        {
            get;
        }

        public abstract void Reset();
        public abstract EFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue);

        internal void ReserveSignal()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Pending,
                    (int)ERHIBinarySynchronizationState.Ready) !=
                (int)ERHIBinarySynchronizationState.Ready)
            {
                throw new InvalidOperationException(
                    "The fence must be Ready before it can be used as a completion fence.");
            }
        }

        internal void RollbackSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Ready,
                    (int)ERHIBinarySynchronizationState.Pending) !=
                (int)ERHIBinarySynchronizationState.Pending)
            {
                throw new InvalidOperationException("The fence signal reservation is not pending.");
            }
        }

        protected bool BeginReset()
        {
            ThrowIfSynchronizationDisposed();
            while (true)
            {
                ERHIBinarySynchronizationState state =
                    (ERHIBinarySynchronizationState)Volatile.Read(ref m_State);
                if (state == ERHIBinarySynchronizationState.Ready)
                {
                    return false;
                }

                if (state != ERHIBinarySynchronizationState.Signaled)
                {
                    throw new InvalidOperationException(
                        "A fence cannot be reset before its pending signal has completed.");
                }

                if (Interlocked.CompareExchange(
                        ref m_State,
                        (int)ERHIBinarySynchronizationState.Resetting,
                        (int)ERHIBinarySynchronizationState.Signaled) ==
                    (int)ERHIBinarySynchronizationState.Signaled)
                {
                    return true;
                }
            }
        }

        protected void CompleteReset()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Ready,
                    (int)ERHIBinarySynchronizationState.Resetting) !=
                (int)ERHIBinarySynchronizationState.Resetting)
            {
                throw new InvalidOperationException("The fence reset operation is not active.");
            }
        }

        protected void RollbackReset()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Signaled,
                    (int)ERHIBinarySynchronizationState.Resetting) !=
                (int)ERHIBinarySynchronizationState.Resetting)
            {
                throw new InvalidOperationException("The fence reset operation is not active.");
            }
        }

        protected bool IsSignalPending
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                return (ERHIBinarySynchronizationState)Volatile.Read(ref m_State) ==
                    ERHIBinarySynchronizationState.Pending;
            }
        }

        protected bool IsSignalKnownComplete
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                return (ERHIBinarySynchronizationState)Volatile.Read(ref m_State) ==
                    ERHIBinarySynchronizationState.Signaled;
            }
        }

        protected void EnsureWaitable()
        {
            ThrowIfSynchronizationDisposed();
            ERHIBinarySynchronizationState state =
                (ERHIBinarySynchronizationState)Volatile.Read(ref m_State);
            if (state == ERHIBinarySynchronizationState.Ready)
            {
                throw new InvalidOperationException("A fence cannot be waited before it has been submitted.");
            }

            if (state == ERHIBinarySynchronizationState.Resetting)
            {
                throw new InvalidOperationException("A fence cannot be waited while it is being reset.");
            }
        }

        protected void MarkSignaled()
        {
            int prior = Interlocked.CompareExchange(
                ref m_State,
                (int)ERHIBinarySynchronizationState.Signaled,
                (int)ERHIBinarySynchronizationState.Pending);
            if (prior != (int)ERHIBinarySynchronizationState.Pending &&
                prior != (int)ERHIBinarySynchronizationState.Signaled)
            {
                throw new InvalidOperationException("The fence does not have a pending signal.");
            }
        }

        protected void ThrowIfSynchronizationDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (OwnerDevice is RHIDevice device)
            {
                device.ThrowIfDeviceUnavailable();
            }
        }
    }

    public abstract class RHISemaphore : Disposal
    {
        internal object OwnerDevice
        {
            get;
        }

        private int m_State;

        protected RHISemaphore(object ownerDevice)
        {
            OwnerDevice = ownerDevice ?? throw new ArgumentNullException(nameof(ownerDevice));
            m_State = (int)ERHIBinarySynchronizationState.Ready;
        }

        internal void ReserveSignal()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Pending,
                    (int)ERHIBinarySynchronizationState.Ready) !=
                (int)ERHIBinarySynchronizationState.Ready)
            {
                throw new InvalidOperationException(
                    "A binary semaphore cannot be signaled again before its prior signal has been consumed.");
            }
        }

        internal void CommitSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Signaled,
                    (int)ERHIBinarySynchronizationState.Pending) !=
                (int)ERHIBinarySynchronizationState.Pending)
            {
                throw new InvalidOperationException("The semaphore signal reservation is not pending.");
            }
        }

        internal void RollbackSignal()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Ready,
                    (int)ERHIBinarySynchronizationState.Pending) !=
                (int)ERHIBinarySynchronizationState.Pending)
            {
                throw new InvalidOperationException("The semaphore signal reservation is not pending.");
            }
        }

        internal void ReserveWait()
        {
            ThrowIfSynchronizationDisposed();
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Pending,
                    (int)ERHIBinarySynchronizationState.Signaled) !=
                (int)ERHIBinarySynchronizationState.Signaled)
            {
                throw new InvalidOperationException(
                    "A binary semaphore cannot be waited before a signal has been submitted or after it has been consumed.");
            }
        }

        internal void CommitWait()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Ready,
                    (int)ERHIBinarySynchronizationState.Pending) !=
                (int)ERHIBinarySynchronizationState.Pending)
            {
                throw new InvalidOperationException("The semaphore wait reservation is not pending.");
            }
        }

        internal void RollbackWait()
        {
            if (Interlocked.CompareExchange(
                    ref m_State,
                    (int)ERHIBinarySynchronizationState.Signaled,
                    (int)ERHIBinarySynchronizationState.Pending) !=
                (int)ERHIBinarySynchronizationState.Pending)
            {
                throw new InvalidOperationException("The semaphore wait reservation is not pending.");
            }
        }

        private void ThrowIfSynchronizationDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
            if (OwnerDevice is RHIDevice device)
            {
                device.ThrowIfDeviceUnavailable();
            }
        }
    }
}
