using System;
using Infinity.Mathmatics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal static class MetalBarrierHelper
    {
        private static readonly Selector s_RespondsToSelector = "respondsToSelector:";
        private static readonly Selector s_BarrierAfterEncoderStages = "barrierAfterEncoderStages:beforeEncoderStages:visibilityOptions:";

        internal static void TryBarrierAfterEncoderStages(in IntPtr encoderPtr, in ulong afterStages, in ulong beforeStages)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            if (!ObjectiveCRuntime.bool_objc_msgSend(encoderPtr, s_RespondsToSelector, s_BarrierAfterEncoderStages))
            {
                return;
            }

            MTL4CommandEncoder encoder = new MTL4CommandEncoder(encoderPtr);
            encoder.BarrierAfterEncoderStages(afterStages, beforeStages, MTL4VisibilityOptions.Device);
        }

        internal static MTLRenderStages ConvertToRenderStages(in ulong stages)
        {
            MTLRenderStages result = 0;
            if ((stages & (1UL << 0)) != 0)
            {
                result |= MTLRenderStages.RenderStageVertex;
            }

            if ((stages & (1UL << 1)) != 0)
            {
                // Some Apple GPUs reject fragment-stage memory barriers in render encoders.
                // Map fragment barriers to vertex stage to preserve ordering without runtime asserts.
                result |= MTLRenderStages.RenderStageVertex;
            }

            return result == 0 ? MTLRenderStages.RenderStageVertex : result;
        }
    }

    internal sealed class MetalTransferEncoder : RHITransferEncoder
    {
        internal MTLBlitCommandEncoder NativeEncoder => m_NativeEncoder;

        private MTLBlitCommandEncoder m_NativeEncoder;

        internal MetalTransferEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder = default;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = commandBuffer.NativeCommandBuffer.BlitCommandEncoder();
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLBlitCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount)
        {
        }

        public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size)
        {
            MetalBuffer src = (MetalBuffer)srcBuffer;
            MetalBuffer dst = (MetalBuffer)dstBuffer;
            m_NativeEncoder.CopyFromBuffer(src.NativeBuffer, (ulong)srcOffset, dst.NativeBuffer, (ulong)dstOffset, (ulong)size);
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            MetalBuffer srcBuffer = (MetalBuffer)src.Buffer;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;

            ulong bytesPerPixel = 4;
            ulong rowPitch = src.RowPitch > 0 ? src.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            m_NativeEncoder.CopyFromBuffer(
                srcBuffer.NativeBuffer,
                src.Offset,
                rowPitch,
                imagePitch,
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstTexture.NativeTexture,
                dst.SliceBase,
                dst.MipLevel,
                new MTLOrigin(dst.Origin.x, dst.Origin.y, dst.Origin.z));
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalBuffer dstBuffer = (MetalBuffer)dst.Buffer;

            ulong bytesPerPixel = 4;
            ulong rowPitch = dst.RowPitch > 0 ? dst.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            m_NativeEncoder.CopyFromTexture(
                srcTexture.NativeTexture,
                src.SliceBase,
                src.MipLevel,
                new MTLOrigin(src.Origin.x, src.Origin.y, src.Origin.z),
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstBuffer.NativeBuffer,
                dst.Offset,
                rowPitch,
                imagePitch);
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;
            m_NativeEncoder.CopyFromTexture(
                srcTexture.NativeTexture,
                src.SliceBase,
                src.MipLevel,
                new MTLOrigin(src.Origin.x, src.Origin.y, src.Origin.z),
                new MTLSize((ulong)size.x, (ulong)size.y, (ulong)size.z),
                dstTexture.NativeTexture,
                dst.SliceBase,
                dst.MipLevel,
                new MTLOrigin(dst.Origin.x, dst.Origin.y, dst.Origin.z));
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalComputeEncoder : RHIComputeEncoder
    {
        internal MTLComputeCommandEncoder NativeEncoder => m_NativeEncoder;

        private MTLComputeCommandEncoder m_NativeEncoder;

        internal MetalComputeEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder = default;
        }

        internal override void BeginPass(in RHIComputePassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = commandBuffer.NativeCommandBuffer.ComputeCommandEncoder();
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLComputeCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (scope != 0)
            {
                m_NativeEncoder.MemoryBarrier(scope);
            }

            MetalCommandBuffer cmd = (MetalCommandBuffer)m_CommandBuffer!;
            if (cmd.EnableMetal4Barriers)
            {
                MetalBarrierHelper.TryBarrierAfterEncoderStages(m_NativeEncoder.NativePtr, afterStages, beforeStages);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            ApplyImmediateBarrier(MTLBarrierScope.Buffers, MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute), MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute));
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            ApplyImmediateBarrier(MTLBarrierScope.Textures, MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute), MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute));
        }

        public override void SetPipeline(RHIComputePipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalComputePipeline metalPipeline = (MetalComputePipeline)pipeline;
            m_NativeEncoder.SetComputePipelineState(metalPipeline.NativePipelineState);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            MetalResourceTable table = (MetalResourceTable)resourceTable;
            MetalResourceTableLayout layout = table.ResourceTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Compute resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            MetalBindInfo[] binds = layout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];
                BindComputeElement(bind, element);
            }
        }

        private void BindComputeElement(in MetalBindInfo bind, in RHIResourceTableElement element)
        {
            switch (bind.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        m_NativeEncoder.SetBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                    }

                    break;

                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    if (element.TextureView is MetalTextureView textureView)
                    {
                        m_NativeEncoder.SetTexture(textureView.NativeTexture, bind.Slot);
                    }

                    break;

                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        m_NativeEncoder.SetSamplerState(sampler.NativeSampler, bind.Slot);
                    }

                    break;

                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                    {
                        m_NativeEncoder.SetAccelerationStructure(topLevel.NativeAccelerationStructure, bind.Slot);
                    }

                    break;
            }
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            m_NativeEncoder.DispatchThreadgroups(
                new MTLSize(groupCountX, groupCountY, groupCountZ),
                new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z));
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            MetalBuffer indirectBuffer = (MetalBuffer)argsBuffer;
            m_NativeEncoder.DispatchThreadgroups(
                indirectBuffer.NativeBuffer,
                argsOffset,
                new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z));
        }

        public override void ExecuteIndirectCommandBuffer(RHIComputeIndirectCommandBuffer indirectCmdBuffer)
        {
            throw new NotSupportedException("Compute indirect command buffer execution is not implemented in Metal backend.");
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalRaytracingEncoder : RHIRaytracingEncoder
    {
        private const ulong IntersectionFunctionTableSlot = 30;
        private const ulong VisibleFunctionTableSlot = 31;

        private MTLComputeCommandEncoder m_NativeEncoder;
        private MTLAccelerationStructureCommandEncoder m_NativeAccelEncoder;

        internal MetalRaytracingEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder = default;
            m_NativeAccelEncoder = default;
        }

        internal override void BeginPass(in RHIRayTracingPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = commandBuffer.NativeCommandBuffer.ComputeCommandEncoder();
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal ray tracing compute encoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.PushDebugGroup(new NSString(name));
            }
            else if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.PopDebugGroup();
            }
            else if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void MemoryBarrier(RHIBuffer buffer, in ERHIBufferState srcState, in ERHIBufferState dstState)
        {
            ApplyImmediateBarrier(MTLBarrierScope.Buffers, MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing), MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing));
        }

        public override void MemoryBarrier(RHITexture texture, in ERHITextureState srcState, in ERHITextureState dstState)
        {
            ApplyImmediateBarrier(MTLBarrierScope.Textures, MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing), MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing));
        }

        public override void SetPipeline(RHIRaytracingPipeline pipeline)
        {
            EnsureComputeEncoder();
            m_CachedPipeline = pipeline;
            MetalRaytracingPipeline metalPipeline = pipeline as MetalRaytracingPipeline ?? throw new InvalidOperationException("Ray tracing pipeline must be a MetalRaytracingPipeline.");
            m_NativeEncoder.SetComputePipelineState(metalPipeline.NativePipelineState);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            EnsureComputeEncoder();

            MetalResourceTable table = (MetalResourceTable)resourceTable;
            MetalResourceTableLayout layout = table.ResourceTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Ray tracing resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            MetalBindInfo[] binds = layout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];
                BindRaytracingElement(bind, element);
            }
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            EnsureAccelerationEncoder();
            MetalTopLevelAccelStruct metalTlas = topLevelAccelStruct as MetalTopLevelAccelStruct ?? throw new InvalidOperationException("TLAS must be a MetalTopLevelAccelStruct.");
            m_NativeAccelEncoder.BuildAccelerationStructure(metalTlas.NativeAccelerationStructure, metalTlas.NativeDescriptor, metalTlas.NativeScratchBuffer, 0);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            EnsureAccelerationEncoder();
            MetalBottomLevelAccelStruct metalBlas = bottomLevelAccelStruct as MetalBottomLevelAccelStruct ?? throw new InvalidOperationException("BLAS must be a MetalBottomLevelAccelStruct.");
            m_NativeAccelEncoder.BuildAccelerationStructure(metalBlas.NativeAccelerationStructure, metalBlas.NativeDescriptor, metalBlas.NativeScratchBuffer, 0);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            EnsureComputeEncoder();
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before dispatch.");
            }

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            BindFunctionTables(table);
            m_NativeEncoder.DispatchThreadgroups(new MTLSize(width, height, depth), new MTLSize(pipeline.ThreadgroupSize.x, pipeline.ThreadgroupSize.y, pipeline.ThreadgroupSize.z));
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            EnsureComputeEncoder();
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before indirect dispatch.");
            }

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing indirect dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            BindFunctionTables(table);
            MetalBuffer indirectBuffer = argsBuffer as MetalBuffer ?? throw new InvalidOperationException("Ray tracing indirect args must be a MetalBuffer.");
            m_NativeEncoder.DispatchThreadgroups(indirectBuffer.NativeBuffer, argsOffset, new MTLSize(pipeline.ThreadgroupSize.x, pipeline.ThreadgroupSize.y, pipeline.ThreadgroupSize.z));
        }

        public override void ExecuteIndirectCommandBuffer(RHIRayTracingIndirectCommandBuffer indirectCmdBuffer)
        {
            throw new NotSupportedException("Ray tracing indirect command buffer execution is not implemented in Metal backend.");
        }

        public override void EndPass()
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.EndEncoding();
                m_NativeAccelEncoder = default;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }

            m_CachedPipeline = null;
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.WaitForFence(fence);
            }
            else if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.UpdateFence(fence);
            }
            else if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (scope != 0)
            {
                EnsureComputeEncoder();
                m_NativeEncoder.MemoryBarrier(scope);
            }

            MetalCommandBuffer cmd = (MetalCommandBuffer)m_CommandBuffer!;
            if (cmd.EnableMetal4Barriers)
            {
                IntPtr encoderPtr = m_NativeAccelEncoder.NativePtr != IntPtr.Zero ? m_NativeAccelEncoder.NativePtr : m_NativeEncoder.NativePtr;
                MetalBarrierHelper.TryBarrierAfterEncoderStages(encoderPtr, afterStages, beforeStages);
            }
        }

        protected override void Release()
        {
        }

        private void BindFunctionTables(MetalFunctionTable table)
        {
            if (table.IntersectionFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetIntersectionFunctionTable(table.IntersectionFunctionTable, IntersectionFunctionTableSlot);
            }

            if (table.VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetVisibleFunctionTable(table.VisibleFunctionTable, VisibleFunctionTableSlot);
            }
        }

        private void BindRaytracingElement(in MetalBindInfo bind, in RHIResourceTableElement element)
        {
            switch (bind.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        m_NativeEncoder.SetBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                    }

                    break;
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    if (element.TextureView is MetalTextureView textureView)
                    {
                        m_NativeEncoder.SetTexture(textureView.NativeTexture, bind.Slot);
                    }

                    break;
                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        m_NativeEncoder.SetSamplerState(sampler.NativeSampler, bind.Slot);
                    }

                    break;
                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct is not MetalTopLevelAccelStruct accelStruct)
                    {
                        throw new InvalidOperationException("Ray tracing acceleration-structure binding expects a Metal TLAS.");
                    }

                    m_NativeEncoder.SetAccelerationStructure(accelStruct.NativeAccelerationStructure, bind.Slot);
                    break;
            }
        }

        private void EnsureComputeEncoder()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                return;
            }

            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.EndEncoding();
                m_NativeAccelEncoder = default;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = commandBuffer.NativeCommandBuffer.ComputeCommandEncoder();
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal ray tracing compute encoder.");
            }
        }

        private void EnsureAccelerationEncoder()
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeAccelEncoder = commandBuffer.NativeCommandBuffer.AccelerationStructureCommandEncoder();
            if (m_NativeAccelEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal acceleration-structure encoder.");
            }
        }
    }

    internal sealed class MetalRasterEncoder : RHIRasterEncoder
    {
        private const uint DrawIndirectArgsStride = 16;
        private const uint DrawIndexedIndirectArgsStride = 20;

        internal MTLRenderCommandEncoder NativeEncoder => m_NativeEncoder;

        private MTLRenderCommandEncoder m_NativeEncoder;
        private MTLBuffer m_IndexBuffer;
        private ulong m_IndexBufferOffset;
        private MTLIndexType m_IndexType;

        internal MetalRasterEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder = default;
            m_IndexBuffer = default;
            m_IndexBufferOffset = 0;
            m_IndexType = MTLIndexType.UInt16;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MTLRenderPassDescriptor nativePassDescriptor = MTLRenderPassDescriptor.New();
            nativePassDescriptor.RenderTargetArrayLength = descriptor.ArrayLength;

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachment = ref descriptor.ColorAttachments.Span[i];
                MetalTexture colorTexture = (MetalTexture)colorAttachment.RenderTarget;
                MTLRenderPassColorAttachmentDescriptor nativeColor = nativePassDescriptor.ColorAttachments[(uint)i];
                nativeColor.Texture = colorTexture.NativeTexture;
                nativeColor.Level = colorAttachment.MipLevel;
                nativeColor.Slice = colorAttachment.ArraySlice;
                nativeColor.LoadAction = MetalUtility.ConvertToMetalLoadAction(colorAttachment.LoadAction);
                nativeColor.StoreAction = MetalUtility.ConvertToMetalStoreAction(colorAttachment.StoreAction);
                nativeColor.ClearColor = new MTLClearColor(colorAttachment.ClearValue.x, colorAttachment.ClearValue.y, colorAttachment.ClearValue.z, colorAttachment.ClearValue.w);

                if (colorAttachment.ResolveTarget != null)
                {
                    MetalTexture resolveTexture = (MetalTexture)colorAttachment.ResolveTarget;
                    nativeColor.ResolveTexture = resolveTexture.NativeTexture;
                    nativeColor.ResolveLevel = colorAttachment.ResolveMipLevel;
                    nativeColor.ResolveSlice = colorAttachment.ResolveArraySlice;
                }

                if (i == 0 && colorTexture.HasBackingDrawable)
                {
                    commandBuffer.SetPresentDrawable(colorTexture.BackingDrawable);
                }
            }

            if (descriptor.DepthStencilAttachment.HasValue)
            {
                RHIDepthStencilAttachmentDescriptor depthStencil = descriptor.DepthStencilAttachment.Value;
                MetalTexture depthTexture = (MetalTexture)depthStencil.RenderTarget;

                MTLRenderPassDepthAttachmentDescriptor depthAttachment = nativePassDescriptor.DepthAttachment;
                depthAttachment.Texture = depthTexture.NativeTexture;
                depthAttachment.Level = depthStencil.MipLevel;
                depthAttachment.Slice = depthStencil.ArraySlice;
                depthAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.DepthLoadOp);
                depthAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.DepthStoreOp);
                depthAttachment.ClearDepth = depthStencil.DepthClearValue;

                MTLRenderPassStencilAttachmentDescriptor stencilAttachment = nativePassDescriptor.StencilAttachment;
                stencilAttachment.Texture = depthTexture.NativeTexture;
                stencilAttachment.Level = depthStencil.MipLevel;
                stencilAttachment.Slice = depthStencil.ArraySlice;
                stencilAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.StencilLoadOp);
                stencilAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.StencilStoreOp);
                stencilAttachment.ClearStencil = (uint)depthStencil.StencilClearValue;
            }

            m_NativeEncoder = commandBuffer.NativeCommandBuffer.RenderCommandEncoder(nativePassDescriptor);
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTLRenderCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence, MTLRenderStages.RenderStageVertex | MTLRenderStages.RenderStageFragment);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr != IntPtr.Zero && m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence, MTLRenderStages.RenderStageVertex | MTLRenderStages.RenderStageFragment);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (scope != 0)
            {
                MTLRenderStages after = MetalBarrierHelper.ConvertToRenderStages(afterStages);
                MTLRenderStages before = MetalBarrierHelper.ConvertToRenderStages(beforeStages);
                m_NativeEncoder.MemoryBarrier(scope, after, before);
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (commandBuffer.EnableMetal4Barriers)
            {
                MetalBarrierHelper.TryBarrierAfterEncoderStages(m_NativeEncoder.NativePtr, afterStages, beforeStages);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginOcclusion(in uint index)
        {
        }

        public override void EndOcclusion(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void NextSubPass()
        {
            throw new NotSupportedException("Raster sub-pass is not implemented in Metal backend.");
        }

        public override void SetScissor(in Rect rect)
        {
            MTLScissorRect nativeRect = new MTLScissorRect
            {
                x = (ulong)Math.Max(0, rect.left),
                y = (ulong)Math.Max(0, rect.top),
                width = (ulong)Math.Max(0, rect.right - rect.left),
                height = (ulong)Math.Max(0, rect.bottom - rect.top)
            };
            m_NativeEncoder.SetScissorRect(nativeRect);
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
            if (rects.Length == 0)
            {
                return;
            }

            SetScissor(rects.Span[0]);
        }

        public override void SetViewport(in Viewport viewport)
        {
            MTLViewport nativeViewport = new MTLViewport
            {
                originX = viewport.TopLeftX,
                originY = viewport.TopLeftY,
                width = viewport.Width,
                height = viewport.Height,
                znear = viewport.MinDepth,
                zfar = viewport.MaxDepth
            };
            m_NativeEncoder.SetViewport(nativeViewport);
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
            if (viewports.Length == 0)
            {
                return;
            }

            SetViewport(viewports.Span[0]);
        }

        public override void SetStencilRef(in uint value)
        {
            m_NativeEncoder.SetStencilReferenceValue(value);
        }

        public override void SetBlendFactor(in float4 value)
        {
            m_NativeEncoder.SetBlendColor(value.x, value.y, value.z, value.w);
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalRasterPipeline metalPipeline = (MetalRasterPipeline)pipeline;
            m_NativeEncoder.SetRenderPipelineState(metalPipeline.NativePipelineState);
            if (metalPipeline.DepthStencilState.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetDepthStencilState(metalPipeline.DepthStencilState);
            }

            m_NativeEncoder.SetCullMode(metalPipeline.CullMode);
            m_NativeEncoder.SetTriangleFillMode(metalPipeline.FillMode);
            m_NativeEncoder.SetFrontFacingWinding(metalPipeline.Winding);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            MetalResourceTable table = (MetalResourceTable)resourceTable;
            MetalResourceTableLayout layout = table.ResourceTableLayout;
            if (layout.Index != tableIndex)
            {
                throw new InvalidOperationException($"Raster resource table index mismatch. expected={layout.Index}, actual={tableIndex}");
            }

            MetalBindInfo[] binds = layout.BindInfos;
            RHIResourceTableElement[] elements = table.Elements;
            for (int i = 0; i < binds.Length && i < elements.Length; ++i)
            {
                ref readonly MetalBindInfo bind = ref binds[i];
                ref RHIResourceTableElement element = ref elements[i];
                BindRasterElement(bind, element);
            }
        }

        private void BindRasterElement(in MetalBindInfo bind, in RHIResourceTableElement element)
        {
            bool bindVertex = (bind.Stage & ERHIShaderStage.Vertex) == ERHIShaderStage.Vertex || (bind.Stage & ERHIShaderStage.All) == ERHIShaderStage.All;
            bool bindFragment = (bind.Stage & ERHIShaderStage.Fragment) == ERHIShaderStage.Fragment || (bind.Stage & ERHIShaderStage.All) == ERHIShaderStage.All;

            switch (bind.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        if (bindVertex)
                        {
                            m_NativeEncoder.SetVertexBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                        }

                        if (bindFragment)
                        {
                            m_NativeEncoder.SetFragmentBuffer(bufferView.Buffer.NativeBuffer, (ulong)Math.Max(0, bufferView.Descriptor.Offset), bind.Slot);
                        }
                    }

                    break;

                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    if (element.TextureView is MetalTextureView textureView)
                    {
                        if (bindVertex)
                        {
                            m_NativeEncoder.SetVertexTexture(textureView.NativeTexture, bind.Slot);
                        }

                        if (bindFragment)
                        {
                            m_NativeEncoder.SetFragmentTexture(textureView.NativeTexture, bind.Slot);
                        }
                    }

                    break;

                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        if (bindVertex)
                        {
                            m_NativeEncoder.SetVertexSamplerState(sampler.NativeSampler, bind.Slot);
                        }

                        if (bindFragment)
                        {
                            m_NativeEncoder.SetFragmentSamplerState(sampler.NativeSampler, bind.Slot);
                        }
                    }

                    break;

                case ERHIBindType.AccelStruct:
                    throw new NotSupportedException("Raster encoder does not support acceleration structure bindings on Metal backend.");
            }
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)buffer;
            m_IndexBuffer = metalBuffer.NativeBuffer;
            m_IndexBufferOffset = offset;
            m_IndexType = MetalUtility.ConvertToMetalIndexType(buffer.Descriptor.Format);
        }

        public override void SetVertexBuffer(RHIBuffer buffer, in uint slot, in uint offset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)buffer;
            m_NativeEncoder.SetVertexBuffer(metalBuffer.NativeBuffer, offset, slot);
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            m_NativeEncoder.DrawPrimitives(MTLPrimitiveType.Triangle, firstVertex, vertexCount, instanceCount, firstInstance);
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            m_NativeEncoder.DrawIndexedPrimitives(MTLPrimitiveType.Triangle, indexCount, m_IndexType, m_IndexBuffer, m_IndexBufferOffset + firstIndex * (m_IndexType == MTLIndexType.UInt16 ? 2UL : 4UL), instanceCount, baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            for (uint i = 0; i < drawCount; ++i)
            {
                m_NativeEncoder.DrawPrimitives(MTLPrimitiveType.Triangle, metalBuffer.NativeBuffer, offset + i * DrawIndirectArgsStride);
            }
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            for (uint i = 0; i < drawCount; ++i)
            {
                m_NativeEncoder.DrawIndexedPrimitives(MTLPrimitiveType.Triangle, m_IndexType, m_IndexBuffer, m_IndexBufferOffset, metalBuffer.NativeBuffer, offset + i * DrawIndexedIndirectArgsStride);
            }
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            m_NativeEncoder.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            m_NativeEncoder.DrawMeshThreadgroups(metalBuffer.NativeBuffer, argsOffset, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
        }

        public override void DispatchGraph()
        {
            throw new NotSupportedException("Work graph dispatch is not implemented in Metal backend.");
        }

        public override void ExecuteIndirectCommandBuffer(RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            throw new NotSupportedException("Raster indirect command buffer execution is not implemented in Metal backend.");
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
        }

        protected override void Release()
        {
        }
    }
}
