using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe class VulkanQuery : RHIQuery
    {
        public VkQueryPool NativeQueryPool => m_NativeQueryPool;
        public uint ResultStrideInBytes => m_ResultStrideInBytes;

        private VulkanDevice m_VulkanDevice;
        private VkQueryPool m_NativeQueryPool;
        private readonly uint m_ResultStrideInBytes;

        public VulkanQuery(VulkanDevice device, in RHIQueryDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_QueryDescriptor = descriptor;
            m_ResultStrideInBytes = GetResultStrideInBytes(descriptor.Type);
            uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));
            m_Results = new ulong[resultElementCount];
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
                    (nuint)(m_Results!.Length * sizeof(ulong)),
                    resultsPtr,
                    m_ResultStrideInBytes,
                    VkQueryResultFlags.Bit64);

                return result == VkResult.Success;
            }
        }

        private static uint GetResultStrideInBytes(in ERHIQueryType queryType)
        {
            return queryType == ERHIQueryType.Statistics
                ? 5u * sizeof(ulong)
                : sizeof(ulong);
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyQueryPool(m_VulkanDevice.NativeDevice, m_NativeQueryPool, null);
        }
    }
}


