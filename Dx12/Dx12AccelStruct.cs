using System;
using System.Diagnostics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal static unsafe class Dx12RaytracingHelper
    {
        public static D3D12_HEAP_PROPERTIES kUploadHeapProps = CreateHeapProperties(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD, D3D12_CPU_PAGE_PROPERTY.D3D12_CPU_PAGE_PROPERTY_UNKNOWN, D3D12_MEMORY_POOL.D3D12_MEMORY_POOL_UNKNOWN, 0, 0);
        public static D3D12_HEAP_PROPERTIES kDefaultHeapProps = CreateHeapProperties(D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT, D3D12_CPU_PAGE_PROPERTY.D3D12_CPU_PAGE_PROPERTY_UNKNOWN, D3D12_MEMORY_POOL.D3D12_MEMORY_POOL_UNKNOWN, 0, 0);

        private static D3D12_HEAP_PROPERTIES CreateHeapProperties(in D3D12_HEAP_TYPE heapType, in D3D12_CPU_PAGE_PROPERTY cpuPage, in D3D12_MEMORY_POOL memoryPool, in uint creationNodeMask, in uint visibleNodeMask)
        {
            D3D12_HEAP_PROPERTIES outHeapProperties;
            outHeapProperties.Type = heapType;
            outHeapProperties.CPUPageProperty = cpuPage;
            outHeapProperties.MemoryPoolPreference = memoryPool;
            outHeapProperties.CreationNodeMask = creationNodeMask;
            outHeapProperties.VisibleNodeMask = visibleNodeMask;
            return outHeapProperties;
        }

        public static ID3D12Resource* CreateBuffer(in ID3D12Device8* pDevice, in uint size, in D3D12_RESOURCE_FLAGS flags, in D3D12_RESOURCE_STATES initState, D3D12_HEAP_PROPERTIES heapProps)
        {
            DXGI_SAMPLE_DESC sampleDesc = new DXGI_SAMPLE_DESC();
            sampleDesc.Count = 1;
            sampleDesc.Quality = 0;

            D3D12_RESOURCE_DESC description = new D3D12_RESOURCE_DESC
            {
                Alignment = 0UL,
                DepthOrArraySize = 1,
                Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
                Flags = flags,
                Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                Height = 1,
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
                MipLevels = 1,
                SampleDesc = sampleDesc,
                Width = size
            };

            ID3D12Resource* dx12Resource;
            HRESULT hResult = pDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_ALLOW_ALL_BUFFERS_AND_TEXTURES, &description, initState, null, __uuidof<ID3D12Resource>(), (void**)&dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            return dx12Resource;
        }
    }

    internal unsafe class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct
    {
        public int DescriptionHeapIndex => m_DescriptionHeapIndex;
        public ID3D12Resource* ResultBuffer => m_ResultBuffer;
        public D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private int m_DescriptionHeapIndex;
        private ID3D12Resource* m_ResultBuffer;
        private ID3D12Resource* m_ScratchBuffer;
        private ID3D12Resource* m_InstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12TopLevelAccelStruct(Dx12Device device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }

        protected override void Release()
        {
            m_ResultBuffer->Release();
            m_ScratchBuffer->Release();
            m_InstancesBuffer->Release();
        }
    }

    internal unsafe class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        public Dx12Device Dx12Device => m_Dx12Device;
        public ID3D12Resource* NativeResultBuffer => m_NativeResultBuffer;
        public D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private Dx12Device m_Dx12Device;
        private ID3D12Resource* m_NativeResultBuffer;
        private ID3D12Resource* m_NativeScratchBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12BottomLevelAccelStruct(Dx12Device device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
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

            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS accelStructDescriptor = new D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS();
            {
                accelStructDescriptor.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL;
                accelStructDescriptor.Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_NONE;
                accelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                accelStructDescriptor.NumDescs = (uint)descriptor.Geometries.Length;
                accelStructDescriptor.pGeometryDescs = geometryDescriptions;
            }

            D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO accelStructPrebuildInfo;
            m_Dx12Device.NativeDevice->GetRaytracingAccelerationStructurePrebuildInfo(&accelStructDescriptor, &accelStructPrebuildInfo);
            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)accelStructPrebuildInfo.ScratchDataSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)accelStructPrebuildInfo.ResultDataMaxSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE, Dx12RaytracingHelper.kDefaultHeapProps);

            m_NativeAccelStrucDescriptor.Inputs = accelStructDescriptor;
            m_NativeAccelStrucDescriptor.DestAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStrucDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer->GetGPUVirtualAddress();
        }

        protected override void Release()
        {
            m_NativeResultBuffer->Release();
            m_NativeScratchBuffer->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
