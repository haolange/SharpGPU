using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
    internal sealed class MetalQuery : RHIQuery
    {
        private readonly bool m_IsTimestampQuery;
        private readonly bool m_IsOcclusionQuery;
        private readonly bool m_IsStatisticsQuery;
        private readonly ulong m_ResultStrideInBytes;
        private readonly ulong m_TimestampFrequency;
        private MTL4CounterHeap m_CounterHeap;
        private MTLBuffer m_ResultBuffer;
        private MTLCounterSampleBuffer m_StatisticsSampleBuffer;

        public MetalQuery(MetalDevice device, in RHIQueryDescriptor descriptor)
        {
            m_Device = device;
            m_QueryDescriptor = descriptor;
            m_IsTimestampQuery = descriptor.Type == ERHIQueryType.TimestampTransfer || descriptor.Type == ERHIQueryType.Timestamp;
            m_IsOcclusionQuery = descriptor.Type == ERHIQueryType.Occlusion;
            m_IsStatisticsQuery = descriptor.Type == ERHIQueryType.Statistics;
            if (m_IsStatisticsQuery)
            {
                m_RasterStatistics = new RHIRasterPipelineStatistics[descriptor.Count];
            }
            else
            {
                m_Results = new ulong[descriptor.Count];
            }

            if (m_IsOcclusionQuery)
            {
                m_ResultStrideInBytes = sizeof(ulong);
                m_ResultBuffer = device.NativeDevice.NewBuffer(
                    m_ResultStrideInBytes * descriptor.Count,
                    MTLResourceOptions.ResourceStorageModeShared);
                if (m_ResultBuffer.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal occlusion query visibility result buffer.");
                }
                return;
            }

            if (m_IsStatisticsQuery)
            {
                if (!MetalDevice.TryGetStatisticsCounterSet(device.NativeDevice, out MTLCounterSet counterSet))
                {
                    throw new NotSupportedException("Metal pipeline statistics queries require a device statistics counter set.");
                }

                MTLCounterSampleBufferDescriptor sampleDescriptor = MTLCounterSampleBufferDescriptor.New();
                NSError sampleError = default;
                try
                {
                    sampleDescriptor.CounterSet = counterSet;
                    sampleDescriptor.StorageMode = MTLStorageMode.Shared;
                    sampleDescriptor.SampleCount = (ulong)descriptor.Count * 2;
                    m_StatisticsSampleBuffer = device.NativeDevice.NewCounterSampleBuffer(sampleDescriptor, ref sampleError);
                    if (m_StatisticsSampleBuffer.NativePtr == IntPtr.Zero)
                    {
                        throw new NotSupportedException("Failed to create Metal pipeline statistics counter sample buffer.");
                    }
                }
                finally
                {
                    if (sampleDescriptor.NativePtr != IntPtr.Zero)
                    {
                        ObjectiveCRuntime.Release(sampleDescriptor);
                    }
                }
                return;
            }

            if (!m_IsTimestampQuery)
            {
                return;
            }

            MTL4CounterHeapDescriptor counterHeapDescriptor = MTL4CounterHeapDescriptor.New();
            NSError error = default;
            try
            {
                counterHeapDescriptor.Type = MTL4CounterHeapType.Timestamp;
                counterHeapDescriptor.Count = descriptor.Count;

                m_CounterHeap = device.NativeDevice.NewCounterHeap(counterHeapDescriptor, ref error);
                if (m_CounterHeap.NativePtr == IntPtr.Zero)
                {
                    throw new NotSupportedException("Metal timestamp queries require MTL4CounterHeap support.");
                }

                m_ResultStrideInBytes = Math.Max(
                    device.NativeDevice.SizeOfCounterHeapEntry(MTL4CounterHeapType.Timestamp),
                    (ulong)Marshal.SizeOf<MTL4TimestampHeapEntry>());
                m_ResultBuffer = device.NativeDevice.NewBuffer(
                    m_ResultStrideInBytes * descriptor.Count,
                    MTLResourceOptions.ResourceStorageModeShared);
                if (m_ResultBuffer.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal timestamp query readback buffer.");
                }

                m_TimestampFrequency = device.NativeDevice.QueryTimestampFrequency();
            }
            finally
            {
                if (counterHeapDescriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(counterHeapDescriptor);
                }
            }
        }

        internal MTLBuffer VisibilityResultBuffer
        {
            get
            {
                if (!m_IsOcclusionQuery || m_ResultBuffer.NativePtr == IntPtr.Zero)
                {
                    throw new NotSupportedException("Metal occlusion query visibility result buffer is not available.");
                }

                return m_ResultBuffer;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteTimestamp(MTL4ComputeCommandEncoder encoder, uint index)
        {
            RequireTimestampQuery(index);
            encoder.WriteTimestampWithGranularity(MTL4TimestampGranularity.Precise, m_CounterHeap, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteTimestamp(MTL4RenderCommandEncoder encoder, ulong afterStage, uint index)
        {
            RequireTimestampQuery(index);
            encoder.WriteTimestamp((long)MTL4TimestampGranularity.Precise, afterStage, m_CounterHeap.NativePtr, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteTimestamp(MetalCommandBuffer commandBuffer, uint index)
        {
            RequireTimestampQuery(index);
            commandBuffer.EnsureMtl4CommandBuffer().WriteTimestampIntoHeap(m_CounterHeap, index);
        }

        internal void Resolve(MetalCommandBuffer commandBuffer, uint startIndex, uint queriesCount)
        {
            if (!m_IsTimestampQuery)
            {
                ValidateRange(startIndex, queriesCount);
                return;
            }

            RequireTimestampQuery(startIndex);
            if (queriesCount == 0)
            {
                return;
            }

            ulong endIndex = (ulong)startIndex + queriesCount;
            if (endIndex > m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queriesCount), "Metal query resolve range exceeds the query heap count.");
            }
        }

        public override ERHIQueryResultStatus ResolveData()
        {
            if (m_IsOcclusionQuery)
            {
                return ResolveOcclusionData();
            }

            if (m_IsStatisticsQuery)
            {
                return ResolveStatisticsData();
            }

            if (!m_IsTimestampQuery)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            if (m_Results == null || m_Results.Length == 0)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            NSData data = new NSData(m_CounterHeap.ResolveCounterRange(new NSRange
            {
                location = 0,
                length = (ulong)m_QueryDescriptor.Count,
            }));
            IntPtr bytes = data.NativePtr == IntPtr.Zero ? IntPtr.Zero : data.Bytes;
            if (bytes == IntPtr.Zero)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            ulong expectedBytes = m_ResultStrideInBytes * (ulong)m_Results.Length;
            if (data.Length < expectedBytes)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            double timestampToNs = m_TimestampFrequency == 0 ? 1.0 : 1_000_000_000.0 / m_TimestampFrequency;
            for (int i = 0; i < m_Results.Length; ++i)
            {
                IntPtr entryPtr = IntPtr.Add(bytes, checked((int)(m_ResultStrideInBytes * (ulong)i)));
                MTL4TimestampHeapEntry entry = Marshal.PtrToStructure<MTL4TimestampHeapEntry>(entryPtr);
                m_Results[i] = (ulong)(entry.Timestamp * timestampToNs);
            }


            return ERHIQueryResultStatus.Ready;
        }

        internal void BeginOcclusion(MTL4RenderCommandEncoder encoder, uint index)
        {
            RequireOcclusionQuery(index);
            encoder.SetVisibilityResultMode(MTLVisibilityResultMode.Counting, (ulong)index * sizeof(ulong));
        }

        internal void EndOcclusion(MTL4RenderCommandEncoder encoder, uint index)
        {
            RequireOcclusionQuery(index);
            encoder.SetVisibilityResultMode(MTLVisibilityResultMode.Disabled, (ulong)index * sizeof(ulong));
        }

        internal void BeginStatistics(MTL4RenderCommandEncoder encoder, uint index)
        {
            RequireStatisticsQuery(index);
            new MTLRenderCommandEncoder(encoder.NativePtr).SampleCountersInBuffer(m_StatisticsSampleBuffer, (ulong)index * 2, true);
        }

        internal void EndStatistics(MTL4RenderCommandEncoder encoder, uint index)
        {
            RequireStatisticsQuery(index);
            new MTLRenderCommandEncoder(encoder.NativePtr).SampleCountersInBuffer(m_StatisticsSampleBuffer, (ulong)index * 2 + 1, true);
        }

        private void RequireTimestampQuery(uint index)
        {
            if (!m_IsTimestampQuery)
            {
                throw new InvalidOperationException("Metal query is not a timestamp query.");
            }

            if (m_CounterHeap.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal timestamp query heap is not available on this device.");
            }

            if (index >= m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Metal timestamp query index exceeds the query heap count.");
            }
        }

        private void RequireOcclusionQuery(uint index)
        {
            if (!m_IsOcclusionQuery)
            {
                throw new InvalidOperationException("Metal query is not an occlusion query.");
            }

            if (index >= m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Metal occlusion query index exceeds the query heap count.");
            }
        }

        private void RequireStatisticsQuery(uint index)
        {
            if (!m_IsStatisticsQuery)
            {
                throw new InvalidOperationException("Metal query is not a pipeline statistics query.");
            }

            if (m_StatisticsSampleBuffer.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal pipeline statistics counter sample buffer is not available.");
            }

            if (index >= m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Metal statistics query index exceeds the query heap count.");
            }
        }


        private ERHIQueryResultStatus ResolveOcclusionData()
        {
            if (m_ResultBuffer.NativePtr == IntPtr.Zero || m_Results == null)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            IntPtr contents = m_ResultBuffer.Contents;
            if (contents == IntPtr.Zero)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            for (int i = 0; i < m_Results.Length; ++i)
            {
                m_Results[i] = (ulong)Marshal.ReadInt64(contents, i * sizeof(ulong));
            }

            return ERHIQueryResultStatus.Ready;
        }

        private static ulong Delta(ulong begin, ulong end)
        {
            return end >= begin ? end - begin : 0;
        }

        private ERHIQueryResultStatus ResolveStatisticsData()
        {
            if (m_StatisticsSampleBuffer.NativePtr == IntPtr.Zero ||
                m_RasterStatistics.Length == 0)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            NSData data = m_StatisticsSampleBuffer.ResolveCounterRange(new NSRange
            {
                location = 0,
                length = (ulong)m_QueryDescriptor.Count * 2
            });
            IntPtr bytes = data.NativePtr == IntPtr.Zero ? IntPtr.Zero : data.Bytes;
            if (bytes == IntPtr.Zero)
            {
                return ERHIQueryResultStatus.NotReady;
            }

            int resultSize = Marshal.SizeOf<MTLCounterResultStatistic>();
            for (int i = 0; i < m_RasterStatistics.Length; ++i)
            {
                IntPtr beginPtr = IntPtr.Add(bytes, checked(i * 2 * resultSize));
                IntPtr endPtr = IntPtr.Add(bytes, checked((i * 2 + 1) * resultSize));
                MTLCounterResultStatistic begin = Marshal.PtrToStructure<MTLCounterResultStatistic>(beginPtr);
                MTLCounterResultStatistic end = Marshal.PtrToStructure<MTLCounterResultStatistic>(endPtr);
                m_RasterStatistics[i] = new RHIRasterPipelineStatistics(
                    0,
                    0,
                    Delta(begin.vertexInvocations, end.vertexInvocations),
                    0,
                    0,
                    Delta(begin.clipperInvocations, end.clipperInvocations),
                    Delta(begin.clipperPrimitivesOut, end.clipperPrimitivesOut),
                    Delta(begin.fragmentInvocations, end.fragmentInvocations),
                    Delta(begin.tessellationInputPatches, end.tessellationInputPatches),
                    Delta(begin.postTessellationVertexInvocations, end.postTessellationVertexInvocations),
                    0,
                    0,
                    0);
            }

            return ERHIQueryResultStatus.Ready;
        }

        protected override void Release()
        {
            if (m_ResultBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_ResultBuffer);
                m_ResultBuffer = default;
            }

            if (m_CounterHeap.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_CounterHeap);
                m_CounterHeap = default;
            }

            if (m_StatisticsSampleBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_StatisticsSampleBuffer);
                m_StatisticsSampleBuffer = default;
            }
        }
    }
}
