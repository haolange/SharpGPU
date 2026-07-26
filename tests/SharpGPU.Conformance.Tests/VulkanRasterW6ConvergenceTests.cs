using System;
using SharpGPU;
using SharpGPU.Mathematics;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterW6ConvergenceTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Vulkan14LocalRead_UsesOneCoreFeatureStructAndMayEnableKhrExtension()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 4, 0),
            VulkanUtility.Version(1, 4, 341),
            hasDescriptorIndexingExtension: false,
            hasDynamicRenderingExtension: false,
            hasCreateRenderPass2Extension: false,
            hasSynchronization2Extension: false,
            hasDynamicRenderingLocalReadExtension: true);

        Assert.True(plan.UseVulkan14Features);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan14Core,
            plan.DynamicRenderingLocalReadQueryProvenance);
        Assert.True(plan.EnableDynamicRenderingLocalReadExtension);
        Assert.False(
            plan.UseDynamicRenderingLocalReadExtensionFeatureStruct);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void SampledFeedbackRangeClassification_IsExactAndFailClosed()
    {
        VkImage image = new(17);
        RHITextureSubresourceRange exact = Range(
            ERHITextureAspectMask.Color,
            mip: 1,
            mipCount: 2,
            layer: 3,
            layerCount: 2);
        RHITextureSubresourceRange partialMip = Range(
            ERHITextureAspectMask.Color,
            mip: 2,
            mipCount: 1,
            layer: 3,
            layerCount: 2);
        RHITextureSubresourceRange disjointLayer = Range(
            ERHITextureAspectMask.Color,
            mip: 1,
            mipCount: 2,
            layer: 8,
            layerCount: 1);

        Assert.Equal(
            EVulkanSampledFeedbackRangeRelation.Exact,
            VulkanSampledFeedbackRangeUtility.Classify(
                image,
                in exact,
                image,
                in exact));
        Assert.Equal(
            EVulkanSampledFeedbackRangeRelation.PartialOverlap,
            VulkanSampledFeedbackRangeUtility.Classify(
                image,
                in partialMip,
                image,
                in exact));
        Assert.Equal(
            EVulkanSampledFeedbackRangeRelation.Disjoint,
            VulkanSampledFeedbackRangeUtility.Classify(
                image,
                in disjointLayer,
                image,
                in exact));
        Assert.Equal(
            EVulkanSampledFeedbackRangeRelation.DifferentImage,
            VulkanSampledFeedbackRangeUtility.Classify(
                new VkImage(18),
                in exact,
                image,
                in exact));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void CombinedDepthStencilBarrier_ExpandsOnlyWhenNativeLayoutRequiresIt()
    {
        Assert.Equal(
            ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil,
            VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                supportsSeparateDepthStencilLayouts: false,
                ERHIPixelFormat.D24_UNorm_S8_UInt,
                ERHITextureAspectMask.Depth));
        Assert.Equal(
            ERHITextureAspectMask.Depth,
            VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                supportsSeparateDepthStencilLayouts: true,
                ERHIPixelFormat.D24_UNorm_S8_UInt,
                ERHITextureAspectMask.Depth));
        Assert.Equal(
            ERHITextureAspectMask.Depth,
            VulkanDepthStencilBarrierUtility.GetNativeAspectMask(
                supportsSeparateDepthStencilLayouts: false,
                ERHIPixelFormat.D32_Float,
                ERHITextureAspectMask.Depth));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void SampledFeedbackPipeline_UsesNamedColorFeedbackFlagOnly()
    {
        RHIAttachmentInterfaceSignature ordinary =
            CreateSignature(outputs: new[] { 0 });
        RHIAttachmentInterfaceSignature feedback =
            CreateSignature(
                outputs: new[] { 0 },
                sampledFeedback: new[] { 0 });

        Assert.Equal(
            (VkPipelineCreateFlags)0,
            VulkanRasterPipelineFlagUtility.Get(in ordinary));
        Assert.Equal(
            VkPipelineCreateFlags.ColorAttachmentFeedbackLoopEXT,
            VulkanRasterPipelineFlagUtility.Get(in feedback));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Sync1Stages_IncludeAccessRequiredRasterAndTransferStages()
    {
        VulkanBarrierEmitter.Sync1BucketPlan[] plans =
            VulkanBarrierEmitter.PlanSync1BucketsForTesting(
                ERHIPipelineType.Graphics,
                new[]
                {
                    RHIBarrier.Global(
                        ERHIStageMask.None,
                        ERHIStageMask.None,
                        ERHIAccessMask.RenderTargetWrite |
                            ERHIAccessMask.DepthStencilWrite,
                        ERHIAccessMask.TransferRead),
                });

        VulkanBarrierEmitter.Sync1BucketPlan plan =
            Assert.Single(plans);
        Assert.True(
            plan.SrcStages.HasFlag(
                VkPipelineStageFlags.ColorAttachmentOutput));
        Assert.True(
            plan.SrcStages.HasFlag(
                VkPipelineStageFlags.EarlyFragmentTests));
        Assert.True(
            plan.SrcStages.HasFlag(
                VkPipelineStageFlags.LateFragmentTests));
        Assert.True(
            plan.DstStages.HasFlag(
                VkPipelineStageFlags.Transfer));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void PrivateDescriptorSetCompile_RejectsDuplicateAndOutOfRangeSets()
    {
        RHIAttachmentInterfaceSignature signature =
            CreateSignature(
                inputs: new[] { 0 },
                outputs: new[] { 0 });

        Assert.Throws<ArgumentException>(
            () => VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 0, 0 },
                maximumBoundDescriptorSets: 4,
                in signature));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 4 },
                maximumBoundDescriptorSets: 4,
                in signature));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FirstReadOnlyDepthSubpass_RejectsClearBeforeNativeLowering()
    {
        using TestTexture depth = new(
            new RHITextureDescriptor
            {
                Extent = new uint3(4, 4, 1),
                MipCount = 1,
                Dimension = ERHITextureDimension.Texture2D,
                Format = ERHIPixelFormat.D24_UNorm_S8_UInt,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHITextureUsage.DepthStencil,
            });
        RHIRasterPassDescriptor descriptor = new()
        {
            DepthStencilAttachment =
                new RHIDepthStencilAttachmentDescriptor
                {
                    RenderTarget = depth,
                    SubresourceRange =
                        RHITextureSubresourceRange.Whole(
                            ERHITextureAspectMask.Depth |
                            ERHITextureAspectMask.Stencil),
                    DepthLoadOp = ERHILoadAction.Clear,
                    DepthStoreOp = ERHIStoreAction.Store,
                    StencilLoadOp = ERHILoadAction.Load,
                    StencilStoreOp = ERHIStoreAction.Store,
                },
            SubPassDescriptors = new[]
            {
                new RHISubPassDescriptor
                {
                    Flags = ERHISubPassFlags.ReadOnlyDepth,
                },
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        VulkanRasterCapabilities capabilities = AllRasterCapabilities();

        Assert.Throws<ArgumentException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in capabilities));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void RenderPass2Description_DeclaresFeedbackOnlyInOwningPhase()
    {
        using TestTexture color = new(
            new RHITextureDescriptor
            {
                Extent = new uint3(4, 4, 1),
                MipCount = 1,
                Dimension = ERHITextureDimension.Texture2D,
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag =
                    ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.ShaderResource,
            });
        RHIRasterPassDescriptor descriptor = new()
        {
            ColorAttachments = new[]
            {
                new RHIColorAttachmentDescriptor
                {
                    RenderTarget = color,
                    LoadAction = ERHILoadAction.Load,
                    StoreAction = ERHIStoreAction.Store,
                },
            },
            SubPassDescriptors = new[]
            {
                new RHISubPassDescriptor
                {
                    ColorOutputs =
                        new RHIAttachmentIndexArray(new[] { 0 }),
                },
                new RHISubPassDescriptor
                {
                    ColorOutputs =
                        new RHIAttachmentIndexArray(new[] { 0 }),
                    SampledFeedbackInputs =
                        new RHIAttachmentIndexArray(new[] { 0 }),
                },
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        VulkanRasterCapabilities capabilities =
            new(
                dynamicRendering: false,
                dynamicRenderingLocalRead: false,
                dynamicRenderingLocalReadDepthStencil: false,
                dynamicRenderingLocalReadMultisampled: false,
                renderPass2: true,
                attachmentFeedbackLoopLayout: true,
                orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);
        VulkanRasterPassLowering lowering =
            VulkanRasterPassLowering.Compile(plan, in capabilities);
        VulkanRenderPass2Description description =
            VulkanRenderPass2Description.Compile(plan, lowering);

        Assert.Empty(
            description.SubPasses.Span[0]
                .SampledFeedbackAttachments.ToArray());
        Assert.Equal(
            new[] { 0 },
            description.SubPasses.Span[1]
                .SampledFeedbackAttachments.ToArray());
    }

    private static VulkanRasterCapabilities AllRasterCapabilities() =>
        new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            attachmentFeedbackLoopLayout: true,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);

    private static RHITextureSubresourceRange Range(
        ERHITextureAspectMask aspect,
        uint mip,
        uint mipCount,
        uint layer,
        uint layerCount) =>
        new()
        {
            AspectMask = aspect,
            BaseMipLevel = mip,
            MipLevelCount = mipCount,
            BaseArrayLayer = layer,
            ArrayLayerCount = layerCount,
        };

    private static RHIAttachmentInterfaceSignature CreateSignature(
        int[]? inputs = null,
        int[]? outputs = null,
        int[]? sampledFeedback = null) =>
        new(
            colorAttachmentCount: 1,
            inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
            sampledFeedback == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(sampledFeedback),
            rasterOrderedReadWriteMask: 0);

    private sealed class TestTexture : RHITexture
    {
        internal TestTexture(in RHITextureDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override RHITextureView CreateTextureView(
            in RHITextureViewDescriptor descriptor) =>
            throw new NotSupportedException();

        protected override void Release()
        {
        }
    }
}
