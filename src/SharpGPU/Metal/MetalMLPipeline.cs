using System;
using Infinity.Core;
using SharpMetal.Metal;

namespace Infinity.Graphics
{
    internal class MetalMLPipeline : RHIMLPipeline
    {
        internal MTL4MachineLearningPipelineState NativePipelineState => m_NativePipelineState;

        private MTL4MachineLearningPipelineState m_NativePipelineState;

        public MetalMLPipeline(MetalDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            // Create ML pipeline via MTL4Compiler
            // For now, store descriptor info; the actual pipeline creation requires
            // an MTL4Compiler instance and proper function descriptor setup
            m_IntermediatesHeapSize = 0;

            // TODO: Full Metal 4 ML pipeline creation
            // 1. Create MTL4MachineLearningPipelineDescriptor
            // 2. Set MachineLearningFunctionDescriptor from descriptor.Function
            // 3. Set input dimensions from descriptor.InputTensors
            // 4. Use MTL4Compiler.NewMachineLearningPipelineState(desc, ref error)
            // 5. Query IntermediatesHeapSize from the compiled state
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                SharpMetal.ObjectiveCCore.ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }
        }
    }
}
