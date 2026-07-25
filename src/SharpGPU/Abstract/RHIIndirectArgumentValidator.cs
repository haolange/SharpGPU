using System;

namespace SharpGPU
{
    internal static class RHIIndirectArgumentValidator
    {
        internal const uint DispatchArgumentStride = 12;
        internal const uint DrawArgumentStride = 16;
        internal const uint DrawIndexedArgumentStride = 20;

        internal static void ValidateDispatch(
            RHIBuffer? buffer,
            uint offset,
            string parameterName,
            string operation)
        {
            Validate(
                buffer,
                offset,
                1,
                DispatchArgumentStride,
                parameterName,
                operation);
        }

        internal static void ValidateDraw(
            RHIBuffer? buffer,
            uint offset,
            uint drawCount,
            bool indexed,
            string parameterName,
            string operation)
        {
            Validate(
                buffer,
                offset,
                drawCount,
                indexed ? DrawIndexedArgumentStride : DrawArgumentStride,
                parameterName,
                operation);
        }

        private static void Validate(
            RHIBuffer? buffer,
            uint offset,
            uint commandCount,
            uint commandStride,
            string parameterName,
            string operation)
        {
            ArgumentNullException.ThrowIfNull(buffer, parameterName);
            if (buffer.IsDisposed)
            {
                throw new ObjectDisposedException(buffer.GetType().FullName);
            }

            RHIBufferDescriptor descriptor = buffer.Descriptor;
            if ((descriptor.UsageFlag & ERHIBufferUsage.IndirectBuffer) == 0)
            {
                throw new ArgumentException(
                    $"{operation} requires a buffer created with IndirectBuffer usage.",
                    parameterName);
            }
            if ((offset & 3u) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    offset,
                    $"{operation} argument offset must be four-byte aligned.");
            }

            ulong requiredEnd = checked(
                (ulong)offset + (ulong)commandCount * commandStride);
            ulong byteSize = checked((ulong)descriptor.ByteSize);
            if (requiredEnd > byteSize)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    offset,
                    $"{operation} requires byte range [{offset}, {requiredEnd}), " +
                    $"but the indirect buffer contains {byteSize} bytes.");
            }
        }
    }
}
