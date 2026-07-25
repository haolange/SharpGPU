using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public struct RHIStorageFileHandle
    {
        public IntPtr NativeHandle;
    }

    public struct RHIStorageBufferRequest
    {
        public RHIStorageFileHandle FileHandle;
        public ulong FileOffset;
        public ulong FileSize;
        public RHIBuffer DestinationBuffer;
        public ulong DestinationOffset;
    }

    public struct RHIStorageTextureRequest
    {
        public RHIStorageFileHandle FileHandle;
        public ulong FileOffset;
        public ulong FileSize;
        public RHITexture DestinationTexture;
        public uint MipLevel;
        public uint ArraySlice;
    }

    public abstract class RHIStorageQueue : Disposal
    {
        public abstract RHIStorageFileHandle OpenFile(string absPath);
        public abstract void CloseFile(in RHIStorageFileHandle fileHandle);
        public abstract ulong QueryFileSize(in RHIStorageFileHandle fileHandle);
        public abstract void RequestBuffer(in RHIStorageBufferRequest request);
        public abstract void RequestTexture(in RHIStorageTextureRequest request);
        public abstract void Submit(RHIFence signalFence);
        public abstract void ThrowIfSubmissionFailed();
    }
}
