using System;

namespace SharpGPU.Collections.LowLevel
{
    public static unsafe class MemoryUtility
    {
        public static void MemCpy(void* src, void* dsc, in long size)
        {
            Buffer.MemoryCopy(src, dsc, size, size);
        }

        public static void CopyTo<T>(this IntPtr src, Span<T> dsc) where T : struct
        {
            new Span<T>(src.ToPointer(), dsc.Length).CopyTo(dsc);
        }
    }
}
