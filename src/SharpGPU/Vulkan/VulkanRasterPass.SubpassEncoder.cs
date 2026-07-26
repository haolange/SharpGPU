using System;
using System.Runtime.InteropServices;
using SharpGPU.Mathematics;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe sealed class VulkanRasterSubpassEncoder :
        VulkanRasterEncoder
    {
        private const uint AttachmentUnused = uint.MaxValue;

        private readonly VulkanCommandBuffer m_VulkanCommandBuffer;
        private RHIRasterPassDescriptor m_PassDescriptor;
        private RasterPassPlan? m_Plan;
        private VulkanRasterPassLowering? m_Lowering;
        private VulkanRenderPass2Plan? m_RenderPass2Plan;
        private VkImageView[]? m_ColorViews;
        private VkImageView[]? m_ColorResolveViews;
        private VkImageView m_DepthView;
        private VkImageView m_DepthResolveView;
        private VkRenderingAttachmentInfo* m_ColorAttachmentInfos;
        private VkRenderingAttachmentInfo* m_DepthAttachmentInfo;
        private VkRenderingAttachmentInfo* m_StencilAttachmentInfo;
        private VulkanSampledFeedbackAttachmentFact[]?
            m_SampledFeedbackAttachments;
        private WeakReference<VulkanBindingTable>?[]?
            m_BoundFeedbackTables;
        private ulong[]? m_BoundFeedbackRevisions;
        private byte[]? m_BoundFeedbackMasks;
        private IVulkanRasterNativePipeline? m_ActiveNativePipeline;
        private bool m_RenderingActive;

        internal VulkanRasterSubpassEncoder(
            VulkanCommandBuffer commandBuffer)
            : base(commandBuffer)
        {
            m_VulkanCommandBuffer = commandBuffer;
        }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;

            VulkanDevice device = GetDevice();
            m_Plan = plan;
            m_PassDescriptor = plan.DescriptorSnapshot;
            m_ActiveNativePipeline = null;
            try
            {
                VulkanRasterCapabilities capabilities =
                    device.RasterCapabilities;
                m_Lowering = VulkanRasterPassLowering.Compile(
                    plan,
                    in capabilities,
                    VulkanRasterStrategyDiagnostics.ForcedStrategy);
                InitializeSampledFeedbackState(
                    plan,
                    m_Lowering,
                    device);
                ValidateRasterOrderedAttachmentStorageFormats(
                    plan,
                    m_Lowering,
                    device);

#if DEBUG
                PushDebugGroup(plan.Name);
#endif
                if (m_PassDescriptor.Timestamp.HasValue)
                {
                    WriteTimestamp(
                        m_PassDescriptor.Timestamp.Value.BeginIndex);
                }

                CreateAttachmentViews(plan);
                TransitionAttachmentScopeLayouts(plan, m_Lowering);
                if (m_Lowering.UsesRenderPass2)
                {
                    BeginRenderPass2(plan, m_Lowering);
                }
                else
                {
                    BeginDynamicRendering(plan, m_Lowering);
                    if (m_Lowering.UsesDynamicRenderingLocalRead)
                    {
                        ApplyDynamicAttachmentMapping(0);
                    }
                }
                m_RenderingActive = true;
            }
            catch
            {
                AbortPassState();
                ClearRasterPassState();
                throw;
            }
        }

        internal void AbortPassState()
        {
            m_RenderingActive = false;
            m_CachedPipeline = null;
            m_ActiveNativePipeline = null;
            m_Plan = null;
            m_Lowering = null;
            m_RenderPass2Plan = null;
            m_ColorViews = null;
            m_ColorResolveViews = null;
            m_DepthView = default;
            m_DepthResolveView = default;
            m_ColorAttachmentInfos = null;
            m_DepthAttachmentInfo = null;
            m_StencilAttachmentInfo = null;
            m_SampledFeedbackAttachments = null;
            m_BoundFeedbackTables = null;
            m_BoundFeedbackRevisions = null;
            m_BoundFeedbackMasks = null;
            m_PassDescriptor = default;
            m_CurrentSubPassIndex = 0;
        }

        public override void NextSubPass()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            int sourceSubPassIndex = m_CurrentSubPassIndex;
            int destinationSubPassIndex = sourceSubPassIndex + 1;
            if (destinationSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            VulkanRasterPassLowering lowering =
                RequireActiveLowering();
            if (destinationSubPassIndex != sourceSubPassIndex + 1)
            {
                throw new InvalidOperationException(
                    "Vulkan subpasses must advance exactly once in order.");
            }

            if (lowering.UsesRenderPass2)
            {
                VkSubpassBeginInfo beginInfo = new()
                {
                    sType = VkStructureType.SubpassBeginInfo,
                    contents = VkSubpassContents.Inline,
                };
                VkSubpassEndInfo endInfo = new()
                {
                    sType = VkStructureType.SubpassEndInfo,
                };
                VulkanNative.vkCmdNextSubpass2(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    &beginInfo,
                    &endInfo,
                    GetDevice().UseRenderPass2KhrCommands);
            }
            else
            {
                EmitDynamicPhaseBarrier(sourceSubPassIndex);
                if (lowering.UsesDynamicRenderingLocalRead)
                {
                    ApplyDynamicAttachmentMapping(
                        destinationSubPassIndex);
                }
            }
            m_CurrentSubPassIndex = destinationSubPassIndex;
            m_PipelineSubPassIndex = -1;
            m_ActiveNativePipeline = null;
        }

        public override void SetPipeline(
            RHIRasterPipeline pipeline)
        {
            if (pipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new ArgumentException(
                    "Vulkan raster encoding requires a Vulkan pipeline.",
                    nameof(pipeline));
            }
            IVulkanRasterNativePipeline nativePipeline = publicPipeline;
            if (RequireActiveLowering().UsesRenderPass2)
            {
                VulkanRasterNativeVariant variant =
                    publicPipeline.CreateCompatibleVariant(
                        m_RenderPass2Plan?.NativeRenderPass ??
                            throw new InvalidOperationException(
                                "The Vulkan RenderPass2 plan is not active."),
                        checked((uint)m_CurrentSubPassIndex));
                m_VulkanCommandBuffer
                    .RegisterTransientRasterPipeline(variant);
                nativePipeline = variant;
            }

            if (!nativePipeline.HasNativePipeline)
            {
                throw new NotSupportedException(
                    "This Vulkan raster pipeline has no native dynamic " +
                    "variant; bind it inside a compatible RenderPass2 pass.");
            }
            ClearBoundSampledFeedbackTables();
            m_CachedPipeline = publicPipeline;
            m_ActiveNativePipeline = nativePipeline;
            VulkanNative.vkCmdBindPipeline(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                nativePipeline.NativePipeline);
            BindPrivateAttachmentSet(nativePipeline);
        }

        public override void SetBindingTable(
            RHIBindingTable resourceTable,
            in uint tableIndex)
        {
            IVulkanRasterNativePipeline nativePipeline =
                m_ActiveNativePipeline
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before " +
                    "binding an binding table.");
            if (m_CachedPipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new InvalidOperationException(
                    "The public Vulkan raster pipeline is unavailable.");
            }
            VulkanBindingTable table =
                publicPipeline.VulkanPipelineLayout.ResolveReadyTable(
                    resourceTable,
                    tableIndex);
            VkDescriptorSet descriptorSet =
                table.NativeDescriptorSet;
            byte requiredMask =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex].SampledInputMask;
            if (requiredMask != 0)
            {
                VulkanSampledFeedbackAttachmentFact[] attachments =
                    m_SampledFeedbackAttachments
                    ?? throw new InvalidOperationException(
                        "The Vulkan sampled-feedback attachment facts " +
                        "are unavailable.");
                VulkanDescriptorSetLease clone =
                    table.CloneForSampledFeedback(
                        attachments,
                        out byte matchedMask,
                        out ulong descriptorRevision);
                using VulkanDescriptorSetLeaseTransaction transaction =
                    new(
                        in clone,
                        value =>
                            GetDevice().DescriptorPoolAllocator.Free(
                                in value));
                transaction.Commit(
                    m_VulkanCommandBuffer
                        .RegisterTransientDescriptorSetLease);
                descriptorSet = clone.Set;
                RecordSampledFeedbackTable(
                    tableIndex,
                    table,
                    descriptorRevision,
                    checked((byte)(matchedMask & requiredMask)));
            }
            else
            {
                ClearBoundSampledFeedbackTable(tableIndex);
            }
            VulkanNative.vkCmdBindDescriptorSets(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                nativePipeline.EffectiveNativePipelineLayout,
                tableIndex,
                1,
                &descriptorSet,
                0,
                null);
        }

        public override void SetPushConstants(
            IntPtr data,
            in uint size,
            in uint offset = 0)
        {
            IVulkanRasterNativePipeline nativePipeline =
                m_ActiveNativePipeline
                ?? throw new InvalidOperationException(
                    "A live Vulkan raster pipeline must be set before " +
                    "writing push constants.");
            if (m_CachedPipeline is not VulkanRasterPipeline publicPipeline)
            {
                throw new InvalidOperationException(
                    "The public Vulkan raster pipeline is unavailable.");
            }
            if (offset + size >
                publicPipeline.VulkanPipelineLayout.PushConstantSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    "The push-constant range exceeds the pipeline layout.");
            }
            VulkanNative.vkCmdPushConstants(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                nativePipeline.EffectiveNativePipelineLayout,
                VkShaderStageFlags.All,
                offset,
                size,
                data.ToPointer());
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            throw new InvalidOperationException(
                "External Vulkan resource and queue barriers must be " +
                "recorded outside an active raster pass. Ordered subpass " +
                "dependencies are emitted by the private raster planner.");
        }

        public override void Barriers(
            ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length != 0)
            {
                throw new InvalidOperationException(
                    "External Vulkan resource and queue barriers must be " +
                    "recorded outside an active raster pass.");
            }
        }

        public override void WriteTimestamp(in uint index)
        {
            if (!m_PassDescriptor.Timestamp.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Timestamp.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The timestamp query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdWriteTimestamp(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.AllGraphics,
                query.NativeQueryPool,
                index);
        }

        public override void BeginOcclusion(in uint index)
        {
            if (!m_PassDescriptor.Occlusion.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Occlusion.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The occlusion query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdBeginQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                VkQueryControlFlags.Precise);
        }

        public override void EndOcclusion(in uint index)
        {
            if (!m_PassDescriptor.Occlusion.HasValue)
            {
                return;
            }
            VulkanQuery query =
                (VulkanQuery)m_PassDescriptor.Occlusion.Value.Query;
            VulkanNative.vkCmdEndQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index);
        }

        public override void BeginStatistics(in uint index)
        {
            if (!m_PassDescriptor.Statistics.HasValue)
            {
                return;
            }
            VulkanQuery query =
                m_PassDescriptor.Statistics.Value.Query as VulkanQuery
                ?? throw new ArgumentException(
                    "The statistics query must belong to Vulkan.");
            VulkanNative.vkCmdResetQueryPool(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                1);
            VulkanNative.vkCmdBeginQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index,
                0);
        }

        public override void EndStatistics(in uint index)
        {
            if (!m_PassDescriptor.Statistics.HasValue)
            {
                return;
            }
            VulkanQuery query =
                (VulkanQuery)m_PassDescriptor.Statistics.Value.Query;
            VulkanNative.vkCmdEndQuery(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                query.NativeQueryPool,
                index);
        }

        public override void Draw(
            in uint vertexCount,
            in uint instanceCount,
            in uint firstVertex,
            in uint firstInstance)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDraw(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                vertexCount,
                instanceCount,
                firstVertex,
                firstInstance);
        }

        public override void DrawIndexed(
            in uint indexCount,
            in uint instanceCount,
            in uint firstIndex,
            in uint baseVertex,
            in uint firstInstance)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDrawIndexed(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                indexCount,
                instanceCount,
                firstIndex,
                checked((int)baseVertex),
                firstInstance);
        }

        public override void DrawIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawIndirect(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                offset,
                drawCount,
                20);
        }

        public override void DrawIndexedIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawIndexedIndirect(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                offset,
                drawCount,
                20);
        }

        public override void DispatchMesh(
            in uint groupCountX,
            in uint groupCountY,
            in uint groupCountZ)
        {
            RequireBoundPipeline();
            VulkanNative.vkCmdDrawMeshTasksEXT(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                groupCountX,
                groupCountY,
                groupCountZ);
        }

        public override void DispatchMeshIndirect(
            RHIBuffer argsBuffer,
            in uint argsOffset)
        {
            RequireBoundPipeline();
            VulkanBuffer arguments =
                argsBuffer as VulkanBuffer
                ?? throw new ArgumentException(
                    "Indirect arguments must belong to Vulkan.",
                    nameof(argsBuffer));
            VulkanNative.vkCmdDrawMeshTasksIndirectEXT(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                arguments.NativeBuffer,
                argsOffset,
                1,
                0);
        }

        public override void ExecuteIndirectCommandBuffer(
            RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            GetDevice().Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan raster ExecuteIndirectCommandBuffer");
            throw new NotSupportedException(
                "Vulkan raster ExecuteIndirectCommandBuffer is unavailable.");
        }

        public override void EndPass()
        {
            RHICommandBuffer commandBuffer = m_CommandBuffer ??
                throw new InvalidOperationException("The raster encoder is not attached to a command buffer.");
            commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Raster);
            RasterPassPlan plan = RequireActiveRasterPass();
            if (m_CurrentSubPassIndex != plan.SubPassCount - 1)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' ended at subpass {m_CurrentSubPassIndex}, " +
                    $"but {plan.SubPassCount} subpasses were declared.");
            }

            if (!m_RenderingActive)
            {
                throw new InvalidOperationException(
                    "The Vulkan raster pass is not active.");
            }
            VulkanRasterPassLowering lowering =
                RequireActiveLowering();
            if (lowering.UsesRenderPass2)
            {
                VkSubpassEndInfo endInfo = new()
                {
                    sType = VkStructureType.SubpassEndInfo,
                };
                VulkanNative.vkCmdEndRenderPass2(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    &endInfo,
                    GetDevice().UseRenderPass2KhrCommands);
            }
            else
            {
                VulkanNative.vkCmdEndRendering(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    GetDevice().UseDynamicRenderingKhrCommands);
            }
            RestoreCanonicalAttachmentLayouts();
            m_RenderingActive = false;

            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(
                    m_PassDescriptor.Timestamp.Value.EndIndex);
            }
#if DEBUG
            PopDebugGroup();
#endif
            m_CachedPipeline = null;
            m_ActiveNativePipeline = null;
            m_Plan = null;
            m_Lowering = null;
            m_RenderPass2Plan = null;
            m_ColorViews = null;
            m_ColorResolveViews = null;
            m_DepthView = default;
            m_DepthResolveView = default;
            m_ColorAttachmentInfos = null;
            m_DepthAttachmentInfo = null;
            m_StencilAttachmentInfo = null;
            m_SampledFeedbackAttachments = null;
            m_BoundFeedbackTables = null;
            m_BoundFeedbackRevisions = null;
            m_BoundFeedbackMasks = null;
            m_PassDescriptor = default;
            commandBuffer.MarkEncoderEndFromEncoder();
            ClearRasterPassState();
        }

        private void CreateAttachmentViews(RasterPassPlan plan)
        {
            m_ColorViews =
                new VkImageView[plan.ColorAttachmentCount];
            m_ColorResolveViews =
                new VkImageView[plan.ColorAttachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(colorIndex);
                m_ColorViews[colorIndex] = CreateImageView(
                    attachment.RenderTarget,
                    attachment.SubresourceRange);
                if (attachment.ResolveTarget != null)
                {
                    m_ColorResolveViews[colorIndex] =
                        CreateImageView(
                            attachment.ResolveTarget,
                            attachment.ResolveSubresourceRange);
                }
            }

            if (plan.HasDepthStencilAttachment)
            {
                RHIDepthStencilAttachmentDescriptor depth =
                    plan.GetDepthStencilAttachment();
                m_DepthView = CreateImageView(
                    depth.RenderTarget,
                    depth.SubresourceRange);
                if (depth.ResolveTarget != null)
                {
                    m_DepthResolveView = CreateImageView(
                        depth.ResolveTarget,
                        depth.ResolveSubresourceRange);
                }
            }
        }

        private VkImageView CreateImageView(
            RHITexture texture,
            in RHITextureSubresourceRange range)
        {
            VulkanTexture vulkanTexture =
                texture as VulkanTexture
                ?? throw new ArgumentException(
                    "Raster attachments must belong to Vulkan.",
                    nameof(texture));
            VulkanDevice device = GetDevice();
            if (!ReferenceEquals(vulkanTexture.VulkanDevice, device))
            {
                throw new ArgumentException(
                    "Raster attachments must belong to the encoder device.",
                    nameof(texture));
            }
            VkImageViewCreateInfo createInfo = new()
            {
                sType = VkStructureType.ImageViewCreateInfo,
                image = vulkanTexture.NativeImage,
                viewType =
                    VulkanUtility.ConvertToVkImageViewType(
                        vulkanTexture.Descriptor.Dimension),
                format =
                    VulkanUtility.ConvertToVkFormat(
                        vulkanTexture.Descriptor.Format),
                subresourceRange =
                    new VkImageSubresourceRange
                    {
                        aspectMask =
                            range.AspectMask.ToVkImageAspectFlags(),
                        baseMipLevel = range.BaseMipLevel,
                        levelCount = range.MipLevelCount,
                        baseArrayLayer = range.BaseArrayLayer,
                        layerCount = range.ArrayLayerCount,
                    },
            };
            VkImageView view = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateImageView(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &view));
            m_VulkanCommandBuffer.RegisterTransientImageView(view);
            return view;
        }

        private void BeginDynamicRendering(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            VulkanDevice device = GetDevice();
            int colorCount = plan.ColorAttachmentCount;
            m_ColorAttachmentInfos =
                (VkRenderingAttachmentInfo*)NativeMemory.AllocZeroed(
                    (nuint)Math.Max(1, colorCount),
                    (nuint)sizeof(VkRenderingAttachmentInfo));
            m_VulkanCommandBuffer.RegisterTransientAllocation(
                m_ColorAttachmentInfos);
            VkAttachmentFeedbackLoopInfoEXT* feedbackLoopInfos =
                stackalloc VkAttachmentFeedbackLoopInfoEXT[
                    Math.Max(1, colorCount)];
            for (int colorIndex = 0;
                 colorIndex < colorCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(colorIndex);
                EVulkanRasterAttachmentScopeLayout scopeLayout =
                    lowering.AttachmentScopeLayouts.Span[colorIndex];

                VkResolveModeFlags colorResolveMode =
                    attachment.ResolveTarget == null
                        ? VkResolveModeFlags.None
                        : IsIntegerColorFormat(
                            attachment.RenderTarget.Descriptor.Format)
                            ? VkResolveModeFlags.SampleZero
                            : VkResolveModeFlags.Average;
                VkClearValue clearValue = default;
                clearValue.color.float32[0] =
                    attachment.ClearValue.x;
                clearValue.color.float32[1] =
                    attachment.ClearValue.y;
                clearValue.color.float32[2] =
                    attachment.ClearValue.z;
                clearValue.color.float32[3] =
                    attachment.ClearValue.w;
                bool hasOrderedFeedbackLoop =
                    VulkanRasterFeedbackLoopUtility.TryCreateAttachmentInfo(
                        scopeLayout,
                        out feedbackLoopInfos[colorIndex]);
                m_ColorAttachmentInfos[colorIndex] =
                    new VkRenderingAttachmentInfo
                    {
                        sType =
                            VkStructureType.RenderingAttachmentInfo,
                        pNext =
                            hasOrderedFeedbackLoop
                                ? &feedbackLoopInfos[colorIndex]
                                : null,
                        imageView = m_ColorViews![colorIndex],
                        imageLayout =
                            ConvertScopeLayout(scopeLayout),
                        resolveMode = colorResolveMode,
                        resolveImageView =
                            colorResolveMode ==
                                VkResolveModeFlags.None
                                ? default
                                : m_ColorResolveViews![colorIndex],
                        resolveImageLayout =
                            VkImageLayout.ColorAttachmentOptimal,
                        loadOp =
                            VulkanUtility.ConvertToVkLoadOp(
                                attachment.LoadAction),
                        storeOp =
                            ConvertRasterStoreOp(
                                attachment.StoreAction),
                        clearValue = clearValue,
                    };
            }

            VkRenderingAttachmentInfo* depthInfo = null;
            VkRenderingAttachmentInfo* stencilInfo = null;
            if (plan.HasDepthStencilAttachment)
            {
                RHIDepthStencilAttachmentDescriptor depthStencil =
                    plan.GetDepthStencilAttachment();
                bool hasDepth =
                    (plan.DepthStencilAspects &
                     ERHITextureAspectMask.Depth) != 0;
                bool hasStencil =
                    (plan.DepthStencilAspects &
                     ERHITextureAspectMask.Stencil) != 0;
                VkResolveModeFlags depthResolveMode = hasDepth
                    ? ConvertResolveMode(
                        depthStencil.DepthResolveMode)
                    : VkResolveModeFlags.None;
                VkResolveModeFlags stencilResolveMode = hasStencil
                    ? ConvertResolveMode(
                        depthStencil.StencilResolveMode)
                    : VkResolveModeFlags.None;
                ValidateDepthStencilResolveModes(
                    device,
                    depthStencil.ResolveTarget != null,
                    depthResolveMode,
                    stencilResolveMode);
                GetDepthStencilRenderingLayouts(
                    device,
                    lowering,
                    in depthStencil,
                    hasDepth,
                    hasStencil,
                    out VkImageLayout depthLayout,
                    out VkImageLayout stencilLayout);

                if (hasDepth)
                {
                    m_DepthAttachmentInfo =
                        (VkRenderingAttachmentInfo*)
                            NativeMemory.AllocZeroed(
                                1,
                                (nuint)sizeof(
                                    VkRenderingAttachmentInfo));
                    m_VulkanCommandBuffer
                        .RegisterTransientAllocation(
                            m_DepthAttachmentInfo);
                    m_DepthAttachmentInfo[0] =
                        new VkRenderingAttachmentInfo
                        {
                            sType =
                                VkStructureType
                                    .RenderingAttachmentInfo,
                            imageView = m_DepthView,
                            imageLayout = depthLayout,
                            resolveMode = depthResolveMode,
                            resolveImageView =
                                depthResolveMode ==
                                    VkResolveModeFlags.None
                                    ? default
                                    : m_DepthResolveView,
                            resolveImageLayout =
                                VkImageLayout
                                    .DepthStencilAttachmentOptimal,
                            loadOp =
                                VulkanUtility.ConvertToVkLoadOp(
                                    depthStencil.DepthLoadOp),
                            storeOp =
                                ConvertRasterStoreOp(
                                    depthStencil.DepthStoreOp),
                            clearValue =
                                new VkClearValue(
                                    depthStencil.DepthClearValue,
                                    checked((uint)
                                        depthStencil
                                            .StencilClearValue)),
                        };
                    depthInfo = m_DepthAttachmentInfo;
                }
                if (hasStencil)
                {
                    m_StencilAttachmentInfo =
                        (VkRenderingAttachmentInfo*)
                            NativeMemory.AllocZeroed(
                                1,
                                (nuint)sizeof(
                                    VkRenderingAttachmentInfo));
                    m_VulkanCommandBuffer
                        .RegisterTransientAllocation(
                            m_StencilAttachmentInfo);
                    m_StencilAttachmentInfo[0] =
                        new VkRenderingAttachmentInfo
                        {
                            sType =
                                VkStructureType
                                    .RenderingAttachmentInfo,
                            imageView = m_DepthView,
                            imageLayout = stencilLayout,
                            resolveMode = stencilResolveMode,
                            resolveImageView =
                                stencilResolveMode ==
                                    VkResolveModeFlags.None
                                    ? default
                                    : m_DepthResolveView,
                            resolveImageLayout =
                                VkImageLayout
                                    .DepthStencilAttachmentOptimal,
                            loadOp =
                                VulkanUtility.ConvertToVkLoadOp(
                                    depthStencil.StencilLoadOp),
                            storeOp =
                                ConvertRasterStoreOp(
                                    depthStencil.StencilStoreOp),
                            clearValue =
                                new VkClearValue(
                                    depthStencil.DepthClearValue,
                                    checked((uint)
                                        depthStencil
                                            .StencilClearValue)),
                        };
                    stencilInfo = m_StencilAttachmentInfo;
                }
            }

            VkRenderingInfo renderingInfo = new()
            {
                sType = VkStructureType.RenderingInfo,
                renderArea = new VkRect2D
                {
                    extent = new VkExtent2D
                    {
                        width = plan.Width,
                        height = plan.Height,
                    },
                },
                layerCount = plan.ArrayLength,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachments =
                    colorCount == 0
                        ? null
                        : m_ColorAttachmentInfos,
                pDepthAttachment = depthInfo,
                pStencilAttachment = stencilInfo,
            };
            VulkanNative.vkCmdBeginRendering(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &renderingInfo,
                device.UseDynamicRenderingKhrCommands);
            for (int colorIndex = 0;
                 colorIndex < colorCount;
                 ++colorIndex)
            {
                m_ColorAttachmentInfos[colorIndex].pNext = null;
            }
        }
        private void BeginRenderPass2(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            VulkanDevice device = GetDevice();
            m_RenderPass2Plan = VulkanRenderPass2Lowering.Create(
                device.NativeDevice,
                plan,
                lowering,
                device.UseRenderPass2KhrCommands);
            m_VulkanCommandBuffer.RegisterTransientRenderPass(
                m_RenderPass2Plan.NativeRenderPass);

            int attachmentCount =
                m_RenderPass2Plan.AttachmentCount;
            VkImageView* attachments =
                stackalloc VkImageView[attachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                attachments[colorIndex] =
                    m_ColorViews![colorIndex];
                int resolveIndex =
                    m_RenderPass2Plan
                        .ColorResolveAttachmentIndices[colorIndex];
                if (resolveIndex >= 0)
                {
                    attachments[resolveIndex] =
                        m_ColorResolveViews![colorIndex];
                }
            }
            if (m_RenderPass2Plan.DepthStencilAttachmentIndex >= 0)
            {
                attachments[
                    m_RenderPass2Plan
                        .DepthStencilAttachmentIndex] =
                    m_DepthView;
            }
            if (m_RenderPass2Plan
                    .DepthStencilResolveAttachmentIndex >= 0)
            {
                attachments[
                    m_RenderPass2Plan
                        .DepthStencilResolveAttachmentIndex] =
                    m_DepthResolveView;
            }

            VkFramebufferCreateInfo framebufferInfo = new()
            {
                sType = VkStructureType.FramebufferCreateInfo,
                renderPass = m_RenderPass2Plan.NativeRenderPass,
                attachmentCount =
                    checked((uint)attachmentCount),
                pAttachments = attachments,
                width = plan.Width,
                height = plan.Height,
                layers = plan.ArrayLength,
            };
            VkFramebuffer framebuffer = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateFramebuffer(
                    device.NativeDevice,
                    &framebufferInfo,
                    null,
                    &framebuffer));
            m_VulkanCommandBuffer.RegisterTransientFramebuffer(
                framebuffer);

            VkClearValue* clearValues =
                stackalloc VkClearValue[attachmentCount];
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                clearValues[colorIndex].color.float32[0] =
                    color.ClearValue.x;
                clearValues[colorIndex].color.float32[1] =
                    color.ClearValue.y;
                clearValues[colorIndex].color.float32[2] =
                    color.ClearValue.z;
                clearValues[colorIndex].color.float32[3] =
                    color.ClearValue.w;
            }
            if (m_RenderPass2Plan.DepthStencilAttachmentIndex >= 0)
            {
                RHIDepthStencilAttachmentDescriptor depth =
                    plan.GetDepthStencilAttachment();
                clearValues[
                    m_RenderPass2Plan
                        .DepthStencilAttachmentIndex] =
                    new VkClearValue(
                        depth.DepthClearValue,
                        checked((uint)depth.StencilClearValue));
            }

            VkRenderPassBeginInfo beginInfo = new()
            {
                sType = VkStructureType.RenderPassBeginInfo,
                renderPass = m_RenderPass2Plan.NativeRenderPass,
                framebuffer = framebuffer,
                renderArea = new VkRect2D
                {
                    extent = new VkExtent2D
                    {
                        width = plan.Width,
                        height = plan.Height,
                    },
                },
                clearValueCount =
                    checked((uint)attachmentCount),
                pClearValues = clearValues,
            };
            VkSubpassBeginInfo subPassBegin = new()
            {
                sType = VkStructureType.SubpassBeginInfo,
                contents = VkSubpassContents.Inline,
            };
            VulkanNative.vkCmdBeginRenderPass2(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &beginInfo,
                &subPassBegin,
                device.UseRenderPass2KhrCommands);
        }

        private void ApplyDynamicAttachmentMapping(
            int subPassIndex)
        {
            VulkanRasterSubPassLowering subPass =
                RequireActiveLowering().SubPasses.Span[
                    subPassIndex];
            int colorCount =
                m_Plan?.ColorAttachmentCount ??
                throw new InvalidOperationException(
                    "The Vulkan raster plan is not active.");
            uint* outputLocations =
                stackalloc uint[Math.Max(colorCount, 1)];
            uint* inputIndices =
                stackalloc uint[Math.Max(colorCount, 1)];
            VulkanDynamicRenderingAttachmentMappingPlan.Populate(
                in subPass,
                colorCount,
                outputLocations,
                inputIndices);

            VkRenderingAttachmentLocationInfo locationInfo = new()
            {
                sType =
                    VkStructureType
                        .RenderingAttachmentLocationInfo,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachmentLocations = outputLocations,
            };
            VkRenderingInputAttachmentIndexInfo inputInfo = new()
            {
                sType =
                    VkStructureType
                        .RenderingInputAttachmentIndexInfo,
                colorAttachmentCount =
                    checked((uint)colorCount),
                pColorAttachmentInputIndices = inputIndices,
            };
            VulkanDevice device = GetDevice();
            VulkanNative.vkCmdSetRenderingAttachmentLocations(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &locationInfo,
                device.UseDynamicRenderingLocalReadKhrCommands);
            VulkanNative.vkCmdSetRenderingInputAttachmentIndices(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                &inputInfo,
                device.UseDynamicRenderingLocalReadKhrCommands);
        }

        private void EmitDynamicPhaseBarrier(
            int sourceSubPassIndex)
        {
            VulkanRasterPhaseBarrierPlan barrier =
                RequireActiveLowering().PhaseBarriers.Span[
                    sourceSubPassIndex];
            if (barrier.Kind ==
                EVulkanRasterPhaseBarrierKind.None)
            {
                return;
            }
            VkMemoryBarrier nativeBarrier = new()
            {
                sType = VkStructureType.MemoryBarrier,
                srcAccessMask =
                    VkAccessFlags.ColorAttachmentWrite |
                    VkAccessFlags.ShaderWrite,
                dstAccessMask =
                    VkAccessFlags.InputAttachmentRead |
                    VkAccessFlags.ShaderRead |
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite,
            };
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.ColorAttachmentOutput |
                    VkPipelineStageFlags.FragmentShader,
                VkPipelineStageFlags.FragmentShader |
                    VkPipelineStageFlags.ColorAttachmentOutput,
                VkDependencyFlags.ByRegion,
                1,
                &nativeBarrier,
                0,
                null,
                0,
                null);
        }

        private void TransitionAttachmentScopeLayouts(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                VulkanTexture texture =
                    (VulkanTexture)color.RenderTarget;
                VkImageLayout destinationLayout =
                    ConvertScopeLayout(
                        lowering.AttachmentScopeLayouts
                            .Span[colorIndex]);
                if (lowering.UsesRenderPass2 &&
                    destinationLayout is
                        VkImageLayout.RenderingLocalRead or
                        VkImageLayout.AttachmentFeedbackLoopOptimalEXT)
                {
                    destinationLayout =
                        VkImageLayout.ColorAttachmentOptimal;
                }
                TransitionExactColorAttachment(
                    texture,
                    in color.SubresourceRange,
                    destinationLayout);

                if (color.ResolveTarget != null)
                {
                    VulkanTexture colorResolveTexture =
                        (VulkanTexture)color.ResolveTarget;
                    TransitionExactAttachmentLayout(
                        colorResolveTexture,
                        in color.ResolveSubresourceRange,
                        VkImageLayout.TransferDstOptimal,
                        VkImageLayout.ColorAttachmentOptimal,
                        depthStencil: false,
                        "A color resolve attachment");
                }
            }

            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }

            VulkanDevice device = GetDevice();
            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            VulkanTexture depthTexture =
                (VulkanTexture)depthStencil.RenderTarget;
            bool hasDepth =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0;
            bool hasStencil =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0;
            bool depthReadOnly =
                hasDepth &&
                IsDepthAspectReadOnly(
                    lowering,
                    in depthStencil);
            bool stencilReadOnly =
                hasStencil &&
                IsStencilAspectReadOnly(
                    lowering,
                    in depthStencil);
            GetDepthStencilRenderingLayouts(
                device,
                lowering,
                in depthStencil,
                hasDepth,
                hasStencil,
                out VkImageLayout depthLayout,
                out VkImageLayout stencilLayout);

            bool combinedBarrier =
                RequiresCombinedDepthStencilBarrier(
                    device,
                    depthTexture);
            if (combinedBarrier)
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        depthTexture,
                        in depthStencil.SubresourceRange);
                bool allSelectedAspectsReadOnly =
                    (!hasDepth || depthReadOnly) &&
                    (!hasStencil || stencilReadOnly);
                VkImageLayout assertedLayout =
                    allSelectedAspectsReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal;
                VkImageLayout destinationLayout =
                    hasDepth ? depthLayout : stencilLayout;
                TransitionExactAttachmentLayout(
                    depthTexture,
                    in combinedRange,
                    assertedLayout,
                    destinationLayout,
                    depthStencil: true,
                    "A combined depth/stencil attachment");
            }
            else
            {
                if (hasDepth)
                {
                    RHITextureSubresourceRange depthRange =
                        SelectAspect(
                            in depthStencil.SubresourceRange,
                            ERHITextureAspectMask.Depth);
                    TransitionExactAttachmentLayout(
                        depthTexture,
                        in depthRange,
                        depthReadOnly
                            ? VkImageLayout
                                .DepthStencilReadOnlyOptimal
                            : VkImageLayout
                                .DepthStencilAttachmentOptimal,
                        depthLayout,
                        depthStencil: true,
                        "A depth attachment");
                }
                if (hasStencil)
                {
                    RHITextureSubresourceRange stencilRange =
                        SelectAspect(
                            in depthStencil.SubresourceRange,
                            ERHITextureAspectMask.Stencil);
                    TransitionExactAttachmentLayout(
                        depthTexture,
                        in stencilRange,
                        stencilReadOnly
                            ? VkImageLayout
                                .DepthStencilReadOnlyOptimal
                            : VkImageLayout
                                .DepthStencilAttachmentOptimal,
                        stencilLayout,
                        depthStencil: true,
                        "A stencil attachment");
                }
            }

            if (depthStencil.ResolveTarget == null)
            {
                return;
            }
            VulkanTexture resolveTexture =
                (VulkanTexture)depthStencil.ResolveTarget;
            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    resolveTexture))
            {
                RHITextureSubresourceRange combinedResolveRange =
                    ExpandToNativeDepthStencilAspects(
                        resolveTexture,
                        in depthStencil.ResolveSubresourceRange);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in combinedResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A combined depth/stencil resolve attachment");
                return;
            }
            if (hasDepth)
            {
                RHITextureSubresourceRange depthResolveRange =
                    SelectAspect(
                        in depthStencil.ResolveSubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in depthResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A depth resolve attachment");
            }
            if (hasStencil)
            {
                RHITextureSubresourceRange stencilResolveRange =
                    SelectAspect(
                        in depthStencil.ResolveSubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in stencilResolveRange,
                    VkImageLayout.TransferDstOptimal,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A stencil resolve attachment");
            }
        }

        private void TransitionExactColorAttachment(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout destinationLayout)
        {
            const VkImageLayout CanonicalLayout =
                VkImageLayout.ColorAttachmentOptimal;
            m_VulkanCommandBuffer.RequireKnownImageLayout(
                texture,
                in range,
                CanonicalLayout,
                "A color attachment");
            if (destinationLayout == CanonicalLayout)
            {
                return;
            }

            VkImageMemoryBarrier barrier = new()
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask =
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite,
                dstAccessMask =
                    VkAccessFlags.ColorAttachmentRead |
                    VkAccessFlags.ColorAttachmentWrite |
                    VkAccessFlags.InputAttachmentRead |
                    VkAccessFlags.ShaderRead |
                    VkAccessFlags.ShaderWrite,
                oldLayout = CanonicalLayout,
                newLayout = destinationLayout,
                srcQueueFamilyIndex = uint.MaxValue,
                dstQueueFamilyIndex = uint.MaxValue,
                image = texture.NativeImage,
                subresourceRange =
                    ConvertToNativeRange(in range),
            };
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineStageFlags.ColorAttachmentOutput,
                VkPipelineStageFlags.FragmentShader |
                    VkPipelineStageFlags.ColorAttachmentOutput,
                VkDependencyFlags.ByRegion,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            m_VulkanCommandBuffer.SetKnownImageLayout(
                texture,
                in range,
                destinationLayout);
        }

        private void RestoreCanonicalAttachmentLayouts()
        {
            RasterPassPlan? plan = m_Plan;
            VulkanRasterPassLowering? lowering = m_Lowering;
            if (plan == null || lowering == null)
            {
                return;
            }
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                VkImageLayout sourceLayout =
                    ConvertScopeLayout(
                        lowering.AttachmentScopeLayouts
                            .Span[colorIndex]);
                if (lowering.UsesRenderPass2 &&
                    sourceLayout is
                        VkImageLayout.RenderingLocalRead or
                        VkImageLayout.AttachmentFeedbackLoopOptimalEXT)
                {
                    sourceLayout =
                        VkImageLayout.ColorAttachmentOptimal;
                }
                if (sourceLayout ==
                    VkImageLayout.ColorAttachmentOptimal)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                VulkanTexture texture =
                    (VulkanTexture)color.RenderTarget;
                m_VulkanCommandBuffer.RequireKnownImageLayout(
                    texture,
                    in color.SubresourceRange,
                    sourceLayout,
                    "A color attachment");
                VkImageMemoryBarrier barrier = new()
                {
                    sType = VkStructureType.ImageMemoryBarrier,
                    srcAccessMask =
                        VkAccessFlags.InputAttachmentRead |
                        VkAccessFlags.ShaderRead |
                        VkAccessFlags.ShaderWrite |
                        VkAccessFlags.ColorAttachmentRead |
                        VkAccessFlags.ColorAttachmentWrite,
                    dstAccessMask =
                        VkAccessFlags.ColorAttachmentRead |
                        VkAccessFlags.ColorAttachmentWrite,
                    oldLayout = sourceLayout,
                    newLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                    srcQueueFamilyIndex = uint.MaxValue,
                    dstQueueFamilyIndex = uint.MaxValue,
                    image = texture.NativeImage,
                    subresourceRange =
                        ConvertToNativeRange(
                            in color.SubresourceRange),
                };
                VulkanNative.vkCmdPipelineBarrier(
                    m_VulkanCommandBuffer.NativeCommandBuffer,
                    VkPipelineStageFlags.FragmentShader |
                        VkPipelineStageFlags.ColorAttachmentOutput,
                    VkPipelineStageFlags.ColorAttachmentOutput,
                    VkDependencyFlags.ByRegion,
                    0,
                    null,
                    0,
                    null,
                    1,
                    &barrier);
                m_VulkanCommandBuffer.SetKnownImageLayout(
                    texture,
                    in color.SubresourceRange,
                    VkImageLayout.ColorAttachmentOptimal);
            }
            RestoreResolveDestinationLayouts(plan);
            RestoreDepthStencilBoundaryLayouts(plan, lowering);
        }

        private void RestoreResolveDestinationLayouts(
            RasterPassPlan plan)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                if (color.ResolveTarget == null)
                {
                    continue;
                }
                VulkanTexture colorResolveTexture =
                    (VulkanTexture)color.ResolveTarget;
                TransitionExactAttachmentLayout(
                    colorResolveTexture,
                    in color.ResolveSubresourceRange,
                    VkImageLayout.ColorAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: false,
                    "A color resolve attachment");
            }
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            if (descriptor.ResolveTarget == null)
            {
                return;
            }

            VulkanDevice device = GetDevice();
            VulkanTexture resolveTexture =
                (VulkanTexture)descriptor.ResolveTarget;
            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    resolveTexture))
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        resolveTexture,
                        in descriptor.ResolveSubresourceRange);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in combinedRange,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A combined depth/stencil resolve attachment");
                return;
            }
            if ((plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.ResolveSubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in range,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A depth resolve attachment");
            }
            if ((plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.ResolveSubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    resolveTexture,
                    in range,
                    VkImageLayout.DepthStencilAttachmentOptimal,
                    VkImageLayout.TransferDstOptimal,
                    depthStencil: true,
                    "A stencil resolve attachment");
            }
        }

        private void RestoreDepthStencilBoundaryLayouts(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering)
        {
            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }
            VulkanDevice device = GetDevice();
            RHIDepthStencilAttachmentDescriptor descriptor =
                plan.GetDepthStencilAttachment();
            VulkanTexture texture =
                (VulkanTexture)descriptor.RenderTarget;
            bool hasDepth =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Depth) != 0;
            bool hasStencil =
                (plan.DepthStencilAspects &
                 ERHITextureAspectMask.Stencil) != 0;
            bool depthReadOnly =
                hasDepth &&
                IsDepthAspectReadOnly(
                    lowering,
                    in descriptor);
            bool stencilReadOnly =
                hasStencil &&
                IsStencilAspectReadOnly(
                    lowering,
                    in descriptor);
            GetDepthStencilRenderingLayouts(
                device,
                lowering,
                in descriptor,
                hasDepth,
                hasStencil,
                out VkImageLayout depthLayout,
                out VkImageLayout stencilLayout);

            if (RequiresCombinedDepthStencilBarrier(
                    device,
                    texture))
            {
                RHITextureSubresourceRange combinedRange =
                    ExpandToNativeDepthStencilAspects(
                        texture,
                        in descriptor.SubresourceRange);
                bool allSelectedAspectsReadOnly =
                    (!hasDepth || depthReadOnly) &&
                    (!hasStencil || stencilReadOnly);
                TransitionExactAttachmentLayout(
                    texture,
                    in combinedRange,
                    hasDepth ? depthLayout : stencilLayout,
                    allSelectedAspectsReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A combined depth/stencil attachment");
                return;
            }

            if (hasDepth)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.SubresourceRange,
                        ERHITextureAspectMask.Depth);
                TransitionExactAttachmentLayout(
                    texture,
                    in range,
                    depthLayout,
                    depthReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A depth attachment");
            }
            if (hasStencil)
            {
                RHITextureSubresourceRange range =
                    SelectAspect(
                        in descriptor.SubresourceRange,
                        ERHITextureAspectMask.Stencil);
                TransitionExactAttachmentLayout(
                    texture,
                    in range,
                    stencilLayout,
                    stencilReadOnly
                        ? VkImageLayout
                            .DepthStencilReadOnlyOptimal
                        : VkImageLayout
                            .DepthStencilAttachmentOptimal,
                    depthStencil: true,
                    "A stencil attachment");
            }
        }

        private void TransitionExactAttachmentLayout(
            VulkanTexture texture,
            in RHITextureSubresourceRange range,
            VkImageLayout sourceLayout,
            VkImageLayout destinationLayout,
            bool depthStencil,
            string operation)
        {
            m_VulkanCommandBuffer.RequireKnownImageLayout(
                texture,
                in range,
                sourceLayout,
                operation);
            if (sourceLayout == destinationLayout)
            {
                return;
            }
            VkImageMemoryBarrier barrier = new()
            {
                sType = VkStructureType.ImageMemoryBarrier,
                srcAccessMask = depthStencil
                    ? VkAccessFlags.DepthStencilAttachmentRead |
                      VkAccessFlags.DepthStencilAttachmentWrite
                    : VkAccessFlags.ColorAttachmentRead |
                      VkAccessFlags.ColorAttachmentWrite |
                      VkAccessFlags.TransferWrite,
                dstAccessMask = depthStencil
                    ? VkAccessFlags.DepthStencilAttachmentRead |
                      VkAccessFlags.DepthStencilAttachmentWrite |
                      VkAccessFlags.TransferWrite
                    : VkAccessFlags.ColorAttachmentRead |
                      VkAccessFlags.ColorAttachmentWrite |
                      VkAccessFlags.TransferWrite,
                oldLayout = sourceLayout,
                newLayout = destinationLayout,
                srcQueueFamilyIndex = uint.MaxValue,
                dstQueueFamilyIndex = uint.MaxValue,
                image = texture.NativeImage,
                subresourceRange = ConvertToNativeRange(in range),
            };
            VkPipelineStageFlags stages = depthStencil
                ? VkPipelineStageFlags.EarlyFragmentTests |
                  VkPipelineStageFlags.LateFragmentTests |
                  VkPipelineStageFlags.Transfer
                : VkPipelineStageFlags.ColorAttachmentOutput |
                  VkPipelineStageFlags.Transfer;
            VulkanNative.vkCmdPipelineBarrier(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                stages,
                stages,
                VkDependencyFlags.ByRegion,
                0,
                null,
                0,
                null,
                1,
                &barrier);
            m_VulkanCommandBuffer.SetKnownImageLayout(
                texture,
                in range,
                destinationLayout);
        }

        private static bool RequiresCombinedDepthStencilBarrier(
            VulkanDevice device,
            VulkanTexture texture)
        {
            ERHITextureAspectMask expanded =
                VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                    device.SupportsSeparateDepthStencilLayouts,
                    texture.Descriptor.Format,
                    ERHITextureAspectMask.Depth);
            return (expanded &
                    (ERHITextureAspectMask.Depth |
                     ERHITextureAspectMask.Stencil)) ==
                (ERHITextureAspectMask.Depth |
                 ERHITextureAspectMask.Stencil);
        }

        private static RHITextureSubresourceRange
            ExpandToNativeDepthStencilAspects(
                VulkanTexture texture,
                in RHITextureSubresourceRange range)
        {
            ERHITextureAspectMask aspects =
                VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                    supportsSeparateDepthStencilLayouts: false,
                    texture.Descriptor.Format,
                    range.AspectMask);
            return new RHITextureSubresourceRange
            {
                AspectMask = aspects,
                BaseMipLevel = range.BaseMipLevel,
                MipLevelCount = range.MipLevelCount,
                BaseArrayLayer = range.BaseArrayLayer,
                ArrayLayerCount = range.ArrayLayerCount,
            };
        }

        private static RHITextureSubresourceRange SelectAspect(
            in RHITextureSubresourceRange range,
            ERHITextureAspectMask aspect) =>
            new()
            {
                AspectMask = aspect,
                BaseMipLevel = range.BaseMipLevel,
                MipLevelCount = range.MipLevelCount,
                BaseArrayLayer = range.BaseArrayLayer,
                ArrayLayerCount = range.ArrayLayerCount,
            };

        private static bool IsDepthAspectReadOnly(
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor) =>
            descriptor.DepthLoadOp == ERHILoadAction.Load &&
            (lowering.SubPasses.Span[0].DepthStencilFlags &
             ERHISubPassFlags.ReadOnlyDepth) != 0;

        private static bool IsStencilAspectReadOnly(
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor) =>
            descriptor.StencilLoadOp == ERHILoadAction.Load &&
            (lowering.SubPasses.Span[0].DepthStencilFlags &
             ERHISubPassFlags.ReadOnlyStencil) != 0;
        private static VkImageSubresourceRange ConvertToNativeRange(
            in RHITextureSubresourceRange range) =>
            new()
            {
                aspectMask =
                    range.AspectMask.ToVkImageAspectFlags(),
                baseMipLevel = range.BaseMipLevel,
                levelCount = range.MipLevelCount,
                baseArrayLayer = range.BaseArrayLayer,
                layerCount = range.ArrayLayerCount,
            };

        private void BindPrivateAttachmentSet(
            IVulkanRasterNativePipeline pipeline)
        {
            VulkanPrivateRasterBindingPlan bindingPlan =
                pipeline.PrivateBindingPlan;
            if (!bindingPlan.HasPrivateBindings)
            {
                return;
            }
            VulkanPrivateRasterDescriptorLayout descriptorLayout =
                pipeline.PrivateDescriptorLayout
                ?? throw new InvalidOperationException(
                    "The private Vulkan raster descriptor layout is missing.");
            VulkanDevice device = GetDevice();
            VulkanDescriptorSetLease lease =
                device.DescriptorPoolAllocator.Allocate(
                    descriptorLayout.PoolRequirements,
                    descriptorLayout.NativeLayout);
            using VulkanDescriptorSetLeaseTransaction transaction =
                new VulkanDescriptorSetLeaseTransaction(
                    in lease,
                    value =>
                        device.DescriptorPoolAllocator.Free(in value));

            VkWriteDescriptorSet* writes =
                stackalloc VkWriteDescriptorSet[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            VkDescriptorImageInfo* images =
                stackalloc VkDescriptorImageInfo[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            int writeCount = 0;
            VulkanRasterSubPassLowering subPass =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex];
            for (int inputIndex = 0;
                 inputIndex <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++inputIndex)
            {
                if (!bindingPlan.UsesInputAttachmentBinding(
                        inputIndex))
                {
                    continue;
                }
                int logicalAttachment =
                    subPass.GetColorInputLogicalAttachment(
                        inputIndex);
                images[writeCount] =
                    new VkDescriptorImageInfo
                    {
                        imageView =
                            m_ColorViews![logicalAttachment],
                        imageLayout =
                            RequireActiveLowering().UsesRenderPass2
                                ? GetRenderPassInputLayout(
                                    logicalAttachment)
                                : VkImageLayout
                                    .RenderingLocalRead,
                    };
                writes[writeCount] =
                    new VkWriteDescriptorSet
                    {
                        sType =
                            VkStructureType.WriteDescriptorSet,
                        dstSet = lease.Set,
                        dstBinding =
                            bindingPlan.GetInputAttachmentBinding(
                                inputIndex),
                        descriptorCount = 1,
                        descriptorType =
                            VkDescriptorType.InputAttachment,
                        pImageInfo = &images[writeCount],
                    };
                ++writeCount;
            }
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((bindingPlan.RasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                images[writeCount] =
                    new VkDescriptorImageInfo
                    {
                        imageView =
                            m_ColorViews![logicalAttachment],
                        imageLayout = VkImageLayout.General,
                    };
                writes[writeCount] =
                    new VkWriteDescriptorSet
                    {
                        sType =
                            VkStructureType.WriteDescriptorSet,
                        dstSet = lease.Set,
                        dstBinding =
                            bindingPlan.GetRasterOrderedBinding(
                                logicalAttachment),
                        descriptorCount = 1,
                        descriptorType =
                            VkDescriptorType.StorageImage,
                        pImageInfo = &images[writeCount],
                    };
                ++writeCount;
            }

            VulkanNative.vkUpdateDescriptorSets(
                device.NativeDevice,
                checked((uint)writeCount),
                writes,
                0,
                null);
            VkDescriptorSet descriptorSet = lease.Set;
            VulkanNative.vkCmdBindDescriptorSets(
                m_VulkanCommandBuffer.NativeCommandBuffer,
                VkPipelineBindPoint.Graphics,
                pipeline.EffectiveNativePipelineLayout,
                bindingPlan.DescriptorSet,
                1,
                &descriptorSet,
                0,
                null);
            transaction.Commit(
                m_VulkanCommandBuffer
                    .RegisterTransientDescriptorSetLease);
        }

        private VkImageLayout GetRenderPassInputLayout(
            int logicalAttachment)
        {
            EVulkanRasterAttachmentScopeLayout scope =
                RequireActiveLowering().AttachmentScopeLayouts.Span[
                    logicalAttachment];
            return scope ==
                EVulkanRasterAttachmentScopeLayout
                    .AttachmentFeedbackLoop
                ? VkImageLayout
                    .AttachmentFeedbackLoopOptimalEXT
                : VkImageLayout.ShaderReadOnlyOptimal;
        }

        private VulkanDevice GetDevice()
        {
            VulkanCommandQueue queue =
                m_VulkanCommandBuffer.CommandQueue as
                    VulkanCommandQueue
                ?? throw new InvalidOperationException(
                    "The Vulkan command buffer has no Vulkan queue.");
            return queue.VulkanDevice;
        }

        private VulkanRasterPassLowering
            RequireActiveLowering() =>
            m_Lowering
            ?? throw new InvalidOperationException(
                "The Vulkan raster pass is not active.");

        private static void
            ValidateRasterOrderedAttachmentStorageFormats(
                RasterPassPlan plan,
                VulkanRasterPassLowering lowering,
                VulkanDevice device)
        {
            byte rasterOrderedMask = 0;
            ReadOnlySpan<VulkanRasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                rasterOrderedMask |=
                    subPasses[subPassIndex].RasterOrderedMask;
            }
            if (rasterOrderedMask == 0)
            {
                return;
            }

            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((rasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(logicalAttachment);
                if (attachment.RenderTarget is not
                    VulkanTexture texture)
                {
                    throw new ArgumentException(
                        "Vulkan RasterOrderedReadWrite attachments must " +
                        "belong to Vulkan.",
                        nameof(plan));
                }
                if ((texture.Descriptor.UsageFlag &
                     ERHITextureUsage.RasterizerOrdered) == 0)
                {
                    throw new ArgumentException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} lacks canonical " +
                        "RasterizerOrdered texture usage.",
                        nameof(plan));
                }
                VkImageUsageFlags nativeUsage =
                    VulkanUtility.ConvertToVkImageUsage(
                        texture.Descriptor.UsageFlag,
                        device.SupportsAttachmentFeedbackLoopLayout);
                if ((nativeUsage & VkImageUsageFlags.Storage) == 0)
                {
                    throw new InvalidOperationException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} was not created with " +
                        "VK_IMAGE_USAGE_STORAGE_BIT.");
                }
                if (texture.Descriptor.SampleCount !=
                    ERHISampleCount.None)
                {
                    throw new NotSupportedException(
                        $"Vulkan RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} is multisampled. The " +
                        "current exact storage-image ABI is single-sample.");
                }

                VkFormat format =
                    VulkanUtility.ConvertToVkFormat(
                        texture.Descriptor.Format);
                VkFormatProperties formatProperties = default;
                VulkanNative.vkGetPhysicalDeviceFormatProperties(
                    device.NativePhysicalDevice,
                    format,
                    &formatProperties);
                if ((formatProperties.optimalTilingFeatures &
                     VkFormatFeatureFlags.StorageImage) == 0)
                {
                    throw new NotSupportedException(
                        $"Vulkan format {format} for " +
                        $"RasterOrderedReadWrite attachment " +
                        $"{logicalAttachment} lacks optimal-tiling " +
                        "VK_FORMAT_FEATURE_STORAGE_IMAGE_BIT.");
                }
            }
        }
        private void InitializeSampledFeedbackState(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            VulkanDevice device)
        {
            if (!lowering.UsesSampledFeedback)
            {
                m_SampledFeedbackAttachments = null;
                m_BoundFeedbackTables = null;
                m_BoundFeedbackRevisions = null;
                m_BoundFeedbackMasks = null;
                return;
            }

            byte passFeedbackMask = 0;
            ReadOnlySpan<VulkanRasterSubPassLowering> subPasses =
                lowering.SubPasses.Span;
            for (int subPassIndex = 0;
                 subPassIndex < subPasses.Length;
                 ++subPassIndex)
            {
                passFeedbackMask |=
                    subPasses[subPassIndex].SampledInputMask;
            }

            int attachmentCount =
                System.Numerics.BitOperations.PopCount(
                    (uint)passFeedbackMask);
            VulkanSampledFeedbackAttachmentFact[] attachments =
                new VulkanSampledFeedbackAttachmentFact[
                    attachmentCount];
            int factIndex = 0;
            for (int logicalAttachment = 0;
                 logicalAttachment < plan.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((passFeedbackMask & bit) == 0)
                {
                    continue;
                }
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(logicalAttachment);
                if (attachment.RenderTarget is not
                    VulkanTexture texture)
                {
                    throw new ArgumentException(
                        "Vulkan sampled-feedback attachments must " +
                        "belong to Vulkan.",
                        nameof(plan));
                }
                ERHITextureUsage requiredUsage =
                    ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.ShaderResource;
                if ((texture.Descriptor.UsageFlag & requiredUsage) !=
                    requiredUsage)
                {
                    throw new ArgumentException(
                        $"Vulkan SampledFeedback attachment " +
                        $"{logicalAttachment} requires both RenderTarget " +
                        "and ShaderResource texture usage.",
                        nameof(plan));
                }
                RHITextureSubresourceRange normalizedRange =
                    VulkanTextureSubresourceRangeUtility.Normalize(
                        texture.Descriptor,
                        in attachment.SubresourceRange);
                attachments[factIndex++] =
                    new VulkanSampledFeedbackAttachmentFact(
                        texture.NativeImage,
                        in normalizedRange,
                        bit);
            }

            int maximumBoundSets =
                checked((int)device.DescriptorLimits.MaximumBoundSets);
            m_SampledFeedbackAttachments = attachments;
            m_BoundFeedbackTables =
                new WeakReference<VulkanBindingTable>?[
                    maximumBoundSets];
            m_BoundFeedbackRevisions =
                new ulong[maximumBoundSets];
            m_BoundFeedbackMasks =
                new byte[maximumBoundSets];
        }

        private void RecordSampledFeedbackTable(
            uint tableIndex,
            VulkanBindingTable table,
            ulong descriptorRevision,
            byte matchedMask)
        {
            WeakReference<VulkanBindingTable>?[] tables =
                m_BoundFeedbackTables
                ?? throw new InvalidOperationException(
                    "The Vulkan sampled-feedback table state is " +
                    "unavailable.");
            if (tableIndex >= (uint)tables.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableIndex),
                    tableIndex,
                    "The descriptor-set index exceeds the Vulkan " +
                    "device limit.");
            }
            int index = checked((int)tableIndex);
            tables[index] =
                new WeakReference<VulkanBindingTable>(table);
            m_BoundFeedbackRevisions![index] =
                descriptorRevision;
            m_BoundFeedbackMasks![index] = matchedMask;
        }

        private void ClearBoundSampledFeedbackTable(
            uint tableIndex)
        {
            WeakReference<VulkanBindingTable>?[]? tables =
                m_BoundFeedbackTables;
            if (tables == null)
            {
                return;
            }
            if (tableIndex >= (uint)tables.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tableIndex),
                    tableIndex,
                    "The descriptor-set index exceeds the Vulkan " +
                    "device limit.");
            }
            int index = checked((int)tableIndex);
            tables[index] = null;
            m_BoundFeedbackRevisions![index] = 0;
            m_BoundFeedbackMasks![index] = 0;
        }

        private void ClearBoundSampledFeedbackTables()
        {
            if (m_BoundFeedbackTables != null)
            {
                Array.Clear(m_BoundFeedbackTables);
                Array.Clear(m_BoundFeedbackRevisions!);
                Array.Clear(m_BoundFeedbackMasks!);
            }
        }

        private void RequireBoundPipeline()
        {
            if (m_ActiveNativePipeline == null)
            {
                throw new InvalidOperationException(
                    "A compatible Vulkan raster pipeline must be " +
                    "bound before drawing.");
            }
            RequireSampledFeedbackBindings();
        }

        private void RequireSampledFeedbackBindings()
        {
            byte requiredMask =
                RequireActiveLowering().SubPasses.Span[
                    m_CurrentSubPassIndex].SampledInputMask;
            if (requiredMask == 0)
            {
                return;
            }

            WeakReference<VulkanBindingTable>?[] tables =
                m_BoundFeedbackTables
                ?? throw new InvalidOperationException(
                    "The Vulkan sampled-feedback table state is " +
                    "unavailable.");
            byte coveredMask = 0;
            for (int index = 0; index < tables.Length; ++index)
            {
                byte tableMask =
                    checked((byte)(
                        m_BoundFeedbackMasks![index] &
                        requiredMask));
                if (tableMask == 0)
                {
                    continue;
                }
                WeakReference<VulkanBindingTable>? weakTable =
                    tables[index];
                if (weakTable == null ||
                    !weakTable.TryGetTarget(
                        out VulkanBindingTable? table))
                {
                    throw new ObjectDisposedException(
                        $"VulkanBindingTable[{index}]",
                        "A SampledFeedback binding table is no " +
                        "longer alive; rebind a live table.");
                }
                table.EnsureReadyForBinding();
                if (table.DescriptorRevision !=
                    m_BoundFeedbackRevisions![index])
                {
                    throw new InvalidOperationException(
                        $"Vulkan SampledFeedback binding table {index} " +
                        "changed after binding. Rebind it before Draw.");
                }
                coveredMask |= tableMask;
            }
            if ((coveredMask & requiredMask) != requiredMask)
            {
                byte missingMask =
                    checked((byte)(requiredMask & ~coveredMask));
                throw new InvalidOperationException(
                    $"Vulkan SampledFeedback attachments 0x" +
                    $"{missingMask:X2} are not covered by exact sampled-" +
                    "image descriptor bindings in the current subpass.");
            }
        }

        private static VkImageLayout ConvertScopeLayout(
            EVulkanRasterAttachmentScopeLayout layout) =>
            layout switch
            {
                EVulkanRasterAttachmentScopeLayout
                    .ColorAttachment =>
                    VkImageLayout.ColorAttachmentOptimal,
                EVulkanRasterAttachmentScopeLayout
                    .RenderingLocalRead =>
                    VkImageLayout.RenderingLocalRead,
                EVulkanRasterAttachmentScopeLayout
                    .AttachmentFeedbackLoop =>
                    VkImageLayout
                        .AttachmentFeedbackLoopOptimalEXT,
                EVulkanRasterAttachmentScopeLayout
                    .GeneralStorage =>
                    VkImageLayout.General,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(layout),
                    layout,
                    "Unknown Vulkan raster attachment scope."),
            };

        private static VkAttachmentStoreOp ConvertRasterStoreOp(
            ERHIStoreAction action) =>
            action switch
            {
                ERHIStoreAction.Store or
                    ERHIStoreAction.StoreAndResolve =>
                    VkAttachmentStoreOp.Store,
                ERHIStoreAction.Resolve or
                    ERHIStoreAction.DontCare =>
                    VkAttachmentStoreOp.DontCare,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unknown raster attachment store action."),
            };

        private static bool IsIntegerColorFormat(
            ERHIPixelFormat format) =>
            format is
                ERHIPixelFormat.R8_UInt or
                ERHIPixelFormat.R8_SInt or
                ERHIPixelFormat.R16_UInt or
                ERHIPixelFormat.R16_SInt or
                ERHIPixelFormat.R8G8_UInt or
                ERHIPixelFormat.R8G8_SInt or
                ERHIPixelFormat.R32_UInt or
                ERHIPixelFormat.R32_SInt or
                ERHIPixelFormat.R16G16_UInt or
                ERHIPixelFormat.R16G16_SInt or
                ERHIPixelFormat.R8G8B8A8_UInt or
                ERHIPixelFormat.R8G8B8A8_SInt or
                ERHIPixelFormat.R10G10B10A2_UInt or
                ERHIPixelFormat.RG32_UInt or
                ERHIPixelFormat.RG32_SInt or
                ERHIPixelFormat.R16G16B16A16_UInt or
                ERHIPixelFormat.R16G16B16A16_SInt or
                ERHIPixelFormat.R32G32B32A32_UInt or
                ERHIPixelFormat.R32G32B32A32_SInt;

        private static void ValidateDepthStencilResolveModes(
            VulkanDevice device,
            bool hasResolveTarget,
            VkResolveModeFlags depthMode,
            VkResolveModeFlags stencilMode)
        {
            if (!hasResolveTarget)
            {
                if (depthMode != VkResolveModeFlags.None ||
                    stencilMode != VkResolveModeFlags.None)
                {
                    throw new ArgumentException(
                        "Depth/stencil resolve modes require a " +
                        "resolve target.");
                }
                return;
            }
            if (depthMode != VkResolveModeFlags.None &&
                (device.SupportedDepthResolveModes &
                 depthMode) == 0)
            {
                throw new NotSupportedException(
                    $"Vulkan depth resolve mode {depthMode} is " +
                    "not supported by the selected device.");
            }
            if (stencilMode != VkResolveModeFlags.None &&
                (device.SupportedStencilResolveModes &
                 stencilMode) == 0)
            {
                throw new NotSupportedException(
                    $"Vulkan stencil resolve mode {stencilMode} is " +
                    "not supported by the selected device.");
            }
            if (depthMode == stencilMode)
            {
                return;
            }
            bool oneIsNone =
                depthMode == VkResolveModeFlags.None ||
                stencilMode == VkResolveModeFlags.None;
            if (oneIsNone && !device.IndependentResolveNone)
            {
                throw new NotSupportedException(
                    "This Vulkan device cannot independently " +
                    "disable depth or stencil resolve.");
            }
            if (!oneIsNone && !device.IndependentResolve)
            {
                throw new NotSupportedException(
                    "This Vulkan device requires identical depth " +
                    "and stencil resolve modes.");
            }
        }

        private static void GetDepthStencilRenderingLayouts(
            VulkanDevice device,
            VulkanRasterPassLowering lowering,
            in RHIDepthStencilAttachmentDescriptor descriptor,
            bool hasDepth,
            bool hasStencil,
            out VkImageLayout depthLayout,
            out VkImageLayout stencilLayout)
        {
            ERHISubPassFlags flags =
                lowering.SubPasses.Span[0].DepthStencilFlags;
            bool depthReadOnly =
                hasDepth &&
                descriptor.DepthLoadOp == ERHILoadAction.Load &&
                (flags & ERHISubPassFlags.ReadOnlyDepth) != 0;
            bool stencilReadOnly =
                hasStencil &&
                descriptor.StencilLoadOp == ERHILoadAction.Load &&
                (flags & ERHISubPassFlags.ReadOnlyStencil) != 0;
            if (hasDepth && hasStencil &&
                depthReadOnly != stencilReadOnly &&
                !device.SupportsSeparateDepthStencilLayouts)
            {
                depthReadOnly = false;
                stencilReadOnly = false;
            }
            if (device.SupportsSeparateDepthStencilLayouts &&
                hasDepth && hasStencil &&
                depthReadOnly != stencilReadOnly)
            {
                depthLayout = depthReadOnly
                    ? VkImageLayout.DepthReadOnlyOptimal
                    : VkImageLayout.DepthAttachmentOptimal;
                stencilLayout = stencilReadOnly
                    ? VkImageLayout.StencilReadOnlyOptimal
                    : VkImageLayout.StencilAttachmentOptimal;
                return;
            }
            VkImageLayout combined =
                depthReadOnly || stencilReadOnly
                    ? VkImageLayout
                        .DepthStencilReadOnlyOptimal
                    : VkImageLayout
                        .DepthStencilAttachmentOptimal;
            depthLayout = combined;
            stencilLayout = combined;
        }
        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) =>
            mode switch
            {
                EResolveMode.None => VkResolveModeFlags.None,
                EResolveMode.Sample0 =>
                    VkResolveModeFlags.SampleZero,
                EResolveMode.Min => VkResolveModeFlags.Min,
                EResolveMode.Max => VkResolveModeFlags.Max,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unknown depth/stencil resolve mode."),            };
    }
}
