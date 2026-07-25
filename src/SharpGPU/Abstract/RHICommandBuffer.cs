using System;
using SharpGPU.Core;

namespace SharpGPU
{
    internal enum ERHICommandBufferState : byte
    {
        Initial = 0,
        Recording = 1,
        Executable = 2,
        Submitted = 3,
        InvalidRecording = 4
    }

    internal enum ERHICommandEncoderKind : byte
    {
        None = 0,
        Transfer = 1,
        Compute = 2,
        RayTracing = 3,
        Raster = 4,
        MachineLearning = 5,
        WorkGraph = 6
    }

    public abstract class RHICommandBuffer : Disposal
    {
        private ERHICommandBufferState m_State;
        private ERHICommandEncoderKind m_ActiveEncoder;

        public RHICommandQueue CommandQueue =>
            m_CommandQueue ?? throw new InvalidOperationException("Command buffer is not associated with a command queue.");

        protected RHICommandQueue? m_CommandQueue;

        private protected void ValidateCanBegin()
        {
            ThrowIfDisposed();
            if (m_State == ERHICommandBufferState.Recording)
            {
                throw new InvalidOperationException(
                    "The command buffer is already recording.");
            }

            if (m_ActiveEncoder != ERHICommandEncoderKind.None)
            {
                throw new InvalidOperationException(
                    "The command buffer has an active encoder.");
            }
        }

        private protected void MarkBeginSucceeded()
        {
            m_State = ERHICommandBufferState.Recording;
            m_ActiveEncoder = ERHICommandEncoderKind.None;
        }

        private protected void MarkRecordingInvalid()
        {
            ThrowIfDisposed();
            m_State = ERHICommandBufferState.InvalidRecording;
            m_ActiveEncoder = ERHICommandEncoderKind.None;
        }

        private protected void ValidateCanBeginEncoder(ERHICommandEncoderKind encoderKind)
        {
            ThrowIfDisposed();
            if (encoderKind == ERHICommandEncoderKind.None)
            {
                throw new ArgumentOutOfRangeException(nameof(encoderKind));
            }

            if (m_State != ERHICommandBufferState.Recording)
            {
                throw new InvalidOperationException(
                    "Begin must be called before beginning a command encoder.");
            }

            if (m_ActiveEncoder != ERHICommandEncoderKind.None)
            {
                throw new InvalidOperationException(
                    $"Cannot begin {encoderKind} while {m_ActiveEncoder} is active.");
            }
        }

        private protected void MarkEncoderBeginSucceeded(ERHICommandEncoderKind encoderKind)
        {
            m_ActiveEncoder = encoderKind;
        }

        private protected void ValidateCanEndEncoder(ERHICommandEncoderKind encoderKind)
        {
            ThrowIfDisposed();
            if (m_State != ERHICommandBufferState.Recording)
            {
                throw new InvalidOperationException(
                    "The command buffer is not recording.");
            }

            if (m_ActiveEncoder != encoderKind)
            {
                throw new InvalidOperationException(
                    $"Cannot end {encoderKind} while {m_ActiveEncoder} is active.");
            }
        }

        private protected void MarkEncoderEndSucceeded()
        {
            m_ActiveEncoder = ERHICommandEncoderKind.None;
        }

        internal void ValidateEncoderEndFromEncoder(ERHICommandEncoderKind encoderKind)
        {
            ValidateCanEndEncoder(encoderKind);
        }

        internal void MarkEncoderEndFromEncoder()
        {
            MarkEncoderEndSucceeded();
        }

        private protected void ValidateCanEnd()
        {
            ThrowIfDisposed();
            if (m_State != ERHICommandBufferState.Recording)
            {
                throw new InvalidOperationException(
                    "The command buffer is not recording.");
            }

            if (m_ActiveEncoder != ERHICommandEncoderKind.None)
            {
                throw new InvalidOperationException(
                    $"The active {m_ActiveEncoder} encoder must be ended before ending the command buffer.");
            }
        }

        private protected void MarkEndSucceeded()
        {
            m_State = ERHICommandBufferState.Executable;
        }

        internal void ValidateCanSubmit()
        {
            ThrowIfDisposed();
            if (m_State != ERHICommandBufferState.Executable)
            {
                throw new InvalidOperationException(
                    "The command buffer must be ended before submission.");
            }
        }

        internal void MarkSubmitted()
        {
            ThrowIfDisposed();
            if (m_State != ERHICommandBufferState.Executable)
            {
                throw new InvalidOperationException(
                    "Only an executable command buffer can transition to submitted.");
            }

            m_State = ERHICommandBufferState.Submitted;
            OnSubmitted();
        }

        private protected virtual void OnSubmitted()
        {
        }

        public abstract void Begin(string name);
        public abstract RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor);
        public abstract void EndTransferPass();
        public abstract RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor);
        public abstract void EndComputePass();
        public abstract RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor);
        public abstract void EndRaytracingPass();
        public abstract RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor);
        public abstract void EndRasterPass();
        public abstract RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor);
        public abstract void EndMLPass();
        public abstract RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor);
        public abstract void EndWorkGraphPass();
        public abstract void End();
        public abstract RHITransferEncoder GetTransferEncoder();
        public abstract RHIComputeEncoder GetComputeEncoder();
        public abstract RHIRaytracingEncoder GetRaytracingEncoder();
        public abstract RHIRasterEncoder GetRasterEncoder();
        public abstract RHIMLEncoder GetMLEncoder();
        public abstract RHIWorkGraphEncoder GetWorkGraphEncoder();
    }

}
