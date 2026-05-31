namespace SharpGPU
{
    internal sealed class MetalBufferView : RHIBufferView
    {
        public MetalBuffer Buffer => m_Buffer;
        public RHIBufferViewDescriptor Descriptor => m_Descriptor;

        private readonly MetalBuffer m_Buffer;
        private readonly RHIBufferViewDescriptor m_Descriptor;

        public MetalBufferView(MetalBuffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_Buffer = buffer;
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
        }
    }
}
