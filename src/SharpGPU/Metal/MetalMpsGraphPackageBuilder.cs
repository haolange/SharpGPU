using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal readonly struct MetalMpsGraphPackage
    {
        internal MTLLibrary Library { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }
        internal string PackageDirectory { get; }

        internal MetalMpsGraphPackage(MTLLibrary library, RHIMLTensorBindingInfo[] bindingInfos, string packageDirectory)
        {
            Library = library;
            BindingInfos = bindingInfos;
            PackageDirectory = packageDirectory;
        }
    }

    internal static class MetalMpsGraphPackageBuilder
    {
        internal const string EntryName = "main";

        private const string MpsGraphFrameworkPath = "/System/Library/Frameworks/MetalPerformanceShadersGraph.framework/MetalPerformanceShadersGraph";
        private const string PackageManifest = "{\n  \"mtlpackage\": {\n    \"version\": { \"major\": 1, \"minor\": 0, \"patch\": 0 },\n    \"pkgtype\": \"MLLibrary\",\n    \"content\": { \"mpspkgname\": \"library.mpsgraphpackage\" }\n  }\n}\n";
        private const ulong MpsDataTypeFloatBit = 0x10000000UL;
        private const ulong MpsDataTypeSignedBit = 0x20000000UL;
        private const ulong MpsDataTypeAlternateEncodingBit = 0x80000000UL;
        private const long MpsGraphDeploymentPlatformMacOS = 0L;

        private static IntPtr s_FrameworkHandle;

        private static readonly Selector s_Alloc = "alloc";
        private static readonly Selector s_Init = "init";
        private static readonly Selector s_ArrayWithObjectsCount = "arrayWithObjects:count:";
        private static readonly Selector s_Dictionary = "dictionary";
        private static readonly Selector s_DictionaryWithObjectsForKeysCount = "dictionaryWithObjects:forKeys:count:";
        private static readonly Selector s_InitWithShapeDataType = "initWithShape:dataType:";
        private static readonly Selector s_PlaceholderWithShapeDataTypeName = "placeholderWithShape:dataType:name:";
        private static readonly Selector s_CompileWithDeviceFeedsTargetTensorsTargetOperationsCompilationDescriptor = "compileWithDevice:feeds:targetTensors:targetOperations:compilationDescriptor:";
        private static readonly Selector s_SerializeToMPSGraphPackageAtURLDescriptor = "serializeToMPSGraphPackageAtURL:descriptor:";
        private static readonly Selector s_SetDeploymentPlatform = "setDeploymentPlatform:";
        private static readonly Selector s_SetMinimumDeploymentTarget = "setMinimumDeploymentTarget:";
        private static readonly Selector s_ReflectionForFunctionWithName = "reflectionForFunctionWithName:";
        private static readonly Selector s_Bindings = "bindings";

        private static readonly Selector s_IdentityWithTensorName = "identityWithTensor:name:";
        private static readonly Selector s_NegativeWithTensorName = "negativeWithTensor:name:";
        private static readonly Selector s_AdditionWithPrimaryTensorSecondaryTensorName = "additionWithPrimaryTensor:secondaryTensor:name:";
        private static readonly Selector s_SubtractionWithPrimaryTensorSecondaryTensorName = "subtractionWithPrimaryTensor:secondaryTensor:name:";
        private static readonly Selector s_MultiplicationWithPrimaryTensorSecondaryTensorName = "multiplicationWithPrimaryTensor:secondaryTensor:name:";
        private static readonly Selector s_DivisionWithPrimaryTensorSecondaryTensorName = "divisionWithPrimaryTensor:secondaryTensor:name:";
        private static readonly Selector s_SquareRootWithTensorName = "squareRootWithTensor:name:";
        private static readonly Selector s_MatrixMultiplicationWithPrimaryTensorSecondaryTensorName = "matrixMultiplicationWithPrimaryTensor:secondaryTensor:name:";
        private static readonly Selector s_ReluWithTensorName = "reLUWithTensor:name:";
        private static readonly Selector s_SigmoidWithTensorName = "sigmoidWithTensor:name:";
        private static readonly Selector s_TanhWithTensorName = "tanhWithTensor:name:";
        private static readonly Selector s_SoftMaxWithTensorAxisName = "softMaxWithTensor:axis:name:";
        private static readonly Selector s_ExpandDimsOfTensorAxisName = "expandDimsOfTensor:axis:name:";
        private static readonly Selector s_ReductionSumWithTensorAxisName = "reductionSumWithTensor:axis:name:";
        private static readonly Selector s_ReshapeTensorWithShapeName = "reshapeTensor:withShape:name:";
        private static readonly Selector s_TransposeTensorPermutationName = "transposeTensor:permutation:name:";
        private static readonly Selector s_TransposeTensorDimensionWithDimensionName = "transposeTensor:dimension:withDimension:name:";
        private static readonly Selector s_MeanOfTensorAxesName = "meanOfTensor:axes:name:";
        private static readonly Selector s_VarianceOfTensorMeanTensorAxesName = "varianceOfTensor:meanTensor:axes:name:";
        private static readonly Selector s_ConstantWithScalarDataType = "constantWithScalar:dataType:";

        internal static bool IsAvailable(out string? unavailableReason)
        {
            unavailableReason = null;

            if (!OperatingSystem.IsMacOSVersionAtLeast(14))
            {
                unavailableReason = "Metal ML MPSGraph package serialization requires macOS 14 or newer.";
                return false;
            }

            if (!EnsureFrameworkLoaded(out unavailableReason))
            {
                return false;
            }

            string[] requiredClasses =
            {
                "MPSGraph",
                "MPSGraphShapedType",
                "MPSGraphExecutableSerializationDescriptor",
                "NSArray",
                "NSDictionary",
            };

            foreach (string className in requiredClasses)
            {
                if (ObjectiveCRuntime.objc_getClass(className) == IntPtr.Zero)
                {
                    unavailableReason = $"Metal ML MPSGraph package lowering requires Objective-C class '{className}'.";
                    return false;
                }
            }

            return true;
        }

        internal static MetalMpsGraphPackage Build(MetalDevice device, in RHIMLProgramDescriptor descriptor)
        {
            if (!IsAvailable(out string? unavailableReason))
            {
                throw new NotSupportedException(unavailableReason ?? "Metal ML MPSGraph package lowering is unavailable.");
            }

            ValidateProgramDescriptor(descriptor);

            string packageDirectory = CreatePackageDirectory(descriptor.Name);
            string graphPackagePath = Path.Combine(packageDirectory, "library.mpsgraphpackage");
            IntPtr graph = IntPtr.Zero;
            IntPtr serializationDescriptor = IntPtr.Zero;
            MTLLibrary library = default;
            NSAutoreleasePool pool = NSAutoreleasePool.Begin();

            try
            {
                Directory.CreateDirectory(packageDirectory);

                BuildGraph(descriptor, out graph, out IntPtr executable, out IntPtr[] targetTensorPointers, out RHIMLTensorBindingInfo[] bindingInfos);
                if (executable == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Metal ML MPSGraph compile returned a null executable.");
                }

                serializationDescriptor = AllocInit("MPSGraphExecutableSerializationDescriptor");
                ObjectiveCRuntime.objc_msgSend(serializationDescriptor, s_SetDeploymentPlatform, MpsGraphDeploymentPlatformMacOS);
                ObjectiveCRuntime.objc_msgSend(serializationDescriptor, s_SetMinimumDeploymentTarget, new NSString("26.0").NativePtr);
                ObjectiveCRuntime.objc_msgSend(
                    executable,
                    s_SerializeToMPSGraphPackageAtURLDescriptor,
                    NSURL.FileURLWithPath(new NSString(graphPackagePath)).NativePtr,
                    serializationDescriptor);

                if (!Directory.Exists(graphPackagePath))
                {
                    throw new InvalidOperationException("Metal ML MPSGraph serialization did not create library.mpsgraphpackage.");
                }

                File.WriteAllText(Path.Combine(packageDirectory, "manifest.json"), PackageManifest);

                NSError libraryError = default;
                library = device.NativeDevice.NewLibrary(NSURL.FileURLWithPath(new NSString(packageDirectory)), ref libraryError);
                if (library.NativePtr == IntPtr.Zero)
                {
                    string errorText = libraryError.NativePtr != IntPtr.Zero ? libraryError.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to load Metal ML package '{packageDirectory}': {errorText}");
                }

                ValidateReflection(library, targetTensorPointers.Length + descriptor.Inputs.Length, descriptor.Name);
                return new MetalMpsGraphPackage(library, bindingInfos, packageDirectory);
            }
            catch
            {
                if (library.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(library.NativePtr);
                }

                TryDeleteDirectory(packageDirectory);
                throw;
            }
            finally
            {
                if (serializationDescriptor != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(serializationDescriptor);
                }

                if (graph != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(graph);
                }

                pool.Drain();
            }
        }

        private static bool EnsureFrameworkLoaded(out string? unavailableReason)
        {
            unavailableReason = null;
            if (s_FrameworkHandle != IntPtr.Zero)
            {
                return true;
            }

            if (NativeLibrary.TryLoad(MpsGraphFrameworkPath, out s_FrameworkHandle))
            {
                return true;
            }

            unavailableReason = $"Metal ML MPSGraph package lowering requires {MpsGraphFrameworkPath}.";
            return false;
        }

        private static void ValidateProgramDescriptor(in RHIMLProgramDescriptor descriptor)
        {
            if (descriptor.Inputs.Length == 0)
            {
                throw new InvalidOperationException("Metal ML program descriptor must contain at least one input tensor.");
            }

            if (descriptor.Outputs.Length == 0)
            {
                throw new InvalidOperationException("Metal ML program descriptor must contain at least one output tensor.");
            }

            if (descriptor.Ops.Length == 0)
            {
                throw new InvalidOperationException("Metal ML program descriptor must contain at least one op.");
            }

            for (int i = 0; i < descriptor.Inputs.Length; ++i)
            {
                ValidateTensorDescriptor($"input[{i}]", descriptor.Inputs[i]);
            }

            for (int i = 0; i < descriptor.Outputs.Length; ++i)
            {
                ValidateTensorDescriptor($"output[{i}]", descriptor.Outputs[i]);
            }

            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                ValidateTensorDescriptor($"op[{i}].output", descriptor.Ops[i].Output);
            }
        }

        private static void ValidateTensorDescriptor(string label, in RHIMLTensorDescriptor descriptor)
        {
            if (!IsSupportedDataType(descriptor.DataType))
            {
                throw new NotSupportedException($"Metal ML MPSGraph lowering supports Float32, Float16 and BFloat16 tensors only. {label} uses '{descriptor.DataType}'.");
            }

            if (descriptor.Dimensions.Length == 0)
            {
                throw new NotSupportedException($"Metal ML MPSGraph lowering requires ranked tensors. {label} is scalar/rank-0.");
            }
        }

        private static bool IsSupportedDataType(ERHIMLDataType dataType)
        {
            return dataType == ERHIMLDataType.Float32
                || dataType == ERHIMLDataType.Float16
                || dataType == ERHIMLDataType.BFloat16;
        }

        private static void BuildGraph(
            in RHIMLProgramDescriptor descriptor,
            out IntPtr graph,
            out IntPtr executable,
            out IntPtr[] targetTensorPointers,
            out RHIMLTensorBindingInfo[] bindingInfos)
        {
            graph = AllocInit("MPSGraph");
            if (graph == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create MPSGraph for Metal ML lowering.");
            }

            bool[] isProgramOutput = BuildProgramOutputMap(descriptor, out int[] opToProgramOutputIndex);
            IntPtr[] inputTensors = new IntPtr[descriptor.Inputs.Length];
            IntPtr[] shapedTypes = new IntPtr[descriptor.Inputs.Length];
            IntPtr[] opOutputs = new IntPtr[descriptor.Ops.Length];
            RHIMLTensorDescriptor[] opOutputDescriptors = BuildOpOutputDescriptors(descriptor, isProgramOutput, opToProgramOutputIndex);

            try
            {
                for (int i = 0; i < descriptor.Inputs.Length; ++i)
                {
                    NSArray shape = CreateNSNumberArray(descriptor.Inputs[i].Dimensions.Span);
                    inputTensors[i] = ObjectiveCRuntime.IntPtr_objc_msgSend(
                        graph,
                        s_PlaceholderWithShapeDataTypeName,
                        shape.NativePtr,
                        ConvertToMpsDataType(descriptor.Inputs[i].DataType),
                        new NSString($"arg{i}").NativePtr);

                    if (inputTensors[i] == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"MPSGraph failed to create placeholder for Metal ML input[{i}].");
                    }

                    shapedTypes[i] = CreateShapedType(descriptor.Inputs[i]);
                }

                GraphBuildContext context = new GraphBuildContext(graph, descriptor.Inputs, opOutputDescriptors, inputTensors, opOutputs);
                for (int i = 0; i < descriptor.Ops.Length; ++i)
                {
                    opOutputs[i] = LowerOp(context, descriptor.Ops[i], i);
                    if (opOutputs[i] == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"MPSGraph lowering returned a null tensor for op[{i}] '{descriptor.Ops[i].Name}'.");
                    }
                }

                targetTensorPointers = new IntPtr[descriptor.Outputs.Length];
                for (int i = 0; i < descriptor.Ops.Length; ++i)
                {
                    if (isProgramOutput[i])
                    {
                        int outputIndex = opToProgramOutputIndex[i];
                        targetTensorPointers[outputIndex] = opOutputs[i];
                    }
                }

                for (int i = 0; i < targetTensorPointers.Length; ++i)
                {
                    if (targetTensorPointers[i] == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Metal ML program output[{i}] is not produced by the op sequence.");
                    }
                }

                NSDictionary feeds = CreateNSDictionaryFromPointers(shapedTypes, inputTensors);
                NSArray targetTensors = MetalArrayHelper.CreateNSArrayFromPointers(targetTensorPointers);
                executable = ObjectiveCRuntime.IntPtr_objc_msgSend(
                    graph,
                    s_CompileWithDeviceFeedsTargetTensorsTargetOperationsCompilationDescriptor,
                    IntPtr.Zero,
                    feeds.NativePtr,
                    targetTensors.NativePtr,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (executable == IntPtr.Zero)
                {
                    throw new InvalidOperationException("MPSGraph failed to compile the Metal ML package executable.");
                }

                bindingInfos = BuildBindingInfos(descriptor);
            }
            finally
            {
                for (int i = 0; i < shapedTypes.Length; ++i)
                {
                    if (shapedTypes[i] != IntPtr.Zero)
                    {
                        ObjectiveCRuntime.Release(shapedTypes[i]);
                    }
                }
            }
        }

        private static RHIMLTensorDescriptor[] BuildOpOutputDescriptors(in RHIMLProgramDescriptor descriptor, bool[] isProgramOutput, int[] opToProgramOutputIndex)
        {
            RHIMLTensorDescriptor[] result = new RHIMLTensorDescriptor[descriptor.Ops.Length];
            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                result[i] = isProgramOutput[i]
                    ? RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[opToProgramOutputIndex[i]])
                    : RHIMLHelpers.CloneLayoutDescriptor(descriptor.Ops[i].Output);
            }

            return result;
        }

        private static bool[] BuildProgramOutputMap(in RHIMLProgramDescriptor descriptor, out int[] opToProgramOutputIndex)
        {
            bool[] isProgramOutput = new bool[descriptor.Ops.Length];
            opToProgramOutputIndex = new int[descriptor.Ops.Length];
            Array.Fill(isProgramOutput, true);
            Array.Fill(opToProgramOutputIndex, -1);

            for (int i = 0; i < descriptor.Ops.Length; ++i)
            {
                foreach (RHIMLOpTensorRef inputRef in descriptor.Ops[i].Inputs)
                {
                    if (inputRef.IsOpOutput)
                    {
                        if ((uint)inputRef.OpIndex >= (uint)descriptor.Ops.Length)
                        {
                            throw new InvalidOperationException($"Metal ML op[{i}] references invalid producer op[{inputRef.OpIndex}].");
                        }

                        if (inputRef.OpIndex >= i)
                        {
                            throw new InvalidOperationException($"Metal ML op[{i}] references non-earlier producer op[{inputRef.OpIndex}].");
                        }

                        isProgramOutput[inputRef.OpIndex] = false;
                    }
                    else if ((uint)inputRef.InputIndex >= (uint)descriptor.Inputs.Length)
                    {
                        throw new InvalidOperationException($"Metal ML op[{i}] references invalid program input[{inputRef.InputIndex}].");
                    }
                }
            }

            int outputCount = 0;
            for (int i = 0; i < isProgramOutput.Length; ++i)
            {
                if (isProgramOutput[i])
                {
                    opToProgramOutputIndex[i] = outputCount++;
                }
            }

            if (outputCount != descriptor.Outputs.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML program descriptor output count mismatch: {outputCount} op(s) produce unconsumed outputs but {descriptor.Outputs.Length} program output(s) were declared.");
            }

            return isProgramOutput;
        }

        private static RHIMLTensorBindingInfo[] BuildBindingInfos(in RHIMLProgramDescriptor descriptor)
        {
            List<RHIMLTensorBindingInfo> bindings = new List<RHIMLTensorBindingInfo>(descriptor.Inputs.Length + descriptor.Outputs.Length);
            for (int i = 0; i < descriptor.Inputs.Length; ++i)
            {
                bindings.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"arg{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Inputs[i]),
                });
            }

            for (int i = 0; i < descriptor.Outputs.Length; ++i)
            {
                bindings.Add(new RHIMLTensorBindingInfo
                {
                    Name = $"out{i}",
                    Index = (uint)i,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(descriptor.Outputs[i]),
                });
            }

            return bindings.ToArray();
        }

        private static IntPtr LowerOp(GraphBuildContext context, in RHIMLOpDescriptor op, int opIndex)
        {
            string name = string.IsNullOrWhiteSpace(op.Name) ? $"op{opIndex}" : op.Name;
            return op.Kind switch
            {
                ERHIMLOpKind.ElementWiseAdd => Binary(context, op, s_AdditionWithPrimaryTensorSecondaryTensorName, name),
                ERHIMLOpKind.ElementWiseSubtract => Binary(context, op, s_SubtractionWithPrimaryTensorSecondaryTensorName, name),
                ERHIMLOpKind.ElementWiseMultiply => Binary(context, op, s_MultiplicationWithPrimaryTensorSecondaryTensorName, name),
                ERHIMLOpKind.ElementWiseDivide => Binary(context, op, s_DivisionWithPrimaryTensorSecondaryTensorName, name),
                ERHIMLOpKind.ElementWiseNegate => Unary(context, op, s_NegativeWithTensorName, name),
                ERHIMLOpKind.ElementWiseIdentity => Identity(context, op, name),
                ERHIMLOpKind.ActivationRelu => Unary(context, op, s_ReluWithTensorName, name),
                ERHIMLOpKind.ActivationSigmoid => Unary(context, op, s_SigmoidWithTensorName, name),
                ERHIMLOpKind.ActivationTanh => Unary(context, op, s_TanhWithTensorName, name),
                ERHIMLOpKind.MatrixMultiply => MatrixMultiply(context, op, name, allowGemmC: false),
                ERHIMLOpKind.GeneralMatrixMultiply => MatrixMultiply(context, op, name, allowGemmC: true),
                ERHIMLOpKind.ActivationSoftmax => Softmax(context, op, name),
                ERHIMLOpKind.MeanVarianceNormalization => MeanVarianceNormalization(context, op, name),
                ERHIMLOpKind.ReduceMean => ReduceMean(context, op, name),
                ERHIMLOpKind.Reshape => Reshape(context, op, name),
                ERHIMLOpKind.Transpose => Transpose(context, op, name),
                _ => throw new NotSupportedException($"Metal ML MPSGraph lowering does not support RHI ML op kind '{op.Kind}' (op '{name}')."),
            };
        }

        private static IntPtr Unary(GraphBuildContext context, in RHIMLOpDescriptor op, Selector selector, string name)
        {
            RequireInputCount(op, 1, name);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, selector, context.ResolveTensor(op.Inputs[0]), new NSString(name).NativePtr);
        }

        private static IntPtr Identity(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            return context.ResolveTensor(op.Inputs[0]);
        }

        private static IntPtr Binary(GraphBuildContext context, in RHIMLOpDescriptor op, Selector selector, string name)
        {
            RequireInputCount(op, 2, name);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                selector,
                context.ResolveTensor(op.Inputs[0]),
                context.ResolveTensor(op.Inputs[1]),
                new NSString(name).NativePtr);
        }

        private static IntPtr MatrixMultiply(GraphBuildContext context, in RHIMLOpDescriptor op, string name, bool allowGemmC)
        {
            RequireInputCount(op, 2, name);
            IntPtr a = context.ResolveTensor(op.Inputs[0]);
            IntPtr b = context.ResolveTensor(op.Inputs[1]);

            if (op.TransformA == ERHIMLMatrixTransform.Transpose)
            {
                a = TransposeLastTwoDimensions(context.Graph, a, context.ResolveDescriptor(op.Inputs[0]), name + ".transA");
            }

            if (op.TransformB == ERHIMLMatrixTransform.Transpose)
            {
                b = TransposeLastTwoDimensions(context.Graph, b, context.ResolveDescriptor(op.Inputs[1]), name + ".transB");
            }

            ValidateMatrixMultiplyShape(context.ResolveDescriptor(op.Inputs[0]), op.TransformA, context.ResolveDescriptor(op.Inputs[1]), op.TransformB, name);
            IntPtr result = ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_MatrixMultiplicationWithPrimaryTensorSecondaryTensorName,
                a,
                b,
                new NSString(name + ".matmul").NativePtr);

            if (!IsOne(op.Alpha))
            {
                result = MultiplyByScalar(context.Graph, result, op.Alpha, op.Output.DataType, name + ".alpha");
            }

            if (allowGemmC && op.Inputs.Length >= 3 && !IsZero(op.Beta))
            {
                IntPtr c = context.ResolveTensor(op.Inputs[2]);
                if (!IsOne(op.Beta))
                {
                    c = MultiplyByScalar(context.Graph, c, op.Beta, context.ResolveDescriptor(op.Inputs[2]).DataType, name + ".beta");
                }

                result = ObjectiveCRuntime.IntPtr_objc_msgSend(
                    context.Graph,
                    s_AdditionWithPrimaryTensorSecondaryTensorName,
                    result,
                    c,
                    new NSString(name + ".bias").NativePtr);
            }

            return ApplyFusedActivation(context.Graph, result, op.FusedActivation, name + ".activation");
        }

        private static void ValidateMatrixMultiplyShape(
            in RHIMLTensorDescriptor a,
            ERHIMLMatrixTransform transformA,
            in RHIMLTensorDescriptor b,
            ERHIMLMatrixTransform transformB,
            string name)
        {
            if (a.Dimensions.Length != 2 || b.Dimensions.Length != 2)
            {
                throw new NotSupportedException($"Metal ML MatMul '{name}' supports rank-2 tensors only.");
            }

            uint aRows = transformA == ERHIMLMatrixTransform.Transpose ? a.Dimensions.Span[1] : a.Dimensions.Span[0];
            uint aCols = transformA == ERHIMLMatrixTransform.Transpose ? a.Dimensions.Span[0] : a.Dimensions.Span[1];
            uint bRows = transformB == ERHIMLMatrixTransform.Transpose ? b.Dimensions.Span[1] : b.Dimensions.Span[0];
            uint bCols = transformB == ERHIMLMatrixTransform.Transpose ? b.Dimensions.Span[0] : b.Dimensions.Span[1];
            _ = aRows;
            _ = bCols;
            if (aCols != bRows)
            {
                throw new NotSupportedException($"Metal ML MatMul '{name}' inner dimension mismatch: A cols={aCols}, B rows={bRows}.");
            }
        }

        private static IntPtr Softmax(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            RHIMLTensorDescriptor inputDescriptor = context.ResolveDescriptor(op.Inputs[0]);
            int axis = ResolveSingleAxis(op.Axes, inputDescriptor.Dimensions.Length);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_SoftMaxWithTensorAxisName,
                context.ResolveTensor(op.Inputs[0]),
                (long)axis,
                new NSString(name).NativePtr);
        }

        private static IntPtr MeanVarianceNormalization(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            IntPtr source = context.ResolveTensor(op.Inputs[0]);
            int[] axes = ResolveAxes(op.Axes, context.ResolveDescriptor(op.Inputs[0]).Dimensions.Length);
            NSArray axesArray = CreateNSNumberArray(axes);
            IntPtr mean = ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, s_MeanOfTensorAxesName, source, axesArray.NativePtr, new NSString(name + ".mean").NativePtr);
            IntPtr variance = ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, s_VarianceOfTensorMeanTensorAxesName, source, mean, axesArray.NativePtr, new NSString(name + ".variance").NativePtr);
            float epsilon = op.Epsilon > 0.0f ? op.Epsilon : 1e-5f;

            IntPtr centered = ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_SubtractionWithPrimaryTensorSecondaryTensorName,
                source,
                mean,
                new NSString(name + ".center").NativePtr);

            IntPtr epsilonTensor = ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, s_ConstantWithScalarDataType, (double)epsilon, ConvertToMpsDataType(op.Output.DataType));
            if (epsilonTensor == IntPtr.Zero)
            {
                throw new InvalidOperationException($"MPSGraph failed to create epsilon constant for '{name}'.");
            }

            IntPtr varianceEpsilon = ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_AdditionWithPrimaryTensorSecondaryTensorName,
                variance,
                epsilonTensor,
                new NSString(name + ".variance_epsilon").NativePtr);
            IntPtr std = ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, s_SquareRootWithTensorName, varianceEpsilon, new NSString(name + ".std").NativePtr);
            IntPtr result = ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_DivisionWithPrimaryTensorSecondaryTensorName,
                centered,
                std,
                new NSString(name + ".normalized").NativePtr);

            if (op.Inputs.Length >= 2)
            {
                result = ObjectiveCRuntime.IntPtr_objc_msgSend(
                    context.Graph,
                    s_MultiplicationWithPrimaryTensorSecondaryTensorName,
                    result,
                    context.ResolveTensor(op.Inputs[1]),
                    new NSString(name + ".gamma").NativePtr);
            }

            if (op.Inputs.Length >= 3)
            {
                result = ObjectiveCRuntime.IntPtr_objc_msgSend(
                    context.Graph,
                    s_AdditionWithPrimaryTensorSecondaryTensorName,
                    result,
                    context.ResolveTensor(op.Inputs[2]),
                    new NSString(name + ".beta").NativePtr);
            }

            return result;
        }

        private static IntPtr ReduceMean(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            int[] axes = ResolveAxes(op.Axes, context.ResolveDescriptor(op.Inputs[0]).Dimensions.Length);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_MeanOfTensorAxesName,
                context.ResolveTensor(op.Inputs[0]),
                CreateNSNumberArray(axes).NativePtr,
                new NSString(name).NativePtr);
        }

        private static IntPtr Reshape(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            IntPtr reshaped = ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_ReshapeTensorWithShapeName,
                context.ResolveTensor(op.Inputs[0]),
                CreateNSNumberArray(op.Output.Dimensions.Span).NativePtr,
                new NSString(name).NativePtr);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(context.Graph, s_IdentityWithTensorName, reshaped, new NSString(name + ".materialize").NativePtr);
        }

        private static IntPtr Transpose(GraphBuildContext context, in RHIMLOpDescriptor op, string name)
        {
            RequireInputCount(op, 1, name);
            int rank = context.ResolveDescriptor(op.Inputs[0]).Dimensions.Length;
            int[] permutation = ResolvePermutation(op.Axes, rank);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                context.Graph,
                s_TransposeTensorPermutationName,
                context.ResolveTensor(op.Inputs[0]),
                CreateNSNumberArray(permutation).NativePtr,
                new NSString(name).NativePtr);
        }

        private static IntPtr TransposeLastTwoDimensions(IntPtr graph, IntPtr tensor, in RHIMLTensorDescriptor descriptor, string name)
        {
            int rank = descriptor.Dimensions.Length;
            if (rank < 2)
            {
                throw new NotSupportedException("Metal ML GEMM transpose requires rank >= 2 tensors.");
            }

            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                graph,
                s_TransposeTensorDimensionWithDimensionName,
                tensor,
                (ulong)(rank - 2),
                (ulong)(rank - 1),
                new NSString(name).NativePtr);
        }

        private static IntPtr MultiplyByScalar(IntPtr graph, IntPtr tensor, float scalar, ERHIMLDataType dataType, string name)
        {
            IntPtr scalarTensor = ObjectiveCRuntime.IntPtr_objc_msgSend(graph, s_ConstantWithScalarDataType, (double)scalar, ConvertToMpsDataType(dataType));
            if (scalarTensor == IntPtr.Zero)
            {
                throw new InvalidOperationException($"MPSGraph failed to create scalar constant for '{name}'.");
            }

            return ObjectiveCRuntime.IntPtr_objc_msgSend(
                graph,
                s_MultiplicationWithPrimaryTensorSecondaryTensorName,
                tensor,
                scalarTensor,
                new NSString(name).NativePtr);
        }

        private static IntPtr ApplyFusedActivation(IntPtr graph, IntPtr tensor, ERHIMLFusedActivation activation, string name)
        {
            return activation switch
            {
                ERHIMLFusedActivation.None => tensor,
                ERHIMLFusedActivation.Relu => ObjectiveCRuntime.IntPtr_objc_msgSend(graph, s_ReluWithTensorName, tensor, new NSString(name).NativePtr),
                ERHIMLFusedActivation.Sigmoid => ObjectiveCRuntime.IntPtr_objc_msgSend(graph, s_SigmoidWithTensorName, tensor, new NSString(name).NativePtr),
                ERHIMLFusedActivation.Tanh => ObjectiveCRuntime.IntPtr_objc_msgSend(graph, s_TanhWithTensorName, tensor, new NSString(name).NativePtr),
                _ => throw new NotSupportedException($"Metal ML MPSGraph lowering does not support fused activation '{activation}'."),
            };
        }

        private static void RequireInputCount(in RHIMLOpDescriptor op, int minimum, string name)
        {
            if (op.Inputs.Length < minimum)
            {
                throw new InvalidOperationException($"Metal ML op '{name}' ({op.Kind}) requires at least {minimum} input(s), actual={op.Inputs.Length}.");
            }
        }

        private static int ResolveSingleAxis(int[]? axes, int rank)
        {
            if (rank <= 0)
            {
                throw new NotSupportedException("Metal ML axis resolution requires rank >= 1 tensors.");
            }

            if (axes == null || axes.Length == 0)
            {
                return rank - 1;
            }

            if (axes.Length != 1)
            {
                throw new NotSupportedException($"Metal ML op expects exactly one axis, actual={axes.Length}.");
            }

            return NormalizeAxis(axes[0], rank);
        }

        private static int[] ResolveAxes(int[]? axes, int rank)
        {
            if (rank <= 0)
            {
                throw new NotSupportedException("Metal ML axis resolution requires rank >= 1 tensors.");
            }

            if (axes == null || axes.Length == 0)
            {
                int[] allAxes = new int[rank];
                for (int i = 0; i < rank; ++i)
                {
                    allAxes[i] = i;
                }

                return allAxes;
            }

            int[] resolved = new int[axes.Length];
            for (int i = 0; i < axes.Length; ++i)
            {
                resolved[i] = NormalizeAxis(axes[i], rank);
            }

            return resolved;
        }

        private static int[] ResolvePermutation(int[]? axes, int rank)
        {
            int[] resolved;
            if (axes == null || axes.Length == 0)
            {
                resolved = new int[rank];
                for (int i = 0; i < rank; ++i)
                {
                    resolved[i] = rank - 1 - i;
                }

                return resolved;
            }

            if (axes.Length != rank)
            {
                throw new NotSupportedException($"Metal ML transpose permutation rank mismatch. rank={rank}, permutation={axes.Length}.");
            }

            resolved = new int[rank];
            bool[] seen = new bool[rank];
            for (int i = 0; i < rank; ++i)
            {
                int axis = NormalizeAxis(axes[i], rank);
                if (seen[axis])
                {
                    throw new NotSupportedException($"Metal ML transpose permutation contains duplicate axis {axis}.");
                }

                seen[axis] = true;
                resolved[i] = axis;
            }

            return resolved;
        }

        private static int NormalizeAxis(int axis, int rank)
        {
            int resolved = axis < 0 ? axis + rank : axis;
            if ((uint)resolved >= (uint)rank)
            {
                throw new NotSupportedException($"Metal ML axis {axis} is outside tensor rank {rank}.");
            }

            return resolved;
        }

        private static bool IsOne(float value) => Math.Abs(value - 1.0f) <= 1e-6f;
        private static bool IsZero(float value) => Math.Abs(value) <= 1e-6f;

        private static ulong ConvertToMpsDataType(ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => MpsDataTypeFloatBit | 32UL,
                ERHIMLDataType.Float16 => MpsDataTypeFloatBit | 16UL,
                ERHIMLDataType.BFloat16 => MpsDataTypeAlternateEncodingBit | (MpsDataTypeFloatBit | 16UL),
                ERHIMLDataType.Int32 => MpsDataTypeSignedBit | 32UL,
                _ => throw new NotSupportedException($"Metal ML MPSGraph lowering does not support tensor data type '{dataType}'."),
            };
        }

        private static IntPtr CreateShapedType(in RHIMLTensorDescriptor descriptor)
        {
            IntPtr shapedType = ObjectiveCRuntime.IntPtr_objc_msgSend(GetClass("MPSGraphShapedType"), s_Alloc);
            shapedType = ObjectiveCRuntime.IntPtr_objc_msgSend(
                shapedType,
                s_InitWithShapeDataType,
                CreateNSNumberArray(descriptor.Dimensions.Span).NativePtr,
                ConvertToMpsDataType(descriptor.DataType));

            if (shapedType == IntPtr.Zero)
            {
                throw new InvalidOperationException("MPSGraph failed to create shaped type for a Metal ML feed.");
            }

            return shapedType;
        }

        private static unsafe NSArray CreateNSNumberArray(ReadOnlySpan<uint> values)
        {
            IntPtr[] numbers = new IntPtr[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                numbers[i] = NSNumber.Number((long)values[i]).NativePtr;
            }

            fixed (IntPtr* ptr = numbers)
            {
                return new NSArray(ObjectiveCRuntime.IntPtr_objc_msgSend(GetClass("NSArray"), s_ArrayWithObjectsCount, (IntPtr)ptr, (ulong)numbers.Length));
            }
        }

        private static unsafe NSArray CreateNSNumberArray(ReadOnlySpan<int> values)
        {
            IntPtr[] numbers = new IntPtr[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                numbers[i] = NSNumber.Number((long)values[i]).NativePtr;
            }

            fixed (IntPtr* ptr = numbers)
            {
                return new NSArray(ObjectiveCRuntime.IntPtr_objc_msgSend(GetClass("NSArray"), s_ArrayWithObjectsCount, (IntPtr)ptr, (ulong)numbers.Length));
            }
        }

        private static unsafe NSDictionary CreateNSDictionaryFromPointers(ReadOnlySpan<IntPtr> objects, ReadOnlySpan<IntPtr> keys)
        {
            if (objects.Length != keys.Length)
            {
                throw new ArgumentException("NSDictionary object/key counts must match.");
            }

            if (objects.Length == 0)
            {
                return new NSDictionary(ObjectiveCRuntime.IntPtr_objc_msgSend(GetClass("NSDictionary"), s_Dictionary));
            }

            fixed (IntPtr* objectPtr = objects)
            fixed (IntPtr* keyPtr = keys)
            {
                return new NSDictionary(ObjectiveCRuntime.IntPtr_objc_msgSend(
                    GetClass("NSDictionary"),
                    s_DictionaryWithObjectsForKeysCount,
                    (IntPtr)objectPtr,
                    (IntPtr)keyPtr,
                    (ulong)objects.Length));
            }
        }

        private static IntPtr AllocInit(string className)
        {
            IntPtr nativeClass = GetClass(className);
            IntPtr instance = ObjectiveCRuntime.IntPtr_objc_msgSend(nativeClass, s_Alloc);
            return ObjectiveCRuntime.IntPtr_objc_msgSend(instance, s_Init);
        }

        private static IntPtr GetClass(string className)
        {
            IntPtr nativeClass = ObjectiveCRuntime.objc_getClass(className);
            if (nativeClass == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Objective-C class '{className}' is unavailable.");
            }

            return nativeClass;
        }

        private static void ValidateReflection(MTLLibrary library, int expectedBindingCount, string programName)
        {
            IntPtr reflection = ObjectiveCRuntime.IntPtr_objc_msgSend(library.NativePtr, s_ReflectionForFunctionWithName, new NSString(EntryName).NativePtr);
            if (reflection == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Metal ML package '{programName}' did not expose reflection for entry '{EntryName}'.");
            }

            NSArray bindings = new NSArray(ObjectiveCRuntime.IntPtr_objc_msgSend(reflection, s_Bindings));
            if (bindings.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Metal ML package '{programName}' reflection did not expose tensor bindings.");
            }

            if ((int)bindings.Count != expectedBindingCount)
            {
                throw new InvalidOperationException(
                    $"Metal ML package '{programName}' reflection binding count mismatch. reflection={bindings.Count}, expected={expectedBindingCount}.");
            }
        }

        private static string CreatePackageDirectory(string programName)
        {
            string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(programName) ? "program" : programName);
            if (safeName.Length > 64)
            {
                safeName = safeName.Substring(0, 64);
            }

            return Path.Combine(Path.GetTempPath(), "SharpGPU", "MetalML", safeName + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".mtlpackage");
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        private readonly struct GraphBuildContext
        {
            internal IntPtr Graph { get; }

            private readonly RHIMLTensorDescriptor[] m_InputDescriptors;
            private readonly RHIMLTensorDescriptor[] m_OpOutputDescriptors;
            private readonly IntPtr[] m_InputTensors;
            private readonly IntPtr[] m_OpOutputTensors;

            internal GraphBuildContext(
                IntPtr graph,
                RHIMLTensorDescriptor[] inputDescriptors,
                RHIMLTensorDescriptor[] opOutputDescriptors,
                IntPtr[] inputTensors,
                IntPtr[] opOutputTensors)
            {
                Graph = graph;
                m_InputDescriptors = inputDescriptors;
                m_OpOutputDescriptors = opOutputDescriptors;
                m_InputTensors = inputTensors;
                m_OpOutputTensors = opOutputTensors;
            }

            internal IntPtr ResolveTensor(in RHIMLOpTensorRef tensorRef)
            {
                if (tensorRef.IsOpOutput)
                {
                    if ((uint)tensorRef.OpIndex >= (uint)m_OpOutputTensors.Length || m_OpOutputTensors[tensorRef.OpIndex] == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Metal ML op references unavailable op output {tensorRef}.");
                    }

                    return m_OpOutputTensors[tensorRef.OpIndex];
                }

                if ((uint)tensorRef.InputIndex >= (uint)m_InputTensors.Length)
                {
                    throw new InvalidOperationException($"Metal ML op references unavailable input {tensorRef}.");
                }

                return m_InputTensors[tensorRef.InputIndex];
            }

            internal RHIMLTensorDescriptor ResolveDescriptor(in RHIMLOpTensorRef tensorRef)
            {
                if (tensorRef.IsOpOutput)
                {
                    if ((uint)tensorRef.OpIndex >= (uint)m_OpOutputDescriptors.Length)
                    {
                        throw new InvalidOperationException($"Metal ML op references unavailable op output descriptor {tensorRef}.");
                    }

                    return m_OpOutputDescriptors[tensorRef.OpIndex];
                }

                if ((uint)tensorRef.InputIndex >= (uint)m_InputDescriptors.Length)
                {
                    throw new InvalidOperationException($"Metal ML op references unavailable input descriptor {tensorRef}.");
                }

                return m_InputDescriptors[tensorRef.InputIndex];
            }
        }
    }
}
