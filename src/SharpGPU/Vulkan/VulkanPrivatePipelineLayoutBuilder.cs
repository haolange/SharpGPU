using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal readonly struct VulkanRasterPipelineVariantKey :
        IEquatable<VulkanRasterPipelineVariantKey>
    {
        internal ulong RenderPassHandle { get; }
        internal uint SubPass { get; }

        internal VulkanRasterPipelineVariantKey(
            VkRenderPass renderPass,
            uint subPass)
        {
            RenderPassHandle = renderPass.Handle;
            SubPass = subPass;
        }

        public bool Equals(VulkanRasterPipelineVariantKey other) =>
            RenderPassHandle == other.RenderPassHandle &&
            SubPass == other.SubPass;

        public override bool Equals(object? obj) =>
            obj is VulkanRasterPipelineVariantKey other &&
            Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RenderPassHandle, SubPass);
    }

    internal static unsafe class VulkanPrivatePipelineLayoutBuilder
    {
        internal static VulkanPrivateRasterBindingPlan CompilePlan(
            VulkanDevice device,
            VulkanPipelineLayout pipelineLayout,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            uint[] ordinarySets =
                new uint[pipelineLayout.NativeTableLayouts.Count];
            int setIndex = 0;
            foreach (uint descriptorSet in
                     pipelineLayout.NativeTableLayouts.Keys)
            {
                ordinarySets[setIndex++] = descriptorSet;
            }
            return VulkanPrivateRasterBindingPlan.Compile(
                ordinarySets,
                device.DescriptorLimits.MaximumBoundSets,
                in attachmentInterface);
        }

        internal static VkPipelineLayout Create(
            VulkanPipelineLayout pipelineLayout,
            VulkanPrivateRasterDescriptorLayout privateLayout)
        {
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            ArgumentNullException.ThrowIfNull(privateLayout);
            VulkanPrivateRasterBindingPlan plan = privateLayout.Plan;
            int setLayoutCount =
                checked((int)plan.DescriptorSet + 1);
            VkDescriptorSetLayout* setLayouts =
                stackalloc VkDescriptorSetLayout[setLayoutCount];
            VkDescriptorSetLayout emptyLayout = default;
            VkPipelineLayout nativeLayout = default;
            VulkanDevice device = pipelineLayout.Device;
            try
            {
                VkDescriptorSetLayoutCreateInfo emptyInfo =
                    new VkDescriptorSetLayoutCreateInfo
                    {
                        sType = VkStructureType
                            .DescriptorSetLayoutCreateInfo,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateDescriptorSetLayout(
                        device.NativeDevice,
                        &emptyInfo,
                        null,
                        &emptyLayout));
                for (int index = 0;
                     index < setLayoutCount;
                     ++index)
                {
                    setLayouts[index] = emptyLayout;
                }
                foreach (KeyValuePair<uint, VkDescriptorSetLayout> pair
                         in pipelineLayout.NativeTableLayouts)
                {
                    setLayouts[checked((int)pair.Key)] = pair.Value;
                }
                setLayouts[checked((int)plan.DescriptorSet)] =
                    privateLayout.NativeLayout;

                VkPushConstantRange pushConstantRange =
                    new VkPushConstantRange
                    {
                        stageFlags = VkShaderStageFlags.All,
                        size = pipelineLayout.PushConstantSize,
                    };
                VkPipelineLayoutCreateInfo createInfo =
                    new VkPipelineLayoutCreateInfo
                    {
                        sType =
                            VkStructureType.PipelineLayoutCreateInfo,
                        setLayoutCount =
                            checked((uint)setLayoutCount),
                        pSetLayouts = setLayouts,
                        pushConstantRangeCount =
                            pipelineLayout.PushConstantSize == 0
                                ? 0u
                                : 1u,
                        pPushConstantRanges =
                            pipelineLayout.PushConstantSize == 0
                                ? null
                                : &pushConstantRange,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreatePipelineLayout(
                        device.NativeDevice,
                        &createInfo,
                        null,
                        &nativeLayout));
                return nativeLayout;
            }
            finally
            {
                if (emptyLayout.Handle != 0)
                {
                    VulkanNative.vkDestroyDescriptorSetLayout(
                        device.NativeDevice,
                        emptyLayout,
                        null);
                }
            }
        }
    }
}
