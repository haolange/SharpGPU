using System;
using System.Runtime.InteropServices;
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

        public static ID3D12Resource* CreateBuffer(in ID3D12Device10* pDevice, in uint size, in D3D12_RESOURCE_FLAGS flags, in D3D12_RESOURCE_STATES initState, D3D12_HEAP_PROPERTIES heapProps)
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
        public D3D12_CPU_DESCRIPTOR_HANDLE NativeCpuDescriptorHandle => m_NativeCpuDescriptorHandle;
        public D3D12_GPU_DESCRIPTOR_HANDLE NativeGpuDescriptorHandle => m_NativeGpuDescriptorHandle;
        public ID3D12Resource* ResultBuffer => m_NativeResultBuffer;
        public D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStructDescriptor => m_NativeAccelStructDescriptor;

        private Dx12Device m_Dx12Device;
        private int m_DescriptionHeapIndex;
        private D3D12_CPU_DESCRIPTOR_HANDLE m_NativeCpuDescriptorHandle;
        private D3D12_GPU_DESCRIPTOR_HANDLE m_NativeGpuDescriptorHandle;
        private ID3D12Resource* m_NativeResultBuffer;
        private ID3D12Resource* m_NativeScratchBuffer;
        private ID3D12Resource* m_NativeInstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStructDescriptor;

        public Dx12TopLevelAccelStruct(Dx12Device device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_DescriptionHeapIndex = -1;
            m_NativeCpuDescriptorHandle = default;
            m_NativeGpuDescriptorHandle = default;

            Span<RHIAccelStructInstance> asInstances = descriptor.Instances.Span;
            D3D12_RAYTRACING_INSTANCE_DESC* nativeInstanceDescriptions = stackalloc D3D12_RAYTRACING_INSTANCE_DESC[descriptor.Instances.Length];

            for (int i = 0; i < descriptor.Instances.Length; ++i)
            {
                ref RHIAccelStructInstance asInstance = ref asInstances[i];
                Dx12BottomLevelAccelStruct accelStruct = asInstance.BottomLevelAccelStruct as Dx12BottomLevelAccelStruct;

                ref D3D12_RAYTRACING_INSTANCE_DESC nativeInstanceDescription = ref nativeInstanceDescriptions[i];
                {
                    ref D3D12_RAYTRACING_INSTANCE_DESC._Transform_e__FixedBuffer transform = ref nativeInstanceDescription.Transform;
                    // D3D12 expects a row-major 3x4 transform matrix.
                    transform[0] = asInstance.TransformMatrix.c0.x; transform[1] = asInstance.TransformMatrix.c1.x; transform[2] = asInstance.TransformMatrix.c2.x; transform[3] = asInstance.TransformMatrix.c3.x;
                    transform[4] = asInstance.TransformMatrix.c0.y; transform[5] = asInstance.TransformMatrix.c1.y; transform[6] = asInstance.TransformMatrix.c2.y; transform[7] = asInstance.TransformMatrix.c3.y;
                    transform[8] = asInstance.TransformMatrix.c0.z; transform[9] = asInstance.TransformMatrix.c1.z; transform[10] = asInstance.TransformMatrix.c2.z; transform[11] = asInstance.TransformMatrix.c3.z;

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
                nativeAccelStructDescriptor.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag);
                nativeAccelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                nativeAccelStructDescriptor.NumDescs = (uint)descriptor.Instances.Length;
                nativeAccelStructDescriptor.InstanceDescs = m_NativeInstancesBuffer->GetGPUVirtualAddress() + descriptor.Offset;
            }

            D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO nativeAccelStructPrebuildInfo;
            m_Dx12Device.NativeDevice->GetRaytracingAccelerationStructurePrebuildInfo(&nativeAccelStructDescriptor, &nativeAccelStructPrebuildInfo);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE, Dx12RaytracingHelper.kDefaultHeapProps);

            Dx12DescriptorInfo accelStructDescriptor = m_Dx12Device.AllocateCbvSrvUavDescriptor(1);
            m_DescriptionHeapIndex = accelStructDescriptor.Index;
            m_NativeCpuDescriptorHandle = accelStructDescriptor.CpuHandle;
            m_NativeGpuDescriptorHandle = accelStructDescriptor.GpuHandle;

            D3D12_SHADER_RESOURCE_VIEW_DESC accelStructSrvDesc = new D3D12_SHADER_RESOURCE_VIEW_DESC();
            accelStructSrvDesc.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
            accelStructSrvDesc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_RAYTRACING_ACCELERATION_STRUCTURE;
            accelStructSrvDesc.Shader4ComponentMapping = 5768;
            accelStructSrvDesc.RaytracingAccelerationStructure.Location = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_Dx12Device.NativeDevice->CreateShaderResourceView(null, &accelStructSrvDesc, m_NativeCpuDescriptorHandle);

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
                    // D3D12 expects a row-major 3x4 transform matrix.
                    transform[0] = asInstance.TransformMatrix.c0.x; transform[1] = asInstance.TransformMatrix.c1.x; transform[2] = asInstance.TransformMatrix.c2.x; transform[3] = asInstance.TransformMatrix.c3.x;
                    transform[4] = asInstance.TransformMatrix.c0.y; transform[5] = asInstance.TransformMatrix.c1.y; transform[6] = asInstance.TransformMatrix.c2.y; transform[7] = asInstance.TransformMatrix.c3.y;
                    transform[8] = asInstance.TransformMatrix.c0.z; transform[9] = asInstance.TransformMatrix.c1.z; transform[10] = asInstance.TransformMatrix.c2.z; transform[11] = asInstance.TransformMatrix.c3.z;

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
                nativeAccelStructDescriptor.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag) | D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_PERFORM_UPDATE;
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
            if (m_DescriptionHeapIndex >= 0)
            {
                m_Dx12Device.FreeCbvSrvUavDescriptor(m_DescriptionHeapIndex);
                m_DescriptionHeapIndex = -1;
            }
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
        private ID3D12Resource* m_NativeCurveAabbBuffer;
        private D3D12_RAYTRACING_GEOMETRY_DESC* m_NativeGeometryDescriptions;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStructDescriptor;

        public Dx12BottomLevelAccelStruct(Dx12Device device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_NativeCurveAabbBuffer = null;

            int geometryCount = descriptor.Geometries.Length;
            if (geometryCount == 0)
            {
                throw new InvalidOperationException("Bottom-level acceleration structure requires at least one geometry descriptor.");
            }

            nuint geometryBytes = (nuint)(geometryCount * sizeof(D3D12_RAYTRACING_GEOMETRY_DESC));
            m_NativeGeometryDescriptions = (D3D12_RAYTRACING_GEOMETRY_DESC*)NativeMemory.AllocZeroed(geometryBytes);
            if (m_NativeGeometryDescriptions == null)
            {
                throw new OutOfMemoryException($"Failed to allocate {geometryBytes} bytes for BLAS geometry descriptors.");
            }

            D3D12_RAYTRACING_GEOMETRY_DESC* nativeGeometryDescriptions = m_NativeGeometryDescriptions;

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
                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

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
                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

                        ref D3D12_RAYTRACING_GEOMETRY_TRIANGLES_DESC nativeTriangleGeometry = ref nativeGeometryDescription.Triangles;
                        nativeTriangleGeometry.IndexCount = triangleGeometry.IndexCount;
                        nativeTriangleGeometry.IndexBuffer = indexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.IndexOffset;
                        nativeTriangleGeometry.IndexFormat = Dx12Utility.ConvertToDx12IndexFormat(triangleGeometry.IndexFormat);
                        nativeTriangleGeometry.VertexCount = triangleGeometry.VertexCount;
                        nativeTriangleGeometry.VertexBuffer.StartAddress = vertexBuffer.NativeResource->GetGPUVirtualAddress() + triangleGeometry.VertexOffset;
                        nativeTriangleGeometry.VertexBuffer.StrideInBytes = triangleGeometry.VertexStride;
                        DXGI_FORMAT vertexFormat = Dx12Utility.ConvertToDx12ViewFormat(triangleGeometry.VertexFormat);
                        if (vertexFormat == DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT)
                        {
                            // DXR triangles require xyz vertex format. Keep float4 layout by using xyz format + explicit stride.
                            vertexFormat = DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT;
                        }

                        nativeTriangleGeometry.VertexFormat = vertexFormat;
                        break;

                    case EAccelStructGeometryType.Curves:
                        // DXR does not support native curve geometry. Map each curve segment to an AABB
                        // for procedural intersection. The intersection shader performs the exact test.
                        RHIAccelStructCurves curveGeometry = asGeometry as RHIAccelStructCurves;
                        Dx12Buffer controlPointBuffer = curveGeometry.ControlPointBuffer as Dx12Buffer;
                        Dx12Buffer radiusBuffer = curveGeometry.RadiusBuffer as Dx12Buffer;
                        Dx12Buffer curveIndexBuffer = curveGeometry.IndexBuffer as Dx12Buffer;
                        if (controlPointBuffer == null || radiusBuffer == null)
                        {
                            throw new InvalidOperationException("Curve geometry requires control-point and radius buffers.");
                        }

                        uint curveAabbCount = curveGeometry.SegmentCount;
                        if (curveAabbCount == 0)
                        {
                            throw new InvalidOperationException("Curve geometry has no segments.");
                        }

                        uint segmentControlPointCount = curveGeometry.SegmentControlPointCount;
                        if (segmentControlPointCount == 0)
                        {
                            throw new InvalidOperationException("Curve geometry segment control-point count must be greater than zero.");
                        }

                        if (curveGeometry.ControlPointStride < 12u)
                        {
                            throw new InvalidOperationException($"Curve control-point stride ({curveGeometry.ControlPointStride}) is smaller than 3-float position size.");
                        }

                        if (curveGeometry.RadiusStride < 4u)
                        {
                            throw new InvalidOperationException($"Curve radius stride ({curveGeometry.RadiusStride}) is smaller than float size.");
                        }

                        if (curveGeometry.IndexBuffer != null && curveIndexBuffer == null)
                        {
                            throw new InvalidOperationException("Curve index buffer is not a DX12 buffer.");
                        }

                        uint curveAabbBufferSize = curveAabbCount * (uint)sizeof(D3D12_RAYTRACING_AABB);

                        // Allocate GPU buffer for AABB data
                        m_NativeCurveAabbBuffer = Dx12RaytracingHelper.CreateBuffer(
                            m_Dx12Device.NativeDevice,
                            curveAabbBufferSize,
                            D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
                            Dx12RaytracingHelper.kUploadHeapProps);

                        // Compute conservative AABBs from curve control points with per-point radius inflation.
                        {
                            void* pAabbData;
                            m_NativeCurveAabbBuffer->Map(0, null, &pAabbData);
                            D3D12_RAYTRACING_AABB* aabbs = (D3D12_RAYTRACING_AABB*)pAabbData;

                            void* pControlPointData = null;
                            void* pRadiusData = null;
                            void* pIndexData = null;
                            controlPointBuffer.NativeResource->Map(0, null, &pControlPointData);
                            radiusBuffer.NativeResource->Map(0, null, &pRadiusData);
                            if (curveIndexBuffer != null)
                            {
                                curveIndexBuffer.NativeResource->Map(0, null, &pIndexData);
                            }
                            try
                            {
                                BuildCurveAabbs(
                                    aabbs,
                                    curveAabbCount,
                                    segmentControlPointCount,
                                    curveGeometry,
                                    (byte*)pControlPointData,
                                    (byte*)pRadiusData,
                                    (byte*)pIndexData);
                            }
                            finally
                            {
                                if (curveIndexBuffer != null)
                                {
                                    curveIndexBuffer.NativeResource->Unmap(0, null);
                                }
                                radiusBuffer.NativeResource->Unmap(0, null);
                                controlPointBuffer.NativeResource->Unmap(0, null);
                                m_NativeCurveAabbBuffer->Unmap(0, null);
                            }
                        }

                        nativeGeometryDescription.Type = D3D12_RAYTRACING_GEOMETRY_TYPE.D3D12_RAYTRACING_GEOMETRY_TYPE_PROCEDURAL_PRIMITIVE_AABBS;
                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

                        ref D3D12_RAYTRACING_GEOMETRY_AABBS_DESC nativeCurveAABBGeometry = ref nativeGeometryDescription.AABBs;
                        nativeCurveAABBGeometry.AABBCount = curveAabbCount;
                        nativeCurveAABBGeometry.AABBs.StartAddress = m_NativeCurveAabbBuffer->GetGPUVirtualAddress();
                        nativeCurveAABBGeometry.AABBs.StrideInBytes = (ulong)sizeof(D3D12_RAYTRACING_AABB);
                        break;
                }
            }

            D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS nativeAccelStructDescriptor = new D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS();
            {
                nativeAccelStructDescriptor.Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL;
                nativeAccelStructDescriptor.Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAGS.D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_NONE;
                nativeAccelStructDescriptor.DescsLayout = D3D12_ELEMENTS_LAYOUT.D3D12_ELEMENTS_LAYOUT_ARRAY;
                nativeAccelStructDescriptor.NumDescs = (uint)descriptor.Geometries.Length;
                nativeAccelStructDescriptor.pGeometryDescs = m_NativeGeometryDescriptions;
            }

            D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO nativeAccelStructPrebuildInfo;
            m_Dx12Device.NativeDevice->GetRaytracingAccelerationStructurePrebuildInfo(&nativeAccelStructDescriptor, &nativeAccelStructPrebuildInfo);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE, Dx12RaytracingHelper.kDefaultHeapProps);

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestAccelerationStructureData = m_NativeResultBuffer->GetGPUVirtualAddress();
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer->GetGPUVirtualAddress();
        }

        private static void BuildCurveAabbs(
            D3D12_RAYTRACING_AABB* aabbs,
            in uint segmentCount,
            in uint segmentControlPointCount,
            in RHIAccelStructCurves curveGeometry,
            byte* controlPointData,
            byte* radiusData,
            byte* indexData)
        {
            byte* controlPointBase = controlPointData + curveGeometry.ControlPointOffset;
            byte* radiusBase = radiusData + curveGeometry.RadiusOffset;
            byte* indexBase = indexData != null ? indexData + curveGeometry.IndexOffset : null;
            uint sequentialSegmentStep = segmentControlPointCount > 1u ? segmentControlPointCount - 1u : 1u;

            for (uint segmentIndex = 0; segmentIndex < segmentCount; ++segmentIndex)
            {
                float minX = float.PositiveInfinity;
                float minY = float.PositiveInfinity;
                float minZ = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float maxY = float.NegativeInfinity;
                float maxZ = float.NegativeInfinity;

                for (uint localControlPoint = 0; localControlPoint < segmentControlPointCount; ++localControlPoint)
                {
                    uint controlPointIndex;
                    if (indexBase != null)
                    {
                        uint indexElement = segmentIndex * segmentControlPointCount + localControlPoint;
                        controlPointIndex = ReadCurveControlPointIndex(indexBase, curveGeometry.IndexFormat, indexElement);
                    }
                    else
                    {
                        controlPointIndex = segmentIndex * sequentialSegmentStep + localControlPoint;
                    }

                    if (controlPointIndex >= curveGeometry.ControlPointCount)
                    {
                        throw new InvalidOperationException($"Curve control-point index {controlPointIndex} out of range (count={curveGeometry.ControlPointCount}).");
                    }

                    float* controlPoint = (float*)(controlPointBase + (controlPointIndex * curveGeometry.ControlPointStride));
                    float* radius = (float*)(radiusBase + (controlPointIndex * curveGeometry.RadiusStride));
                    float inflatedRadius = *radius;
                    float candidateMinX = controlPoint[0] - inflatedRadius;
                    float candidateMinY = controlPoint[1] - inflatedRadius;
                    float candidateMinZ = controlPoint[2] - inflatedRadius;
                    float candidateMaxX = controlPoint[0] + inflatedRadius;
                    float candidateMaxY = controlPoint[1] + inflatedRadius;
                    float candidateMaxZ = controlPoint[2] + inflatedRadius;

                    if (candidateMinX < minX) minX = candidateMinX;
                    if (candidateMinY < minY) minY = candidateMinY;
                    if (candidateMinZ < minZ) minZ = candidateMinZ;
                    if (candidateMaxX > maxX) maxX = candidateMaxX;
                    if (candidateMaxY > maxY) maxY = candidateMaxY;
                    if (candidateMaxZ > maxZ) maxZ = candidateMaxZ;
                }

                aabbs[segmentIndex].MinX = minX;
                aabbs[segmentIndex].MinY = minY;
                aabbs[segmentIndex].MinZ = minZ;
                aabbs[segmentIndex].MaxX = maxX;
                aabbs[segmentIndex].MaxY = maxY;
                aabbs[segmentIndex].MaxZ = maxZ;
            }
        }

        private static uint ReadCurveControlPointIndex(byte* indexBase, in ERHIBufferFormat format, in uint elementIndex)
        {
            return format switch
            {
                ERHIBufferFormat.UInt16 => ((ushort*)indexBase)[elementIndex],
                ERHIBufferFormat.UInt32 => ((uint*)indexBase)[elementIndex],
                _ => throw new NotSupportedException($"Unsupported curve index format '{format}'."),
            };
        }

        protected override void Release()
        {
            if (m_NativeGeometryDescriptions != null)
            {
                NativeMemory.Free(m_NativeGeometryDescriptions);
                m_NativeGeometryDescriptions = null;
            }

            if (m_NativeCurveAabbBuffer != null)
            {
                m_NativeCurveAabbBuffer->Release();
                m_NativeCurveAabbBuffer = null;
            }
            m_NativeResultBuffer->Release();
            m_NativeScratchBuffer->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
