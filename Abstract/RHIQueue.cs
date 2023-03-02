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

    public abstract class RHIQueue : Disposal
    {
        public EQueueType Type
        {
            get
            {
                return m_Type;
            }
        }

        protected EQueueType m_Type;
        public abstract RHICommandPool CreateCommandPool();
        public abstract void Submit(RHICommandBuffer cmdBuffer, RHIFence fence);
    }
}
