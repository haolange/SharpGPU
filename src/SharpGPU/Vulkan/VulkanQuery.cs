using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe sealed class VulkanQuery : RHIQuery
    {
        public VkQueryPool NativeQueryPool
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeQueryPool;
            }
        }

        public uint ResultStrideInBytes => m_ResultStrideInBytes;

        private readonly VulkanDevice m_VulkanDevice;
        private VkQueryPool m_NativeQueryPool;
        private readonly uint m_ResultStrideInBytes;
        private uint m_ResolvedStartIndex;
        private uint m_ResolvedQueryCount;

        public VulkanQuery(VulkanDevice device, in RHIQueryDescriptor descriptor)
        {
            m_Device = device;
            m_VulkanDevice = device;
            m_QueryDescriptor = descriptor;
            m_ResultStrideInBytes = GetResultStrideInBytes(descriptor.Type);
            uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));
            m_Results = new ulong[resultElementCount];
            m_ResolvedStartIndex = 0;
            m_ResolvedQueryCount = descriptor.Count;

            VkQueryPoolCreateInfo queryPoolInfo = new VkQueryPoolCreateInfo
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
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateQueryPool(
                        device.NativeDevice,
                        &queryPoolInfo,
                        null,
                        poolPtr));
            }
        }

        internal void MarkResolvedRange(uint startIndex, uint queryCount)
        {
            ValidateRange(startIndex, queryCount);
            m_ResolvedStartIndex = startIndex;
            m_ResolvedQueryCount = queryCount;
        }

        public override ERHIQueryResultStatus ResolveData()
        {
            ThrowIfDisposed();
            uint elementsPerQuery = m_ResultStrideInBytes / sizeof(ulong);
            fixed (ulong* resultsStart = m_Results)
            {
                ulong* resultsPtr = resultsStart + m_ResolvedStartIndex * elementsPerQuery;
                VkResult result = VulkanNative.vkGetQueryPoolResults(
                    m_VulkanDevice.NativeDevice,
                    m_NativeQueryPool,
                    m_ResolvedStartIndex,
                    m_ResolvedQueryCount,
                    checked((nuint)(m_ResolvedQueryCount * m_ResultStrideInBytes)),
                    resultsPtr,
                    m_ResultStrideInBytes,
                    VkQueryResultFlags.Bit64);

                if (result == VkResult.Success)
                {
                    return ERHIQueryResultStatus.Ready;
                }
                if (result == VkResult.NotReady)
                {
                    return ERHIQueryResultStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
                throw new InvalidOperationException("Vulkan query result handling returned unexpectedly.");
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
            VulkanNative.vkDestroyQueryPool(
                m_VulkanDevice.NativeDevice,
                m_NativeQueryPool,
                null);
            m_NativeQueryPool = default;
        }
    }
}