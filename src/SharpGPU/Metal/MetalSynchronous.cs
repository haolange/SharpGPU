using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpMetal.ObjectiveCCore;
using SharpMetal.Metal;

namespace Infinity.Graphics
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
        // CPU-side timestamp tracking for queries.
        // Metal does not expose a simple readback heap like D3D12; real GPU counters
        // require MTLCounterSampleBuffer (or MTL4CounterHeap), which in turn need
        // a counter set obtained from MTLDevice.counterSets. Because SharpMetal
        // bindings do not guarantee all counter APIs are reachable on every host
        // (e.g. iOS simulators, non-Apple CI), we implement a CPU-side fallback
        // that records Stopwatch ticks at BeginQuery/EndQuery boundaries.
        //
        // When real counter sample buffers are available the engine should prefer
        // using them via the command encoder; the CPU timestamps here are a
        // best-effort stand-in that makes ResolveData() return meaningful values.

        private readonly long[] m_CpuTimestamps;
        private readonly object m_Lock;

        public MetalQuery(in RHIQueryDescriptor descriptor)
        {
            m_QueryDescriptor = descriptor;
            m_Results = new ulong[descriptor.Count];
            Results = new ReadOnlyMemory<ulong>(m_Results);
            m_CpuTimestamps = new long[descriptor.Count];
            m_Lock = new object();
        }

        /// <summary>
        /// Record a CPU timestamp for the given query index.
        /// Called by the command encoder at BeginQuery / EndQuery time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void RecordTimestamp(uint index)
        {
            if (index < (uint)m_CpuTimestamps.Length)
            {
                m_CpuTimestamps[index] = Stopwatch.GetTimestamp();
            }
        }

        public override bool ResolveData()
        {
            if (m_Results == null || m_Results.Length == 0)
            {
                return false;
            }

            lock (m_Lock)
            {
                // Convert Stopwatch ticks → nanoseconds, matching the convention
                // used by GPU timestamp queries on Metal (ns units).
                double ticksToNs = 1_000_000_000.0 / Stopwatch.Frequency;
                for (int i = 0; i < m_Results.Length; ++i)
                {
                    m_Results[i] = (ulong)(m_CpuTimestamps[i] * ticksToNs);
                }

                Results = new ReadOnlyMemory<ulong>(m_Results);
            }

            return true;
        }

        protected override void Release()
        {
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
        internal MetalHeap(in RHIHeapDescription descriptor)
        {
        }

        protected override void Release()
        {
        }
    }
}
