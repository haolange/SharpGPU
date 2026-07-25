using System;
using SharpGPU.Core;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    public enum ERHIQueryResultStatus : byte
    {
        Ready,
        NotReady
    }

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

    internal static class RHIQueryUseValidator
    {
        internal static TQuery Require<TQuery>(
            RHIQuery? query,
            RHIDevice ownerDevice,
            ERHIQueryType expectedType,
            uint index,
            string operation)
            where TQuery : RHIQuery
        {
            return RequireCore<TQuery>(
                query,
                ownerDevice,
                expectedType,
                null,
                index,
                operation);
        }

        internal static TQuery RequireTimestamp<TQuery>(
            RHIQuery? query,
            RHIDevice ownerDevice,
            bool allowTransferTimestamp,
            uint index,
            string operation)
            where TQuery : RHIQuery
        {
            return RequireCore<TQuery>(
                query,
                ownerDevice,
                ERHIQueryType.Timestamp,
                allowTransferTimestamp ? ERHIQueryType.TimestampTransfer : null,
                index,
                operation);
        }

        private static TQuery RequireCore<TQuery>(
            RHIQuery? query,
            RHIDevice ownerDevice,
            ERHIQueryType expectedType,
            ERHIQueryType? alternateType,
            uint index,
            string operation)
            where TQuery : RHIQuery
        {
            ArgumentNullException.ThrowIfNull(ownerDevice);
            if (query == null)
            {
                throw new InvalidOperationException(
                    $"{operation} requires the corresponding query in the active pass descriptor.");
            }
            if (query is not TQuery typedQuery)
            {
                throw new InvalidOperationException(
                    $"{operation} received a query from a different backend.");
            }
            if (!ReferenceEquals(typedQuery.OwnerDevice, ownerDevice))
            {
                throw new InvalidOperationException(
                    $"{operation} received a query owned by a different device.");
            }

            RHIQueryDescriptor descriptor = typedQuery.QueryDescriptor;
            if (descriptor.Type != expectedType &&
                (!alternateType.HasValue || descriptor.Type != alternateType.Value))
            {
                string expected = alternateType.HasValue
                    ? $"{expectedType} or {alternateType.Value}"
                    : expectedType.ToString();
                throw new InvalidOperationException(
                    $"{operation} requires a {expected} query, but received {descriptor.Type}.");
            }

            typedQuery.ValidateRange(index, 1);
            return typedQuery;
        }
    }
    public abstract class RHIQuery : Disposal
    {
        public ReadOnlyMemory<ulong> Results
        {
            get
            {
                ThrowIfDisposed();
                return m_Results;
            }
        }

        public RHIQueryDescriptor QueryDescriptor
        {
            get
            {
                ThrowIfDisposed();
                return m_QueryDescriptor;
            }
        }

        internal RHIDevice OwnerDevice =>
            m_Device ?? throw new InvalidOperationException("The query is not attached to an RHI device.");

        protected ulong[] m_Results = Array.Empty<ulong>();
        protected RHIQueryDescriptor m_QueryDescriptor;
        protected RHIDevice? m_Device;

        internal void ValidateRange(uint startIndex, uint queryCount)
        {
            ThrowIfDisposed();
            if (queryCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(queryCount), "A query range must contain at least one query.");
            }

            ulong endIndex = (ulong)startIndex + queryCount;
            if (endIndex > m_QueryDescriptor.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(queryCount), "The query range exceeds the query count.");
            }
        }

        internal void ValidateType(ERHIQueryType expectedType)
        {
            ThrowIfDisposed();
            if (m_QueryDescriptor.Type != expectedType)
            {
                throw new InvalidOperationException(
                    $"The query type is {m_QueryDescriptor.Type}, but {expectedType} is required.");
            }
        }

        public abstract ERHIQueryResultStatus ResolveData();
    }
}