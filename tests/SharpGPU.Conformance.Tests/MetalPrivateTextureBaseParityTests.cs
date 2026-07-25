using System;
using System.Collections.Generic;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class MetalPrivateTextureBaseParityTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void DirectScalarLayoutMatchesSharpShaderTextureNamespace()
    {
        ShaderTextureFact[] shaderFacts =
        {
            new(0, 1),
            new(3, 2),
        };
        using MetalArgumentTableLayout layout =
            CreateLayout(
                index: 0,
                Element(31, 1, ERHIBindType.UniformBuffer),
                Element(0, 1, ERHIBindType.Texture2D),
                Element(3, 2, ERHIBindType.StorageTexture2DArray));

        uint shaderTextureBase =
            ComputeSharpShaderTextureRangeEnd(shaderFacts);
        uint halTextureBase =
            MetalPrivateRasterBindingPlan.GetOrdinaryTextureRangeEnd(
                new[] { layout });

        Assert.Equal(5u, shaderTextureBase);
        Assert.Equal(shaderTextureBase, halTextureBase);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void ReferenceArrayLayoutMatchesSharpShaderByteOffsets()
    {
        ShaderReferenceTextureFact[] shaderFacts =
        {
            new(4u * sizeof(ulong), 3),
            new(9u * sizeof(ulong), 2),
        };
        using MetalArgumentTableLayout layout =
            CreateLayout(
                index: 0,
                Element(0, 4, ERHIBindType.StorageBuffer),
                Element(4, 3, ERHIBindType.Texture2DArray),
                Element(9, 2, ERHIBindType.StorageTexture2D));

        uint shaderTextureBase =
            ComputeSharpShaderReferenceTextureRangeEnd(shaderFacts);
        uint halTextureBase =
            MetalPrivateRasterBindingPlan.GetOrdinaryTextureRangeEnd(
                new[] { layout });

        Assert.True(layout.RequiresReferenceBuffer);
        Assert.Equal(11u, shaderTextureBase);
        Assert.Equal(shaderTextureBase, halTextureBase);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void MultiTableReferenceRootKeepsPrivateRangeAfterAllTextures()
    {
        ShaderReferenceTextureFact[] shaderFacts =
        {
            new(2u * sizeof(ulong), 4),
            new(11u * sizeof(ulong), 2),
        };
        using MetalArgumentTableLayout table0 =
            CreateLayout(
                index: 0,
                Element(63, 1, ERHIBindType.UniformBuffer),
                Element(2, 4, ERHIBindType.Texture2DArray));
        using MetalArgumentTableLayout table1 =
            CreateLayout(
                index: 3,
                Element(11, 2, ERHIBindType.StorageTexture2D),
                Element(127, 1, ERHIBindType.Sampler));
        RHIAttachmentInterfaceSignature signature =
            CreateRasterOrderedSignature(logicalAttachment: 5);

        uint shaderTextureBase =
            ComputeSharpShaderReferenceTextureRangeEnd(shaderFacts);
        MetalPrivateRasterBindingPlan plan =
            MetalPrivateRasterBindingPlan.Compile(
                new[] { table0, table1 },
                maximumTextureBindings: 64,
                in signature);

        Assert.Equal(13u, shaderTextureBase);
        Assert.Equal(shaderTextureBase, plan.TextureBase);
        Assert.Equal(18u, plan.GetRasterOrderedTextureIndex(5));
        Assert.Equal(19u, plan.RequiredTextureBindingCount);
    }

    private static uint ComputeSharpShaderTextureRangeEnd(
        ReadOnlySpan<ShaderTextureFact> facts)
    {
        uint end = 0;
        for (int index = 0; index < facts.Length; ++index)
        {
            end = Math.Max(
                end,
                checked(facts[index].Slot +
                        facts[index].BoundedCount));
        }
        return end;
    }

    private static uint ComputeSharpShaderReferenceTextureRangeEnd(
        ReadOnlySpan<ShaderReferenceTextureFact> facts)
    {
        uint end = 0;
        for (int index = 0; index < facts.Length; ++index)
        {
            uint firstElement =
                checked(facts[index].ByteOffset /
                        (uint)sizeof(ulong));
            end = Math.Max(
                end,
                checked(firstElement +
                        facts[index].BoundedCount));
        }
        return end;
    }

    private static MetalArgumentTableLayout CreateLayout(
        uint index,
        params RHIArgumentTableLayoutElement[] elements) =>
        new(
            new RHIArgumentTableLayoutDescriptor
            {
                Index = index,
                Elements = elements,
            });

    private static RHIArgumentTableLayoutElement Element(
        uint slot,
        uint count,
        ERHIBindType type) =>
        new()
        {
            Slot = slot,
            Count = count,
            Type = type,
            Stages = ERHIShaderStageMask.Fragment,
            Requirement = ERHIArgumentBindingRequirement.Required,
        };

    private static RHIAttachmentInterfaceSignature
        CreateRasterOrderedSignature(int logicalAttachment)
    {
        RHIAttachmentIndexArray attachment =
            new RHIAttachmentIndexArray(1);
        attachment[0] = logicalAttachment;
        return new RHIAttachmentInterfaceSignature(
            colorAttachmentCount: logicalAttachment + 1,
            colorInputs: attachment,
            colorOutputs: attachment,
            sampledFeedbackInputs:
                RHIAttachmentIndexArray.Empty,
            rasterOrderedReadWriteMask:
                checked((byte)(1 << logicalAttachment)));
    }

    private readonly record struct ShaderTextureFact(
        uint Slot,
        uint BoundedCount);

    private readonly record struct ShaderReferenceTextureFact(
        uint ByteOffset,
        uint BoundedCount);
}
