using System;

namespace SharpGPU
{
    internal static class RHIMLBinaryHash
    {
        internal static ulong ComputeContentHash(ReadOnlySpan<byte> payload)
        {
            const ulong offsetBasis = 0xCBF29CE484222325UL;
            const ulong prime = 0x100000001B3UL;
            ulong hash = offsetBasis;
            for (int i = 0; i < payload.Length; ++i)
            {
                hash ^= payload[i];
                hash *= prime;
            }

            return hash;
        }
    }
}
