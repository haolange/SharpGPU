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

        internal uint ResultStrideInBytes => m_ResultStrideInBytes;
        internal VkQueryPipelineStatisticFlags PipelineStatisticFlags =>
            m_PipelineStatisticFlags;
        internal uint ResolvedStartIndex => m_ResolvedStartIndex;
        internal uint ResolvedQueryCount => m_ResolvedQueryCount;

        private readonly VulkanDevice m_VulkanDevice;
        private VkQueryPool m_NativeQueryPool;
        private readonly uint m_ResultStrideInBytes;
        private readonly VkQueryPipelineStatisticFlags m_PipelineStatisticFlags;
        private readonly ERHIPipelineStatisticCounter[] m_EnabledCounters;
        private uint m_ResolvedStartIndex;
        private uint m_ResolvedQueryCount;

        public VulkanQuery(VulkanDevice device, in RHIQueryDescriptor descriptor)
        {
            m_Device = device;
            m_VulkanDevice = device;
            m_QueryDescriptor = descriptor;
            m_PipelineStatisticFlags = descriptor.Type == ERHIQueryType.Statistics
                ? ConvertToVulkanStatisticFlags(descriptor.CounterMask)
                : 0;
            m_EnabledCounters = descriptor.Type == ERHIQueryType.Statistics
                ? EnumerateEnabledCounters(descriptor.CounterMask)
                : Array.Empty<ERHIPipelineStatisticCounter>();
            m_ResultStrideInBytes = descriptor.Type == ERHIQueryType.Statistics
                ? checked((uint)m_EnabledCounters.Length * sizeof(ulong))
                : sizeof(ulong);
            if (descriptor.Type == ERHIQueryType.Statistics)
            {
                AllocateStatisticsSlots(descriptor);
            }
            else
            {
                uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));
                m_Results = new ulong[resultElementCount];
            }

            m_ResolvedStartIndex = 0;
            m_ResolvedQueryCount = descriptor.Count;

            VkQueryPoolCreateInfo queryPoolInfo = new VkQueryPoolCreateInfo
            {
                sType = VkStructureType.QueryPoolCreateInfo,
                queryType = VulkanUtility.ConvertToVkQueryType(descriptor.Type),
                queryCount = descriptor.Count,
                pipelineStatistics = m_PipelineStatisticFlags,
            };

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
            if (m_QueryDescriptor.Type == ERHIQueryType.Statistics)
            {
                return ResolveStatisticsData();
            }

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

        private ERHIQueryResultStatus ResolveStatisticsData()
        {
            ulong[] packed = new ulong[checked(m_ResolvedQueryCount * (uint)m_EnabledCounters.Length)];
            fixed (ulong* resultsPtr = packed)
            {
                VkResult result = VulkanNative.vkGetQueryPoolResults(
                    m_VulkanDevice.NativeDevice,
                    m_NativeQueryPool,
                    m_ResolvedStartIndex,
                    m_ResolvedQueryCount,
                    checked((nuint)(packed.Length * sizeof(ulong))),
                    resultsPtr,
                    m_ResultStrideInBytes,
                    VkQueryResultFlags.Bit64);
                if (result == VkResult.NotReady)
                {
                    return ERHIQueryResultStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
            }

            for (uint i = 0; i < m_ResolvedQueryCount; i++)
            {
                uint slot = m_ResolvedStartIndex + i;
                int packedOffset = checked((int)(i * (uint)m_EnabledCounters.Length));
                StoreStatistics(slot, packed.AsSpan(packedOffset, m_EnabledCounters.Length));
            }

            return ERHIQueryResultStatus.Ready;
        }

        private void AllocateStatisticsSlots(in RHIQueryDescriptor descriptor)
        {
            switch (descriptor.Domain)
            {
                case ERHIPipelineStatisticsDomain.Raster:
                    m_RasterStatistics = new RHIRasterPipelineStatistics[descriptor.Count];
                    break;
                case ERHIPipelineStatisticsDomain.Compute:
                    m_ComputeStatistics = new RHIComputePipelineStatistics[descriptor.Count];
                    break;
                case ERHIPipelineStatisticsDomain.RayTracing:
                    m_RayTracingStatistics = new RHIRayTracingPipelineStatistics[descriptor.Count];
                    break;
            }
        }

        private void StoreStatistics(uint index, ReadOnlySpan<ulong> packed)
        {
            ulong inputAssemblyVertices = 0;
            ulong inputAssemblyPrimitives = 0;
            ulong vertexShaderInvocations = 0;
            ulong geometryShaderInvocations = 0;
            ulong geometryShaderPrimitives = 0;
            ulong clipperInvocations = 0;
            ulong clipperPrimitives = 0;
            ulong pixelShaderInvocations = 0;
            ulong hullShaderInvocations = 0;
            ulong domainShaderInvocations = 0;
            ulong computeShaderInvocations = 0;
            ulong meshShaderInvocations = 0;
            ulong taskShaderInvocations = 0;
            ulong meshShaderPrimitives = 0;
            for (int i = 0; i < m_EnabledCounters.Length; i++)
            {
                ulong value = packed[i];
                switch (m_EnabledCounters[i])
                {
                    case ERHIPipelineStatisticCounter.InputAssemblyVertices:
                        inputAssemblyVertices = value;
                        break;
                    case ERHIPipelineStatisticCounter.InputAssemblyPrimitives:
                        inputAssemblyPrimitives = value;
                        break;
                    case ERHIPipelineStatisticCounter.VertexShaderInvocations:
                        vertexShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.GeometryShaderInvocations:
                        geometryShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.GeometryShaderPrimitives:
                        geometryShaderPrimitives = value;
                        break;
                    case ERHIPipelineStatisticCounter.ClipperInvocations:
                        clipperInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.ClipperPrimitives:
                        clipperPrimitives = value;
                        break;
                    case ERHIPipelineStatisticCounter.PixelShaderInvocations:
                        pixelShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.HullShaderInvocations:
                        hullShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.DomainShaderInvocations:
                        domainShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.ComputeShaderInvocations:
                        computeShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.MeshShaderInvocations:
                        meshShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.TaskShaderInvocations:
                        taskShaderInvocations = value;
                        break;
                    case ERHIPipelineStatisticCounter.MeshShaderPrimitives:
                        meshShaderPrimitives = value;
                        break;
                }
            }

            if (m_QueryDescriptor.Domain == ERHIPipelineStatisticsDomain.Compute)
            {
                m_ComputeStatistics[index] = new RHIComputePipelineStatistics(computeShaderInvocations);
                return;
            }

            m_RasterStatistics[index] = new RHIRasterPipelineStatistics(
                inputAssemblyVertices,
                inputAssemblyPrimitives,
                vertexShaderInvocations,
                geometryShaderInvocations,
                geometryShaderPrimitives,
                clipperInvocations,
                clipperPrimitives,
                pixelShaderInvocations,
                hullShaderInvocations,
                domainShaderInvocations,
                meshShaderInvocations,
                taskShaderInvocations,
                meshShaderPrimitives);
        }

        internal static VkQueryPipelineStatisticFlags ConvertToVulkanStatisticFlags(
            ERHIPipelineStatisticCounter mask)
        {
            VkQueryPipelineStatisticFlags flags = 0;
            if ((mask & ERHIPipelineStatisticCounter.InputAssemblyVertices) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.InputAssemblyVertices;
            }

            if ((mask & ERHIPipelineStatisticCounter.InputAssemblyPrimitives) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.InputAssemblyPrimitives;
            }

            if ((mask & ERHIPipelineStatisticCounter.VertexShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.VertexShaderInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.GeometryShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.GeometryShaderInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.GeometryShaderPrimitives) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.GeometryShaderPrimitives;
            }

            if ((mask & ERHIPipelineStatisticCounter.ClipperInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.ClippingInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.ClipperPrimitives) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.ClippingPrimitives;
            }

            if ((mask & ERHIPipelineStatisticCounter.PixelShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.FragmentShaderInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.HullShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.TessellationControlShaderPatches;
            }

            if ((mask & ERHIPipelineStatisticCounter.DomainShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.TessellationEvaluationShaderInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.ComputeShaderInvocations) != 0)
            {
                flags |= VkQueryPipelineStatisticFlags.ComputeShaderInvocations;
            }

            if ((mask & ERHIPipelineStatisticCounter.TaskShaderInvocations) != 0)
            {
                flags |= (VkQueryPipelineStatisticFlags)0x800;
            }

            if ((mask & ERHIPipelineStatisticCounter.MeshShaderInvocations) != 0)
            {
                flags |= (VkQueryPipelineStatisticFlags)0x1000;
            }

            if ((mask & ERHIPipelineStatisticCounter.MeshShaderPrimitives) != 0)
            {
                flags |= (VkQueryPipelineStatisticFlags)0x2000;
            }

            return flags;
        }

        private static ERHIPipelineStatisticCounter[] EnumerateEnabledCounters(
            ERHIPipelineStatisticCounter mask)
        {
            ERHIPipelineStatisticCounter[] order =
            {
                ERHIPipelineStatisticCounter.InputAssemblyVertices,
                ERHIPipelineStatisticCounter.InputAssemblyPrimitives,
                ERHIPipelineStatisticCounter.VertexShaderInvocations,
                ERHIPipelineStatisticCounter.GeometryShaderInvocations,
                ERHIPipelineStatisticCounter.GeometryShaderPrimitives,
                ERHIPipelineStatisticCounter.ClipperInvocations,
                ERHIPipelineStatisticCounter.ClipperPrimitives,
                ERHIPipelineStatisticCounter.PixelShaderInvocations,
                ERHIPipelineStatisticCounter.HullShaderInvocations,
                ERHIPipelineStatisticCounter.DomainShaderInvocations,
                ERHIPipelineStatisticCounter.ComputeShaderInvocations,
                ERHIPipelineStatisticCounter.TaskShaderInvocations,
                ERHIPipelineStatisticCounter.MeshShaderInvocations,
                ERHIPipelineStatisticCounter.MeshShaderPrimitives,
            };
            int count = 0;
            for (int i = 0; i < order.Length; i++)
            {
                if ((mask & order[i]) != 0)
                {
                    count++;
                }
            }

            ERHIPipelineStatisticCounter[] enabled = new ERHIPipelineStatisticCounter[count];
            int write = 0;
            for (int i = 0; i < order.Length; i++)
            {
                if ((mask & order[i]) != 0)
                {
                    enabled[write++] = order[i];
                }
            }

            return enabled;
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
