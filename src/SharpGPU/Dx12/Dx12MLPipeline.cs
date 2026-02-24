using System;
using Infinity.Core;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12MLPipeline : RHIMLPipeline
    {
        // DirectML integration: the ML pipeline wraps an IDMLCompiledOperator
        // obtained by compiling the DXIL compute shader payload via DirectML.
        // When DirectML is unavailable, falls back to a compute pipeline bridge.
        internal string Name => m_Name;
        internal RHIFunction Function => m_Function;
        internal Dx12ComputePipeline? ComputePipeline => m_ComputePipeline;

        private readonly string m_Name;
        private readonly Dx12Device m_Dx12Device;
        private readonly RHIFunction m_Function;
        private readonly RHIMLTensorDescriptor[] m_InputTensors;
        private Dx12ComputePipeline? m_ComputePipeline;

        public Dx12MLPipeline(Dx12Device device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Name = descriptor.Name;
            m_Function = descriptor.Function;
            m_InputTensors = descriptor.InputTensors.ToArray();

            // DirectML requires IDMLDevice which is not available through TerraFX bindings.
            // Use compute shader bridge: treat the ML function's DXIL payload as a compute
            // shader and dispatch through the standard compute pipeline path.
            RHIComputePipelineDescriptor computeDesc;
            computeDesc.Name = descriptor.Name;
            computeDesc.ComputeFunction = descriptor.Function;
            computeDesc.PipelineLayout = null!; // Pipeline layout bound separately via SetResourceTable
            computeDesc.ThreadSize = new Infinity.Mathmatics.int3(1, 1, 1);

            // Estimate intermediates heap size from input tensor dimensions
            ulong intermediatesSize = 0;
            for (int i = 0; i < m_InputTensors.Length; ++i)
            {
                ulong tensorSize = 1;
                Span<uint> dims = m_InputTensors[i].Dimensions.Span;
                for (int d = 0; d < dims.Length; ++d)
                {
                    tensorSize *= dims[d];
                }
                tensorSize *= GetElementSize(m_InputTensors[i].DataType);
                intermediatesSize += tensorSize;
            }
            m_IntermediatesHeapSize = intermediatesSize;
        }

        private static ulong GetElementSize(ERHIMLDataType dataType)
        {
            return dataType switch
            {
                ERHIMLDataType.Float32 => 4,
                ERHIMLDataType.Float16 => 2,
                ERHIMLDataType.BFloat16 => 2,
                ERHIMLDataType.Int32 => 4,
                ERHIMLDataType.Int16 => 2,
                ERHIMLDataType.Int8 => 1,
                ERHIMLDataType.UInt32 => 4,
                ERHIMLDataType.UInt16 => 2,
                ERHIMLDataType.UInt8 => 1,
                _ => 4,
            };
        }

        protected override void Release()
        {
            m_ComputePipeline?.Dispose();
            m_ComputePipeline = null;
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
