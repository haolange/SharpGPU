using System;
using System.IO;
using SharpMetal.Metal;
using System.Threading;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace SharpGPU
{
    internal sealed class MetalFence : RHIFence
    {
        private readonly ManualResetEventSlim m_Event;

        internal MetalFence()
        {
            m_Event = new ManualResetEventSlim(false);
        }

        public override EFenceStatus Status => m_Event.IsSet ? EFenceStatus.Success : EFenceStatus.NotReady;

        public override void Reset()
        {
            m_Event.Reset();
        }

        public override void Wait()
        {
            m_Event.Wait();
        }

        internal void Signal()
        {
            m_Event.Set();
        }

        protected override void Release()
        {
            m_Event.Dispose();
        }
    }

    internal sealed class MetalSemaphore : RHISemaphore
    {
        public MTLSharedEvent NativeEvent => m_NativeEvent;

        private readonly object m_Lock;
        private MTLSharedEvent m_NativeEvent;
        private ulong m_Value;

        internal MetalSemaphore(MetalDevice device)
        {
            m_Lock = new object();
            m_NativeEvent = device.NativeDevice.NewSharedEvent();
            m_Value = 1;
        }

        internal ulong CurrentValue
        {
            get
            {
                lock (m_Lock)
                {
                    return m_Value;
                }
            }
        }

        internal ulong AcquireSignalValue()
        {
            lock (m_Lock)
            {
                ulong signalValue = m_Value;
                ++m_Value;
                return signalValue;
            }
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
            m_QueryDescriptor = descriptor;
            m_Results = new ulong[descriptor.Count];
            Results = new ReadOnlyMemory<ulong>(m_Results);
            m_IsTimestampQuery = descriptor.Type == ERHIQueryType.TimestampTransfer || descriptor.Type == ERHIQueryType.TimestampGenerice;
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

        public override bool ResolveData()
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
                return false;
            }

            if (m_Results == null || m_Results.Length == 0)
            {
                return false;
            }

            NSData data = new NSData(m_CounterHeap.ResolveCounterRange(new NSRange
            {
                location = 0,
                length = (ulong)m_QueryDescriptor.Count,
            }));
            IntPtr bytes = data.NativePtr == IntPtr.Zero ? IntPtr.Zero : data.Bytes;
            if (bytes == IntPtr.Zero)
            {
                return false;
            }

            ulong expectedBytes = m_ResultStrideInBytes * (ulong)m_Results.Length;
            if (data.Length < expectedBytes)
            {
                return false;
            }

            double timestampToNs = m_TimestampFrequency == 0 ? 1.0 : 1_000_000_000.0 / m_TimestampFrequency;
            for (int i = 0; i < m_Results.Length; ++i)
            {
                IntPtr entryPtr = IntPtr.Add(bytes, checked((int)(m_ResultStrideInBytes * (ulong)i)));
                MTL4TimestampHeapEntry entry = Marshal.PtrToStructure<MTL4TimestampHeapEntry>(entryPtr);
                m_Results[i] = (ulong)(entry.Timestamp * timestampToNs);
            }

            Results = new ReadOnlyMemory<ulong>(m_Results);

            return true;
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

        private void ValidateRange(uint startIndex, uint queriesCount)
        {
            ulong endIndex = (ulong)startIndex + queriesCount;
            if (endIndex > m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queriesCount), "Metal query resolve range exceeds the query heap count.");
            }
        }

        private bool ResolveOcclusionData()
        {
            if (m_ResultBuffer.NativePtr == IntPtr.Zero || m_Results == null)
            {
                return false;
            }

            IntPtr contents = m_ResultBuffer.Contents;
            if (contents == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < m_Results.Length; ++i)
            {
                m_Results[i] = (ulong)Marshal.ReadInt64(contents, i * sizeof(ulong));
            }

            Results = new ReadOnlyMemory<ulong>(m_Results);
            return true;
        }

        private bool ResolveStatisticsData()
        {
            if (m_StatisticsSampleBuffer.NativePtr == IntPtr.Zero || m_Results == null)
            {
                return false;
            }

            NSData data = m_StatisticsSampleBuffer.ResolveCounterRange(new NSRange
            {
                location = 0,
                length = (ulong)m_QueryDescriptor.Count * 2
            });
            IntPtr bytes = data.NativePtr == IntPtr.Zero ? IntPtr.Zero : data.Bytes;
            if (bytes == IntPtr.Zero)
            {
                return false;
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

            Results = new ReadOnlyMemory<ulong>(m_Results);
            return true;
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

    internal sealed class MetalStorageQueue : RHIStorageQueue
    {
        // Metal does not have a direct equivalent of DirectStorage (Win32 API).
        // We use managed System.IO file streams to open / read / close files and
        // batch the read requests into a pending list that is drained on Submit().
        //
        // File handles are represented as GCHandle-pinned FileStream references
        // stored inside the RHIStorageFileHandle.NativeHandle field. This avoids
        // any platform-specific P/Invoke while keeping the same abstract interface.

        private readonly List<Action> m_PendingRequests;
        private readonly Dictionary<IntPtr, FileStream> m_OpenFiles;

        public MetalStorageQueue()
        {
            m_PendingRequests = new List<Action>();
            m_OpenFiles = new Dictionary<IntPtr, FileStream>();
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            if (string.IsNullOrEmpty(absPath))
            {
                throw new ArgumentException("File path must not be null or empty.", nameof(absPath));
            }

            FileStream stream = new FileStream(absPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            GCHandle gcHandle = GCHandle.Alloc(stream);
            IntPtr handlePtr = GCHandle.ToIntPtr(gcHandle);
            m_OpenFiles[handlePtr] = stream;

            RHIStorageFileHandle result;
            result.NativeHandle = handlePtr;
            return result;
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            if (m_OpenFiles.TryGetValue(fileHandle.NativeHandle, out FileStream stream))
            {
                stream.Close();
                stream.Dispose();
                m_OpenFiles.Remove(fileHandle.NativeHandle);

                GCHandle gcHandle = GCHandle.FromIntPtr(fileHandle.NativeHandle);
                gcHandle.Free();
            }
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            if (m_OpenFiles.TryGetValue(fileHandle.NativeHandle, out FileStream stream))
            {
                return (ulong)stream.Length;
            }

            return 0;
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            // Capture the request value so the lambda does not capture the
            // by-ref parameter (which would be invalid after this method returns).
            RHIStorageBufferRequest capturedRequest = request;
            m_PendingRequests.Add(() =>
            {
                if (!m_OpenFiles.TryGetValue(capturedRequest.FileHandle.NativeHandle, out FileStream stream))
                {
                    return;
                }

                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                stream.Seek((long)capturedRequest.FileOffset, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < tempBuffer.Length)
                {
                    int bytesRead = stream.Read(tempBuffer, totalRead, tempBuffer.Length - totalRead);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }

                // Copy into the destination RHI buffer via Map/UnMap
                MetalBuffer metalBuffer = capturedRequest.DestinationBuffer as MetalBuffer;
                if (metalBuffer != null)
                {
                    unsafe
                    {
                        IntPtr dstPtr = metalBuffer.Map((uint)capturedRequest.DestinationOffset, (uint)(capturedRequest.DestinationOffset + capturedRequest.FileSize));
                        if (dstPtr != IntPtr.Zero)
                        {
                            Marshal.Copy(tempBuffer, 0, dstPtr, totalRead);
                            metalBuffer.UnMap((uint)capturedRequest.DestinationOffset, (uint)(capturedRequest.DestinationOffset + (ulong)totalRead));
                        }
                    }
                }
            });
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            RHIStorageTextureRequest capturedRequest = request;
            m_PendingRequests.Add(() =>
            {
                if (!m_OpenFiles.TryGetValue(capturedRequest.FileHandle.NativeHandle, out FileStream stream))
                {
                    return;
                }

                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                stream.Seek((long)capturedRequest.FileOffset, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < tempBuffer.Length)
                {
                    int bytesRead = stream.Read(tempBuffer, totalRead, tempBuffer.Length - totalRead);
                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }

                // For textures, we use the Metal replaceRegion approach via the
                // native MTLTexture API. Since MetalTexture wraps an MTLTexture,
                // we write into it using the CPU-accessible path.
                MetalTexture metalTexture = capturedRequest.DestinationTexture as MetalTexture;
                if (metalTexture != null)
                {
                    unsafe
                    {
                        fixed (byte* pData = tempBuffer)
                        {
                            uint width = (uint)metalTexture.NativeTexture.Width;
                            uint height = (uint)metalTexture.NativeTexture.Height;
                            // RGBA8 = 4 bytes per pixel
                            ulong bytesPerRow = width * 4u;

                            MTLRegion region = new MTLRegion();
                            region.origin = new MTLOrigin { x = 0, y = 0, z = 0 };
                            region.size = new MTLSize { width = width, height = height, depth = 1 };

                            metalTexture.NativeTexture.ReplaceRegion(
                                region,
                                capturedRequest.MipLevel,
                                capturedRequest.ArraySlice,
                                (IntPtr)pData,
                                bytesPerRow,
                                0);
                        }
                    }
                }
            });
        }

        public override void Submit(RHIFence signalFence)
        {
            // Execute all pending file-read requests
            foreach (Action request in m_PendingRequests)
            {
                request();
            }
            m_PendingRequests.Clear();

            // Signal the fence to indicate all requests have completed
            if (signalFence != null)
            {
                MetalFence metalFence = signalFence as MetalFence;
                metalFence?.Signal();
            }
        }

        protected override void Release()
        {
            m_PendingRequests.Clear();

            // Close any remaining open file handles
            foreach (KeyValuePair<IntPtr, FileStream> pair in m_OpenFiles)
            {
                pair.Value.Close();
                pair.Value.Dispose();

                GCHandle gcHandle = GCHandle.FromIntPtr(pair.Key);
                gcHandle.Free();
            }
            m_OpenFiles.Clear();
        }
    }

    internal sealed class MetalHeap : RHIHeap
    {
        internal SharpMetal.Metal.MTLHeap NativeHeap => m_NativeHeap;

        private readonly MetalDevice m_MetalDevice;
        private SharpMetal.Metal.MTLHeap m_NativeHeap;

        internal MetalHeap(MetalDevice device, in RHIHeapDescription descriptor)
        {
            m_MetalDevice = device;

            MTLHeapDescriptor nativeDescriptor = MTLHeapDescriptor.New();
            nativeDescriptor.Size = descriptor.Size;
            nativeDescriptor.Type = MTLHeapType.Placement;
            nativeDescriptor.ResourceOptions = MetalUtility.ConvertToMetalResourceOptions(descriptor.StorageMode);
            nativeDescriptor.StorageMode = (MTLStorageMode)(((ulong)nativeDescriptor.ResourceOptions >> 4) & 0xF);
            nativeDescriptor.CpuCacheMode = (MTLCPUCacheMode)((ulong)nativeDescriptor.ResourceOptions & 0xF);

            m_NativeHeap = device.NativeDevice.NewHeap(nativeDescriptor);
            ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);

            if (m_NativeHeap.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLHeap.");
            }
        }

        protected override void Release()
        {
            if (m_NativeHeap.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeHeap);
                m_NativeHeap = default;
            }
        }
    }
}
