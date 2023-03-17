using System;
using Infinity.Core;
using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct
    {
        internal Dx12Buffer ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private Dx12Buffer m_ResultBuffer;
        private Dx12Buffer m_ScratchBuffer;
        private Dx12Buffer m_InstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12TopLevelAccelStruct(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return m_ResultBuffer.CreateBufferView(descriptor);
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }
    }

    internal unsafe class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        internal Dx12Buffer ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private Dx12Buffer m_ResultBuffer;
        private Dx12Buffer m_ScratchBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        public Dx12BottomLevelAccelStruct(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return m_ResultBuffer.CreateBufferView(descriptor);
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
