using System;

namespace SharpGPU
{
    internal sealed class VulkanMLProgram : RHIMLProgram
    {
        internal RHIMLTensorBindingInfo[] BindingInfos { get; }

        internal VulkanMLProgram(string name, params RHIMLTensorBindingInfo[] bindingInfos)
        {
            m_Name = name;
            BindingInfos = bindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();
        }

        protected override void Release()
        {
        }
    }

    internal sealed class VulkanMLBindingSet : RHIMLBindingSet
    {
        internal VulkanTensor[] Inputs { get; }
        internal VulkanTensor[] Outputs { get; }

        internal VulkanMLBindingSet(in RHIMLBindingSetDescriptor descriptor)
        {
            if (descriptor.Pipeline is not VulkanMLPipeline vkPipeline)
            {
                throw new InvalidOperationException($"Vulkan ML binding set requires a {nameof(VulkanMLPipeline)}.");
            }

            m_Pipeline = vkPipeline;
            Inputs = ConvertTensors(descriptor.Inputs.Span, ERHIMLTensorBindingKind.Input, vkPipeline.InputCount);
            Outputs = ConvertTensors(descriptor.Outputs.Span, ERHIMLTensorBindingKind.Output, vkPipeline.OutputCount);
        }

        private static VulkanTensor[] ConvertTensors(ReadOnlySpan<RHITensor> tensors, ERHIMLTensorBindingKind kind, uint expectedCount)
        {
            if (tensors.Length != expectedCount)
            {
                throw new InvalidOperationException($"Vulkan ML binding count mismatch for {kind}. expected={expectedCount}, actual={tensors.Length}.");
            }

            VulkanTensor[] result = new VulkanTensor[tensors.Length];
            for (int i = 0; i < tensors.Length; ++i)
            {
                result[i] = tensors[i] as VulkanTensor
                    ?? throw new InvalidOperationException($"Vulkan ML binding tensor[{i}] must be a {nameof(VulkanTensor)}.");
            }

            return result;
        }

        protected override void Release()
        {
        }
    }
}
