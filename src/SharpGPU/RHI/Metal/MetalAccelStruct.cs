using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal static class MetalArrayHelper
    {
        private static readonly ObjectiveCClass s_NsArrayClass = new ObjectiveCClass("NSArray");
        private static readonly Selector s_ArrayWithObjectsCount = "arrayWithObjects:count:";

        internal static unsafe NSArray CreateNSArrayFromPointers(ReadOnlySpan<IntPtr> objects)
        {
            if (objects.Length == 0)
            {
                return default;
            }

            fixed (IntPtr* pointers = objects)
            {
                IntPtr arrayPtr = ObjectiveCRuntime.IntPtr_objc_msgSend(s_NsArrayClass, s_ArrayWithObjectsCount, (IntPtr)pointers, (ulong)objects.Length);
                return new NSArray(arrayPtr);
            }
        }
    }

    internal sealed class MetalBottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        internal MTLAccelerationStructure NativeAccelerationStructure => m_NativeAccelerationStructure;
        internal MTLPrimitiveAccelerationStructureDescriptor NativeDescriptor => m_NativeDescriptor;
        internal MTLBuffer NativeScratchBuffer => m_NativeScratchBuffer;

        private readonly MetalDevice m_MetalDevice;
        private readonly List<IntPtr> m_GeometryDescriptors;
        private MTLPrimitiveAccelerationStructureDescriptor m_NativeDescriptor;
        private MTLAccelerationStructure m_NativeAccelerationStructure;
        private MTLBuffer m_NativeScratchBuffer;

        internal MetalBottomLevelAccelStruct(MetalDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_Descriptor = descriptor;
            m_GeometryDescriptors = new List<IntPtr>(Math.Max(1, descriptor.Geometries.Length));

            m_NativeDescriptor = MTLPrimitiveAccelerationStructureDescriptor.New();
            m_NativeDescriptor.Usage = MTLAccelerationStructureUsage.None;
            BuildGeometryDescriptors(descriptor.Geometries);

            MTLAccelerationStructureSizes sizes = m_MetalDevice.NativeDevice.AccelerationStructureSizes(m_NativeDescriptor);
            if (sizes.accelerationStructureSize == 0 || sizes.buildScratchBufferSize == 0)
            {
                throw new InvalidOperationException("Metal BLAS size query returned zero-sized allocation.");
            }

            m_NativeAccelerationStructure = m_MetalDevice.NativeDevice.NewAccelerationStructure(sizes.accelerationStructureSize);
            m_NativeScratchBuffer = m_MetalDevice.NativeDevice.NewBuffer(sizes.buildScratchBufferSize, MTLResourceOptions.ResourceStorageModePrivate);
            if (m_NativeAccelerationStructure.NativePtr == IntPtr.Zero || m_NativeScratchBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate Metal BLAS resources.");
            }
        }

        private void BuildGeometryDescriptors(RHIAccelStructGeometry[] geometries)
        {
            if (geometries == null || geometries.Length == 0)
            {
                throw new InvalidOperationException("BLAS requires at least one geometry.");
            }

            IntPtr[] geometryDescriptorPtrs = new IntPtr[geometries.Length];
            for (int i = 0; i < geometries.Length; ++i)
            {
                RHIAccelStructGeometry geometry = geometries[i];
                if (geometry == null)
                {
                    throw new InvalidOperationException($"BLAS geometry[{i}] is null.");
                }

                IntPtr geometryDescriptorPtr = geometry.GeometryType switch
                {
                    EAccelStructGeometryType.Triangle => CreateTriangleGeometryDescriptor((RHIAccelStructTriangles)geometry),
                    EAccelStructGeometryType.AABB => CreateAabbGeometryDescriptor((RHIAccelStructAABBs)geometry),
                    EAccelStructGeometryType.Curves => CreateCurveGeometryDescriptor((RHIAccelStructCurves)geometry),
                    _ => throw new NotSupportedException($"Unsupported geometry type '{geometry.GeometryType}'.")
                };

                geometryDescriptorPtrs[i] = geometryDescriptorPtr;
                m_GeometryDescriptors.Add(geometryDescriptorPtr);
            }

            m_NativeDescriptor.GeometryDescriptors = MetalArrayHelper.CreateNSArrayFromPointers(geometryDescriptorPtrs);
        }

        private static IntPtr CreateTriangleGeometryDescriptor(RHIAccelStructTriangles geometry)
        {
            if (geometry.VertexBuffer is not MetalBuffer vertexBuffer)
            {
                throw new InvalidOperationException("Triangle geometry vertex buffer is missing or not a Metal buffer.");
            }

            MTLAccelerationStructureTriangleGeometryDescriptor descriptor = MTLAccelerationStructureTriangleGeometryDescriptor.New();
            descriptor.IntersectionFunctionTableOffset = geometry.FunctionTableOffset;
            descriptor.Opaque = MetalUtility.IsMetalGeometryOpaque(geometry.GeometryFlag);
            descriptor.AllowDuplicateIntersectionFunctionInvocation = MetalUtility.AllowMetalDuplicateIntersectionInvocation(geometry.GeometryFlag);
            descriptor.VertexBuffer = vertexBuffer.NativeBuffer;
            descriptor.VertexBufferOffset = geometry.VertexOffset;
            descriptor.VertexStride = geometry.VertexStride;
            descriptor.VertexFormat = MetalUtility.ConvertToMetalAttributeFormat(geometry.VertexFormat);

            if (geometry.IndexBuffer is MetalBuffer indexBuffer && geometry.IndexCount > 0)
            {
                descriptor.IndexBuffer = indexBuffer.NativeBuffer;
                descriptor.IndexBufferOffset = geometry.IndexOffset;
                descriptor.IndexType = MetalUtility.ConvertToMetalIndexType(geometry.IndexFormat);
                descriptor.TriangleCount = geometry.IndexCount / 3u;
            }
            else if (geometry.VertexCount > 0)
            {
                descriptor.TriangleCount = geometry.VertexCount / 3u;
            }
            else
            {
                throw new InvalidOperationException("Triangle geometry has no valid index/vertex count.");
            }

            return descriptor.NativePtr;
        }

        private static IntPtr CreateAabbGeometryDescriptor(RHIAccelStructAABBs geometry)
        {
            if (geometry.AABBBuffer is not MetalBuffer aabbBuffer)
            {
                throw new InvalidOperationException("AABB geometry buffer is missing or not a Metal buffer.");
            }

            MTLAccelerationStructureBoundingBoxGeometryDescriptor descriptor = MTLAccelerationStructureBoundingBoxGeometryDescriptor.New();
            descriptor.IntersectionFunctionTableOffset = geometry.FunctionTableOffset;
            descriptor.Opaque = MetalUtility.IsMetalGeometryOpaque(geometry.GeometryFlag);
            descriptor.AllowDuplicateIntersectionFunctionInvocation = MetalUtility.AllowMetalDuplicateIntersectionInvocation(geometry.GeometryFlag);
            descriptor.BoundingBoxBuffer = aabbBuffer.NativeBuffer;
            descriptor.BoundingBoxBufferOffset = geometry.Offset;
            descriptor.BoundingBoxStride = geometry.Stride;
            descriptor.BoundingBoxCount = geometry.Count;
            return descriptor.NativePtr;
        }

        private static IntPtr CreateCurveGeometryDescriptor(RHIAccelStructCurves geometry)
        {
            if (geometry.ControlPointBuffer is not MetalBuffer controlPointBuffer)
            {
                throw new InvalidOperationException("Curve control-point buffer is missing or not a Metal buffer.");
            }

            if (geometry.RadiusBuffer is not MetalBuffer radiusBuffer)
            {
                throw new InvalidOperationException("Curve radius buffer is missing or not a Metal buffer.");
            }

            MTLAccelerationStructureCurveGeometryDescriptor descriptor = MTLAccelerationStructureCurveGeometryDescriptor.New();
            descriptor.IntersectionFunctionTableOffset = geometry.FunctionTableOffset;
            descriptor.Opaque = MetalUtility.IsMetalGeometryOpaque(geometry.GeometryFlag);
            descriptor.AllowDuplicateIntersectionFunctionInvocation = MetalUtility.AllowMetalDuplicateIntersectionInvocation(geometry.GeometryFlag);
            descriptor.ControlPointBuffer = controlPointBuffer.NativeBuffer;
            descriptor.ControlPointBufferOffset = geometry.ControlPointOffset;
            descriptor.ControlPointCount = geometry.ControlPointCount;
            descriptor.ControlPointStride = geometry.ControlPointStride;
            descriptor.ControlPointFormat = MetalUtility.ConvertToMetalAttributeFormat(geometry.ControlPointFormat);
            descriptor.RadiusBuffer = radiusBuffer.NativeBuffer;
            descriptor.RadiusBufferOffset = geometry.RadiusOffset;
            descriptor.RadiusStride = geometry.RadiusStride;
            descriptor.RadiusFormat = MetalUtility.ConvertToMetalAttributeFormat(geometry.RadiusFormat);

            if (geometry.IndexBuffer is MetalBuffer indexBuffer)
            {
                descriptor.IndexBuffer = indexBuffer.NativeBuffer;
                descriptor.IndexBufferOffset = geometry.IndexOffset;
                descriptor.IndexType = MetalUtility.ConvertToMetalIndexType(geometry.IndexFormat);
            }

            descriptor.SegmentCount = geometry.SegmentCount;
            descriptor.SegmentControlPointCount = geometry.SegmentControlPointCount;
            descriptor.CurveType = MetalUtility.ConvertToMetalCurveType(geometry.CurveType);
            descriptor.CurveBasis = MetalUtility.ConvertToMetalCurveBasis(geometry.CurveBasis);
            descriptor.CurveEndCaps = MetalUtility.ConvertToMetalCurveEndCaps(geometry.CurveEndCaps);
            return descriptor.NativePtr;
        }

        protected override void Release()
        {
            if (m_NativeScratchBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeScratchBuffer);
                m_NativeScratchBuffer = default;
            }

            if (m_NativeAccelerationStructure.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeAccelerationStructure);
                m_NativeAccelerationStructure = default;
            }

            if (m_NativeDescriptor.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDescriptor);
                m_NativeDescriptor = default;
            }

            for (int i = 0; i < m_GeometryDescriptors.Count; ++i)
            {
                IntPtr descriptorPtr = m_GeometryDescriptors[i];
                if (descriptorPtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptorPtr);
                }
            }

            m_GeometryDescriptors.Clear();
        }
    }

    internal sealed class MetalTopLevelAccelStruct : RHITopLevelAccelStruct
    {
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct MetalInstanceDescriptorRaw
        {
            public fixed float Transform[12];
            public uint Options;
            public uint Mask;
            public uint IntersectionFunctionTableOffset;
            public uint AccelerationStructureIndex;
        }

        private const ulong kMetalInstanceDescriptorSize = 64UL;

        internal MTLAccelerationStructure NativeAccelerationStructure => m_NativeAccelerationStructure;
        internal MTLInstanceAccelerationStructureDescriptor NativeDescriptor => m_NativeDescriptor;
        internal MTLBuffer NativeScratchBuffer => m_NativeScratchBuffer;

        private readonly MetalDevice m_MetalDevice;
        private MTLInstanceAccelerationStructureDescriptor m_NativeDescriptor;
        private MTLAccelerationStructure m_NativeAccelerationStructure;
        private MTLBuffer m_NativeScratchBuffer;
        private MTLBuffer m_InstanceDescriptorBuffer;
        private NSArray m_InstancedAccelerationStructures;
        private ulong m_InstanceBufferSize;

        internal MetalTopLevelAccelStruct(MetalDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_MetalDevice = device;
            m_NativeDescriptor = MTLInstanceAccelerationStructureDescriptor.New();
            m_NativeDescriptor.InstanceDescriptorType = MTLAccelerationStructureInstanceDescriptorType.Default;
            m_NativeDescriptor.InstanceTransformationMatrixLayout = MTLMatrixLayout.ColumnMajor;
            m_NativeDescriptor.Usage = MetalUtility.ConvertToMetalAccelerationStructureUsage(descriptor.Flag);

            m_Descriptor = descriptor;
            UpdateNativeDescriptorAndBuffers(descriptor);
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            UpdateNativeDescriptorAndBuffers(descriptor);
        }

        private unsafe void UpdateNativeDescriptorAndBuffers(in RHITopLevelAccelStructDescriptor descriptor)
        {
            if (descriptor.Instances.Length == 0)
            {
                throw new InvalidOperationException("TLAS requires at least one instance.");
            }

            Dictionary<IntPtr, uint> accelIndexMap = new Dictionary<IntPtr, uint>(descriptor.Instances.Length);
            List<IntPtr> accelArray = new List<IntPtr>(descriptor.Instances.Length);
            MetalInstanceDescriptorRaw[] instanceDescriptors = new MetalInstanceDescriptorRaw[descriptor.Instances.Length];

            Span<RHIAccelStructInstance> instances = descriptor.Instances.Span;
            for (int i = 0; i < instances.Length; ++i)
            {
                ref RHIAccelStructInstance instance = ref instances[i];
                if (instance.BottomLevelAccelStruct is not MetalBottomLevelAccelStruct blas)
                {
                    throw new InvalidOperationException($"TLAS instance[{i}] references a non-Metal BLAS.");
                }

                IntPtr blasPtr = blas.NativeAccelerationStructure.NativePtr;
                if (!accelIndexMap.TryGetValue(blasPtr, out uint accelIndex))
                {
                    accelIndex = (uint)accelArray.Count;
                    accelIndexMap.Add(blasPtr, accelIndex);
                    accelArray.Add(blasPtr);
                }

                instanceDescriptors[i] = BuildRawInstanceDescriptor(instance.TransformMatrix, instance.Flag, instance.InstanceMask, instance.HitGroupIndex, accelIndex);
            }

            m_InstancedAccelerationStructures = MetalArrayHelper.CreateNSArrayFromPointers(accelArray.ToArray());
            m_NativeDescriptor.InstancedAccelerationStructures = m_InstancedAccelerationStructures;
            m_NativeDescriptor.InstanceCount = (ulong)instanceDescriptors.Length;
            m_NativeDescriptor.InstanceDescriptorStride = kMetalInstanceDescriptorSize;

            EnsureInstanceDescriptorBufferCapacity((ulong)instanceDescriptors.Length * kMetalInstanceDescriptorSize);
            fixed (MetalInstanceDescriptorRaw* src = instanceDescriptors)
            {
                Buffer.MemoryCopy(src, (void*)m_InstanceDescriptorBuffer.Contents, m_InstanceBufferSize, (ulong)instanceDescriptors.Length * kMetalInstanceDescriptorSize);
            }

            m_NativeDescriptor.InstanceDescriptorBuffer = m_InstanceDescriptorBuffer;
            m_NativeDescriptor.InstanceDescriptorBufferOffset = descriptor.Offset;

            MTLAccelerationStructureSizes sizes = m_MetalDevice.NativeDevice.AccelerationStructureSizes(m_NativeDescriptor);
            if (sizes.accelerationStructureSize == 0 || sizes.buildScratchBufferSize == 0)
            {
                throw new InvalidOperationException("Metal TLAS size query returned zero-sized allocation.");
            }

            RecreateAccelerationStructureResources(sizes.accelerationStructureSize, sizes.buildScratchBufferSize);
        }

        private static unsafe MetalInstanceDescriptorRaw BuildRawInstanceDescriptor(in float4x4 transform, in EAccelStructInstanceFlag flag, in byte mask, in uint functionTableOffset, in uint accelerationStructureIndex)
        {
            MetalInstanceDescriptorRaw descriptor = default;
            descriptor.Options = (uint)MetalUtility.ConvertToMetalAccelerationStructureInstanceOptions(flag);
            descriptor.Mask = mask;
            descriptor.IntersectionFunctionTableOffset = functionTableOffset;
            descriptor.AccelerationStructureIndex = accelerationStructureIndex;

            descriptor.Transform[0] = transform.c0.x;
            descriptor.Transform[1] = transform.c0.y;
            descriptor.Transform[2] = transform.c0.z;
            descriptor.Transform[3] = transform.c1.x;
            descriptor.Transform[4] = transform.c1.y;
            descriptor.Transform[5] = transform.c1.z;
            descriptor.Transform[6] = transform.c2.x;
            descriptor.Transform[7] = transform.c2.y;
            descriptor.Transform[8] = transform.c2.z;
            descriptor.Transform[9] = transform.c3.x;
            descriptor.Transform[10] = transform.c3.y;
            descriptor.Transform[11] = transform.c3.z;
            return descriptor;
        }

        private void EnsureInstanceDescriptorBufferCapacity(ulong requiredSize)
        {
            if (requiredSize == 0)
            {
                requiredSize = kMetalInstanceDescriptorSize;
            }

            if (m_InstanceDescriptorBuffer.NativePtr != IntPtr.Zero && m_InstanceBufferSize >= requiredSize)
            {
                return;
            }

            if (m_InstanceDescriptorBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_InstanceDescriptorBuffer);
                m_InstanceDescriptorBuffer = default;
            }

            m_InstanceDescriptorBuffer = m_MetalDevice.NativeDevice.NewBuffer(requiredSize, MTLResourceOptions.ResourceStorageModeShared);
            if (m_InstanceDescriptorBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate Metal TLAS instance descriptor buffer.");
            }

            m_InstanceBufferSize = requiredSize;
        }

        private void RecreateAccelerationStructureResources(ulong accelerationStructureSize, ulong scratchBufferSize)
        {
            if (m_NativeAccelerationStructure.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeAccelerationStructure);
                m_NativeAccelerationStructure = default;
            }

            if (m_NativeScratchBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeScratchBuffer);
                m_NativeScratchBuffer = default;
            }

            m_NativeAccelerationStructure = m_MetalDevice.NativeDevice.NewAccelerationStructure(accelerationStructureSize);
            m_NativeScratchBuffer = m_MetalDevice.NativeDevice.NewBuffer(scratchBufferSize, MTLResourceOptions.ResourceStorageModePrivate);
            if (m_NativeAccelerationStructure.NativePtr == IntPtr.Zero || m_NativeScratchBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate Metal TLAS resources.");
            }
        }

        protected override void Release()
        {
            if (m_NativeScratchBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeScratchBuffer);
                m_NativeScratchBuffer = default;
            }

            if (m_NativeAccelerationStructure.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeAccelerationStructure);
                m_NativeAccelerationStructure = default;
            }

            if (m_InstanceDescriptorBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_InstanceDescriptorBuffer);
                m_InstanceDescriptorBuffer = default;
            }

            if (m_NativeDescriptor.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDescriptor);
                m_NativeDescriptor = default;
            }
        }
    }
}
