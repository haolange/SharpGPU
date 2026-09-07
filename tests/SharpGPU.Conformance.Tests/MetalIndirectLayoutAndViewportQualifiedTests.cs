// Copyright (c) CGBull. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Text;
using SharpGPU;
using SharpMath;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using Xunit;

namespace SharpGPU.Conformance.Tests;

/// <summary>
/// Matching-host MetalQualified evidence for ICB encode→execute→readback and
/// multi viewport/scissor native array APIs (not only-first).
/// </summary>
[Trait("Category", "SharpGpuMetalQualified")]
public sealed class MetalIcbAndViewportQualifiedTests
{
    private const int MaxMetalViewports = 16;
    private const uint MagicValue = 0xA11CEu;

    [Fact]
    public void Metal_LayoutStreamExecution_ShouldFailClosedUntilNativeLoweringExists()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            context.Device.Capabilities.IndirectCommandBuffer.Execution.Tier);
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            context.Device.Capabilities.IndirectCommandBuffer.Tokens.Dispatch.Tier);
        Assert.Throws<NotSupportedException>(
            () => context.Device.CreateIndirectCommandLayout(
                new RHIIndirectCommandLayoutDescriptor(
                    ERHIIndirectDomain.Compute,
                    new[]
                    {
                        new RHIIndirectTokenDescriptor(ERHIIndirectTokenType.Dispatch),
                    })));
    }

    [Fact]
    public void Metal_MultiViewportAndScissor_ArrayApi_ShouldAffectBothHalvesAndFailClosed()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        const uint width = 8;
        const uint height = 4;
        const uint rowPitch = width * 4;

        using RHITexture renderTarget = context.Device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(width, height, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.RenderTarget | ERHITextureUsage.CopySrc,
            Dimension = ERHITextureDimension.Texture2D,
        });
        using RHIRasterPipeline pipeline = CreateViewportArrayPipeline(context.Device);
        using RHIBuffer readback = context.Device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = checked((int)(rowPitch * height)),
            UsageFlag = ERHIBufferUsage.CopyDst,
            StorageMode = ERHIStorageMode.Readback,
            Format = ERHIBufferFormat.Undefine,
        });

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.MultiViewportScissor.PixelEvidence");

        RHIRasterEncoder raster = commandBuffer.BeginRasterPass(new RHIRasterPassDescriptor
        {
            Name = "MultiViewport",
            ArrayLength = 1,
            SampleCount = ERHISampleCount.None,
            ColorAttachments = new[]
            {
                new RHIColorAttachmentDescriptor
                {
                    SubresourceRange = new RHITextureSubresourceRange
                    {
                        BaseMipLevel = 0,
                        MipLevelCount = 1,
                        BaseArrayLayer = 0,
                        ArrayLayerCount = 1,
                        AspectMask = ERHITextureAspectMask.Color,
                    },
                    ClearValue = new float4(0, 0, 0, 1),
                    LoadAction = ERHILoadAction.Clear,
                    StoreAction = ERHIStoreAction.Store,
                    RenderTarget = renderTarget,
                },
            },
            SubPassDescriptors = Memory<RHISubPassDescriptor>.Empty,
        });

        // Fail-closed contracts for the native array path.
        Assert.Throws<ArgumentException>(() => raster.SetViewports(Memory<Viewport>.Empty));
        Assert.Throws<ArgumentException>(() => raster.SetScissors(Memory<Rect>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Viewport[] tooMany = new Viewport[MaxMetalViewports + 1];
            for (int i = 0; i < tooMany.Length; ++i)
            {
                tooMany[i] = new Viewport(0, 0, 1, 1, 0, 1);
            }

            raster.SetViewports(tooMany);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            Rect[] tooMany = new Rect[MaxMetalViewports + 1];
            for (int i = 0; i < tooMany.Length; ++i)
            {
                tooMany[i] = new Rect(0, 0, 1, 1);
            }

            raster.SetScissors(tooMany);
        });

        raster.SetPipeline(pipeline);
        raster.SetViewports(new[]
        {
            new Viewport(0, 0, 4, 4, 0, 1),
            new Viewport(4, 0, 4, 4, 0, 1),
        });
        raster.SetScissors(new[]
        {
            new Rect(0, 0, 4, 4),
            new Rect(4, 0, 8, 4),
        });
        // Two instances → viewport_array_index 0/1 with distinct colors.
        raster.Draw(3, 2, 0, 0);
        commandBuffer.EndRasterPass();

        RHITransferEncoder transfer = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "ViewportReadback",
        });
        transfer.Barrier(RHIBarrier.Texture(
            renderTarget,
            RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
            ERHITextureLayout.RenderTarget,
            ERHITextureLayout.CopySource,
            ERHIStageMask.Fragment,
            ERHIStageMask.Transfer,
            ERHIAccessMask.RenderTargetWrite,
            ERHIAccessMask.TransferRead));
        transfer.CopyTextureToBuffer(
            new RHITextureCopyDescriptor
            {
                Texture = renderTarget,
                MipLevel = 0,
                SliceBase = 0,
                SliceCount = 1,
                Origin = new uint3(0, 0, 0),
            },
            new RHIBufferCopyDescriptor
            {
                Buffer = readback,
                Offset = 0,
                RowPitch = rowPitch,
                TextureHeight = new uint3(width, height, 1),
            },
            new int3((int)width, (int)height, 1));
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        SubmitAndWait(context, commandBuffer);

        IntPtr mapped = readback.Map(0, rowPitch * height);
        try
        {
            byte[] pixels = new byte[rowPitch * height];
            Marshal.Copy(mapped, pixels, 0, pixels.Length);

            // Left half (viewport 0): red RGBA(255,0,0,255)
            AssertPixel(pixels, rowPitch, x: 1, y: 1, r: 255, g: 0, b: 0, a: 255, "left/viewport0");
            // Right half (viewport 1): green RGBA(0,255,0,255) — proves not only-first.
            AssertPixel(pixels, rowPitch, x: 5, y: 1, r: 0, g: 255, b: 0, a: 255, "right/viewport1");
        }
        finally
        {
            readback.UnMap(0, 0);
        }
    }

    private static void AssertPixel(
        byte[] pixels,
        uint rowPitch,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a,
        string label)
    {
        int offset = checked((int)(y * rowPitch + x * 4));
        Assert.True(
            pixels[offset] == r &&
            pixels[offset + 1] == g &&
            pixels[offset + 2] == b &&
            pixels[offset + 3] == a,
            $"{label} expected RGBA({r},{g},{b},{a}), got ({pixels[offset]},{pixels[offset + 1]},{pixels[offset + 2]},{pixels[offset + 3]}).");
    }

    private static RHIRasterPipeline CreateViewportArrayPipeline(RHIDevice device)
    {
        using RHIFunction vertexFunction = CreateMslFunction(device, ERHIFunctionType.Vertex, "vs_main", ViewportArrayMsl);
        using RHIFunction fragmentFunction = CreateMslFunction(device, ERHIFunctionType.Fragment, "fs_main", ViewportArrayMsl);
        RHIPipelineLayout layout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
        {
            bLocalSignature = false,
            bUseVertexLayout = false,
            PushConstantSize = 0,
            BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
        });

        try
        {
            return device.CreateRasterPipeline(new RHIRasterPipelineDescriptor
            {
                SampleCount = ERHISampleCount.None,
                DepthFormat = ERHIPixelFormat.Unknown,
                ColorFormats = new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
                PipelineLayout = layout,
                FragmentFunction = fragmentFunction,
                PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
                {
                    PrimitiveTopology = ERHIPrimitiveTopology.TriangleList,
                    VertexAssembler = new RHIVertexAssemblerDescriptor(vertexFunction, Array.Empty<RHIVertexLayoutDescriptor>()),
                },
                RenderState = CreateDefaultRenderState(),
            });
        }
        catch
        {
            layout.Dispose();
            throw;
        }
    }

    private static RHIRenderStateDescriptor CreateDefaultRenderState()
    {
        RHIStencilStateDescriptor keepStencilFace = new()
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilPassOp = ERHIStencilOp.Keep,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };

        return new RHIRenderStateDescriptor
        {
            BlendState = new RHIBlendStateDescriptor
            {
                BlendDescriptor0 = new RHIBlendDescriptor
                {
                    BlendEnable = false,
                    BlendOpColor = ERHIBlendOp.Add,
                    SrcBlendColor = ERHIBlendMode.One,
                    DstBlendColor = ERHIBlendMode.Zero,
                    BlendOpAlpha = ERHIBlendOp.Add,
                    SrcBlendAlpha = ERHIBlendMode.One,
                    DstBlendAlpha = ERHIBlendMode.Zero,
                    ColorWriteChannel = ERHIColorWriteChannel.All,
                },
            },
            RasterizerState = new RHIRasterizerStateDescriptor
            {
                FillMode = ERHIFillMode.Solid,
                CullMode = ERHICullMode.None,
                DepthClipEnable = true,
                FrontCounterClockwise = false,
            },
            DepthStencilState = new RHIDepthStencilStateDescriptor
            {
                DepthEnable = false,
                DepthWriteMask = false,
                StencilEnable = false,
                ComparisonMode = ERHIComparisonMode.Always,
                FrontFace = keepStencilFace,
                BackFace = keepStencilFace,
            },
        };
    }

    private static RHIFunction CreateMslFunction(RHIDevice device, ERHIFunctionType type, string entryName, string source)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        IntPtr sourcePtr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, sourcePtr, bytes.Length);
            return device.CreateFunction(new RHIFunctionDescriptor
            {
                ByteCode = sourcePtr,
                ByteSize = (uint)bytes.Length,
                EntryName = entryName,
                Type = type,
                PayloadKind = ERHIShaderPayloadKind.MslSource,
            });
        }
        finally
        {
            Marshal.FreeHGlobal(sourcePtr);
        }
    }

    private static void SubmitAndWait(MetalTestContext context, RHICommandBuffer commandBuffer)
    {
        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();
    }

    private const string ComputeWriteMsl = """
#include <metal_stdlib>
using namespace metal;

kernel void cs_main(device uint* output [[buffer(0)]])
{
    output[0] = 0xA11CEu;
}
""";

    private const string ViewportArrayMsl = """
#include <metal_stdlib>
using namespace metal;

struct VSOut
{
    float4 position [[position]];
    uint viewportIndex [[viewport_array_index]];
    float4 color;
};

vertex VSOut vs_main(uint vertexID [[vertex_id]], uint instanceID [[instance_id]])
{
    float2 positions[3] =
    {
        float2(-1.0, -1.0),
        float2( 3.0, -1.0),
        float2(-1.0,  3.0),
    };

    VSOut out;
    out.position = float4(positions[vertexID], 0.0, 1.0);
    out.viewportIndex = instanceID;
    out.color = instanceID == 0
        ? float4(1.0, 0.0, 0.0, 1.0)
        : float4(0.0, 1.0, 0.0, 1.0);
    return out;
}

fragment float4 fs_main(VSOut in [[stage_in]])
{
    return in.color;
}
""";
}
