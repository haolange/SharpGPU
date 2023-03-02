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

    public class RHIAccelStructAABBs : RHIAccelStructGeometry
    {
        public uint Stride;
        public uint Offset;
        public ulong Count;
        public RHIBuffer? AABBs;
        public EAccelStructGeometryFlags GeometryFlags;

        public EAccelStructGeometryFlags GetGeometryFlags()
        { 
            return GeometryFlags; 
        }
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
        public EAccelStructGeometryFlags GeometryFlags;

        public EAccelStructGeometryFlags GetGeometryFlags()
        {
            return GeometryFlags;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RHIAccelStructInstance
    {
        public EAccelStructInstanceFlags Flags;
        public float4x4 TransformMatrix;
        public uint InstanceID;
        public byte InstanceMask;
        public uint InstanceContributionToHitGroupIndex;
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
        public RHITopLevelAccelStructDescriptor Descriptor;

        protected RHITopLevelAccelStruct(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            Descriptor = descriptor;
        }
    }

    public abstract class RHIBottomLevelAccelStruct : Disposal
    {
        public RHIBottomLevelAccelStructDescriptor Descriptor;

        protected RHIBottomLevelAccelStruct(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            Descriptor = descriptor;
        }
    }
}
