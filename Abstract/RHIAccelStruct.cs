using System;
using Infinity.Core;
using Infinity.Mathmatics;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    public enum EAccelStructFlags : byte
    {
        None,
        AllowUpdate,
        PerformUpdate,
        MinimizeMemory,
        PreferFastTrace,
        PreferFastBuild,
        AllowCompactation
    }

    public enum EAccelStructGeometryFlags : byte
    {
        None,
        Opaque,
        NoDuplicateAnyhitInverseOcation
    }

    public enum EAccelStructInstanceFlags : byte
    {
        None,
        ForceOpaque,
        ForceNonOpaque,
        TriangleCullDisable,
        TriangleFrontCounterclockwise
    }

    public interface RHIAccelStructGeometry
    {
        public EAccelStructGeometryFlags GetGeometryFlags();
    }

    public struct RHIAccelStructAABBs : RHIAccelStructGeometry
    {
        public uint Count;
        public uint Stride;
        public uint Offset;
        public RHIBuffer? AABBs;
        public EAccelStructGeometryFlags GeometryFlags;

        public EAccelStructGeometryFlags GetGeometryFlags()
        { 
            return GeometryFlags; 
        }
    }

    public struct RHIAccelStructTriangles : RHIAccelStructGeometry
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
        public EAccelStructGeometryFlags GeometryFlags;

        public EAccelStructGeometryFlags GetGeometryFlags()
        {
            return GeometryFlags;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructInstance
    {
        public uint InstanceID;
        public byte InstanceMask;
        public uint HitGroupIndex;
        public float4x4 TransformMatrix;
        public EAccelStructInstanceFlags Flags;
        public RHIBottomLevelAccelStruct BottonLevelAccelStruct;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHITopLevelAccelStructDescriptor
    {
        public uint Offset;
        public EAccelStructFlags Flags;
        public Memory<RHIAccelStructInstance> Instances;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIBottomLevelAccelStructDescriptor
    {
        public Memory<RHIAccelStructGeometry> Geometries;
    }

    public abstract class RHITopLevelAccelStruct : Disposal
    {
        public RHITopLevelAccelStructDescriptor Descriptor => m_Descriptor;

        protected RHITopLevelAccelStructDescriptor m_Descriptor;

        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
        public abstract void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
    }

    public abstract class RHIBottomLevelAccelStruct : Disposal
    {
        public RHIBottomLevelAccelStructDescriptor Descriptor => m_Descriptor;

        protected RHIBottomLevelAccelStructDescriptor m_Descriptor;

        public abstract RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor);
        public abstract void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor);
    }
}
