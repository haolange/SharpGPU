using SharpGPU.Core;
using SharpGPU.Mathematics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System;

namespace SharpGPU
{
    public enum EAccelStructFlag
    {
        None = 0,
        AllowUpdate = 0x1,
        PerformUpdate = 0x2,
        MinimizeMemory = 0x4,
        PreferFastTrace = 0x8,
        PreferFastBuild = 0x10,
        AllowCompaction = 0x20
    }

    public enum EAccelStructGeometryFlag
    {
        None = 0,
        Opaque = 0x1,
        NoDuplicateAnyhitInverseOcation = 0x2
    }

    public enum EAccelStructInstanceFlag
    {
        None = 0,
        ForceOpaque = 0x4,
        ForceNonOpaque = 0x8,
        TriangleCullDisable = 0x1,
        TriangleFrontCounterclockwise = 0x2
    }

    public enum EAccelStructGeometryType : byte
    {
        AABB,
        Curves,
        Triangle
    }

    public enum EAccelStructCurveType : byte
    {
        Round = 0,
        Flat = 1
    }

    public enum EAccelStructCurveBasis : byte
    {
        BSpline = 0,
        CatmullRom = 1,
        Linear = 2,
        Bezier = 3
    }

    public enum EAccelStructCurveEndCaps : byte
    {
        None = 0,
        Disk = 1,
        Sphere = 2
    }

    public class RHIAccelStructGeometry
    {
        public EAccelStructGeometryType GeometryType;
        public EAccelStructGeometryFlag GeometryFlag;
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
        public EAccelStructCurveType CurveType;
        public EAccelStructCurveBasis CurveBasis;
        public EAccelStructCurveEndCaps CurveEndCaps;
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
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructInstance
    {
        public uint InstanceID;
        public byte InstanceMask;
        public uint HitGroupIndex;
        public float4x4 TransformMatrix;
        public EAccelStructInstanceFlag Flag;
        public RHIBottomLevelAccelStruct BottomLevelAccelStruct;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHITopLevelAccelStructDescriptor
    {
        public uint Offset;
        public EAccelStructFlag Flag;
        public Memory<RHIAccelStructInstance> Instances;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIBottomLevelAccelStructDescriptor
    {
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

        public void WriteResourceIndex(in uint argumentTableIndex, in uint slot, in uint arrayIndex)
        {
            // Standard token payload layout: {tableIndex, slot, arrayIndex}.
            WriteU32(argumentTableIndex);
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

