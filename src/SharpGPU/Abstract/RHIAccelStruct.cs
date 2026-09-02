using SharpGPU.Core;
using SharpGPU.Mathematics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.IO;
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
        AllowDisableOmms = 0x40,
        Motion = 0x80
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

    public enum ERHIAccelStructMotionInstanceType : byte
    {
        None = 0,
        Matrix = 1,
        Srt = 2
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
        public RHIBuffer? MotionVertexBuffer;
        public uint MotionVertexOffset;
        public uint MotionVertexStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructSrtTransform
    {
        public float Sx;
        public float A;
        public float B;
        public float Pvx;
        public float Sy;
        public float C;
        public float Pvy;
        public float Sz;
        public float Pvz;
        public float Qx;
        public float Qy;
        public float Qz;
        public float Qw;
        public float Tx;
        public float Ty;
        public float Tz;
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

    internal static class RHIAccelStructTriangleContract
    {
        public static bool HasIndexIntent(in RHIAccelStructTriangles triangles)
        {
            return triangles.IndexBuffer != null || triangles.IndexCount > 0;
        }

        public static void ValidateIndexIntent(in RHIAccelStructTriangles triangles)
        {
            if (!HasIndexIntent(in triangles))
            {
                return;
            }

            if (triangles.IndexBuffer == null)
            {
                throw new ArgumentException(
                    "Triangle IndexCount requires a non-null IndexBuffer.");
            }

            if (triangles.IndexCount == 0 || (triangles.IndexCount % 3u) != 0)
            {
                throw new ArgumentException(
                    "Indexed triangle geometry requires IndexCount > 0 and divisible by 3.");
            }
        }
    }

    internal static class RHIAccelStructMotionContract
    {
        public static void ValidateMotionType(in ERHIAccelStructMotionInstanceType motionType)
        {
            if (motionType != ERHIAccelStructMotionInstanceType.None &&
                motionType != ERHIAccelStructMotionInstanceType.Matrix &&
                motionType != ERHIAccelStructMotionInstanceType.Srt)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(motionType),
                    motionType,
                    "Unknown acceleration-structure motion instance type is fail-closed.");
            }
        }

        public static bool HasMotionTriangles(in RHIAccelStructTriangles triangles)
        {
            return triangles.MotionVertexBuffer != null;
        }

        public static bool HasMotionInstance(in RHIAccelStructInstance instance)
        {
            return instance.MotionType != ERHIAccelStructMotionInstanceType.None;
        }

        public static bool AnyTriangleUsesMotion(RHIAccelStructGeometry[]? geometries)
        {
            if (geometries == null)
            {
                return false;
            }

            for (int i = 0; i < geometries.Length; ++i)
            {
                if (geometries[i] is RHIAccelStructTriangles triangles &&
                    HasMotionTriangles(triangles))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool AnyInstanceUsesMotion(ReadOnlySpan<RHIAccelStructInstance> instances)
        {
            for (int i = 0; i < instances.Length; ++i)
            {
                if (HasMotionInstance(instances[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool UsesMotionFlag(in ERHIAccelStructFlag flag)
        {
            return (flag & ERHIAccelStructFlag.Motion) != 0;
        }

        public static void ValidateMotionFlagMatchesData(
            RHIDevice device,
            in ERHIAccelStructFlag flag,
            bool hasMotionData,
            string surface)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentException.ThrowIfNullOrWhiteSpace(surface);
            bool flagSet = UsesMotionFlag(flag);
            if (flagSet != hasMotionData)
            {
                throw new ArgumentException(
                    flagSet
                        ? $"ERHIAccelStructFlag.Motion is set on {surface} but no motion instance or triangle data is present."
                        : $"{surface} has motion data but ERHIAccelStructFlag.Motion is missing.");
            }

            if (flagSet)
            {
                device.Capabilities.RayTracing.Motion.Require("RayTracing.Motion");
            }
        }

        public static void ValidateTriangleAttachment(in RHIAccelStructTriangles triangles)
        {
            if (triangles.MotionVertexBuffer == null)
            {
                if (triangles.MotionVertexOffset != 0 || triangles.MotionVertexStride != 0)
                {
                    throw new ArgumentException(
                        "Motion vertex offset or stride requires a MotionVertexBuffer.");
                }

                return;
            }

            RHIOpacityMicromapContract.RequireAccelStructBuildInputUsage(
                triangles.MotionVertexBuffer,
                "MotionVertexBuffer");
            ResolveMotionVertexStride(in triangles);
        }

        public static uint ResolveMotionVertexStride(in RHIAccelStructTriangles triangles)
        {
            if (triangles.MotionVertexStride == 0)
            {
                return triangles.VertexStride;
            }

            if (triangles.MotionVertexStride != triangles.VertexStride)
            {
                throw new NotSupportedException(
                    "MotionVertexStride that differs from VertexStride is unsupported; DX12, Vulkan, and Metal motion AS paths share one vertex stride.");
            }

            return triangles.MotionVertexStride;
        }

        public static void RejectMotionModeSwitch(bool existingUsesMotion, in ERHIAccelStructFlag newFlag)
        {
            if (UsesMotionFlag(newFlag) != existingUsesMotion)
            {
                throw new InvalidOperationException(
                    "TLAS motion attachment cannot change across UpdateAccelerationStructure; recreate the acceleration structure.");
            }
        }

        public static void ValidateInstance(in RHIAccelStructInstance instance)
        {
            ValidateMotionType(instance.MotionType);
        }

        public static void ValidateTlasDescriptor(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            ReadOnlySpan<RHIAccelStructInstance> instances = descriptor.Instances.Span;
            for (int i = 0; i < instances.Length; ++i)
            {
                ValidateInstance(instances[i]);
            }

            ValidateMotionFlagMatchesData(
                device,
                descriptor.Flag,
                AnyInstanceUsesMotion(instances),
                "top-level acceleration structure");
        }

        public static void ValidateBlasDescriptor(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(device);
            RHIAccelStructGeometry[] geometries = descriptor.Geometries;
            if (geometries == null)
            {
                return;
            }

            for (int i = 0; i < geometries.Length; ++i)
            {
                if (geometries[i] is RHIAccelStructTriangles triangles)
                {
                    RHIAccelStructTriangleContract.ValidateIndexIntent(in triangles);
                    ValidateTriangleAttachment(triangles);
                }
            }

            ValidateMotionFlagMatchesData(
                device,
                descriptor.Flag,
                AnyTriangleUsesMotion(geometries),
                "bottom-level acceleration structure");
        }

        public static void WriteIdentity(BinaryWriter writer, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write((uint)descriptor.Flag);
            RHIAccelStructGeometry[] geometries = descriptor.Geometries ?? Array.Empty<RHIAccelStructGeometry>();
            writer.Write(geometries.Length);
            for (int i = 0; i < geometries.Length; ++i)
            {
                RHIAccelStructGeometry geometry = geometries[i];
                writer.Write((byte)geometry.GeometryType);
                writer.Write((uint)geometry.GeometryFlag);
                if (geometry is RHIAccelStructTriangles triangles)
                {
                    writer.Write(HasMotionTriangles(triangles));
                    writer.Write(triangles.MotionVertexOffset);
                    writer.Write(triangles.MotionVertexStride);
                }
                else
                {
                    writer.Write(false);
                    writer.Write(0u);
                    writer.Write(0u);
                }
            }
        }

        public static void WriteIdentity(BinaryWriter writer, in RHITopLevelAccelStructDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write((uint)descriptor.Flag);
            ReadOnlySpan<RHIAccelStructInstance> instances = descriptor.Instances.Span;
            writer.Write(instances.Length);
            for (int i = 0; i < instances.Length; ++i)
            {
                WriteIdentity(writer, instances[i]);
            }
        }

        public static void WriteIdentity(BinaryWriter writer, in RHIAccelStructInstance instance)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write((byte)instance.MotionType);
            WriteMatrix(writer, instance.MotionTransformMatrix);
            WriteSrt(writer, instance.MotionSrtT0);
            WriteSrt(writer, instance.MotionSrtT1);
        }

        public static byte[] HashBottomLevel(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteIdentity(writer, in descriptor);
            writer.Flush();
            return System.Security.Cryptography.SHA256.HashData(stream.ToArray());
        }

        public static byte[] HashTopLevel(in RHITopLevelAccelStructDescriptor descriptor)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteIdentity(writer, in descriptor);
            writer.Flush();
            return System.Security.Cryptography.SHA256.HashData(stream.ToArray());
        }

        private static void WriteMatrix(BinaryWriter writer, in float4x4 matrix)
        {
            writer.Write(matrix.c0.x);
            writer.Write(matrix.c0.y);
            writer.Write(matrix.c0.z);
            writer.Write(matrix.c0.w);
            writer.Write(matrix.c1.x);
            writer.Write(matrix.c1.y);
            writer.Write(matrix.c1.z);
            writer.Write(matrix.c1.w);
            writer.Write(matrix.c2.x);
            writer.Write(matrix.c2.y);
            writer.Write(matrix.c2.z);
            writer.Write(matrix.c2.w);
            writer.Write(matrix.c3.x);
            writer.Write(matrix.c3.y);
            writer.Write(matrix.c3.z);
            writer.Write(matrix.c3.w);
        }

        private static void WriteSrt(BinaryWriter writer, in RHIAccelStructSrtTransform transform)
        {
            writer.Write(transform.Sx);
            writer.Write(transform.A);
            writer.Write(transform.B);
            writer.Write(transform.Pvx);
            writer.Write(transform.Sy);
            writer.Write(transform.C);
            writer.Write(transform.Pvy);
            writer.Write(transform.Sz);
            writer.Write(transform.Pvz);
            writer.Write(transform.Qx);
            writer.Write(transform.Qy);
            writer.Write(transform.Qz);
            writer.Write(transform.Qw);
            writer.Write(transform.Tx);
            writer.Write(transform.Ty);
            writer.Write(transform.Tz);
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
        public ERHIAccelStructMotionInstanceType MotionType;
        public float4x4 MotionTransformMatrix;
        public RHIAccelStructSrtTransform MotionSrtT0;
        public RHIAccelStructSrtTransform MotionSrtT1;
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

