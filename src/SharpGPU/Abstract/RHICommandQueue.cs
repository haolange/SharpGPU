using SharpGPU.Core;
using System;

namespace SharpGPU
{
    public readonly struct RHIQueueSemaphoreWait
    {
        public RHISemaphore Semaphore
        {
            get;
        }

        public ERHIStageMask StageMask
        {
            get;
        }

        public RHIQueueSemaphoreWait(RHISemaphore semaphore, ERHIStageMask stageMask)
        {
            Semaphore = semaphore ?? throw new ArgumentNullException(nameof(semaphore));
            StageMask = stageMask;
        }
    }

    public readonly struct RHIQueueSubmitDescriptor
    {
        public ReadOnlyMemory<RHICommandBuffer> CommandBuffers
        {
            get;
        }

        public ReadOnlyMemory<RHIQueueSemaphoreWait> WaitSemaphores
        {
            get;
        }

        public ReadOnlyMemory<RHISemaphore> SignalSemaphores
        {
            get;
        }

        public RHIFence? CompletionFence
        {
            get;
        }

        public RHIQueueSubmitDescriptor(
            ReadOnlyMemory<RHICommandBuffer> commandBuffers = default,
            ReadOnlyMemory<RHIQueueSemaphoreWait> waitSemaphores = default,
            ReadOnlyMemory<RHISemaphore> signalSemaphores = default,
            RHIFence? completionFence = null)
        {
            CommandBuffers = commandBuffers;
            WaitSemaphores = waitSemaphores;
            SignalSemaphores = signalSemaphores;
            CompletionFence = completionFence;
        }
    }

    public abstract class RHICommandQueue : Disposal
    {
        private const ERHIStageMask KnownWaitStageMask =
            ERHIStageMask.Transfer |
            ERHIStageMask.Indirect |
            ERHIStageMask.IndexInput |
            ERHIStageMask.VertexInput |
            ERHIStageMask.Vertex |
            ERHIStageMask.Fragment |
            ERHIStageMask.Compute |
            ERHIStageMask.Task |
            ERHIStageMask.Mesh |
            ERHIStageMask.RayTracing |
            ERHIStageMask.AccelStructBuild |
            ERHIStageMask.AccelStructCopy |
            ERHIStageMask.MachineLearning;

        public ERHIPipelineType PipelineType
        {
            get
            {
                return m_PipelineType;
            }
        }
        public abstract ulong Frequency
        {
            get;
        }

        protected ERHIPipelineType m_PipelineType;
        protected abstract object DeviceIdentity
        {
            get;
        }

        public abstract RHICommandBuffer CreateCommandBuffer();
        public abstract void Submit(in RHIQueueSubmitDescriptor descriptor);

        public virtual void BindSparse(in RHISparseBindDescriptor descriptor)
        {
            ThrowIfDisposed();
            ThrowIfOwnerDeviceUnavailable();
            throw new NotSupportedException(
                $"{GetType().Name} does not expose queue-ordered sparse texture binding.");
        }

        protected void ValidateSubmit(in RHIQueueSubmitDescriptor descriptor)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            ThrowIfOwnerDeviceUnavailable();

            ReadOnlySpan<RHICommandBuffer> commandBuffers = descriptor.CommandBuffers.Span;
            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            if (commandBuffers.Length == 0 &&
                waitSemaphores.Length == 0 &&
                signalSemaphores.Length == 0 &&
                descriptor.CompletionFence == null)
            {
                throw new ArgumentException("A queue submission must contain work or a synchronization operation.", nameof(descriptor));
            }

            for (int i = 0; i < commandBuffers.Length; ++i)
            {
                RHICommandBuffer commandBuffer = commandBuffers[i] ??
                    throw new ArgumentException($"Command buffer at index {i} is null.", nameof(descriptor));
                if (commandBuffer.IsDisposed)
                {
                    throw new ObjectDisposedException(commandBuffer.GetType().FullName);
                }

                if (!ReferenceEquals(commandBuffer.CommandQueue, this))
                {
                    throw new ArgumentException(
                        $"Command buffer at index {i} was not created by this queue.",
                        nameof(descriptor));
                }

                commandBuffer.ValidateCanSubmit();
                for (int priorIndex = 0; priorIndex < i; ++priorIndex)
                {
                    if (ReferenceEquals(commandBuffers[priorIndex], commandBuffer))
                    {
                        throw new ArgumentException(
                            $"Command buffer at index {i} is duplicated in the submission.",
                            nameof(descriptor));
                    }
                }
            }

            for (int i = 0; i < waitSemaphores.Length; ++i)
            {
                ref readonly RHIQueueSemaphoreWait wait = ref waitSemaphores[i];
                if (wait.Semaphore == null)
                {
                    throw new ArgumentException($"Wait semaphore at index {i} is null.", nameof(descriptor));
                }

                ValidateSemaphore(wait.Semaphore, $"wait semaphore at index {i}", in descriptor);
                if (wait.StageMask == ERHIStageMask.None)
                {
                    throw new ArgumentException(
                        $"Wait semaphore at index {i} must specify a non-empty stage mask.",
                        nameof(descriptor));
                }

                if (wait.StageMask != ERHIStageMask.All &&
                    (((ulong)wait.StageMask & ~(ulong)KnownWaitStageMask) != 0))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        $"Wait semaphore at index {i} contains an unknown stage-mask bit.");
                }

                for (int priorIndex = 0; priorIndex < i; ++priorIndex)
                {
                    if (ReferenceEquals(waitSemaphores[priorIndex].Semaphore, wait.Semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at wait index {i} is duplicated in the submission.",
                            nameof(descriptor));
                    }
                }
            }

            for (int i = 0; i < signalSemaphores.Length; ++i)
            {
                RHISemaphore semaphore = signalSemaphores[i] ??
                    throw new ArgumentException($"Signal semaphore at index {i} is null.", nameof(descriptor));
                ValidateSemaphore(semaphore, $"signal semaphore at index {i}", in descriptor);
                for (int waitIndex = 0; waitIndex < waitSemaphores.Length; ++waitIndex)
                {
                    if (ReferenceEquals(waitSemaphores[waitIndex].Semaphore, semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at signal index {i} is also present in the wait list.",
                            nameof(descriptor));
                    }
                }

                for (int priorIndex = 0; priorIndex < i; ++priorIndex)
                {
                    if (ReferenceEquals(signalSemaphores[priorIndex], semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at signal index {i} is duplicated in the submission.",
                            nameof(descriptor));
                    }
                }
            }

            if (descriptor.CompletionFence != null)
            {
                RHIFence fence = descriptor.CompletionFence;
                if (fence.IsDisposed)
                {
                    throw new ObjectDisposedException(fence.GetType().FullName);
                }

                if (!ReferenceEquals(fence.OwnerDevice, DeviceIdentity))
                {
                    throw new ArgumentException(
                        "The completion fence was created by a different device.",
                        nameof(descriptor));
                }
            }
        }

        protected void ReserveSubmit(in RHIQueueSubmitDescriptor descriptor)
        {
            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            int reservedWaits = 0;
            int reservedSignals = 0;
            bool reservedFence = false;

            try
            {
                for (; reservedWaits < waitSemaphores.Length; ++reservedWaits)
                {
                    waitSemaphores[reservedWaits].Semaphore.ReserveWait();
                }

                for (; reservedSignals < signalSemaphores.Length; ++reservedSignals)
                {
                    signalSemaphores[reservedSignals].ReserveSignal();
                }

                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    reservedFence = true;
                }
            }
            catch
            {
                if (reservedFence)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }

                for (int i = reservedSignals - 1; i >= 0; --i)
                {
                    signalSemaphores[i].RollbackSignal();
                }

                for (int i = reservedWaits - 1; i >= 0; --i)
                {
                    waitSemaphores[i].Semaphore.RollbackWait();
                }

                throw;
            }
        }

        protected static void CommitSubmit(in RHIQueueSubmitDescriptor descriptor)
        {
            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waitSemaphores.Length; ++i)
            {
                waitSemaphores[i].Semaphore.CommitWait();
            }

            for (int i = 0; i < signalSemaphores.Length; ++i)
            {
                signalSemaphores[i].CommitSignal();
            }

            ReadOnlySpan<RHICommandBuffer> commandBuffers = descriptor.CommandBuffers.Span;
            for (int i = 0; i < commandBuffers.Length; ++i)
            {
                commandBuffers[i].MarkSubmitted();
            }
        }

        protected static void RollbackSubmit(in RHIQueueSubmitDescriptor descriptor)
        {
            if (descriptor.CompletionFence != null)
            {
                descriptor.CompletionFence.RollbackSignal();
            }

            ReadOnlySpan<RHISemaphore> signalSemaphores = descriptor.SignalSemaphores.Span;
            for (int i = signalSemaphores.Length - 1; i >= 0; --i)
            {
                signalSemaphores[i].RollbackSignal();
            }

            ReadOnlySpan<RHIQueueSemaphoreWait> waitSemaphores = descriptor.WaitSemaphores.Span;
            for (int i = waitSemaphores.Length - 1; i >= 0; --i)
            {
                waitSemaphores[i].Semaphore.RollbackWait();
            }
        }

        protected void ValidateSparseBind(in RHISparseBindDescriptor descriptor)
        {
            ThrowIfDisposed();
            ThrowIfOwnerDeviceUnavailable();
            if (descriptor.TileBindings.IsEmpty &&
                descriptor.MipTailBindings.IsEmpty)
            {
                throw new ArgumentException(
                    "A sparse binding operation must contain at least one tile or mip-tail binding.",
                    nameof(descriptor));
            }

            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waits.Length; ++i)
            {
                RHISemaphore semaphore = waits[i] ??
                    throw new ArgumentException($"Wait semaphore at index {i} is null.", nameof(descriptor));
                ValidateSparseSemaphore(semaphore, $"wait semaphore at index {i}", in descriptor);
                for (int prior = 0; prior < i; ++prior)
                {
                    if (ReferenceEquals(waits[prior], semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at wait index {i} is duplicated.",
                            nameof(descriptor));
                    }
                }
            }

            for (int i = 0; i < signals.Length; ++i)
            {
                RHISemaphore semaphore = signals[i] ??
                    throw new ArgumentException($"Signal semaphore at index {i} is null.", nameof(descriptor));
                ValidateSparseSemaphore(semaphore, $"signal semaphore at index {i}", in descriptor);
                for (int waitIndex = 0; waitIndex < waits.Length; ++waitIndex)
                {
                    if (ReferenceEquals(waits[waitIndex], semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at signal index {i} is also present in the wait list.",
                            nameof(descriptor));
                    }
                }
                for (int prior = 0; prior < i; ++prior)
                {
                    if (ReferenceEquals(signals[prior], semaphore))
                    {
                        throw new ArgumentException(
                            $"Semaphore at signal index {i} is duplicated.",
                            nameof(descriptor));
                    }
                }
            }

            if (descriptor.CompletionFence != null)
            {
                RHIFence fence = descriptor.CompletionFence;
                if (fence.IsDisposed)
                {
                    throw new ObjectDisposedException(fence.GetType().FullName);
                }
                if (!ReferenceEquals(fence.OwnerDevice, DeviceIdentity))
                {
                    throw new ArgumentException(
                        "The sparse completion fence was created by a different device.",
                        nameof(descriptor));
                }
            }
        }

        protected static void ReserveSparseBind(in RHISparseBindDescriptor descriptor)
        {
            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            int reservedWaits = 0;
            int reservedSignals = 0;
            bool reservedFence = false;
            try
            {
                for (; reservedWaits < waits.Length; ++reservedWaits)
                {
                    waits[reservedWaits].ReserveWait();
                }
                for (; reservedSignals < signals.Length; ++reservedSignals)
                {
                    signals[reservedSignals].ReserveSignal();
                }
                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    reservedFence = true;
                }
            }
            catch
            {
                if (reservedFence)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                for (int i = reservedSignals - 1; i >= 0; --i)
                {
                    signals[i].RollbackSignal();
                }
                for (int i = reservedWaits - 1; i >= 0; --i)
                {
                    waits[i].RollbackWait();
                }
                throw;
            }
        }

        protected static void CommitSparseBind(in RHISparseBindDescriptor descriptor)
        {
            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            for (int i = 0; i < waits.Length; ++i)
            {
                waits[i].CommitWait();
            }
            for (int i = 0; i < signals.Length; ++i)
            {
                signals[i].CommitSignal();
            }
        }

        protected static void RollbackSparseBind(in RHISparseBindDescriptor descriptor)
        {
            if (descriptor.CompletionFence != null)
            {
                descriptor.CompletionFence.RollbackSignal();
            }
            ReadOnlySpan<RHISemaphore> signals = descriptor.SignalSemaphores.Span;
            for (int i = signals.Length - 1; i >= 0; --i)
            {
                signals[i].RollbackSignal();
            }
            ReadOnlySpan<RHISemaphore> waits = descriptor.WaitSemaphores.Span;
            for (int i = waits.Length - 1; i >= 0; --i)
            {
                waits[i].RollbackWait();
            }
        }

        private void ValidateSparseSemaphore(
            RHISemaphore semaphore,
            string argumentDescription,
            in RHISparseBindDescriptor descriptor)
        {
            if (semaphore.IsDisposed)
            {
                throw new ObjectDisposedException(semaphore.GetType().FullName);
            }
            if (!ReferenceEquals(semaphore.OwnerDevice, DeviceIdentity))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} was created by a different device.",
                    nameof(descriptor));
            }
        }

        private void ValidateSemaphore(
            RHISemaphore semaphore,
            string argumentDescription,
            in RHIQueueSubmitDescriptor descriptor)
        {
            if (semaphore.IsDisposed)
            {
                throw new ObjectDisposedException(semaphore.GetType().FullName);
            }

            if (!ReferenceEquals(semaphore.OwnerDevice, DeviceIdentity))
            {
                throw new ArgumentException(
                    $"The {argumentDescription} was created by a different device.",
                    nameof(descriptor));
            }
        }

        private void ThrowIfOwnerDeviceUnavailable()
        {
            if (DeviceIdentity is RHIDevice device)
            {
                device.ThrowIfDeviceUnavailable();
            }
        }
    }
}