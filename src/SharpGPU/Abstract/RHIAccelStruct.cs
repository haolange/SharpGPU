using SharpGPU.Core;
using SharpGPU.Mathematics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System;

namespace SharpGPU
{
    public enum ERHIAccelStructFlag
    {
        None = 0,
        AllowUpdate = 0x1,
        PerformUpdate = 0x2,
        MinimizeMemory = 0x4,
        PreferFastTrace = 0x8,
        PreferFastBuild = 0x10,
        AllowCompaction = 0x20,
        AllowDisableOmms = 0x40
    }

    public enum ERHIAccelStructGeometryFlag
    {
        None = 0,
        Opaque = 0x1,
        NoDuplicateAnyhitInverseOcation = 0x2
    }

    public enum ERHIAccelStructInstanceFlag
    {
        None = 0,
        TriangleCullDisable = 0x1,
        TriangleFrontCounterclockwise = 0x2,
        ForceOpaque = 0x4,
        ForceNonOpaque = 0x8,
        ForceOmm2State = 0x10,
        DisableOmms = 0x20
    }

    public enum ERHIOpacityMicromapFormat
    {
        Oc1_2State = 1,
        Oc1_4State = 2
    }

    public enum ERHIOpacityMicromapSpecialIndex
    {
        FullyTransparent = -1,
        FullyOpaque = -2,
        FullyUnknownTransparent = -3,
        FullyUnknownOpaque = -4
    }

    public enum ERHIAccelStructGeometryType : byte
    {
        AABB,
        Curves,
        Triangle
    }

    public enum ERHIAccelStructCurveType : byte
    {
        Round = 0,
        Flat = 1
    }

    public enum ERHIAccelStructCurveBasis : byte
    {
        BSpline = 0,
        CatmullRom = 1,
        Linear = 2,
        Bezier = 3
    }

    public enum ERHIAccelStructCurveEndCaps : byte
    {
        None = 0,
        Disk = 1,
        Sphere = 2
    }

    public class RHIAccelStructGeometry
    {
        public ERHIAccelStructGeometryType GeometryType;
        public ERHIAccelStructGeometryFlag GeometryFlag;
        public uint FunctionTableOffset;
    }

    public class RHIAccelStructAABBs : RHIAccelStructGeometry
    {
        public uint Count;
        public uint Stride;
        public uint Offset;
        public RHIBuffer? AABBBuffer;
    }

    public class RHIAccelStructCurves : RHIAccelStructGeometry
    {
        public RHIBuffer? ControlPointBuffer;
        public uint ControlPointOffset;
        public uint ControlPointCount;
        public uint ControlPointStride;
        public ERHIPixelFormat ControlPointFormat;

        public RHIBuffer? RadiusBuffer;
        public uint RadiusOffset;
        public uint RadiusStride;
        public ERHIPixelFormat RadiusFormat;

        public RHIBuffer? IndexBuffer;
        public uint IndexOffset;
        public ERHIBufferFormat IndexFormat;

        public uint SegmentCount;
        public uint SegmentControlPointCount;
        public ERHIAccelStructCurveType CurveType;
        public ERHIAccelStructCurveBasis CurveBasis;
        public ERHIAccelStructCurveEndCaps CurveEndCaps;
    }

    public class RHIAccelStructTriangles : RHIAccelStructGeometry
    {
        public uint IndexCount;
        public uint IndexOffset;
        public RHIBuffer? IndexBuffer;
        public ERHIBufferFormat IndexFormat;
        public uint VertexCount;
        public uint VertexStride;
        public uint VertexOffset;
        public RHIBuffer? VertexBuffer;
        public ERHIPixelFormat VertexFormat;
        public RHIOpacityMicromap? OpacityMicromap;
        public RHIBuffer? OpacityMicromapIndexBuffer;
        public uint OpacityMicromapIndexOffset;
        public uint OpacityMicromapIndexStride;
        public ERHIBufferFormat OpacityMicromapIndexFormat;
        public bool HasOpacityMicromapSpecialIndex;
        public ERHIOpacityMicromapSpecialIndex OpacityMicromapSpecialIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIOpacityMicromapUsageCount
    {
        public uint Count;
        public uint SubdivisionLevel;
        public ERHIOpacityMicromapFormat Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIOpacityMicromapBuildDescriptor
    {
        public ERHIAccelStructFlag Flag;
        public RHIOpacityMicromapUsageCount[] UsageCounts;
        public RHIBuffer? InputBuffer;
        public uint InputBufferOffset;
        public RHIBuffer? TriangleArrayBuffer;
        public uint TriangleArrayOffset;
        public uint TriangleArrayStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIOpacityMicromapMemoryRequirements
    {
        public ulong ResultSizeInBytes;
        public ulong ScratchSizeInBytes;
        public ulong UpdateScratchSizeInBytes;
    }

    public abstract class RHIOpacityMicromap : Disposal
    {
        public RHIOpacityMicromapBuildDescriptor Descriptor => m_Descriptor;

        protected RHIOpacityMicromapBuildDescriptor m_Descriptor;
    }

    internal static class RHIOpacityMicromapContract
    {
        public static void ValidateFormat(in ERHIOpacityMicromapFormat format)
        {
            if (format != ERHIOpacityMicromapFormat.Oc1_2State &&
                format != ERHIOpacityMicromapFormat.Oc1_4State)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Unknown opacity micromap format is fail-closed.");
            }
        }

        public static void ValidateBuildDescriptor(in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            if (descriptor.UsageCounts == null || descriptor.UsageCounts.Length == 0)
            {
                throw new ArgumentException(
                    "Opacity micromap build requires a non-empty usage-count histogram.",
                    nameof(descriptor));
            }

            if (descriptor.InputBuffer == null)
            {
                throw new ArgumentException(
                    "Opacity micromap build requires an input buffer of micromap arrays.",
                    nameof(descriptor));
            }

            if (descriptor.TriangleArrayBuffer == null)
            {
                throw new ArgumentException(
                    "Opacity micromap build requires a per-triangle description buffer.",
                    nameof(descriptor));
            }

            for (int i = 0; i < descriptor.UsageCounts.Length; ++i)
            {
                RHIOpacityMicromapUsageCount usage = descriptor.UsageCounts[i];
                if (usage.Count == 0)
                {
                    throw new ArgumentException(
                        $"Opacity micromap usage count at index {i} must be greater than zero.",
                        nameof(descriptor));
                }

                ValidateFormat(usage.Format);
            }

            RequireAccelStructBuildInputUsage(descriptor.InputBuffer, "InputBuffer");
            RequireAccelStructBuildInputUsage(descriptor.TriangleArrayBuffer, "TriangleArrayBuffer");
            RejectAllowDisableOmmsOnNonBlas(descriptor.Flag, "opacity micromap array descriptor");
        }

        public static RHICapability CreateUnavailableSerialization(string probeSource)
        {
            return RHICapability.Unavailable(
                "Opacity micromap serialize/deserialize is not implemented; SharpGPU does not claim blob compatibility.",
                ERHICapabilityProbeKind.BackendContract,
                probeSource);
        }

        public static void ValidateTriangleAttachment(in RHIAccelStructTriangles triangles)
        {
            if (triangles.OpacityMicromap == null)
            {
                if (triangles.OpacityMicromapIndexBuffer != null ||
                    triangles.HasOpacityMicromapSpecialIndex)
                {
                    throw new ArgumentException(
                        "Opacity micromap index or special-index attachment requires an RHIOpacityMicromap.");
                }

                return;
            }

            if (triangles.OpacityMicromapIndexBuffer == null &&
                !triangles.HasOpacityMicromapSpecialIndex)
            {
                throw new ArgumentException(
                    "Attaching an opacity micromap requires a per-triangle index buffer or a special-index constant.");
            }

            if (triangles.OpacityMicromapIndexBuffer != null)
            {
                RequireAccelStructBuildInputUsage(
                    triangles.OpacityMicromapIndexBuffer,
                    "OpacityMicromapIndexBuffer");
            }
        }

        public static void RequireAccelStructBuildInputUsage(RHIBuffer buffer, string bufferName)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if ((buffer.Descriptor.UsageFlag & ERHIBufferUsage.AccelStruct) != ERHIBufferUsage.AccelStruct)
            {
                throw new ArgumentException(
                    $"{bufferName} must include ERHIBufferUsage.AccelStruct so native backends can attach MICROMAP_BUILD_INPUT_READ_ONLY.",
                    nameof(buffer));
            }
        }

        public static bool AnyInstanceDisablesOmms(ReadOnlySpan<RHIAccelStructInstance> instances)
        {
            for (int i = 0; i < instances.Length; ++i)
            {
                if ((instances[i].Flag & ERHIAccelStructInstanceFlag.DisableOmms) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool BlasAllowsDisableOmms(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            return (descriptor.Flag & ERHIAccelStructFlag.AllowDisableOmms) != 0;
        }

        public static void ValidateDisableOmmsInstances(ReadOnlySpan<RHIAccelStructInstance> instances)
        {
            if (!AnyInstanceDisablesOmms(instances))
            {
                return;
            }

            for (int i = 0; i < instances.Length; ++i)
            {
                if ((instances[i].Flag & ERHIAccelStructInstanceFlag.DisableOmms) == 0)
                {
                    continue;
                }

                RHIBottomLevelAccelStruct? blas = instances[i].BottomLevelAccelStruct;
                if (blas == null || !BlasAllowsDisableOmms(blas.Descriptor))
                {
                    throw new InvalidOperationException(
                        "ERHIAccelStructInstanceFlag.DisableOmms requires the referenced BLAS to be created with ERHIAccelStructFlag.AllowDisableOmms.");
                }
            }
        }

        public static void ValidateTlasDescriptor(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            RejectAllowDisableOmmsOnNonBlas(descriptor.Flag, "TLAS descriptor");
            if (AnyInstanceUsesOmmFlags(descriptor.Instances.Span))
            {
                device.Capabilities.RayTracing.OpacityMicromap.Require("RayTracing.OpacityMicromap");
            }

            ValidateDisableOmmsInstances(descriptor.Instances.Span);
        }

        public static void ValidateBlasDescriptor(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (BlasAllowsDisableOmms(descriptor))
            {
                device.Capabilities.RayTracing.OpacityMicromap.Require("RayTracing.OpacityMicromap");
            }
        }

        private static bool AnyInstanceUsesOmmFlags(ReadOnlySpan<RHIAccelStructInstance> instances)
        {
            for (int i = 0; i < instances.Length; ++i)
            {
                ERHIAccelStructInstanceFlag flag = instances[i].Flag;
                if ((flag & ERHIAccelStructInstanceFlag.ForceOmm2State) != 0 ||
                    (flag & ERHIAccelStructInstanceFlag.DisableOmms) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RejectAllowDisableOmmsOnNonBlas(in ERHIAccelStructFlag flag, string surface)
        {
            if ((flag & ERHIAccelStructFlag.AllowDisableOmms) != 0)
            {
                throw new ArgumentException(
                    $"ERHIAccelStructFlag.AllowDisableOmms is BLAS-only and is not valid on a {surface}.");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructInstance
    {
        public uint InstanceID;
        public byte InstanceMask;
        public uint HitGroupIndex;
        public float4x4 TransformMatrix;
        public ERHIAccelStructInstanceFlag Flag;
        public RHIBottomLevelAccelStruct BottomLevelAccelStruct;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHITopLevelAccelStructDescriptor
    {
        public uint Offset;
        public ERHIAccelStructFlag Flag;
        public Memory<RHIAccelStructInstance> Instances;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIBottomLevelAccelStructDescriptor
    {
        public ERHIAccelStructFlag Flag;
        public RHIAccelStructGeometry[] Geometries;
    }

    public abstract class RHITopLevelAccelStruct : Disposal
    {
        public RHITopLevelAccelStructDescriptor Descriptor => m_Descriptor;

        protected RHITopLevelAccelStructDescriptor m_Descriptor;

        public abstract void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
    }

    public abstract class RHIBottomLevelAccelStruct : Disposal
    {
        public RHIBottomLevelAccelStructDescriptor Descriptor => m_Descriptor;

        protected RHIBottomLevelAccelStructDescriptor m_Descriptor;
    }
}

namespace SharpGPU
{
    public sealed class RHIRayRecordBuilder
    {
        private byte[] m_Buffer;
        private int m_Length;

        public int Length => m_Length;

        public RHIRayRecordBuilder(int initialCapacity = 64)
        {
            if (initialCapacity <= 0)
            {
                initialCapacity = 64;
            }

            m_Buffer = new byte[initialCapacity];
            m_Length = 0;
        }

        public void Clear()
        {
            m_Length = 0;
        }

        public void WriteU32(in uint value)
        {
            EnsureCapacity(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(m_Buffer.AsSpan(m_Length, sizeof(uint)), value);
            m_Length += sizeof(uint);
        }

        public void WriteU64(in ulong value)
        {
            EnsureCapacity(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(m_Buffer.AsSpan(m_Length, sizeof(ulong)), value);
            m_Length += sizeof(ulong);
        }

        public void WriteF32(in float value)
        {
            WriteU32(BitConverter.SingleToUInt32Bits(value));
        }

        public void WriteBytes(in ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return;
            }

            EnsureCapacity(bytes.Length);
            bytes.CopyTo(m_Buffer.AsSpan(m_Length));
            m_Length += bytes.Length;
        }

        public void WriteBytes(in ReadOnlyMemory<byte> bytes)
        {
            WriteBytes(bytes.Span);
        }

        public void AlignTo(in int alignment, in byte fill = 0)
        {
            if (alignment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be greater than zero.");
            }

            int alignedLength = ((m_Length + alignment - 1) / alignment) * alignment;
            int padding = alignedLength - m_Length;
            if (padding <= 0)
            {
                return;
            }

            EnsureCapacity(padding);
            if (fill != 0)
            {
                m_Buffer.AsSpan(m_Length, padding).Fill(fill);
            }
            m_Length = alignedLength;
        }

        public void WriteResourceIndex(in uint bindingTableIndex, in uint slot, in uint arrayIndex)
        {
            // Standard token payload layout: {tableIndex, slot, arrayIndex}.
            WriteU32(bindingTableIndex);
            WriteU32(slot);
            WriteU32(arrayIndex);
        }

        public ReadOnlyMemory<byte> ToMemory()
        {
            return new ReadOnlyMemory<byte>(ToArray());
        }

        public byte[] ToArray()
        {
            if (m_Length == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] result = new byte[m_Length];
            Buffer.BlockCopy(m_Buffer, 0, result, 0, m_Length);
            return result;
        }

        private void EnsureCapacity(in int appendLength)
        {
            int required = m_Length + appendLength;
            if (required <= m_Buffer.Length)
            {
                return;
            }

            int newCapacity = m_Buffer.Length;
            while (newCapacity < required)
            {
                newCapacity *= 2;
            }

            Array.Resize(ref m_Buffer, newCapacity);
        }
    }
}

