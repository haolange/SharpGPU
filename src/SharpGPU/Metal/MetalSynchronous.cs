using System;
using System.Threading;
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
        public MetalQuery(in RHIQueryDescriptor descriptor)
        {
            m_QueryDescriptor = descriptor;
            m_Results = new ulong[descriptor.Count];
            Results = new ReadOnlyMemory<ulong>(m_Results);
        }

        public override bool ResolveData()
        {
            return false;
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalStorageQueue : RHIStorageQueue
    {
        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        public override void Submit(RHIFence signalFence)
        {
            throw new NotSupportedException("Metal storage queue is not implemented.");
        }

        protected override void Release()
        {
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
