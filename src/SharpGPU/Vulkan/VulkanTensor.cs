using System;

namespace SharpGPU
{
    internal sealed class VulkanTensor : RHITensor
    {
        internal VulkanDevice Device { get; }

        public VulkanTensor(VulkanDevice device, in RHIMLTensorDescriptor descriptor)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            _ = descriptor;
            throw CreateNotSupported("Vulkan machine-learning tensors");
        }

        protected override void Release()
        {
        }

        internal static NotSupportedException CreateNotSupported(string operation) =>
            new NotSupportedException(
                operation
                + " are unavailable because SharpGPU has no official native ML tensor API for the Vulkan backend.");
    }
}
