using System;
using Infinity.Core;
using Infinity.Mathmatics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Infinity.Graphics
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

    public enum EAccelStructInstanceFlag : byte
    {
        None,
        ForceOpaque,
        ForceNonOpaque,
        TriangleCullDisable,
        TriangleFrontCounterclockwise
    }

    public enum EAccelStructGeometryType : byte
    {
        AABB,
        Triangle
    }

    public class RHIAccelStructGeometry
    {
        public EAccelStructGeometryType GeometryType;
        public EAccelStructGeometryFlag GeometryFlag;
    }

    public class RHIAccelStructAABBs : RHIAccelStructGeometry
    {
        public uint Count;
        public uint Stride;
        public uint Offset;
        public RHIBuffer? AABBBuffer;
    }

    public class RHIAccelStructTriangles : RHIAccelStructGeometry
    {
        public uint IndexCount;
        public uint IndexOffset;
        public RHIBuffer? IndexBuffer;
        public EIndexFormat IndexFormat;
        public uint VertexCount;
        public uint VertexStride;
        public uint VertexOffset;
        public RHIBuffer? VertexBuffer;
        public EPixelFormat VertexFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructInstance
    {
        public uint InstanceID;
        public byte InstanceMask;
        public uint HitGroupIndex;
        public float4x4 TransformMatrix;
        public EAccelStructInstanceFlag Flag;
        public RHIBottomLevelAccelStruct BottonLevelAccelStruct;
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
