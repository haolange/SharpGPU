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
        if (context.Device.Feature?.IsTimestampQueriesSupported != true)
        {
            Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.TimestampTransfer,
            }));
            Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.TimestampGenerice,
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

        Assert.True(query.ResolveData(), "Metal timestamp query did not resolve native data.");
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
        using RHIQuery? occlusionQuery = context.Device.Feature?.IsOcclusionQueriesSupported == true
            ? context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 1,
                Type = ERHIQueryType.Occlusion,
            })
            : null;

        RHIQuery? statisticsQuery = null;
        if (context.Device.Feature?.IsPipelineStatsQueriesSupported == true)
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

            Assert.True(occlusionQuery!.ResolveData(), "Metal occlusion query did not resolve native visibility data.");
            ulong occlusionValue = occlusionQuery.Results.Span[0];
            Assert.True(occlusionValue > 0, $"Metal occlusion query returned zero after a covering triangle draw. value={occlusionValue}.");

            if (statisticsQuery != null)
            {
                Assert.True(statisticsQuery.ResolveData(), "Metal pipeline statistics query did not resolve native counter data.");
                ulong statisticsValue = statisticsQuery.Results.Span[0];
                Assert.True(statisticsValue > 0, $"Metal pipeline statistics query returned zero after a triangle draw. value={statisticsValue}.");
            }
        }
        finally
        {
            statisticsQuery?.Dispose();
        }
    }

    private static RHITexture CreateRenderTarget(RHIDevice device)
    {
        return device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(4, 4, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.RenderTarget | ERHITextureUsage.CopySrc,
            Dimension = ERHITextureDimension.Texture2D,
        });
    }

    private static RHIRasterPipeline CreateRasterSmokePipeline(RHIDevice device)
    {
        using RHIFunction vertexFunction = CreateMslFunction(device, ERHIFunctionType.Vertex, "vs_main", RasterSmokeMsl);
        using RHIFunction fragmentFunction = CreateMslFunction(device, ERHIFunctionType.Fragment, "fs_main", RasterSmokeMsl);
        using RHIPipelineLayout layout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
        {
            bLocalSignature = false,
            bUseVertexLayout = false,
            PushConstantSize = 0,
            ArgumentTableLayouts = Array.Empty<RHIArgumentTableLayout>(),
        });

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

    private static RHIRenderStateDescriptor CreateDefaultRenderState()
    {
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
            },
        };
    }

    private static RHIRasterPassDescriptor CreateRasterPassDescriptor(RHITexture target, RHIQuery occlusionQuery, RHIQuery? statisticsQuery)
    {
        RHIColorAttachmentDescriptor[] colorAttachments =
        {
            new()
            {
                MipLevel = 0,
                ArraySlice = 0,
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
        context.Queue.Submit(commandBuffer, context.Fence, null!, null!);
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
    return float4(1.0, 0.0, 0.0, 1.0);
}
""";
}

public sealed class MetalMLContractTests
{
    [Fact]
    public void MetalML_UnsupportedContract_ShouldReportFalseAndThrowNotSupported()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Feature?.IsMLSupported == true)
        {
            Assert.Fail("Metal ML reported supported, but this conformance test requires a native Metal ML package create/bind/dispatch path before support can be advertised.");
        }

        Assert.Throws<NotSupportedException>(() => context.Device.CreateMLPipeline(default));
        Assert.Throws<NotSupportedException>(() => context.Device.CreateMLBindingSet(default));

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.ML.UnsupportedContract");
        Assert.Throws<NotSupportedException>(() => commandBuffer.BeginMLPass(new RHIMLPassDescriptor
        {
            Name = "UnsupportedML",
        }));
        commandBuffer.End();
    }
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
            EnableValidatior = false,
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
