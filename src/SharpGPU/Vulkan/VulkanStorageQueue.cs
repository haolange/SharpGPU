using System;

namespace SharpGPU
{
    internal sealed class VulkanStorageQueue : RHIStorageQueue
    {
        internal VulkanDevice Device { get; }

        public VulkanStorageQueue(VulkanDevice device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public override RHIStorageFileHandle OpenFile(string absPath) =>
            throw CreateNotSupported(nameof(OpenFile));

        public override void CloseFile(in RHIStorageFileHandle fileHandle) =>
            throw CreateNotSupported(nameof(CloseFile));

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle) =>
            throw CreateNotSupported(nameof(QueryFileSize));

        public override void RequestBuffer(in RHIStorageBufferRequest request) =>
            throw CreateNotSupported(nameof(RequestBuffer));

        public override void RequestTexture(in RHIStorageTextureRequest request) =>
            throw CreateNotSupported(nameof(RequestTexture));

        public override void Submit(RHIFence signalFence) =>
            throw CreateNotSupported(nameof(Submit));

        public override void ThrowIfSubmissionFailed() =>
            throw CreateNotSupported(nameof(ThrowIfSubmissionFailed));

        public override void CancelRequestsWithTag(ulong mask, ulong value) =>
            throw CreateNotSupported(nameof(CancelRequestsWithTag));

        public override void CancelPending() =>
            throw CreateNotSupported(nameof(CancelPending));

        protected override void Release()
        {
        }

        private static NotSupportedException CreateNotSupported(string operation) =>
            new NotSupportedException(
                $"Vulkan StorageQueue.{operation} is unavailable because SharpGPU has no official native storage API for this backend.");
    }
}
