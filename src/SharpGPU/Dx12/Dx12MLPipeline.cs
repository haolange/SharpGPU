using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    internal unsafe class Dx12MLPipeline : RHIMLPipeline
    {
        // DirectML stores compiled operator and binding table description
        // For now, store the descriptor info for future DirectML integration
        internal string Name => m_Name;
        internal RHIFunction Function => m_Function;

        private readonly string m_Name;
        private readonly RHIFunction m_Function;
        private readonly RHIMLTensorDescriptor[] m_InputTensors;

        public Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Name = descriptor.Name;
            m_Function = descriptor.Function;
            m_InputTensors = descriptor.InputTensors.ToArray();
            m_IntermediatesHeapSize = 0;

            // TODO: DirectML integration
            // 1. Get IDMLDevice from Dx12Device
            // 2. Create operator from descriptor
            // 3. Compile operator: IDMLDevice::CompileOperator() -> IDMLCompiledOperator
            // 4. Query m_IntermediatesHeapSize from compiled operator binding properties
        }

        protected override void Release()
        {
            // TODO: Release IDMLCompiledOperator and related resources
        }
    }
}
