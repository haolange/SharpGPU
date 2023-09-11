using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIQueryDescriptor : IEquatable<RHIQueryDescriptor>
    {
        public uint Count;
        public ERHIQueryType Type;

        public bool Equals(RHIQueryDescriptor other) => (Type == other.Type) && (Count == other.Count);

        public override bool Equals(object? obj)
        {
            return (obj != null) ? Equals((RHIQueryDescriptor)obj) : false;
        }

        public override int GetHashCode() => new uint2(Count, (uint)Type).GetHashCode();

        public static bool operator == (in RHIQueryDescriptor value1, in RHIQueryDescriptor value2) => value1.Equals(value2);

        public static bool operator != (in RHIQueryDescriptor value1, in RHIQueryDescriptor value2) => !value1.Equals(value2);
    }

    public abstract class RHIQuery : Disposal
    {
        public ReadOnlyMemory<ulong> Results;
        public RHIQueryDescriptor QueryDescriptor  => m_QueryDescriptor;

        protected ulong[]? m_Results;

        protected RHIQueryDescriptor m_QueryDescriptor;

        public abstract bool ResolveData();
    }
}