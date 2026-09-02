#if SHARPGPU_ENABLE_DX12
using Xunit;
using System;
using SharpGPU;
using SharpGPU.Mathematics;
using System.Runtime.InteropServices;
using SharpShader.HLSLCrossCompiler;

namespace SharpGPU.Conformance.Tests
{
    public sealed class MeshShadingWindowsQualifiedTests
    {
        private const uint RenderTargetExtent = 8;
        private const uint ReadbackRowPitch = 256;
        private const byte ExpectedRed = 255;
        private const byte ExpectedGreen = 0;
        private const byte ExpectedBlue = 255;
        private const byte ExpectedAlpha = 255;

        private const string TaskShaderSource = """
struct Payload
{
    uint Value;
};

groupshared Payload g_Payload;

[numthreads(1, 1, 1)]
void as_main(uint3 dispatchId : SV_DispatchThreadID)
{
    g_Payload.Value = 0;
    DispatchMesh(1, 1, 1, g_Payload);
}
""";

        private const string MeshShaderSource = """
struct MeshVertex
{
    float4 Position : SV_Position;
};

[outputtopology("triangle")]
[numthreads(1, 1, 1)]
void ms_main(out vertices MeshVertex verticesOut[3], out indices uint3 primitives[1])
{
    SetMeshOutputCounts(3, 1);
    verticesOut[0].Position = float4(-1.0, -1.0, 0.0, 1.0);
    verticesOut[1].Position = float4(-1.0, 3.0, 0.0, 1.0);
    verticesOut[2].Position = float4(3.0, -1.0, 0.0, 1.0);
    primitives[0] = uint3(0, 1, 2);
}
""";

        private const string MeshShaderWithPayloadSource = """
struct Payload
{
    uint Value;
};

struct MeshVertex
{
    float4 Position : SV_Position;
};

[outputtopology("triangle")]
[numthreads(1, 1, 1)]
void ms_main(
    in payload Payload payload,
    out vertices MeshVertex verticesOut[3],
    out indices uint3 primitives[1])
{
    SetMeshOutputCounts(3, 1);
    verticesOut[0].Position = float4(-1.0, -1.0, 0.0, 1.0);
    verticesOut[1].Position = float4(-1.0, 3.0, 0.0, 1.0);
    verticesOut[2].Position = float4(3.0, -1.0, 0.0, 1.0);
    primitives[0] = uint3(0, 1, 2);
}
""";

        private const string SolidPixelShader = """
float4 ps_main() : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);
}
""";

        [Fact]
        [Trait("Category", "SharpGpuWindowsQualified")]
        public void Dx12_MeshDispatch_ShouldWriteDeterministicPixel()
        {
            using FeatureContractContext context = RequireDx12();
            RHIDevice device = context.Device;
            if (device.Capabilities.Mesh.MeshShader.Tier == ERHICapabilityTier.Unavailable)
            {
                AssertMissingMeshFunctionFailsClosed(device);
                return;
            }

            bool useTask =
                device.Capabilities.Mesh.TaskShader.Tier != ERHICapabilityTier.Unavailable;
            using RHIPipelineLayout layout = device.CreatePipelineLayout(
                new RHIPipelineLayoutDescriptor
                {
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                });
            using RHIFunction? task = useTask
                ? CompileFunction(
                    device,
                    ERHIFunctionType.Task,
                    ShaderStageKind.Amplification,
                    "as_main",
                    TaskShaderSource,
                    ShaderTargetKind.Dxil)
                : null;
            using RHIFunction mesh = CompileFunction(
                device,
                ERHIFunctionType.Mesh,
                ShaderStageKind.Mesh,
                "ms_main",
                useTask ? MeshShaderWithPayloadSource : MeshShaderSource,
                ShaderTargetKind.Dxil);
            using RHIFunction pixel = CompileFunction(
                device,
                ERHIFunctionType.Fragment,
                ShaderStageKind.Pixel,
                "ps_main",
                SolidPixelShader,
                ShaderTargetKind.Dxil);
            using RHIRasterPipeline pipeline = device.CreateRasterPipeline(
                CreateMeshPipelineDescriptor(layout, mesh, pixel, task));
            using RHITexture target = CreateRenderTarget(device);
            using RHIBuffer readback = CreateReadbackBuffer(device);

            using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            commandBuffer.Begin("G3.Dx12.MeshDispatch");
            PrepareRenderTarget(commandBuffer, target);
            RHIRasterEncoder raster = commandBuffer.BeginRasterPass(new RHIRasterPassDescriptor
            {
                Name = "G3.Dx12.MeshDispatch",
                ArrayLength = 1,
                SampleCount = ERHISampleCount.None,
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
            raster.SetViewport(new Viewport(0, 0, RenderTargetExtent, RenderTargetExtent, 0, 1));
            raster.SetScissor(new Rect(0, 0, RenderTargetExtent, RenderTargetExtent));
            raster.DispatchMesh(1, 1, 1);
            commandBuffer.EndRasterPass();
            CopyTextureToReadback(commandBuffer, target, readback);
            commandBuffer.End();

            context.Fence.Reset();
            context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: context.Fence));
            context.Fence.Wait();

            byte[] pixelBytes = ReadFirstPixel(readback);
            Assert.Equal(ExpectedRed, pixelBytes[0]);
            Assert.Equal(ExpectedGreen, pixelBytes[1]);
            Assert.Equal(ExpectedBlue, pixelBytes[2]);
            Assert.Equal(ExpectedAlpha, pixelBytes[3]);
        }

        private static FeatureContractContext RequireDx12()
        {
            Assert.True(
                FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out string reason),
                $"DX12 is required for the Windows-qualified mesh gate: {reason}");
            Assert.NotNull(context);
            return context;
        }

        private static void AssertMissingMeshFunctionFailsClosed(RHIDevice device)
        {
            using RHIPipelineLayout layout = device.CreatePipelineLayout(
                new RHIPipelineLayoutDescriptor
                {
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                });
            Assert.ThrowsAny<Exception>(
                () => device.CreateRasterPipeline(
                    CreateMeshPipelineDescriptor(layout, meshFunction: null, pixel: null, taskFunction: null)));
        }

        private static RHIRasterPipelineDescriptor CreateMeshPipelineDescriptor(
            RHIPipelineLayout pipelineLayout,
            RHIFunction? meshFunction,
            RHIFunction? pixel,
            RHIFunction? taskFunction)
        {
            return new RHIRasterPipelineDescriptor
            {
                SampleCount = ERHISampleCount.None,
                DepthFormat = ERHIPixelFormat.Unknown,
                ColorFormats = new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
                PipelineLayout = pipelineLayout,
                FragmentFunction = pixel,
                PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
                {
                    PrimitiveTopology = ERHIPrimitiveTopology.TriangleList,
                    MeshletAssembler = new RHIMeshletAssemblerDescriptor(taskFunction, meshFunction),
                },
                RenderState = CreateDefaultRenderState(),
            };
        }

        private static RHITexture CreateRenderTarget(RHIDevice device)
        {
            return device.CreateTexture(new RHITextureDescriptor
            {
                MipCount = 1,
                Extent = new uint3(RenderTargetExtent, RenderTargetExtent, 1),
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHITextureUsage.RenderTarget | ERHITextureUsage.CopySrc,
                Dimension = ERHITextureDimension.Texture2D,
            });
        }

        private static RHIBuffer CreateReadbackBuffer(RHIDevice device)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = (int)(ReadbackRowPitch * RenderTargetExtent),
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = ERHIBufferUsage.CopyDst,
                StorageMode = ERHIStorageMode.Readback,
            });
        }

        private static void PrepareRenderTarget(RHICommandBuffer command, RHITexture texture)
        {
            RHITransferEncoder transfer = command.BeginTransferPass(
                new RHITransferPassDescriptor
                {
                    Name = "G3.PrepareRenderTarget",
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

        private static void CopyTextureToReadback(
            RHICommandBuffer command,
            RHITexture texture,
            RHIBuffer readback)
        {
            RHITransferEncoder transfer = command.BeginTransferPass(
                new RHITransferPassDescriptor
                {
                    Name = "G3.Mesh.Readback",
                });
            transfer.Barrier(RHIBarrier.Texture(
                texture,
                RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
                ERHITextureLayout.RenderTarget,
                ERHITextureLayout.CopySource,
                ERHIStageMask.Fragment,
                ERHIStageMask.Transfer,
                ERHIAccessMask.RenderTargetWrite,
                ERHIAccessMask.TransferRead));
            transfer.Barrier(RHIBarrier.Buffer(
                readback,
                RHIBufferRange.Whole(),
                ERHIStageMask.None,
                ERHIStageMask.Transfer,
                ERHIAccessMask.None,
                ERHIAccessMask.TransferWrite));
            transfer.CopyTextureToBuffer(
                new RHITextureCopyDescriptor
                {
                    Texture = texture,
                    MipLevel = 0,
                    SliceBase = 0,
                    SliceCount = 1,
                    Origin = new uint3(0, 0, 0),
                },
                new RHIBufferCopyDescriptor
                {
                    Buffer = readback,
                    Offset = 0,
                    RowPitch = ReadbackRowPitch,
                    TextureHeight = new uint3(RenderTargetExtent, RenderTargetExtent, 1),
                },
                new int3((int)RenderTargetExtent, (int)RenderTargetExtent, 1));
            command.EndTransferPass();
        }

        private static byte[] ReadFirstPixel(RHIBuffer readback)
        {
            IntPtr pointer = readback.Map(0, ReadbackRowPitch * RenderTargetExtent);
            try
            {
                return new[]
                {
                    Marshal.ReadByte(pointer, 0),
                    Marshal.ReadByte(pointer, 1),
                    Marshal.ReadByte(pointer, 2),
                    Marshal.ReadByte(pointer, 3),
                };
            }
            finally
            {
                readback.UnMap(0, 0);
            }
        }

        private static RHIFunction CompileFunction(
            RHIDevice device,
            ERHIFunctionType functionType,
            ShaderStageKind stage,
            string entryPoint,
            string source,
            ShaderTargetKind target)
        {
            ShaderCompileResult result = HLSLCrossCompiler.Compile(
                new ShaderCompileRequest
                {
                    Source = source,
                    SourceName = "MeshShadingWindowsQualifiedTests.hlsl",
                    EntryPoint = entryPoint,
                    Stage = stage,
                    ShaderModel = new ShaderModelVersion(6, 5),
                    Target = target,
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
