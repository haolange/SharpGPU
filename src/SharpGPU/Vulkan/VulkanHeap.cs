using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanHeap : RHIHeap
    {
        public VkDeviceMemory NativeMemory => m_NativeMemory;

        private VulkanDevice m_VulkanDevice;
        private VkDeviceMemory m_NativeMemory;

        public VulkanHeap(VulkanDevice device, in RHIHeapDescription descriptor)
        {
            m_VulkanDevice = device;

            VkMemoryPropertyFlags memProps = VulkanUtility.ConvertToVkMemoryProperty(descriptor.StorageMode);

            // Find a suitable memory type
            VkPhysicalDeviceMemoryProperties memProperties = device.MemoryProperties;
            uint memTypeIndex = 0;
            for (uint i = 0; i < memProperties.memoryTypeCount; ++i)
            {
                if ((memProperties.GetMemoryType(i).propertyFlags & memProps) == memProps)
                {
                    memTypeIndex = i;
                    break;
                }
            }

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO,
                allocationSize = descriptor.Size,
                memoryTypeIndex = memTypeIndex,
            };

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
        }
    }
#pragma warning restore CS8618
}
