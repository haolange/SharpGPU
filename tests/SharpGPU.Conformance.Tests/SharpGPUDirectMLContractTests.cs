#if SHARPGPU_ENABLE_DX12
using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUDirectMLContractTests
{
    private const float Epsilon = 1e-5f;

    [Fact]
    public void Dx12_DirectML_GenericBuilder_AddSigmoid_EndToEnd_ShouldMatchCpuReference()
    {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
            if (context == null)
            {
                return;
            }

            // A 2-op program cooked to DirectMLProgramV1 binary: ElementWiseAdd(a,b) -> intermediate
            // -> Sigmoid -> output. Verifies CreateMLPipeline(binary) end-to-end (ADR-0052).
            uint[] dims = { 1, 1, 2, 3 };
            float[] a = { 0.5f, -1.0f, 2.0f, -3.0f, 1.0f, 0.0f };
            float[] b = { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.0f };
            float[] expected = new float[a.Length];
            for (int i = 0; i < a.Length; ++i)
            {
                expected[i] = 1.0f / (1.0f + MathF.Exp(-(a[i] + b[i])));
            }

            RHIMLTensorDescriptor aLayout = CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
            RHIMLTensorDescriptor bLayout = CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
            RHIMLTensorDescriptor outLayout = CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write);
            RHIMLTensorDescriptor intermediateLayout = new RHIMLTensorDescriptor
            {
                DataType = ERHIMLDataType.Float32,
                UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
                StorageMode = ERHIStorageMode.GPULocal,
                Dimensions = dims,
                Strides = null,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };

            RHIMLOpDescriptor addOp = RHIMLOpDescriptor.Create(
                ERHIMLOpKind.ElementWiseAdd,
                new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
                intermediateLayout,
                "Add");
            RHIMLOpDescriptor sigmoidOp = RHIMLOpDescriptor.Create(
                ERHIMLOpKind.ActivationSigmoid,
                new[] { RHIMLOpTensorRef.FromOpOutput(0) },
                outLayout,
                "Sigmoid");

            RHIMLProgramIR programIr = RHIMLProgramIR.Create(
                "DirectML.AddSigmoid",
                new[] { aLayout, bLayout },
                new[] { outLayout },
                new[] { addOp, sigmoidOp });
            RHIMLBinary binary = Dx12MlBinaryCodec.Pack(programIr);

            RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
            {
                Name = "DirectML.AddSigmoid.Pipeline",
                Binary = binary,
            });

            ulong byteSize = CalculateTensorByteLength(dims);
            RHIBuffer uploadA = CreateUploadBuffer(context.Device, checked((int)byteSize));
            RHIBuffer uploadB = CreateUploadBuffer(context.Device, checked((int)byteSize));
            RHIBuffer inputABacking = CreateTensorBackingBuffer(context.Device, checked((int)byteSize));
            RHIBuffer inputBBacking = CreateTensorBackingBuffer(context.Device, checked((int)byteSize));
            RHIBuffer outputBacking = CreateTensorBackingBuffer(context.Device, checked((int)byteSize));
            RHITensor inputA = context.Device.CreateTensor(CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read, inputABacking));
            RHITensor inputB = context.Device.CreateTensor(CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read, inputBBacking));
            RHITensor output = context.Device.CreateTensor(CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write, outputBacking));
            RHIBuffer readback = CreateReadbackBuffer(context.Device, checked((int)byteSize));

            RHIMLBindingTable bindingSet = context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
            {
                Pipeline = pipeline,
                Inputs = new[] { inputA, inputB },
                Outputs = new[] { output },
            });

            UploadFloats(uploadA, a);
            UploadFloats(uploadB, b);

            using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            commandBuffer.Begin("DirectML.AddSigmoid");
            RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Upload" });
            upload.Barriers(new[]
            {
                RHIBarrier.Buffer(inputABacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
                RHIBarrier.Buffer(inputBBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            });
            upload.CopyBufferToBuffer(uploadA, 0, inputABacking, 0, inputABacking.Descriptor.ByteSize);
            upload.CopyBufferToBuffer(uploadB, 0, inputBBacking, 0, inputBBacking.Descriptor.ByteSize);
            upload.Barriers(new[]
            {
                RHIBarrier.Buffer(inputABacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
                RHIBarrier.Buffer(inputBBacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
                RHIBarrier.Buffer(outputBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
            });
            commandBuffer.EndTransferPass();

            RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "AddSigmoid" });
            ml.SetPipeline(pipeline);
            ml.SetBindingTable(bindingSet);
            ml.Dispatch();
            commandBuffer.EndMLPass();

            RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Readback" });
            download.Barriers(new[]
            {
                RHIBarrier.Buffer(outputBacking, RHIBufferRange.Whole(), ERHIStageMask.MachineLearning, ERHIStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
            });
            download.CopyBufferToBuffer(outputBacking, 0, readback, 0, readback.Descriptor.ByteSize);
            commandBuffer.EndTransferPass();
            commandBuffer.End();

            context.Fence.Reset();
            context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: context.Fence));
            context.Fence.Wait();

            float[] actual = ReadBackFloats(readback, expected.Length);
            AssertFloatArraysEqual(expected, actual);

            readback.Dispose();
            output.Dispose();
            inputB.Dispose();
            inputA.Dispose();
            outputBacking.Dispose();
            inputBBacking.Dispose();
            inputABacking.Dispose();
            uploadB.Dispose();
            uploadA.Dispose();
            bindingSet.Dispose();
            pipeline.Dispose();
        }

    [Fact]
    public void Dx12_DirectML_TensorViewOffsetBind_ShouldDispatchAndReadback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
        if (context == null)
        {
            return;
        }

        uint[] dims = { 1, 1, 2, 3 };
        float[] a = { 0.25f, -0.5f, 1.5f, -2.0f, 0.75f, 0.0f };
        float[] b = { 0.75f, 0.5f, -0.5f, 2.0f, 0.25f, 1.0f };
        float[] expected = new float[a.Length];
        for (int i = 0; i < a.Length; ++i)
        {
            expected[i] = a[i] + b[i];
        }

        RHIMLTensorDescriptor layout = CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
        RHIMLTensorDescriptor outLayout = CreateTensorDescriptor(dims, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write);
        RHIMLOpDescriptor addOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.ElementWiseAdd,
            new[] { RHIMLOpTensorRef.FromInput(0), RHIMLOpTensorRef.FromInput(1) },
            outLayout,
            "Add");
        RHIMLProgramIR programIr = RHIMLProgramIR.Create(
            "DirectML.TensorViewAdd",
            new[] { layout, layout },
            new[] { outLayout },
            new[] { addOp });
        RHIMLBinary binary = Dx12MlBinaryCodec.Pack(programIr);
        RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "DirectML.TensorViewAdd.Pipeline",
            Binary = binary,
        });

        ulong tensorBytes = CalculateTensorByteLength(dims);
        // DirectML buffer bindings require an aligned base offset (use 256B pad).
        const ulong viewOffset = 256;
        int parentBytes = checked((int)(viewOffset + tensorBytes));

        RHIBuffer uploadA = CreateUploadBuffer(context.Device, parentBytes);
        RHIBuffer uploadB = CreateUploadBuffer(context.Device, parentBytes);
        RHIBuffer inputABacking = CreateTensorBackingBuffer(context.Device, parentBytes);
        RHIBuffer inputBBacking = CreateTensorBackingBuffer(context.Device, parentBytes);
        RHIBuffer outputBacking = CreateTensorBackingBuffer(context.Device, parentBytes);
        RHIBuffer readback = CreateReadbackBuffer(context.Device, parentBytes);

        // Parents span the full padded buffer; CreateView selects the aligned payload window.
        uint[] parentDims = { 1, 1, 1, (uint)(parentBytes / sizeof(float)) };
        RHITensor parentA = context.Device.CreateTensor(CreateTensorDescriptor(
            parentDims,
            ERHITensorUsage.MachineLearning | ERHITensorUsage.Read,
            inputABacking));
        RHITensor parentB = context.Device.CreateTensor(CreateTensorDescriptor(
            parentDims,
            ERHITensorUsage.MachineLearning | ERHITensorUsage.Read,
            inputBBacking));
        RHITensor parentOut = context.Device.CreateTensor(CreateTensorDescriptor(
            parentDims,
            ERHITensorUsage.MachineLearning | ERHITensorUsage.Write,
            outputBacking));

        using RHITensorView viewA = parentA.CreateView(new RHITensorViewDescriptor
        {
            Offset = viewOffset,
            Dimensions = dims,
        });
        using RHITensorView viewB = parentB.CreateView(new RHITensorViewDescriptor
        {
            Offset = viewOffset,
            Dimensions = dims,
        });
        using RHITensorView viewOut = parentOut.CreateView(new RHITensorViewDescriptor
        {
            Offset = viewOffset,
            Dimensions = dims,
        });
        Assert.Equal(viewOffset, viewA.Descriptor.BackingBufferOffset);
        Assert.Equal(viewOffset, viewB.Descriptor.BackingBufferOffset);
        Assert.Equal(viewOffset, viewOut.Descriptor.BackingBufferOffset);

        RHIMLBindingTable bindingTable = context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
        {
            Pipeline = pipeline,
            InputViews = new[] { viewA, viewB },
            OutputViews = new[] { viewOut },
        });

        // Upload into the view window (offset region) of each parent buffer.
        byte[] uploadBytesA = new byte[parentBytes];
        byte[] uploadBytesB = new byte[parentBytes];
        Buffer.BlockCopy(a, 0, uploadBytesA, checked((int)viewOffset), a.Length * sizeof(float));
        Buffer.BlockCopy(b, 0, uploadBytesB, checked((int)viewOffset), b.Length * sizeof(float));
        UploadBytes(uploadA, uploadBytesA);
        UploadBytes(uploadB, uploadBytesB);

        using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
        commandBuffer.Begin("DirectML.TensorViewAdd");
        RHITransferEncoder upload = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Upload" });
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputABacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(inputBBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
        });
        upload.CopyBufferToBuffer(uploadA, 0, inputABacking, 0, inputABacking.Descriptor.ByteSize);
        upload.CopyBufferToBuffer(uploadB, 0, inputBBacking, 0, inputBBacking.Descriptor.ByteSize);
        upload.Barriers(new[]
        {
            RHIBarrier.Buffer(inputABacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
            RHIBarrier.Buffer(inputBBacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
            RHIBarrier.Buffer(outputBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder ml = commandBuffer.BeginMLPass(new RHIMLPassDescriptor { Name = "TensorViewAdd" });
        ml.SetPipeline(pipeline);
        ml.SetBindingTable(bindingTable);
        ml.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder download = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor { Name = "Readback" });
        download.Barriers(new[]
        {
            RHIBarrier.Buffer(outputBacking, RHIBufferRange.Whole(), ERHIStageMask.MachineLearning, ERHIStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        download.CopyBufferToBuffer(outputBacking, 0, readback, 0, readback.Descriptor.ByteSize);
        commandBuffer.EndTransferPass();
        commandBuffer.End();

        context.Fence.Reset();
        context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        float[] actualFull = ReadBackFloats(readback, parentBytes / sizeof(float));
        float[] actual = new float[expected.Length];
        Array.Copy(actualFull, checked((int)(viewOffset / sizeof(float))), actual, 0, expected.Length);
        AssertFloatArraysEqual(expected, actual);

        bindingTable.Dispose();
        parentOut.Dispose();
        parentB.Dispose();
        parentA.Dispose();
        readback.Dispose();
        outputBacking.Dispose();
        inputBBacking.Dispose();
        inputABacking.Dispose();
        uploadB.Dispose();
        uploadA.Dispose();
        pipeline.Dispose();
    }

    [Fact]
    public void Dx12_NeuralCook_ElementWiseAdd_DmlbinFixture_ShouldCreateMLPipeline()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
        if (context == null)
        {
            return;
        }

        string? fixturePath = ResolveNeuralCookFixture("elementwise_add.dmlbin");
        Assert.True(
            fixturePath != null && File.Exists(fixturePath),
            "Missing NeuralCook DX12 fixture elementwise_add.dmlbin.");

        RHIMLBinary binary = Dx12MlBinaryCodec.Load(File.ReadAllBytes(fixturePath));
        Assert.Equal(ERHIMLBinaryFormat.DirectMLProgramV1, binary.Format);
        using RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "NeuralCook.elementwise_add",
            Binary = binary,
        });
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void Dx12_DirectML_GemmAddRelu_EndToEnd_ShouldMatchCpuReference()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
        if (context == null)
        {
            return;
        }

        float[] a =
        {
            1.0f, 2.0f, 3.0f,
            4.0f, 5.0f, 6.0f,
        };
        float[] b =
        {
            1.0f, 0.0f, -1.0f, 2.0f,
            0.5f, 1.0f, 0.0f, -0.5f,
            2.0f, -1.0f, 1.0f, 0.0f,
        };
        float[] c =
        {
            0.5f, -1.0f, 0.0f, 1.0f,
            -10.0f, 0.5f, 1.0f, -2.0f,
        };
        float[] expected = ComputeReference(a, b, c, m: 2, k: 3, n: 4);

        using DirectMLFixture fixture = CreateFixture(context);
        UploadFloats(fixture.UploadABacking, a);
        UploadFloats(fixture.UploadBBacking, b);
        UploadFloats(fixture.UploadCBacking, c);

        using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
        RecordEndToEndCommandBuffer(commandBuffer, fixture);

        context.Fence.Reset();
        context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
            new RHICommandBuffer[] { commandBuffer },
            completionFence: context.Fence));
        context.Fence.Wait();

        float[] actual = ReadBackFloats(fixture.ReadbackOutput, expected.Length);
        AssertFloatArraysEqual(expected, actual);
    }

    private static DirectMLFixture CreateFixture(DirectMLTestContext context)
    {
        uint[] aDimensions = { 1, 1, 2, 3 };
        uint[] bDimensions = { 1, 1, 3, 4 };
        uint[] cDimensions = { 1, 1, 2, 4 };
        uint[] outputDimensions = { 1, 1, 2, 4 };

        RHIMLTensorDescriptor aLayout = CreateTensorDescriptor(aDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
        RHIMLTensorDescriptor bLayout = CreateTensorDescriptor(bDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
        RHIMLTensorDescriptor cLayout = CreateTensorDescriptor(cDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
        RHIMLTensorDescriptor outputLayout = CreateTensorDescriptor(outputDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write);

        ulong aBytes = CalculateTensorByteLength(aDimensions);
        ulong bBytes = CalculateTensorByteLength(bDimensions);
        ulong cBytes = CalculateTensorByteLength(cDimensions);
        ulong outputBytes = CalculateTensorByteLength(outputDimensions);

        RHIMLBinary binary = CreateGemmReluBinary(aLayout, bLayout, cLayout, outputLayout);
        RHIMLPipeline pipeline = context.Device.CreateMLPipeline(new RHIMLPipelineDescriptor
        {
            Name = "DirectML.GemmAddRelu",
            Binary = binary,
        });

        RHIBuffer uploadABacking = CreateUploadBuffer(context.Device, checked((int)aBytes));
        RHIBuffer uploadBBacking = CreateUploadBuffer(context.Device, checked((int)bBytes));
        RHIBuffer uploadCBacking = CreateUploadBuffer(context.Device, checked((int)cBytes));
        RHIBuffer inputABacking = CreateTensorBackingBuffer(context.Device, checked((int)aBytes));
        RHIBuffer inputBBacking = CreateTensorBackingBuffer(context.Device, checked((int)bBytes));
        RHIBuffer inputCBacking = CreateTensorBackingBuffer(context.Device, checked((int)cBytes));
        RHIBuffer outputBacking = CreateTensorBackingBuffer(context.Device, checked((int)outputBytes));

        RHITensor inputATensor = context.Device.CreateTensor(CreateTensorDescriptor(aDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read, inputABacking));
        RHITensor inputBTensor = context.Device.CreateTensor(CreateTensorDescriptor(bDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read, inputBBacking));
        RHITensor inputCTensor = context.Device.CreateTensor(CreateTensorDescriptor(cDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read, inputCBacking));
        RHITensor outputTensor = context.Device.CreateTensor(CreateTensorDescriptor(outputDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write, outputBacking));
        RHIBuffer readbackOutput = CreateReadbackBuffer(context.Device, checked((int)outputBytes));

        RHITensor[] inputs = { inputATensor, inputBTensor, inputCTensor };
        RHITensor[] outputs = { outputTensor };
        RHIMLBindingTable bindingSet = context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
        {
            Pipeline = pipeline,
            Inputs = inputs,
            Outputs = outputs,
        });

        return new DirectMLFixture(
            pipeline,
            bindingSet,
            uploadABacking,
            uploadBBacking,
            uploadCBacking,
            inputABacking,
            inputBBacking,
            inputCBacking,
            outputBacking,
            inputATensor,
            inputBTensor,
            inputCTensor,
            outputTensor,
            readbackOutput);
    }

    private static void RecordEndToEndCommandBuffer(RHICommandBuffer commandBuffer, DirectMLFixture fixture)
    {
        commandBuffer.Begin("DirectML.EndToEnd");

        RHITransferEncoder uploadEncoder = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "Upload",
        });
        uploadEncoder.Barriers(new[]
        {
            RHIBarrier.Buffer(fixture.InputABacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(fixture.InputBBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
            RHIBarrier.Buffer(fixture.InputCBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.Transfer, ERHIAccessMask.None, ERHIAccessMask.TransferWrite),
        });
        uploadEncoder.CopyBufferToBuffer(fixture.UploadABacking, 0, fixture.InputABacking, 0, fixture.InputABacking.Descriptor.ByteSize);
        uploadEncoder.CopyBufferToBuffer(fixture.UploadBBacking, 0, fixture.InputBBacking, 0, fixture.InputBBacking.Descriptor.ByteSize);
        uploadEncoder.CopyBufferToBuffer(fixture.UploadCBacking, 0, fixture.InputCBacking, 0, fixture.InputCBacking.Descriptor.ByteSize);
        uploadEncoder.Barriers(new[]
        {
            RHIBarrier.Buffer(fixture.InputABacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
            RHIBarrier.Buffer(fixture.InputBBacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
            RHIBarrier.Buffer(fixture.InputCBacking, RHIBufferRange.Whole(), ERHIStageMask.Transfer, ERHIStageMask.MachineLearning, ERHIAccessMask.TransferWrite, ERHIAccessMask.ShaderWrite),
            RHIBarrier.Buffer(fixture.OutputBacking, RHIBufferRange.Whole(), ERHIStageMask.None, ERHIStageMask.MachineLearning, ERHIAccessMask.None, ERHIAccessMask.ShaderWrite),
        });
        commandBuffer.EndTransferPass();

        RHIMLEncoder mlEncoder = commandBuffer.BeginMLPass(new RHIMLPassDescriptor
        {
            Name = "DirectML",
        });
        mlEncoder.SetPipeline(fixture.Pipeline);
        mlEncoder.SetBindingTable(fixture.BindingSet);
        mlEncoder.Dispatch();
        commandBuffer.EndMLPass();

        RHITransferEncoder readbackEncoder = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
        {
            Name = "Readback",
        });
        readbackEncoder.Barriers(new[]
        {
            RHIBarrier.Buffer(fixture.OutputBacking, RHIBufferRange.Whole(), ERHIStageMask.MachineLearning, ERHIStageMask.Transfer, ERHIAccessMask.ShaderWrite, ERHIAccessMask.TransferRead),
        });
        readbackEncoder.CopyBufferToBuffer(fixture.OutputBacking, 0, fixture.ReadbackOutput, 0, fixture.ReadbackOutput.Descriptor.ByteSize);
        commandBuffer.EndTransferPass();

        commandBuffer.End();
    }

    private static string? ResolveNeuralCookFixture(string fixtureFileName)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string[] candidates =
            [
                Path.Combine(dir.FullName, "TestData", "NeuralCook", fixtureFileName),
                Path.Combine(
                    dir.FullName,
                    "Engine",
                    "Source",
                    "Developer",
                    "Tests",
                    "TestData",
                    "NeuralCook",
                    fixtureFileName),
                Path.Combine(
                    dir.FullName,
                    "Source",
                    "Developer",
                    "Tests",
                    "TestData",
                    "NeuralCook",
                    fixtureFileName),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static RHIMLBinary CreateGemmReluBinary(
        in RHIMLTensorDescriptor aLayout,
        in RHIMLTensorDescriptor bLayout,
        in RHIMLTensorDescriptor cLayout,
        in RHIMLTensorDescriptor outputLayout)
    {
        RHIMLTensorDescriptor intermediateLayout = new RHIMLTensorDescriptor
        {
            DataType = outputLayout.DataType,
            UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
            StorageMode = ERHIStorageMode.GPULocal,
            Dimensions = outputLayout.Dimensions.ToArray(),
        };

        RHIMLOpDescriptor gemmOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.GeneralMatrixMultiply,
            new[]
            {
                RHIMLOpTensorRef.FromInput(0),
                RHIMLOpTensorRef.FromInput(1),
                RHIMLOpTensorRef.FromInput(2),
            },
            intermediateLayout,
            "DirectML.Gemm");
        gemmOp.Alpha = 1.0f;
        gemmOp.Beta = 1.0f;

        RHIMLOpDescriptor reluOp = RHIMLOpDescriptor.Create(
            ERHIMLOpKind.ActivationRelu,
            new[] { RHIMLOpTensorRef.FromOpOutput(0) },
            outputLayout,
            "DirectML.Relu");

        RHIMLProgramIR programIr = RHIMLProgramIR.Create(
            "DirectML.GemmAddRelu",
            new[] { aLayout, bLayout, cLayout },
            new[] { outputLayout },
            new[] { gemmOp, reluOp });
        return Dx12MlBinaryCodec.Pack(programIr);
    }

    private static RHIMLTensorDescriptor CreateTensorDescriptor(uint[] dimensions, ERHITensorUsage usage, RHIBuffer? backingBuffer = null)
    {
        return new RHIMLTensorDescriptor
        {
            DataType = ERHIMLDataType.Float32,
            UsageFlag = usage,
            StorageMode = ERHIStorageMode.GPULocal,
            Dimensions = dimensions,
            BackingBuffer = backingBuffer,
            BackingBufferOffset = 0,
        };
    }

    private static RHIBuffer CreateTensorBackingBuffer(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
        });
    }

    private static RHIBuffer CreateUploadBuffer(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.HostUpload,
            UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.ShaderResource,
        });
    }

    private static RHIBuffer CreateReadbackBuffer(RHIDevice device, int byteSize)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            StorageMode = ERHIStorageMode.Readback,
            UsageFlag = ERHIBufferUsage.CopyDst,
        });
    }

    private static void UploadFloats(RHIBuffer buffer, float[] data)
    {
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            buffer.UnMap(0, checked((uint)(data.Length * sizeof(float))));
        }
    }

    private static void UploadBytes(RHIBuffer buffer, byte[] data)
    {
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            buffer.UnMap(0, checked((uint)data.Length));
        }
    }

    private static float[] ReadBackFloats(RHIBuffer buffer, int elementCount)
    {
        float[] result = new float[elementCount];
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(pointer, result, 0, elementCount);
        }
        finally
        {
            buffer.UnMap(0, 0);
        }

        return result;
    }

    private static float[] ReadBackFloatsAtOffset(RHIBuffer buffer, int byteOffset, int elementCount)
    {
        float[] result = new float[elementCount];
        IntPtr pointer = buffer.Map(0, 0);
        try
        {
            Marshal.Copy(IntPtr.Add(pointer, byteOffset), result, 0, elementCount);
        }
        finally
        {
            buffer.UnMap(0, 0);
        }

        return result;
    }

    private static void AssertFloatArraysEqual(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            float delta = Math.Abs(expected[i] - actual[i]);
            Assert.True(delta <= Epsilon, $"Mismatch at index {i}. expected={expected[i]}, actual={actual[i]}, delta={delta}.");
        }
    }

    private static float[] ComputeReference(float[] a, float[] b, float[] c, int m, int k, int n)
    {
        float[] output = new float[m * n];
        for (int row = 0; row < m; ++row)
        {
            for (int col = 0; col < n; ++col)
            {
                float value = c[row * n + col];
                for (int inner = 0; inner < k; ++inner)
                {
                    value += a[row * k + inner] * b[inner * n + col];
                }

                output[row * n + col] = Math.Max(0.0f, value);
            }
        }

        return output;
    }

    private static ulong CalculateTensorByteLength(uint[] dimensions)
    {
        ulong elementCount = 1;
        for (int i = 0; i < dimensions.Length; ++i)
        {
            elementCount *= dimensions[i];
        }

        return elementCount * sizeof(float);
    }

    private sealed class DirectMLTestContext : IDisposable
    {
        public RHIInstance Instance { get; }
        public RHIDevice Device { get; }
        public RHICommandQueue CommandQueue { get; }
        public RHIFence Fence { get; }

        private DirectMLTestContext(RHIInstance instance, RHIDevice device, RHICommandQueue commandQueue, RHIFence fence)
        {
            Instance = instance;
            Device = device;
            CommandQueue = commandQueue;
            Fence = fence;
        }

        public static DirectMLTestContext? TryCreate()
        {
            if (!RHIInstance.IsBackendSupported(ERHIBackend.DirectX12, out _))
            {
                return null;
            }

            RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.DirectX12,
                EnableDebugLayer = false,
                EnableValidation = false,
                ComputeQueueRequestCount = 0,
                TransferQueueRequestCount = 0,
                GraphicsQueueRequestCount = 1,
            }) ?? throw new InvalidOperationException("Failed to create DX12 RHI instance.");

            RHIDevice? selectedDevice = null;
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice candidate = instance.GetDevice(i);
                if (candidate.Capabilities.MachineLearning.Execution.Tier != ERHICapabilityTier.Unavailable)
                {
                    selectedDevice = candidate;
                    break;
                }
            }

            if (selectedDevice == null)
            {
                instance.Dispose();
                return null;
            }

            RHICommandQueue commandQueue = selectedDevice.GetCommandQueue(ERHIPipelineType.Graphics, 0)
                ?? throw new InvalidOperationException("DX12 graphics queue is unavailable.");
            RHIFence fence = selectedDevice.CreateFence();
            return new DirectMLTestContext(instance, selectedDevice, commandQueue, fence);
        }

        public void Dispose()
        {
            Fence.Dispose();
            Instance.Dispose();
        }
    }

    private sealed class DirectMLFixture : IDisposable
    {
        public RHIMLPipeline Pipeline { get; }
        public RHIMLBindingTable BindingSet { get; }
        public RHIBuffer UploadABacking { get; }
        public RHIBuffer UploadBBacking { get; }
        public RHIBuffer UploadCBacking { get; }
        public RHIBuffer InputABacking { get; }
        public RHIBuffer InputBBacking { get; }
        public RHIBuffer InputCBacking { get; }
        public RHIBuffer OutputBacking { get; }
        public RHITensor InputATensor { get; }
        public RHITensor InputBTensor { get; }
        public RHITensor InputCTensor { get; }
        public RHITensor OutputTensor { get; }
        public RHIBuffer ReadbackOutput { get; }

        public DirectMLFixture(
            RHIMLPipeline pipeline,
            RHIMLBindingTable bindingSet,
            RHIBuffer uploadABacking,
            RHIBuffer uploadBBacking,
            RHIBuffer uploadCBacking,
            RHIBuffer inputABacking,
            RHIBuffer inputBBacking,
            RHIBuffer inputCBacking,
            RHIBuffer outputBacking,
            RHITensor inputATensor,
            RHITensor inputBTensor,
            RHITensor inputCTensor,
            RHITensor outputTensor,
            RHIBuffer readbackOutput)
        {
            Pipeline = pipeline;
            BindingSet = bindingSet;
            UploadABacking = uploadABacking;
            UploadBBacking = uploadBBacking;
            UploadCBacking = uploadCBacking;
            InputABacking = inputABacking;
            InputBBacking = inputBBacking;
            InputCBacking = inputCBacking;
            OutputBacking = outputBacking;
            InputATensor = inputATensor;
            InputBTensor = inputBTensor;
            InputCTensor = inputCTensor;
            OutputTensor = outputTensor;
            ReadbackOutput = readbackOutput;
        }

        public void Dispose()
        {
            ReadbackOutput.Dispose();
            OutputTensor.Dispose();
            InputCTensor.Dispose();
            InputBTensor.Dispose();
            InputATensor.Dispose();
            OutputBacking.Dispose();
            UploadCBacking.Dispose();
            UploadBBacking.Dispose();
            UploadABacking.Dispose();
            InputCBacking.Dispose();
            InputBBacking.Dispose();
            InputABacking.Dispose();
            BindingSet.Dispose();
            Pipeline.Dispose();
        }
    }
}
#endif
