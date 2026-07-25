using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class PrivateRasterBindingAbiTests
{
    [Fact]
    public void MetalPrivateTextureBase_FollowsCompleteOrdinaryTextureRange()
    {
        using MetalArgumentTableLayout direct =
            CreateMetalLayout(
                Element(
                    slot: 0,
                    count: 1,
                    ERHIBindType.UniformBuffer),
                Element(
                    slot: 0,
                    count: 1,
                    ERHIBindType.Texture2D),
                Element(
                    slot: 1,
                    count: 1,
                    ERHIBindType.StorageTexture2D));

        Assert.Equal(
            2u,
            MetalPrivateRasterBindingPlan
                .GetOrdinaryTextureRangeEnd(
                    new[] { direct }));
    }

    [Fact]
    public void MetalPrivateTextureBase_MatchesReferenceArrayFlattening()
    {
        using MetalArgumentTableLayout reference =
            CreateMetalLayout(
                Element(
                    slot: 0,
                    count: 4,
                    ERHIBindType.StorageBuffer),
                Element(
                    slot: 4,
                    count: 3,
                    ERHIBindType.Texture2DArray),
                Element(
                    slot: 7,
                    count: 1,
                    ERHIBindType.Sampler));

        Assert.True(reference.RequiresReferenceBuffer);
        Assert.Equal(
            7u,
            MetalPrivateRasterBindingPlan
                .GetOrdinaryTextureRangeEnd(
                    new[] { reference }));
    }

    [Fact]
    public void MetalPrivateTexturePlan_RejectsOverflowAndLimitCollision()
    {
        Assert.Throws<OverflowException>(
            () => CreateMetalLayout(
                Element(
                    slot: uint.MaxValue,
                    count: 1,
                    ERHIBindType.Texture2D)));

        using MetalArgumentTableLayout nearLimit =
            CreateMetalLayout(
                Element(
                    slot: 125,
                    count: 1,
                    ERHIBindType.Texture2D));
        RHIAttachmentInterfaceSignature ordered =
            CreateSignature(
                colorAttachmentCount: 3,
                inputs: new[] { 2 },
                outputs: new[] { 2 },
                rasterOrderedMask: 1 << 2);
        Assert.Throws<NotSupportedException>(
            () => MetalPrivateRasterBindingPlan.Compile(
                new[] { nearLimit },
                maximumTextureBindings: 128,
                in ordered));
    }

    [Fact]
    public void MetalPrivateTexturePlan_RejectsDisposedAndCrossDeviceState()
    {
        object device = new object();
        object otherDevice = new object();

        Assert.Throws<ObjectDisposedException>(
            () => MetalPrivateRasterBindingPlan
                .ValidatePipelineLayoutIdentity(
                    isDisposed: true,
                    actualDevice: device,
                    expectedDevice: device));
        Assert.Throws<ArgumentException>(
            () => MetalPrivateRasterBindingPlan
                .ValidatePipelineLayoutIdentity(
                    isDisposed: false,
                    actualDevice: otherDevice,
                    expectedDevice: device));

        MetalArgumentTableLayout disposed =
            CreateMetalLayout(
                Element(
                    slot: 0,
                    count: 1,
                    ERHIBindType.Texture2D));
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => MetalPrivateRasterBindingPlan
                .GetOrdinaryTextureRangeEnd(
                    new[] { disposed }));
    }

    [Fact]
    public void VulkanPrivateSetAndBindings_MatchSharpShaderAbi()
    {
        RHIAttachmentInterfaceSignature signature =
            CreateSignature(
                colorAttachmentCount: 6,
                inputs: new[] { 1, 5 },
                outputs: new[] { 5 },
                rasterOrderedMask: 1 << 5);

        VulkanPrivateRasterBindingPlan plan =
            VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 0, 2 },
                maximumBoundDescriptorSets: 4,
                in signature);

        Assert.Equal(3u, plan.DescriptorSet);
        Assert.Equal(0u, plan.GetInputAttachmentBinding(0));
        Assert.Equal(13u, plan.GetRasterOrderedBinding(5));
        Assert.True(plan.HasPrivateBindings);
    }

    [Fact]
    public void VulkanPrivateSet_RejectsLimitCollisionDuplicateAndOutOfRangeSets()
    {
        RHIAttachmentInterfaceSignature signature =
            CreateSignature(
                colorAttachmentCount: 2,
                inputs: new[] { 1 },
                outputs: Array.Empty<int>(),
                rasterOrderedMask: 0);

        Assert.Throws<NotSupportedException>(
            () => VulkanPrivateRasterBindingPlan.Compile(
                new uint[] { 0, 3 },
                maximumBoundDescriptorSets: 4,
                in signature));
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
    public void VulkanPrivateSet_RejectsDisposedAndCrossDeviceState()
    {
        object device = new object();
        object otherDevice = new object();

        Assert.Throws<ObjectDisposedException>(
            () => VulkanPrivateRasterBindingPlan
                .ValidatePipelineLayoutIdentity(
                    isDisposed: true,
                    actualDevice: device,
                    expectedDevice: device));
        Assert.Throws<ArgumentException>(
            () => VulkanPrivateRasterBindingPlan
                .ValidatePipelineLayoutIdentity(
                    isDisposed: false,
                    actualDevice: otherDevice,
                    expectedDevice: device));
    }

    private static MetalArgumentTableLayout CreateMetalLayout(
        params RHIArgumentTableLayoutElement[] elements)
    {
        return new MetalArgumentTableLayout(
            new RHIArgumentTableLayoutDescriptor
            {
                Index = 0,
                Elements = elements,
            });
    }

    private static RHIArgumentTableLayoutElement Element(
        uint slot,
        uint count,
        ERHIBindType type)
    {
        return new RHIArgumentTableLayoutElement
        {
            Slot = slot,
            Count = count,
            Type = type,
            Stages = ERHIShaderStageMask.Fragment,
            Requirement =
                ERHIArgumentBindingRequirement.Required,
        };
    }

    private static RHIAttachmentInterfaceSignature CreateSignature(
        int colorAttachmentCount,
        int[] inputs,
        int[] outputs,
        int rasterOrderedMask)
    {
        RHIAttachmentIndexArray inputSlots =
            new RHIAttachmentIndexArray(inputs.Length);
        for (int index = 0; index < inputs.Length; ++index)
        {
            inputSlots[index] = inputs[index];
        }

        RHIAttachmentIndexArray outputSlots =
            new RHIAttachmentIndexArray(outputs.Length);
        for (int index = 0; index < outputs.Length; ++index)
        {
            outputSlots[index] = outputs[index];
        }

        return new RHIAttachmentInterfaceSignature(
            colorAttachmentCount,
            inputSlots,
            outputSlots,
            RHIAttachmentIndexArray.Empty,
            checked((byte)rasterOrderedMask));
    }
}
