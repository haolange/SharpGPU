using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanArgumentTableLayout : RHIArgumentTableLayout
    {
        public VkDescriptorSetLayout NativeDescriptorSetLayout => m_NativeDescriptorSetLayout;
        public RHIArgumentTableLayoutDescriptor Descriptor => m_Descriptor;

        private VulkanDevice m_VulkanDevice;
        private VkDescriptorSetLayout m_NativeDescriptorSetLayout;
        private RHIArgumentTableLayoutDescriptor m_Descriptor;

        public VulkanArgumentTableLayout(VulkanDevice device, in RHIArgumentTableLayoutDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;

            int elementCount = descriptor.Elements.Length;
            VkDescriptorSetLayoutBinding* bindings = stackalloc VkDescriptorSetLayoutBinding[elementCount];

            for (int i = 0; i < elementCount; ++i)
            {
                ref RHIArgumentTableLayoutElement element = ref descriptor.Elements.Span[i];
                bindings[i] = new VkDescriptorSetLayoutBinding()
                {
                    binding = element.Slot,
                    descriptorType = VulkanUtility.ConvertToVkDescriptorType(element.Type),
                    descriptorCount = element.Count > 0 ? element.Count : 1,
                    stageFlags = VulkanUtility.ConvertToVkShaderStage(element.Stage),
                    pImmutableSamplers = null,
                };
            }

            VkDescriptorSetLayoutCreateInfo layoutInfo = new VkDescriptorSetLayoutCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO,
                bindingCount = (uint)elementCount,
                pBindings = bindings,
            };

            fixed (VkDescriptorSetLayout* layoutPtr = &m_NativeDescriptorSetLayout)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateDescriptorSetLayout(device.NativeDevice, &layoutInfo, null, layoutPtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyDescriptorSetLayout(m_VulkanDevice.NativeDevice, m_NativeDescriptorSetLayout, null);
        }
    }

    internal unsafe class VulkanArgumentTable : RHIArgumentTable
    {
        public VkDescriptorSet NativeDescriptorSet => m_NativeDescriptorSet;

        private VulkanDevice m_VulkanDevice;
        private VkDescriptorSet m_NativeDescriptorSet;
        private VulkanArgumentTableLayout m_Layout;

        public VulkanArgumentTable(VulkanDevice device, in RHIArgumentTableDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Layout = descriptor.Layout as VulkanArgumentTableLayout;

            VkDescriptorSetLayout layout = m_Layout.NativeDescriptorSetLayout;

            VkDescriptorSetAllocateInfo allocInfo = new VkDescriptorSetAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO,
                descriptorPool = device.NativeDescriptorPool,
                descriptorSetCount = 1,
                pSetLayouts = &layout,
            };

            fixed (VkDescriptorSet* setPtr = &m_NativeDescriptorSet)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateDescriptorSets(device.NativeDevice, &allocInfo, setPtr));
            }

            // Update descriptor set with initial elements
            int elementCount = descriptor.Elements.Length;
            Span<RHIArgumentTableLayoutElement> layoutElements = m_Layout.Descriptor.Elements.Span;

            for (int i = 0; i < elementCount; ++i)
            {
                ref RHIArgumentTableElement element = ref descriptor.Elements.Span[i];
                ref RHIArgumentTableLayoutElement layoutElement = ref layoutElements[i];
                SetBindElement(element, layoutElement.Type, (int)layoutElement.Slot);
            }
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot)
        {
            VkWriteDescriptorSet writeDescriptor = new VkWriteDescriptorSet()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
                dstSet = m_NativeDescriptorSet,
                dstBinding = (uint)slot,
                dstArrayElement = 0,
                descriptorCount = 1,
                descriptorType = VulkanUtility.ConvertToVkDescriptorType(bindType),
            };

            switch (bindType)
            {
                case ERHIBindType.Sampler:
                {
                    if (element.Sampler != null)
                    {
                        VulkanSampler vkSampler = element.Sampler as VulkanSampler;
                        VkDescriptorImageInfo imageInfo = vkSampler.GetDescriptorImageInfo();
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                {
                    if (element.BufferView != null)
                    {
                        VulkanBufferView vkBufferView = element.BufferView as VulkanBufferView;
                        VkDescriptorBufferInfo bufferInfo = vkBufferView.GetDescriptorBufferInfo();
                        writeDescriptor.pBufferInfo = &bufferInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                {
                    if (element.TextureView != null)
                    {
                        VulkanTextureView vkTextureView = element.TextureView as VulkanTextureView;
                        VkDescriptorImageInfo imageInfo = vkTextureView.GetDescriptorImageInfo(VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                {
                    if (element.TextureView != null)
                    {
                        VulkanTextureView vkTextureView = element.TextureView as VulkanTextureView;
                        VkDescriptorImageInfo imageInfo = vkTextureView.GetDescriptorImageInfo(VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
            }
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex)
        {
            VkWriteDescriptorSet writeDescriptor = new VkWriteDescriptorSet()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET,
                dstSet = m_NativeDescriptorSet,
                dstBinding = (uint)slot,
                dstArrayElement = (uint)arrayIndex,
                descriptorCount = 1,
                descriptorType = VulkanUtility.ConvertToVkDescriptorType(bindType),
            };

            switch (bindType)
            {
                case ERHIBindType.Sampler:
                {
                    if (element.Sampler != null)
                    {
                        VulkanSampler vkSampler = element.Sampler as VulkanSampler;
                        VkDescriptorImageInfo imageInfo = vkSampler.GetDescriptorImageInfo();
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                {
                    if (element.BufferView != null)
                    {
                        VulkanBufferView vkBufferView = element.BufferView as VulkanBufferView;
                        VkDescriptorBufferInfo bufferInfo = vkBufferView.GetDescriptorBufferInfo();
                        writeDescriptor.pBufferInfo = &bufferInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                {
                    if (element.TextureView != null)
                    {
                        VulkanTextureView vkTextureView = element.TextureView as VulkanTextureView;
                        VkDescriptorImageInfo imageInfo = vkTextureView.GetDescriptorImageInfo(VkImageLayout.VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                {
                    if (element.TextureView != null)
                    {
                        VulkanTextureView vkTextureView = element.TextureView as VulkanTextureView;
                        VkDescriptorImageInfo imageInfo = vkTextureView.GetDescriptorImageInfo(VkImageLayout.VK_IMAGE_LAYOUT_GENERAL);
                        writeDescriptor.pImageInfo = &imageInfo;
                        VulkanNative.vkUpdateDescriptorSets(m_VulkanDevice.NativeDevice, 1, &writeDescriptor, 0, null);
                    }
                    break;
                }
            }
        }

        protected override void Release()
        {
            fixed (VkDescriptorSet* setPtr = &m_NativeDescriptorSet)
            {
                VulkanNative.vkFreeDescriptorSets(m_VulkanDevice.NativeDevice, m_VulkanDevice.NativeDescriptorPool, 1, setPtr);
            }
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
