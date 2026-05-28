using System;
using Vortice.Vulkan;

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
                sType = VkStructureType.QueryPoolCreateInfo,
                queryType = VulkanUtility.ConvertToVkQueryType(descriptor.Type),
                queryCount = descriptor.Count,
            };

            if (descriptor.Type == ERHIQueryType.Statistics)
            {
                queryPoolInfo.pipelineStatistics =
                    VkQueryPipelineStatisticFlags.InputAssemblyVertices |
                    VkQueryPipelineStatisticFlags.InputAssemblyPrimitives |
                    VkQueryPipelineStatisticFlags.VertexShaderInvocations |
                    VkQueryPipelineStatisticFlags.FragmentShaderInvocations |
                    VkQueryPipelineStatisticFlags.ComputeShaderInvocations;
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
                    VkQueryResultFlags.Bit64);

                return result == VkResult.Success;
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyQueryPool(m_VulkanDevice.NativeDevice, m_NativeQueryPool, null);
        }
    }
#pragma warning restore CS8618
}


