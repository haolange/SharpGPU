using System;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterSubpassPlannerTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void AutoPrefersExactDynamicLocalReadAndFallsBackToRenderPass2()
    {
        using TestTexture texture = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[] { CreateAttachment(texture) },
            CreateSubPass(outputs: new[] { 0 }),
            CreateSubPass(inputs: new[] { 0 }, outputs: new[] { 0 }));
        VulkanRasterCapabilities both = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            rasterizationOrderAttachmentAccess: true);
        VulkanRasterCapabilities renderPassOnly = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: false,
            dynamicRenderingLocalReadDepthStencil: false,
            dynamicRenderingLocalReadMultisampled: false,
            renderPass2: true,
            rasterizationOrderAttachmentAccess: true);

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
        RHIRasterPassPlan plan = Compile(
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
            rasterizationOrderAttachmentAccess: false);
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
    public void FramebufferReadWriteRequiresRoaaAndNeverUsesFeedbackLoopLayout()
    {
        using TestTexture readWrite = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[] { CreateAttachment(readWrite) },
            CreateSubPass(
                inputs: new[] { 0 },
                outputs: new[] { 0 }));
        VulkanRasterCapabilities supported = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            rasterizationOrderAttachmentAccess: true);
        VulkanRasterCapabilities unsupported = new(
            dynamicRendering: true,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            rasterizationOrderAttachmentAccess: false);

        VulkanRasterCapabilities noLoweringRoute = new(
            dynamicRendering: false,
            dynamicRenderingLocalRead: false,
            dynamicRenderingLocalReadDepthStencil: false,
            dynamicRenderingLocalReadMultisampled: false,
            renderPass2: false,
            rasterizationOrderAttachmentAccess: true);

        VulkanRasterPassLowering lowering =
            VulkanRasterPassLowering.Compile(plan, in supported);

        Assert.True(lowering.RequiresPrivateAttachmentTable);
        Assert.Equal((byte)1, lowering.SubPasses.Span[0].RasterOrderedMask);
        Assert.Equal(
            EVulkanRasterAttachmentScopeLayout.RenderingLocalRead,
            lowering.AttachmentScopeLayouts.Span[0]);
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(plan, in unsupported));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in noLoweringRoute));
    }

    private static RHIRasterPassPlan Compile(
        RHIColorAttachmentDescriptor[] attachments,
        params RHISubPassDescriptor[] subPasses)
    {
        RHIRasterPassDescriptor descriptor = new()
        {
            ColorAttachments = attachments,
            SubPassDescriptors = subPasses,
        };
        return RHIRasterPassPlanner.Compile(in descriptor);
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
        int[]? outputs = null) =>
        new()
        {
            ColorInputs = inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
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
