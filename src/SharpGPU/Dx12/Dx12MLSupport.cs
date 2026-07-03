using System;
using System.Collections.Generic;
using Vortice.DirectML;

namespace SharpGPU
{
    internal enum Dx12MLProgramKind : byte
    {
        // Legacy single-purpose kind retained for the CreateGemmAddRelu convenience factory's
        // diagnostics. Descriptor-driven programs (Create(RHIMLProgramDescriptor)) carry the op
        // sequence directly and do not need a discriminating kind.
        GeneralMatrixMultiplyAddRelu = 0,
        // Descriptor-driven op sequence (ADR-0028). The op list lives on the program descriptor.
        OpSequence = 1,
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

        /// <summary>
        /// Builds the DirectML <see cref="OperatorDescription"/> for a single RHI ML op, using the
        /// provided resolved tensor descriptions (already mapped from program inputs / earlier op
        /// outputs). Returns null when the op kind is not mapped to a DirectML operator; the caller
        /// surfaces that as an explicit unsupported-op error.
        /// </summary>
        internal static OperatorDescription? CreateOperatorDescription(
            in RHIMLOpDescriptor op,
            TensorDescription[] resolvedInputs,
            TensorDescription outputTensor)
        {
            switch (op.Kind)
            {
                case ERHIMLOpKind.ElementWiseAdd:
                    return new ElementWiseAddOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseSubtract:
                    return new ElementWiseSubtractOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseMultiply:
                    return new ElementWiseMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseDivide:
                    return new ElementWiseDivideOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ElementWiseNegate:
                    return new ElementWiseNegateOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationRelu:
                    return new ActivationReluOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationSigmoid:
                    return new ActivationSigmoidOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.ActivationTanh:
                    return new ActivationTanhOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.MatrixMultiply:
                    return new GeneralMatrixMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        CTensor = null,
                        OutputTensor = outputTensor,
                        TransformA = MapMatrixTransform(op.TransformA),
                        TransformB = MapMatrixTransform(op.TransformB),
                        Alpha = op.Alpha,
                        Beta = 0.0f,
                        FusedActivation = BuildFusedActivation(op.FusedActivation),
                    };
                case ERHIMLOpKind.GeneralMatrixMultiply:
                    return new GeneralMatrixMultiplyOperatorDescription
                    {
                        ATensor = resolvedInputs[0],
                        BTensor = resolvedInputs[1],
                        CTensor = resolvedInputs.Length >= 3 ? resolvedInputs[2] : null,
                        OutputTensor = outputTensor,
                        TransformA = MapMatrixTransform(op.TransformA),
                        TransformB = MapMatrixTransform(op.TransformB),
                        Alpha = op.Alpha,
                        Beta = op.Beta,
                        FusedActivation = BuildFusedActivation(op.FusedActivation),
                    };
                case ERHIMLOpKind.ActivationSoftmax:
                    return new ActivationSoftmaxOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                    };
                case ERHIMLOpKind.MeanVarianceNormalization:
                    return new MeanVarianceNormalization1OperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        ScaleTensor = resolvedInputs.Length >= 2 ? resolvedInputs[1] : null,
                        BiasTensor = resolvedInputs.Length >= 3 ? resolvedInputs[2] : null,
                        OutputTensor = outputTensor,
                        Axes = op.Axes ?? new[] { -1 },
                        NormalizeVariance = true,
                        Epsilon = op.Epsilon,
                        FusedActivation = null,
                    };
                case ERHIMLOpKind.ReduceMean:
                    return new ReduceOperatorDescription
                    {
                        Function = ReduceFunction.Average,
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                        Axes = op.Axes ?? new[] { -1 },
                    };
                case ERHIMLOpKind.Reshape:
                case ERHIMLOpKind.Transpose:
                case ERHIMLOpKind.ElementWiseIdentity:
                    // DirectML has no dedicated reshape/transpose operator: both are expressed as an
                    // element-wise identity copy with the permuted/reshaped layout encoded on the
                    // output tensor descriptor (sizes + strides). The outputTensor carries the
                    // target shape (reshape) or the permuted strides (transpose).
                    return new ElementWiseIdentityOperatorDescription
                    {
                        InputTensor = resolvedInputs[0],
                        OutputTensor = outputTensor,
                        ScaleBias = null,
                    };
                default:
                    return null;
            }
        }

        internal static OperatorDescription? BuildFusedActivation(ERHIMLFusedActivation activation)
        {
            return activation switch
            {
                ERHIMLFusedActivation.None => null,
                ERHIMLFusedActivation.Relu => new ActivationReluOperatorDescription(),
                ERHIMLFusedActivation.Sigmoid => new ActivationSigmoidOperatorDescription(),
                ERHIMLFusedActivation.Tanh => new ActivationTanhOperatorDescription(),
                _ => null,
            };
        }

        internal static MatrixTransform MapMatrixTransform(ERHIMLMatrixTransform transform)
        {
            return transform switch
            {
                ERHIMLMatrixTransform.None => MatrixTransform.None,
                ERHIMLMatrixTransform.Transpose => MatrixTransform.Transpose,
                _ => MatrixTransform.None,
            };
        }
    }

    internal sealed class Dx12MLProgram : RHIMLProgram
    {
        internal Dx12MLProgramKind Kind { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }
        internal RHIMLTensorDescriptor[] IntermediateTensorDescriptors => m_IntermediateTensorDescriptors;
        internal RHIMLOpDescriptor[] Ops => m_Ops;
        internal RHIMLTensorDescriptor[] ProgramInputs => m_ProgramInputs;
        internal RHIMLTensorDescriptor[] ProgramOutputs => m_ProgramOutputs;

        private readonly RHIMLTensorDescriptor[] m_ProgramInputs;
        private readonly RHIMLTensorDescriptor[] m_ProgramOutputs;
        private readonly RHIMLTensorDescriptor[] m_IntermediateTensorDescriptors;
        private readonly RHIMLOpDescriptor[] m_Ops;

        // Legacy single-intermediate accessor for the GemmAddRelu convenience factory's
        // diagnostics. Descriptor-driven programs read their intermediate list via the array above.
        internal RHIMLTensorDescriptor IntermediateTensorDescriptor
        {
            get
            {
                if (m_IntermediateTensorDescriptors.Length == 0)
                {
                    throw new InvalidOperationException("DX12 ML program has no intermediate tensor.");
                }

                return m_IntermediateTensorDescriptors[0];
            }
        }

        private Dx12MLProgram(
            string name,
            Dx12MLProgramKind kind,
            RHIMLTensorDescriptor[] programInputs,
            RHIMLTensorDescriptor[] programOutputs,
            RHIMLTensorDescriptor[] intermediateDescriptors,
            RHIMLOpDescriptor[] ops,
            RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            Kind = kind;
            m_ProgramInputs = programInputs;
            m_ProgramOutputs = programOutputs;
            m_IntermediateTensorDescriptors = intermediateDescriptors;
            m_Ops = ops;
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        /// <summary>
        /// Descriptor-driven constructor: the canonical entry point for general subgraph lowering
        /// (ADR-0028). Each op's output that is not a program output becomes an intermediate
        /// tensor; program outputs are surfaced as the binding set's output slots. Binding infos
        /// advertise the program's input and output slots so that <see cref="Dx12MLPipeline"/> and
        /// <see cref="Dx12MLBindingSet"/> can wire stages identically to the legacy 2-stage path.
        /// </summary>
        internal static Dx12MLProgram Create(in RHIMLProgramDescriptor descriptor)
        {
            if (descriptor.Ops.Length == 0)
            {
                throw new InvalidOperationException("DX12 ML program descriptor must contain at least one op.");
            }

            RHIMLTensorDescriptor[] programInputs = CloneDescriptors(descriptor.Inputs);
            RHIMLOpDescriptor[] ops = new RHIMLOpDescriptor[descriptor.Ops.Length];
            List<RHIMLTensorDescriptor> intermediates = new List<RHIMLTensorDescriptor>();

            // Determine which op outputs are program outputs (by matching shape/dtype to the
            // declared program outputs, in declared order) and which are intermediates. A program
            // output is matched to the op that produces it by position: the k-th program output is
            // the output of the op referenced by no-one-but-the-output-binding. Because the
            // descriptor is a linear DAG, we mark an op output as a program output when its op is
            // the last producer of that logical output; the simpler, contract-faithful rule used
            // here: op outputs that are never consumed by a later op are program outputs (in op
            // order), and their count must equal descriptor.Outputs.Length.
            bool[] isProgramOutput = new bool[ops.Length];
            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                isProgramOutput[i] = true;
            }

            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in descriptor.Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex < isProgramOutput.Length)
                    {
                        isProgramOutput[inputRef.OpIndex] = false;
                    }
                }
            }

            int programOutputCount = 0;
            for (int i = 0; i < isProgramOutput.Length; ++i)
            {
                if (isProgramOutput[i])
                {
                    ++programOutputCount;
                }
            }

            if (programOutputCount != descriptor.Outputs.Length)
            {
                throw new InvalidOperationException(
                    $"DX12 ML program descriptor output count mismatch: {programOutputCount} op(s) produce unconsumed outputs but {descriptor.Outputs.Length} program output(s) were declared.");
            }

            // Build per-op output descriptors: program outputs use the declared output descriptors
            // (in op order over unconsumed ops); intermediate outputs get a GPULocal descriptor with
            // the op's output shape. The op's output descriptor already carries the shape from the
            // graph builder.
            int outputMatchIndex = 0;
            RHIMLTensorDescriptor[] opOutputDescriptors = new RHIMLTensorDescriptor[ops.Length];
            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                ref readonly RHIMLOpDescriptor srcOp = ref descriptor.Ops[i];
                if (isProgramOutput[i])
                {
                    opOutputDescriptors[i] = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[outputMatchIndex]);
                    ++outputMatchIndex;
                }
                else
                {
                    RHIMLTensorDescriptor intermediate = RHIMLHelpers.CloneLayoutDescriptor(srcOp.Output);
                    intermediate.StorageMode = ERHIStorageMode.GPULocal;
                    intermediate.BackingBuffer = null;
                    intermediate.BackingBufferOffset = 0;
                    intermediate.UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write;
                    opOutputDescriptors[i] = intermediate;
                    intermediates.Add(intermediate);
                }

                ops[i] = srcOp;
            }

            // Binding infos: program inputs first (Input kind), then program outputs (Output kind).
            // Intermediate tensors are owned by the binding set, not advertised as program bindings.
            List<RHIMLTensorBindingInfo> bindingInfos = new List<RHIMLTensorBindingInfo>();
            for (int i = 0; i < programInputs.Length; ++i)
            {
                bindingInfos.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"Input{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(programInputs[i]),
                });
            }

            for (int i = 0; i < descriptor.Outputs.Length; ++i)
            {
                bindingInfos.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"Output{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[i]),
                });
            }

            return new Dx12MLProgram(
                descriptor.Name,
                Dx12MLProgramKind.OpSequence,
                programInputs,
                CloneDescriptors(descriptor.Outputs),
                intermediates.ToArray(),
                ops,
                bindingInfos.ToArray());
        }

        /// <summary>
        /// Convenience factory for the canonical <c>alpha=1, beta=1, no-transpose GEMM + ReLU</c>
        /// program (RFC-0003 §4.1 v1 reference). Retained as a thin sugar over the descriptor-driven
        /// <see cref="Create(in RHIMLProgramDescriptor)"/> path so that existing callers (the
        /// DirectML conformance and rendering tests) compile and pass unchanged while indirectly
        /// exercising the general op-sequence machinery. The signature is preserved exactly. The
        /// program is a 2-op sequence (GEMM -> intermediate -> ReLU -> output) mirroring the original
        /// hand-written <c>CreateOperatorDescriptions</c> so that the compiled-operator topology and
        /// binding shape are byte-for-byte equivalent to the pre-refactor program.
        /// </summary>
        internal static Dx12MLProgram CreateGemmAddRelu(
            string name,
            in RHIMLTensorDescriptor aDescriptor,
            in RHIMLTensorDescriptor bDescriptor,
            in RHIMLTensorDescriptor cDescriptor,
            in RHIMLTensorDescriptor outputDescriptor)
        {
            Dx12MLUtilities.ValidateGemmAddReluLayouts(aDescriptor, bDescriptor, cDescriptor, outputDescriptor);

            RHIMLTensorDescriptor[] inputs =
            {
                RHIMLHelpers.CloneLayoutDescriptor(aDescriptor),
                RHIMLHelpers.CloneLayoutDescriptor(bDescriptor),
                RHIMLHelpers.CloneLayoutDescriptor(cDescriptor),
            };
            RHIMLTensorDescriptor[] outputs = { RHIMLHelpers.CloneLayoutDescriptor(outputDescriptor) };

            // Intermediate tensor between GEMM and ReLU — same shape/dtype as the output, GPULocal.
            RHIMLTensorDescriptor intermediate = new RHIMLTensorDescriptor
            {
                DataType = outputDescriptor.DataType,
                UsageFlag = ERHITensorUsage.MachineLearning | ERHITensorUsage.Read | ERHITensorUsage.Write,
                StorageMode = ERHIStorageMode.GPULocal,
                Dimensions = outputDescriptor.Dimensions.ToArray(),
                Strides = null,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };

            // Op 0: GEMM (alpha=1, beta=1, no transpose, no fused activation) writes A*B + C into the
            // intermediate tensor. Op 1: ReLU reads the intermediate and writes the program output.
            RHIMLOpDescriptor gemmOp = RHIMLOpDescriptor.Create(
                ERHIMLOpKind.GeneralMatrixMultiply,
                new[]
                {
                    RHIMLOpTensorRef.FromInput(0),
                    RHIMLOpTensorRef.FromInput(1),
                    RHIMLOpTensorRef.FromInput(2),
                },
                intermediate,
                name + ".Gemm");
            gemmOp.Alpha = 1.0f;
            gemmOp.Beta = 1.0f;
            gemmOp.TransformA = ERHIMLMatrixTransform.None;
            gemmOp.TransformB = ERHIMLMatrixTransform.None;
            gemmOp.FusedActivation = ERHIMLFusedActivation.None;

            RHIMLOpDescriptor reluOp = RHIMLOpDescriptor.Create(
                ERHIMLOpKind.ActivationRelu,
                new[] { RHIMLOpTensorRef.FromOpOutput(0) },
                RHIMLHelpers.CloneLayoutDescriptor(outputDescriptor),
                name + ".Relu");

            RHIMLProgramDescriptor descriptor = RHIMLProgramDescriptor.Create(name, inputs, outputs, new[] { gemmOp, reluOp });
            return Create(descriptor);
        }

        internal void ValidateDeviceSupport(Dx12Device device)
        {
            if (!device.SupportsDirectML)
            {
                throw new NotSupportedException("DirectML is unavailable on this DX12 device.");
            }

            HashSet<TensorDataType> seenTypes = new HashSet<TensorDataType>();
            for (int i = 0; i < m_ProgramInputs.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_ProgramInputs[i].DataType));
            }

            for (int i = 0; i < m_ProgramOutputs.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_ProgramOutputs[i].DataType));
            }

            for (int i = 0; i < m_IntermediateTensorDescriptors.Length; ++i)
            {
                seenTypes.Add(Dx12MLUtilities.ConvertToDirectMLDataType(m_IntermediateTensorDescriptors[i].DataType));
            }

            foreach (TensorDataType tensorDataType in seenTypes)
            {
                if (!device.DirectMLDevice.CheckTensorDataTypeSupport(tensorDataType))
                {
                    throw new NotSupportedException($"DirectML tensor data type '{tensorDataType}' is not supported by the current DX12 device.");
                }
            }
        }

        internal OperatorDescription[] CreateOperatorDescriptions()
        {
            OperatorDescription[] descriptions = new OperatorDescription[m_Ops.Length];
            for (int i = 0; i < m_Ops.Length; ++i)
            {
                ref readonly RHIMLOpDescriptor op = ref m_Ops[i];
                TensorDescription[] resolvedInputs = ResolveOpInputs(op);
                TensorDescription outputTensor = Dx12MLUtilities.CreateTensorDescription(GetOpOutputDescriptor(i));
                OperatorDescription? description = Dx12MLUtilities.CreateOperatorDescription(op, resolvedInputs, outputTensor);
                if (!description.HasValue)
                {
                    throw new NotSupportedException($"DirectML does not support RHI ML op kind '{op.Kind}' (op '{op.Name}').");
                }

                descriptions[i] = description.Value;
            }

            return descriptions;
        }

        internal RHIMLTensorDescriptor GetOpOutputDescriptor(int opIndex)
        {
            // A program output if unconsumed; otherwise an intermediate.
            // Re-derive the isProgramOutput flag consistently with Create().
            bool isProgramOutput = true;
            for (int i = 0; i < m_Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in m_Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex == opIndex && i != opIndex)
                    {
                        isProgramOutput = false;
                        break;
                    }
                }

                if (!isProgramOutput)
                {
                    break;
                }
            }

            if (isProgramOutput)
            {
                // Map to the program output by op order among unconsumed ops.
                int outputIndex = 0;
                for (int i = 0; i < opIndex; ++i)
                {
                    bool unconsumed = true;
                    for (int j = 0; j < m_Ops.Length; ++j)
                    {
                        foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                        {
                            if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                            {
                                unconsumed = false;
                                break;
                            }
                        }

                        if (!unconsumed)
                        {
                            break;
                        }
                    }

                    if (unconsumed)
                    {
                        ++outputIndex;
                    }
                }

                return m_ProgramOutputs[outputIndex];
            }

            // Intermediate: find by op order among consumed ops.
            int intermediateIndex = 0;
            for (int i = 0; i < opIndex; ++i)
            {
                bool consumed = false;
                for (int j = 0; j < m_Ops.Length; ++j)
                {
                    foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                    {
                        if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                        {
                            consumed = true;
                            break;
                        }
                    }

                    if (consumed)
                    {
                        break;
                    }
                }

                if (consumed)
                {
                    ++intermediateIndex;
                }
            }

            return m_IntermediateTensorDescriptors[intermediateIndex];
        }

        internal int GetOpIntermediateIndex(int opIndex)
        {
            int intermediateIndex = 0;
            for (int i = 0; i < opIndex; ++i)
            {
                bool consumed = false;
                for (int j = 0; j < m_Ops.Length; ++j)
                {
                    foreach (RHIMLOpTensorRef inputRef in m_Ops[j].Inputs)
                    {
                        if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                        {
                            consumed = true;
                            break;
                        }
                    }

                    if (consumed)
                    {
                        break;
                    }
                }

                if (consumed)
                {
                    ++intermediateIndex;
                }
            }

            return intermediateIndex;
        }

        private TensorDescription[] ResolveOpInputs(in RHIMLOpDescriptor op)
        {
            TensorDescription[] resolved = new TensorDescription[op.Inputs.Length];
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef inputRef = op.Inputs[i];
                if (inputRef.IsOpOutput)
                {
                    resolved[i] = Dx12MLUtilities.CreateTensorDescription(GetOpOutputDescriptor(inputRef.OpIndex));
                }
                else
                {
                    resolved[i] = Dx12MLUtilities.CreateTensorDescription(m_ProgramInputs[inputRef.InputIndex]);
                }
            }

            return resolved;
        }

        private static RHIMLTensorDescriptor[] CloneDescriptors(RHIMLTensorDescriptor[] descriptors)
        {
            RHIMLTensorDescriptor[] clones = new RHIMLTensorDescriptor[descriptors.Length];
            for (int i = 0; i < descriptors.Length; ++i)
            {
                clones[i] = RHIMLHelpers.CloneLayoutDescriptor(descriptors[i]);
            }

            return clones;
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
        internal Dx12Buffer[] IntermediateBuffers => m_IntermediateBuffers;
        internal IDMLBindingTable InitializerBindingTable => m_InitializerBindingTable ?? throw new InvalidOperationException("DX12 ML initializer binding table is unavailable.");
        internal bool IsInitialized => m_IsInitialized;
        internal bool InternalResourcesPrepared => m_InternalResourcesPrepared;

        private readonly Dx12Device m_Device;
        private readonly BindingDescription[] m_ProgramInputBindings;
        private readonly BindingDescription[] m_ProgramOutputBindings;
        private readonly BindingDescription[] m_IntermediateBindings;
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
        private Dx12Buffer[] m_IntermediateBuffers;
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

            m_ProgramInputBindings = CreateTensorBindings(Inputs);
            m_ProgramOutputBindings = CreateTensorBindings(Outputs);
            m_ExecutionBindingTables = new IDMLBindingTable[dx12Pipeline.StageCount];
            m_ExecutionDescriptorAllocations = new Dx12DescriptorInfo[dx12Pipeline.StageCount];
            m_ExecutionDescriptorCounts = new int[dx12Pipeline.StageCount];
            m_StagePersistentBindings = new BindingDescription?[dx12Pipeline.StageCount];

            Dx12MLProgram program = dx12Pipeline.Program;
            int intermediateCount = program.IntermediateTensorDescriptors.Length;
            m_IntermediateBuffers = new Dx12Buffer[intermediateCount];
            m_IntermediateBindings = new BindingDescription[intermediateCount];

            Dx12Buffer? temporaryBuffer = null;
            Dx12Buffer? persistentBuffer = null;
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

                for (int i = 0; i < intermediateCount; ++i)
                {
                    ulong intermediateSize = RHIMLHelpers.CalculateMinimumByteLength(program.IntermediateTensorDescriptors[i]);
                    Dx12Buffer intermediateBuffer = CreateInternalResourceBuffer(device, intermediateSize);
                    m_IntermediateBuffers[i] = intermediateBuffer;
                    m_IntermediateBindings[i] = Dx12MLUtilities.CreateBufferBinding(intermediateBuffer, 0, intermediateSize);
                }

                // Per-stage input/output binding arrays, resolved from the program's op dataflow.
                m_StageInputs = new BindingDescription[dx12Pipeline.StageCount][];
                m_StageOutputs = new BindingDescription[dx12Pipeline.StageCount][];
                for (int stageIndex = 0; stageIndex < dx12Pipeline.StageCount; ++stageIndex)
                {
                    m_StageInputs[stageIndex] = ResolveStageInputs(program, stageIndex);
                    m_StageOutputs[stageIndex] = ResolveStageOutputs(program, stageIndex);

                    ulong stagePersistentSize = dx12Pipeline.GetPersistentResourceSize(stageIndex);
                    if (persistentBuffer != null && stagePersistentSize > 0)
                    {
                        m_StagePersistentBindings[stageIndex] = Dx12MLUtilities.CreateBufferBinding(
                            persistentBuffer,
                            dx12Pipeline.GetPersistentResourceOffset(stageIndex),
                            stagePersistentSize);
                    }

                    int executionDescriptorCount = Math.Max(1, checked((int)dx12Pipeline.GetRequiredDescriptorCount(stageIndex)));
                    Dx12DescriptorInfo executionDescriptorAllocation = device.AllocateCbvSrvUavDescriptor(executionDescriptorCount);
                    BindingTableDescription executionTableDescription = new BindingTableDescription
                    {
                        Dispatchable = dx12Pipeline.GetCompiledOperator(stageIndex),
                        CPUDescriptorHandle = executionDescriptorAllocation.CpuHandle,
                        GPUDescriptorHandle = executionDescriptorAllocation.GpuHandle,
                        SizeInDescriptors = checked((uint)executionDescriptorCount),
                    };

                    m_ExecutionDescriptorCounts[stageIndex] = executionDescriptorCount;
                    m_ExecutionDescriptorAllocations[stageIndex] = executionDescriptorAllocation;
                    m_ExecutionBindingTables[stageIndex] = device.DirectMLDevice.CreateBindingTable(ref executionTableDescription);
                    ++createdExecutionTables;
                }

                m_InitializerBindingTable = initializerBindingTable;
                m_TemporaryBuffer = temporaryBuffer;
                m_PersistentBuffer = persistentBuffer;
            }
            catch
            {
                initializerBindingTable?.Release();
                temporaryBuffer?.Dispose();
                persistentBuffer?.Dispose();
                for (int i = 0; i < m_IntermediateBuffers.Length; ++i)
                {
                    m_IntermediateBuffers[i]?.Dispose();
                }

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

        private BindingDescription[] ResolveStageInputs(Dx12MLProgram program, int stageIndex)
        {
            RHIMLOpDescriptor op = program.Ops[stageIndex];
            BindingDescription[] inputs = new BindingDescription[op.Inputs.Length];
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef inputRef = op.Inputs[i];
                if (inputRef.IsOpOutput)
                {
                    int intermediateIndex = program.GetOpIntermediateIndex(inputRef.OpIndex);
                    inputs[i] = m_IntermediateBindings[intermediateIndex];
                }
                else
                {
                    inputs[i] = m_ProgramInputBindings[inputRef.InputIndex];
                }
            }

            return inputs;
        }

        private BindingDescription[] ResolveStageOutputs(Dx12MLProgram program, int stageIndex)
        {
            // A stage output is either a program output (unconsumed op) or an intermediate.
            bool isProgramOutput = true;
            for (int i = 0; i < program.Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in program.Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput && inputRef.OpIndex == stageIndex && i != stageIndex)
                    {
                        isProgramOutput = false;
                        break;
                    }
                }

                if (!isProgramOutput)
                {
                    break;
                }
            }

            if (isProgramOutput)
            {
                int outputIndex = 0;
                for (int i = 0; i < stageIndex; ++i)
                {
                    bool unconsumed = true;
                    for (int j = 0; j < program.Ops.Length; ++j)
                    {
                        foreach (RHIMLOpTensorRef inputRef in program.Ops[j].Inputs)
                        {
                            if (inputRef.IsOpOutput && inputRef.OpIndex == i && j != i)
                            {
                                unconsumed = false;
                                break;
                            }
                        }

                        if (!unconsumed)
                        {
                            break;
                        }
                    }

                    if (unconsumed)
                    {
                        ++outputIndex;
                    }
                }

                return new[] { m_ProgramOutputBindings[outputIndex] };
            }

            int intermediateIndex = program.GetOpIntermediateIndex(stageIndex);
            return new[] { m_IntermediateBindings[intermediateIndex] };
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

            if (m_IntermediateBuffers != null)
            {
                for (int i = 0; i < m_IntermediateBuffers.Length; ++i)
                {
                    m_IntermediateBuffers[i]?.Dispose();
                }
                m_IntermediateBuffers = Array.Empty<Dx12Buffer>();
            }

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
