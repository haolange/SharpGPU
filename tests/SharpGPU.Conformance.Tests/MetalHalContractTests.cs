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
    return float4(1.0, 0.0, 0.0, 1.0);
}
""";
}

public sealed class MetalMLContractTests
{
    [Fact]
    public void MetalML_ArtifactBackedPipeline_ShouldCreateAndDispatch()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.MachineLearning.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            RHIMLTensorDescriptor tensorDescriptor = CreateTensorDescriptor(2, 3);
            RHIMLOpDescriptor addOp = RHIMLOpDescriptor.Create(
                ERHIMLOpKind.ElementWiseAdd,
                new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
                tensorDescriptor,
                "Add");
            NotSupportedException exception = Assert.Throws<NotSupportedException>(() => context.Device.CreateMLProgram(RHIMLProgramDescriptor.Create(
                "MetalML.Unsupported.Contract",
                new[] { tensorDescriptor, tensorDescriptor },
                new[] { tensorDescriptor },
                new[] { addOp })));
            Assert.Contains("MPSGraph-package route is disabled", exception.Message);
            Assert.Contains("No compute-kernel or CPU fallback", exception.Message);
            return;
        }

        RunGemmReluContract(context);
        RunAddContract(context);
        RunMatrixMultiplyContract(context);
        AssertMultiOpPackageRejected(context);
    }

    private static void RunGemmReluContract(MetalTestContext context)
    {
        RHIMLTensorDescriptor aDescriptor = CreateTensorDescriptor(2, 3);
        RHIMLTensorDescriptor bDescriptor = CreateTensorDescriptor(3, 4);
        RHIMLTensorDescriptor cDescriptor = CreateTensorDescriptor(2, 4);
        RHIMLTensorDescriptor outputDescriptor = CreateTensorDescriptor(2, 4);
        RHIMLOpDescriptor gemmReluOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.GeneralMatrixMultiply,
            new[]
            {
                RHIMLOpTensorRef.FromInput(0),
                RHIMLOpTensorRef.FromInput(1),
                RHIMLOpTensorRef.FromInput(2),
            },
            outputDescriptor,
            "GemmRelu");
        gemmReluOp.FusedActivation = ERHIMLFusedActivation.Relu;

        using RHIMLProgram program = context.Device.CreateMLProgram(RHIMLProgramDescriptor.Create(
            "MetalML.GemmRelu.Contract",
            new[] { aDescriptor, bDescriptor, cDescriptor },
            new[] { outputDescriptor },
            new[] { gemmReluOp }));

        using RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "MetalML.GemmRelu.Pipeline",
            Program = program,
        });

        Assert.Equal(3u, pipeline.InputCount);
        Assert.Equal(1u, pipeline.OutputCount);
        Assert.Equal(4, pipeline.BindingInfos.Length);

        int aNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(aDescriptor));
        int bNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(bDescriptor));
        int cNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(cDescriptor));
        int outputNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(outputDescriptor));
        using RHIBuffer inputA = CreateGpuBacking(context.Device, aNativeByteSize);
        using RHIBuffer inputB = CreateGpuBacking(context.Device, bNativeByteSize);
        using RHIBuffer inputC = CreateGpuBacking(context.Device, cNativeByteSize);
        using RHIBuffer output = CreateGpuBacking(context.Device, outputNativeByteSize);
        using RHITensor tensorA = context.Device.CreateTensor(WithBacking(aDescriptor, inputA));
        using RHITensor tensorB = context.Device.CreateTensor(WithBacking(bDescriptor, inputB));
        using RHITensor tensorC = context.Device.CreateTensor(WithBacking(cDescriptor, inputC));
        using RHITensor tensorOut = context.Device.CreateTensor(WithBacking(outputDescriptor, output));

        RHITensor[] inputs = { tensorA, tensorB, tensorC };
        RHITensor[] outputs = { tensorOut };
        using RHIMLBindingSet bindingSet = context.Device.CreateMLBindingSet(new RHIMLBindingSetDescriptor
        {
            Pipeline = pipeline,
            Inputs = inputs,
            Outputs = outputs,
        });

        using RHIBuffer uploadA = CreateUpload(context.Device, Pack(aDescriptor, ToBytes(new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f })));
        using RHIBuffer uploadB = CreateUpload(context.Device, Pack(bDescriptor, ToBytes(new[]
        {
            1.0f, 0.0f, -1.0f, 2.0f,
            0.5f, 1.0f, 0.0f, -0.5f,
            2.0f, -1.0f, 1.0f, 0.0f,
        })));
        using RHIBuffer uploadC = CreateUpload(context.Device, Pack(cDescriptor, ToBytes(new[]
        {
            0.5f, -1.0f, 0.0f, 1.0f,
            -10.0f, 0.5f, 1.0f, -2.0f,
        })));
        using RHIBuffer readback = CreateReadback(context.Device, outputNativeByteSize);

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.ML.GemmRelu.ArtifactContract");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "UploadML" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputC, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputA, 0, aNativeByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputB, 0, bNativeByteSize);
        upload.CopyBufferToBuffer(uploadC, 0, inputC, 0, cNativeByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputC, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "ArtifactML" });
        ml.SetPipeline(pipeline);
        ml.SetBindingSet(bindingSet);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "ReadbackML" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.MachineLearning, ERHISyncStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(output, 0, readback, 0, outputNativeByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        float[] actual = FromBytes(Unpack(outputDescriptor, ReadbackBytes(readback, outputNativeByteSize)));
        AssertClose(new[] { 8.5f, 0.0f, 2.0f, 2.0f, 8.5f, 0.0f, 3.0f, 3.5f }, actual, "MetalML.GemmRelu");
    }

    private static void RunAddContract(MetalTestContext context)
    {
        RHIMLTensorDescriptor tensorDescriptor = CreateTensorDescriptor(2, 3);
        RHIMLOpDescriptor addOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.ElementWiseAdd,
            new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
            tensorDescriptor,
            "Add");

        using RHIMLProgram program = context.Device.CreateMLProgram(RHIMLProgramDescriptor.Create(
            "MetalML.Add.Contract",
            new[] { tensorDescriptor, tensorDescriptor },
            new[] { tensorDescriptor },
            new[] { addOp }));

        using RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "MetalML.Add.Pipeline",
            Program = program,
        });

        Assert.Equal(2u, pipeline.InputCount);
        Assert.Equal(1u, pipeline.OutputCount);
        Assert.Equal(3, pipeline.BindingInfos.Length);

        int nativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(tensorDescriptor));
        using RHIBuffer inputA = CreateGpuBacking(context.Device, nativeByteSize);
        using RHIBuffer inputB = CreateGpuBacking(context.Device, nativeByteSize);
        using RHIBuffer output = CreateGpuBacking(context.Device, nativeByteSize);
        using RHITensor tensorA = context.Device.CreateTensor(WithBacking(tensorDescriptor, inputA));
        using RHITensor tensorB = context.Device.CreateTensor(WithBacking(tensorDescriptor, inputB));
        using RHITensor tensorOut = context.Device.CreateTensor(WithBacking(tensorDescriptor, output));

        RHITensor[] inputs = { tensorA, tensorB };
        RHITensor[] outputs = { tensorOut };
        using RHIMLBindingSet bindingSet = context.Device.CreateMLBindingSet(new RHIMLBindingSetDescriptor
        {
            Pipeline = pipeline,
            Inputs = inputs,
            Outputs = outputs,
        });

        using RHIBuffer uploadA = CreateUpload(context.Device, Pack(tensorDescriptor, ToBytes(new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f })));
        using RHIBuffer uploadB = CreateUpload(context.Device, Pack(tensorDescriptor, ToBytes(new[] { 10.0f, 20.0f, 30.0f, 40.0f, 50.0f, 60.0f })));
        using RHIBuffer readback = CreateReadback(context.Device, nativeByteSize);

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.ML.Add.ArtifactContract");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "UploadML" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputA, 0, nativeByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputB, 0, nativeByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "ArtifactML" });
        ml.SetPipeline(pipeline);
        ml.SetBindingSet(bindingSet);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "ReadbackML" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.MachineLearning, ERHISyncStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(output, 0, readback, 0, nativeByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        float[] actual = FromBytes(Unpack(tensorDescriptor, ReadbackBytes(readback, nativeByteSize)));
        AssertClose(new[] { 11.0f, 22.0f, 33.0f, 44.0f, 55.0f, 66.0f }, actual, "MetalML.Add");
    }

    private static void RunMatrixMultiplyContract(MetalTestContext context)
    {
        RHIMLTensorDescriptor aDescriptor = CreateTensorDescriptor(2, 3);
        RHIMLTensorDescriptor bDescriptor = CreateTensorDescriptor(3, 2);
        RHIMLTensorDescriptor outputDescriptor = CreateTensorDescriptor(2, 2);
        RHIMLOpDescriptor matMulOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.MatrixMultiply,
            new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
            outputDescriptor,
            "MatMul");

        using RHIMLProgram program = context.Device.CreateMLProgram(RHIMLProgramDescriptor.Create(
            "MetalML.MatMul.Contract",
            new[] { aDescriptor, bDescriptor },
            new[] { outputDescriptor },
            new[] { matMulOp }));

        using RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "MetalML.MatMul.Pipeline",
            Program = program,
        });

        Assert.Equal(2u, pipeline.InputCount);
        Assert.Equal(1u, pipeline.OutputCount);
        Assert.Equal(3, pipeline.BindingInfos.Length);

        int aNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(aDescriptor));
        int bNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(bDescriptor));
        int outputNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(outputDescriptor));
        using RHIBuffer inputA = CreateGpuBacking(context.Device, aNativeByteSize);
        using RHIBuffer inputB = CreateGpuBacking(context.Device, bNativeByteSize);
        using RHIBuffer output = CreateGpuBacking(context.Device, outputNativeByteSize);
        using RHITensor tensorA = context.Device.CreateTensor(WithBacking(aDescriptor, inputA));
        using RHITensor tensorB = context.Device.CreateTensor(WithBacking(bDescriptor, inputB));
        using RHITensor tensorOut = context.Device.CreateTensor(WithBacking(outputDescriptor, output));

        RHITensor[] inputs = { tensorA, tensorB };
        RHITensor[] outputs = { tensorOut };
        using RHIMLBindingSet bindingSet = context.Device.CreateMLBindingSet(new RHIMLBindingSetDescriptor
        {
            Pipeline = pipeline,
            Inputs = inputs,
            Outputs = outputs,
        });

        using RHIBuffer uploadA = CreateUpload(context.Device, Pack(aDescriptor, ToBytes(new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f })));
        using RHIBuffer uploadB = CreateUpload(context.Device, Pack(bDescriptor, ToBytes(new[] { 7.0f, 8.0f, 9.0f, 10.0f, 11.0f, 12.0f })));
        using RHIBuffer readback = CreateReadback(context.Device, outputNativeByteSize);

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Metal.ML.MatMul.ArtifactContract");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "UploadML" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.None, ERHISyncStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputA, 0, aNativeByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputB, 0, bNativeByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHISyncStageMask.Transfer, ERHISyncStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "ArtifactML" });
        ml.SetPipeline(pipeline);
        ml.SetBindingSet(bindingSet);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "ReadbackML" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHISyncStageMask.MachineLearning, ERHISyncStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(output, 0, readback, 0, outputNativeByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        float[] actual = FromBytes(Unpack(outputDescriptor, ReadbackBytes(readback, outputNativeByteSize)));
        AssertClose(new[] { 58.0f, 64.0f, 139.0f, 154.0f }, actual, "MetalML.MatMul");
    }

    private static void AssertMultiOpPackageRejected(MetalTestContext context)
    {
        RHIMLTensorDescriptor tensorDescriptor = CreateTensorDescriptor(2, 3);
        RHIMLOpDescriptor addOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.ElementWiseAdd,
            new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
            tensorDescriptor,
            "Add");
        RHIMLOpDescriptor reluOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.ActivationRelu,
            new[] { RHIMLOpTensorRef.FromOpOutput(0) },
            tensorDescriptor,
            "Relu");

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => context.Device.CreateMLProgram(RHIMLProgramDescriptor.Create(
            "MetalML.MultiOp.Rejected",
            new[] { tensorDescriptor, tensorDescriptor },
            new[] { tensorDescriptor },
            new[] { addOp, reluOp })));
        Assert.Contains("Multi-op packages", exception.Message);
        Assert.Contains("CPU fallbacks", exception.Message);
    }

    private static void AssertClose(float[] expected, float[] actual, string name)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            Assert.True(Math.Abs(expected[i] - actual[i]) <= 1e-4f,
                $"{name} mismatch at {i}: expected {expected[i]}, got {actual[i]}.");
        }
    }

    private static RHIMLTensorDescriptor CreateTensorDescriptor(params uint[] dimensions)
    {
        return new RHIMLTensorDescriptor
        {
            DataType = ERHIMLDataType.Float32,
            UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
            StorageMode = ERHIStorageMode.GPULocal,
            Dimensions = dimensions,
        };
    }

    private static RHIMLTensorDescriptor WithBacking(RHIMLTensorDescriptor descriptor, RHIBuffer backing)
    {
        descriptor.BackingBuffer = backing;
        descriptor.BackingBufferOffset = 0;
        return descriptor;
    }

    private static RHIBuffer CreateGpuBacking(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
        });
    }

    private static RHIBuffer CreateUpload(RHIDevice device, byte[] data)
    {
        RHIBuffer buffer = device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = data.Length,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.HostUpload,
            UsageFlag = ERHIBufferUsage.CopySrc,
        });
        WriteBytes(buffer, data);
        return buffer;
    }

    private static RHIBuffer CreateReadback(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.Readback,
            UsageFlag = ERHIBufferUsage.CopyDst,
        });
    }

    private static byte[] ToBytes(float[] values) => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();

    private static byte[] Pack(RHIMLTensorDescriptor descriptor, byte[] contiguous)
    {
        byte[] native = new byte[checked((int)MetalTensor.CalculateNativeBufferByteLength(descriptor))];
        MetalTensor.PackContiguousToNative(contiguous, native, descriptor);
        return native;
    }

    private static byte[] Unpack(RHIMLTensorDescriptor descriptor, byte[] native)
    {
        byte[] contiguous = new byte[checked((int)RHIMLHelpers.CalculateMinimumByteLength(descriptor))];
        MetalTensor.UnpackNativeToContiguous(native, contiguous, descriptor);
        return contiguous;
    }

    private static void WriteBytes(RHIBuffer buffer, ReadOnlySpan<byte> data)
    {
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(data.ToArray(), 0, pointer, data.Length);
        }
        finally
        {
            buffer.UnMap(0, checked((uint)data.Length));
        }
    }

    private static byte[] ReadbackBytes(RHIBuffer buffer, int byteCount)
    {
        byte[] bytes = new byte[byteCount];
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(pointer, bytes, 0, byteCount);
        }
        finally
        {
            buffer.UnMap(0, checked((uint)byteCount));
        }

        return bytes;
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
