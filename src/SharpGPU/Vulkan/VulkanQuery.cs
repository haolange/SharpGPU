using System;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanQuery : RHIQuery
    {
        public VkQueryPool NativeQueryPool => m_NativeQueryPool;

        private VulkanDevice m_VulkanDevice;
        private VkQueryPool m_NativeQueryPool;

        public VulkanQuery(VulkanDevice device, in RHIQueryDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_QueryDescriptor = descriptor;
            m_Results = new ulong[descriptor.Count];
            Results = m_Results;

            VkQueryPoolCreateInfo queryPoolInfo = new VkQueryPoolCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO,
                queryType = VulkanUtility.ConvertToVkQueryType(descriptor.Type),
                queryCount = descriptor.Count,
            };

            if (descriptor.Type == ERHIQueryType.Statistics)
            {
                queryPoolInfo.pipelineStatistics =
                    VkQueryPipelineStatisticFlags.VK_QUERY_PIPELINE_STATISTIC_INPUT_ASSEMBLY_VERTICES_BIT |
                    VkQueryPipelineStatisticFlags.VK_QUERY_PIPELINE_STATISTIC_INPUT_ASSEMBLY_PRIMITIVES_BIT |
                    VkQueryPipelineStatisticFlags.VK_QUERY_PIPELINE_STATISTIC_VERTEX_SHADER_INVOCATIONS_BIT |
                    VkQueryPipelineStatisticFlags.VK_QUERY_PIPELINE_STATISTIC_FRAGMENT_SHADER_INVOCATIONS_BIT |
                    VkQueryPipelineStatisticFlags.VK_QUERY_PIPELINE_STATISTIC_COMPUTE_SHADER_INVOCATIONS_BIT;
            }

            fixed (VkQueryPool* poolPtr = &m_NativeQueryPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateQueryPool(device.NativeDevice, &queryPoolInfo, null, poolPtr));
            }
        }

        public override bool ResolveData()
        {
            fixed (ulong* resultsPtr = m_Results)
            {
                VkResult result = VulkanNative.vkGetQueryPoolResults(
                    m_VulkanDevice.NativeDevice,
                    m_NativeQueryPool,
                    0,
                    m_QueryDescriptor.Count,
                    (nuint)(m_QueryDescriptor.Count * sizeof(ulong)),
                    resultsPtr,
                    (ulong)sizeof(ulong),
                    VkQueryResultFlags.VK_QUERY_RESULT_64_BIT);

                return result == VkResult.VK_SUCCESS;
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyQueryPool(m_VulkanDevice.NativeDevice, m_NativeQueryPool, null);
        }
    }
#pragma warning restore CS8618
}
