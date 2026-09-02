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

    public enum ERHITimeDomain : byte
    {
        Device,
        QueryPerformanceCounter,
        ClockMonotonic,
        Pending
    }

    public readonly struct RHIClockCalibration
    {
        public ulong GpuTimestamp { get; }
        public ulong CpuTimestamp { get; }
        public ulong GpuTimestampFrequency { get; }
        public ERHITimeDomain TimeDomain { get; }
        public ERHIPipelineType Queue { get; }
        public int QueueIndex { get; }
        /// <summary>
        /// Maximum deviation reported by the native calibrated-timestamp API.
        /// Zero means the backend does not report a deviation (D3D12
        /// <c>ID3D12CommandQueue.GetClockCalibration</c> has no max-deviation
        /// output). Do not treat zero as a proven nanosecond bound.
        /// </summary>
        public ulong MaxDeviation { get; }

        public RHIClockCalibration(
            ulong gpuTimestamp,
            ulong cpuTimestamp,
            ulong gpuTimestampFrequency,
            ERHITimeDomain timeDomain,
            ERHIPipelineType queue,
            int queueIndex,
            ulong maxDeviation)
        {
            if (timeDomain == ERHITimeDomain.Pending ||
                !Enum.IsDefined(timeDomain))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeDomain),
                    timeDomain,
                    "Clock calibration requires a concrete time domain.");
            }
            if (queue == ERHIPipelineType.Pending ||
                !Enum.IsDefined(queue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queue),
                    queue,
                    "Clock calibration requires a concrete queue type.");
            }
            if (queueIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(queueIndex),
                    queueIndex,
                    "Clock calibration queue index must not be negative.");
            }
            if (gpuTimestampFrequency == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gpuTimestampFrequency),
                    gpuTimestampFrequency,
                    "Clock calibration requires a non-zero GPU timestamp frequency.");
            }

            GpuTimestamp = gpuTimestamp;
            CpuTimestamp = cpuTimestamp;
            GpuTimestampFrequency = gpuTimestampFrequency;
            TimeDomain = timeDomain;
            Queue = queue;
            QueueIndex = queueIndex;
            MaxDeviation = maxDeviation;
        }
    }

    public enum ERHIPipelineStatisticsDomain : byte
    {
        Raster,
        Compute,
        RayTracing
    }

    [Flags]
    public enum ERHIPipelineStatisticCounter : ulong
    {
        None = 0,
        InputAssemblyVertices = 1UL << 0,
        InputAssemblyPrimitives = 1UL << 1,
        VertexShaderInvocations = 1UL << 2,
        GeometryShaderInvocations = 1UL << 3,
        GeometryShaderPrimitives = 1UL << 4,
        ClipperInvocations = 1UL << 5,
        ClipperPrimitives = 1UL << 6,
        PixelShaderInvocations = 1UL << 7,
        HullShaderInvocations = 1UL << 8,
        DomainShaderInvocations = 1UL << 9,
        ComputeShaderInvocations = 1UL << 10,
        MeshShaderInvocations = 1UL << 11,
        TaskShaderInvocations = 1UL << 12,
        MeshShaderPrimitives = 1UL << 13
    }

    public readonly struct RHIRasterPipelineStatistics
    {
        public ulong InputAssemblyVertices { get; }
        public ulong InputAssemblyPrimitives { get; }
        public ulong VertexShaderInvocations { get; }
        public ulong GeometryShaderInvocations { get; }
        public ulong GeometryShaderPrimitives { get; }
        public ulong ClipperInvocations { get; }
        public ulong ClipperPrimitives { get; }
        public ulong PixelShaderInvocations { get; }
        public ulong HullShaderInvocations { get; }
        public ulong DomainShaderInvocations { get; }
        public ulong MeshShaderInvocations { get; }
        public ulong TaskShaderInvocations { get; }
        public ulong MeshShaderPrimitives { get; }

        public RHIRasterPipelineStatistics(
            ulong inputAssemblyVertices,
            ulong inputAssemblyPrimitives,
            ulong vertexShaderInvocations,
            ulong geometryShaderInvocations,
            ulong geometryShaderPrimitives,
            ulong clipperInvocations,
            ulong clipperPrimitives,
            ulong pixelShaderInvocations,
            ulong hullShaderInvocations,
            ulong domainShaderInvocations,
            ulong meshShaderInvocations,
            ulong taskShaderInvocations,
            ulong meshShaderPrimitives)
        {
            InputAssemblyVertices = inputAssemblyVertices;
            InputAssemblyPrimitives = inputAssemblyPrimitives;
            VertexShaderInvocations = vertexShaderInvocations;
            GeometryShaderInvocations = geometryShaderInvocations;
            GeometryShaderPrimitives = geometryShaderPrimitives;
            ClipperInvocations = clipperInvocations;
            ClipperPrimitives = clipperPrimitives;
            PixelShaderInvocations = pixelShaderInvocations;
            HullShaderInvocations = hullShaderInvocations;
            DomainShaderInvocations = domainShaderInvocations;
            MeshShaderInvocations = meshShaderInvocations;
            TaskShaderInvocations = taskShaderInvocations;
            MeshShaderPrimitives = meshShaderPrimitives;
        }
    }

    public readonly struct RHIComputePipelineStatistics
    {
        public ulong ComputeShaderInvocations { get; }

        public RHIComputePipelineStatistics(ulong computeShaderInvocations)
        {
            ComputeShaderInvocations = computeShaderInvocations;
        }
    }

    public readonly struct RHIRayTracingPipelineStatistics
    {
        public RHIRayTracingPipelineStatistics()
        {
        }
    }

    public struct RHIQueryDescriptor : IEquatable<RHIQueryDescriptor>
    {
        public uint Count;
        public ERHIQueryType Type;
        public ERHIPipelineStatisticsDomain Domain;
        public ERHIPipelineStatisticCounter CounterMask;

        public bool Equals(RHIQueryDescriptor other) =>
            Type == other.Type &&
            Count == other.Count &&
            Domain == other.Domain &&
            CounterMask == other.CounterMask;

        public override bool Equals(object? obj)
        {
            return obj is RHIQueryDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Count, (uint)Type, (byte)Domain, (ulong)CounterMask);
        }

        public static bool operator ==(in RHIQueryDescriptor value1, in RHIQueryDescriptor value2) =>
            value1.Equals(value2);

        public static bool operator !=(in RHIQueryDescriptor value1, in RHIQueryDescriptor value2) =>
            !value1.Equals(value2);
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
            return RequireQuery<TQuery>(
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
            return RequireQuery<TQuery>(
                query,
                ownerDevice,
                ERHIQueryType.Timestamp,
                allowTransferTimestamp ? ERHIQueryType.TimestampTransfer : null,
                index,
                operation);
        }

        internal static TQuery RequireStatistics<TQuery>(
            RHIQuery? query,
            RHIDevice ownerDevice,
            ERHIPipelineStatisticsDomain expectedDomain,
            uint index,
            string operation)
            where TQuery : RHIQuery
        {
            TQuery typedQuery = RequireQuery<TQuery>(
                query,
                ownerDevice,
                ERHIQueryType.Statistics,
                null,
                index,
                operation);
            if (typedQuery.QueryDescriptor.Domain != expectedDomain)
            {
                throw new InvalidOperationException(
                    $"{operation} requires a {expectedDomain} statistics query, " +
                    $"but received {typedQuery.QueryDescriptor.Domain}.");
            }

            return typedQuery;
        }

        private static TQuery RequireQuery<TQuery>(
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
                if (m_QueryDescriptor.Type == ERHIQueryType.Statistics)
                {
                    throw new InvalidOperationException(
                        "Statistics queries expose typed domain results. " +
                        "Use TryGetRasterStatistics, TryGetComputeStatistics, " +
                        "or TryGetRayTracingStatistics.");
                }

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
        protected RHIRasterPipelineStatistics[] m_RasterStatistics =
            Array.Empty<RHIRasterPipelineStatistics>();
        protected RHIComputePipelineStatistics[] m_ComputeStatistics =
            Array.Empty<RHIComputePipelineStatistics>();
        protected RHIRayTracingPipelineStatistics[] m_RayTracingStatistics =
            Array.Empty<RHIRayTracingPipelineStatistics>();
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

        public bool TryGetRasterStatistics(
            uint index,
            out RHIRasterPipelineStatistics statistics)
        {
            ThrowIfDisposed();
            RequireStatisticsDomain(ERHIPipelineStatisticsDomain.Raster);
            ValidateRange(index, 1);
            if (m_RasterStatistics.Length == 0 ||
                index >= (uint)m_RasterStatistics.Length)
            {
                statistics = default;
                return false;
            }

            statistics = m_RasterStatistics[index];
            return true;
        }

        public bool TryGetComputeStatistics(
            uint index,
            out RHIComputePipelineStatistics statistics)
        {
            ThrowIfDisposed();
            RequireStatisticsDomain(ERHIPipelineStatisticsDomain.Compute);
            ValidateRange(index, 1);
            if (m_ComputeStatistics.Length == 0 ||
                index >= (uint)m_ComputeStatistics.Length)
            {
                statistics = default;
                return false;
            }

            statistics = m_ComputeStatistics[index];
            return true;
        }

        public bool TryGetRayTracingStatistics(
            uint index,
            out RHIRayTracingPipelineStatistics statistics)
        {
            ThrowIfDisposed();
            RequireStatisticsDomain(ERHIPipelineStatisticsDomain.RayTracing);
            ValidateRange(index, 1);
            if (m_RayTracingStatistics.Length == 0 ||
                index >= (uint)m_RayTracingStatistics.Length)
            {
                statistics = default;
                return false;
            }

            statistics = m_RayTracingStatistics[index];
            return true;
        }

        private void RequireStatisticsDomain(ERHIPipelineStatisticsDomain domain)
        {
            if (m_QueryDescriptor.Type != ERHIQueryType.Statistics)
            {
                throw new InvalidOperationException(
                    $"Typed pipeline statistics require a Statistics query, but received {m_QueryDescriptor.Type}.");
            }

            if (m_QueryDescriptor.Domain != domain)
            {
                throw new InvalidOperationException(
                    $"The statistics query domain is {m_QueryDescriptor.Domain}, but {domain} was requested.");
            }
        }

        public abstract ERHIQueryResultStatus ResolveData();
    }
}