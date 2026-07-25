using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanFeatureChainPlanTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Vulkan11_WithDescriptorIndexingExtension_UsesOnlyExtensionFeatureStructs()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 1, 0),
            VulkanUtility.Version(1, 1, 128),
            hasDescriptorIndexingExtension: true,
            hasDynamicRenderingExtension: true,
            hasCreateRenderPass2Extension: true,
            hasSynchronization2Extension: true);

        Assert.Equal(VulkanUtility.Version(1, 1, 0), plan.EffectiveApiVersion);
        Assert.False(plan.UseVulkan12Features);
        Assert.False(plan.UseVulkan13Features);
        Assert.Equal(
            EVulkanDescriptorIndexingPath.ExtDescriptorIndexing,
            plan.DescriptorIndexingPath);
        Assert.True(plan.UseDynamicRenderingExtension);
        Assert.True(plan.UseSynchronization2Extension);
        Assert.True(plan.SupportsRenderPass2);
        Assert.True(plan.UseCreateRenderPass2Extension);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Vulkan11_WithoutDescriptorIndexingExtension_FailsClosed()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 1, 0),
            VulkanUtility.Version(1, 1, 0),
            hasDescriptorIndexingExtension: false,
            hasDynamicRenderingExtension: false,
            hasCreateRenderPass2Extension: false,
            hasSynchronization2Extension: false);

        Assert.False(plan.UseVulkan12Features);
        Assert.False(plan.UseVulkan13Features);
        Assert.Equal(
            EVulkanDescriptorIndexingPath.Unavailable,
            plan.DescriptorIndexingPath);
        Assert.False(plan.UseDynamicRenderingExtension);
        Assert.False(plan.UseSynchronization2Extension);
        Assert.False(plan.SupportsRenderPass2);
        Assert.False(plan.UseCreateRenderPass2Extension);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Vulkan12_UsesCoreDescriptorIndexingWithoutVulkan13Struct()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 2, 0),
            VulkanUtility.Version(1, 3, 0),
            hasDescriptorIndexingExtension: true,
            hasDynamicRenderingExtension: false,
            hasCreateRenderPass2Extension: false,
            hasSynchronization2Extension: false);

        Assert.Equal(VulkanUtility.Version(1, 2, 0), plan.EffectiveApiVersion);
        Assert.True(plan.UseVulkan12Features);
        Assert.False(plan.UseVulkan13Features);
        Assert.Equal(
            EVulkanDescriptorIndexingPath.Vulkan12Core,
            plan.DescriptorIndexingPath);
        Assert.False(plan.UseDynamicRenderingExtension);
        Assert.False(plan.UseSynchronization2Extension);
        Assert.True(plan.SupportsRenderPass2);
        Assert.False(plan.UseCreateRenderPass2Extension);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Vulkan13_UsesOnlyPromotedCoreFeatureStructs()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 3, 0),
            VulkanUtility.Version(1, 4, 0),
            hasDescriptorIndexingExtension: true,
            hasDynamicRenderingExtension: true,
            hasCreateRenderPass2Extension: true,
            hasSynchronization2Extension: true);

        Assert.Equal(VulkanUtility.Version(1, 3, 0), plan.EffectiveApiVersion);
        Assert.True(plan.UseVulkan12Features);
        Assert.True(plan.UseVulkan13Features);
        Assert.Equal(
            EVulkanDescriptorIndexingPath.Vulkan12Core,
            plan.DescriptorIndexingPath);
        Assert.False(plan.UseDynamicRenderingExtension);
        Assert.False(plan.UseSynchronization2Extension);
        Assert.True(plan.SupportsRenderPass2);
        Assert.False(plan.UseCreateRenderPass2Extension);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void UnknownOrInvalidVersions_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanFeatureChainPlan.Create(
                VulkanUtility.Version(2, 0, 0),
                VulkanUtility.Version(2, 0, 0),
                false,
                false,
                false,
                false));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanFeatureChainPlan.Create(
                0,
                VulkanUtility.Version(1, 1, 0),
                false,
                false,
                false,
                false));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanFeatureChainPlan.Create(
                VulkanUtility.Version(1, 1, 0),
                0,
                false,
                false,
                false,
                false));
    }
}
