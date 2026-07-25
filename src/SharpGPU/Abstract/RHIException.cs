using System;

namespace SharpGPU
{
    public enum ERHIErrorCode : byte
    {
        Unknown,
        InitializationFailed,
        OutOfMemory,
        SubmissionFailed,
        SynchronizationFailed,
        SurfaceLost,
        DeviceLost,
        NativeFailure
    }

    public enum ERHIDeviceState : byte
    {
        Unknown,
        Operational,
        Lost,
        Removed,
        Reset
    }

    public sealed class RHIException : Exception
    {
        public ERHIErrorCode ErrorCode { get; }
        public ERHIBackend Backend { get; }
        public long NativeCode { get; }
        public string NativeMessage { get; }
        public ERHIDeviceState DeviceState { get; }

        public RHIException(
            ERHIErrorCode errorCode,
            ERHIBackend backend,
            long nativeCode,
            string nativeMessage,
            ERHIDeviceState deviceState,
            Exception? innerException = null)
            : base(BuildMessage(errorCode, backend, nativeCode, nativeMessage, deviceState), innerException)
        {
            if (!Enum.IsDefined(errorCode))
            {
                throw new ArgumentOutOfRangeException(nameof(errorCode), errorCode, "Unknown RHI error code.");
            }
            if (!Enum.IsDefined(backend) || backend == ERHIBackend.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown RHI backend.");
            }
            if (!Enum.IsDefined(deviceState))
            {
                throw new ArgumentOutOfRangeException(nameof(deviceState), deviceState, "Unknown RHI device state.");
            }
            if (string.IsNullOrWhiteSpace(nativeMessage))
            {
                throw new ArgumentException("Native error message must not be empty.", nameof(nativeMessage));
            }

            ErrorCode = errorCode;
            Backend = backend;
            NativeCode = nativeCode;
            NativeMessage = nativeMessage;
            DeviceState = deviceState;
        }

        private static string BuildMessage(
            ERHIErrorCode errorCode,
            ERHIBackend backend,
            long nativeCode,
            string nativeMessage,
            ERHIDeviceState deviceState)
        {
            return $"{backend} {errorCode} (native=0x{nativeCode:X}, device={deviceState}): {nativeMessage}";
        }
    }
}
