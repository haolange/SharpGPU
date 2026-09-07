using System;
using System.Runtime.InteropServices;
using SharpMath;
using SharpShader.HLSLCrossCompiler;

namespace SharpGPU.Examples
{
    internal static class RasterWorkload
    {
    private const string FullscreenVertexShader = """
struct VSOut
{
    float4 position : SV_Position;
};

VSOut vs_main(uint vertexId : SV_VertexID)
{
    float2 positions[3] =
    {
        float2(-1.0, -1.0),
        float2(-1.0, 3.0),
        float2(3.0, -1.0)
    };
    VSOut output;
    output.position = float4(positions[vertexId], 0.0, 1.0);
    return output;
}
""";

    private const string SolidPixelShader = """
float4 ps_main() : SV_Target0
{
    return float4(0.0, 1.0, 0.0, 1.0);
}
""";

public static void Run(ERHIBackend backend)
    {
        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = backend, SurfaceKind = ERHINativeSurfaceKind.Headless,
            GraphicsQueueRequestCount = 1,
        });
        using RHIDevice device = instance.GetDevice(0);
        RHICommandQueue queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
            ?? throw new InvalidOperationException("Graphics queue unavailable.");
        using RHIFence fence = device.CreateFence();
        Require(device.Capabilities.Synchronization.PipelineStatisticsQueries.Tier != ERHICapabilityTier.Unavailable, "Pipeline statistics unavailable.");

        using RHIQuery query = device.CreateQuery(new RHIQueryDescriptor
        {
            Count = 1,
            Type = ERHIQueryType.Statistics,
            Domain = ERHIPipelineStatisticsDomain.Raster,
            CounterMask =
                ERHIPipelineStatisticCounter.InputAssemblyPrimitives |
                ERHIPipelineStatisticCounter.PixelShaderInvocations,
        });
        using RHITexture target = device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(4, 4, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.RenderTarget,
            Dimension = ERHITextureDimension.Texture2D,
        });
        using RHIPipelineLayout layout = device.CreatePipelineLayout(
            new RHIPipelineLayoutDescriptor
            {
                BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
            });
        using RHIFunction vertex = CompileFunction(
            device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        using RHIFunction pixel = CompileFunction(
            device,
            ERHIFunctionType.Fragment,
            ShaderStageKind.Pixel,
            "ps_main",
            SolidPixelShader);
        using RHIRasterPipeline pipeline = device.CreateRasterPipeline(
            new RHIRasterPipelineDescriptor
            {
                SampleCount = ERHISampleCount.None,
                DepthFormat = ERHIPixelFormat.Unknown,
                ColorFormats = new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
                PipelineLayout = layout,
                FragmentFunction = pixel,
                PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
                {
                    PrimitiveTopology = ERHIPrimitiveTopology.TriangleList,
                    VertexAssembler = new RHIVertexAssemblerDescriptor(
                        vertex,
                        Array.Empty<RHIVertexLayoutDescriptor>()),
                },
                RenderState = CreateDefaultRenderState(),
            });

        using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
        commandBuffer.Begin("Sample.Dx12.RasterStatistics");
        PrepareRenderTarget(commandBuffer, target);
        RHIRasterEncoder raster = commandBuffer.BeginRasterPass(new RHIRasterPassDescriptor
        {
            Name = "Sample.RasterStatistics",
            ArrayLength = 1,
            SampleCount = ERHISampleCount.None,
            Statistics = new RHIStatisticsDescriptor
            {
                Query = query,
                WriteIndex = 0,
            },
            ColorAttachments = new[]
            {
                new RHIColorAttachmentDescriptor
                {
                    SubresourceRange = RHITextureSubresourceRange.Whole(
                        ERHITextureAspectMask.Color),
                    ClearValue = new float4(0, 0, 0, 1),
                    LoadAction = ERHILoadAction.Clear,
                    StoreAction = ERHIStoreAction.Store,
                    RenderTarget = target,
                },
            },
            SubPassDescriptors = Memory<RHISubPassDescriptor>.Empty,
        });
        raster.SetPipeline(pipeline);
        raster.SetViewport(new Viewport(0, 0, 4, 4));
        raster.SetScissor(new Rect(0, 0, 4, 4));
        raster.BeginStatistics(0);
        raster.Draw(3, 1, 0, 0);
        raster.EndStatistics(0);
        commandBuffer.EndRasterPass();

        RHITransferEncoder resolve = commandBuffer.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "Sample.RasterStatistics.Resolve",
            });
        resolve.ResolveQuery(query, 0, 1);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        fence.Reset();
        queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: fence));
        fence.Wait();

        Require(query.ResolveData() == ERHIQueryResultStatus.Ready, "Query readback is not ready.");
        Require(query.TryGetRasterStatistics(0, out RHIRasterPipelineStatistics statistics), "Raster statistics unavailable.");
        Require(
            statistics.PixelShaderInvocations > 0 ||
            statistics.InputAssemblyPrimitives > 0,
            $"Raster statistics did not move after a triangle draw. PS={statistics.PixelShaderInvocations}, IAPrim={statistics.InputAssemblyPrimitives}.");
        Console.WriteLine($"{backend}: triangle draw, primitives={statistics.InputAssemblyPrimitives}, pixelInvocations={statistics.PixelShaderInvocations}.");
    }
private static void PrepareRenderTarget(RHICommandBuffer command, RHITexture texture)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "Sample.PrepareRenderTarget",
            });
        transfer.Barrier(RHIBarrier.Texture(
            texture,
            RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
            ERHITextureLayout.Undefined,
            ERHITextureLayout.RenderTarget,
            ERHIStageMask.None,
            ERHIStageMask.Fragment,
            ERHIAccessMask.None,
            ERHIAccessMask.RenderTargetRead | ERHIAccessMask.RenderTargetWrite));
        command.EndTransferPass();
    }
private static RHIFunction CompileFunction(
        RHIDevice device,
        ERHIFunctionType functionType,
        ShaderStageKind stage,
        string entryPoint,
        string source)
    {
        ShaderCompileResult result = HLSLCrossCompiler.Compile(
            new ShaderCompileRequest
            {
                Source = source,
                SourceName = "Triangle.hlsl",
                EntryPoint = entryPoint,
                Stage = stage,
                ShaderModel = new ShaderModelVersion(6, 6),
                Target = device.BackendType == ERHIBackend.DirectX12 ? ShaderTargetKind.Dxil : ShaderTargetKind.SpirV,
            });
        Require(result.Bytecode.Length > 0, "Compiler returned empty bytecode.");

        IntPtr pointer = Marshal.AllocHGlobal(result.Bytecode.Length);
        try
        {
            Marshal.Copy(result.Bytecode, 0, pointer, result.Bytecode.Length);
            return device.CreateFunction(
                new RHIFunctionDescriptor
                {
                    ByteCode = pointer,
                    ByteSize = checked((uint)result.Bytecode.Length),
                    EntryName = entryPoint,
                    Type = functionType,
                    PayloadKind = device.BackendType == ERHIBackend.DirectX12 ? ERHIShaderPayloadKind.Dxil : ERHIShaderPayloadKind.SpirV,
                });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
private static RHIRenderStateDescriptor CreateDefaultRenderState()
    {
        RHIStencilStateDescriptor keep = new()
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
                IndependentBlend = false,
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
                FrontFace = keep,
                BackFace = keep,
            },
        };
    }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
