using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    [Flags]
    public enum ERHIStorageCompressionFormat : byte
    {
        None = 0,
        GDeflate = 1 << 0
    }

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
        public ERHIStorageCompressionFormat CompressionFormat;
        public uint UncompressedSize;
        public ulong CancellationTag;
    }

    public struct RHIStorageTextureRequest
    {
        public RHIStorageFileHandle FileHandle;
        public ulong FileOffset;
        public ulong FileSize;
        public RHITexture DestinationTexture;
        public uint MipLevel;
        public uint ArraySlice;
        public ERHIStorageCompressionFormat CompressionFormat;
        public uint UncompressedSize;
        public ulong CancellationTag;
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
        public abstract void CancelRequestsWithTag(ulong mask, ulong value);
        public abstract void CancelPending();
    }
}
