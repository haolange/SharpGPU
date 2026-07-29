namespace SharpGPU
{
    internal sealed class VulkanIndirectCommandLayout : RHIIndirectCommandLayout
    {
        internal VulkanIndirectCommandLayout(
            VulkanDevice device,
            in RHIIndirectCommandLayoutDescriptor descriptor)
            : base(descriptor)
        {
            ValidateForDevice(device);
        }

        protected override void Release()
        {
        }
    }
}
