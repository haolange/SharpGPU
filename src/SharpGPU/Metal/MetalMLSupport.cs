using System;
using SharpMetal.Metal;

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
}
