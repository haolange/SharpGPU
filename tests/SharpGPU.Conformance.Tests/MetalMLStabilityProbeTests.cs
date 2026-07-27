// Copyright (c) CGBull. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpGPU.Conformance.Tests;

/// <summary>
/// Stability probe tests for Metal ML multi-dispatch corruption investigation.
/// Task: Isolate root cause of MTL4MachineLearningCommandEncoder dispatch pollution on macOS 26.5.
/// </summary>
[Trait("Category", "SharpGpuMetalQualified")]
public sealed class MetalMLStabilityProbeTests
{
    [Fact]
    public void ProbeTest_SingleDispatch_ElementWiseAdd_ShouldSucceed()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.MachineLearning.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            return;
        }

        Assert.True(
            TryCreatePipelineFromFixture(context, "probe_add_single.mtlmlbin", "Probe.Add.Pipeline", out RHIMLPipeline? pipeline),
            "Missing Metal ML fixture probe_add_single.mtlmlbin (SharpGPU.MLCook CoreML鈫抦tlmlbin).");

        using (pipeline)
        {
            RHIMLTensorDescriptor tensorDesc = CreateTensorDescriptor(2, 3);
            float[] result = ExecuteSingleDispatch(context, pipeline, tensorDesc,
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 10.0f, 20.0f, 30.0f, 40.0f, 50.0f, 60.0f });

            AssertClose(new[] { 11.0f, 22.0f, 33.0f, 44.0f, 55.0f, 66.0f }, result, "SingleDispatch.Add");
        }
    }

    [Fact]
    public void ProbeTest_MultiDispatch_SamePipeline_ElementWiseAdd_DetectCorruption()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.MachineLearning.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            return;
        }

        Assert.True(
            TryCreatePipelineFromFixture(context, "probe_add_multi.mtlmlbin", "Probe.Add.MultiDispatch", out RHIMLPipeline? pipeline),
            "Missing Metal ML fixture probe_add_multi.mtlmlbin (SharpGPU.MLCook CoreML鈫抦tlmlbin).");

        using (pipeline)
        {
            RHIMLTensorDescriptor tensorDesc = CreateTensorDescriptor(2, 3);

            float[] result1 = ExecuteSingleDispatch(context, pipeline, tensorDesc,
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 10.0f, 20.0f, 30.0f, 40.0f, 50.0f, 60.0f });

            AssertClose(new[] { 11.0f, 22.0f, 33.0f, 44.0f, 55.0f, 66.0f }, result1, "MultiDispatch.First");

            float[] result2 = ExecuteSingleDispatch(context, pipeline, tensorDesc,
                new[] { 2.0f, 4.0f, 6.0f, 8.0f, 10.0f, 12.0f },
                new[] { 100.0f, 200.0f, 300.0f, 400.0f, 500.0f, 600.0f });

            AssertClose(new[] { 102.0f, 204.0f, 306.0f, 408.0f, 510.0f, 612.0f }, result2, "MultiDispatch.Second");
        }
    }

    [Fact]
    public void ProbeTest_SingleDispatch_Gemm_WithIntermediatesHeap_ShouldSucceed()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.MachineLearning.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            return;
        }

        Assert.True(
            TryCreatePipelineFromFixture(context, "probe_gemm_single.mtlmlbin", "Probe.Gemm.Pipeline", out RHIMLPipeline? pipeline),
            "Missing Metal ML fixture probe_gemm_single.mtlmlbin (SharpGPU.MLCook CoreML鈫抦tlmlbin).");

        using (pipeline)
        {
            RHIMLTensorDescriptor aDesc = CreateTensorDescriptor(2, 3);
            RHIMLTensorDescriptor bDesc = CreateTensorDescriptor(3, 2);
            RHIMLTensorDescriptor cDesc = CreateTensorDescriptor(2, 2);
            RHIMLTensorDescriptor outDesc = CreateTensorDescriptor(2, 2);

            float[] result = ExecuteGemmDispatch(context, pipeline, aDesc, bDesc, cDesc, outDesc,
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 0.5f, 1.0f, 1.5f, 2.0f });

            AssertClose(new[] { 22.5f, 29.0f, 50.5f, 66.0f }, result, "SingleDispatch.Gemm");
        }
    }

    [Fact]
    public void ProbeTest_MultiDispatch_Gemm_DetectIntermediatesHeapCorruption()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using MetalTestContext context = MetalTestContext.Create();
        if (context.Device.Capabilities.MachineLearning.Execution.Tier == ERHICapabilityTier.Unavailable)
        {
            return;
        }

        Assert.True(
            TryCreatePipelineFromFixture(context, "probe_gemm_multi.mtlmlbin", "Probe.Gemm.MultiDispatch", out RHIMLPipeline? pipeline),
            "Missing Metal ML fixture probe_gemm_multi.mtlmlbin (SharpGPU.MLCook CoreML鈫抦tlmlbin).");

        using (pipeline)
        {
            RHIMLTensorDescriptor aDesc = CreateTensorDescriptor(2, 3);
            RHIMLTensorDescriptor bDesc = CreateTensorDescriptor(3, 2);
            RHIMLTensorDescriptor cDesc = CreateTensorDescriptor(2, 2);
            RHIMLTensorDescriptor outDesc = CreateTensorDescriptor(2, 2);

            float[] result1 = ExecuteGemmDispatch(context, pipeline, aDesc, bDesc, cDesc, outDesc,
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f },
                new[] { 0.5f, 1.0f, 1.5f, 2.0f });

            AssertClose(new[] { 22.5f, 29.0f, 50.5f, 66.0f }, result1, "MultiDispatch.Gemm.First");

            float[] result2 = ExecuteGemmDispatch(context, pipeline, aDesc, bDesc, cDesc, outDesc,
                new[] { 2.0f, 0.0f, 1.0f, 3.0f, 1.0f, 2.0f },
                new[] { 1.0f, 0.0f, 2.0f, 1.0f, 0.0f, 3.0f },
                new[] { 1.0f, 2.0f, 3.0f, 4.0f });

            AssertClose(new[] { 3.0f, 5.0f, 8.0f, 11.0f }, result2, "MultiDispatch.Gemm.Second");
        }
    }

    // Helper Methods

    private static bool TryCreatePipelineFromFixture(
        MetalTestContext context,
        string fixtureFileName,
        string pipelineName,
        [NotNullWhen(true)] out RHIMLPipeline? pipeline)
    {
        pipeline = null;
        string? fixturePath = ResolveMetalMlFixturePath(fixtureFileName);
        if (fixturePath == null)
        {
            return false;
        }

        RHIMLBinary binary = MetalMlBinaryCodec.Load(File.ReadAllBytes(fixturePath));
        pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = pipelineName,
            Binary = binary,
        });
        return true;
    }

    private static string? ResolveMetalMlFixturePath(string fixtureFileName)
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "MetalML", fixtureFileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "MetalML", fixtureFileName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestData", "SharpGPU.MLCook", fixtureFileName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TestData", "SharpGPU.MLCook", fixtureFileName)),
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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

    private static void AssertClose(float[] expected, float[] actual, string name)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            float diff = Math.Abs(expected[i] - actual[i]);
            Assert.True(diff <= 1e-4f,
                $"{name} mismatch at [{i}]: expected {expected[i]}, got {actual[i]}, diff {diff}.");
        }
    }

    private static byte[] ToBytes(float[] values) => MemoryMarshal.AsBytes(values.AsSpan()).ToArray();

    private static float[] FromBytes(byte[] bytes) => MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();

    private static RHIBuffer CreateGpuBacking(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst |
                       ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
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

    private static byte[] Pack(RHIMLTensorDescriptor descriptor, byte[] contiguous)
    {
        byte[] native = new byte[checked((int)MetalTensor.CalculateNativeBufferByteLength(descriptor))];
        MetalTensor.PackContiguousToNative(contiguous, native, descriptor);
        return native;
    }

    private static byte[] Unpack(RHIMLTensorDescriptor descriptor, byte[] native)
    {
        byte[] contiguous = new byte[GetContiguousByteLength(descriptor)];
        MetalTensor.UnpackNativeToContiguous(native, contiguous, descriptor);
        return contiguous;
    }

    private static int GetContiguousByteLength(RHIMLTensorDescriptor descriptor)
    {
        ulong elementCount = 1;
        foreach (uint dimension in descriptor.Dimensions.Span)
        {
            elementCount = checked(elementCount * Math.Max(1u, dimension));
        }

        uint elementSize = descriptor.DataType switch
        {
            ERHIMLDataType.Float32 => 4,
            ERHIMLDataType.Float16 => 2,
            ERHIMLDataType.BFloat16 => 2,
            ERHIMLDataType.Int32 => 4,
            ERHIMLDataType.Int16 => 2,
            ERHIMLDataType.Int8 => 1,
            ERHIMLDataType.UInt32 => 4,
            ERHIMLDataType.UInt16 => 2,
            ERHIMLDataType.UInt8 => 1,
            _ => throw new NotSupportedException($"Unsupported tensor data type '{descriptor.DataType}'."),
        };

        return checked((int)(elementCount * elementSize));
    }

    private static void WriteBytes(RHIBuffer buffer, byte[] data)
    {
        IntPtr mapped = buffer.Map(0, 0);
        Marshal.Copy(data, 0, mapped, data.Length);
        buffer.UnMap(0, checked((uint)data.Length));
    }

    private static byte[] ReadbackBytes(RHIBuffer buffer, int byteSize)
    {
        byte[] data = new byte[byteSize];
        IntPtr mapped = buffer.Map(0, 0);
        Marshal.Copy(mapped, data, 0, byteSize);
        buffer.UnMap(0, 0);
        return data;
    }

    private static float[] ExecuteSingleDispatch(
        MetalTestContext context,
        RHIMLPipeline pipeline,
        RHIMLTensorDescriptor tensorDesc,
        float[] inputAData,
        float[] inputBData)
    {
        int nativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(tensorDesc));

        using RHIBuffer inputA = CreateGpuBacking(context.Device, nativeByteSize);
        using RHIBuffer inputB = CreateGpuBacking(context.Device, nativeByteSize);
        using RHIBuffer output = CreateGpuBacking(context.Device, nativeByteSize);

        using RHITensor tensorA = context.Device.CreateTensor(WithBacking(tensorDesc, inputA));
        using RHITensor tensorB = context.Device.CreateTensor(WithBacking(tensorDesc, inputB));
        using RHITensor tensorOut = context.Device.CreateTensor(WithBacking(tensorDesc, output));

        using RHIMLBindingTable bindingSet = context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
        {
            Pipeline = pipeline,
            Inputs = new[] { tensorA, tensorB },
            Outputs = new[] { tensorOut },
        });

        using RHIBuffer uploadA = CreateUpload(context.Device, Pack(tensorDesc, ToBytes(inputAData)));
        using RHIBuffer uploadB = CreateUpload(context.Device, Pack(tensorDesc, ToBytes(inputBData)));
        using RHIBuffer readback = CreateReadback(context.Device, nativeByteSize);

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Probe.SingleDispatch");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Upload" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputA, 0, nativeByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputB, 0, nativeByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "ML" });
        ml.SetPipeline(pipeline);
        ml.SetBindingTable(bindingSet);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Readback" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHIStageMask.MachineLearning, ERHIStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(output, 0, readback, 0, nativeByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        return FromBytes(Unpack(tensorDesc, ReadbackBytes(readback, nativeByteSize)));
    }

    private static float[] ExecuteGemmDispatch(
        MetalTestContext context,
        RHIMLPipeline pipeline,
        RHIMLTensorDescriptor aDesc,
        RHIMLTensorDescriptor bDesc,
        RHIMLTensorDescriptor cDesc,
        RHIMLTensorDescriptor outDesc,
        float[] aData,
        float[] bData,
        float[] cData)
    {
        int aNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(aDesc));
        int bNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(bDesc));
        int cNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(cDesc));
        int outNativeByteSize = checked((int)MetalTensor.CalculateNativeBufferByteLength(outDesc));

        using RHIBuffer inputA = CreateGpuBacking(context.Device, aNativeByteSize);
        using RHIBuffer inputB = CreateGpuBacking(context.Device, bNativeByteSize);
        using RHIBuffer inputC = CreateGpuBacking(context.Device, cNativeByteSize);
        using RHIBuffer output = CreateGpuBacking(context.Device, outNativeByteSize);

        using RHITensor tensorA = context.Device.CreateTensor(WithBacking(aDesc, inputA));
        using RHITensor tensorB = context.Device.CreateTensor(WithBacking(bDesc, inputB));
        using RHITensor tensorC = context.Device.CreateTensor(WithBacking(cDesc, inputC));
        using RHITensor tensorOut = context.Device.CreateTensor(WithBacking(outDesc, output));

        using RHIMLBindingTable bindingSet = context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
        {
            Pipeline = pipeline,
            Inputs = new[] { tensorA, tensorB, tensorC },
            Outputs = new[] { tensorOut },
        });

        using RHIBuffer uploadA = CreateUpload(context.Device, Pack(aDesc, ToBytes(aData)));
        using RHIBuffer uploadB = CreateUpload(context.Device, Pack(bDesc, ToBytes(bData)));
        using RHIBuffer uploadC = CreateUpload(context.Device, Pack(cDesc, ToBytes(cData)));
        using RHIBuffer readback = CreateReadback(context.Device, outNativeByteSize);

        using RHICommandBuffer commandBuffer = context.Queue.CreateCommandBuffer();
        commandBuffer.Begin("Probe.GemmDispatch");

        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Upload" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputC, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputA, 0, aNativeByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputB, 0, bNativeByteSize);
        upload.CopyBufferToBuffer(uploadC, 0, inputC, 0, cNativeByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputA, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputB, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
            RHIBarrier.Buffer(inputC, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderRead),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "ML" });
        ml.SetPipeline(pipeline);
        ml.SetBindingTable(bindingSet);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Readback" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(output, RHIBufferRange.Whole(), ERHIStageMask.MachineLearning, ERHIStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(output, 0, readback, 0, outNativeByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.Queue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        return FromBytes(Unpack(outDesc, ReadbackBytes(readback, outNativeByteSize)));
    }
}
