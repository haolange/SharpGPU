using System;
using SharpMetal.Metal;
using System.Threading;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
    internal sealed class MetalFence : RHIFence
    {
        internal MTLSharedEvent NativeEvent => m_NativeEvent;

        private MTLSharedEvent m_NativeEvent;
        private long m_NextSignalValue;
        private long m_TargetValue;

        internal MetalFence(MetalDevice device) : base(device)
        {
            m_NativeEvent = device.NativeDevice.NewSharedEvent();
            if (m_NativeEvent.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal completion fences require MTLSharedEvent support.");
            }

            m_NextSignalValue = 0;
            m_TargetValue = 0;
        }

        public override EFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return EFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return EFenceStatus.NotReady;
                }

                ulong targetValue = (ulong)Volatile.Read(ref m_TargetValue);
                bool complete = targetValue != 0 && m_NativeEvent.SignaledValue >= targetValue;
                if (complete)
                {
                    MarkSignaled();
                }

                return complete ? EFenceStatus.Success : EFenceStatus.NotReady;
            }
        }

        public override void Reset()
        {
            if (IsSignalPending)
            {
                _ = Status;
            }

            if (!BeginReset())
            {
                return;
            }

            CompleteReset();
        }

        public override EFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return EFenceStatus.Success;
            }

            ulong targetValue = (ulong)Volatile.Read(ref m_TargetValue);
            if (targetValue == 0)
            {
                throw new InvalidOperationException("The fence has no native signal value.");
            }

            ulong timeoutMilliseconds;
            if (timeoutNanoseconds == ulong.MaxValue)
            {
                timeoutMilliseconds = ulong.MaxValue;
            }
            else
            {
                timeoutMilliseconds = timeoutNanoseconds / 1_000_000UL;
                if ((timeoutNanoseconds % 1_000_000UL) != 0)
                {
                    ++timeoutMilliseconds;
                }
            }

            m_NativeEvent.WaitUntilSignaledValue(targetValue, timeoutMilliseconds);
            if (m_NativeEvent.SignaledValue < targetValue)
            {
                return EFenceStatus.NotReady;
            }

            MarkSignaled();
            return EFenceStatus.Success;
        }

        internal ulong PrepareSignalValue()
        {
            if (!IsSignalPending)
            {
                throw new InvalidOperationException("The fence signal must be reserved before obtaining a native value.");
            }

            long signalValue = Interlocked.Increment(ref m_NextSignalValue);
            Volatile.Write(ref m_TargetValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            if (m_NativeEvent.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeEvent);
                m_NativeEvent = default;
            }
        }
    }

    internal sealed class MetalSemaphore : RHISemaphore
    {
        public MTLSharedEvent NativeEvent => m_NativeEvent;
        internal ulong LastSignaledValue => (ulong)Volatile.Read(ref m_LastSignaledValue);

        private MTLSharedEvent m_NativeEvent;
        private long m_NextSignalValue;
        private long m_LastSignaledValue;

        internal MetalSemaphore(MetalDevice device) : base(device)
        {
            m_NativeEvent = device.NativeDevice.NewSharedEvent();
            if (m_NativeEvent.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal binary semaphores require MTLSharedEvent support.");
            }

            m_NextSignalValue = 0;
            m_LastSignaledValue = 0;
        }

        internal ulong PrepareSignalValue()
        {
            long signalValue = Interlocked.Increment(ref m_NextSignalValue);
            Volatile.Write(ref m_LastSignaledValue, signalValue);
            return (ulong)signalValue;
        }

        protected override void Release()
        {
            if (m_NativeEvent.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeEvent);
                m_NativeEvent = default;
            }
        }
    }

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
            m_Results = new ulong[descriptor.Count];
            m_IsTimestampQuery = descriptor.Type == ERHIQueryType.TimestampTransfer || descriptor.Type == ERHIQueryType.Timestamp;
            m_IsOcclusionQuery = descriptor.Type == ERHIQueryType.Occlusion;
            m_IsStatisticsQuery = descriptor.Type == ERHIQueryType.Statistics;

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

        private ERHIQueryResultStatus ResolveStatisticsData()
        {
            if (m_StatisticsSampleBuffer.NativePtr == IntPtr.Zero || m_Results == null)
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
            for (int i = 0; i < m_Results.Length; ++i)
            {
                IntPtr beginPtr = IntPtr.Add(bytes, checked(i * 2 * resultSize));
                IntPtr endPtr = IntPtr.Add(bytes, checked((i * 2 + 1) * resultSize));
                MTLCounterResultStatistic begin = Marshal.PtrToStructure<MTLCounterResultStatistic>(beginPtr);
                MTLCounterResultStatistic end = Marshal.PtrToStructure<MTLCounterResultStatistic>(endPtr);
                m_Results[i] = end.fragmentInvocations >= begin.fragmentInvocations
                    ? end.fragmentInvocations - begin.fragmentInvocations
                    : end.fragmentsPassed;
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

    internal sealed class MetalHeap : RHIHeap
    {
        internal SharpMetal.Metal.MTLHeap NativeHeap { get { ThrowIfDisposed(); return m_NativeHeap; } }

        private readonly MetalDevice m_MetalDevice;
        private SharpMetal.Metal.MTLHeap m_NativeHeap;

        internal MetalHeap(MetalDevice device, in RHIHeapDescription descriptor)
            : this(device, descriptor, MTLHeapType.Placement)
        {
        }

        internal MetalHeap(MetalDevice device, in RHIHeapDescription descriptor, MTLHeapType heapType)
            : base(device, descriptor, 1UL)
        {
            m_MetalDevice = device;

            MTLHeapDescriptor nativeDescriptor = MTLHeapDescriptor.New();
            nativeDescriptor.Size = descriptor.Size;
            nativeDescriptor.Type = heapType;
            nativeDescriptor.ResourceOptions = MetalUtility.ConvertToMetalResourceOptions(descriptor.StorageMode);
            nativeDescriptor.StorageMode = (MTLStorageMode)(((ulong)nativeDescriptor.ResourceOptions >> 4) & 0xF);
            nativeDescriptor.CpuCacheMode = (MTLCPUCacheMode)((ulong)nativeDescriptor.ResourceOptions & 0xF);
            if (MetalSparseMemoryUtility.RequiresPlacementSparseCompatibility(
                    descriptor.Compatibility))
            {
                nativeDescriptor.MaxCompatiblePlacementSparsePageSize =
                    MetalSparseMemoryUtility.SparsePageSize;
            }
            else if (descriptor.Compatibility.NativeAllocationFlags != 0)
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
                throw new ArgumentException(
                    "The Metal heap compatibility contains unknown native allocation flags.",
                    nameof(descriptor));
            }

            try
            {
                m_NativeHeap = device.NativeDevice.NewHeap(nativeDescriptor);
            }
            finally
            {
                ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            }

            if (m_NativeHeap.NativePtr == IntPtr.Zero)
            {
                throw new RHIException(
                    ERHIErrorCode.OutOfMemory,
                    ERHIBackend.Metal,
                    0,
                    "MTLDevice failed to create a placement heap.",
                    ERHIDeviceState.Operational);
            }
        }

        protected override void Release()
        {
            if (m_NativeHeap.NativePtr != IntPtr.Zero)
            {
                m_MetalDevice.RemoveResidencyAllocation(m_NativeHeap);
                ObjectiveCRuntime.Release(m_NativeHeap);
                m_NativeHeap = default;
            }
        }
    }
}
