#if SHARPGPU_ENABLE_DX12
using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using SharpShader.HLSLCrossCompiler;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
public sealed class G2WindowsQualifiedTests
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

    private const string EmptyComputeShader = """
[numthreads(1, 1, 1)]
void main(uint3 dispatchThreadId : SV_DispatchThreadID)
{
}
""";

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_QueryFormatSupport_ShouldReportHonestPairedResolveBits()
    {
        using FeatureContractContext context = RequireDx12();
        Dx12Device device = Assert.IsType<Dx12Device>(context.Device);
        RHIFormatSupportQuery query = new(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHITextureUsage.RenderTarget,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.Count4,
            ERHITextureTiling.Optimal);
        RHICapability capability = device.QueryFormatSupport(in query);
        Assert.True(
            capability.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedFormatOperationMask,
                out ulong mask));

        Vortice.DXGI.Format nativeFormat =
            Dx12Utility.ConvertToDx12ViewFormat(ERHIPixelFormat.R8G8B8A8_UNorm);
        Assert.True(
            device.NativeDevice.CheckFormatSupport(
                nativeFormat,
                out Vortice.Direct3D12.FormatSupport1 support1,
                out _));

        bool nativeSource =
            (support1 & Vortice.Direct3D12.FormatSupport1.MultisampleRendertarget) != 0;
        bool nativeDestination =
            (support1 & Vortice.Direct3D12.FormatSupport1.MultisampleResolve) != 0;
        Assert.Equal(
            nativeSource,
            (mask & (ulong)ERHIFormatSupportOperation.ResolveSource) != 0);
        Assert.Equal(
            nativeDestination,
            (mask & (ulong)ERHIFormatSupportOperation.ResolveDestination) != 0);
        Assert.True(
            nativeSource || nativeDestination,
            "DX12 RGBA8 should expose at least one MSAA resolve format bit.");
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_QueryResolveSupport_ShouldAcceptCommonMsaaColorPair()
    {
        using FeatureContractContext context = RequireDx12();
        RHIResolveSupportQuery query = new(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHITextureUsage.RenderTarget,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.Count4,
            ERHITextureTiling.Optimal,
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHITextureUsage.RenderTarget | ERHITextureUsage.ResolveTarget,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.None,
            ERHITextureTiling.Optimal,
            ERHITextureAspectMask.Color,
            ERHIResolveMode.Sample0);
        RHICapability capability = context.Device.QueryResolveSupport(in query);
        Assert.NotEqual(ERHICapabilityTier.Unavailable, capability.Tier);
        Assert.True(
            capability.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedResolveModeMask,
                out ulong modeMask));
        Assert.NotEqual(0UL, modeMask);
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_RasterPipelineStatistics_ShouldMoveTypedCountersAfterTriangleDraw()
    {
        using FeatureContractContext context = RequireDx12();
        RHIDevice device = context.Device;
        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            device.Capabilities.Synchronization.PipelineStatisticsQueries.Tier);

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

        using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
        commandBuffer.Begin("G2.Dx12.RasterStatistics");
        PrepareRenderTarget(commandBuffer, target);
        RHIRasterEncoder raster = commandBuffer.BeginRasterPass(new RHIRasterPassDescriptor
        {
            Name = "G2.RasterStatistics",
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
                Name = "G2.RasterStatistics.Resolve",
            });
        resolve.ResolveQuery(query, 0, 1);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        Assert.Equal(ERHIQueryResultStatus.Ready, query.ResolveData());
        Assert.True(query.TryGetRasterStatistics(0, out RHIRasterPipelineStatistics statistics));
        Assert.True(
            statistics.PixelShaderInvocations > 0 ||
            statistics.InputAssemblyPrimitives > 0,
            $"DX12 raster statistics did not move after a triangle draw. PS={statistics.PixelShaderInvocations}, IAPrim={statistics.InputAssemblyPrimitives}.");
        Assert.Throws<InvalidOperationException>(() => query.TryGetComputeStatistics(0, out _));
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_ComputePipelineStatistics_ShouldCountDispatchInvocationsWhenAvailable()
    {
        using FeatureContractContext context = RequireDx12();
        RHIDevice device = context.Device;
        RHICapability capability =
            device.Capabilities.Synchronization.PipelineStatisticsQueries;
        if (capability.Tier == ERHICapabilityTier.Unavailable ||
            !capability.Limits.TryGetValue(
                ERHICapabilityLimitKind.ComputePipelineStatisticCounterMask,
                out ulong computeMask) ||
            (computeMask & (ulong)ERHIPipelineStatisticCounter.ComputeShaderInvocations) == 0)
        {
            Assert.Throws<NotSupportedException>(
                () => device.CreateQuery(new RHIQueryDescriptor
                {
                    Count = 1,
                    Type = ERHIQueryType.Statistics,
                    Domain = ERHIPipelineStatisticsDomain.Compute,
                    CounterMask = ERHIPipelineStatisticCounter.ComputeShaderInvocations,
                }));
            return;
        }

        using RHIQuery query = device.CreateQuery(new RHIQueryDescriptor
        {
            Count = 1,
            Type = ERHIQueryType.Statistics,
            Domain = ERHIPipelineStatisticsDomain.Compute,
            CounterMask = ERHIPipelineStatisticCounter.ComputeShaderInvocations,
        });
        using RHIFunction function = CompileFunction(
            device,
            ERHIFunctionType.Compute,
            ShaderStageKind.Compute,
            "main",
            EmptyComputeShader);
        using RHIPipelineLayout layout = device.CreatePipelineLayout(
            new RHIPipelineLayoutDescriptor
            {
                BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
            });
        using RHIComputePipeline pipeline = device.CreateComputePipeline(
            new RHIComputePipelineDescriptor
            {
                ThreadSize = new uint3(1, 1, 1),
                ComputeFunction = function,
                PipelineLayout = layout,
            });

        using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
        commandBuffer.Begin("G2.Dx12.ComputeStatistics");
        RHIComputeEncoder compute = commandBuffer.BeginComputePass(new RHIComputePassDescriptor
        {
            Name = "G2.ComputeStatistics",
            Statistics = new RHIStatisticsDescriptor
            {
                Query = query,
                WriteIndex = 0,
            },
        });
        compute.SetPipeline(pipeline);
        compute.BeginStatistics(0);
        compute.Dispatch(1, 1, 1);
        compute.EndStatistics(0);
        commandBuffer.EndComputePass();

        RHITransferEncoder resolve = commandBuffer.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "G2.ComputeStatistics.Resolve",
            });
        resolve.ResolveQuery(query, 0, 1);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        Assert.Equal(ERHIQueryResultStatus.Ready, query.ResolveData());
        Assert.True(query.TryGetComputeStatistics(0, out RHIComputePipelineStatistics statistics));
        Assert.True(
            statistics.ComputeShaderInvocations > 0,
            $"DX12 compute statistics returned zero after dispatch. CS={statistics.ComputeShaderInvocations}.");
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_QueryResolveSupport_ShouldNotAdvertiseDepthSample0()
    {
        using FeatureContractContext context = RequireDx12();
        RHIResolveSupportQuery query = new(
            ERHIPixelFormat.D32_Float,
            ERHITextureUsage.DepthStencil,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.Count4,
            ERHITextureTiling.Optimal,
            ERHIPixelFormat.D32_Float,
            ERHITextureUsage.DepthStencil | ERHITextureUsage.ResolveTarget,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.None,
            ERHITextureTiling.Optimal,
            ERHITextureAspectMask.Depth,
            ERHIResolveMode.Min);
        RHICapability capability = context.Device.QueryResolveSupport(in query);
        if (capability.Tier == ERHICapabilityTier.Unavailable)
        {
            RHIResolveSupportQuery sample0 = new(
                ERHIPixelFormat.D32_Float,
                ERHITextureUsage.DepthStencil,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.Count4,
                ERHITextureTiling.Optimal,
                ERHIPixelFormat.D32_Float,
                ERHITextureUsage.DepthStencil | ERHITextureUsage.ResolveTarget,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal,
                ERHITextureAspectMask.Depth,
                ERHIResolveMode.Sample0);
            Assert.Equal(
                ERHICapabilityTier.Unavailable,
                context.Device.QueryResolveSupport(in sample0).Tier);
            return;
        }

        Assert.True(
            capability.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedResolveModeMask,
                out ulong modeMask));
        Assert.Equal(0UL, modeMask & (1UL << (byte)ERHIResolveMode.Sample0));
        Assert.NotEqual(0UL, modeMask & (1UL << (byte)ERHIResolveMode.Min));
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_BeginStatistics_WithoutPassDeclaration_ShouldThrow()
    {
        using FeatureContractContext context = RequireDx12();
        using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
        commandBuffer.Begin("G2.Dx12.Statistics.Undeclared");
        RHIComputeEncoder compute = commandBuffer.BeginComputePass(
            new RHIComputePassDescriptor
            {
                Name = "G2.Dx12.Statistics.Undeclared",
            });
        Assert.Throws<InvalidOperationException>(() => compute.BeginStatistics(0));
        Assert.Throws<InvalidOperationException>(() => compute.EndStatistics(0));
        commandBuffer.EndComputePass();
        commandBuffer.End();
    }

    private static FeatureContractContext RequireDx12()
    {
        Assert.True(
            FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out string reason),
            $"DX12 is required for the Windows-qualified G2 gate: {reason}");
        Assert.NotNull(context);
        return context;
    }

    private static void PrepareRenderTarget(RHICommandBuffer command, RHITexture texture)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "G2.PrepareRenderTarget",
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
                SourceName = "G2WindowsQualifiedTests.hlsl",
                EntryPoint = entryPoint,
                Stage = stage,
                ShaderModel = new ShaderModelVersion(6, 6),
                Target = ShaderTargetKind.Dxil,
            });
        Assert.NotEmpty(result.Bytecode);

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
                    PayloadKind = ERHIShaderPayloadKind.Dxil,
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
}
}
#endif
