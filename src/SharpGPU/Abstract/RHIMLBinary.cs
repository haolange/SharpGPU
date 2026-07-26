using System;

namespace SharpGPU
{
    public enum ERHIMLBinaryFormat : byte
    {
        Unknown = 0,
        DirectMLProgramV1 = 1,
        MetalPackageV1 = 2,
    }

    public struct RHIMLBinaryReflection
    {
        public string EntryName;
        public RHIMLTensorBindingInfo[] Bindings;
        public ulong IntermediateHeapSizeHint;
    }

    /// <summary>
    /// Opaque ML binary artifact consumed by <see cref="RHIDevice.CreateMLPipeline"/>.
    /// Public RHI ML surface is Binary → Pipeline → BindingSet → Encoder only (ADR-0052).
    /// </summary>
    public sealed class RHIMLBinary
    {
        public ERHIMLBinaryFormat Format { get; }
        public ReadOnlyMemory<byte> Payload { get; }
        public ulong ContentHash { get; }
        public RHIMLBinaryReflection Reflection { get; }

        public RHIMLBinary(
            ERHIMLBinaryFormat format,
            ReadOnlyMemory<byte> payload,
            in RHIMLBinaryReflection reflection,
            ulong contentHash)
        {
            if (format == ERHIMLBinaryFormat.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(format), "ML binary format must be specified.");
            }

            if (payload.IsEmpty)
            {
                throw new ArgumentException("ML binary payload must not be empty.", nameof(payload));
            }

            Format = format;
            Payload = payload;
            Reflection = reflection;
            ContentHash = contentHash;
        }
    }
}
