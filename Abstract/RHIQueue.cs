using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIQueueDescriptor : IEquatable<RHIQueueDescriptor>
    {
        public uint Count;
        public EQueueType Type;

        public bool Equals(RHIQueueDescriptor other) => (Type == other.Type) && (Count == other.Count);

        public override bool Equals(object? obj)
        {
            return (obj != null) ? Equals((RHIQueueDescriptor)obj) : false;
        }

        public override int GetHashCode() => new uint2(Count, (uint)Type).GetHashCode();

        public static bool operator ==(in RHIQueueDescriptor value1, in RHIQueueDescriptor value2) => value1.Equals(value2);

        public static bool operator !=(in RHIQueueDescriptor value1, in RHIQueueDescriptor value2) => !value1.Equals(value2);
    }

    public struct RHISubmitDescriptor
    {
        public RHISemaphore[] WaitSemaphores;
        public RHISemaphore[] SignalSemaphores;
        public RHICommandBuffer[] CommandBuffers;
    }

    public abstract class RHIQueue : Disposal
    {
        public EQueueType Type
        {
            get
            {
                return m_Type;
            }
        }
        public abstract ulong Frequency
        {
            get;
        }

        protected EQueueType m_Type;
        public abstract RHICommandAllocator CreateCommandAllocator();
        public abstract void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore);
        public abstract void Submit(in RHISubmitDescriptor submitDescriptor, RHIFence signalFence);
    }
}
