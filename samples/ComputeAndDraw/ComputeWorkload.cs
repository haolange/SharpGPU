using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using SharpShader.Compilation;
using SharpShader.CSharp;
using SharpShader.SharpGPU;

namespace SharpGPU.Examples
{
    internal static class ComputeWorkload
    {
        private const string VariantKey = "default";
        private const string EntryPoint = "CSMain";
        private const uint ExpectedResult = 41;
        public static void Run()
        {
            CSharpShaderCompilation csharp = CSharpShaderCompiler.Shared.Compile(
                WriteConstantSource,
                "SharpSLWriteConstant.cs",
                new CSharpShaderCompilerOptions(
                    ShaderProgramTarget.DirectX12 | ShaderProgramTarget.Vulkan));
            ShaderProgramCompilation compilation = csharp.Primary.Program;
            Require(compilation.Artifacts.Count == 2, "Expected DX12 and Vulkan compiler artifacts.");

            bool executed = false;
            if (RHIInstance.IsBackendSupported(ERHIBackend.DirectX12, out _))
            {
                ExecuteBackend(compilation, ERHIBackend.DirectX12);
                executed = true;
            }

            if (RHIInstance.IsBackendSupported(ERHIBackend.Vulkan, out _))
            {
                ExecuteBackend(compilation, ERHIBackend.Vulkan);
                executed = true;
            }

            Require(
                executed,
                "Neither DX12 nor Vulkan is available for SharpSL GPU conformance.");
        }

        private static void ExecuteBackend(ShaderProgramCompilation compilation, ERHIBackend backend)
        {
            using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
            {
                Backend = backend,
                SurfaceKind = backend == ERHIBackend.Vulkan
                    ? ERHINativeSurfaceKind.Headless
                    : ERHINativeSurfaceKind.Headless,
                EnableDebugLayer = false,
                EnableValidation = false,
                GraphicsQueueRequestCount = 1,
            });
            Require(instance.DeviceCount > 0, backend + " exposed no devices.");
            RHIDevice device = instance.GetDevice(0);
            Console.WriteLine(backend + ": selected device='" + device.Name + "'.");

            using (device)
            {
                RHICommandQueue queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
                    ?? throw new InvalidOperationException(backend + " graphics queue is unavailable.");
                SharpGpuBindingTableLayoutPlan plan =
                    SharpGpuShaderInterfaceAdapter.CreateBindingTableLayoutPlan(
                        compilation.Manifest,
                        VariantKey,
                        EntryPoint,
                        ShaderExecutionStage.Compute,
                        backend);
                using SharpGpuBindingTableLayouts ownedLayouts =
                    plan.CreateBindingTableLayouts(device);
                using RHIPipelineLayout pipelineLayout = device.CreatePipelineLayout(
                    new RHIPipelineLayoutDescriptor
                    {
                        BindingTableLayouts = ownedLayouts.Layouts.ToArray(),
                    });
                using RHIFunction function = CreateFunction(device, compilation, backend);
                using RHIComputePipeline pipeline = device.CreateComputePipeline(
                    new RHIComputePipelineDescriptor
                    {
                        ThreadSize = new uint3(1, 1, 1),
                        ComputeFunction = function,
                        PipelineLayout = pipelineLayout,
                    });
                using RHIBuffer output = device.CreateBuffer(new RHIBufferDescriptor
                {
                    ByteSize = sizeof(uint),
                    Format = ERHIBufferFormat.Undefine,
                    UsageFlag = ERHIBufferUsage.UnorderedAccess | ERHIBufferUsage.CopySrc,
                    StorageMode = ERHIStorageMode.GPULocal,
                });
                using RHIBuffer readback = device.CreateBuffer(new RHIBufferDescriptor
                {
                    ByteSize = sizeof(uint),
                    Format = ERHIBufferFormat.Undefine,
                    UsageFlag = ERHIBufferUsage.CopyDst,
                    StorageMode = ERHIStorageMode.Readback,
                });
                using RHIBufferView outputView = output.CreateBufferView(new RHIBufferViewDescriptor
                {
                    Count = 1,
                    Stride = sizeof(uint),
                    ViewType = ERHIBufferViewType.UnorderedAccess,
                });
                using RHIBindingTable table = device.CreateBindingTable(new RHIBindingTableDescriptor
                {
                    Layout = ownedLayouts.Layouts[0],
                    Elements = new[]
                    {
                        new RHIBindingTableElement { BufferView = outputView },
                    },
                });
                using RHIFence fence = device.CreateFence();
                using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
                commandBuffer.Begin("SharpSL.WriteConstant." + backend);
                RHIComputeEncoder compute = commandBuffer.BeginComputePass(
                    new RHIComputePassDescriptor { Name = "Dispatch" });
                compute.Barrier(RHIBarrier.Buffer(
                    output,
                    RHIBufferRange.Whole(),
                    ERHIStageMask.None,
                    ERHIStageMask.Compute,
                    ERHIAccessMask.None,
                    ERHIAccessMask.ShaderWrite));
                compute.SetPipeline(pipeline);
                compute.SetBindingTable(table, 0);
                compute.Dispatch(1, 1, 1);
                commandBuffer.EndComputePass();
                RHITransferEncoder copy = commandBuffer.BeginTransferPass(
                    new RHITransferPassDescriptor { Name = "Readback" });
                copy.Barrier(RHIBarrier.Buffer(
                    output,
                    RHIBufferRange.Whole(),
                    ERHIStageMask.Compute,
                    ERHIStageMask.Transfer,
                    ERHIAccessMask.ShaderWrite,
                    ERHIAccessMask.TransferRead));
                copy.CopyBufferToBuffer(output, 0, readback, 0, sizeof(uint));
                commandBuffer.EndTransferPass();
                commandBuffer.End();
                fence.Reset();
                queue.Submit(new RHIQueueSubmitDescriptor(
                    new RHICommandBuffer[] { commandBuffer },
                    completionFence: fence));
                fence.Wait();
                IntPtr pointer = readback.Map(0, sizeof(uint));
                uint actual;
                try
                {
                    actual = unchecked((uint)Marshal.ReadInt32(pointer));
                }
                finally
                {
                    readback.UnMap(0, 0);
                }

                Require(actual == ExpectedResult, $"Readback mismatch: {actual} != {ExpectedResult}.");
                Console.WriteLine(backend + ": dispatch/readback=" + actual + ".");
            }
        }

        private static RHIFunction CreateFunction(
            RHIDevice device,
            ShaderProgramCompilation compilation,
            ERHIBackend backend)
        {
            ShaderArtifactKind artifactKind = backend == ERHIBackend.DirectX12
                ? ShaderArtifactKind.Dxil
                : ShaderArtifactKind.SpirV;
            ERHIShaderPayloadKind payloadKind = backend == ERHIBackend.DirectX12
                ? ERHIShaderPayloadKind.Dxil
                : ERHIShaderPayloadKind.SpirV;
            byte[] payload = compilation.GetArtifact(
                    VariantKey,
                    EntryPoint,
                    ShaderExecutionStage.Compute,
                    artifactKind)
                .Content
                .ToArray();
            IntPtr pointer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, pointer, payload.Length);
                return device.CreateFunction(new RHIFunctionDescriptor
                {
                    ByteSize = checked((uint)payload.Length),
                    ByteCode = pointer,
                    EntryName = EntryPoint,
                    Type = ERHIFunctionType.Compute,
                    PayloadKind = payloadKind,
                });
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private const string WriteConstantSource = @"
using SharpMath;
using SharpShader.CSharp.ShaderLib;

public static class WriteConstantShader
{
    [Binding(0, 0)]
    public static RWStructuredBuffer<uint> Output;

    [NumThreads(1, 1, 1)]
    [ComputeShader]
    public static void CSMain([SV.DispatchThreadID] uint3 tid)
    {
        Output.Store(0, 41u);
    }
}
";
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
