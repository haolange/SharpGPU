using System;
using SharpGPU.Core;

namespace SharpGPU
{
    /// <summary>
    /// Execution-layer ML operator kinds the RHI can lower to a backend-native operator
    /// (DirectML operator description on DX12, Metal 4 ML operation on Metal). This enum is
    /// intentionally independent of any higher-layer graph IR: it describes only what a backend
    /// can actually compile, and is the contract surface between a graph builder living above the
    /// RHI (e.g. SharpNeural) and the RHI execution layer. New backends / new DirectML operators
    /// are added here together with the matching translation in each backend's ML support module.
    /// </summary>
    public enum ERHIMLOpKind : ushort
    {
        Unknown = 0,

        /// <summary>Element-wise add of two tensors (broadcast via tensor descriptors).</summary>
        ElementWiseAdd = 1,
        /// <summary>Element-wise subtract: A - B.</summary>
        ElementWiseSubtract = 2,
        /// <summary>Element-wise multiply of two tensors.</summary>
        ElementWiseMultiply = 3,
        /// <summary>Element-wise divide: A / B.</summary>
        ElementWiseDivide = 4,
        /// <summary>Element-wise negate: -A.</summary>
        ElementWiseNegate = 5,

        /// <summary>Rectified linear unit: max(0, A).</summary>
        ActivationRelu = 10,
        /// <summary>Sigmoid: 1 / (1 + exp(-A)).</summary>
        ActivationSigmoid = 11,
        /// <summary>Hyperbolic tangent: tanh(A).</summary>
        ActivationTanh = 12,

        /// <summary>Matrix multiply of two 2D+ tensors (batched matmul).</summary>
        MatrixMultiply = 20,
        /// <summary>
        /// General matrix multiply: alpha * TransA(A) * TransB(B) + beta * C, with an optional
        /// fused activation applied to the output. Carries <see cref="RHIMLOpDescriptor.Alpha"/>,
        /// <see cref="RHIMLOpDescriptor.Beta"/>, <see cref="RHIMLOpDescriptor.TransformA"/>,
        /// <see cref="RHIMLOpDescriptor.TransformB"/> and
        /// <see cref="RHIMLOpDescriptor.FusedActivation"/>.
        /// </summary>
        GeneralMatrixMultiply = 21,

        /// <summary>Softmax over the trailing axis group.</summary>
        ActivationSoftmax = 30,
        /// <summary>
        /// Layer normalization (mean-variance normalization over a trailing axis group) with
        /// optional scale/bias and epsilon. Carries <see cref="RHIMLOpDescriptor.Epsilon"/>.
        /// </summary>
        MeanVarianceNormalization = 31,
        /// <summary>
        /// Reduction (mean) over the axes carried in <see cref="RHIMLOpDescriptor.Axes"/>.
        /// </summary>
        ReduceMean = 32,

        /// <summary>
        /// Reshape: reinterpret the element buffer under a new shape (zero-copy when the backend
        /// supports strided output tensors; otherwise a copy). The target shape is encoded on the
        /// output tensor descriptor dimensions.
        /// </summary>
        Reshape = 40,
        /// <summary>
        /// Transpose: permute axes according to <see cref="RHIMLOpDescriptor.Axes"/> (the permutation
        /// array). Implemented via strided output tensor descriptors where the backend supports it.
        /// </summary>
        Transpose = 41,

        /// <summary>Element-wise identity copy (used as the lowering target for reshape/transpose).</summary>
        ElementWiseIdentity = 50,
    }

    /// <summary>Matrix transpose flag for <see cref="ERHIMLOpKind.GeneralMatrixMultiply"/>.</summary>
    public enum ERHIMLMatrixTransform : byte
    {
        None = 0,
        Transpose = 1,
    }

    /// <summary>
    /// Fused activation kind that a backend may apply inside a GEMM / MVN operator. Backends that
    /// lack a fused-activation path emit a separate activation op instead. This is a property of
    /// the program descriptor, not an op kind on its own.
    /// </summary>
    public enum ERHIMLFusedActivation : byte
    {
        None = 0,
        Relu = 1,
        Sigmoid = 2,
        Tanh = 3,
    }

    /// <summary>
    /// Reference to a tensor consumed by an op inside a program: either a program input (by index)
    /// or the output of an earlier op (by op index). This is the dataflow edge of the linear op
    /// sequence — a DAG encoded as ordered ops plus predecessor references, not a graph DSL.
    /// </summary>
    public struct RHIMLOpTensorRef
    {
        /// <summary>Index of the program input tensor, when <see cref="IsOpOutput"/> is false.</summary>
        public int InputIndex;
        /// <summary>Index of the producing op within the program, when <see cref="IsOpOutput"/> is true.</summary>
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

    /// <summary>
    /// One ML operator inside a program: its kind, ordered input tensor references, the output
    /// tensor descriptor (which also carries the output shape/strides), and the scalar parameters
    /// the supported op set needs. Unknown parameters are ignored by backends that do not consume
    /// them. The descriptor is pure data; it holds no native handle.
    /// </summary>
    public struct RHIMLOpDescriptor
    {
        public ERHIMLOpKind Kind;
        public RHIMLOpTensorRef[] Inputs;
        public RHIMLTensorDescriptor Output;
        public string Name;

        // Scalar parameters (consumed per op kind; defaults match the common ONNX semantics).
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
    /// A backend-neutral ML program: an ordered sequence of <see cref="RHIMLOpDescriptor"/>s plus
    /// the named program-level input/output tensor descriptors. The sequence is a linear DAG: each
    /// op's inputs reference either a program input (by index) or the output of an earlier op (by
    /// op index). This generalizes the single 2-stage GEMM+ReLU program the DX12 backend shipped
    /// with (RFC-0003 §4.1) to an arbitrary N-stage op sequence, and is the contract surface that
    /// lets a graph builder above the RHI lower a subgraph without taking a hard compile-time
    /// dependency on any backend's private operator-description types. This is not a graph DSL — it
    /// is a constructible execution primitive, at the same layer as <see cref="RHIMLEncoder"/> /
    /// <see cref="RHIBuffer"/> (RFC-0003 §3.3 / ADR-0028).
    /// </summary>
    public struct RHIMLProgramDescriptor
    {
        public string Name;
        /// <summary>Program input tensor descriptors, referenced by ops via <see cref="RHIMLOpTensorRef.FromInput"/>.</summary>
        public RHIMLTensorDescriptor[] Inputs;
        /// <summary>Program output tensor descriptors. Each must be the output of exactly one op in <see cref="Ops"/>.</summary>
        public RHIMLTensorDescriptor[] Outputs;
        /// <summary>Ordered op sequence. Op inputs may reference program inputs or the output of any earlier op.</summary>
        public RHIMLOpDescriptor[] Ops;

        public static RHIMLProgramDescriptor Create(string name, RHIMLTensorDescriptor[] inputs, RHIMLTensorDescriptor[] outputs, RHIMLOpDescriptor[] ops)
        {
            return new RHIMLProgramDescriptor
            {
                Name = name,
                Inputs = inputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Outputs = outputs ?? Array.Empty<RHIMLTensorDescriptor>(),
                Ops = ops ?? Array.Empty<RHIMLOpDescriptor>(),
            };
        }
    }
}
