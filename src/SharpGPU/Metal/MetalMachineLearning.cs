using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalMLProgram : RHIMLProgram
    {
        internal MTLLibrary NativeLibrary => m_NativeLibrary;
        internal string EntryName { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }

        private MTLLibrary m_NativeLibrary;

        internal MetalMLProgram(string name, MTLLibrary nativeLibrary, string entryName, string packageDirectory, params RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            if (nativeLibrary.NativePtr == IntPtr.Zero)
            {
                throw new ArgumentException("Metal ML program requires a native MTLLibrary.", nameof(nativeLibrary));
            }

            m_NativeLibrary = nativeLibrary;
            EntryName = string.IsNullOrWhiteSpace(entryName) ? MetalMpsGraphPackageBuilder.EntryName : entryName;
            _ = packageDirectory;
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        protected override void Release()
        {
            m_NativeLibrary = default;
        }
    }

    internal sealed class MetalMLBindingSet : RHIMLBindingSet
    {
        internal MetalMLPipeline PipelineTyped => (MetalMLPipeline)(m_Pipeline ?? throw new InvalidOperationException("Metal ML binding set pipeline is unavailable."));
        internal MetalTensor[] Inputs { get; }
        internal MetalTensor[] Outputs { get; }
        internal MTL4ArgumentTable NativeArgumentTable => m_NativeArgumentTable;
        internal MTLHeap NativeIntermediatesHeap => m_IntermediatesHeap?.NativeHeap ?? default;

        private MTL4ArgumentTable m_NativeArgumentTable;
        private MetalHeap? m_IntermediatesHeap;

        internal MetalMLBindingSet(MetalDevice device, in RHIMLBindingSetDescriptor descriptor)
        {
            if (descriptor.Pipeline is not MetalMLPipeline metalPipeline)
            {
                throw new InvalidOperationException($"Metal ML binding set requires a {nameof(MetalMLPipeline)}.");
            }

            m_Pipeline = metalPipeline;
            Inputs = ConvertTensors(descriptor.Inputs.Span, ERHIMLTensorBindingKind.Input, metalPipeline.InputCount);
            Outputs = ConvertTensors(descriptor.Outputs.Span, ERHIMLTensorBindingKind.Output, metalPipeline.OutputCount);

            MTL4ArgumentTableDescriptor argumentTableDescriptor = MTL4ArgumentTableDescriptor.New();
            argumentTableDescriptor.MaxBufferBindCount = Math.Max(1UL, metalPipeline.ArgumentTableBufferBindCount);
            argumentTableDescriptor.InitializeBindings = true;
            SharpMetal.Foundation.NSError error = default;
            m_NativeArgumentTable = device.NativeDevice.NewArgumentTable(argumentTableDescriptor, ref error);
            SharpMetal.ObjectiveCCore.ObjectiveCRuntime.Release(argumentTableDescriptor);

            if (m_NativeArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTL4ArgumentTable for ML binding set: {errorText}");
            }

            device.RegisterMetalMLArgumentTable(m_NativeArgumentTable);

            if (metalPipeline.TemporaryResourceSize > 0)
            {
                RHIResourceMemoryRequirements heapRequirements = new RHIResourceMemoryRequirements(
                    device,
                    metalPipeline.TemporaryResourceSize,
                    1,
                    ERHIStorageMode.GPULocal,
                    1,
                    ERHIMemoryResourceKind.Buffer);
                RHIHeapDescription heapDescriptor = new RHIHeapDescription(
                    metalPipeline.TemporaryResourceSize,
                    heapRequirements);
                m_IntermediatesHeap = new MetalHeap(device, heapDescriptor, MTLHeapType.Automatic);
                device.RegisterMetalMLIntermediatesHeap(m_IntermediatesHeap.NativeHeap);
            }

            PopulateArgumentTable(device, metalPipeline);
        }

        private static MetalTensor[] ConvertTensors(ReadOnlySpan<RHITensor> tensors, ERHIMLTensorBindingKind kind, uint expectedCount)
        {
            if (tensors.Length != expectedCount)
            {
                throw new InvalidOperationException($"Metal ML binding count mismatch for {kind}. expected={expectedCount}, actual={tensors.Length}.");
            }

            MetalTensor[] result = new MetalTensor[tensors.Length];
            for (int i = 0; i < tensors.Length; ++i)
            {
                result[i] = tensors[i] as MetalTensor
                    ?? throw new InvalidOperationException($"Metal ML binding tensor[{i}] must be a {nameof(MetalTensor)}.");
            }

            return result;
        }

        private void PopulateArgumentTable(MetalDevice device, MetalMLPipeline pipeline)
        {
            _ = device;
            if (pipeline.ReflectionBindingCount != 0UL && pipeline.ReflectionBindingCount != (ulong)pipeline.BindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML reflection binding count mismatch. reflection={pipeline.ReflectionBindingCount}, expected={pipeline.BindingInfos.Length}.");
            }

            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos = pipeline.BindingInfos.Span;
            ReadOnlySpan<ulong> bindingSlots = pipeline.NativeBindingSlots.Span;
            if (bindingSlots.Length != bindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML native binding slot count mismatch. slots={bindingSlots.Length}, bindings={bindingInfos.Length}.");
            }

            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                MetalTensor tensor = bindingInfo.Kind switch
                {
                    ERHIMLTensorBindingKind.Input => Inputs[(int)bindingInfo.Index],
                    ERHIMLTensorBindingKind.Output => Outputs[(int)bindingInfo.Index],
                    _ => throw new InvalidOperationException($"Unsupported Metal ML tensor binding kind '{bindingInfo.Kind}'."),
                };

                if (!RHIMLHelpers.HasCompatibleLayout(bindingInfo.Descriptor, tensor.Descriptor))
                {
                    throw new InvalidOperationException(
                        $"Metal ML tensor layout mismatch for '{bindingInfo.Name}'. expected={RHIMLHelpers.DescribeLayout(bindingInfo.Descriptor)}, actual={RHIMLHelpers.DescribeLayout(tensor.Descriptor)}.");
                }

                ulong bindingSlot = bindingSlots[i];
                m_NativeArgumentTable.SetResource(tensor.NativeTensor.GpuResourceID, bindingSlot);
            }
        }

        protected override void Release()
        {
            m_NativeArgumentTable = default;
            m_IntermediatesHeap = null;
        }
    }

    internal class MetalMLPipeline : RHIMLPipeline
    {
        internal MTL4MachineLearningPipelineState NativePipelineState => m_NativePipelineState;
        internal MetalMLProgram Program => m_Program;
        internal ulong ReflectionBindingCount => m_ReflectionBindingCount;
        internal ReadOnlyMemory<ulong> NativeBindingSlots => m_NativeBindingSlots;
        internal ulong ArgumentTableBufferBindCount => m_ArgumentTableBufferBindCount;

        private MTL4MachineLearningPipelineState m_NativePipelineState;
        private readonly MetalMLProgram m_Program;
        private readonly ulong[] m_NativeBindingSlots;
        private readonly ulong m_ArgumentTableBufferBindCount;
        private ulong m_ReflectionBindingCount;

        public MetalMLPipeline(MetalDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_Program = descriptor.Program as MetalMLProgram
                ?? throw new InvalidOperationException("Metal ML pipeline requires a MetalMLProgram.");
            m_BindingInfos = m_Program.BindingInfos;

            if (!device.SupportsMetal4)
            {
                throw new NotSupportedException(
                    "MetalMLPipeline requires Metal 4 (MTL4MachineLearningPipelineState). " +
                    "The current device does not support Metal 4.");
            }

            // ── Create MTL4Compiler ──
            NSError compilerError = default;
            MTL4CompilerDescriptor compilerDesc = MTL4CompilerDescriptor.New();
            MTL4Compiler compiler = device.NativeDevice.NewCompiler(compilerDesc, ref compilerError);
            ObjectiveCRuntime.Release(compilerDesc);

            if (compiler.NativePtr == IntPtr.Zero)
            {
                string errorText = compilerError.NativePtr != IntPtr.Zero
                    ? compilerError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4Compiler - {errorText}");
            }

            // ── Build MTL4LibraryFunctionDescriptor from RHIFunction ──
            MTL4LibraryFunctionDescriptor funcDesc = MTL4LibraryFunctionDescriptor.New();
            funcDesc.Library = m_Program.NativeLibrary;
            funcDesc.Name = new NSString(m_Program.EntryName);

            // ── Build ML pipeline descriptor ──
            MTL4MachineLearningPipelineDescriptor mlDesc = MTL4MachineLearningPipelineDescriptor.New();
            mlDesc.MachineLearningFunctionDescriptor = funcDesc;
            MTL4PipelineOptions pipelineOptions = MTL4PipelineOptions.New();
            pipelineOptions.ShaderReflection = MTL4ShaderReflection.BindingInfo;
            MTL4PipelineDescriptor pipelineDescriptor = mlDesc;
            pipelineDescriptor.Options = pipelineOptions;
            ObjectiveCRuntime.Release(pipelineOptions.NativePtr);
            if (!string.IsNullOrEmpty(descriptor.Name))
            {
                mlDesc.Label = new NSString(descriptor.Name);
            }

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                if (bindingInfo.Kind != ERHIMLTensorBindingKind.Input)
                {
                    continue;
                }

                MTLTensorExtents dimensions = MetalTensor.CreateNativeTensorExtents(bindingInfo.Descriptor.Dimensions.Span);
                mlDesc.SetInputDimensions(dimensions, bindingInfo.Index);
                ReleaseNativeObject(dimensions);
            }
            ObjectiveCRuntime.Release(funcDesc);

            // ── Create pipeline state ──
            NSError pipelineError = default;
            m_NativePipelineState = compiler.NewMachineLearningPipelineState(mlDesc, ref pipelineError);
            ObjectiveCRuntime.Release(mlDesc);
            ObjectiveCRuntime.Release(compiler);

            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = pipelineError.NativePtr != IntPtr.Zero
                    ? pipelineError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4MachineLearningPipelineState '{descriptor.Name}' - {errorText}");
            }

            device.RegisterMetalMLPipelineState(m_NativePipelineState);
            m_NativeBindingSlots = ResolveNativeBindingSlots();
            m_ArgumentTableBufferBindCount = CalculateArgumentTableBufferBindCount(m_NativeBindingSlots);

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            m_TemporaryResourceSize = m_NativePipelineState.IntermediatesHeapSize;
            m_PersistentResourceSize = 0;
        }

        protected override void Release()
        {
            m_NativePipelineState = default;
        }

        private static void ReleaseNativeObject(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            ObjectiveCRuntime.Release(nativePtr);
        }

        private ulong[] ResolveNativeBindingSlots()
        {
            MTL4MachineLearningPipelineReflection reflection = m_NativePipelineState.Reflection;
            if (reflection.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal ML pipeline reflection is unavailable; cannot bind artifact-backed tensor arguments.");
            }

            NSArray reflectionBindings = reflection.Bindings;
            if (reflectionBindings.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal ML pipeline reflection did not expose tensor bindings.");
            }

            m_ReflectionBindingCount = reflectionBindings.Count;
            Dictionary<string, ulong> slotsByName = new Dictionary<string, ulong>(StringComparer.Ordinal);
            List<ulong> tensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<ulong> readOnlyTensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<ulong> writableTensorSlots = new List<ulong>(checked((int)m_ReflectionBindingCount));
            List<string> observedBindings = new List<string>(checked((int)m_ReflectionBindingCount));

            for (ulong i = 0; i < m_ReflectionBindingCount; ++i)
            {
                IntPtr bindingPtr = reflectionBindings.Object(i);
                if (bindingPtr == IntPtr.Zero)
                {
                    continue;
                }

                MTLBinding binding = new MTLBinding(bindingPtr);
                string bindingName = GetBindingName(binding);
                MTLBindingType bindingType = binding.Type;
                MTLBindingAccess bindingAccess = binding.Access;
                ulong bindingIndex = binding.Index;
                observedBindings.Add(string.IsNullOrWhiteSpace(bindingName)
                    ? $"<unnamed>:{bindingType}:{bindingAccess}@{bindingIndex}"
                    : $"{bindingName}:{bindingType}:{bindingAccess}@{bindingIndex}");

                if (bindingType != MTLBindingType.Tensor)
                {
                    continue;
                }

                tensorSlots.Add(bindingIndex);
                if (bindingAccess == MTLBindingAccess.ReadOnly)
                {
                    readOnlyTensorSlots.Add(bindingIndex);
                }
                else
                {
                    writableTensorSlots.Add(bindingIndex);
                }

                if (string.IsNullOrWhiteSpace(bindingName))
                {
                    continue;
                }

                if (!slotsByName.TryAdd(bindingName, bindingIndex))
                {
                    throw new InvalidOperationException($"Metal ML pipeline reflection has duplicate tensor binding name '{bindingName}'.");
                }
            }

            RHIMLTensorBindingInfo[] bindingInfos = m_BindingInfos
                ?? throw new InvalidOperationException("Metal ML pipeline binding metadata is unavailable.");
            if (slotsByName.Count == 0 && tensorSlots.Count == bindingInfos.Length)
            {
                if (TryResolveUnnamedBindingSlotsByAccess(bindingInfos, readOnlyTensorSlots, writableTensorSlots, out ulong[]? accessSlots) && accessSlots != null)
                {
                    return accessSlots;
                }

                return tensorSlots.ToArray();
            }

            if (slotsByName.Count < bindingInfos.Length)
            {
                throw new InvalidOperationException(
                    $"Metal ML pipeline reflection exposed {slotsByName.Count} named tensor binding(s), expected {bindingInfos.Length}. observed=[{string.Join(", ", observedBindings)}].");
            }

            ulong[] slots = new ulong[bindingInfos.Length];
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref bindingInfos[i];
                if (!slotsByName.TryGetValue(bindingInfo.Name, out ulong slot))
                {
                    throw new InvalidOperationException(
                        $"Metal ML pipeline reflection did not expose expected tensor binding '{bindingInfo.Name}'. observed=[{string.Join(", ", observedBindings)}].");
                }

                slots[i] = slot;
            }

            return slots;
        }

        private static bool TryResolveUnnamedBindingSlotsByAccess(
            ReadOnlySpan<RHIMLTensorBindingInfo> bindingInfos,
            List<ulong> readOnlyTensorSlots,
            List<ulong> writableTensorSlots,
            out ulong[]? slots)
        {
            slots = null;
            int inputCount = 0;
            int outputCount = 0;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                if (bindingInfos[i].Kind == ERHIMLTensorBindingKind.Input)
                {
                    ++inputCount;
                }
                else if (bindingInfos[i].Kind == ERHIMLTensorBindingKind.Output)
                {
                    ++outputCount;
                }
            }

            if (readOnlyTensorSlots.Count != inputCount || writableTensorSlots.Count != outputCount)
            {
                return false;
            }

            slots = new ulong[bindingInfos.Length];
            int inputCursor = 0;
            int outputCursor = 0;
            for (int i = 0; i < bindingInfos.Length; ++i)
            {
                slots[i] = bindingInfos[i].Kind switch
                {
                    ERHIMLTensorBindingKind.Input => readOnlyTensorSlots[inputCursor++],
                    ERHIMLTensorBindingKind.Output => writableTensorSlots[outputCursor++],
                    _ => throw new InvalidOperationException($"Unsupported Metal ML tensor binding kind '{bindingInfos[i].Kind}'."),
                };
            }

            return true;
        }

        private static string GetBindingName(in MTLBinding binding)
        {
            NSString name = binding.Name;
            return name.NativePtr == IntPtr.Zero ? string.Empty : name.ToString();
        }

        private static ulong CalculateArgumentTableBufferBindCount(ReadOnlySpan<ulong> bindingSlots)
        {
            ulong maxSlot = 0;
            for (int i = 0; i < bindingSlots.Length; ++i)
            {
                maxSlot = Math.Max(maxSlot, bindingSlots[i]);
            }

            return maxSlot + 1UL;
        }
    }

    internal sealed class MetalMLEncoder : RHIMLEncoder
    {
        private MTL4MachineLearningCommandEncoder m_NativeEncoder;
        private readonly MetalDevice m_MetalDevice;
        private RHIMLPassDescriptor m_PassDescriptor;

        public MetalMLEncoder(MetalCommandBuffer cmdBuffer)
        {
            m_CommandBuffer = cmdBuffer;
            m_MetalDevice = ((MetalCommandQueue)cmdBuffer.CommandQueue).MetalDevice;
        }

        internal override void BeginPass(in RHIMLPassDescriptor descriptor)
        {
            if (!m_MetalDevice.SupportsMetalML)
            {
                throw new NotSupportedException(m_MetalDevice.MetalMLUnavailableReason ?? "Metal ML is not supported on this device.");
            }

            m_PassDescriptor = descriptor;
            m_NativeEncoder = default;
            m_CachedPipeline = null;
            m_CachedBindingSet = null;

            MTL4CommandBuffer mtl4CmdBuffer = ((MetalCommandBuffer)m_CommandBuffer!).EnsureMtl4CommandBuffer();
            m_NativeEncoder = mtl4CmdBuffer.MachineLearningCommandEncoder();

            if (m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                throw new NotSupportedException("Metal ML requires a native MTL4MachineLearningCommandEncoder.");
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Name))
            {
                PushDebugGroup(descriptor.Name);
            }

            if (descriptor.Timestamp.HasValue)
            {
                WriteTimestamp(descriptor.Timestamp.Value.BeginIndex);
            }
        }

        public override void Barrier(in RHIBarrier barrier)
        {
            ReadOnlySpan<RHIBarrier> singleBarrier = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in barrier), 1);
            Barriers(singleBarrier);
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
            if (barriers.Length == 0 || m_NativeEncoder.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MetalCommandBuffer commandBuffer = (MetalCommandBuffer)m_CommandBuffer!;
            MetalBarrierHelper.MetalBarrierBatchPlan plan = MetalBarrierHelper.PlanBarriers(commandBuffer, barriers);
            MetalBarrierHelper.ApplyPlan(m_NativeEncoder.NativePtr, plan);
        }

        public override void PushDebugGroup(string name)
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PushDebugGroup(new NSString(name));
            }
        }

        public override void PopDebugGroup()
        {
            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.PopDebugGroup();
            }
        }

        public override void WriteTimestamp(in uint index)
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                MetalQuery query = m_PassDescriptor.Timestamp.Value.Query as MetalQuery
                    ?? throw new InvalidOperationException("Metal ML timestamp pass requires a MetalQuery.");
                query.WriteTimestamp((MetalCommandBuffer)m_CommandBuffer!, index);
            }
        }

        public override void SetPipeline(RHIMLPipeline pipeline)
        {
            m_CachedPipeline = pipeline as MetalMLPipeline
                ?? throw new InvalidOperationException($"Metal ML encoder expects {nameof(MetalMLPipeline)} but got {pipeline?.GetType().Name ?? "<null>"}.");
            MetalMLPipeline metalPipeline = (MetalMLPipeline)m_CachedPipeline;
            if (metalPipeline.NativePipelineState.NativePtr != IntPtr.Zero)
            {
                if (m_CommandBuffer?.CommandQueue is MetalCommandQueue queue)
                {
                    queue.AddResidencyAllocation(new MTLAllocation(metalPipeline.NativePipelineState.NativePtr));
                }

                m_NativeEncoder.SetPipelineState(metalPipeline.NativePipelineState);
            }
        }

        public override void SetBindingSet(RHIMLBindingSet bindingSet)
        {
            MetalMLBindingSet metalBindingSet = bindingSet as MetalMLBindingSet
                ?? throw new InvalidOperationException($"Metal ML encoder expects {nameof(MetalMLBindingSet)} but got {bindingSet?.GetType().Name ?? "<null>"}.");
            m_CachedBindingSet = metalBindingSet;
            if (metalBindingSet.NativeArgumentTable.NativePtr != IntPtr.Zero)
            {
                TrackBindingResidency(metalBindingSet);
                m_NativeEncoder.SetArgumentTable(metalBindingSet.NativeArgumentTable);
            }
        }

        public override void Dispatch()
        {
            if (m_CachedPipeline is not MetalMLPipeline)
            {
                throw new InvalidOperationException("Metal ML encoder requires SetPipeline before Dispatch.");
            }

            if (m_CachedBindingSet is not MetalMLBindingSet metalBindingSet)
            {
                throw new InvalidOperationException("Metal ML encoder requires SetBindingSet before Dispatch.");
            }

            m_NativeEncoder.DispatchNetworkWithIntermediatesHeap(metalBindingSet.NativeIntermediatesHeap);
            ((MetalCommandBuffer)m_CommandBuffer!).MarkStagesSeen(MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.MachineLearning));
        }

        internal override void EndPassCore()
        {
            if (m_PassDescriptor.Timestamp.HasValue)
            {
                WriteTimestamp(m_PassDescriptor.Timestamp.Value.EndIndex);
            }

            if (m_NativeEncoder.NativePtr != IntPtr.Zero)
            {
                MTL4CommandEncoder baseEncoder = new MTL4CommandEncoder(m_NativeEncoder.NativePtr);
                baseEncoder.EndEncoding();
                m_NativeEncoder = default;
            }
            m_CachedPipeline = null;
            m_CachedBindingSet = null;
            m_PassDescriptor = default;
        }

        protected override void Release()
        {
        }

        private void TrackBindingResidency(MetalMLBindingSet bindingSet)
        {
            if (m_CommandBuffer?.CommandQueue is not MetalCommandQueue queue)
            {
                return;
            }

            for (int i = 0; i < bindingSet.Inputs.Length; ++i)
            {
                TrackTensorResidency(queue, bindingSet.Inputs[i]);
            }

            for (int i = 0; i < bindingSet.Outputs.Length; ++i)
            {
                TrackTensorResidency(queue, bindingSet.Outputs[i]);
            }

            if (bindingSet.NativeIntermediatesHeap.NativePtr != IntPtr.Zero)
            {
                queue.AddResidencyAllocation(bindingSet.NativeIntermediatesHeap);
            }
        }

        private static void TrackTensorResidency(MetalCommandQueue queue, MetalTensor tensor)
        {
            if (tensor.BackingBuffer != null)
            {
                queue.AddResidencyAllocation(tensor.BackingBuffer.NativeBuffer);
            }

            MTLBuffer nativeBuffer = tensor.NativeTensor.Buffer;
            if (nativeBuffer.NativePtr != IntPtr.Zero)
            {
                queue.AddResidencyAllocation(nativeBuffer);
            }
        }
    }

    // ========== WorkGraph Encoder ==========
}
