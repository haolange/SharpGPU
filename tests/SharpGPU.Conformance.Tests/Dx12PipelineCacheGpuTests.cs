#if SHARPGPU_ENABLE_DX12
using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using SharpShader.HLSLCrossCompiler;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12PipelineCacheGpuTests
{
    private const string ComputeShader = """
        [numthreads(1, 1, 1)]
        void main(uint3 dispatchThreadId : SV_DispatchThreadID)
        {
        }
        """;

    [Trait("Category", "SharpGpuWindowsQualified")]
    [Fact]
    public void PipelineCache_ColdPopulateAndRestartImport_UsesNativeDx12Cache()
    {
        bool initialized = FeatureContractContext.TryCreateDx12(
            out FeatureContractContext? context,
            out string reason);
        Assert.True(
            initialized,
            $"DX12 is required for the Windows-qualified pipeline-cache gate: {reason}");
        Assert.NotNull(context);

        using (context)
        {
            RHIDevice device = context.Device;
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
            using (Dx12PipelineCache coldCache =
                   Assert.IsType<Dx12PipelineCache>(device.CreatePipelineCache()))
            {
                using RHIComputePipeline coldPipeline =
                    coldCache.CreateComputePipeline(descriptor);
                Assert.IsType<Dx12ComputePipeline>(coldPipeline);
                Assert.Equal(0, coldCache.NativeHitCount);
                Assert.Equal(1, coldCache.NativeMissCount);
                Assert.Equal(1, coldCache.NativeStoreCount);

                restartBlob = coldCache.Export();
                Assert.NotEmpty(restartBlob);
            }

            using Dx12PipelineCache restartCache =
                Assert.IsType<Dx12PipelineCache>(device.CreatePipelineCache());
            RHIPipelineCacheImportResult importResult =
                restartCache.Import(restartBlob);
            Assert.Equal(ERHIPipelineCacheImportStatus.Loaded, importResult.Status);

            byte[] corruptBlob = (byte[])restartBlob.Clone();
            corruptBlob[^1] ^= 0x5a;
            RHIPipelineCacheImportResult corruptResult =
                restartCache.Import(corruptBlob);
            Assert.Equal(ERHIPipelineCacheImportStatus.Corrupt, corruptResult.Status);

            using RHIComputePipeline warmPipeline =
                restartCache.CreateComputePipeline(descriptor);
            Assert.IsType<Dx12ComputePipeline>(warmPipeline);
            Assert.Equal(1, restartCache.NativeHitCount);
            Assert.Equal(0, restartCache.NativeMissCount);
            Assert.Equal(0, restartCache.NativeStoreCount);
        }
    }

    private static RHIFunction CompileComputeFunction(RHIDevice device)
    {
        ShaderCompileResult result = HLSLCrossCompiler.Compile(
            new ShaderCompileRequest
            {
                Source = ComputeShader,
                EntryPoint = "main",
                SourceName = "Dx12PipelineCacheGpuTests.hlsl",
                Stage = ShaderStageKind.Compute,
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
                    ByteSize = checked((uint)result.Bytecode.Length),
                    ByteCode = pointer,
                    EntryName = "main",
                    Type = ERHIFunctionType.Compute,
                    PayloadKind = ERHIShaderPayloadKind.Dxil,
                });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
#endif
