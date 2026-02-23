using System;
using System.Collections.Generic;
using Infinity.Mathmatics;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;
using SharpMetal.QuartzCore;

namespace Infinity.Graphics
{
    internal static class MetalBindingLogHelper
    {
        private static readonly object s_LogLock = new object();
        private static readonly HashSet<string> s_LoggedPipelineModeKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static void LogPipelineModeOnce(string pipelineType, in IntPtr pipelineStatePtr, in MetalBindingMode mode, in MetalCommandEncodingPath path)
        {
            string key = $"{pipelineType}:{pipelineStatePtr}:{mode}:{path}";
            lock (s_LogLock)
            {
                if (!s_LoggedPipelineModeKeys.Add(key))
                {
                    return;
                }
            }

            Console.WriteLine($"[MetalBinding] {pipelineType} pipeline mode={mode}, encodingPath={path}, pipeline=0x{pipelineStatePtr.ToString("x")}.");
        }
    }

    internal static class MetalBarrierHelper
    {
        private static readonly Selector s_RespondsToSelector = "respondsToSelector:";
        private static readonly Selector s_BarrierAfterEncoderStages = "barrierAfterEncoderStages:beforeEncoderStages:visibilityOptions:";
        private const ulong s_ValidMetal4StageMask = (1UL << 0) | (1UL << 1) | (1UL << 27) | (1UL << 29);

        internal static void TryBarrierAfterEncoderStages(in IntPtr encoderPtr, in ulong afterStages, in ulong beforeStages)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            if (!IsValidMetal4StageMask(afterStages) || !IsValidMetal4StageMask(beforeStages))
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

        private static bool IsValidMetal4StageMask(in ulong stages)
        {
            return stages != 0 && (stages & ~s_ValidMetal4StageMask) == 0;
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

            return result;
        }
    }

    internal sealed class MetalTransferEncoder : RHITransferEncoder
    {
        internal MTLBlitCommandEncoder NativeEncoder => m_NativeEncoder;

        private MTLBlitCommandEncoder m_NativeEncoder;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;

        internal MetalTransferEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
        }

        internal override void BeginPass(in RHITransferPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;

            if (commandBuffer.EncodingPath == MetalCommandEncodingPath.MTL4)
            {
                m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
                if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTL4ComputeCommandEncoder for transfer pass.");
                }
            }
            else
            {
                m_NativeEncoder = commandBuffer.EnsureClassicCommandBuffer().BlitCommandEncoder();
                if (m_NativeEncoder.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTLBlitCommandEncoder.");
                }
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence);
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.WaitForFence(fence, m_NativeEncoder4.Stages);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence);
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.UpdateFence(fence, m_NativeEncoder4.Stages);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PopDebugGroup();
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
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.CopyFromBuffer(src.NativeBuffer, (ulong)srcOffset, dst.NativeBuffer, (ulong)dstOffset, (ulong)size);
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.CopyFromBuffer(src.NativeBuffer, (ulong)srcOffset, dst.NativeBuffer, (ulong)dstOffset, (ulong)size);
            }
            else
            {
                throw new InvalidOperationException("Transfer encoder is not initialized.");
            }
        }

        public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            MetalBuffer srcBuffer = (MetalBuffer)src.Buffer;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;

            ulong bytesPerPixel = 4;
            ulong rowPitch = src.RowPitch > 0 ? src.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
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
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.CopyFromBuffer(
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
            else
            {
                throw new InvalidOperationException("Transfer encoder is not initialized.");
            }
        }

        public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in int3 size)
        {
            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalBuffer dstBuffer = (MetalBuffer)dst.Buffer;

            ulong bytesPerPixel = 4;
            ulong rowPitch = dst.RowPitch > 0 ? dst.RowPitch : (uint)(size.x * (int)bytesPerPixel);
            ulong imagePitch = rowPitch * (ulong)Math.Max(1, size.y);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
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
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.CopyFromTexture(
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
            else
            {
                throw new InvalidOperationException("Transfer encoder is not initialized.");
            }
        }

        public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in int3 size)
        {
            MetalTexture srcTexture = (MetalTexture)src.Texture;
            MetalTexture dstTexture = (MetalTexture)dst.Texture;
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
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
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder4.CopyFromTexture(
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
            else
            {
                throw new InvalidOperationException("Transfer encoder is not initialized.");
            }
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalComputeEncoder : RHIComputeEncoder
    {
        internal MTLComputeCommandEncoder NativeEncoder => m_NativeEncoder;

        private readonly MetalDevice m_MetalDevice;
        private MTLComputeCommandEncoder m_NativeEncoder;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;

        internal MetalComputeEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_MetalDevice = ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
        }

        internal override void BeginPass(in RHIComputePassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_PendingPassDebugGroup = null;

            if (commandBuffer.EncodingPath == MetalCommandEncodingPath.Classic)
            {
                EnsureEncoderForPath(MetalCommandEncodingPath.Classic);
            }
            else if (commandBuffer.EncodingPath == MetalCommandEncodingPath.MTL4)
            {
                EnsureEncoderForPath(MetalCommandEncodingPath.MTL4);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence);
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.WaitForFence(fence, stage);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence);
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.UpdateFence(fence, stage);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                if (scope != 0)
                {
                    m_NativeEncoder.MemoryBarrier(scope);
                }

                MetalCommandBuffer cmd = (MetalCommandBuffer)m_CommandBuffer!;
                if (cmd.EnableMetal4Barriers)
                {
                    MetalBarrierHelper.TryBarrierAfterEncoderStages(m_NativeEncoder.NativePtr, afterStages, beforeStages);
                }

                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong resolvedAfter = afterStages != 0 ? afterStages : MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Compute);
                ulong resolvedBefore = beforeStages != 0 ? beforeStages : resolvedAfter;
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.BarrierAfterEncoderStages(resolvedAfter, resolvedBefore, MTL4VisibilityOptions.Device);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PushDebugGroup(new NSString(name));
                return;
            }

            throw new InvalidOperationException("Compute encoder is not created yet. Set pipeline before using debug groups.");
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PopDebugGroup();
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
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Compute pipeline layout must be a MetalPipelineLayout.");
            MetalBindingMode mode = ConfigureBindingBackend(pipelineLayout, out MetalCommandEncodingPath path);
            EnsureEncoderForPath(path);
            ApplyPendingPassDebugGroup();
            ((MetalCommandBuffer)m_CommandBuffer!).ApplyPendingBarrierForActiveEncoderIfNeeded();

            if (path == MetalCommandEncodingPath.Classic)
            {
                m_NativeEncoder.SetComputePipelineState(metalPipeline.NativePipelineState);
            }
            else
            {
                m_NativeEncoder4.SetComputePipelineState(metalPipeline.NativePipelineState);
            }

            MetalBindingLogHelper.LogPipelineModeOnce("Compute", metalPipeline.NativePipelineState.NativePtr, mode, path);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Compute pipeline must be set before binding resource tables.");
            }

            MetalResourceTable table = (MetalResourceTable)resourceTable;
            m_BindingBackend.SetResourceTable(table, tableIndex);
        }

        public override void Dispatch(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            MTLSize threadGroupCount = new MTLSize(groupCountX, groupCountY, groupCountZ);
            MTLSize threadsPerGroup = new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitCompute(m_NativeEncoder);
                m_NativeEncoder.DispatchThreadgroups(threadGroupCount, threadsPerGroup);
            }
            else
            {
                m_BindingBackend?.CommitCompute(m_NativeEncoder4);
                m_NativeEncoder4.DispatchThreadgroups(threadGroupCount, threadsPerGroup);
            }
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            if (m_CachedPipeline is not MetalComputePipeline computePipeline)
            {
                throw new InvalidOperationException("Compute pipeline must be set before dispatch.");
            }

            MetalBuffer indirectBuffer = (MetalBuffer)argsBuffer;
            MTLSize threadsPerGroup = new MTLSize(computePipeline.ThreadgroupSize.x, computePipeline.ThreadgroupSize.y, computePipeline.ThreadgroupSize.z);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitCompute(m_NativeEncoder);
                m_NativeEncoder.DispatchThreadgroups(indirectBuffer.NativeBuffer, argsOffset, threadsPerGroup);
            }
            else
            {
                m_BindingBackend?.CommitCompute(m_NativeEncoder4);
                m_NativeEncoder4.DispatchThreadgroupsWithIndirectBuffer(indirectBuffer.NativeBuffer.GpuAddress + argsOffset, threadsPerGroup);
            }
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

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private MetalBindingMode ConfigureBindingBackend(MetalPipelineLayout pipelineLayout, out MetalCommandEncodingPath path)
        {
            MetalBindingMode mode = MetalBindingPolicyResolver.Resolve(m_MetalDevice.BindingCapabilities, pipelineLayout.ResourceTableLayoutCount);
            path = mode == MetalBindingMode.ArgumentTable ? MetalCommandEncodingPath.MTL4 : MetalCommandEncodingPath.Classic;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            commandBuffer.LockEncodingPath(path, "compute pipeline set");

            bool? legacyCompatibilityOverride = null;
            if (mode == MetalBindingMode.SetBytes && !MetalBindingPolicyResolver.IsStrictSetBytesModeEnabled())
            {
                legacyCompatibilityOverride = true;
            }

            if (m_BindingBackend == null || m_BindingBackend.Mode != mode)
            {
                m_BindingBackend?.Dispose();
                m_BindingBackend = MetalBindingBackendFactory.Create(m_MetalDevice, mode, MetalBindingPipelineType.Compute, legacyCompatibilityOverride);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
            return mode;
        }

        private void EnsureEncoderForPath(in MetalCommandEncodingPath path)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (path == MetalCommandEncodingPath.Classic)
            {
                if (m_NativeEncoder.NativePtr == IntPtr.Zero)
                {
                    m_NativeEncoder = commandBuffer.EnsureClassicCommandBuffer().ComputeCommandEncoder();
                    if (m_NativeEncoder.NativePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Failed to create MTLComputeCommandEncoder.");
                    }
                }

                return;
            }

            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
                if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTL4ComputeCommandEncoder.");
                }
            }
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private bool HasNativeEncoder => m_NativeEncoder.NativePtr != IntPtr.Zero || m_NativeEncoder4.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalRaytracingEncoder : RHIRaytracingEncoder
    {
        private readonly MetalDevice m_MetalDevice;
        private MTLComputeCommandEncoder m_NativeEncoder;
        private MTL4ComputeCommandEncoder m_NativeEncoder4;
        private MTLAccelerationStructureCommandEncoder m_NativeAccelEncoder;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;

        internal MetalRaytracingEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_MetalDevice = ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_NativeAccelEncoder = default;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
        }

        internal override void BeginPass(in RHIRayTracingPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_NativeAccelEncoder = default;
            m_PendingPassDebugGroup = null;

            if (commandBuffer.EncodingPath == MetalCommandEncodingPath.Classic)
            {
                EnsureComputeEncoder(MetalCommandEncodingPath.Classic);
            }
            else if (commandBuffer.EncodingPath == MetalCommandEncodingPath.MTL4)
            {
                EnsureComputeEncoder(MetalCommandEncodingPath.MTL4);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.PushDebugGroup(new NSString(name));
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PushDebugGroup(new NSString(name));
                return;
            }

            throw new InvalidOperationException("Ray tracing encoder is not created yet. Set pipeline before using debug groups.");
        }

        public override void PopDebugGroup()
        {
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.PopDebugGroup();
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PopDebugGroup();
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
            m_CachedPipeline = pipeline;
            MetalRaytracingPipeline metalPipeline = pipeline as MetalRaytracingPipeline ?? throw new InvalidOperationException("Ray tracing pipeline must be a MetalRaytracingPipeline.");
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Ray tracing pipeline layout must be a MetalPipelineLayout.");
            MetalBindingMode mode = ConfigureBindingBackend(pipelineLayout, out MetalCommandEncodingPath path);
            EnsureComputeEncoder(path);
            ApplyPendingPassDebugGroup();
            ((MetalCommandBuffer)m_CommandBuffer!).ApplyPendingBarrierForActiveEncoderIfNeeded();

            if (path == MetalCommandEncodingPath.Classic)
            {
                m_NativeEncoder.SetComputePipelineState(metalPipeline.NativePipelineState);
            }
            else
            {
                m_NativeEncoder4.SetComputePipelineState(metalPipeline.NativePipelineState);
            }

            MetalBindingLogHelper.LogPipelineModeOnce("Ray", metalPipeline.NativePipelineState.NativePtr, mode, path);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before binding resource tables.");
            }

            MetalResourceTable table = (MetalResourceTable)resourceTable;
            if (m_BindingBackend.UsesReservedRayFunctionTableSlots && MetalBindingHelpers.HasRayFunctionTableSlotConflict(table))
            {
                throw new InvalidOperationException($"Ray tracing resource table conflicts with reserved Metal function-table slots ({MetalBindingHelpers.RtVisibleFunctionTableSlot}/{MetalBindingHelpers.RtIntersectionFunctionTableSlot}). Use another slot or disable legacy compatibility binding mode.");
            }

            m_BindingBackend.SetResourceTable(table, tableIndex);
        }

        public override void BuildAccelerationStructure(RHITopLevelAccelStruct topLevelAccelStruct)
        {
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MetalTopLevelAccelStruct metalTlas = topLevelAccelStruct as MetalTopLevelAccelStruct ?? throw new InvalidOperationException("TLAS must be a MetalTopLevelAccelStruct.");
                try
                {
                    MTL4BufferRange scratchRange = MTL4BufferRange.Make(metalTlas.NativeScratchBuffer.GpuAddress, metalTlas.NativeScratchBuffer.Length);
                    m_NativeEncoder4.BuildAccelerationStructure(metalTlas.NativeAccelerationStructure, metalTlas.NativeDescriptor.NativePtr, scratchRange);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"MTL4 TLAS build failed in ray-tracing pass. Build in classic init phase or force setbytes mode. detail={ex.Message}");
                }

                return;
            }

            EnsureAccelerationEncoderClassic();
            MetalTopLevelAccelStruct classicTlas = topLevelAccelStruct as MetalTopLevelAccelStruct ?? throw new InvalidOperationException("TLAS must be a MetalTopLevelAccelStruct.");
            m_NativeAccelEncoder.BuildAccelerationStructure(classicTlas.NativeAccelerationStructure, classicTlas.NativeDescriptor, classicTlas.NativeScratchBuffer, 0);
        }

        public override void BuildAccelerationStructure(RHIBottomLevelAccelStruct bottomLevelAccelStruct)
        {
            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MetalBottomLevelAccelStruct metalBlas = bottomLevelAccelStruct as MetalBottomLevelAccelStruct ?? throw new InvalidOperationException("BLAS must be a MetalBottomLevelAccelStruct.");
                try
                {
                    MTL4BufferRange scratchRange = MTL4BufferRange.Make(metalBlas.NativeScratchBuffer.GpuAddress, metalBlas.NativeScratchBuffer.Length);
                    m_NativeEncoder4.BuildAccelerationStructure(metalBlas.NativeAccelerationStructure, metalBlas.NativeDescriptor.NativePtr, scratchRange);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"MTL4 BLAS build failed in ray-tracing pass. Build in classic init phase or force setbytes mode. detail={ex.Message}");
                }

                return;
            }

            EnsureAccelerationEncoderClassic();
            MetalBottomLevelAccelStruct classicBlas = bottomLevelAccelStruct as MetalBottomLevelAccelStruct ?? throw new InvalidOperationException("BLAS must be a MetalBottomLevelAccelStruct.");
            m_NativeAccelEncoder.BuildAccelerationStructure(classicBlas.NativeAccelerationStructure, classicBlas.NativeDescriptor, classicBlas.NativeScratchBuffer, 0);
        }

        public override void Dispatch(in uint width, in uint height, in uint depth, RHIFunctionTable functionTable)
        {
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before dispatch.");
            }

            MetalCommandEncodingPath path = ((MetalCommandBuffer)m_CommandBuffer!).EncodingPath;
            if (path == MetalCommandEncodingPath.Unknown)
            {
                throw new InvalidOperationException("Ray tracing command buffer encoding path is not locked. Set pipeline before dispatch.");
            }

            EnsureComputeEncoder(path);

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            MTLSize threadgroupCount = new MTLSize(width, height, depth);
            MTLSize threadsPerGroup = new MTLSize(pipeline.ThreadgroupSize.x, pipeline.ThreadgroupSize.y, pipeline.ThreadgroupSize.z);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaytracing(m_NativeEncoder, table);
                m_NativeEncoder.DispatchThreadgroups(threadgroupCount, threadsPerGroup);
            }
            else
            {
                m_BindingBackend?.CommitRaytracing(m_NativeEncoder4, table);
                m_NativeEncoder4.DispatchThreadgroups(threadgroupCount, threadsPerGroup);
            }
        }

        public override void DispatchIndirect(RHIBuffer argsBuffer, in uint argsOffset, RHIFunctionTable functionTable)
        {
            if (m_CachedPipeline is not MetalRaytracingPipeline pipeline)
            {
                throw new InvalidOperationException("Ray tracing pipeline must be set before indirect dispatch.");
            }

            MetalCommandEncodingPath path = ((MetalCommandBuffer)m_CommandBuffer!).EncodingPath;
            if (path == MetalCommandEncodingPath.Unknown)
            {
                throw new InvalidOperationException("Ray tracing command buffer encoding path is not locked. Set pipeline before indirect dispatch.");
            }

            EnsureComputeEncoder(path);

            MetalFunctionTable table = functionTable as MetalFunctionTable ?? throw new InvalidOperationException("Ray tracing indirect dispatch requires a MetalFunctionTable.");
            if (!table.IsGenerated)
            {
                table.Generate(pipeline);
            }

            MetalBuffer indirectBuffer = argsBuffer as MetalBuffer ?? throw new InvalidOperationException("Ray tracing indirect args must be a MetalBuffer.");
            MTLSize threadsPerGroup = new MTLSize(pipeline.ThreadgroupSize.x, pipeline.ThreadgroupSize.y, pipeline.ThreadgroupSize.z);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaytracing(m_NativeEncoder, table);
                m_NativeEncoder.DispatchThreadgroups(indirectBuffer.NativeBuffer, argsOffset, threadsPerGroup);
            }
            else
            {
                m_BindingBackend?.CommitRaytracing(m_NativeEncoder4, table);
                m_NativeEncoder4.DispatchThreadgroupsWithIndirectBuffer(indirectBuffer.NativeBuffer.GpuAddress + argsOffset, threadsPerGroup);
            }
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

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDebugGroup = null;
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
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.WaitForFence(fence, stage);
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
            else if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.UpdateFence(fence, stage);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                if (scope != 0)
                {
                    m_NativeEncoder.MemoryBarrier(scope);
                }

                MetalCommandBuffer cmd = (MetalCommandBuffer)m_CommandBuffer!;
                if (cmd.EnableMetal4Barriers)
                {
                    IntPtr encoderPtr = m_NativeAccelEncoder.NativePtr != IntPtr.Zero ? m_NativeAccelEncoder.NativePtr : m_NativeEncoder.NativePtr;
                    MetalBarrierHelper.TryBarrierAfterEncoderStages(encoderPtr, afterStages, beforeStages);
                }

                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong resolvedAfter = afterStages != 0 ? afterStages : MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.RayTracing);
                ulong resolvedBefore = beforeStages != 0 ? beforeStages : resolvedAfter;
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.BarrierAfterEncoderStages(resolvedAfter, resolvedBefore, MTL4VisibilityOptions.Device);
                return;
            }

            if (scope != 0)
            {
                EnsureComputeEncoder(MetalCommandEncodingPath.Classic);
                m_NativeEncoder.MemoryBarrier(scope);
            }
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private MetalBindingMode ConfigureBindingBackend(MetalPipelineLayout pipelineLayout, out MetalCommandEncodingPath path)
        {
            MetalBindingMode mode = MetalBindingPolicyResolver.Resolve(m_MetalDevice.BindingCapabilities, pipelineLayout.ResourceTableLayoutCount);
            path = mode == MetalBindingMode.ArgumentTable ? MetalCommandEncodingPath.MTL4 : MetalCommandEncodingPath.Classic;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            commandBuffer.LockEncodingPath(path, "ray tracing pipeline set");

            bool? legacyCompatibilityOverride = null;
            if (mode == MetalBindingMode.SetBytes && !MetalBindingPolicyResolver.IsStrictSetBytesModeEnabled())
            {
                legacyCompatibilityOverride = true;
            }

            if (m_BindingBackend == null || m_BindingBackend.Mode != mode)
            {
                m_BindingBackend?.Dispose();
                m_BindingBackend = MetalBindingBackendFactory.Create(m_MetalDevice, mode, MetalBindingPipelineType.Raytracing, legacyCompatibilityOverride);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
            return mode;
        }

        private void EnsureComputeEncoder(in MetalCommandEncodingPath path)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (path == MetalCommandEncodingPath.MTL4)
            {
                if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
                {
                    return;
                }

                if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
                {
                    m_NativeAccelEncoder.EndEncoding();
                    m_NativeAccelEncoder = default;
                }

                m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().ComputeCommandEncoder();
                if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTL4 ray tracing compute encoder.");
                }

                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                return;
            }

            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeAccelEncoder.EndEncoding();
                m_NativeAccelEncoder = default;
            }

            m_NativeEncoder = commandBuffer.EnsureClassicCommandBuffer().ComputeCommandEncoder();
            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal ray tracing compute encoder.");
            }
        }

        private void EnsureAccelerationEncoderClassic()
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            commandBuffer.LockEncodingPath(MetalCommandEncodingPath.Classic, "ray tracing acceleration-structure build");
            if (m_NativeAccelEncoder.NativePtr != IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                throw new InvalidOperationException("Ray tracing acceleration-structure build cannot switch to classic path after MTL4 encoding has started.");
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.EndEncoding();
                m_NativeEncoder = default;
            }

            m_NativeAccelEncoder = commandBuffer.EnsureClassicCommandBuffer().AccelerationStructureCommandEncoder();
            if (m_NativeAccelEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create Metal acceleration-structure encoder.");
            }
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private bool HasNativeEncoder => m_NativeEncoder.NativePtr != IntPtr.Zero || m_NativeEncoder4.NativePtr != IntPtr.Zero || m_NativeAccelEncoder.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalRasterEncoder : RHIRasterEncoder
    {
        private const uint DrawIndirectArgsStride = 16;
        private const uint DrawIndexedIndirectArgsStride = 20;

        internal MTLRenderCommandEncoder NativeEncoder => m_NativeEncoder;

        private readonly MetalDevice m_MetalDevice;
        private MTLRenderCommandEncoder m_NativeEncoder;
        private MTL4RenderCommandEncoder m_NativeEncoder4;
        private MTLBuffer m_IndexBuffer;
        private ulong m_IndexBufferOffset;
        private MTLIndexType m_IndexType;
        private IMetalBindingBackend? m_BindingBackend;
        private string? m_PendingPassDebugGroup;
        private RHIRasterPassDescriptor m_PendingPassDescriptor;
        private bool m_HasPendingPassDescriptor;
        private readonly Dictionary<uint, uint> m_VertexStrides;

        internal MetalRasterEncoder(MetalCommandBuffer commandBuffer)
        {
            m_CommandBuffer = commandBuffer;
            m_MetalDevice = ((MetalCommandQueue)commandBuffer.CommandQueue).MetalDevice;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_IndexBuffer = default;
            m_IndexBufferOffset = 0;
            m_IndexType = MTLIndexType.UInt16;
            m_BindingBackend = null;
            m_PendingPassDebugGroup = null;
            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_VertexStrides = new Dictionary<uint, uint>();
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = default;
            m_NativeEncoder4 = default;
            m_PendingPassDescriptor = descriptor;
            m_HasPendingPassDescriptor = true;
            m_PendingPassDebugGroup = null;

            if (commandBuffer.EncodingPath == MetalCommandEncodingPath.Classic)
            {
                EnsureEncoderForPath(MetalCommandEncodingPath.Classic);
            }
            else if (commandBuffer.EncodingPath == MetalCommandEncodingPath.MTL4)
            {
                EnsureEncoderForPath(MetalCommandEncodingPath.MTL4);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                if (HasNativeEncoder)
                {
                    PushDebugGroup(descriptor.Name);
                }
                else
                {
                    m_PendingPassDebugGroup = descriptor.Name;
                }
            }
        }

        internal void WaitForFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.WaitForFence(fence, MTLRenderStages.RenderStageVertex | MTLRenderStages.RenderStageFragment);
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Vertex) | MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Fragment);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.WaitForFence(fence, stage);
            }
        }

        internal void SignalFence(in MTLFence fence)
        {
            if (fence.NativePtr == IntPtr.Zero)
            {
                return;
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.UpdateFence(fence, MTLRenderStages.RenderStageVertex | MTLRenderStages.RenderStageFragment);
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong stage = MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Vertex) | MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Fragment);
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.UpdateFence(fence, stage);
            }
        }

        internal void ApplyImmediateBarrier(in MTLBarrierScope scope, in ulong afterStages, in ulong beforeStages)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                if (scope != 0)
                {
                    MTLRenderStages after = MetalBarrierHelper.ConvertToRenderStages(afterStages);
                    MTLRenderStages before = MetalBarrierHelper.ConvertToRenderStages(beforeStages);
                    if (after != 0 && before != 0)
                    {
                        m_NativeEncoder.MemoryBarrier(scope, after, before);
                    }
                }

                MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
                if (commandBuffer.EnableMetal4Barriers)
                {
                    MetalBarrierHelper.TryBarrierAfterEncoderStages(m_NativeEncoder.NativePtr, afterStages, beforeStages);
                }

                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                ulong resolvedAfter = afterStages != 0 ? afterStages : (MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Vertex) | MetalUtility.ConvertToMetal4Stages(ERHIPipelineStage.Fragment));
                ulong resolvedBefore = beforeStages != 0 ? beforeStages : resolvedAfter;
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.BarrierAfterEncoderStages(resolvedAfter, resolvedBefore, MTL4VisibilityOptions.Device);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PushDebugGroup(new NSString(name));
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PushDebugGroup(new NSString(name));
                return;
            }

            throw new InvalidOperationException("Raster encoder is not created yet. Set pipeline before using debug groups.");
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.PopDebugGroup();
                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.PopDebugGroup();
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
            RequireEncoderForState("SetScissor");
            MTLScissorRect nativeRect = new MTLScissorRect
            {
                x = (ulong)Math.Max(0, rect.left),
                y = (ulong)Math.Max(0, rect.top),
                width = (ulong)Math.Max(0, rect.right - rect.left),
                height = (ulong)Math.Max(0, rect.bottom - rect.top)
            };
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetScissorRect(nativeRect);
            }
            else
            {
                m_NativeEncoder4.SetScissorRect(nativeRect);
            }
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
            RequireEncoderForState("SetViewport");
            MTLViewport nativeViewport = new MTLViewport
            {
                originX = viewport.TopLeftX,
                originY = viewport.TopLeftY,
                width = viewport.Width,
                height = viewport.Height,
                znear = viewport.MinDepth,
                zfar = viewport.MaxDepth
            };
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetViewport(nativeViewport);
            }
            else
            {
                m_NativeEncoder4.SetViewport(nativeViewport);
            }
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
            RequireEncoderForState("SetStencilRef");
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetStencilReferenceValue(value);
            }
            else
            {
                m_NativeEncoder4.SetStencilReferenceValue(value);
            }
        }

        public override void SetBlendFactor(in float4 value)
        {
            RequireEncoderForState("SetBlendFactor");
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetBlendColor(value.x, value.y, value.z, value.w);
            }
            else
            {
                m_NativeEncoder4.SetBlendColor(value.x, value.y, value.z, value.w);
            }
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalRasterPipeline metalPipeline = (MetalRasterPipeline)pipeline;
            MetalPipelineLayout pipelineLayout = pipeline.Descriptor.PipelineLayout as MetalPipelineLayout ?? throw new InvalidOperationException("Raster pipeline layout must be a MetalPipelineLayout.");
            MetalBindingMode mode = ConfigureBindingBackend(pipelineLayout, out MetalCommandEncodingPath path);
            EnsureEncoderForPath(path);
            ApplyPendingPassDebugGroup();
            ((MetalCommandBuffer)m_CommandBuffer!).ApplyPendingBarrierForActiveEncoderIfNeeded();
            BuildVertexStrideMap(metalPipeline);

            if (path == MetalCommandEncodingPath.Classic)
            {
                m_NativeEncoder.SetRenderPipelineState(metalPipeline.NativePipelineState);
                if (metalPipeline.DepthStencilState.NativePtr != IntPtr.Zero)
                {
                    m_NativeEncoder.SetDepthStencilState(metalPipeline.DepthStencilState);
                }

                m_NativeEncoder.SetCullMode(metalPipeline.CullMode);
                m_NativeEncoder.SetTriangleFillMode(metalPipeline.FillMode);
                m_NativeEncoder.SetFrontFacingWinding(metalPipeline.Winding);
            }
            else
            {
                m_NativeEncoder4.SetRenderPipelineState(metalPipeline.NativePipelineState);
                if (metalPipeline.DepthStencilState.NativePtr != IntPtr.Zero)
                {
                    m_NativeEncoder4.SetDepthStencilState(metalPipeline.DepthStencilState.NativePtr);
                }

                m_NativeEncoder4.SetCullMode(metalPipeline.CullMode);
                m_NativeEncoder4.SetTriangleFillMode(metalPipeline.FillMode);
                m_NativeEncoder4.SetFrontFacingWinding(metalPipeline.Winding);
            }

            MetalBindingLogHelper.LogPipelineModeOnce("Raster", metalPipeline.NativePipelineState.NativePtr, mode, path);
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            if (m_BindingBackend == null)
            {
                throw new InvalidOperationException("Raster pipeline must be set before binding resource tables.");
            }

            MetalResourceTable table = (MetalResourceTable)resourceTable;
            m_BindingBackend.SetResourceTable(table, tableIndex);
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
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetVertexBuffer(metalBuffer.NativeBuffer, offset, slot);
                return;
            }

            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Raster encoder is not created yet. Set pipeline before binding vertex buffers.");
            }

            ulong address = metalBuffer.NativeBuffer.GpuAddress + offset;
            m_VertexStrides.TryGetValue(slot, out uint stride);
            m_BindingBackend?.SetRasterVertexBuffer(slot, address, stride);
        }

        public override void SetShadingRate(in ERHIShadingRate shadingRate, in ERHIShadingRateCombiner shadingRateCombiner)
        {
        }

        public override void Draw(in uint vertexCount, in uint instanceCount, in uint firstVertex, in uint firstInstance)
        {
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder);
                m_NativeEncoder.DrawPrimitives(primitiveType, firstVertex, vertexCount, instanceCount, firstInstance);
            }
            else
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                m_NativeEncoder4.DrawPrimitives(primitiveType, firstVertex, vertexCount, instanceCount, firstInstance);
            }
        }

        public override void DrawIndexed(in uint indexCount, in uint instanceCount, in uint firstIndex, in uint baseVertex, in uint firstInstance)
        {
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            ulong indexOffset = m_IndexBufferOffset + firstIndex * (m_IndexType == MTLIndexType.UInt16 ? 2UL : 4UL);
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder);
                m_NativeEncoder.DrawIndexedPrimitives(primitiveType, indexCount, m_IndexType, m_IndexBuffer, indexOffset, instanceCount, baseVertex, firstInstance);
                return;
            }

            if (m_IndexBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Index buffer must be bound before DrawIndexed.");
            }

            ulong indexAddress = m_IndexBuffer.GpuAddress + indexOffset;
            ulong indexLength = indexOffset < m_IndexBuffer.Length ? m_IndexBuffer.Length - indexOffset : 0;
            m_BindingBackend?.CommitRaster(m_NativeEncoder4);
            m_NativeEncoder4.DrawIndexedPrimitives(primitiveType, indexCount, m_IndexType, indexAddress, indexLength, instanceCount, baseVertex, firstInstance);
        }

        public override void DrawIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            for (uint i = 0; i < drawCount; ++i)
            {
                if (m_NativeEncoder.NativePtr != IntPtr.Zero)
                {
                    m_BindingBackend?.CommitRaster(m_NativeEncoder);
                    m_NativeEncoder.DrawPrimitives(primitiveType, metalBuffer.NativeBuffer, offset + i * DrawIndirectArgsStride);
                }
                else
                {
                    m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                    ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + offset + i * DrawIndirectArgsStride;
                    m_NativeEncoder4.DrawPrimitives(primitiveType, indirectAddress);
                }
            }
        }

        public override void DrawIndexedIndirect(RHIBuffer argsBuffer, in uint offset, in uint drawCount)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            MTLPrimitiveType primitiveType = ResolvePrimitiveType();
            if (m_IndexBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Index buffer must be bound before DrawIndexedIndirect.");
            }

            ulong indexLength = m_IndexBufferOffset < m_IndexBuffer.Length ? m_IndexBuffer.Length - m_IndexBufferOffset : 0;
            ulong indexAddress = m_IndexBuffer.GpuAddress + m_IndexBufferOffset;
            for (uint i = 0; i < drawCount; ++i)
            {
                if (m_NativeEncoder.NativePtr != IntPtr.Zero)
                {
                    m_BindingBackend?.CommitRaster(m_NativeEncoder);
                    m_NativeEncoder.DrawIndexedPrimitives(primitiveType, m_IndexType, m_IndexBuffer, m_IndexBufferOffset, metalBuffer.NativeBuffer, offset + i * DrawIndexedIndirectArgsStride);
                }
                else
                {
                    m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                    ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + offset + i * DrawIndexedIndirectArgsStride;
                    m_NativeEncoder4.DrawIndexedPrimitives(primitiveType, m_IndexType, indexAddress, indexLength, indirectAddress);
                }
            }
        }

        public override void DispatchMesh(in uint groupCountX, in uint groupCountY, in uint groupCountZ)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder);
                m_NativeEncoder.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            }
            else
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                m_NativeEncoder4.DrawMeshThreadgroups(new MTLSize(groupCountX, groupCountY, groupCountZ), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            }
        }

        public override void DispatchMeshIndirect(RHIBuffer argsBuffer, in uint argsOffset)
        {
            MetalBuffer metalBuffer = (MetalBuffer)argsBuffer;
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder);
                m_NativeEncoder.DrawMeshThreadgroups(metalBuffer.NativeBuffer, argsOffset, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            }
            else
            {
                m_BindingBackend?.CommitRaster(m_NativeEncoder4);
                ulong indirectAddress = metalBuffer.NativeBuffer.GpuAddress + argsOffset;
                m_NativeEncoder4.DrawMeshThreadgroupsWithIndirectBuffer(indirectAddress, new MTLSize(1, 1, 1), new MTLSize(1, 1, 1));
            }
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

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder encoder4 = new MTL4CommandEncoder(m_NativeEncoder4.NativePtr);
                encoder4.EndEncoding();
                m_NativeEncoder4 = default;
            }

            m_PendingPassDescriptor = default;
            m_HasPendingPassDescriptor = false;
            m_PendingPassDebugGroup = null;
            m_VertexStrides.Clear();
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
            m_BindingBackend?.Dispose();
            m_BindingBackend = null;
        }

        private MetalBindingMode ConfigureBindingBackend(MetalPipelineLayout pipelineLayout, out MetalCommandEncodingPath path)
        {
            MetalBindingMode mode = MetalBindingPolicyResolver.Resolve(m_MetalDevice.BindingCapabilities, pipelineLayout.ResourceTableLayoutCount);
            path = mode == MetalBindingMode.ArgumentTable ? MetalCommandEncodingPath.MTL4 : MetalCommandEncodingPath.Classic;
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            commandBuffer.LockEncodingPath(path, "raster pipeline set");

            bool? legacyCompatibilityOverride = null;
            if (mode == MetalBindingMode.SetBytes && !MetalBindingPolicyResolver.IsStrictSetBytesModeEnabled())
            {
                legacyCompatibilityOverride = true;
            }

            if (m_BindingBackend == null || m_BindingBackend.Mode != mode)
            {
                m_BindingBackend?.Dispose();
                m_BindingBackend = MetalBindingBackendFactory.Create(m_MetalDevice, mode, MetalBindingPipelineType.Raster, legacyCompatibilityOverride);
            }

            m_BindingBackend.ResetForPipeline(pipelineLayout);
            return mode;
        }

        private void EnsureEncoderForPath(in MetalCommandEncodingPath path)
        {
            if (!m_HasPendingPassDescriptor)
            {
                throw new InvalidOperationException("Raster pass descriptor is not set before encoder creation.");
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            if (path == MetalCommandEncodingPath.Classic)
            {
                if (m_NativeEncoder.NativePtr != IntPtr.Zero)
                {
                    return;
                }

                MTLRenderPassDescriptor passDescriptor = BuildClassicRenderPassDescriptor(commandBuffer, m_PendingPassDescriptor);
                m_NativeEncoder = commandBuffer.EnsureClassicCommandBuffer().RenderCommandEncoder(passDescriptor);
                if (m_NativeEncoder.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create MTLRenderCommandEncoder.");
                }

                return;
            }

            if (m_NativeEncoder4.NativePtr != IntPtr.Zero)
            {
                return;
            }

            MTL4RenderPassDescriptor passDescriptor4 = BuildMtl4RenderPassDescriptor(commandBuffer, m_PendingPassDescriptor);
            m_NativeEncoder4 = commandBuffer.EnsureMtl4CommandBuffer().RenderCommandEncoder(passDescriptor4);
            if (m_NativeEncoder4.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4RenderCommandEncoder.");
            }
        }

        private static MTLRenderPassDescriptor BuildClassicRenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor)
        {
            MTLRenderPassDescriptor passDescriptor = MTLRenderPassDescriptor.New();
            PopulateRenderPassDescriptor(commandBuffer, descriptor, passDescriptor);
            return passDescriptor;
        }

        private static MTL4RenderPassDescriptor BuildMtl4RenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor)
        {
            MTL4RenderPassDescriptor passDescriptor = MTL4RenderPassDescriptor.New();
            PopulateRenderPassDescriptor(commandBuffer, descriptor, passDescriptor);
            return passDescriptor;
        }

        private static void PopulateRenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor, MTLRenderPassDescriptor passDescriptor)
        {
            passDescriptor.RenderTargetArrayLength = descriptor.ArrayLength;

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachment = ref descriptor.ColorAttachments.Span[i];
                MetalTexture colorTexture = (MetalTexture)colorAttachment.RenderTarget;
                MTLRenderPassColorAttachmentDescriptor nativeColor = passDescriptor.ColorAttachments[(uint)i];
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

            if (!descriptor.DepthStencilAttachment.HasValue)
            {
                return;
            }

            RHIDepthStencilAttachmentDescriptor depthStencil = descriptor.DepthStencilAttachment.Value;
            MetalTexture depthTexture = (MetalTexture)depthStencil.RenderTarget;

            MTLRenderPassDepthAttachmentDescriptor depthAttachment = passDescriptor.DepthAttachment;
            depthAttachment.Texture = depthTexture.NativeTexture;
            depthAttachment.Level = depthStencil.MipLevel;
            depthAttachment.Slice = depthStencil.ArraySlice;
            depthAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.DepthLoadOp);
            depthAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.DepthStoreOp);
            depthAttachment.ClearDepth = depthStencil.DepthClearValue;

            MTLRenderPassStencilAttachmentDescriptor stencilAttachment = passDescriptor.StencilAttachment;
            stencilAttachment.Texture = depthTexture.NativeTexture;
            stencilAttachment.Level = depthStencil.MipLevel;
            stencilAttachment.Slice = depthStencil.ArraySlice;
            stencilAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.StencilLoadOp);
            stencilAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.StencilStoreOp);
            stencilAttachment.ClearStencil = (uint)depthStencil.StencilClearValue;
        }

        private static void PopulateRenderPassDescriptor(MetalCommandBuffer commandBuffer, in RHIRasterPassDescriptor descriptor, MTL4RenderPassDescriptor passDescriptor)
        {
            passDescriptor.RenderTargetArrayLength = descriptor.ArrayLength;

            for (int i = 0; i < descriptor.ColorAttachments.Length; ++i)
            {
                ref RHIColorAttachmentDescriptor colorAttachment = ref descriptor.ColorAttachments.Span[i];
                MetalTexture colorTexture = (MetalTexture)colorAttachment.RenderTarget;
                MTLRenderPassColorAttachmentDescriptor nativeColor = passDescriptor.ColorAttachments[(uint)i];
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

            if (!descriptor.DepthStencilAttachment.HasValue)
            {
                return;
            }

            RHIDepthStencilAttachmentDescriptor depthStencil = descriptor.DepthStencilAttachment.Value;
            MetalTexture depthTexture = (MetalTexture)depthStencil.RenderTarget;

            MTLRenderPassDepthAttachmentDescriptor depthAttachment = passDescriptor.DepthAttachment;
            depthAttachment.Texture = depthTexture.NativeTexture;
            depthAttachment.Level = depthStencil.MipLevel;
            depthAttachment.Slice = depthStencil.ArraySlice;
            depthAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.DepthLoadOp);
            depthAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.DepthStoreOp);
            depthAttachment.ClearDepth = depthStencil.DepthClearValue;

            MTLRenderPassStencilAttachmentDescriptor stencilAttachment = passDescriptor.StencilAttachment;
            stencilAttachment.Texture = depthTexture.NativeTexture;
            stencilAttachment.Level = depthStencil.MipLevel;
            stencilAttachment.Slice = depthStencil.ArraySlice;
            stencilAttachment.LoadAction = MetalUtility.ConvertToMetalLoadAction(depthStencil.StencilLoadOp);
            stencilAttachment.StoreAction = MetalUtility.ConvertToMetalStoreAction(depthStencil.StencilStoreOp);
            stencilAttachment.ClearStencil = (uint)depthStencil.StencilClearValue;
        }

        private void ApplyPendingPassDebugGroup()
        {
            if (string.IsNullOrWhiteSpace(m_PendingPassDebugGroup))
            {
                return;
            }

            PushDebugGroup(m_PendingPassDebugGroup);
            m_PendingPassDebugGroup = null;
        }

        private void BuildVertexStrideMap(MetalRasterPipeline pipeline)
        {
            m_VertexStrides.Clear();
            RHIVertexAssemblerDescriptor? vertexAssembler = pipeline.Descriptor.PrimitiveAssembler.VertexAssembler;
            if (!vertexAssembler.HasValue)
            {
                return;
            }

            Span<RHIVertexLayoutDescriptor> layouts = vertexAssembler.Value.VertexLayouts.Span;
            for (int i = 0; i < layouts.Length; ++i)
            {
                ref readonly RHIVertexLayoutDescriptor layout = ref layouts[i];
                m_VertexStrides[layout.Index] = layout.Stride;
            }
        }

        private MTLPrimitiveType ResolvePrimitiveType()
        {
            if (m_CachedPipeline is MetalRasterPipeline rasterPipeline)
            {
                return rasterPipeline.PrimitiveType;
            }

            return MTLPrimitiveType.Triangle;
        }

        private void RequireEncoderForState(string operation)
        {
            if (HasNativeEncoder)
            {
                return;
            }

            throw new InvalidOperationException($"{operation} requires an active raster encoder. Set pipeline first so encoding path can be resolved.");
        }

        private bool HasNativeEncoder => m_NativeEncoder.NativePtr != IntPtr.Zero || m_NativeEncoder4.NativePtr != IntPtr.Zero;
    }

    internal sealed class MetalMLEncoder : RHIMLEncoder
    {
        private MTL4MachineLearningCommandEncoder m_NativeEncoder;
        private readonly MetalDevice m_MetalDevice;

        public MetalMLEncoder(MetalCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_MetalDevice = ((MetalCommandQueue)cmdBuffer.CommandQueue).MetalDevice;
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
        {
            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            m_NativeEncoder = default;

            // ML encoding requires MTL4 path
            commandBuffer.LockEncodingPath(MetalCommandEncodingPath.MTL4, "ML pass");
            MTL4CommandBuffer mtl4CmdBuffer = commandBuffer.EnsureMtl4CommandBuffer();
            m_NativeEncoder = mtl4CmdBuffer.MachineLearningCommandEncoder();

            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MTL4MachineLearningCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
            // TODO: MTL4 timestamp support for ML pass
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline;
            MetalMLPipeline metalPipeline = (MetalMLPipeline)pipeline;
            if (metalPipeline.NativePipelineState.NativePtr != IntPtr.Zero)
            {
                m_NativeEncoder.SetPipelineState(metalPipeline.NativePipelineState);
            }
        }

        public override void SetResourceTable(RHIResourceTable resourceTable, in uint tableIndex)
        {
            // TODO: Convert RHIResourceTable to MTL4ArgumentTable and bind
            // m_NativeEncoder.SetArgumentTable(argumentTable);
        }

        public override void Dispatch(RHIHeap intermediatesHeap)
        {
            if (intermediatesHeap != null)
            {
                MetalHeap metalHeap = (MetalHeap)intermediatesHeap;
                // TODO: Get native MTLHeap from MetalHeap and dispatch
                // m_NativeEncoder.DispatchNetworkWithIntermediatesHeap(nativeHeap);
            }
        }

        public override void EndPass()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
            m_CachedPipeline = null;
        }

        protected override void Release()
        {
        }
    }
}
