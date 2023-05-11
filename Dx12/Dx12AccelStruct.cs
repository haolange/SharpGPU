using System;
using Infinity.Core;
using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct
    {
        internal int DescriptionHeapIndex => m_DescriptionHeapIndex;
        internal ID3D12Resource* ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private int m_DescriptionHeapIndex;
        private ID3D12Resource* m_ResultBuffer;
        private ID3D12Resource* m_ScratchBuffer;
        private ID3D12Resource* m_InstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12TopLevelAccelStruct(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }
    }

    internal unsafe class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        internal ID3D12Resource* ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private ID3D12Resource* m_ResultBuffer;
        private ID3D12Resource* m_ScratchBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12BottomLevelAccelStruct(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            D3D12_RAYTRACING_GEOMETRY_DESC* geometryDescriptions = stackalloc D3D12_RAYTRACING_GEOMETRY_DESC[descriptor.Geometries.Length];
            for (int i = 0; i < descriptor.Geometries.Length; ++i)
            {
                RHIAccelStructGeometry rtGeometry = descriptor.Geometries[i];
                ref D3D12_RAYTRACING_GEOMETRY_DESC geometryDescription = ref geometryDescriptions[i];

                switch (rtGeometry.GeometryType)
                {
                    case EAccelStructGeometryType.AABB:
                        RHIAccelStructAABBs aabbGeometry = rtGeometry as RHIAccelStructAABBs;
                        Dx12Buffer aabbBuffer = aabbGeometry.AABBBuffer as Dx12Buffer;

                        geometryDescription.Type = D3D12_RAYTRACING_GEOMETRY_TYPE.D3D12_RAYTRACING_GEOMETRY_TYPE_PROCEDURAL_PRIMITIVE_AABBS;
                        geometryDescription.Flags = (D3D12_RAYTRACING_GEOMETRY_FLAGS)rtGeometry.GeometryFlag;

                        ref D3D12_RAYTRACING_GEOMETRY_AABBS_DESC dx12AABBGeometry = ref geometryDescription.AABBs;
                        dx12AABBGeometry.AABBCount = aabbGeometry.Count;
                        dx12AABBGeometry.AABBs.StartAddress = aabbBuffer.NativeResource->GetGPUVirtualAddress() + aabbGeometry.Offset;
                        dx12AABBGeometry.AABBs.StrideInBytes = aabbGeometry.Stride;
                        break;

                    case EAccelStructGeometryType.Triangle:
                        RHIAccelStructTriangles triangleGeometry = rtGeometry as RHIAccelStructTriangles;
                        Dx12Buffer indexBuffer = triangleGeometry.IndexBuffer as Dx12Buffer;
                        Dx12Buffer vertexBuffer = triangleGeometry.VertexBuffer as Dx12Buffer;

                        geometryDescription.Type = D3D12_RAYTRACING_GEOMETRY_TYPE.D3D12_RAYTRACING_GEOMETRY_TYPE_TRIANGLES;
                        geometryDescription.Flags = (D3D12_RAYTRACING_GEOMETRY_FLAGS)rtGeometry.GeometryFlag;

                        ref D3D12_RAYTRACING_GEOMETRY_TRIANGLES_DESC dx12TriangleGeometry = ref geometryDescription.Triangles;
                        dx12TriangleGeometry.IndexCount = triangleGeometry.IndexCount;
                        dx12TriangleGeometry.IndexBuffer = indexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.IndexOffset;
                        dx12TriangleGeometry.IndexFormat = Dx12Utility.ConvertToDx12IndexFormat(triangleGeometry.IndexFormat);
                        dx12TriangleGeometry.VertexCount = triangleGeometry.VertexCount;
                        dx12TriangleGeometry.VertexBuffer.StartAddress = vertexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.VertexOffset;
                        dx12TriangleGeometry.VertexBuffer.StrideInBytes = triangleGeometry.VertexStride;
                        dx12TriangleGeometry.VertexFormat = Dx12Utility.ConvertToDx12Format(triangleGeometry.VertexFormat);
                        break;
                }
            }
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
