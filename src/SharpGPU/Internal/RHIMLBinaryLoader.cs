using System;
using System.Buffers.Binary;

namespace SharpGPU
{
    internal static class RHIMLBinaryLoader
    {
        internal static RHIMLBinary Load(ReadOnlyMemory<byte> container)
        {
            ReadOnlySpan<byte> bytes = container.Span;
            if (bytes.Length < 5)
            {
                throw new InvalidOperationException("ML binary container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            return magic switch
            {
                Dx12MlBinaryCodec.Magic => LoadDirectMl(container),
                MetalMlBinaryCodec.Magic => LoadMetalPackage(container),
                _ => throw new InvalidOperationException($"Unknown ML binary magic 0x{magic:X8}."),
            };
        }

        private static RHIMLBinary LoadDirectMl(ReadOnlyMemory<byte> container)
        {
            Dx12MlBinaryCodec.ValidateContainer(container, ERHIMLBinaryFormat.DirectMLProgramV1);
            RHIMLBinaryReflection reflection = Dx12MlBinaryCodec.ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.DirectMLProgramV1,
                container,
                reflection,
                contentHash);
        }

        private static RHIMLBinary LoadMetalPackage(ReadOnlyMemory<byte> container)
        {
            RHIMLBinaryReflection reflection = MetalMlBinaryCodec.ReadReflection(container);
            ulong contentHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Span.Slice(5, 8));
            return new RHIMLBinary(
                ERHIMLBinaryFormat.MetalPackageV1,
                container,
                reflection,
                contentHash);
        }
    }
}
