using Xunit;
using System;
using SharpGPU;
using System.Runtime.InteropServices;

namespace SharpGPU.Conformance.Tests
{
    public sealed class DirectMLBackendTests
    {
        private const float Epsilon = 1e-5f;

        [Fact]
        public void DirectML_GemmAddRelu_EndToEnd_ShouldMatchCpuReference()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
            Assert.NotNull(context);

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

            using DirectMLFixture fixture = CreateFixture(context!, contiguousOutput: true);
            UploadFloats(fixture.UploadABacking, a);
            UploadFloats(fixture.UploadBBacking, b);
            UploadFloats(fixture.UploadCBacking, c);

            using RHICommandBuffer commandBuffer = context!.CommandQueue.CreateCommandBuffer();
            RecordEndToEndCommandBuffer(commandBuffer, fixture);

            context.Fence.Reset();
            context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                new[] { commandBuffer }, completionFence: context.Fence));
            context.Fence.Wait();

            float[] actual = ReadBackFloats(fixture.ReadbackOutput, expected.Length);
            AssertFloatArraysEqual(expected, actual);
        }

        [Fact]
        public void DirectML_BindingSet_MissingInput_ShouldThrow()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
            Assert.NotNull(context);

            using DirectMLFixture fixture = CreateFixture(context!, contiguousOutput: true);
            RHITensor[] missingInputArray = { fixture.InputATensor, fixture.InputBTensor };
            RHITensor[] outputs = { fixture.OutputTensor };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                context!.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
                {
                    Pipeline = fixture.Pipeline,
                    Inputs = missingInputArray,
                    Outputs = outputs,
                }));

            Assert.Contains("binding count mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DirectML_TensorStrideAndShapeMismatch_ShouldThrow()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
            Assert.NotNull(context);

            using DirectMLFixture fixture = CreateFixture(context!, contiguousOutput: true);

            uint[] mismatchedOutputDimensions = { 1, 1, 2, 4 };
            uint[] mismatchedOutputStrides = { 12, 12, 6, 1 };
            int mismatchedOutputBytes = checked((int)CalculateTensorByteLength(mismatchedOutputDimensions, mismatchedOutputStrides));

            using RHIBuffer mismatchedOutputBacking = CreateTensorBackingBuffer(context!.Device, mismatchedOutputBytes);
            RHIMLTensorDescriptor mismatchedOutputDescriptor = CreateTensorDescriptor(
                mismatchedOutputDimensions,
                usage: ERHITensorUsage.MachineLearning | ERHITensorUsage.Write,
                backingBuffer: mismatchedOutputBacking,
                strides: mismatchedOutputStrides);
            using RHITensor mismatchedOutputTensor = context.Device.CreateTensor(mismatchedOutputDescriptor);

            RHITensor[] inputs = { fixture.InputATensor, fixture.InputBTensor, fixture.InputCTensor };
            RHITensor[] outputs = { mismatchedOutputTensor };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                context.Device.CreateMLBindingTable(new RHIMLBindingTableDescriptor
                {
                    Pipeline = fixture.Pipeline,
                    Inputs = inputs,
                    Outputs = outputs,
                }));

            Assert.Contains("layout mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MlExecutionApi_ProgramBindingSetDispatch_ShouldPreserveEncoderContract()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            using DirectMLTestContext? context = DirectMLTestContext.TryCreate();
            Assert.NotNull(context);

            using DirectMLFixture fixture = CreateFixture(context!, contiguousOutput: true);
            using RHICommandBuffer commandBuffer = context!.CommandQueue.CreateCommandBuffer();

            commandBuffer.Begin("DirectML.Contract");
            RHIMLEncoder mlEncoder = commandBuffer.BeginMLPass(new RHIMLPassDescriptor
            {
                Name = "Contract",
            });

            Exception missingPipeline = Assert.ThrowsAny<Exception>(() => mlEncoder.Dispatch());
            Assert.Contains("SetPipeline", missingPipeline.Message, StringComparison.OrdinalIgnoreCase);

            mlEncoder.SetPipeline(fixture.Pipeline);

            InvalidOperationException missingBindingSet = Assert.Throws<InvalidOperationException>(() => mlEncoder.Dispatch());
            Assert.Contains("SetBindingTable", missingBindingSet.Message, StringComparison.OrdinalIgnoreCase);

            mlEncoder.SetBindingTable(fixture.BindingSet);
            mlEncoder.Dispatch();

            commandBuffer.EndMLPass();
            commandBuffer.End();
        }

        private static DirectMLFixture CreateFixture(DirectMLTestContext context, bool contiguousOutput)
        {
            uint[] aDimensions = { 1, 1, 2, 3 };
            uint[] bDimensions = { 1, 1, 3, 4 };
            uint[] cDimensions = { 1, 1, 2, 4 };
            uint[] outputDimensions = { 1, 1, 2, 4 };
            uint[]? outputStrides = contiguousOutput ? null : new uint[] { 12, 12, 6, 1 };

            RHIMLTensorDescriptor aLayout = CreateTensorDescriptor(aDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
            RHIMLTensorDescriptor bLayout = CreateTensorDescriptor(bDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
            RHIMLTensorDescriptor cLayout = CreateTensorDescriptor(cDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Read);
            RHIMLTensorDescriptor outputLayout = CreateTensorDescriptor(
                outputDimensions,
                ERHITensorUsage.MachineLearning | ERHITensorUsage.Write,
                strides: outputStrides);

            ulong aBytes = CalculateTensorByteLength(aDimensions, null);
            ulong bBytes = CalculateTensorByteLength(bDimensions, null);
            ulong cBytes = CalculateTensorByteLength(cDimensions, null);
            ulong outputBytes = CalculateTensorByteLength(outputDimensions, outputStrides);

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
            RHITensor outputTensor = context.Device.CreateTensor(CreateTensorDescriptor(outputDimensions, ERHITensorUsage.MachineLearning | ERHITensorUsage.Write, outputBacking, outputStrides));

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

        private static RHIMLTensorDescriptor CreateTensorDescriptor(
            uint[] dimensions,
            ERHITensorUsage usage,
            RHIBuffer? backingBuffer = null,
            uint[]? strides = null)
        {
            return new RHIMLTensorDescriptor
            {
                DataType = ERHIMLDataType.Float32,
                UsageFlag = usage,
                StorageMode = ERHIStorageMode.GPULocal,
                Dimensions = dimensions,
                Strides = strides,
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

        private static ulong CalculateTensorByteLength(uint[] dimensions, uint[]? strides)
        {
            if (dimensions.Length == 0)
            {
                return 0;
            }

            if (strides != null)
            {
                if (strides.Length != dimensions.Length)
                {
                    throw new InvalidOperationException("Stride rank mismatch in test helper.");
                }

                ulong offsetInElements = 0;
                for (int i = 0; i < dimensions.Length; ++i)
                {
                    offsetInElements += (ulong)(dimensions[i] - 1) * strides[i];
                }

                return (offsetInElements + 1) * sizeof(float);
            }

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
}
