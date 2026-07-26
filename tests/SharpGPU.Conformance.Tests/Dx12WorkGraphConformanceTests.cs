#if SHARPGPU_ENABLE_DX12
using System;
using SharpGPU;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using SharpShader.HLSLCrossCompiler;
using Xunit;
using Xunit.Abstractions;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12WorkGraphConformanceTests
{
    private const string ProgramName = "SharpGPU.WorkGraphSmoke";
    private const string EntryNodeName = "WorkNode";
    private const int InputRecordStride = 16;
    private const int ExpectedValue = 42;

    private readonly ITestOutputHelper m_Output;

    public Dx12WorkGraphConformanceTests(ITestOutputHelper output)
    {
        m_Output = output;
    }

    [Fact]
    public void Dx12_WorkGraph_GpuInputDispatch_ShouldWriteReadbackValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ResetTrace();
        TraceStep("start");

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });

        TraceStep("instance-created");
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0, "DX12 instance did not enumerate any adapters.");

        RHIDevice[] devices = Enumerable.Range(0, instance.DeviceCount)
            .Select(index => instance.GetDevice(index))
            .ToArray();
        SharpGPUFeatureReport[] reports = devices
            .Select((device, index) => SharpGPUFeatureReport.FromDevice(ERHIBackend.DirectX12, index, device))
            .ToArray();

        RHIDevice? rtx5090 = devices.FirstOrDefault(IsRtx5090);
        if (rtx5090 != null && rtx5090.Capabilities.WorkGraph.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            Assert.Fail("RTX 5090 adapter was found but DX12 WorkGraph is not exposed.\n" + BuildDiagnostic(reports, rtx5090));
        }

        RHIDevice? selectedDevice = rtx5090 ?? devices.FirstOrDefault(
            device => device.Capabilities.WorkGraph.Execution.Tier != ERHICapabilityTier.Unavailable);
        if (selectedDevice == null)
        {
            foreach (RHIDevice device in devices)
            {
                Assert.Throws<NotSupportedException>(() => device.CreateWorkGraphPipeline(default));
            }

            return;
        }

        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            selectedDevice.Capabilities.WorkGraph.Execution.Tier);
        TraceStep("device-selected");

        RHICommandQueue commandQueue = selectedDevice.GetCommandQueue(ERHIPipelineType.Graphics, 0)
            ?? throw new InvalidOperationException($"DX12 graphics queue is unavailable for {selectedDevice.Name}.");
        using RHIFence fence = selectedDevice.CreateFence();

        ShaderCompileResult shader = CompileWorkGraphShader(selectedDevice, reports);
        TraceStep("shader-compiled");
        using WorkGraphSmokeFixture fixture = CreateFixture(selectedDevice, shader.Bytecode);
        TraceStep("fixture-created");
        WriteInputRecord(fixture.InputRecordBuffer, ExpectedValue - 1);
        TraceStep("input-written");

        using RHICommandBuffer commandBuffer = commandQueue.CreateCommandBuffer();
        RecordWorkGraphSmoke(commandBuffer, fixture);
        TraceStep("command-recorded");

        fence.Reset();
        TraceStep("submit-begin");
        commandQueue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: fence));
        TraceStep("submit-end");
        WaitForFenceOrFail(fence, TimeSpan.FromSeconds(30), selectedDevice, reports);
        TraceStep("fence-complete");

        int actual = ReadBackInt32(fixture.ReadbackBuffer);
        TraceStep("readback-complete");
        Assert.Equal(ExpectedValue, actual);
    }

    private static WorkGraphSmokeFixture CreateFixture(RHIDevice device, byte[] dxilLibrary)
    {
        TraceStep("fixture-table-layout-begin");
        RHIBindingTableLayout tableLayout = device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
        {
            Index = 0,
            Elements = new[]
            {
                new RHIBindingTableLayoutElement
                {
                    Slot = 0,
                    Count = 1,
                    Type = ERHIBindType.StorageBuffer,
                    Stages = ERHIShaderStageMask.Compute,
                },
            },
        });
        TraceStep("fixture-table-layout-end");

        TraceStep("fixture-pipeline-layout-begin");
        RHIPipelineLayout pipelineLayout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
        {
            bLocalSignature = false,
            bUseVertexLayout = false,
            PushConstantSize = 0,
            BindingTableLayouts = new[] { tableLayout },
        });
        TraceStep("fixture-pipeline-layout-end");

        TraceStep("fixture-function-library-begin");
        RHIFunctionLibrary functionLibrary = CreateFunctionLibrary(device, dxilLibrary);
        TraceStep("fixture-function-library-end");
        TraceStep("fixture-workgraph-pipeline-begin");
        RHIWorkGraphPipeline pipeline = device.CreateWorkGraphPipeline(new RHIWorkGraphPipelineDescriptor
        {
            Name = ProgramName,
            FunctionLibrary = functionLibrary,
            PipelineLayout = pipelineLayout,
        });
        TraceStep("fixture-workgraph-pipeline-end");

        TraceStep("fixture-buffers-begin");
        int backingMemorySize = CalculateBackingMemoryByteSize(pipeline.MemoryRequirements);
        RHIBuffer backingMemory = CreateGpuBuffer(device, backingMemorySize, ERHIBufferUsage.UnorderedAccess);
        RHIBuffer inputRecordBuffer = device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = InputRecordStride,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.ShaderResource,
            StorageMode = ERHIStorageMode.HostUpload,
        });
        RHIBuffer outputBuffer = CreateGpuBuffer(device, sizeof(int), ERHIBufferUsage.UnorderedAccess | ERHIBufferUsage.CopySrc);
        RHIBufferView outputView = outputBuffer.CreateBufferView(new RHIBufferViewDescriptor
        {
            Count = 1,
            Offset = 0,
            Stride = sizeof(int),
            ViewType = ERHIBufferViewType.UnorderedAccess,
        });
        RHIBindingTable bindingTable = device.CreateBindingTable(new RHIBindingTableDescriptor
        {
            Layout = tableLayout,
            Elements = new[]
            {
                new RHIBindingTableElement
                {
                    BufferView = outputView,
                },
            },
        });
        RHIBuffer readbackBuffer = device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = sizeof(int),
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = ERHIBufferUsage.CopyDst,
            StorageMode = ERHIStorageMode.Readback,
        });
        TraceStep("fixture-buffers-end");

        return new WorkGraphSmokeFixture(
            tableLayout,
            pipelineLayout,
            functionLibrary,
            pipeline,
            bindingTable,
            outputView,
            backingMemory,
            inputRecordBuffer,
            outputBuffer,
            readbackBuffer,
            backingMemorySize);
    }

    private static void RecordWorkGraphSmoke(RHICommandBuffer commandBuffer, WorkGraphSmokeFixture fixture)
    {
        commandBuffer.Begin("SharpGPU.WorkGraphSmoke");

        RHIWorkGraphEncoder workGraph = commandBuffer.BeginWorkGraphPass(new RHIWorkGraphPassDescriptor
        {
            Name = "WorkGraph",
        });
        workGraph.SetPipeline(fixture.Pipeline);
        workGraph.SetBindingTable(fixture.BindingTable, 0);
        workGraph.Barrier(RHIBarrier.Buffer(
            fixture.BackingMemory,
            RHIBufferRange.Whole(),
            ERHISyncStageMask.None,
            ERHISyncStageMask.Compute,
            ERHIAccessMask.None,
            ERHIAccessMask.ShaderWrite));
        workGraph.Barrier(RHIBarrier.Buffer(
            fixture.OutputBuffer,
            RHIBufferRange.Whole(),
            ERHISyncStageMask.None,
            ERHISyncStageMask.Compute,
            ERHIAccessMask.None,
            ERHIAccessMask.ShaderWrite));
        workGraph.SetBackingMemory(fixture.BackingMemory, 0, (ulong)fixture.BackingMemorySize);
        workGraph.DispatchGraph(EntryNodeName, 1, InputRecordStride, fixture.InputRecordBuffer);
        commandBuffer.EndWorkGraphPass();

        RHITransferEncoder transfer = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "Readback",
        });
        transfer.Barrier(RHIBarrier.Buffer(
            fixture.OutputBuffer,
            RHIBufferRange.Whole(),
            ERHISyncStageMask.Compute,
            ERHISyncStageMask.Transfer,
            ERHIAccessMask.ShaderWrite,
            ERHIAccessMask.TransferRead));
        transfer.CopyBufferToBuffer(fixture.OutputBuffer, 0, fixture.ReadbackBuffer, 0, sizeof(int));
        commandBuffer.EndTransferPass();
        commandBuffer.End();
    }

    private static void WaitForFenceOrFail(RHIFence fence, TimeSpan timeout, RHIDevice selectedDevice, SharpGPUFeatureReport[] reports)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (fence.Status != EFenceStatus.Success)
        {
            if (stopwatch.Elapsed >= timeout)
            {
                string fenceDiagnostic = fence is Dx12Fence dx12Fence
                    ? $"CompletedValue={dx12Fence.NativeFence.CompletedValue}"
                    : $"Status={fence.Status}";
                TraceStep("fence-timeout");
                Assert.Fail($"DX12 WorkGraph smoke timed out waiting for GPU fence after {timeout.TotalSeconds:0}s. {fenceDiagnostic}\n" + BuildDiagnostic(reports, selectedDevice));
            }

            Thread.Sleep(1);
        }
    }

    private static void ResetTrace()
    {
        string path = ResolveTracePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private static void TraceStep(string step)
    {
        string path = ResolveTracePath();
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} {step}{Environment.NewLine}");
    }

    private static string ResolveTracePath()
    {
        return Path.Combine(Path.GetTempPath(), "SharpGPU", "workgraph-smoke-trace.txt");
    }

    private ShaderCompileResult CompileWorkGraphShader(RHIDevice selectedDevice, SharpGPUFeatureReport[] reports)
    {
        try
        {
            return HLSLCrossCompiler.Compile(new ShaderCompileRequest
            {
                Source = WorkGraphShaderSource,
                SourceName = "SharpGPU.WorkGraphSmoke.hlsl",
                EntryPoint = string.Empty,
                Stage = ShaderStageKind.Library,
                ShaderModel = new ShaderModelVersion(6, 8),
                Target = ShaderTargetKind.Dxil,
                OptimizationLevel = 3,
            });
        }
        catch (ShaderCompilerException ex)
        {
            Assert.Fail("DXC failed to compile the WorkGraph smoke shader.\n" + BuildDiagnostic(reports, selectedDevice) + "\nDiagnostics:\n" + ex.Diagnostics + "\n" + ex);
            throw;
        }
    }

    private static RHIBuffer CreateGpuBuffer(RHIDevice device, int byteSize, ERHIBufferUsage usage)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = usage,
            StorageMode = ERHIStorageMode.GPULocal,
        });
    }

    private static RHIFunctionLibrary CreateFunctionLibrary(RHIDevice device, byte[] bytecode)
    {
        IntPtr pointer = Marshal.AllocHGlobal(bytecode.Length);
        try
        {
            Marshal.Copy(bytecode, 0, pointer, bytecode.Length);
            return device.CreateFunctionLibrary(new RHIFunctionLibraryDescriptor
            {
                ByteSize = checked((uint)bytecode.Length),
                ByteCode = pointer,
                PayloadKind = ERHIShaderPayloadKind.Dxil,
            });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static int CalculateBackingMemoryByteSize(RHIWorkGraphMemoryRequirements requirements)
    {
        ulong size = requirements.MinSizeInBytes;
        if (size == 0)
        {
            size = Math.Max(64 * 1024UL, requirements.SizeGranularityInBytes);
        }

        if (requirements.SizeGranularityInBytes != 0)
        {
            ulong granularity = requirements.SizeGranularityInBytes;
            size = ((size + granularity - 1) / granularity) * granularity;
        }

        if (requirements.MaxSizeInBytes != 0 && size > requirements.MaxSizeInBytes)
        {
            size = requirements.MaxSizeInBytes;
        }

        if (size == 0 || size > int.MaxValue)
        {
            throw new InvalidOperationException($"Unsupported WorkGraph backing memory size: min={requirements.MinSizeInBytes}, max={requirements.MaxSizeInBytes}, granularity={requirements.SizeGranularityInBytes}.");
        }

        return checked((int)size);
    }

    private static void WriteInputRecord(RHIBuffer inputRecordBuffer, int value)
    {
        IntPtr pointer = inputRecordBuffer.Map(0, 0);
        try
        {
            Marshal.WriteInt32(pointer, value);
            Marshal.WriteInt32(pointer + 4, 0);
            Marshal.WriteInt32(pointer + 8, 0);
            Marshal.WriteInt32(pointer + 12, 0);
        }
        finally
        {
            inputRecordBuffer.UnMap(0, InputRecordStride);
        }
    }

    private static int ReadBackInt32(RHIBuffer readbackBuffer)
    {
        IntPtr pointer = readbackBuffer.Map(0, 0);
        try
        {
            return Marshal.ReadInt32(pointer);
        }
        finally
        {
            readbackBuffer.UnMap(0, 0);
        }
    }

    private static bool IsRtx5090(RHIDevice device)
    {
        return device.VendorId.IntValue == (uint)ERHIVendorType.Nvidia
            && (device.Name?.Contains("RTX 5090", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string BuildDiagnostic(SharpGPUFeatureReport[] reports, RHIDevice selectedDevice)
    {
        string reportText = JsonSerializer.Serialize(reports, JsonOptions.Indented);
        return $"SelectedAdapter={selectedDevice.Name}, Vendor={selectedDevice.VendorId.DecimalValue}, Device={selectedDevice.DeviceId.DecimalValue}\n{reportText}";
    }

    private const string WorkGraphShaderSource = """
struct InputRecord
{
    uint Value;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

RWStructuredBuffer<uint> Output : register(u0, space0);

[Shader("node")]
[NodeLaunch("thread")]
[NodeIsProgramEntry]
void WorkNode(ThreadNodeInputRecord<InputRecord> input)
{
    Output[0] = input.Get().Value + 1;
}
""";

    private sealed class WorkGraphSmokeFixture : IDisposable
    {
        public RHIBindingTableLayout TableLayout { get; }
        public RHIPipelineLayout PipelineLayout { get; }
        public RHIFunctionLibrary FunctionLibrary { get; }
        public RHIWorkGraphPipeline Pipeline { get; }
        public RHIBindingTable BindingTable { get; }
        public RHIBufferView OutputView { get; }
        public RHIBuffer BackingMemory { get; }
        public RHIBuffer InputRecordBuffer { get; }
        public RHIBuffer OutputBuffer { get; }
        public RHIBuffer ReadbackBuffer { get; }
        public int BackingMemorySize { get; }

        public WorkGraphSmokeFixture(
            RHIBindingTableLayout tableLayout,
            RHIPipelineLayout pipelineLayout,
            RHIFunctionLibrary functionLibrary,
            RHIWorkGraphPipeline pipeline,
            RHIBindingTable bindingTable,
            RHIBufferView outputView,
            RHIBuffer backingMemory,
            RHIBuffer inputRecordBuffer,
            RHIBuffer outputBuffer,
            RHIBuffer readbackBuffer,
            int backingMemorySize)
        {
            TableLayout = tableLayout;
            PipelineLayout = pipelineLayout;
            FunctionLibrary = functionLibrary;
            Pipeline = pipeline;
            BindingTable = bindingTable;
            OutputView = outputView;
            BackingMemory = backingMemory;
            InputRecordBuffer = inputRecordBuffer;
            OutputBuffer = outputBuffer;
            ReadbackBuffer = readbackBuffer;
            BackingMemorySize = backingMemorySize;
        }

        public void Dispose()
        {
            BindingTable.Dispose();
            OutputView.Dispose();
            Pipeline.Dispose();
            FunctionLibrary.Dispose();
            PipelineLayout.Dispose();
            TableLayout.Dispose();
            ReadbackBuffer.Dispose();
            OutputBuffer.Dispose();
            InputRecordBuffer.Dispose();
            BackingMemory.Dispose();
        }
    }
}
#endif
