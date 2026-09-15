using System;
using SharpGPU.Collections.LowLevel;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal static unsafe class Dx12RaytracingHelper
    {
        public static Vortice.Direct3D12.HeapProperties kUploadHeapProps = CreateHeapProperties(Vortice.Direct3D12.HeapType.Upload, Vortice.Direct3D12.CpuPageProperty.Unknown, Vortice.Direct3D12.MemoryPool.Unknown, 0, 0);
        public static Vortice.Direct3D12.HeapProperties kDefaultHeapProps = CreateHeapProperties(Vortice.Direct3D12.HeapType.Default, Vortice.Direct3D12.CpuPageProperty.Unknown, Vortice.Direct3D12.MemoryPool.Unknown, 0, 0);

        private static Vortice.Direct3D12.HeapProperties CreateHeapProperties(in Vortice.Direct3D12.HeapType heapType, in Vortice.Direct3D12.CpuPageProperty cpuPage, in Vortice.Direct3D12.MemoryPool memoryPool, in uint creationNodeMask, in uint visibleNodeMask)
        {
            Vortice.Direct3D12.HeapProperties outHeapProperties;
            outHeapProperties.Type = heapType;
            outHeapProperties.CPUPageProperty = cpuPage;
            outHeapProperties.MemoryPoolPreference = memoryPool;
            outHeapProperties.CreationNodeMask = creationNodeMask;
            outHeapProperties.VisibleNodeMask = visibleNodeMask;
            return outHeapProperties;
        }

        public static Vortice.Direct3D12.ID3D12Resource CreateBuffer(in Vortice.Direct3D12.ID3D12Device10 pDevice, in uint size, in Vortice.Direct3D12.ResourceFlags flags, in Vortice.Direct3D12.ResourceStates initState, Vortice.Direct3D12.HeapProperties heapProps)
        {
            Vortice.DXGI.SampleDescription sampleDesc = new Vortice.DXGI.SampleDescription();
            sampleDesc.Count = 1;
            sampleDesc.Quality = 0;

            Vortice.Direct3D12.ResourceDescription description = new Vortice.Direct3D12.ResourceDescription
            {
                Alignment = 0UL,
                DepthOrArraySize = 1,
                Dimension = Vortice.Direct3D12.ResourceDimension.Buffer,
                Flags = flags,
                Format = Vortice.DXGI.Format.Unknown,
                Height = 1,
                Layout = Vortice.Direct3D12.TextureLayout.RowMajor,
                MipLevels = 1,
                SampleDescription = sampleDesc,
                Width = size
            };

            Vortice.Direct3D12.ID3D12Resource? nativeResource;
            SharpGen.Runtime.Result hResult = pDevice.CreateCommittedResource(heapProps, Vortice.Direct3D12.HeapFlags.AllowAllBuffersAndTextures, description, initState, null, out nativeResource);
            return Dx12Utility.RequireCreatedObject(
                nativeResource,
                hResult,
                "ID3D12Device.CreateCommittedResource(acceleration-structure buffer)");
        }
    }

    internal unsafe class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct, IDx12DescriptorView
    {
        public Dx12Device Device => m_Dx12Device;
        public Dx12DescriptorClass DescriptorClass => Dx12DescriptorClass.AccelerationStructure;
        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle => m_Staging.Descriptor.CpuHandle;
        public Vortice.Direct3D12.ID3D12Resource ResultBuffer => m_NativeResultBuffer;
        public Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription NativeAccelStructDescriptor => m_NativeAccelStructDescriptor;

        private Dx12Device m_Dx12Device;
        private bool m_HasDescriptors;
        private Dx12CpuDescriptorAllocation m_Staging;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResultBuffer;
        private Vortice.Direct3D12.ID3D12Resource m_NativeScratchBuffer;
        private Vortice.Direct3D12.ID3D12Resource m_NativeInstancesBuffer;
        private Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription m_NativeAccelStructDescriptor;

        public Dx12TopLevelAccelStruct(Dx12Device device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_HasDescriptors = false;
            RHIOpacityMicromapContract.ValidateTlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(device, in descriptor);
            Span<RHIAccelStructInstance> asInstances = descriptor.Instances.Span;
            Vortice.Direct3D12.RaytracingInstanceDescription* nativeInstanceDescriptions = stackalloc Vortice.Direct3D12.RaytracingInstanceDescription[descriptor.Instances.Length];

            for (int i = 0; i < descriptor.Instances.Length; ++i)
            {
                ref RHIAccelStructInstance asInstance = ref asInstances[i];
                Dx12BottomLevelAccelStruct accelStruct = asInstance.BottomLevelAccelStruct as Dx12BottomLevelAccelStruct ?? throw new ArgumentException("TLAS instance must reference a Dx12BottomLevelAccelStruct.", nameof(descriptor));

                ref Vortice.Direct3D12.RaytracingInstanceDescription nativeInstanceDescription = ref nativeInstanceDescriptions[i];
                {
                    nativeInstanceDescription.Transform = new Vortice.Mathematics.Matrix3x4(
                        asInstance.TransformMatrix.c0.x, asInstance.TransformMatrix.c1.x, asInstance.TransformMatrix.c2.x, asInstance.TransformMatrix.c3.x,
                        asInstance.TransformMatrix.c0.y, asInstance.TransformMatrix.c1.y, asInstance.TransformMatrix.c2.y, asInstance.TransformMatrix.c3.y,
                        asInstance.TransformMatrix.c0.z, asInstance.TransformMatrix.c1.z, asInstance.TransformMatrix.c2.z, asInstance.TransformMatrix.c3.z);

                    nativeInstanceDescription.Flags = (Vortice.Direct3D12.RaytracingInstanceFlags)asInstance.Flag;
                    nativeInstanceDescription.InstanceID = (Vortice.UInt24)asInstance.InstanceID;
                    nativeInstanceDescription.InstanceMask = asInstance.InstanceMask;
                    nativeInstanceDescription.InstanceContributionToHitGroupIndex = (Vortice.UInt24)asInstance.HitGroupIndex;
                    nativeInstanceDescription.AccelerationStructure = accelStruct.NativeResultBuffer.GPUVirtualAddress;
                }
            }

            m_NativeInstancesBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)sizeof(Vortice.Direct3D12.RaytracingInstanceDescription) * (uint)descriptor.Instances.Length, Vortice.Direct3D12.ResourceFlags.None, Vortice.Direct3D12.ResourceStates.AllShaderResource | Vortice.Direct3D12.ResourceStates.CopySource | Vortice.Direct3D12.ResourceStates.IndexBuffer | Vortice.Direct3D12.ResourceStates.IndirectArgument | Vortice.Direct3D12.ResourceStates.VertexAndConstantBuffer, Dx12RaytracingHelper.kUploadHeapProps);

            void* data;
            m_NativeInstancesBuffer.Map(0, null, &data);
            MemoryUtility.MemCpy(nativeInstanceDescriptions, data, (uint)sizeof(Vortice.Direct3D12.RaytracingInstanceDescription) * (uint)descriptor.Instances.Length);
            m_NativeInstancesBuffer.Unmap(0, null);

            Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs nativeAccelStructDescriptor = new Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs();
            {
                nativeAccelStructDescriptor.Type = Vortice.Direct3D12.RaytracingAccelerationStructureType.TopLevel;
                nativeAccelStructDescriptor.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag);
                nativeAccelStructDescriptor.Layout = Vortice.Direct3D12.ElementsLayout.Array;
                nativeAccelStructDescriptor.DescriptorsCount = (uint)descriptor.Instances.Length;
                nativeAccelStructDescriptor.InstanceDescriptions = m_NativeInstancesBuffer.GPUVirtualAddress + descriptor.Offset;
            }

            Vortice.Direct3D12.RaytracingAccelerationStructurePrebuildInfo nativeAccelStructPrebuildInfo = m_Dx12Device.NativeDevice.GetRaytracingAccelerationStructurePrebuildInfo(nativeAccelStructDescriptor);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess, Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.UnorderedAccess, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess, Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure, Dx12RaytracingHelper.kDefaultHeapProps);

            m_Staging = m_Dx12Device.AllocateStagingCbvSrvUavDescriptor(1);
            m_HasDescriptors = true;

            Vortice.Direct3D12.ShaderResourceViewDescription accelStructSrvDesc = new Vortice.Direct3D12.ShaderResourceViewDescription();
            accelStructSrvDesc.Format = Vortice.DXGI.Format.Unknown;
            accelStructSrvDesc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.RaytracingAccelerationStructure;
            accelStructSrvDesc.Shader4ComponentMapping = 5768;
            accelStructSrvDesc.RaytracingAccelerationStructure.Location = m_NativeResultBuffer.GPUVirtualAddress;
            m_Dx12Device.NativeDevice.CreateShaderResourceView(null, accelStructSrvDesc, m_Staging.Descriptor.CpuHandle);

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestinationAccelerationStructureData = m_NativeResultBuffer.GPUVirtualAddress;
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer.GPUVirtualAddress;
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            RHIOpacityMicromapContract.ValidateTlasDescriptor(m_Dx12Device, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(m_Dx12Device, in descriptor);
            RHIAccelStructMotionContract.RejectMotionModeSwitch(
                RHIAccelStructMotionContract.UsesMotionFlag(m_Descriptor.Flag),
                descriptor.Flag);

            Span<RHIAccelStructInstance> asInstances = descriptor.Instances.Span;
            Vortice.Direct3D12.RaytracingInstanceDescription* nativeInstanceDescriptions = stackalloc Vortice.Direct3D12.RaytracingInstanceDescription[descriptor.Instances.Length];
            Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags nativeFlags =
                Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag) |
                Vortice.Direct3D12.RaytracingAccelerationStructureBuildFlags.PerformUpdate;

            for (int i = 0; i < descriptor.Instances.Length; ++i)
            {
                ref RHIAccelStructInstance asInstance = ref asInstances[i];
                Dx12BottomLevelAccelStruct accelStruct = asInstance.BottomLevelAccelStruct as Dx12BottomLevelAccelStruct ?? throw new ArgumentException("TLAS instance must reference a Dx12BottomLevelAccelStruct.", nameof(descriptor));

                ref Vortice.Direct3D12.RaytracingInstanceDescription nativeInstanceDescription = ref nativeInstanceDescriptions[i];
                {
                    nativeInstanceDescription.Transform = new Vortice.Mathematics.Matrix3x4(
                        asInstance.TransformMatrix.c0.x, asInstance.TransformMatrix.c1.x, asInstance.TransformMatrix.c2.x, asInstance.TransformMatrix.c3.x,
                        asInstance.TransformMatrix.c0.y, asInstance.TransformMatrix.c1.y, asInstance.TransformMatrix.c2.y, asInstance.TransformMatrix.c3.y,
                        asInstance.TransformMatrix.c0.z, asInstance.TransformMatrix.c1.z, asInstance.TransformMatrix.c2.z, asInstance.TransformMatrix.c3.z);

                    nativeInstanceDescription.Flags = (Vortice.Direct3D12.RaytracingInstanceFlags)asInstance.Flag;
                    nativeInstanceDescription.InstanceID = (Vortice.UInt24)asInstance.InstanceID;
                    nativeInstanceDescription.InstanceMask = asInstance.InstanceMask;
                    nativeInstanceDescription.InstanceContributionToHitGroupIndex = (Vortice.UInt24)asInstance.HitGroupIndex;
                    nativeInstanceDescription.AccelerationStructure = accelStruct.NativeResultBuffer.GPUVirtualAddress;
                }
            }

            void* data;
            m_NativeInstancesBuffer.Map(0, null, &data);
            MemoryUtility.MemCpy(nativeInstanceDescriptions, data, (uint)sizeof(Vortice.Direct3D12.RaytracingInstanceDescription) * (uint)descriptor.Instances.Length);
            m_NativeInstancesBuffer.Unmap(0, null);

            Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs nativeAccelStructDescriptor = new Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs();
            {
                nativeAccelStructDescriptor.Type = Vortice.Direct3D12.RaytracingAccelerationStructureType.TopLevel;
                nativeAccelStructDescriptor.Flags = nativeFlags;
                nativeAccelStructDescriptor.Layout = Vortice.Direct3D12.ElementsLayout.Array;
                nativeAccelStructDescriptor.DescriptorsCount = (uint)descriptor.Instances.Length;
                nativeAccelStructDescriptor.InstanceDescriptions = m_NativeInstancesBuffer.GPUVirtualAddress + descriptor.Offset;
            }

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestinationAccelerationStructureData = m_NativeResultBuffer.GPUVirtualAddress;
            m_NativeAccelStructDescriptor.SourceAccelerationStructureData = m_NativeResultBuffer.GPUVirtualAddress;
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer.GPUVirtualAddress;
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
            if (m_HasDescriptors)
            {
                m_Dx12Device.FreeStagingCbvSrvUavDescriptor(m_Staging);
                m_HasDescriptors = false;
            }
            m_NativeResultBuffer.Release();
            m_NativeScratchBuffer.Release();
            m_NativeInstancesBuffer.Release();
        }
    }

    internal unsafe class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        public Dx12Device Device => m_Dx12Device;
        public Vortice.Direct3D12.ID3D12Resource NativeResultBuffer => m_NativeResultBuffer;
        public Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription NativeAccelStructDescriptor => m_NativeAccelStructDescriptor;

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResultBuffer;
        private Vortice.Direct3D12.ID3D12Resource m_NativeScratchBuffer;
        private Vortice.Direct3D12.ID3D12Resource? m_NativeCurveAabbBuffer;
        private Vortice.Direct3D12.RaytracingGeometryDescription[] m_NativeGeometryDescriptions;
        private Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription m_NativeAccelStructDescriptor;
        private IntPtr[]? m_NativeOmmTriangleDescs;
        private IntPtr[]? m_NativeOmmLinkageDescs;
        private Vortice.Direct3D12.ID3D12Resource?[]? m_NativeOmmSpecialIndexBuffers;

        public Dx12BottomLevelAccelStruct(Dx12Device device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Dx12Device = device;
            RHIOpacityMicromapContract.ValidateBlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateBlasDescriptor(device, in descriptor);
            m_Descriptor = descriptor;
            m_NativeCurveAabbBuffer = null;
            m_NativeOmmTriangleDescs = null;
            m_NativeOmmLinkageDescs = null;
            m_NativeOmmSpecialIndexBuffers = null;

            int geometryCount = descriptor.Geometries.Length;
            if (geometryCount == 0)
            {
                throw new InvalidOperationException("Bottom-level acceleration structure requires at least one geometry descriptor.");
            }

            m_NativeGeometryDescriptions = new Vortice.Direct3D12.RaytracingGeometryDescription[geometryCount];
            Span<Vortice.Direct3D12.RaytracingGeometryDescription> nativeGeometryDescriptions = m_NativeGeometryDescriptions;

            for (int i = 0; i < descriptor.Geometries.Length; ++i)
            {
                RHIAccelStructGeometry asGeometry = descriptor.Geometries[i];
                ref Vortice.Direct3D12.RaytracingGeometryDescription nativeGeometryDescription = ref nativeGeometryDescriptions[i];

                switch (asGeometry.GeometryType)
                {
                    case ERHIAccelStructGeometryType.AABB:
                        if (asGeometry is not RHIAccelStructAABBs aabbGeometry)
                        {
                            throw new ArgumentException("DX12 AABB geometry descriptor has an unexpected type.", nameof(descriptor));
                        }
                        Dx12Buffer aabbBuffer = aabbGeometry.AABBBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));

                        nativeGeometryDescription.Type = Vortice.Direct3D12.RaytracingGeometryType.ProceduralPrimitiveAabbs;
                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

                        ref Vortice.Direct3D12.RaytracingGeometryAabbsDescription nativeAABBGeometry = ref nativeGeometryDescription.AABBs;
                        nativeAABBGeometry.AABBCount = aabbGeometry.Count;
                        nativeAABBGeometry.AABBs.StartAddress = aabbBuffer.NativeResource.GPUVirtualAddress + aabbGeometry.Offset;
                        nativeAABBGeometry.AABBs.StrideInBytes = aabbGeometry.Stride;
                        break;

                    case ERHIAccelStructGeometryType.Triangle:
                        if (asGeometry is not RHIAccelStructTriangles triangleGeometry)
                        {
                            throw new ArgumentException("DX12 triangle geometry descriptor has an unexpected type.", nameof(descriptor));
                        }
                        RHIOpacityMicromapContract.ValidateTriangleAttachment(triangleGeometry);
                        RHIAccelStructMotionContract.ValidateTriangleAttachment(triangleGeometry);
                        Dx12Buffer indexBuffer = triangleGeometry.IndexBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));
                        Dx12Buffer vertexBuffer = triangleGeometry.VertexBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));

                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

                        ref Vortice.Direct3D12.RaytracingGeometryTrianglesDescription nativeTriangleGeometry = ref nativeGeometryDescription.Triangles;
                        nativeTriangleGeometry.IndexCount = triangleGeometry.IndexCount;
                        nativeTriangleGeometry.IndexBuffer = indexBuffer.NativeResource.GPUVirtualAddress + triangleGeometry.IndexOffset;
                        nativeTriangleGeometry.IndexFormat = Dx12Utility.ConvertToDx12IndexFormat(triangleGeometry.IndexFormat);
                        nativeTriangleGeometry.VertexCount = triangleGeometry.VertexCount;
                        nativeTriangleGeometry.VertexBuffer.StartAddress = vertexBuffer.NativeResource.GPUVirtualAddress + triangleGeometry.VertexOffset;
                        nativeTriangleGeometry.VertexBuffer.StrideInBytes = triangleGeometry.VertexStride;
                        Vortice.DXGI.Format vertexFormat = Dx12Utility.ConvertToDx12ViewFormat(triangleGeometry.VertexFormat);
                        if (vertexFormat == Vortice.DXGI.Format.R32G32B32A32_Float)
                        {
                            // DXR triangles require xyz vertex format. Keep float4 layout by using xyz format + explicit stride.
                            vertexFormat = Vortice.DXGI.Format.R32G32B32_Float;
                        }

                        nativeTriangleGeometry.VertexFormat = vertexFormat;

                        if (triangleGeometry.OpacityMicromap != null)
                        {
                            AttachOpacityMicromap(
                                ref nativeGeometryDescription,
                                i,
                                triangleGeometry,
                                nativeTriangleGeometry);
                        }
                        else
                        {
                            nativeGeometryDescription.Type = Vortice.Direct3D12.RaytracingGeometryType.Triangles;
                        }
                        break;

                    case ERHIAccelStructGeometryType.Curves:
                        if (asGeometry is not RHIAccelStructCurves curveGeometry)
                        {
                            throw new ArgumentException("DX12 curve geometry descriptor has an unexpected type.", nameof(descriptor));
                        }
                        Dx12Buffer controlPointBuffer = curveGeometry.ControlPointBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));
                        Dx12Buffer radiusBuffer = curveGeometry.RadiusBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));
                        Dx12Buffer curveIndexBuffer = curveGeometry.IndexBuffer as Dx12Buffer ?? throw new ArgumentException("DX12 acceleration-structure geometry requires a Dx12Buffer.", nameof(descriptor));
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

                        uint curveAabbBufferSize = curveAabbCount * (uint)sizeof(Vortice.Direct3D12.RaytracingAabb);

                        // Allocate GPU buffer for AABB data
                        m_NativeCurveAabbBuffer = Dx12RaytracingHelper.CreateBuffer(
                            m_Dx12Device.NativeDevice,
                            curveAabbBufferSize,
                            Vortice.Direct3D12.ResourceFlags.None,
                            Vortice.Direct3D12.ResourceStates.GenericRead,
                            Dx12RaytracingHelper.kUploadHeapProps);

                        // Compute conservative AABBs from curve control points with per-point radius inflation.
                        {
                            void* pAabbData;
                            m_NativeCurveAabbBuffer.Map(0, null, &pAabbData);
                            Vortice.Direct3D12.RaytracingAabb* aabbs = (Vortice.Direct3D12.RaytracingAabb*)pAabbData;

                            void* pControlPointData = null;
                            void* pRadiusData = null;
                            void* pIndexData = null;
                            controlPointBuffer.NativeResource.Map(0, null, &pControlPointData);
                            radiusBuffer.NativeResource.Map(0, null, &pRadiusData);
                            if (curveIndexBuffer != null)
                            {
                                curveIndexBuffer.NativeResource.Map(0, null, &pIndexData);
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
                                    curveIndexBuffer.NativeResource.Unmap(0, null);
                                }
                                radiusBuffer.NativeResource.Unmap(0, null);
                                controlPointBuffer.NativeResource.Unmap(0, null);
                                m_NativeCurveAabbBuffer.Unmap(0, null);
                            }
                        }

                        nativeGeometryDescription.Type = Vortice.Direct3D12.RaytracingGeometryType.ProceduralPrimitiveAabbs;
                        nativeGeometryDescription.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(asGeometry.GeometryFlag);

                        ref Vortice.Direct3D12.RaytracingGeometryAabbsDescription nativeCurveAABBGeometry = ref nativeGeometryDescription.AABBs;
                        nativeCurveAABBGeometry.AABBCount = curveAabbCount;
                        nativeCurveAABBGeometry.AABBs.StartAddress = m_NativeCurveAabbBuffer.GPUVirtualAddress;
                        nativeCurveAABBGeometry.AABBs.StrideInBytes = (ulong)sizeof(Vortice.Direct3D12.RaytracingAabb);
                        break;
                }
            }

            Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs nativeAccelStructDescriptor = new Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs();
            {
                nativeAccelStructDescriptor.Type = Vortice.Direct3D12.RaytracingAccelerationStructureType.BottomLevel;
                nativeAccelStructDescriptor.Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag);
                nativeAccelStructDescriptor.Layout = Vortice.Direct3D12.ElementsLayout.Array;
                nativeAccelStructDescriptor.DescriptorsCount = (uint)descriptor.Geometries.Length;
                nativeAccelStructDescriptor.GeometryDescriptions = m_NativeGeometryDescriptions;
            }

            Vortice.Direct3D12.RaytracingAccelerationStructurePrebuildInfo nativeAccelStructPrebuildInfo = m_Dx12Device.NativeDevice.GetRaytracingAccelerationStructurePrebuildInfo(nativeAccelStructDescriptor);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ScratchDataSizeInBytes, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess, Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.UnorderedAccess, Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(m_Dx12Device.NativeDevice, (uint)nativeAccelStructPrebuildInfo.ResultDataMaxSizeInBytes, Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess, Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure, Dx12RaytracingHelper.kDefaultHeapProps);

            m_NativeAccelStructDescriptor.Inputs = nativeAccelStructDescriptor;
            m_NativeAccelStructDescriptor.DestinationAccelerationStructureData = m_NativeResultBuffer.GPUVirtualAddress;
            m_NativeAccelStructDescriptor.ScratchAccelerationStructureData = m_NativeScratchBuffer.GPUVirtualAddress;
        }

        private static void BuildCurveAabbs(
            Vortice.Direct3D12.RaytracingAabb* aabbs,
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

        private void AttachOpacityMicromap(
            ref Vortice.Direct3D12.RaytracingGeometryDescription nativeGeometryDescription,
            in int geometryIndex,
            in RHIAccelStructTriangles triangleGeometry,
            in Vortice.Direct3D12.RaytracingGeometryTrianglesDescription triangleDesc)
        {
            Dx12OpacityMicromap micromap = triangleGeometry.OpacityMicromap as Dx12OpacityMicromap
                ?? throw new ArgumentException("DX12 triangle OMM attachment requires a Dx12OpacityMicromap.");

            m_NativeOmmTriangleDescs ??= new IntPtr[m_NativeGeometryDescriptions.Length];
            m_NativeOmmLinkageDescs ??= new IntPtr[m_NativeGeometryDescriptions.Length];
            m_NativeOmmSpecialIndexBuffers ??= new Vortice.Direct3D12.ID3D12Resource?[m_NativeGeometryDescriptions.Length];

            ulong indexAddress;
            Vortice.DXGI.Format indexFormat;
            if (triangleGeometry.OpacityMicromapIndexBuffer != null)
            {
                Dx12Buffer indexBuffer = triangleGeometry.OpacityMicromapIndexBuffer as Dx12Buffer
                    ?? throw new ArgumentException("DX12 OMM index buffer must be a Dx12Buffer.");
                indexAddress = indexBuffer.NativeResource.GPUVirtualAddress + triangleGeometry.OpacityMicromapIndexOffset;
                indexFormat = Dx12Utility.ConvertToDx12IndexFormat(triangleGeometry.OpacityMicromapIndexFormat);
            }
            else
            {
                uint triangleCount = triangleGeometry.IndexBuffer != null
                    ? triangleGeometry.IndexCount / 3u
                    : triangleGeometry.VertexCount / 3u;
                if (triangleCount == 0)
                {
                    throw new InvalidOperationException("OMM special-index attachment requires at least one triangle.");
                }

                uint bufferSize = triangleCount * sizeof(uint);
                Vortice.Direct3D12.ID3D12Resource specialIndexBuffer = Dx12RaytracingHelper.CreateBuffer(
                    m_Dx12Device.NativeDevice,
                    bufferSize,
                    Vortice.Direct3D12.ResourceFlags.None,
                    Vortice.Direct3D12.ResourceStates.GenericRead,
                    Dx12RaytracingHelper.kUploadHeapProps);
                m_NativeOmmSpecialIndexBuffers[geometryIndex] = specialIndexBuffer;

                uint specialIndex = unchecked((uint)(int)triangleGeometry.OpacityMicromapSpecialIndex);
                void* mapped = null;
                specialIndexBuffer.Map(0, null, &mapped);
                try
                {
                    uint* indices = (uint*)mapped;
                    for (uint i = 0; i < triangleCount; ++i)
                    {
                        indices[i] = specialIndex;
                    }
                }
                finally
                {
                    specialIndexBuffer.Unmap(0, null);
                }

                indexAddress = specialIndexBuffer.GPUVirtualAddress;
                indexFormat = Vortice.DXGI.Format.R32_UInt;
            }

            IntPtr trianglesAlloc = (IntPtr)NativeMemory.Alloc((nuint)sizeof(Vortice.Direct3D12.RaytracingGeometryTrianglesDescription));
            IntPtr linkageAlloc = (IntPtr)NativeMemory.Alloc((nuint)sizeof(Vortice.Direct3D12.RaytracingGeometryOmmLinkageDescription));
            m_NativeOmmTriangleDescs[geometryIndex] = trianglesAlloc;
            m_NativeOmmLinkageDescs[geometryIndex] = linkageAlloc;
            *(Vortice.Direct3D12.RaytracingGeometryTrianglesDescription*)trianglesAlloc = triangleDesc;
            uint indexStride = triangleGeometry.OpacityMicromapIndexStride == 0
                ? (indexFormat == Vortice.DXGI.Format.R16_UInt ? 2u : 4u)
                : triangleGeometry.OpacityMicromapIndexStride;
            *(Vortice.Direct3D12.RaytracingGeometryOmmLinkageDescription*)linkageAlloc =
                new Vortice.Direct3D12.RaytracingGeometryOmmLinkageDescription
                {
                    OpacityMicromapIndexBuffer = new Vortice.Direct3D12.GpuVirtualAddressAndStride(
                        indexAddress,
                        indexStride),
                    OpacityMicromapIndexFormat = indexFormat,
                    OpacityMicromapBaseLocation = 0,
                    OpacityMicromapArray = micromap.ResultBuffer.GPUVirtualAddress,
                };

            nativeGeometryDescription.Type = Vortice.Direct3D12.RaytracingGeometryType.OmmTriangles;
            nativeGeometryDescription.OmmTriangles = new Vortice.Direct3D12.RaytracingGeometryOmmTrianglesDescription
            {
                Triangles = trianglesAlloc,
                OmmLinkage = linkageAlloc,
            };
        }

        protected override void Release()
        {
            if (m_NativeOmmTriangleDescs != null)
            {
                for (int i = 0; i < m_NativeOmmTriangleDescs.Length; ++i)
                {
                    if (m_NativeOmmTriangleDescs[i] != IntPtr.Zero)
                    {
                        NativeMemory.Free(m_NativeOmmTriangleDescs[i].ToPointer());
                        m_NativeOmmTriangleDescs[i] = IntPtr.Zero;
                    }
                }

                m_NativeOmmTriangleDescs = null;
            }

            if (m_NativeOmmLinkageDescs != null)
            {
                for (int i = 0; i < m_NativeOmmLinkageDescs.Length; ++i)
                {
                    if (m_NativeOmmLinkageDescs[i] != IntPtr.Zero)
                    {
                        NativeMemory.Free(m_NativeOmmLinkageDescs[i].ToPointer());
                        m_NativeOmmLinkageDescs[i] = IntPtr.Zero;
                    }
                }

                m_NativeOmmLinkageDescs = null;
            }

            if (m_NativeOmmSpecialIndexBuffers != null)
            {
                for (int i = 0; i < m_NativeOmmSpecialIndexBuffers.Length; ++i)
                {
                    m_NativeOmmSpecialIndexBuffers[i]?.Release();
                    m_NativeOmmSpecialIndexBuffers[i] = null;
                }

                m_NativeOmmSpecialIndexBuffers = null;
            }

            if (m_NativeCurveAabbBuffer != null)
            {
                m_NativeCurveAabbBuffer.Release();
                m_NativeCurveAabbBuffer = null;
            }
            m_NativeResultBuffer.Release();
            m_NativeScratchBuffer.Release();
        }
    }

    internal unsafe class Dx12OpacityMicromap : RHIOpacityMicromap
    {
        public Dx12Device Device => m_Dx12Device;
        public Vortice.Direct3D12.ID3D12Resource ResultBuffer => m_NativeResultBuffer;
        public Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription NativeBuildDescription => m_NativeBuildDescription;

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResultBuffer;
        private Vortice.Direct3D12.ID3D12Resource m_NativeScratchBuffer;
        private IntPtr m_NativeArrayDesc;
        private IntPtr m_NativeHistogram;
        private Vortice.Direct3D12.BuildRaytracingAccelerationStructureDescription m_NativeBuildDescription;

        public Dx12OpacityMicromap(Dx12Device device, in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            Vortice.Direct3D12.RaytracingAccelerationStructurePrebuildInfo prebuild =
                QueryPrebuildInfo(device, in descriptor, out m_NativeArrayDesc, out m_NativeHistogram);

            m_NativeScratchBuffer = Dx12RaytracingHelper.CreateBuffer(
                device.NativeDevice,
                (uint)prebuild.ScratchDataSizeInBytes,
                Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess,
                Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.UnorderedAccess,
                Dx12RaytracingHelper.kDefaultHeapProps);
            m_NativeResultBuffer = Dx12RaytracingHelper.CreateBuffer(
                device.NativeDevice,
                (uint)prebuild.ResultDataMaxSizeInBytes,
                Vortice.Direct3D12.ResourceFlags.AllowUnorderedAccess,
                Vortice.Direct3D12.ResourceStates.Common | Vortice.Direct3D12.ResourceStates.RaytracingAccelerationStructure,
                Dx12RaytracingHelper.kDefaultHeapProps);

            Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs inputs = CreateInputs(in descriptor, m_NativeArrayDesc);
            m_NativeBuildDescription.Inputs = inputs;
            m_NativeBuildDescription.DestinationAccelerationStructureData = m_NativeResultBuffer.GPUVirtualAddress;
            m_NativeBuildDescription.ScratchAccelerationStructureData = m_NativeScratchBuffer.GPUVirtualAddress;
        }

        public static RHIOpacityMicromapMemoryRequirements QueryMemoryRequirements(
            Dx12Device device,
            in RHIOpacityMicromapBuildDescriptor descriptor)
        {
            Vortice.Direct3D12.RaytracingAccelerationStructurePrebuildInfo prebuild =
                QueryPrebuildInfo(device, in descriptor, out IntPtr arrayDesc, out IntPtr histogram);
            NativeMemory.Free(arrayDesc.ToPointer());
            NativeMemory.Free(histogram.ToPointer());
            return new RHIOpacityMicromapMemoryRequirements
            {
                ResultSizeInBytes = prebuild.ResultDataMaxSizeInBytes,
                ScratchSizeInBytes = prebuild.ScratchDataSizeInBytes,
                UpdateScratchSizeInBytes = prebuild.UpdateScratchDataSizeInBytes,
            };
        }

        private static Vortice.Direct3D12.RaytracingAccelerationStructurePrebuildInfo QueryPrebuildInfo(
            Dx12Device device,
            in RHIOpacityMicromapBuildDescriptor descriptor,
            out IntPtr arrayDesc,
            out IntPtr histogram)
        {
            AllocateNativeArrayDesc(in descriptor, out arrayDesc, out histogram);
            Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs inputs = CreateInputs(in descriptor, arrayDesc);
            return device.NativeDevice.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
        }

        private static Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs CreateInputs(
            in RHIOpacityMicromapBuildDescriptor descriptor,
            in IntPtr arrayDesc)
        {
            return new Vortice.Direct3D12.BuildRaytracingAccelerationStructureInputs
            {
                Type = Vortice.Direct3D12.RaytracingAccelerationStructureType.OpacityMicromapArray,
                Flags = Dx12Utility.ConvertToDx12AccelStructGeometryFlag(descriptor.Flag),
                Layout = Vortice.Direct3D12.ElementsLayout.Array,
                DescriptorsCount = 1,
                OpacityMicromapArrayDesc = arrayDesc,
            };
        }

        private static void AllocateNativeArrayDesc(
            in RHIOpacityMicromapBuildDescriptor descriptor,
            out IntPtr arrayDesc,
            out IntPtr histogram)
        {
            Dx12Buffer inputBuffer = descriptor.InputBuffer as Dx12Buffer
                ?? throw new ArgumentException("DX12 opacity micromap input buffer must be a Dx12Buffer.");
            Dx12Buffer triangleArrayBuffer = descriptor.TriangleArrayBuffer as Dx12Buffer
                ?? throw new ArgumentException("DX12 opacity micromap triangle-array buffer must be a Dx12Buffer.");

            int histogramBytes = descriptor.UsageCounts.Length * sizeof(Dx12OmmHistogramEntry);
            histogram = (IntPtr)NativeMemory.Alloc((nuint)histogramBytes);
            Dx12OmmHistogramEntry* histogramPtr = (Dx12OmmHistogramEntry*)histogram;
            for (int i = 0; i < descriptor.UsageCounts.Length; ++i)
            {
                RHIOpacityMicromapUsageCount usage = descriptor.UsageCounts[i];
                histogramPtr[i] = new Dx12OmmHistogramEntry
                {
                    Count = usage.Count,
                    SubdivisionLevel = usage.SubdivisionLevel,
                    Format = (uint)usage.Format,
                };
            }

            arrayDesc = (IntPtr)NativeMemory.Alloc((nuint)sizeof(Dx12OmmArrayDesc));
            Dx12OmmArrayDesc* arrayPtr = (Dx12OmmArrayDesc*)arrayDesc;
            arrayPtr->NumOmmHistogramEntries = (uint)descriptor.UsageCounts.Length;
            arrayPtr->pOmmHistogram = histogram;
            arrayPtr->InputBuffer = inputBuffer.NativeResource.GPUVirtualAddress + descriptor.InputBufferOffset;
            arrayPtr->PerOmmDescsStartAddress = triangleArrayBuffer.NativeResource.GPUVirtualAddress + descriptor.TriangleArrayOffset;
            arrayPtr->PerOmmDescsStrideInBytes = descriptor.TriangleArrayStride == 0
                ? (ulong)sizeof(Vortice.Direct3D12.RaytracingOpacityMicromapDescription)
                : descriptor.TriangleArrayStride;
        }

        protected override void Release()
        {
            if (m_NativeArrayDesc != IntPtr.Zero)
            {
                NativeMemory.Free(m_NativeArrayDesc.ToPointer());
                m_NativeArrayDesc = IntPtr.Zero;
            }

            if (m_NativeHistogram != IntPtr.Zero)
            {
                NativeMemory.Free(m_NativeHistogram.ToPointer());
                m_NativeHistogram = IntPtr.Zero;
            }

            m_NativeResultBuffer.Release();
            m_NativeScratchBuffer.Release();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dx12OmmHistogramEntry
    {
        public uint Count;
        public uint SubdivisionLevel;
        public uint Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Dx12OmmArrayDesc
    {
        public uint NumOmmHistogramEntries;
        public IntPtr pOmmHistogram;
        public ulong InputBuffer;
        public ulong PerOmmDescsStartAddress;
        public ulong PerOmmDescsStrideInBytes;
    }
#pragma warning restore CA1416
}
