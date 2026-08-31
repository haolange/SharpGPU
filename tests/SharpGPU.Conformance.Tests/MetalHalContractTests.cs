using System;
using System.Runtime.InteropServices;
using System.Text;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class MetalHalContractTests
{
    [Fact]
    public void Metal_TimestampQueryContract_ShouldUseNativeCounterHeapOrThrow()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.Synchronization.TimestampQueries.Tier == ERHICapabilityTier.Unavailable)
        {
            Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.TimestampTransfer,
            }));
            Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.Timestamp,
            }));
            return;
        }

        using RHIQuery query = context.Device.CreateQuery(new RHIQueryDescriptor
        {
            Count = 2,
            Type = ERHIQueryType.TimestampTransfer,
        });

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.Timestamp.Contract");

        RHITransferEncoder timestampPass = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "Timestamp",
            Timestamp = new RHITimestampDescriptor
            {
                Query = query,
                BeginIndex = 0,
                EndIndex = 1,
            },
        });
        commandBuffer.EndTransferPass();

        RHITransferEncoder resolvePass = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "ResolveTimestamp",
        });
        resolvePass.ResolveQuery(query, 0, 2);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        SubmitAndWait(context, commandBuffer);

        Assert.Equal(ERHIQueryResultStatus.Ready, query.ResolveData());
        ReadOnlySpan<ulong> results = query.Results.Span;
        Assert.Equal(2, results.Length);
        Assert.True(results[0] != 0 || results[1] != 0, $"Timestamp results are both zero. begin={results[0]}, end={results[1]}.");
        Assert.True(results[1] >= results[0], $"Timestamp results are not monotonic. begin={results[0]}, end={results[1]}.");
    }

    [Fact]
    public void Metal_OcclusionAndPipelineStatisticsContracts_ShouldUseNativeRenderQueriesOrThrow()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        using RHIQuery? occlusionQuery = context.Device.Capabilities.Synchronization.OcclusionQueries.Tier != ERHICapabilityTier.Unavailable
            ? context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 1,
                Type = ERHIQueryType.Occlusion,
            })
            : null;

        RHIQuery? statisticsQuery = null;
        if (context.Device.Capabilities.Synchronization.PipelineStatisticsQueries.Tier != ERHICapabilityTier.Unavailable)
        {
            statisticsQuery = context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 1,
                Type = ERHIQueryType.Statistics,
            });
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 1,
                Type = ERHIQueryType.Statistics,
            }));
        }

        try
        {
            Assert.NotNull(occlusionQuery);
            using RHITexture renderTarget = CreateRenderTarget(context.Device);
            using RHIRasterPipeline pipeline = CreateRasterSmokePipeline(context.Device);
            using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();

            commandBuffer.Begin("Metal.RenderQuery.Contract");
            RHIRasterEncoder raster = commandBuffer.BeginRasterPass(CreateRasterPassDescriptor(renderTarget, occlusionQuery!, statisticsQuery));
            raster.SetPipeline(pipeline);
            raster.SetViewport(new Viewport(0, 0, 4, 4));
            raster.SetScissor(new Rect(0, 0, 4, 4));
            raster.BeginOcclusion(0);
            if (statisticsQuery != null)
            {
                raster.BeginStatistics(0);
            }

            raster.Draw(3, 1, 0, 0);

            if (statisticsQuery != null)
            {
                raster.EndStatistics(0);
            }

            raster.EndOcclusion(0);
            commandBuffer.EndRasterPass();
            commandBuffer.End();

            SubmitAndWait(context, commandBuffer);

            Assert.Equal(ERHIQueryResultStatus.Ready, occlusionQuery!.ResolveData());
            ulong occlusionValue = occlusionQuery.Results.Span[0];
            Assert.True(occlusionValue > 0, $"Metal occlusion query returned zero after a covering triangle draw. value={occlusionValue}.");

            if (statisticsQuery != null)
            {
                Assert.Equal(ERHIQueryResultStatus.Ready, statisticsQuery.ResolveData());
                ulong statisticsValue = statisticsQuery.Results.Span[0];
                Assert.True(statisticsValue > 0, $"Metal pipeline statistics query returned zero after a triangle draw. value={statisticsValue}.");
            }
        }
        finally
        {
            statisticsQuery?.Dispose();
        }
    }

    [Fact]
    public void Metal_BeginPass_WithShadingRateTexture_ShouldFailClosedAndAllowRetry()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        using RHITexture renderTarget = CreateRenderTarget(context.Device);
        using RHITexture shadingRateTexture = CreateRenderTarget(context.Device);
        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.VRS.Attachment.FailClosed");

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => commandBuffer.BeginRasterPass(
                CreateColorOnlyRasterPassDescriptor(renderTarget, shadingRateTexture)));
        Assert.Contains(
            MetalRasterPassBeginGuard.VariableRateShadingAttachmentCapabilityName,
            error.Message,
            StringComparison.Ordinal);

        RHIRasterEncoder encoder = commandBuffer.BeginRasterPass(
            CreateColorOnlyRasterPassDescriptor(renderTarget, shadingRateTexture: null));
        Assert.NotNull(encoder);
        commandBuffer.EndRasterPass();
        commandBuffer.End();
    }

    [Fact]
    public void Metal_BgraOffscreenRasterSmoke_ShouldWriteFragmentColor()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        using RHITexture renderTarget = CreateRenderTarget(context.Device, ERHIPixelFormat.B8G8R8A8_UNorm);
        using RHIRasterPipeline pipeline = CreateRasterSmokePipeline(context.Device, ERHIPixelFormat.B8G8R8A8_UNorm);
        const uint width = 4;
        const uint height = 4;
        const uint rowPitch = width * 4;
        using RHIBuffer readback = context.Device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = checked((int)(rowPitch * height)),
            UsageFlag = ERHIBufferUsage.CopyDst,
            StorageMode = ERHIStorageMode.Readback,
            Format = ERHIBufferFormat.Undefine,
        });

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.Bgra.RasterSmoke.Readback");
        RHIRasterEncoder raster = commandBuffer.BeginRasterPass(new RHIRasterPassDescriptor
        {
            Name = "BgraSmoke",
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
        raster.SetPipeline(pipeline);
        raster.SetViewport(new Viewport(0, 0, width, height));
        raster.SetScissor(new Rect(0, 0, width, height));
        raster.Draw(3, 1, 0, 0);
        commandBuffer.EndRasterPass();

        RHITransferEncoder transfer = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "BgraReadback",
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
            // BGRA8: solid green FS → B=0, G=255, R=0, A=255
            Assert.True(
                pixels[0] == 0 && pixels[1] == 255 && pixels[2] == 0 && pixels[3] == 255,
                $"Metal BGRA8 offscreen smoke pixel expected BGRA(0,255,0,255), got ({pixels[0]},{pixels[1]},{pixels[2]},{pixels[3]}).");
        }
        finally
        {
            readback.UnMap(0, 0);
        }
    }

    private static RHITexture CreateRenderTarget(RHIDevice device, ERHIPixelFormat format = ERHIPixelFormat.R8G8B8A8_UNorm)
    {
        return device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(4, 4, 1),
            Format = format,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.RenderTarget | ERHITextureUsage.CopySrc,
            Dimension = ERHITextureDimension.Texture2D,
        });
    }

    private static RHIRasterPipeline CreateRasterSmokePipeline(
        RHIDevice device,
        ERHIPixelFormat colorFormat = ERHIPixelFormat.R8G8B8A8_UNorm)
    {
        using RHIFunction vertexFunction = CreateMslFunction(device, ERHIFunctionType.Vertex, "vs_main", RasterSmokeMsl);
        using RHIFunction fragmentFunction = CreateMslFunction(device, ERHIFunctionType.Fragment, "fs_main", RasterSmokeMsl);
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
                ColorFormats = new[] { colorFormat },
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

    private static RHIRasterPassDescriptor CreateColorOnlyRasterPassDescriptor(
        RHITexture target,
        RHITexture? shadingRateTexture)
    {
        return new RHIRasterPassDescriptor
        {
            Name = "VrsAttachmentContract",
            ArrayLength = 1,
            SampleCount = ERHISampleCount.None,
            ShadingRateTexture = shadingRateTexture,
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
                    RenderTarget = target,
                },
            },
            SubPassDescriptors = Memory<RHISubPassDescriptor>.Empty,
        };
    }

    private static RHIRasterPassDescriptor CreateRasterPassDescriptor(RHITexture target, RHIQuery occlusionQuery, RHIQuery? statisticsQuery)
    {
        RHIColorAttachmentDescriptor[] colorAttachments =
        {
            new()
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
                RenderTarget = target,
            },
        };

        return new RHIRasterPassDescriptor
        {
            Name = "RenderQuery",
            ArrayLength = 1,
            SampleCount = ERHISampleCount.None,
            Occlusion = new RHIOcclusionDescriptor
            {
                Query = occlusionQuery,
            },
            Statistics = statisticsQuery == null
                ? null
                : new RHIStatisticsDescriptor
                {
                    Query = statisticsQuery,
                    WriteIndex = 0,
                },
            ColorAttachments = colorAttachments,
            SubPassDescriptors = Memory<RHISubPassDescriptor>.Empty,
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

    private const string RasterSmokeMsl = """
#include <metal_stdlib>
using namespace metal;

struct VSOut
{
    float4 position [[position]];
};

vertex VSOut vs_main(uint vertexID [[vertex_id]])
{
    float2 positions[3] =
    {
        float2(-1.0, -1.0),
        float2( 3.0, -1.0),
        float2(-1.0,  3.0),
    };

    VSOut out;
    out.position = float4(positions[vertexID], 0.0, 1.0);
    return out;
}

fragment float4 fs_main()
{
    return float4(0.0, 1.0, 0.0, 1.0);
}
""";
}

internal sealed class MetalTestContext : IDisposable
{
    public RHIInstance Instance { get; }
    public RHIDevice Device { get; }
    public RHICommandQueue Queue { get; }
    public RHIFence Fence { get; }

    private MetalTestContext(RHIInstance instance, RHIDevice device, RHICommandQueue queue, RHIFence fence)
    {
        Instance = instance;
        Device = device;
        Queue = queue;
        Fence = fence;
    }

    public static MetalTestContext Create()
    {
        Assert.True(RHIInstance.IsBackendSupported(ERHIBackend.Metal, out string reason), reason);

        RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.Metal,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        }) ?? throw new InvalidOperationException("Failed to create Metal RHI instance.");

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            RHICommandQueue? queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0);
            if (queue != null)
            {
                return new MetalTestContext(instance, device, queue, device.CreateFence());
            }
        }

        instance.Dispose();
        throw new InvalidOperationException("Metal backend did not expose a graphics queue.");
    }

    public void Dispose()
    {
        Fence.Dispose();
        Instance.Dispose();
    }
}
