using System;
using SharpMetal.Metal;
using SharpMath;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
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
        internal MetalDevice Device => m_MetalDevice;
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
            RHIOpacityMicromapContract.ValidateBlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateBlasDescriptor(device, in descriptor);
            m_Descriptor = descriptor;
            m_GeometryDescriptors = new List<IntPtr>(Math.Max(1, descriptor.Geometries.Length));

            m_NativeDescriptor = MTLPrimitiveAccelerationStructureDescriptor.New();
            m_NativeDescriptor.Usage = MTLAccelerationStructureUsage.None;
            if (RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag))
            {
                m_NativeDescriptor.MotionKeyframeCount = 2;
                m_NativeDescriptor.MotionStartTime = 0f;
                m_NativeDescriptor.MotionEndTime = 1f;
            }

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
                    ERHIAccelStructGeometryType.Triangle => CreateTriangleGeometryDescriptor(
                        m_MetalDevice,
                        (RHIAccelStructTriangles)geometry,
                        m_Descriptor.Flag),
                    ERHIAccelStructGeometryType.AABB => CreateAabbGeometryDescriptor((RHIAccelStructAABBs)geometry),
                    ERHIAccelStructGeometryType.Curves => CreateCurveGeometryDescriptor((RHIAccelStructCurves)geometry),
                    _ => throw new NotSupportedException($"Unsupported geometry type '{geometry.GeometryType}'.")
                };

                geometryDescriptorPtrs[i] = geometryDescriptorPtr;
                m_GeometryDescriptors.Add(geometryDescriptorPtr);
            }

            m_NativeDescriptor.GeometryDescriptors = MetalArrayHelper.CreateNSArrayFromPointers(geometryDescriptorPtrs);
        }

        private static IntPtr CreateTriangleGeometryDescriptor(
            MetalDevice device,
            RHIAccelStructTriangles geometry,
            in ERHIAccelStructFlag accelStructFlag)
        {
            RHIOpacityMicromapContract.ValidateTriangleAttachment(geometry);
            RHIAccelStructMotionContract.ValidateTriangleAttachment(geometry);
            if (geometry.OpacityMicromap != null)
            {
                device.Capabilities.RayTracing.OpacityMicromap.Require("RayTracing.OpacityMicromap");
            }

            if (geometry.VertexBuffer is not MetalBuffer vertexBuffer)
            {
                throw new InvalidOperationException("Triangle geometry vertex buffer is missing or not a Metal buffer.");
            }

            RHIAccelStructTriangleContract.ValidateIndexIntent(in geometry);
            MetalBuffer? metalIndexBuffer = null;
            uint triangleCount;
            if (RHIAccelStructTriangleContract.HasIndexIntent(in geometry))
            {
                if (geometry.IndexBuffer is not MetalBuffer indexBuffer)
                {
                    throw new InvalidOperationException(
                        "Indexed triangle geometry requires a Metal IndexBuffer.");
                }

                metalIndexBuffer = indexBuffer;
                triangleCount = geometry.IndexCount / 3u;
            }
            else if (geometry.VertexCount > 0)
            {
                triangleCount = geometry.VertexCount / 3u;
            }
            else
            {
                throw new InvalidOperationException("Triangle geometry has no valid index/vertex count.");
            }

            if (RHIAccelStructMotionContract.UsesMotionFlag(accelStructFlag) &&
                RHIAccelStructMotionContract.HasMotionTriangles(geometry))
            {
                if (geometry.MotionVertexBuffer is not MetalBuffer motionVertexBuffer)
                {
                    throw new InvalidOperationException("Motion triangle geometry requires a Metal MotionVertexBuffer.");
                }

                MTLAccelerationStructureMotionTriangleGeometryDescriptor motionDescriptor =
                    MTLAccelerationStructureMotionTriangleGeometryDescriptor.New();
                motionDescriptor.IntersectionFunctionTableOffset = geometry.FunctionTableOffset;
                motionDescriptor.Opaque = MetalUtility.IsMetalGeometryOpaque(geometry.GeometryFlag);
                motionDescriptor.AllowDuplicateIntersectionFunctionInvocation =
                    MetalUtility.AllowMetalDuplicateIntersectionInvocation(geometry.GeometryFlag);
                motionDescriptor.VertexStride = RHIAccelStructMotionContract.ResolveMotionVertexStride(in geometry);
                motionDescriptor.VertexFormat = MetalUtility.ConvertToMetalAttributeFormat(geometry.VertexFormat);
                motionDescriptor.TriangleCount = triangleCount;

                MTLMotionKeyframeData keyframe0 = MTLMotionKeyframeData.New();
                keyframe0.Buffer = vertexBuffer.NativeBuffer;
                keyframe0.Offset = geometry.VertexOffset;
                MTLMotionKeyframeData keyframe1 = MTLMotionKeyframeData.New();
                keyframe1.Buffer = motionVertexBuffer.NativeBuffer;
                keyframe1.Offset = geometry.MotionVertexOffset;
                motionDescriptor.VertexBuffers = MetalArrayHelper.CreateNSArrayFromPointers(
                    new[] { keyframe0.NativePtr, keyframe1.NativePtr });

                if (metalIndexBuffer != null)
                {
                    motionDescriptor.IndexBuffer = metalIndexBuffer.NativeBuffer;
                    motionDescriptor.IndexBufferOffset = geometry.IndexOffset;
                    motionDescriptor.IndexType = MetalUtility.ConvertToMetalIndexType(geometry.IndexFormat);
                }

                return motionDescriptor.NativePtr;
            }

            MTLAccelerationStructureTriangleGeometryDescriptor descriptor = MTLAccelerationStructureTriangleGeometryDescriptor.New();
            descriptor.IntersectionFunctionTableOffset = geometry.FunctionTableOffset;
            descriptor.Opaque = MetalUtility.IsMetalGeometryOpaque(geometry.GeometryFlag);
            descriptor.AllowDuplicateIntersectionFunctionInvocation = MetalUtility.AllowMetalDuplicateIntersectionInvocation(geometry.GeometryFlag);
            descriptor.VertexBuffer = vertexBuffer.NativeBuffer;
            descriptor.VertexBufferOffset = geometry.VertexOffset;
            descriptor.VertexStride = geometry.VertexStride;
            descriptor.VertexFormat = MetalUtility.ConvertToMetalAttributeFormat(geometry.VertexFormat);

            if (metalIndexBuffer != null)
            {
                descriptor.IndexBuffer = metalIndexBuffer.NativeBuffer;
                descriptor.IndexBufferOffset = geometry.IndexOffset;
                descriptor.IndexType = MetalUtility.ConvertToMetalIndexType(geometry.IndexFormat);
            }

            descriptor.TriangleCount = triangleCount;

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
        private const ulong kMetalMotionTransformSize = 48UL;

        internal MTLAccelerationStructure NativeAccelerationStructure => m_NativeAccelerationStructure;
        internal MTLInstanceAccelerationStructureDescriptor NativeDescriptor => m_NativeDescriptor;
        internal MTLBuffer NativeScratchBuffer => m_NativeScratchBuffer;
        internal MetalDevice Device => m_MetalDevice;

        private readonly MetalDevice m_MetalDevice;
        private MTLInstanceAccelerationStructureDescriptor m_NativeDescriptor;
        private MTLAccelerationStructure m_NativeAccelerationStructure;
        private MTLBuffer m_NativeScratchBuffer;
        private MTLBuffer m_InstanceDescriptorBuffer;
        private MTLBuffer m_MotionTransformBuffer;
        private NSArray m_InstancedAccelerationStructures;
        private ulong m_InstanceBufferSize;
        private ulong m_MotionTransformBufferSize;
        private bool m_UsesMotion;

        internal MetalTopLevelAccelStruct(MetalDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            RHIOpacityMicromapContract.ValidateTlasDescriptor(device, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(device, in descriptor);
            m_MetalDevice = device;
            m_UsesMotion = RHIAccelStructMotionContract.UsesMotionFlag(descriptor.Flag);
            m_NativeDescriptor = MTLInstanceAccelerationStructureDescriptor.New();
            m_NativeDescriptor.InstanceDescriptorType = m_UsesMotion
                ? MTLAccelerationStructureInstanceDescriptorType.Motion
                : MTLAccelerationStructureInstanceDescriptorType.Default;
            m_NativeDescriptor.InstanceTransformationMatrixLayout = MTLMatrixLayout.ColumnMajor;
            m_NativeDescriptor.Usage = MetalUtility.ConvertToMetalAccelerationStructureUsage(descriptor.Flag);

            UpdateNativeDescriptorAndBuffers(descriptor);
            m_Descriptor = descriptor;
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            RHIOpacityMicromapContract.ValidateTlasDescriptor(m_MetalDevice, in descriptor);
            RHIAccelStructMotionContract.ValidateTlasDescriptor(m_MetalDevice, in descriptor);
            RHIAccelStructMotionContract.RejectMotionModeSwitch(m_UsesMotion, descriptor.Flag);
            ReplaceTopLevelNativeState(in descriptor);
            m_Descriptor = descriptor;
        }

        private unsafe void UpdateNativeDescriptorAndBuffers(in RHITopLevelAccelStructDescriptor descriptor)
        {
            if (descriptor.Instances.Length == 0)
            {
                throw new InvalidOperationException("TLAS requires at least one instance.");
            }

            Dictionary<IntPtr, uint> accelIndexMap = new Dictionary<IntPtr, uint>(descriptor.Instances.Length);
            List<IntPtr> accelArray = new List<IntPtr>(descriptor.Instances.Length);
            Span<RHIAccelStructInstance> instances = descriptor.Instances.Span;

            if (m_UsesMotion)
            {
                ulong motionDescriptorSize = (ulong)Marshal.SizeOf<MTLAccelerationStructureMotionInstanceDescriptor>();
                MTLAccelerationStructureMotionInstanceDescriptor[] motionDescriptors =
                    new MTLAccelerationStructureMotionInstanceDescriptor[instances.Length];
                float[] motionTransforms = new float[instances.Length * 24];

                for (int i = 0; i < instances.Length; ++i)
                {
                    ref RHIAccelStructInstance instance = ref instances[i];
                    if (instance.MotionType == ERHIAccelStructMotionInstanceType.Srt)
                    {
                        throw new NotSupportedException(
                            "Metal motion instances use matrix keyframes; SRT motion is fail-closed on Metal.");
                    }

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

                    WritePackedTransform(motionTransforms, i * 24, instance.TransformMatrix);
                    WritePackedTransform(
                        motionTransforms,
                        i * 24 + 12,
                        instance.MotionType == ERHIAccelStructMotionInstanceType.Matrix
                            ? instance.MotionTransformMatrix
                            : instance.TransformMatrix);

                    motionDescriptors[i] = new MTLAccelerationStructureMotionInstanceDescriptor
                    {
                        options = MetalUtility.ConvertToMetalAccelerationStructureInstanceOptions(instance.Flag),
                        mask = instance.InstanceMask,
                        intersectionFunctionTableOffset = instance.HitGroupIndex,
                        accelerationStructureIndex = accelIndex,
                        userID = instance.InstanceID,
                        motionTransformsStartIndex = (uint)(i * 2),
                        motionTransformsCount = 2,
                        motionStartBorderMode = MTLMotionBorderMode.Clamp,
                        motionEndBorderMode = MTLMotionBorderMode.Clamp,
                        motionStartTime = 0f,
                        motionEndTime = 1f,
                    };
                }

                m_InstancedAccelerationStructures = MetalArrayHelper.CreateNSArrayFromPointers(accelArray.ToArray());
                m_NativeDescriptor.InstancedAccelerationStructures = m_InstancedAccelerationStructures;
                m_NativeDescriptor.InstanceCount = (ulong)motionDescriptors.Length;
                m_NativeDescriptor.InstanceDescriptorStride = motionDescriptorSize;
                m_NativeDescriptor.MotionTransformType = MTLTransformType.PackedFloat4x3;
                m_NativeDescriptor.MotionTransformStride = kMetalMotionTransformSize;
                m_NativeDescriptor.MotionTransformCount = (ulong)(instances.Length * 2);

                EnsureInstanceDescriptorBufferCapacity((ulong)motionDescriptors.Length * motionDescriptorSize);
                fixed (MTLAccelerationStructureMotionInstanceDescriptor* src = motionDescriptors)
                {
                    Buffer.MemoryCopy(
                        src,
                        (void*)m_InstanceDescriptorBuffer.Contents,
                        m_InstanceBufferSize,
                        (ulong)motionDescriptors.Length * motionDescriptorSize);
                }

                EnsureMotionTransformBufferCapacity((ulong)motionTransforms.Length * sizeof(float));
                Marshal.Copy(motionTransforms, 0, m_MotionTransformBuffer.Contents, motionTransforms.Length);
                m_NativeDescriptor.MotionTransformBuffer = m_MotionTransformBuffer;
                m_NativeDescriptor.MotionTransformBufferOffset = 0;
                m_NativeDescriptor.InstanceDescriptorBuffer = m_InstanceDescriptorBuffer;
                m_NativeDescriptor.InstanceDescriptorBufferOffset = descriptor.Offset;
            }
            else
            {
                MetalInstanceDescriptorRaw[] instanceDescriptors = new MetalInstanceDescriptorRaw[descriptor.Instances.Length];

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
            }

            MTLAccelerationStructureSizes sizes = m_MetalDevice.NativeDevice.AccelerationStructureSizes(m_NativeDescriptor);
            if (sizes.accelerationStructureSize == 0 || sizes.buildScratchBufferSize == 0)
            {
                throw new InvalidOperationException("Metal TLAS size query returned zero-sized allocation.");
            }

            RecreateAccelerationStructureResources(sizes.accelerationStructureSize, sizes.buildScratchBufferSize);
        }

        private unsafe void ReplaceTopLevelNativeState(in RHITopLevelAccelStructDescriptor descriptor)
        {
            if (descriptor.Instances.Length == 0)
            {
                throw new InvalidOperationException("TLAS requires at least one instance.");
            }

            Dictionary<IntPtr, uint> accelIndexMap = new Dictionary<IntPtr, uint>(descriptor.Instances.Length);
            List<IntPtr> accelArray = new List<IntPtr>(descriptor.Instances.Length);
            Span<RHIAccelStructInstance> instances = descriptor.Instances.Span;

            MTLInstanceAccelerationStructureDescriptor pending = MTLInstanceAccelerationStructureDescriptor.New();
            pending.InstanceDescriptorType = m_UsesMotion
                ? MTLAccelerationStructureInstanceDescriptorType.Motion
                : MTLAccelerationStructureInstanceDescriptorType.Default;
            pending.InstanceTransformationMatrixLayout = MTLMatrixLayout.ColumnMajor;
            pending.Usage = MetalUtility.ConvertToMetalAccelerationStructureUsage(descriptor.Flag);

            MTLBuffer newInstanceBuffer = default;
            MTLBuffer newMotionBuffer = default;
            MTLAccelerationStructure newAccelerationStructure = default;
            MTLBuffer newScratchBuffer = default;
            NSArray newInstanced = default;
            ulong newInstanceSize = 0;
            ulong newMotionSize = 0;
            bool committed = false;
            try
            {
                if (m_UsesMotion)
                {
                    ulong motionDescriptorSize = (ulong)Marshal.SizeOf<MTLAccelerationStructureMotionInstanceDescriptor>();
                    MTLAccelerationStructureMotionInstanceDescriptor[] motionDescriptors =
                        new MTLAccelerationStructureMotionInstanceDescriptor[instances.Length];
                    float[] motionTransforms = new float[instances.Length * 24];
                    for (int i = 0; i < instances.Length; ++i)
                    {
                        ref RHIAccelStructInstance instance = ref instances[i];
                        if (instance.MotionType == ERHIAccelStructMotionInstanceType.Srt)
                        {
                            throw new NotSupportedException(
                                "Metal motion instances use matrix keyframes; SRT motion is fail-closed on Metal.");
                        }

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

                        WritePackedTransform(motionTransforms, i * 24, instance.TransformMatrix);
                        WritePackedTransform(
                            motionTransforms,
                            i * 24 + 12,
                            instance.MotionType == ERHIAccelStructMotionInstanceType.Matrix
                                ? instance.MotionTransformMatrix
                                : instance.TransformMatrix);

                        motionDescriptors[i] = new MTLAccelerationStructureMotionInstanceDescriptor
                        {
                            options = MetalUtility.ConvertToMetalAccelerationStructureInstanceOptions(instance.Flag),
                            mask = instance.InstanceMask,
                            intersectionFunctionTableOffset = instance.HitGroupIndex,
                            accelerationStructureIndex = accelIndex,
                            userID = instance.InstanceID,
                            motionTransformsStartIndex = (uint)(i * 2),
                            motionTransformsCount = 2,
                            motionStartBorderMode = MTLMotionBorderMode.Clamp,
                            motionEndBorderMode = MTLMotionBorderMode.Clamp,
                            motionStartTime = 0f,
                            motionEndTime = 1f,
                        };
                    }

                    newInstanceSize = (ulong)motionDescriptors.Length * motionDescriptorSize;
                    newInstanceBuffer = AllocateSharedBuffer(newInstanceSize);
                    fixed (MTLAccelerationStructureMotionInstanceDescriptor* src = motionDescriptors)
                    {
                        Buffer.MemoryCopy(
                            src,
                            (void*)newInstanceBuffer.Contents,
                            newInstanceSize,
                            newInstanceSize);
                    }

                    newMotionSize = (ulong)motionTransforms.Length * sizeof(float);
                    newMotionBuffer = AllocateSharedBuffer(newMotionSize);
                    Marshal.Copy(motionTransforms, 0, newMotionBuffer.Contents, motionTransforms.Length);

                    pending.InstanceCount = (ulong)motionDescriptors.Length;
                    pending.InstanceDescriptorStride = motionDescriptorSize;
                    pending.MotionTransformType = MTLTransformType.PackedFloat4x3;
                    pending.MotionTransformStride = kMetalMotionTransformSize;
                    pending.MotionTransformCount = (ulong)(instances.Length * 2);
                    pending.MotionTransformBuffer = newMotionBuffer;
                    pending.MotionTransformBufferOffset = 0;
                    pending.InstanceDescriptorBuffer = newInstanceBuffer;
                    pending.InstanceDescriptorBufferOffset = descriptor.Offset;
                }
                else
                {
                    MetalInstanceDescriptorRaw[] instanceDescriptors =
                        new MetalInstanceDescriptorRaw[descriptor.Instances.Length];
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

                        instanceDescriptors[i] = BuildRawInstanceDescriptor(
                            instance.TransformMatrix,
                            instance.Flag,
                            instance.InstanceMask,
                            instance.HitGroupIndex,
                            accelIndex);
                    }

                    newInstanceSize = (ulong)instanceDescriptors.Length * kMetalInstanceDescriptorSize;
                    newInstanceBuffer = AllocateSharedBuffer(newInstanceSize);
                    fixed (MetalInstanceDescriptorRaw* src = instanceDescriptors)
                    {
                        Buffer.MemoryCopy(
                            src,
                            (void*)newInstanceBuffer.Contents,
                            newInstanceSize,
                            newInstanceSize);
                    }

                    pending.InstanceCount = (ulong)instanceDescriptors.Length;
                    pending.InstanceDescriptorStride = kMetalInstanceDescriptorSize;
                    pending.InstanceDescriptorBuffer = newInstanceBuffer;
                    pending.InstanceDescriptorBufferOffset = descriptor.Offset;
                }

                newInstanced = MetalArrayHelper.CreateNSArrayFromPointers(accelArray.ToArray());
                pending.InstancedAccelerationStructures = newInstanced;

                MTLAccelerationStructureSizes sizes = m_MetalDevice.NativeDevice.AccelerationStructureSizes(pending);
                if (sizes.accelerationStructureSize == 0 || sizes.buildScratchBufferSize == 0)
                {
                    throw new InvalidOperationException("Metal TLAS size query returned zero-sized allocation.");
                }

                newAccelerationStructure = m_MetalDevice.NativeDevice.NewAccelerationStructure(sizes.accelerationStructureSize);
                newScratchBuffer = m_MetalDevice.NativeDevice.NewBuffer(
                    sizes.buildScratchBufferSize,
                    MTLResourceOptions.ResourceStorageModePrivate);
                if (newAccelerationStructure.NativePtr == IntPtr.Zero || newScratchBuffer.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to allocate Metal TLAS resources.");
                }

                ReleaseMetalBuffer(ref m_InstanceDescriptorBuffer);
                m_InstanceDescriptorBuffer = newInstanceBuffer;
                m_InstanceBufferSize = newInstanceSize;
                newInstanceBuffer = default;

                ReleaseMetalBuffer(ref m_MotionTransformBuffer);
                m_MotionTransformBuffer = newMotionBuffer;
                m_MotionTransformBufferSize = newMotionSize;
                newMotionBuffer = default;

                if (m_NativeAccelerationStructure.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(m_NativeAccelerationStructure);
                }

                m_NativeAccelerationStructure = newAccelerationStructure;
                newAccelerationStructure = default;

                ReleaseMetalBuffer(ref m_NativeScratchBuffer);
                m_NativeScratchBuffer = newScratchBuffer;
                newScratchBuffer = default;

                if (m_NativeDescriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(m_NativeDescriptor);
                }

                m_NativeDescriptor = pending;
                pending = default;
                m_InstancedAccelerationStructures = newInstanced;
                newInstanced = default;
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    ReleaseMetalBuffer(ref newInstanceBuffer);
                    ReleaseMetalBuffer(ref newMotionBuffer);
                    if (newAccelerationStructure.NativePtr != IntPtr.Zero)
                    {
                        ObjectiveCRuntime.Release(newAccelerationStructure);
                    }

                    ReleaseMetalBuffer(ref newScratchBuffer);
                    if (pending.NativePtr != IntPtr.Zero)
                    {
                        ObjectiveCRuntime.Release(pending);
                    }
                }
            }
        }

        private MTLBuffer AllocateSharedBuffer(in ulong requiredSize)
        {
            ulong byteSize = requiredSize == 0 ? kMetalInstanceDescriptorSize : requiredSize;
            MTLBuffer buffer = m_MetalDevice.NativeDevice.NewBuffer(byteSize, MTLResourceOptions.ResourceStorageModeShared);
            if (buffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate Metal TLAS buffer.");
            }

            return buffer;
        }

        private static void ReleaseMetalBuffer(ref MTLBuffer buffer)
        {
            if (buffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(buffer);
                buffer = default;
            }
        }

        private static void WritePackedTransform(float[] destination, in int offset, in float4x4 transform)
        {
            destination[offset + 0] = transform.c0.x;
            destination[offset + 1] = transform.c0.y;
            destination[offset + 2] = transform.c0.z;
            destination[offset + 3] = transform.c1.x;
            destination[offset + 4] = transform.c1.y;
            destination[offset + 5] = transform.c1.z;
            destination[offset + 6] = transform.c2.x;
            destination[offset + 7] = transform.c2.y;
            destination[offset + 8] = transform.c2.z;
            destination[offset + 9] = transform.c3.x;
            destination[offset + 10] = transform.c3.y;
            destination[offset + 11] = transform.c3.z;
        }

        private static unsafe MetalInstanceDescriptorRaw BuildRawInstanceDescriptor(in float4x4 transform, in ERHIAccelStructInstanceFlag flag, in byte mask, in uint functionTableOffset, in uint accelerationStructureIndex)
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

        private void EnsureMotionTransformBufferCapacity(ulong requiredSize)
        {
            if (requiredSize == 0)
            {
                requiredSize = kMetalMotionTransformSize;
            }

            if (m_MotionTransformBuffer.NativePtr != IntPtr.Zero && m_MotionTransformBufferSize >= requiredSize)
            {
                return;
            }

            if (m_MotionTransformBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_MotionTransformBuffer);
                m_MotionTransformBuffer = default;
            }

            m_MotionTransformBuffer = m_MetalDevice.NativeDevice.NewBuffer(requiredSize, MTLResourceOptions.ResourceStorageModeShared);
            if (m_MotionTransformBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to allocate Metal TLAS motion transform buffer.");
            }

            m_MotionTransformBufferSize = requiredSize;
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

            if (m_MotionTransformBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_MotionTransformBuffer);
                m_MotionTransformBuffer = default;
            }

            if (m_NativeDescriptor.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeDescriptor);
                m_NativeDescriptor = default;
            }
        }
    }
}
