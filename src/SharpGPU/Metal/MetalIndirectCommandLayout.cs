namespace SharpGPU
{
    internal sealed class MetalIndirectCommandLayout : RHIIndirectCommandLayout
    {
        internal MetalIndirectCommandLayout(
            MetalDevice device,
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
