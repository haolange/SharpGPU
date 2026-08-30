using System;
using SharpGPU;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterSubpassQualifiedTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FeatureChain_AcceptsVulkan14AndUsesKnownPromotedCoreStructs()
    {
        VulkanFeatureChainPlan plan = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 4, 0),
            VulkanUtility.Version(1, 4, 341),
            hasDescriptorIndexingExtension: false,
            hasDynamicRenderingExtension: false,
            hasCreateRenderPass2Extension: false,
            hasSynchronization2Extension: false,
            hasDynamicRenderingLocalReadExtension: true);

        Assert.Equal(
            VulkanUtility.Version(1, 4, 0),
            plan.EffectiveApiVersion);
        Assert.True(plan.UseVulkan12Features);
        Assert.True(plan.UseVulkan13Features);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan13Core,
            plan.DynamicRenderingProvenance);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan12Core,
            plan.RenderPass2Provenance);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan14Core,
            plan.DynamicRenderingLocalReadQueryProvenance);
        Assert.True(plan.UseVulkan14Features);
        Assert.True(plan.EnableDynamicRenderingLocalReadExtension);
        Assert.False(
            plan.UseDynamicRenderingLocalReadExtensionFeatureStruct);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FeatureChain_RenderPass2ProvenanceDistinguishesKhrAndCore()
    {
        VulkanFeatureChainPlan khr = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 1, 0),
            VulkanUtility.Version(1, 1, 130),
            false,
            false,
            true,
            false);
        VulkanFeatureChainPlan core = VulkanFeatureChainPlan.Create(
            VulkanUtility.Version(1, 2, 0),
            VulkanUtility.Version(1, 2, 198),
            false,
            false,
            false,
            false);

        Assert.Equal(
            EVulkanFeatureProvenance.KhrExtension,
            khr.RenderPass2Provenance);
        Assert.True(khr.UseCreateRenderPass2Extension);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan12Core,
            core.RenderPass2Provenance);
        Assert.False(core.UseCreateRenderPass2Extension);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FeatureChain_UnknownMajorFailsClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanFeatureChainPlan.Create(
                VulkanUtility.Version(2, 0, 0),
                VulkanUtility.Version(2, 0, 0),
                false,
                false,
                false,
                false));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void PrivateSetAlgorithm_MatchesCanonicalSparseAndRepeatedFixtures()
    {
        Assert.Equal(
            0u,
            VulkanPrivateRasterBindingPlan
                .GetPrivateAttachmentDescriptorSet(
                    ReadOnlySpan<uint>.Empty));
        Assert.Equal(
            1u,
            VulkanPrivateRasterBindingPlan
                .GetPrivateAttachmentDescriptorSet(
                    new uint[] { 0, 0 }));
        Assert.Equal(
            4u,
            VulkanPrivateRasterBindingPlan
                .GetPrivateAttachmentDescriptorSet(
                    new uint[] { 0, 2, 2, 3 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanPrivateRasterBindingPlan
                .GetPrivateAttachmentDescriptorSet(
                    new uint[] { uint.MaxValue }));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void PrivateBindings_PreserveInputHolesAndIncludeReadWriteInputs()
    {
        RHIAttachmentInterfaceSignature signature = CreateSignature(
            colorAttachmentCount: 6,
            inputs: new[]
            {
                RHIAttachmentInterfaceSignature.UnboundLogicalAttachment,
                4,
                RHIAttachmentInterfaceSignature.UnboundLogicalAttachment,
                2,
            },
            outputs: new[]
            {
                RHIAttachmentInterfaceSignature.UnboundLogicalAttachment,
                4,
            });

        VulkanPrivateRasterBindingPlan plan =
            VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 0, 2 },
                maximumBoundDescriptorSets: 4,
                in signature);

        Assert.Equal(3u, plan.DescriptorSet);
        Assert.Equal((1 << 2) | (1 << 4), plan.LocalInputMask);
        Assert.Equal((1 << 1) | (1 << 3), plan.LocalInputBindingMask);
        Assert.Equal(2u, plan.InputAttachmentCount);
        Assert.False(plan.UsesInputAttachmentBinding(0));
        Assert.True(plan.UsesInputAttachmentBinding(1));
        Assert.False(plan.UsesInputAttachmentBinding(2));
        Assert.True(plan.UsesInputAttachmentBinding(3));
        Assert.Equal(1u, plan.GetInputAttachmentBinding(1));
        Assert.Equal(3u, plan.GetInputAttachmentBinding(3));
        Assert.Equal(2u, plan.PoolRequirements.InputAttachments);
        Assert.Equal(0u, plan.PoolRequirements.StorageImages);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void OutputOnly_UsesNoPrivateDescriptor()
    {
        RHIAttachmentInterfaceSignature signature = CreateSignature(
            colorAttachmentCount: 3,
            inputs: Array.Empty<int>(),
            outputs: new[] { 0 });

        VulkanPrivateRasterBindingPlan plan =
            VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 0, 3 },
                maximumBoundDescriptorSets: 4,
                in signature);

        Assert.False(plan.HasPrivateBindings);
        Assert.Equal(0u, plan.InputAttachmentCount);
        Assert.Equal(0u, plan.PoolRequirements.InputAttachments);
        Assert.Equal(0u, plan.PoolRequirements.StorageImages);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void PrivateDescriptorLease_RollsBackUnlessRegistrationCommits()
    {
        VulkanDescriptorSetLease lease = new VulkanDescriptorSetLease(
            new VkDescriptorPool(1),
            new VkDescriptorSet(2));
        int rollbackCount = 0;
        int registrationCount = 0;

        using (VulkanDescriptorSetLeaseTransaction transaction =
               new VulkanDescriptorSetLeaseTransaction(
                   in lease,
                   _ => rollbackCount++))
        {
        }
        Assert.Equal(1, rollbackCount);

        using (VulkanDescriptorSetLeaseTransaction transaction =
               new VulkanDescriptorSetLeaseTransaction(
                   in lease,
                   _ => rollbackCount++))
        {
            transaction.Commit(_ => registrationCount++);
        }
        Assert.Equal(1, rollbackCount);
        Assert.Equal(1, registrationCount);

        using (VulkanDescriptorSetLeaseTransaction transaction =
               new VulkanDescriptorSetLeaseTransaction(
                   in lease,
                   _ => rollbackCount++))
        {
            Assert.Throws<InvalidOperationException>(
                () => transaction.Commit(
                    _ => throw new InvalidOperationException(
                        "registration failure")));
        }
        Assert.Equal(2, rollbackCount);
    }

    private static RHIAttachmentInterfaceSignature CreateSignature(
        int colorAttachmentCount,
        int[] inputs,
        int[] outputs)
    {
        RHIAttachmentIndexArray inputSlots =
            CreateSlots(inputs);
        RHIAttachmentIndexArray outputSlots =
            CreateSlots(outputs);
        return new RHIAttachmentInterfaceSignature(
            colorAttachmentCount,
            inputSlots,
            outputSlots);
    }

    private static RHIAttachmentIndexArray CreateSlots(
        ReadOnlySpan<int> values)
    {
        RHIAttachmentIndexArray slots =
            new RHIAttachmentIndexArray(values.Length);
        for (int index = 0; index < values.Length; ++index)
        {
            slots[index] = values[index];
        }
        return slots;
    }
}
