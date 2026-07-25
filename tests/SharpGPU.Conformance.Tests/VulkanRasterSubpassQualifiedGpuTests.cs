using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpGPU.Mathematics;
using SharpShader.HLSLCrossCompiler;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanRasterSubpassQualifiedGpuTests
{
#if SHARPGPU_VULKAN_QUALIFICATION_HOST
    [Fact]
    [Trait("Category", "SharpGpuVulkanQualified")]
    public void Rtx5090_ForcedDynamicAndRenderPass2_LocalReadPixelsMatchWithZeroVuid()
    {
        Assert.True(
            OperatingSystem.IsWindows() ||
            OperatingSystem.IsLinux() ||
            OperatingSystem.IsAndroid(),
            "SharpGpuVulkanQualified requires a Vulkan qualification host.");

        using RHIInstance instance = RHIInstance.Create(
            new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.Vulkan,
                SurfaceKind = GetVulkanSurfaceKind(),
                EnableDebugLayer = true,
                EnableValidation = true,
                GraphicsQueueRequestCount = 1,
            });
        Assert.True(instance.DeviceCount > 0);
        VulkanInstance vulkanInstance =
            Assert.IsType<VulkanInstance>(instance);
        Assert.True(
            vulkanInstance.HasDebugUtils,
            "The real Vulkan raster gate requires VK_EXT_debug_utils.");
        RHIDevice device = instance.GetDevice(0);
        VulkanDevice vulkanDevice =
            Assert.IsType<VulkanDevice>(device);
        Assert.Contains(
            "RTX 5090",
            device.Name,
            StringComparison.OrdinalIgnoreCase);

        Assert.True(
            vulkanDevice.SupportsDynamicRenderingLocalRead,
            "RTX 5090 qualification requires the exact " +
            "VK_KHR_dynamic_rendering_local_read feature.");
        Assert.True(
            vulkanDevice.SupportsRenderPass2,
            "RTX 5090 qualification requires RenderPass2.");
        Assert.True(
            vulkanDevice.SupportsAttachmentFeedbackLoopLayout,
            "RTX 5090 qualification requires the exact " +
            "VK_EXT_attachment_feedback_loop_layout feature.");
        Assert.True(
            vulkanDevice.SupportsFragmentShaderPixelInterlock,
            "RTX 5090 qualification requires the exact " +
            "VK_EXT_fragment_shader_interlock pixel feature.");
        Assert.True(
            vulkanDevice.DynamicRenderingLocalReadProvenance is
                EVulkanFeatureProvenance.Vulkan14Core or
                EVulkanFeatureProvenance.KhrExtension,
            "Dynamic-rendering local-read must come from Vulkan 1.4 core or " +
            "VK_KHR_dynamic_rendering_local_read.");
        Assert.Equal(
            EVulkanFeatureProvenance.ExtExtension,
            vulkanDevice.AttachmentFeedbackLoopLayoutProvenance);
        Assert.Equal(
            EVulkanFeatureProvenance.ExtExtension,
            vulkanDevice.FragmentShaderPixelInterlockProvenance);
        Assert.Equal(
            EVulkanFeatureProvenance.Vulkan12Core,
            vulkanDevice.RenderPass2Provenance);
        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            device.Capabilities.Raster.SampledFeedback.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            device.Capabilities.Raster.SampledFeedback.Provenance.Kind);
        Assert.Contains(
            "VK_EXT_attachment_feedback_loop_layout",
            device.Capabilities.Raster.SampledFeedback.Provenance.Source,
            StringComparison.Ordinal);

        using VulkanValidationCollector validation =
            VulkanValidationCollector.Attach(
                vulkanInstance.NativeInstance.Handle);

        byte[] dynamicPixels;
        byte[] renderPass2Pixels;
        try
        {
            dynamicPixels = RunLocalRead(
                device,
                EVulkanRasterPassForcedStrategy.DynamicRenderingLocalRead);
            renderPass2Pixels = RunLocalRead(
                device,
                EVulkanRasterPassForcedStrategy.NativeRenderPass2);
        }
        catch
        {
            validation.CompleteAndAssert(
                "failed forced local-read qualification");
            throw;
        }

        Assert.Equal(dynamicPixels, renderPass2Pixels);
        Assert.InRange(dynamicPixels[0], (byte)190, (byte)192);
        Assert.InRange(dynamicPixels[1], (byte)62, (byte)65);
        Assert.Equal((byte)0, dynamicPixels[2]);
        Assert.Equal(byte.MaxValue, dynamicPixels[3]);

        Assert.Equal(
            VkResult.Success,
            VulkanNative.vkDeviceWaitIdle(vulkanDevice.NativeDevice));
        validation.CompleteAndAssert(
            "forced dynamic-rendering local-read and RenderPass2 " +
            "pipeline/private-descriptor/draw/readback cleanup");
    }
#endif

    private static byte[] RunLocalRead(
        RHIDevice device,
        EVulkanRasterPassForcedStrategy forcedStrategy)
    {
        RHICommandQueue queue =
            device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
            ?? throw new InvalidOperationException(
                "The Vulkan graphics queue is unavailable.");
        using RHITexture first = CreateGpuTexture(device);
        using RHITexture second = CreateGpuTexture(device);
        using RHIBuffer readback = CreateReadbackBuffer(device);

        RHIRasterPassDescriptor descriptor = new()
        {
            Name = $"Vulkan.LocalRead.{forcedStrategy}",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(first),
                GpuAttachment(second),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(outputs: new[] { 0 }),
                SubPass(inputs: new[] { 0 }, outputs: new[] { 1 }),
            },
        };
        RasterPassPlan passPlan =
            RasterPassPlanner.Compile(in descriptor);
        using RHIPipelineLayout layout =
            device.CreatePipelineLayout(
                new RHIPipelineLayoutDescriptor
                {
                    ArgumentTableLayouts =
                        Array.Empty<RHIArgumentTableLayout>(),
                });
        using RHIFunction vertex = CompileFunction(
            device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        ERHIPixelFormat[] colorFormats =
        {
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHIPixelFormat.R8G8B8A8_UNorm,
        };
        using RHIRasterPipeline firstPipeline =
            CreateRasterPipeline(
                device,
                layout,
                vertex,
                passPlan.GetSubPass(0).AttachmentInterface,
                colorFormats,
                ConstantHalfRedShader,
                "ps_main");
        using RHIRasterPipeline secondPipeline =
            CreateRasterPipeline(
                device,
                layout,
                vertex,
                passPlan.GetSubPass(1).AttachmentInterface,
                colorFormats,
                LocalReadAddShader,
                "ps_main");
        using RHICommandBuffer command =
            queue.CreateCommandBuffer();
        using RHIFence fence = device.CreateFence();
        using IDisposable strategy =
            VulkanRasterStrategyDiagnostics.Push(forcedStrategy);

        command.Begin($"Vulkan.LocalRead.{forcedStrategy}");
        PrepareRenderTargets(command, first, second);
        RHIRasterEncoder raster =
            command.BeginRasterPass(in descriptor);
        SetFourByFourViewport(raster);
        raster.SetPipeline(firstPipeline);
        raster.Draw(3, 1, 0, 0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        raster.NextSubPass();
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);

        raster.SetPipeline(secondPipeline);
        raster.Draw(3, 1, 0, 0);
        raster.EndPass();
        CopyTextureToReadback(command, second, readback);
        command.End();

        fence.Reset();
        queue.Submit(
            new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { command },
                completionFence: fence));
        fence.Wait();
        return ReadFirstPixel(readback);
    }

    private static RHITexture CreateGpuTexture(RHIDevice device) =>
        device.CreateTexture(
            new RHITextureDescriptor
            {
                MipCount = 1,
                Extent = new uint3(4, 4, 1),
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag =
                    ERHITextureUsage.RenderTarget |
                    ERHITextureUsage.CopySrc,
                Dimension = ERHITextureDimension.Texture2D,
            });

    private static RHIColorAttachmentDescriptor GpuAttachment(
        RHITexture texture) =>
        new()
        {
            RenderTarget = texture,
            LoadAction = ERHILoadAction.Clear,
            StoreAction = ERHIStoreAction.Store,
            ClearValue = new float4(0, 0, 0, 1),
        };

    private static RHIBuffer CreateReadbackBuffer(RHIDevice device) =>
        device.CreateBuffer(
            new RHIBufferDescriptor
            {
                ByteSize = 256 * 4,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = ERHIBufferUsage.CopyDst,
                StorageMode = ERHIStorageMode.Readback,
            });

    private static RHISubPassDescriptor SubPass(
        int[]? inputs = null,
        int[]? outputs = null) =>
        new()
        {
            ColorInputs = inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
            SampledFeedbackInputs =
                RHIAttachmentIndexArray.Empty,
        };

    private static RHIRasterPipeline CreateRasterPipeline(
        RHIDevice device,
        RHIPipelineLayout layout,
        RHIFunction vertex,
        in RHIAttachmentInterfaceSignature attachmentInterface,
        ERHIPixelFormat[] colorFormats,
        string fragmentSource,
        string fragmentEntry)
    {
        using RHIFunction fragment = CompileFunction(
            device,
            ERHIFunctionType.Fragment,
            ShaderStageKind.Pixel,
            fragmentEntry,
            fragmentSource);
        return device.CreateRasterPipeline(
            new RHIRasterPipelineDescriptor
            {
                SampleCount = ERHISampleCount.None,
                DepthFormat = ERHIPixelFormat.Unknown,
                ColorFormats = colorFormats,
                AttachmentInterface = attachmentInterface,
                PipelineLayout = layout,
                FragmentFunction = fragment,
                PrimitiveAssembler =
                    new RHIPrimitiveAssemblerDescriptor
                    {
                        PrimitiveTopology =
                            ERHIPrimitiveTopology.TriangleList,
                        VertexAssembler =
                            new RHIVertexAssemblerDescriptor(
                                vertex,
                                Array.Empty<
                                    RHIVertexLayoutDescriptor>()),
                    },
                RenderState = CreateDefaultRenderState(),
            });
    }

    private static RHIFunction CompileFunction(
        RHIDevice device,
        ERHIFunctionType functionType,
        ShaderStageKind stage,
        string entryPoint,
        string source)
    {
        ShaderCompileResult result =
            HLSLCrossCompiler.Compile(
                new ShaderCompileRequest
                {
                    Source = source,
                    SourceName =
                        "VulkanRasterSubpassQualifiedGpuTests.hlsl",
                    EntryPoint = entryPoint,
                    Stage = stage,
                    ShaderModel = new ShaderModelVersion(6, 6),
                    Target = ShaderTargetKind.SpirV,
                });
        Assert.NotEmpty(result.Bytecode);

        IntPtr pointer =
            Marshal.AllocHGlobal(result.Bytecode.Length);
        try
        {
            Marshal.Copy(
                result.Bytecode,
                0,
                pointer,
                result.Bytecode.Length);
            return device.CreateFunction(
                new RHIFunctionDescriptor
                {
                    ByteCode = pointer,
                    ByteSize =
                        checked((uint)result.Bytecode.Length),
                    EntryName = entryPoint,
                    Type = functionType,
                    PayloadKind = ERHIShaderPayloadKind.SpirV,
                });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static RHIRenderStateDescriptor
        CreateDefaultRenderState()
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
                BlendDescriptor0 = new RHIBlendDescriptor
                {
                    BlendEnable = false,
                    BlendOpColor = ERHIBlendOp.Add,
                    SrcBlendColor = ERHIBlendMode.One,
                    DstBlendColor = ERHIBlendMode.Zero,
                    BlendOpAlpha = ERHIBlendOp.Add,
                    SrcBlendAlpha = ERHIBlendMode.One,
                    DstBlendAlpha = ERHIBlendMode.Zero,
                    ColorWriteChannel =
                        ERHIColorWriteChannel.All,
                },
            },
            RasterizerState =
                new RHIRasterizerStateDescriptor
                {
                    FillMode = ERHIFillMode.Solid,
                    CullMode = ERHICullMode.None,
                    DepthClipEnable = true,
                },
            DepthStencilState =
                new RHIDepthStencilStateDescriptor
                {
                    DepthEnable = false,
                    DepthWriteMask = false,
                    StencilEnable = false,
                    ComparisonMode =
                        ERHIComparisonMode.Always,
                    FrontFace = keep,
                    BackFace = keep,
                },
        };
    }

    private static void PrepareRenderTargets(
        RHICommandBuffer command,
        params RHITexture[] textures)
    {
        RHITransferEncoder transfer =
            command.BeginTransferPass(
                new RHITransferPassDescriptor
                {
                    Name = "PrepareRenderTargets",
                });
        foreach (RHITexture texture in textures)
        {
            transfer.Barrier(
                RHIBarrier.Texture(
                    texture,
                    RHITextureSubresourceRange.Whole(
                        ERHITextureAspectMask.Color),
                    ERHITextureLayout.Undefined,
                    ERHITextureLayout.RenderTarget,
                    ERHISyncStageMask.None,
                    ERHISyncStageMask.Fragment,
                    ERHIAccessMask.None,
                    ERHIAccessMask.RenderTargetRead |
                        ERHIAccessMask.RenderTargetWrite));
        }
        transfer.EndPass();
    }

    private static void CopyTextureToReadback(
        RHICommandBuffer command,
        RHITexture texture,
        RHIBuffer readback)
    {
        RHITransferEncoder transfer =
            command.BeginTransferPass(
                new RHITransferPassDescriptor
                {
                    Name = "Readback",
                });
        transfer.Barrier(
            RHIBarrier.Texture(
                texture,
                RHITextureSubresourceRange.Whole(
                    ERHITextureAspectMask.Color),
                ERHITextureLayout.RenderTarget,
                ERHITextureLayout.CopySource,
                ERHISyncStageMask.Fragment,
                ERHISyncStageMask.Transfer,
                ERHIAccessMask.RenderTargetWrite,
                ERHIAccessMask.TransferRead));
        transfer.Barrier(
            RHIBarrier.Buffer(
                readback,
                RHIBufferRange.Whole(),
                ERHISyncStageMask.None,
                ERHISyncStageMask.Transfer,
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
                RowPitch = 256,
                TextureHeight = new uint3(4, 4, 1),
            },
            new int3(4, 4, 1));
        transfer.EndPass();
    }

    private static void SetFourByFourViewport(
        RHIRasterEncoder raster)
    {
        raster.SetViewport(
            new Viewport(0, 0, 4, 4, 0, 1));
        raster.SetScissor(
            new Rect(0, 0, 4, 4));
    }

    private static byte[] ReadFirstPixel(RHIBuffer readback)
    {
        IntPtr pointer = readback.Map(0, 256 * 4);
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

    private static RHINativeSurfaceKind
        GetVulkanSurfaceKind()
    {
        if (OperatingSystem.IsWindows())
        {
            return RHINativeSurfaceKind.Win32Hwnd;
        }
        if (OperatingSystem.IsLinux())
        {
            return string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "WAYLAND_DISPLAY"))
                ? RHINativeSurfaceKind.X11Window
                : RHINativeSurfaceKind.WaylandSurface;
        }
        if (OperatingSystem.IsAndroid())
        {
            return RHINativeSurfaceKind.AndroidNativeWindow;
        }
        throw new PlatformNotSupportedException();
    }

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
    output.position =
        float4(positions[vertexId], 0.0, 1.0);
    return output;
}
""";

    private const string ConstantHalfRedShader = """
float4 ps_main() : SV_Target0
{
    return float4(0.5, 0.0, 0.0, 1.0);
}
""";

    private const string LocalReadAddShader = """
[[vk::binding(0, 0)]]
[[vk::input_attachment_index(0)]]
SubpassInput<float4> LocalColor;

float4 ps_main() : SV_Target0
{
    float4 value = LocalColor.SubpassLoad();
    return float4(
        value.r + 0.25,
        value.r * 0.5,
        0.0,
        1.0);
}
""";
}
