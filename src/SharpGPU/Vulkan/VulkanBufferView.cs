using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanBufferView : RHIBufferView
    {
        public VulkanBuffer VulkanBuffer { get { ThrowIfDisposed(); return m_VulkanBuffer; } }
        public RHIBufferViewDescriptor Descriptor { get { ThrowIfDisposed(); return m_Descriptor; } }
        internal VulkanDevice Device { get { ThrowIfDisposed(); return m_VulkanBuffer.VulkanDevice; } }

        private VulkanBuffer m_VulkanBuffer;
        private RHIBufferViewDescriptor m_Descriptor;

        public VulkanBufferView(VulkanBuffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_VulkanBuffer = buffer;
            m_Descriptor = descriptor;
        }

        public VkDescriptorBufferInfo GetDescriptorBufferInfo()
        {
            ThrowIfDisposed();
            return new VkDescriptorBufferInfo()
            {
                buffer = m_VulkanBuffer.NativeBuffer,
                offset = (ulong)(m_Descriptor.Stride * m_Descriptor.Offset),
                range = m_Descriptor.ViewType == ERHIBufferViewType.UniformBuffer
                    ? (ulong)m_Descriptor.Stride
                    : (ulong)(m_Descriptor.Count * m_Descriptor.Stride),
            };
        }

        protected override void Release()
        {
        }
    }
}

