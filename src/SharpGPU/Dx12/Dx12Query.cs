using System;
using SharpGPU.Collections.LowLevel;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe sealed class Dx12Query : RHIQuery
    {
        public Vortice.Direct3D12.ID3D12Resource QueryResult
        {
            get
            {
                ThrowIfDisposed();
                return m_QueryResult;
            }
        }

        public Vortice.Direct3D12.ID3D12QueryHeap QueryHeap
        {
            get
            {
                ThrowIfDisposed();
                return m_QueryHeap;
            }
        }

        internal uint ResultStrideInBytes => m_ResultStrideInBytes;
        internal Vortice.Direct3D12.QueryType NativeQueryType => m_NativeQueryType;

        private readonly Vortice.Direct3D12.ID3D12Resource m_QueryResult;
        private readonly Vortice.Direct3D12.ID3D12QueryHeap m_QueryHeap;
        private readonly uint m_ResultStrideInBytes;
        private readonly Vortice.Direct3D12.QueryType m_NativeQueryType;
        private readonly bool m_UsesStatistics1;

        public Dx12Query(Dx12Device device, in RHIQueryDescriptor descriptor)
        {
            m_Device = device;
            m_QueryDescriptor = descriptor;
            m_UsesStatistics1 = UsesPipelineStatistics1(device, in descriptor);
            m_NativeQueryType = ConvertNativeQueryType(descriptor.Type, m_UsesStatistics1);
            m_ResultStrideInBytes = GetResultStrideInBytes(descriptor.Type, m_UsesStatistics1);
            if (descriptor.Type == ERHIQueryType.Statistics)
            {
                AllocateStatisticsSlots(descriptor);
            }
            else
            {
                uint resultElementCount = checked((descriptor.Count * m_ResultStrideInBytes) / sizeof(ulong));
                m_Results = new ulong[resultElementCount];
            }

            Vortice.Direct3D12.QueryHeapDescription queryHeapDesc;
            queryHeapDesc.Type = ConvertNativeQueryHeapType(descriptor.Type, m_UsesStatistics1);
            queryHeapDesc.Count = descriptor.Count;
            queryHeapDesc.NodeMask = 0;

            SharpGen.Runtime.Result queryHeapResult =
                device.NativeDevice.CreateQueryHeap(
                    queryHeapDesc,
                    out Vortice.Direct3D12.ID3D12QueryHeap queryHeap);
            Dx12Utility.CHECK_HR(queryHeapResult);
            m_QueryHeap = queryHeap ?? throw new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.DirectX12,
                queryHeapResult.Code,
                "ID3D12Device.CreateQueryHeap returned no query heap.",
                ERHIDeviceState.Operational);

            Vortice.Direct3D12.HeapProperties heapProperties;
            heapProperties.Type = Vortice.Direct3D12.HeapType.Readback;
            heapProperties.CPUPageProperty = Vortice.Direct3D12.CpuPageProperty.Unknown;
            heapProperties.MemoryPoolPreference = Vortice.Direct3D12.MemoryPool.Unknown;
            heapProperties.VisibleNodeMask = 0;
            heapProperties.CreationNodeMask = 0;

            Vortice.Direct3D12.ResourceDescription resourceDesc;
            resourceDesc.Alignment = 0;
            resourceDesc.Dimension = Vortice.Direct3D12.ResourceDimension.Buffer;
            resourceDesc.Width = m_ResultStrideInBytes * descriptor.Count;
            resourceDesc.Height = 1;
            resourceDesc.DepthOrArraySize = 1;
            resourceDesc.MipLevels = 1;
            resourceDesc.SampleDescription.Count = 1;
            resourceDesc.SampleDescription.Quality = 0;
            resourceDesc.Format = Vortice.DXGI.Format.Unknown;
            resourceDesc.Flags = Vortice.Direct3D12.ResourceFlags.None;
            resourceDesc.Layout = Vortice.Direct3D12.TextureLayout.RowMajor;

            SharpGen.Runtime.Result resourceResult =
                device.NativeDevice.CreateCommittedResource(
                    heapProperties,
                    Vortice.Direct3D12.HeapFlags.None,
                    resourceDesc,
                    Vortice.Direct3D12.ResourceStates.CopyDest,
                    null,
                    out Vortice.Direct3D12.ID3D12Resource queryResult);
            try
            {
                Dx12Utility.CHECK_HR(resourceResult);
                m_QueryResult = queryResult ?? throw new RHIException(
                    ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    resourceResult.Code,
                    "ID3D12Device.CreateCommittedResource returned no query readback buffer.",
                    ERHIDeviceState.Operational);
            }
            catch
            {
                m_QueryHeap.Release();
                throw;
            }
        }

        public override ERHIQueryResultStatus ResolveData()
        {
            ThrowIfDisposed();
            uint byteCount = m_QueryDescriptor.Type == ERHIQueryType.Statistics
                ? checked(m_ResultStrideInBytes * m_QueryDescriptor.Count)
                : checked((uint)(m_Results.Length * sizeof(ulong)));
            void* queryResult = null;
            Vortice.Direct3D12.Range readRange =
                new Vortice.Direct3D12.Range(0, byteCount);
            SharpGen.Runtime.Result mapResult = m_QueryResult.Map(0, readRange, &queryResult);
            Dx12Utility.CHECK_HR(mapResult);
            if (queryResult == null)
            {
                throw new RHIException(
                    ERHIErrorCode.NativeFailure,
                    ERHIBackend.DirectX12,
                    mapResult.Code,
                    "ID3D12Resource.Map returned a null query readback pointer.",
                    ERHIDeviceState.Operational);
            }

            try
            {
                if (m_QueryDescriptor.Type == ERHIQueryType.Statistics)
                {
                    CopyStatisticsResults(queryResult);
                }
                else
                {
                    new IntPtr(queryResult).CopyTo(m_Results.AsSpan());
                }
            }
            finally
            {
                m_QueryResult.Unmap(0, null);
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

        private void CopyStatisticsResults(void* queryResult)
        {
            byte* bytes = (byte*)queryResult;
            for (uint index = 0; index < m_QueryDescriptor.Count; index++)
            {
                if (m_UsesStatistics1)
                {
                    Vortice.Direct3D12.QueryDataPipelineStatistics1 stats1 =
                        *(Vortice.Direct3D12.QueryDataPipelineStatistics1*)(bytes + (index * m_ResultStrideInBytes));
                    StoreStatistics(index, in stats1);
                }
                else
                {
                    Vortice.Direct3D12.QueryDataPipelineStatistics stats =
                        *(Vortice.Direct3D12.QueryDataPipelineStatistics*)(bytes + (index * m_ResultStrideInBytes));
                    StoreStatistics(index, in stats);
                }
            }
        }

        private void StoreStatistics(
            uint index,
            in Vortice.Direct3D12.QueryDataPipelineStatistics stats)
        {
            if (m_QueryDescriptor.Domain == ERHIPipelineStatisticsDomain.Compute)
            {
                m_ComputeStatistics[index] = new RHIComputePipelineStatistics(stats.CSInvocations);
                return;
            }

            m_RasterStatistics[index] = new RHIRasterPipelineStatistics(
                stats.IAVertices,
                stats.IAPrimitives,
                stats.VSInvocations,
                stats.GSInvocations,
                stats.GSPrimitives,
                stats.CInvocations,
                stats.CPrimitives,
                stats.PSInvocations,
                stats.HSInvocations,
                stats.DSInvocations,
                0,
                0,
                0);
        }

        private void StoreStatistics(
            uint index,
            in Vortice.Direct3D12.QueryDataPipelineStatistics1 stats)
        {
            if (m_QueryDescriptor.Domain == ERHIPipelineStatisticsDomain.Compute)
            {
                m_ComputeStatistics[index] = new RHIComputePipelineStatistics(stats.CSInvocations);
                return;
            }

            m_RasterStatistics[index] = new RHIRasterPipelineStatistics(
                stats.IAVertices,
                stats.IAPrimitives,
                stats.VSInvocations,
                stats.GSInvocations,
                stats.GSPrimitives,
                stats.CInvocations,
                stats.CPrimitives,
                stats.PSInvocations,
                stats.HSInvocations,
                stats.DSInvocations,
                stats.MSInvocations,
                stats.ASInvocations,
                stats.MSPrimitives);
        }

        private static bool UsesPipelineStatistics1(
            Dx12Device device,
            in RHIQueryDescriptor descriptor)
        {
            if (descriptor.Type != ERHIQueryType.Statistics)
            {
                return false;
            }

            const ERHIPipelineStatisticCounter meshBits =
                ERHIPipelineStatisticCounter.MeshShaderInvocations |
                ERHIPipelineStatisticCounter.TaskShaderInvocations |
                ERHIPipelineStatisticCounter.MeshShaderPrimitives;
            if ((descriptor.CounterMask & meshBits) == 0)
            {
                return false;
            }

            return device.Capabilities.Mesh.MeshShader.Tier != ERHICapabilityTier.Unavailable;
        }

        private static Vortice.Direct3D12.QueryType ConvertNativeQueryType(
            ERHIQueryType queryType,
            bool usesStatistics1)
        {
            return queryType switch
            {
                ERHIQueryType.Occlusion => Vortice.Direct3D12.QueryType.Occlusion,
                ERHIQueryType.Statistics => usesStatistics1
                    ? Vortice.Direct3D12.QueryType.PipelineStatistics1
                    : Vortice.Direct3D12.QueryType.PipelineStatistics,
                ERHIQueryType.TimestampTransfer => Vortice.Direct3D12.QueryType.Timestamp,
                ERHIQueryType.Timestamp => Vortice.Direct3D12.QueryType.Timestamp,
                _ => throw new ArgumentOutOfRangeException(nameof(queryType), queryType, "Unknown DX12 query type."),
            };
        }

        private static Vortice.Direct3D12.QueryHeapType ConvertNativeQueryHeapType(
            ERHIQueryType queryType,
            bool usesStatistics1)
        {
            return queryType switch
            {
                ERHIQueryType.Occlusion => Vortice.Direct3D12.QueryHeapType.Occlusion,
                ERHIQueryType.Statistics => usesStatistics1
                    ? Vortice.Direct3D12.QueryHeapType.PipelineStatistics1
                    : Vortice.Direct3D12.QueryHeapType.PipelineStatistics,
                ERHIQueryType.TimestampTransfer => Vortice.Direct3D12.QueryHeapType.CopyQueueTimestamp,
                ERHIQueryType.Timestamp => Vortice.Direct3D12.QueryHeapType.Timestamp,
                _ => throw new ArgumentOutOfRangeException(nameof(queryType), queryType, "Unknown DX12 query heap type."),
            };
        }

        private static uint GetResultStrideInBytes(ERHIQueryType queryType, bool usesStatistics1)
        {
            if (queryType != ERHIQueryType.Statistics)
            {
                return sizeof(ulong);
            }

            return usesStatistics1
                ? (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics1)
                : (uint)sizeof(Vortice.Direct3D12.QueryDataPipelineStatistics);
        }

        protected override void Release()
        {
            m_QueryHeap.Release();
            m_QueryResult.Release();
        }
    }
#pragma warning restore CA1416
}
