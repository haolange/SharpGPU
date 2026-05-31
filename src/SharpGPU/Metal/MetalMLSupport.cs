using System;
using SharpMetal.Metal;

namespace SharpGPU
{
    internal sealed class MetalMLProgram : RHIMLProgram
    {
        internal MetalFunction Function { get; }
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }

        internal MetalMLProgram(string name, MetalFunction function, params RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            Function = function ?? throw new ArgumentNullException(nameof(function));
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalMLBindingSet : RHIMLBindingSet
    {
        internal MetalMLPipeline PipelineTyped => (MetalMLPipeline)(m_Pipeline ?? throw new InvalidOperationException("Metal ML binding set pipeline is unavailable."));
        internal MetalTensor[] Inputs { get; }
        internal MetalTensor[] Outputs { get; }
        internal MTL4ArgumentTable NativeArgumentTable => m_NativeArgumentTable;
        internal SharpMetal.Metal.MTLHeap NativeIntermediatesHeap => m_IntermediatesHeap?.NativeHeap ?? default;

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
            argumentTableDescriptor.MaxBufferBindCount = Math.Max(1u, (uint)metalPipeline.BindingInfos.Length);
            SharpMetal.Foundation.NSError error = default;
            m_NativeArgumentTable = device.NativeDevice.NewArgumentTable(argumentTableDescriptor, ref error);
            SharpMetal.ObjectiveCCore.ObjectiveCRuntime.Release(argumentTableDescriptor);

            if (m_NativeArgumentTable.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTL4ArgumentTable for ML binding set: {errorText}");
            }

            if (metalPipeline.TemporaryResourceSize > 0)
            {
                RHIHeapDescription heapDescriptor = new RHIHeapDescription
                {
                    Size = metalPipeline.TemporaryResourceSize,
                    StorageMode = ERHIStorageMode.GPULocal,
                };
                m_IntermediatesHeap = new MetalHeap(device, heapDescriptor);
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
            ulong bindingSlot = 0;
            for (int i = 0; i < bindingInfos.Length; ++i, ++bindingSlot)
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

                m_NativeArgumentTable.SetResource(tensor.NativeTensor.GpuResourceID, bindingSlot);
            }
        }

        protected override void Release()
        {
            if (m_NativeArgumentTable.NativePtr != IntPtr.Zero)
            {
                SharpMetal.ObjectiveCCore.ObjectiveCRuntime.Release(m_NativeArgumentTable);
                m_NativeArgumentTable = default;
            }

            m_IntermediatesHeap?.Dispose();
            m_IntermediatesHeap = null;
        }
    }
}
