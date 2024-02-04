using System;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using Infinity.Collections.LowLevel;
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

            ID3D12Resource* nativeResource;
            HRESULT hResult = pDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_ALLOW_ALL_BUFFERS_AND_TEXTURES, &description, initState, null, __uuidof<ID3D12Resource>(), (void**)&nativeResource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            return nativeResource;
        }
    }

    internal unsafe class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct
    {
        public Dx12Device Dx12Device => m_Dx12Device;
        public int DescriptionHeapIndex => m_DescriptionHeapIndex;
        public ID3D12Resource* ResultBuffer => m_NativeResultBuffer;
        public D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStructDescriptor => m_NativeAccelStructDescriptor;

        private Dx12Device m_Dx12Device;
        private int m_DescriptionHeapIndex;
        private ID3D12Resource* m_NativeResultBuffer;
        private ID3D12Resource* m_NativeScratchBuffer;
        private ID3D12Resource* m_NativeInstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStructDescriptor;

        public Dx12TopLevelAccelStruct(Dx12Device device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            Span<RHIAccelStructInstance> asInstances = descriptor.Instances.Span;
            D3D12_RAYTRACING_INSTANCE_DESC* nativeInstanceDescriptions = stackalloc D3D12_RAYTRACING_INSTANCE_DESC[descriptor.Instances.Length];

            for (int i = 0; i < descriptor.Instances.Length; ++i)
            {
                ref RHIAccelStructInstance asInstance = ref asInstances[i];
                Dx12BottomLevelAccelStruct accelStruct = asInstance.BottomLevelAccelStruct as Dx12BottomLevelAccelStruct;

                ref D3D12_RAYTRACING_INSTANCE_DESC nativeInstanceDescription = ref nativeInstanceDescriptions[i];
                {
                    ref D3D12_RAYTRACING_INSTANCE_DESC._Transform_e__FixedBuffer transform = ref nativeInstanceDescription.Transform;
                    transform[0] = asInstance.TransformMatrix.c0.x; transform[1] = asInstance.TransformMatrix.c0.y; transform[2] = asInstance.TransformMatrix.c0.z;
                    transform[3] = asInstance.TransformMatrix.c1.x; transform[4] = asInstance.TransformMatrix.c1.y; transform[5] = asInstance.TransformMatrix.c1.z;
                    transform[6] = asInstance.TransformMatrix.c2.x; transform[7] = asInstance.TransformMatrix.c2.y; transform[8] = asInstance.TransformMatrix.c2.z;
                    transform[9] = 1; transform[10] = 1; transform[11] = 1;

                    nativeInstanceDescription.Flags = (uint)(D3D12_RAYTRACING_INSTANCE_FLAGS)asInstance.Flag;
                    nativeInstanceDescription.InstanceID = asInstance.InstanceID;
                    nativeInstanceDescription.InstanceMask = asInstance.InstanceMask;
                    nativeInstanceDescription.InstanceContributionToHitGroupIndex = asInstance.HitGroupIndex;
                    nativeInstanceDescription.AccelerationStructure = accelStruct.NativeResultBuffer->GetGPUVirtualAddress();
                }
            }

            m_NativeInstancesBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)sizeof(D3D12_RAYTRACING_INSTANCE_DESC) * (uint)descriptor.Instances.Length, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDEX_BUFFER | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER, Dx12RaytracingHelper.kUploadHeapProps);

            void* data;
            m_NativeInstancesBuffer->Map(0, null, &data);
            MemoryUtility.MemCpy(nativeInstanceDescriptions, data, (uint)sizeof(D3D12_RAYTRACING_INSTANCE_DESC) * (uint)descriptor.Instances.Length);
            m_NativeInstancesBuffer->Unmap(0, null);

            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS nativeAccelStructDescriptor = new D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS();
            {
                nativeAccelStructDescriptor.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL;
                nativeAccelStructDescriptor.Flags = (D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS)descriptor.Flag;
                nativeAccelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                nativeAccelStructDescriptor.NumDescs = (uint)descriptor.Instances.Length;
                nativeAccelStructDescriptor.InstanceDescs = m_NativeInstancesBuffer->GetGPUVirtualAddress() + descriptor.Offset;
            }

            D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO nativeAccelStructPrebuildInfo;
            m_Dx12Device.NativeDevice->GetRaytracingAccelerationStructurePrebuildInfo(&nativeAccelStructDescriptor, &nativeAccelStructPrebuildInfo);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE, Dx12RaytracingHelper.kDefaultHeapProps);

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer->GetGPUVirtualAddress();
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            Span<RHIAccelStructInstance> asInstances = descriptor.Instances.Span;
            D3D12_RAYTRACING_INSTANCE_DESC* nativeInstanceDescriptions = stackalloc D3D12_RAYTRACING_INSTANCE_DESC[descriptor.Instances.Length];

            for (int i = 0; i < descriptor.Instances.Length; ++i)
            {
                ref RHIAccelStructInstance asInstance = ref asInstances[i];
                Dx12BottomLevelAccelStruct accelStruct = asInstance.BottomLevelAccelStruct as Dx12BottomLevelAccelStruct;

                ref D3D12_RAYTRACING_INSTANCE_DESC nativeInstanceDescription = ref nativeInstanceDescriptions[i];
                {
                    ref D3D12_RAYTRACING_INSTANCE_DESC._Transform_e__FixedBuffer transform = ref nativeInstanceDescription.Transform;
                    transform[0] = asInstance.TransformMatrix.c0.x; transform[1] = asInstance.TransformMatrix.c0.y; transform[2] = asInstance.TransformMatrix.c0.z;
                    transform[3] = asInstance.TransformMatrix.c1.x; transform[4] = asInstance.TransformMatrix.c1.y; transform[5] = asInstance.TransformMatrix.c1.z;
                    transform[6] = asInstance.TransformMatrix.c2.x; transform[7] = asInstance.TransformMatrix.c2.y; transform[8] = asInstance.TransformMatrix.c2.z;
                    transform[9] = 1; transform[10] = 1; transform[11] = 1;

                    nativeInstanceDescription.Flags = (uint)(D3D12_RAYTRACING_INSTANCE_FLAGS)asInstance.Flag;
                    nativeInstanceDescription.InstanceID = asInstance.InstanceID;
                    nativeInstanceDescription.InstanceMask = asInstance.InstanceMask;
                    nativeInstanceDescription.InstanceContributionToHitGroupIndex = asInstance.HitGroupIndex;
                    nativeInstanceDescription.AccelerationStructure = accelStruct.NativeResultBuffer->GetGPUVirtualAddress();
                }
            }

            void* data;
            m_NativeInstancesBuffer->Map(0, null, &data);
            MemoryUtility.MemCpy(nativeInstanceDescriptions, data, (uint)sizeof(D3D12_RAYTRACING_INSTANCE_DESC) * (uint)descriptor.Instances.Length);
            m_NativeInstancesBuffer->Unmap(0, null);

            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS nativeAccelStructDescriptor = new D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS();
            {
                nativeAccelStructDescriptor.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL;
                nativeAccelStructDescriptor.Flags = (D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS)descriptor.Flag | D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_PERFORM_UPDATE;
                nativeAccelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                nativeAccelStructDescriptor.NumDescs = (uint)descriptor.Instances.Length;
                nativeAccelStructDescriptor.InstanceDescs = m_NativeInstancesBuffer->GetGPUVirtualAddress() + descriptor.Offset;
            }

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStructDescriptor.SourceAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer->GetGPUVirtualAddress();
        }

        protected override void Release()
        {
            m_NativeResultBuffer->Release();
            m_NativeScratchBuffer->Release();
            m_NativeInstancesBuffer->Release();
        }
    }

    internal unsafe class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        public Dx12Device Dx12Device => m_Dx12Device;
        public ID3D12Resource* NativeResultBuffer => m_NativeResultBuffer;
        public D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStructDescriptor => m_NativeAccelStructDescriptor;

        private Dx12Device m_Dx12Device;
        private ID3D12Resource* m_NativeResultBuffer;
        private ID3D12Resource* m_NativeScratchBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStructDescriptor;

        public Dx12BottomLevelAccelStruct(Dx12Device device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            D3D12_RAYTRACING_GEOMETRY_DESC* nativeGeometryDescriptions = stackalloc D3D12_RAYTRACING_GEOMETRY_DESC[descriptor.Geometries.Length];

            for (int i = 0; i < descriptor.Geometries.Length; ++i)
            {
                RHIAccelStructGeometry asGeometry = descriptor.Geometries[i];
                ref D3D12_RAYTRACING_GEOMETRY_DESC nativeGeometryDescription = ref nativeGeometryDescriptions[i];

                switch (asGeometry.GeometryType)
                {
                    case EAccelStructGeometryType.AABB:
                        RHIAccelStructAABBs aabbGeometry = asGeometry as RHIAccelStructAABBs;
                        Dx12Buffer aabbBuffer = aabbGeometry.AABBBuffer as Dx12Buffer;

                        nativeGeometryDescription.Type = D3D12_RAYTRACING_GEOMETRY_TYPE.D3D12_RAYTRACING_GEOMETRY_TYPE_PROCEDURAL_PRIMITIVE_AABBS;
                        nativeGeometryDescription.Flags = (D3D12_RAYTRACING_GEOMETRY_FLAGS)asGeometry.GeometryFlag;

                        ref D3D12_RAYTRACING_GEOMETRY_AABBS_DESC nativeAABBGeometry = ref nativeGeometryDescription.AABBs;
                        nativeAABBGeometry.AABBCount = aabbGeometry.Count;
                        nativeAABBGeometry.AABBs.StartAddress = aabbBuffer.NativeResource->GetGPUVirtualAddress() + aabbGeometry.Offset;
                        nativeAABBGeometry.AABBs.StrideInBytes = aabbGeometry.Stride;
                        break;

                    case EAccelStructGeometryType.Triangle:
                        RHIAccelStructTriangles triangleGeometry = asGeometry as RHIAccelStructTriangles;
                        Dx12Buffer indexBuffer = triangleGeometry.IndexBuffer as Dx12Buffer;
                        Dx12Buffer vertexBuffer = triangleGeometry.VertexBuffer as Dx12Buffer;

                        nativeGeometryDescription.Type = D3D12_RAYTRACING_GEOMETRY_TYPE.D3D12_RAYTRACING_GEOMETRY_TYPE_TRIANGLES;
                        nativeGeometryDescription.Flags = (D3D12_RAYTRACING_GEOMETRY_FLAGS)asGeometry.GeometryFlag;

                        ref D3D12_RAYTRACING_GEOMETRY_TRIANGLES_DESC nativeTriangleGeometry = ref nativeGeometryDescription.Triangles;
                        nativeTriangleGeometry.IndexCount = triangleGeometry.IndexCount;
                        nativeTriangleGeometry.IndexBuffer = indexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.IndexOffset;
                        nativeTriangleGeometry.IndexFormat = Dx12Utility.ConvertToDx12IndexFormat(triangleGeometry.IndexFormat);
                        nativeTriangleGeometry.VertexCount = triangleGeometry.VertexCount;
                        nativeTriangleGeometry.VertexBuffer.StartAddress = vertexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.VertexOffset;
                        nativeTriangleGeometry.VertexBuffer.StrideInBytes = triangleGeometry.VertexStride;
                        nativeTriangleGeometry.VertexFormat = Dx12Utility.ConvertToDx12Format(triangleGeometry.VertexFormat);
                        break;
                }
            }

            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS nativeAccelStructDescriptor = new D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS();
            {
                nativeAccelStructDescriptor.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL;
                nativeAccelStructDescriptor.Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_NONE;
                nativeAccelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                nativeAccelStructDescriptor.NumDescs = (uint)descriptor.Geometries.Length;
                nativeAccelStructDescriptor.pGeometryDescs = nativeGeometryDescriptions;
            }

            D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO nativeAccelStructPrebuildInfo;
            m_Dx12Device.NativeDevice->GetRaytracingAccelerationStructurePrebuildInfo(&nativeAccelStructDescriptor, &nativeAccelStructPrebuildInfo);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE, Dx12RaytracingHelper.kDefaultHeapProps);

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer->GetGPUVirtualAddress();
        }

        protected override void Release()
        {
            m_NativeResultBuffer->Release();
            m_NativeScratchBuffer->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
