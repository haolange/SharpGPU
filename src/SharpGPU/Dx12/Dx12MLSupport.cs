using System;
using Vortice.DirectML;

namespace Infinity.Graphics
{
    internal enum Dx12MLProgramKind : byte
    {
        GeneralMatrixMultiplyAddRelu = 0,
    }

    internal static class Dx12MLUtilities
    {
        internal static TensorDataType ConvertToDirectMLDataType(in ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => TensorDataType.Float32,
                ERHIMLDataType.Float16 => TensorDataType.Float16,
                ERHIMLDataType.Int32 => TensorDataType.Int32,
                ERHIMLDataType.Int16 => TensorDataType.Int16,
                ERHIMLDataType.Int8 => TensorDataType.Int8,
                ERHIMLDataType.UInt32 => TensorDataType.Uint32,
                ERHIMLDataType.UInt16 => TensorDataType.Uint16,
                ERHIMLDataType.UInt8 => TensorDataType.Uint8,
                _ => throw new NotSupportedException($"DX12 DirectML does not support ML data type '{dataType}'."),
            };
        }

        internal static TensorDescription CreateTensorDescription(in RHIMLTensorDescriptor descriptor)
        {
            BufferTensorDescription tensorDescription = new BufferTensorDescription
            {
                DataType = ConvertToDirectMLDataType(descriptor.DataType),
                Flags = TensorFlags.None,
                Sizes = descriptor.Dimensions.ToArray(),
                Strides = RHIMLHelpers.HasExplicitStrides(descriptor) ? RHIMLHelpers.GetEffectiveStrides(descriptor) : null!,
                TotalTensorSizeInBytes = RHIMLHelpers.CalculateMinimumByteLength(descriptor),
                GuaranteedBaseOffsetAlignment = 0,
            };
            return tensorDescription;
        }

        internal static BindingDescription CreateBufferBinding(Dx12Buffer buffer, ulong offset, ulong sizeInBytes)
        {
            BufferBinding bufferBinding;
            bufferBinding.Buffer = buffer.NativeResource;
            bufferBinding.Offset = offset;
            bufferBinding.SizeInBytes = sizeInBytes;
            return bufferBinding;
        }

        internal static BindingDescription CreateTensorBinding(Dx12Tensor tensor)
        {
            return CreateBufferBinding(tensor.BackingBuffer, tensor.BackingBufferOffset, tensor.ByteLength);
        }

        internal static void ValidateTensorLayout(string label, in RHIMLTensorDescriptor expected, in RHIMLTensorDescriptor actual)
        {
            if (!RHIMLHelpers.HasCompatibleLayout(expected, actual))
            {
                throw new InvalidOperationException(
                    $"{label} tensor layout mismatch. expected={RHIMLHelpers.DescribeLayout(expected)}, actual={RHIMLHelpers.DescribeLayout(actual)}.");
            }
        }

        internal static void ValidateGemmAddReluLayouts(
            in RHIMLTensorDescriptor aDescriptor,
            in RHIMLTensorDescriptor bDescriptor,
            in RHIMLTensorDescriptor cDescriptor,
            in RHIMLTensorDescriptor outputDescriptor)
        {
            ReadOnlySpan<uint> aDims = aDescriptor.Dimensions.Span;
            ReadOnlySpan<uint> bDims = bDescriptor.Dimensions.Span;
            ReadOnlySpan<uint> cDims = cDescriptor.Dimensions.Span;
            ReadOnlySpan<uint> outputDims = outputDescriptor.Dimensions.Span;

            if (aDims.Length < 2 || aDims.Length != bDims.Length || aDims.Length != cDims.Length || aDims.Length != outputDims.Length)
            {
                throw new InvalidOperationException("DX12 ML GEMM+Add+ReLU requires A/B/C/Output tensors to share the same rank and have rank >= 2.");
            }

            int batchRank = aDims.Length - 2;
            for (int i = 0; i < batchRank; ++i)
            {
                if (aDims[i] != bDims[i] || aDims[i] != cDims[i] || aDims[i] != outputDims[i])
                {
                    throw new InvalidOperationException("DX12 ML GEMM+Add+ReLU requires matching batch dimensions across A/B/C/Output.");
                }
            }

            uint m = aDims[aDims.Length - 2];
            uint k = aDims[aDims.Length - 1];
            if (bDims[bDims.Length - 2] != k)
            {
                throw new InvalidOperationException($"DX12 ML GEMM+Add+ReLU requires compatible matrix dimensions. A.K={k}, B.K={bDims[bDims.Length - 2]}.");
            }

            uint n = bDims[bDims.Length - 1];
            if (outputDims[outputDims.Length - 2] != m || outputDims[outputDims.Length - 1] != n)
            {
                throw new InvalidOperationException(
                    $"DX12 ML GEMM+Add+ReLU output shape mismatch. expected MxN={m}x{n}, actual={outputDims[outputDims.Length - 2]}x{outputDims[outputDims.Length - 1]}.");
            }

            for (int i = 0; i < cDims.Length; ++i)
            {
                if (cDims[i] != outputDims[i])
                {
                    throw new InvalidOperationException("DX12 ML GEMM+Add+ReLU bias tensor C must match the output tensor shape exactly.");
                }
            }

            if (aDescriptor.DataType != bDescriptor.DataType
                || aDescriptor.DataType != cDescriptor.DataType
                || aDescriptor.DataType != outputDescriptor.DataType)
            {
                throw new InvalidOperationException("DX12 ML GEMM+Add+ReLU requires A/B/C/Output tensors to share the same data type.");
            }
        }
    }

    internal sealed class Dx12MLProgram : RHIMLProgram
    {
        internal Dx12MLProgramKind Kind { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }
        internal RHIMLTensorDescriptor IntermediateTensorDescriptor => m_IntermediateTensorDescriptor;

        private readonly RHIMLTensorDescriptor m_ATensorDescriptor;
        private readonly RHIMLTensorDescriptor m_BTensorDescriptor;
        private readonly RHIMLTensorDescriptor m_CTensorDescriptor;
        private readonly RHIMLTensorDescriptor m_IntermediateTensorDescriptor;
        private readonly RHIMLTensorDescriptor m_OutputTensorDescriptor;

        private Dx12MLProgram(
            string name,
            Dx12MLProgramKind kind,
            in RHIMLTensorDescriptor aDescriptor,
            in RHIMLTensorDescriptor bDescriptor,
            in RHIMLTensorDescriptor cDescriptor,
            in RHIMLTensorDescriptor intermediateDescriptor,
            in RHIMLTensorDescriptor outputDescriptor,
            RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            Kind = kind;
            m_ATensorDescriptor = RHIMLHelpers.CloneLayoutDescriptor(aDescriptor);
            m_BTensorDescriptor = RHIMLHelpers.CloneLayoutDescriptor(bDescriptor);
            m_CTensorDescriptor = RHIMLHelpers.CloneLayoutDescriptor(cDescriptor);
            m_IntermediateTensorDescriptor = RHIMLHelpers.CloneLayoutDescriptor(intermediateDescriptor);
            m_OutputTensorDescriptor = RHIMLHelpers.CloneLayoutDescriptor(outputDescriptor);
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        internal static Dx12MLProgram CreateGemmAddRelu(
            string name,
            in RHIMLTensorDescriptor aDescriptor,
            in RHIMLTensorDescriptor bDescriptor,
            in RHIMLTensorDescriptor cDescriptor,
            in RHIMLTensorDescriptor outputDescriptor)
        {
            Dx12MLUtilities.ValidateGemmAddReluLayouts(aDescriptor, bDescriptor, cDescriptor, outputDescriptor);

            RHIMLTensorDescriptor intermediateDescriptor = new RHIMLTensorDescriptor
            {
                DataType = outputDescriptor.DataType,
                UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
                StorageMode = ERHIStorageMode.GPULocal,
                Dimensions = outputDescriptor.Dimensions.ToArray(),
                Strides = null,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };

            RHIMLTensorBindingInfo[] bindingInfos =
            {
                new RHIMLTensorBindingInfo
                {
                    Name = "A",
                    Index = 0,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(aDescriptor),
                },
                new RHIMLTensorBindingInfo
                {
                    Name = "B",
                    Index = 1,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(bDescriptor),
                },
                new RHIMLTensorBindingInfo
                {
                    Name = "C",
                    Index = 2,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(cDescriptor),
                },
                new RHIMLTensorBindingInfo
                {
                    Name = "Output",
                    Index = 0,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(outputDescriptor),
                },
            };

            return new Dx12MLProgram(
                name,
                Dx12MLProgramKind.GeneralMatrixMultiplyAddRelu,
                aDescriptor,
                bDescriptor,
                cDescriptor,
                intermediateDescriptor,
                outputDescriptor,
                bindingInfos);
        }

        internal void ValidateDeviceSupport(Dx12Device device)
        {
            if (!device.SupportsDirectML)
            {
                throw new NotSupportedException("DirectML is unavailable on this DX12 device.");
            }

            TensorDataType[] tensorDataTypes =
            {
                Dx12MLUtilities.ConvertToDirectMLDataType(m_ATensorDescriptor.DataType),
                Dx12MLUtilities.ConvertToDirectMLDataType(m_BTensorDescriptor.DataType),
                Dx12MLUtilities.ConvertToDirectMLDataType(m_CTensorDescriptor.DataType),
                Dx12MLUtilities.ConvertToDirectMLDataType(m_IntermediateTensorDescriptor.DataType),
                Dx12MLUtilities.ConvertToDirectMLDataType(m_OutputTensorDescriptor.DataType),
            };

            for (int i = 0; i < tensorDataTypes.Length; ++i)
            {
                if (!device.DirectMLDevice.CheckTensorDataTypeSupport(tensorDataTypes[i]))
                {
                    throw new NotSupportedException($"DirectML tensor data type '{tensorDataTypes[i]}' is not supported by the current DX12 device.");
                }
            }
        }

        internal OperatorDescription[] CreateOperatorDescriptions()
        {
            GeneralMatrixMultiplyOperatorDescription gemmDescription = new GeneralMatrixMultiplyOperatorDescription
            {
                ATensor = Dx12MLUtilities.CreateTensorDescription(m_ATensorDescriptor),
                BTensor = Dx12MLUtilities.CreateTensorDescription(m_BTensorDescriptor),
                CTensor = Dx12MLUtilities.CreateTensorDescription(m_CTensorDescriptor),
                OutputTensor = Dx12MLUtilities.CreateTensorDescription(m_IntermediateTensorDescriptor),
                TransformA = MatrixTransform.None,
                TransformB = MatrixTransform.None,
                Alpha = 1.0f,
                Beta = 1.0f,
                FusedActivation = null,
            };

            ActivationReluOperatorDescription reluDescription = new ActivationReluOperatorDescription
            {
                InputTensor = Dx12MLUtilities.CreateTensorDescription(m_IntermediateTensorDescriptor),
                OutputTensor = Dx12MLUtilities.CreateTensorDescription(m_OutputTensorDescriptor),
            };

            return new OperatorDescription[]
            {
                gemmDescription,
                reluDescription,
            };
        }

        protected override void Release()
        {
        }
    }

    internal sealed class Dx12MLBindingSet : RHIMLBindingSet
    {
        internal Dx12MLPipeline PipelineTyped => (Dx12MLPipeline)(m_Pipeline ?? throw new InvalidOperationException("DX12 ML binding set pipeline is unavailable."));
        internal Dx12Tensor[] Inputs { get; }
        internal Dx12Tensor[] Outputs { get; }
        internal Dx12Buffer? TemporaryBuffer => m_TemporaryBuffer;
        internal Dx12Buffer? PersistentBuffer => m_PersistentBuffer;
        internal Dx12Buffer IntermediateBuffer => m_IntermediateBuffer ?? throw new InvalidOperationException("DX12 ML intermediate buffer is unavailable.");
        internal IDMLBindingTable InitializerBindingTable => m_InitializerBindingTable ?? throw new InvalidOperationException("DX12 ML initializer binding table is unavailable.");
        internal bool IsInitialized => m_IsInitialized;
        internal bool InternalResourcesPrepared => m_InternalResourcesPrepared;

        private readonly Dx12Device m_Device;
        private readonly BindingDescription[] m_InputBindings;
        private readonly BindingDescription[] m_OutputBindings;
        private readonly BindingDescription[][] m_StageInputs;
        private readonly BindingDescription[][] m_StageOutputs;
        private readonly BindingDescription?[] m_StagePersistentBindings;
        private readonly BindingDescription? m_TemporaryBinding;

        private IDMLBindingTable? m_InitializerBindingTable;
        private IDMLBindingTable[] m_ExecutionBindingTables;
        private Dx12DescriptorInfo[] m_ExecutionDescriptorAllocations;
        private int[] m_ExecutionDescriptorCounts;
        private Dx12DescriptorInfo m_InitializerDescriptorAllocation;
        private int m_InitializerDescriptorCount;
        private Dx12Buffer? m_TemporaryBuffer;
        private Dx12Buffer? m_PersistentBuffer;
        private Dx12Buffer? m_IntermediateBuffer;
        private bool m_IsInitialized;
        private bool m_InternalResourcesPrepared;

        internal Dx12MLBindingSet(Dx12Device device, in RHIMLBindingSetDescriptor descriptor)
        {
            m_Device = device;

            if (descriptor.Pipeline is not Dx12MLPipeline dx12Pipeline)
            {
                throw new InvalidOperationException($"DX12 ML binding set requires a {nameof(Dx12MLPipeline)}.");
            }

            m_Pipeline = dx12Pipeline;
            Inputs = ConvertTensors(dx12Pipeline, descriptor.Inputs.Span, ERHIMLTensorBindingKind.Input, dx12Pipeline.InputCount);
            Outputs = ConvertTensors(dx12Pipeline, descriptor.Outputs.Span, ERHIMLTensorBindingKind.Output, dx12Pipeline.OutputCount);

            m_InputBindings = CreateTensorBindings(Inputs);
            m_OutputBindings = CreateTensorBindings(Outputs);
            m_ExecutionBindingTables = new IDMLBindingTable[dx12Pipeline.StageCount];
            m_ExecutionDescriptorAllocations = new Dx12DescriptorInfo[dx12Pipeline.StageCount];
            m_ExecutionDescriptorCounts = new int[dx12Pipeline.StageCount];
            m_StagePersistentBindings = new BindingDescription?[dx12Pipeline.StageCount];

            Dx12Buffer? temporaryBuffer = null;
            Dx12Buffer? persistentBuffer = null;
            Dx12Buffer? intermediateBuffer = null;
            IDMLBindingTable? initializerBindingTable = null;
            int createdExecutionTables = 0;

            try
            {
                m_InitializerDescriptorCount = Math.Max(1, checked((int)dx12Pipeline.InitializerBindingProperties.RequiredDescriptorCount));
                m_InitializerDescriptorAllocation = device.AllocateCbvSrvUavDescriptor(m_InitializerDescriptorCount);
                BindingTableDescription initializerTableDescription = new BindingTableDescription
                {
                    Dispatchable = dx12Pipeline.OperatorInitializer,
                    CPUDescriptorHandle = m_InitializerDescriptorAllocation.CpuHandle,
                    GPUDescriptorHandle = m_InitializerDescriptorAllocation.GpuHandle,
                    SizeInDescriptors = checked((uint)m_InitializerDescriptorCount),
                };
                initializerBindingTable = device.DirectMLDevice.CreateBindingTable(ref initializerTableDescription);

                if (dx12Pipeline.TemporaryResourceSize > 0)
                {
                    temporaryBuffer = CreateInternalResourceBuffer(device, dx12Pipeline.TemporaryResourceSize);
                    m_TemporaryBinding = Dx12MLUtilities.CreateBufferBinding(temporaryBuffer, 0, dx12Pipeline.TemporaryResourceSize);
                }

                if (dx12Pipeline.PersistentResourceSize > 0)
                {
                    persistentBuffer = CreateInternalResourceBuffer(device, dx12Pipeline.PersistentResourceSize);
                }

                intermediateBuffer = CreateInternalResourceBuffer(device, dx12Pipeline.ProgramIntermediateTensorSize);
                BindingDescription intermediateBinding = Dx12MLUtilities.CreateBufferBinding(intermediateBuffer, 0, dx12Pipeline.ProgramIntermediateTensorSize);

                m_StageInputs = new BindingDescription[][]
                {
                    m_InputBindings,
                    new[] { intermediateBinding },
                };
                m_StageOutputs = new BindingDescription[][]
                {
                    new[] { intermediateBinding },
                    m_OutputBindings,
                };

                for (int i = 0; i < dx12Pipeline.StageCount; ++i)
                {
                    ulong stagePersistentSize = dx12Pipeline.GetPersistentResourceSize(i);
                    if (persistentBuffer != null && stagePersistentSize > 0)
                    {
                        m_StagePersistentBindings[i] = Dx12MLUtilities.CreateBufferBinding(
                            persistentBuffer,
                            dx12Pipeline.GetPersistentResourceOffset(i),
                            stagePersistentSize);
                    }

                    int executionDescriptorCount = Math.Max(1, checked((int)dx12Pipeline.GetRequiredDescriptorCount(i)));
                    Dx12DescriptorInfo executionDescriptorAllocation = device.AllocateCbvSrvUavDescriptor(executionDescriptorCount);
                    BindingTableDescription executionTableDescription = new BindingTableDescription
                    {
                        Dispatchable = dx12Pipeline.GetCompiledOperator(i),
                        CPUDescriptorHandle = executionDescriptorAllocation.CpuHandle,
                        GPUDescriptorHandle = executionDescriptorAllocation.GpuHandle,
                        SizeInDescriptors = checked((uint)executionDescriptorCount),
                    };

                    m_ExecutionDescriptorCounts[i] = executionDescriptorCount;
                    m_ExecutionDescriptorAllocations[i] = executionDescriptorAllocation;
                    m_ExecutionBindingTables[i] = device.DirectMLDevice.CreateBindingTable(ref executionTableDescription);
                    ++createdExecutionTables;
                }

                m_InitializerBindingTable = initializerBindingTable;
                m_TemporaryBuffer = temporaryBuffer;
                m_PersistentBuffer = persistentBuffer;
                m_IntermediateBuffer = intermediateBuffer;
            }
            catch
            {
                initializerBindingTable?.Release();
                temporaryBuffer?.Dispose();
                persistentBuffer?.Dispose();
                intermediateBuffer?.Dispose();

                if (m_InitializerDescriptorCount > 0)
                {
                    device.FreeCbvSrvUavDescriptor(m_InitializerDescriptorAllocation.Index, m_InitializerDescriptorCount);
                }

                for (int i = 0; i < createdExecutionTables; ++i)
                {
                    m_ExecutionBindingTables[i]?.Release();
                    if (m_ExecutionDescriptorCounts[i] > 0)
                    {
                        device.FreeCbvSrvUavDescriptor(m_ExecutionDescriptorAllocations[i].Index, m_ExecutionDescriptorCounts[i]);
                    }
                }

                throw;
            }
        }

        internal void PrepareForInitialization()
        {
            if (m_TemporaryBinding.HasValue)
            {
                InitializerBindingTable.BindTemporaryResource(m_TemporaryBinding);
            }

            if (m_PersistentBuffer != null && PipelineTyped.PersistentResourceSize > 0)
            {
                InitializerBindingTable.BindPersistentResource(Dx12MLUtilities.CreateBufferBinding(m_PersistentBuffer, 0, PipelineTyped.PersistentResourceSize));
            }
        }

        internal void PrepareForExecution(int stageIndex)
        {
            IDMLBindingTable bindingTable = m_ExecutionBindingTables[stageIndex];
            bindingTable.BindInputs(m_StageInputs[stageIndex]);
            bindingTable.BindOutputs(m_StageOutputs[stageIndex]);
            if (m_TemporaryBinding.HasValue)
            {
                bindingTable.BindTemporaryResource(m_TemporaryBinding);
            }

            if (m_StagePersistentBindings[stageIndex].HasValue)
            {
                bindingTable.BindPersistentResource(m_StagePersistentBindings[stageIndex]);
            }
        }

        internal IDMLBindingTable GetExecutionBindingTable(int stageIndex)
        {
            return m_ExecutionBindingTables[stageIndex];
        }

        internal void MarkInitialized()
        {
            m_IsInitialized = true;
        }

        internal void MarkInternalResourcesPrepared()
        {
            m_InternalResourcesPrepared = true;
        }

        private static Dx12Tensor[] ConvertTensors(
            Dx12MLPipeline pipeline,
            ReadOnlySpan<RHITensor> tensors,
            ERHIMLTensorBindingKind kind,
            uint expectedCount)
        {
            if (tensors.Length != expectedCount)
            {
                throw new InvalidOperationException($"DX12 ML binding count mismatch for {kind}. expected={expectedCount}, actual={tensors.Length}.");
            }

            Dx12Tensor[] result = new Dx12Tensor[tensors.Length];
            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos = pipeline.BindingInfos.Span;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                if (bindingInfo.Kind != kind)
                {
                    continue;
                }

                if (bindingInfo.Index >= tensors.Length)
                {
                    throw new InvalidOperationException($"DX12 ML binding index out of range for {kind}. index={bindingInfo.Index}, count={tensors.Length}.");
                }

                Dx12Tensor tensor = tensors[(int)bindingInfo.Index] as Dx12Tensor
                    ?? throw new InvalidOperationException($"DX12 ML binding tensor[{bindingInfo.Index}] must be a {nameof(Dx12Tensor)}.");
                Dx12MLUtilities.ValidateTensorLayout($"{kind}[{bindingInfo.Index}] '{bindingInfo.Name}'", bindingInfo.Descriptor, tensor.Descriptor);
                if (tensor.BackingBuffer.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
                {
                    throw new InvalidOperationException(
                        $"DX12 ML {kind}[{bindingInfo.Index}] '{bindingInfo.Name}' must be backed by a GPULocal buffer. " +
                        $"DirectML dispatch requires resources in COMMON/UAV-capable memory, but got {tensor.BackingBuffer.Descriptor.StorageMode}.");
                }
                result[bindingInfo.Index] = tensor;
            }

            for (int i = 0; i < result.Length; ++i)
            {
                if (result[i] == null!)
                {
                    throw new InvalidOperationException($"DX12 ML binding set is missing a {kind} tensor at index {i}.");
                }
            }

            return result;
        }

        private static BindingDescription[] CreateTensorBindings(Dx12Tensor[] tensors)
        {
            BindingDescription[] bindings = new BindingDescription[tensors.Length];
            for (int i = 0; i < tensors.Length; ++i)
            {
                bindings[i] = Dx12MLUtilities.CreateTensorBinding(tensors[i]);
            }

            return bindings;
        }

        private static Dx12Buffer CreateInternalResourceBuffer(Dx12Device device, ulong byteSize)
        {
            RHIBufferDescriptor bufferDescriptor = new RHIBufferDescriptor
            {
                ByteSize = checked((int)byteSize),
                Format = ERHIBufferFormat.Undefine,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource | ERHIBufferUsage.UnorderedAccess,
            };
            return new Dx12Buffer(device, bufferDescriptor);
        }

        protected override void Release()
        {
            m_InitializerBindingTable?.Release();
            m_InitializerBindingTable = null;

            if (m_ExecutionBindingTables != null)
            {
                for (int i = 0; i < m_ExecutionBindingTables.Length; ++i)
                {
                    m_ExecutionBindingTables[i]?.Release();
                }
                m_ExecutionBindingTables = Array.Empty<IDMLBindingTable>();
            }

            m_TemporaryBuffer?.Dispose();
            m_TemporaryBuffer = null;

            m_PersistentBuffer?.Dispose();
            m_PersistentBuffer = null;

            m_IntermediateBuffer?.Dispose();
            m_IntermediateBuffer = null;

            if (m_InitializerDescriptorCount > 0)
            {
                m_Device.FreeCbvSrvUavDescriptor(m_InitializerDescriptorAllocation.Index, m_InitializerDescriptorCount);
            }

            if (m_ExecutionDescriptorCounts != null)
            {
                for (int i = 0; i < m_ExecutionDescriptorCounts.Length; ++i)
                {
                    if (m_ExecutionDescriptorCounts[i] > 0)
                    {
                        m_Device.FreeCbvSrvUavDescriptor(m_ExecutionDescriptorAllocations[i].Index, m_ExecutionDescriptorCounts[i]);
                    }
                }
            }
        }
    }
}
