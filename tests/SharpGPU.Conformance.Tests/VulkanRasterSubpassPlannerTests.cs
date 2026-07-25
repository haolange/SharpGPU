using System;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterSubpassPlannerTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void AutoPrefersExactDynamicLocalReadAndFallsBackToRenderPass2()
    {
        using TestTexture texture = CreateColorTexture();
        RasterPassPlan plan = Compile(
            new[] { CreateAttachment(texture) },
            CreateSubPass(outputs: new[] { 0 }),
            CreateSubPass(inputs: new[] { 0 }, outputs: new[] { 0 }));
        VulkanRasterCapabilities both = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            attachmentFeedbackLoopLayout: true,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);
        VulkanRasterCapabilities renderPassOnly = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: false,
            dynamicRenderingLocalReadDepthStencil: false,
            dynamicRenderingLocalReadMultisampled: false,
            renderPass2: true,
            attachmentFeedbackLoopLayout: true,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);

        Assert.Equal(
            EVulkanRasterPassStrategy.DynamicRenderingLocalRead,
            VulkanRasterPassLowering.Compile(plan, in both).Strategy);
        Assert.Equal(
            EVulkanRasterPassStrategy.NativeRenderPass2,
            VulkanRasterPassLowering.Compile(plan, in renderPassOnly).Strategy);
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in renderPassOnly,
                EVulkanRasterPassForcedStrategy.DynamicRenderingLocalRead));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void RenderPass2DescriptionPreservesSparseInputOutputMappings()
    {
        using TestTexture first = CreateColorTexture();
        using TestTexture second = CreateColorTexture();
        using TestTexture third = CreateColorTexture();
        RasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(first),
                CreateAttachment(second),
                CreateAttachment(third),
            },
            CreateSubPass(outputs: new[] { 0, 2 }),
            CreateSubPass(outputs: new[] { 1 }),
            CreateSubPass(
                inputs: new[] { -1, 0 },
                outputs: new[] { 2, -1, 1 }));
        VulkanRasterCapabilities renderPassOnly = new(
            dynamicRendering: false,
            dynamicRenderingLocalRead: false,
            dynamicRenderingLocalReadDepthStencil: false,
            dynamicRenderingLocalReadMultisampled: false,
            renderPass2: true,
            attachmentFeedbackLoopLayout: false,
            orderedFragmentPixelInterlock: false,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: false);
        VulkanRasterPassLowering lowering =
            VulkanRasterPassLowering.Compile(plan, in renderPassOnly);

        VulkanRenderPass2Description description =
            VulkanRenderPass2Description.Compile(plan, lowering);
        VulkanRenderPass2SubPassDescription middle =
            description.SubPasses.Span[1];
        VulkanRenderPass2SubPassDescription last =
            description.SubPasses.Span[2];

        Assert.Equal(
            new[] { 0, 2 },
            middle.PreserveAttachments.ToArray());
        Assert.Equal(
            new[] { -1, 0 },
            last.InputAttachmentsByIndex.ToArray());
        Assert.Equal(
            new[] { 2, -1, 1 },
            last.ColorAttachmentsByLocation.ToArray());
        Assert.Equal(
            new[] { -1, -1, -1 },
            last.ResolveAttachmentsByLocation.ToArray());
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void SampledFeedbackIsOrdinaryBindingAndRequiresExactCapability()
    {
        using TestTexture output = CreateColorTexture();
        using TestTexture sampled = CreateColorTexture();
        RasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(output),
                CreateAttachment(sampled),
            },
            CreateSubPass(
                outputs: new[] { 0 },
                sampled: new[] { -1, 1 }));
        VulkanRasterCapabilities supported = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            attachmentFeedbackLoopLayout: true,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);
        VulkanRasterCapabilities unsupported = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            attachmentFeedbackLoopLayout: false,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);

        VulkanRasterCapabilities noLoweringRoute = new(
            dynamicRendering: false,
            dynamicRenderingLocalRead: false,
            dynamicRenderingLocalReadDepthStencil: false,
            dynamicRenderingLocalReadMultisampled: false,
            renderPass2: false,
            attachmentFeedbackLoopLayout: true,
            orderedFragmentPixelInterlock: true,
            fragmentStoresAndAtomics: true,
            unifiedImageLayouts: true);

        VulkanRasterPassLowering lowering =
            VulkanRasterPassLowering.Compile(plan, in supported);

        Assert.False(lowering.RequiresPrivateAttachmentTable);
        Assert.True(lowering.UsesAttachmentFeedbackLoopLayout);
        Assert.Equal(
            1,
            lowering.SubPasses.Span[0]
                .GetSampledFeedbackLogicalAttachment(1));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(plan, in unsupported));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in noLoweringRoute));
    }

    private static RasterPassPlan Compile(
        RHIColorAttachmentDescriptor[] attachments,
        params RHISubPassDescriptor[] subPasses)
    {
        RHIRasterPassDescriptor descriptor = new()
        {
            ColorAttachments = attachments,
            SubPassDescriptors = subPasses,
        };
        return RasterPassPlanner.Compile(in descriptor);
    }

    private static RHIColorAttachmentDescriptor CreateAttachment(
        RHITexture texture) =>
        new()
        {
            RenderTarget = texture,
            LoadAction = ERHILoadAction.Load,
            StoreAction = ERHIStoreAction.Store,
        };

    private static RHISubPassDescriptor CreateSubPass(
        int[]? inputs = null,
        int[]? outputs = null,
        int[]? sampled = null) =>
        new()
        {
            ColorInputs = inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
            SampledFeedbackInputs = sampled == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(sampled),
        };

    private static TestTexture CreateColorTexture() =>
        new(
            new RHITextureDescriptor
            {
                Extent = new uint3(16, 16, 1),
                MipCount = 1,
                Dimension = ERHITextureDimension.Texture2D,
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.ShaderResource,
            });

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
