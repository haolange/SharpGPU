using System;

namespace SharpGPU
{
    /// <summary>
    /// Backend-private ML operator kinds used by cook tools and internal program IR.
    /// Not part of the RHI public surface (ADR-0052).
    /// </summary>
    internal enum ERHIMLOpKind : ushort
    {
        Unknown = 0,

        ElementWiseAdd = 1,
        ElementWiseSubtract = 2,
        ElementWiseMultiply = 3,
        ElementWiseDivide = 4,
        ElementWiseNegate = 5,

        ActivationRelu = 10,
        ActivationSigmoid = 11,
        ActivationTanh = 12,

        MatrixMultiply = 20,
        GeneralMatrixMultiply = 21,

        ActivationSoftmax = 30,
        MeanVarianceNormalization = 31,
        ReduceMean = 32,

        Reshape = 40,
        Transpose = 41,

        ElementWiseIdentity = 50,
    }

    internal enum ERHIMLMatrixTransform : byte
    {
        None = 0,
        Transpose = 1,
    }

    internal enum ERHIMLFusedActivation : byte
    {
        None = 0,
        Relu = 1,
        Sigmoid = 2,
        Tanh = 3,
    }

    internal struct RHIMLOpTensorRef
    {
        public int InputIndex;
        public int OpIndex;
        public bool IsOpOutput;

        public static RHIMLOpTensorRef FromInput(int inputIndex)
        {
            return new RHIMLOpTensorRef { InputIndex = inputIndex, OpIndex = -1, IsOpOutput = false };
        }

        public static RHIMLOpTensorRef FromOpOutput(int opIndex)
        {
            if (opIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(opIndex), "Op index must be non-negative.");
            }

            return new RHIMLOpTensorRef { InputIndex = -1, OpIndex = opIndex, IsOpOutput = true };
        }

        public readonly override string ToString() => IsOpOutput ? $"op[{OpIndex}].out" : $"input[{InputIndex}]";
    }

    internal struct RHIMLOpDescriptor
    {
        public ERHIMLOpKind Kind;
        public RHIMLOpTensorRef[] Inputs;
        public RHIMLTensorDescriptor Output;
        public string Name;

        public float Alpha;
        public float Beta;
        public ERHIMLMatrixTransform TransformA;
        public ERHIMLMatrixTransform TransformB;
        public ERHIMLFusedActivation FusedActivation;
        public float Epsilon;
        public int[]? Axes;

        public static RHIMLOpDescriptor Create(ERHIMLOpKind kind, RHIMLOpTensorRef[] inputs, in RHIMLTensorDescriptor output, string? name = null)
        {
            return new RHIMLOpDescriptor
            {
                Kind = kind,
                Inputs = inputs ?? Array.Empty<RHIMLOpTensorRef>(),
                Output = output,
                Name = name ?? string.Empty,
                Alpha = 1.0f,
                Beta = 1.0f,
                TransformA = ERHIMLMatrixTransform.None,
                TransformB = ERHIMLMatrixTransform.None,
                FusedActivation = ERHIMLFusedActivation.None,
                Epsilon = 0.0f,
                Axes = null,
            };
        }
    }

    /// <summary>
    /// Backend-private ordered op-sequence program IR. Cook tools serialize this into
    /// <see cref="RHIMLBinary"/> payloads; runtime never exposes it publicly (ADR-0052).
    /// </summary>
    internal struct RHIMLProgramIR
    {
        public string Name;
        public RHIMLTensorDescriptor[] Inputs;
        public RHIMLTensorDescriptor[] Outputs;
        public RHIMLOpDescriptor[] Ops;

        public static RHIMLProgramIR Create(string name, RHIMLTensorDescriptor[] inputs, RHIMLTensorDescriptor[] outputs, RHIMLOpDescriptor[] ops)
        {
            return new RHIMLProgramIR
            {
                Name = name,
                Inputs = inputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Outputs = outputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Ops = ops ?? Array.Empty<RHIMLOpDescriptor>(),
            };
        }
    }
}
