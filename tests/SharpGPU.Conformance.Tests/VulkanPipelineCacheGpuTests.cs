using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using SharpShader.HLSLCrossCompiler;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanPipelineCacheGpuTests
{
    private const string ComputeShader = """
        [numthreads(1, 1, 1)]
        void main(uint3 dispatchThreadId : SV_DispatchThreadID)
        {
        }
        """;

    [Trait("Category", "SharpGpuVulkanQualified")]
    [Fact]
    public void PipelineCache_ColdPopulateAndRestartImport_UsesVkPipelineCache()
    {
        if (!OperatingSystem.IsWindows()
            && !OperatingSystem.IsLinux()
            && !OperatingSystem.IsAndroid())
        {
            // Vulkan qualification is not applicable to the Metal-only Apple gates.
            return;
        }

        using (RHIInstance instance = RHIInstance.Create(
                   new RHIInstanceDescriptor
                   {
                       Backend = ERHIBackend.Vulkan,
                       SurfaceKind = GetVulkanSurfaceKind(),
                       EnableDebugLayer = false,
                       EnableValidation = false,
                       GraphicsQueueRequestCount = 1,
                   }))
        {
            Assert.True(instance.DeviceCount > 0);
            RHIDevice device = instance.GetDevice(0);
            using RHIFunction function = CompileComputeFunction(device);
            using RHIPipelineLayout layout = device.CreatePipelineLayout(
                new RHIPipelineLayoutDescriptor
                {
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                });
            RHIComputePipelineDescriptor descriptor = new()
            {
                ThreadSize = new uint3(1, 1, 1),
                ComputeFunction = function,
                PipelineLayout = layout,
            };

            byte[] restartBlob;
            using (VulkanPipelineCache coldCache =
                   Assert.IsType<VulkanPipelineCache>(device.CreatePipelineCache()))
            {
                using RHIComputePipeline coldPipeline =
                    coldCache.CreateComputePipeline(descriptor);
                Assert.IsType<VulkanComputePipeline>(coldPipeline);

                restartBlob = coldCache.Export();
                Assert.NotEmpty(restartBlob);
            }

            using VulkanPipelineCache restartCache =
                Assert.IsType<VulkanPipelineCache>(device.CreatePipelineCache());
            RHIPipelineCacheImportResult importResult =
                restartCache.Import(restartBlob);
            Assert.Equal(ERHIPipelineCacheImportStatus.Loaded, importResult.Status);

            using RHIComputePipeline warmPipeline =
                restartCache.CreateComputePipeline(descriptor);
            Assert.IsType<VulkanComputePipeline>(warmPipeline);
        }
    }

    private static ERHINativeSurfaceKind GetVulkanSurfaceKind()
    {
        if (OperatingSystem.IsWindows())
        {
            return ERHINativeSurfaceKind.Win32Hwnd;
        }
        if (OperatingSystem.IsLinux())
        {
            return string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                ? ERHINativeSurfaceKind.X11Window
                : ERHINativeSurfaceKind.WaylandSurface;
        }
        if (OperatingSystem.IsAndroid())
        {
            return ERHINativeSurfaceKind.AndroidNativeWindow;
        }
        throw new PlatformNotSupportedException();
    }

    private static RHIFunction CompileComputeFunction(RHIDevice device)
    {
        ShaderCompileResult result = HLSLCrossCompiler.Compile(
            new ShaderCompileRequest
            {
                Source = ComputeShader,
                EntryPoint = "main",
                SourceName = "VulkanPipelineCacheGpuTests.hlsl",
                Stage = ShaderStageKind.Compute,
                ShaderModel = new ShaderModelVersion(6, 6),
                Target = ShaderTargetKind.SpirV,
            });
        Assert.NotEmpty(result.Bytecode);

        IntPtr pointer = Marshal.AllocHGlobal(result.Bytecode.Length);
        try
        {
            Marshal.Copy(result.Bytecode, 0, pointer, result.Bytecode.Length);
            return device.CreateFunction(
                new RHIFunctionDescriptor
                {
                    ByteSize = checked((uint)result.Bytecode.Length),
                    ByteCode = pointer,
                    EntryName = "main",
                    Type = ERHIFunctionType.Compute,
                    PayloadKind = ERHIShaderPayloadKind.SpirV,
                });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
