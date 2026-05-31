using Vortice.Vulkan;

namespace SharpGPU
{
#pragma warning disable CS8618
    internal unsafe class VulkanBufferView : RHIBufferView
    {
        public VulkanBuffer VulkanBuffer => m_VulkanBuffer;

        private VulkanBuffer m_VulkanBuffer;
        private RHIBufferViewDescriptor m_Descriptor;

        public VulkanBufferView(VulkanBuffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_VulkanBuffer = buffer;
            m_Descriptor = descriptor;
        }

        public VkDescriptorBufferInfo GetDescriptorBufferInfo()
        {
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
#pragma warning restore CS8618
}

