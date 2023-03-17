using Infinity.Core;
using System;
using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    public abstract class Dx12TopLevelAccelStruct : RHITopLevelAccelStruct
    {
        internal ID3D12Resource ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private ID3D12Resource m_ResultBuffer;
        private ID3D12Resource m_ScratchBuffer;
        private ID3D12Resource m_InstancesBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        protected Dx12TopLevelAccelStruct(RHIDevice device, in RHITopLevelAccelStructDescriptor descriptor) : base(device, descriptor)
        {

        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }
    }

    public abstract class Dx12BottomLevelAccelStruct : RHIBottomLevelAccelStruct
    {
        internal ID3D12Resource ResultBuffer => m_ResultBuffer;
        internal D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC NativeAccelStrucDescriptor => m_NativeAccelStrucDescriptor;

        private ID3D12Resource m_ResultBuffer;
        private ID3D12Resource m_ScratchBuffer;
        private D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC m_NativeAccelStrucDescriptor;

        protected Dx12BottomLevelAccelStruct(RHIDevice device, in RHIBottomLevelAccelStructDescriptor descriptor) : base(device, descriptor)
        {

        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override void UpdateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {

        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
