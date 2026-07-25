using System;
using System.Reflection;
using SharpGPU;
using SharpGPU.Core;
using SharpGPU.Mathematics;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterExactFeedbackTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void UnifiedImageLayouts_IsAnExplicitKhrFeatureFact()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 3, 0),
            VulkanUtility.Version(1, 3, 0),
            hasDescriptorIndexingExtension: false,
            hasDynamicRenderingExtension: false,
            hasCreateRenderPass2Extension: false,
            hasSynchronization2Extension: false,
            hasUnifiedImageLayoutsExtension: true);

        Assert.Equal(
            EVulkanFeatureProvenance.KhrExtension,
            plan.UnifiedImageLayoutsQueryProvenance);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void OrderedFeedbackLoop_ChainsOnlyTheColorAttachmentInfo()
    {
        Assert.True(
            VulkanRasterFeedbackLoopUtility.TryCreateAttachmentInfo(
                EVulkanRasterAttachmentScopeLayout.GeneralStorage,
                out VkAttachmentFeedbackLoopInfoEXT ordered));
        Assert.Equal(
            VkStructureType.AttachmentFeedbackLoopInfoEXT,
            ordered.sType);
        Assert.True(ordered.feedbackLoopEnable);

        Assert.False(
            VulkanRasterFeedbackLoopUtility.TryCreateAttachmentInfo(
                EVulkanRasterAttachmentScopeLayout.ColorAttachment,
                out VkAttachmentFeedbackLoopInfoEXT ordinary));
        Assert.Equal(default, ordinary);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void RasterOrderedPipeline_UsesNamedColorFeedbackFlag()
    {
        RHIAttachmentInterfaceSignature signature = new(
            colorAttachmentCount: 1,
            new RHIAttachmentIndexArray(new[] { 0 }),
            new RHIAttachmentIndexArray(new[] { 0 }),
            RHIAttachmentIndexArray.Empty,
            rasterOrderedReadWriteMask: 1);

        Assert.Equal(
            VkPipelineCreateFlags.ColorAttachmentFeedbackLoopEXT,
            VulkanRasterPipelineFlagUtility.Get(in signature));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void RasterOrderedLowering_RequiresEveryExactFactAndRejectsRenderPass2()
    {
        using TestTexture texture = new(
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
                    ERHITextureUsage.RasterizerOrdered,
            });
        RHIRasterPassDescriptor descriptor = new()
        {
            ColorAttachments = new[]
            {
                new RHIColorAttachmentDescriptor
                {
                    RenderTarget = texture,
                    LoadAction = ERHILoadAction.Load,
                    StoreAction = ERHIStoreAction.Store,
                    Access =
                        ERHIRasterAttachmentAccess
                            .RasterOrderedReadWrite,
                },
            },
            SubPassDescriptors = new[]
            {
                new RHISubPassDescriptor
                {
                    ColorInputs =
                        new RHIAttachmentIndexArray(new[] { 0 }),
                    ColorOutputs =
                        new RHIAttachmentIndexArray(new[] { 0 }),
                },
            },
        };
        RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
        VulkanRasterCapabilities exact = Capabilities(
            feedback: true,
            interlock: true,
            unified: true);
        VulkanRasterCapabilities noUnified = Capabilities(
            feedback: true,
            interlock: true,
            unified: false);
        VulkanRasterCapabilities noFeedback = Capabilities(
            feedback: false,
            interlock: true,
            unified: true);
        VulkanRasterCapabilities noInterlock = Capabilities(
            feedback: true,
            interlock: false,
            unified: true);
        VulkanRasterCapabilities noDynamicRendering = Capabilities(
            feedback: true,
            interlock: true,
            unified: true,
            dynamicRendering: false);
        VulkanRasterCapabilities noFragmentStoresAndAtomics = Capabilities(
            feedback: true,
            interlock: true,
            unified: true,
            fragmentStoresAndAtomics: false);

        VulkanRasterPassLowering lowering =
            VulkanRasterPassLowering.Compile(plan, in exact);

        Assert.Equal(
            EVulkanRasterPassStrategy.DynamicRendering,
            lowering.Strategy);
        Assert.Equal(
            EVulkanRasterAttachmentScopeLayout.GeneralStorage,
            lowering.AttachmentScopeLayouts.Span[0]);
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in exact,
                EVulkanRasterPassForcedStrategy.NativeRenderPass2));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(plan, in noUnified));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(plan, in noFeedback));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(plan, in noInterlock));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in noDynamicRendering));
        Assert.Throws<NotSupportedException>(
            () => VulkanRasterPassLowering.Compile(
                plan,
                in noFragmentStoresAndAtomics));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void SampledFeedbackRangeClassifier_DistinguishesExactPartialAndDisjoint()
    {
        VkImage image = default;
        RHITextureSubresourceRange exact = new()
        {
            AspectMask = ERHITextureAspectMask.Color,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
        };
        RHITextureSubresourceRange partial = new()
        {
            AspectMask = ERHITextureAspectMask.Color,
            BaseMipLevel = 0,
            MipLevelCount = 2,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
        };
        RHITextureSubresourceRange disjoint = new()
        {
            AspectMask = ERHITextureAspectMask.Color,
            BaseMipLevel = 1,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
        };

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
                in exact,
                image,
                in partial));
        Assert.Equal(
            EVulkanSampledFeedbackRangeRelation.Disjoint,
            VulkanSampledFeedbackRangeUtility.Classify(
                image,
                in exact,
                image,
                in disjoint));
    }
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void CapabilityRoutes_FailClosedWithoutARealizableNativePath()
    {
        Assert.False(
            VulkanRasterCapabilityUtility
                .HasSampledFeedbackLoweringRoute(
                    dynamicRendering: false,
                    renderPass2: false));
        Assert.True(
            VulkanRasterCapabilityUtility
                .HasSampledFeedbackLoweringRoute(
                    dynamicRendering: true,
                    renderPass2: false));
        Assert.True(
            VulkanRasterCapabilityUtility
                .HasSampledFeedbackLoweringRoute(
                    dynamicRendering: false,
                    renderPass2: true));

        Assert.False(
            VulkanRasterCapabilityUtility
                .HasFramebufferLocalReadLoweringRoute(
                    dynamicRendering: false,
                    dynamicRenderingLocalRead: true,
                    renderPass2: false));
        Assert.False(
            VulkanRasterCapabilityUtility
                .HasFramebufferLocalReadLoweringRoute(
                    dynamicRendering: true,
                    dynamicRenderingLocalRead: false,
                    renderPass2: false));
        Assert.True(
            VulkanRasterCapabilityUtility
                .HasFramebufferLocalReadLoweringRoute(
                    dynamicRendering: true,
                    dynamicRenderingLocalRead: true,
                    renderPass2: false));
        Assert.True(
            VulkanRasterCapabilityUtility
                .HasFramebufferLocalReadLoweringRoute(
                    dynamicRendering: false,
                    dynamicRenderingLocalRead: false,
                    renderPass2: true));
    }
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void DetachedNativeVariant_RetainsNoPublicRhiWrapper()
    {
        FieldInfo[] fields = typeof(VulkanRasterNativeVariant).GetFields(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic);

        Assert.DoesNotContain(
            fields,
            field => typeof(Disposal).IsAssignableFrom(field.FieldType));
        Assert.DoesNotContain(
            fields,
            field => field.FieldType.Name.StartsWith(
                "RHI",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(VulkanPrivateRasterDescriptorLayout).GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic),
            field => field.FieldType == typeof(VulkanDevice));
    }

    private static VulkanRasterCapabilities Capabilities(
        bool feedback,
        bool interlock,
        bool unified,
        bool dynamicRendering = true,
        bool fragmentStoresAndAtomics = true) =>
        new(
            dynamicRendering: dynamicRendering,
            dynamicRenderingLocalRead: true,
            dynamicRenderingLocalReadDepthStencil: true,
            dynamicRenderingLocalReadMultisampled: true,
            renderPass2: true,
            attachmentFeedbackLoopLayout: feedback,
            orderedFragmentPixelInterlock: interlock,
            fragmentStoresAndAtomics: fragmentStoresAndAtomics,
            unifiedImageLayouts: unified);

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
