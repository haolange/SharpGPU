using SharpGPU.Mathematics;

namespace SharpGPU
{
    internal unsafe class Dx12BufferView : RHIBufferView
    {
        public Vortice.Direct3D12.ID3D12DescriptorHeap NativeDescriptorHeap
        {
            get
            {
                return m_NativeDescriptorHeap;
            }
        }
        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle
        {
            get
            {
                return m_NativeCpuDescriptorHandle;
            }
        }
        public Vortice.Direct3D12.GpuDescriptorHandle NativeGpuDescriptorHandle
        {
            get
            {
                return m_NativeGpuDescriptorHandle;
            }
        }

        private int m_HeapIndex;
        private bool4 m_LifeState;
        private Dx12Buffer m_Dx12Buffer;
        private Vortice.Direct3D12.ID3D12DescriptorHeap m_NativeDescriptorHeap;
        private Vortice.Direct3D12.CpuDescriptorHandle m_NativeCpuDescriptorHandle;
        private Vortice.Direct3D12.GpuDescriptorHandle m_NativeGpuDescriptorHandle;

        public Dx12BufferView(Dx12Buffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_LifeState = false;
            m_Dx12Buffer = buffer;

            if (descriptor.ViewType == ERHIBufferViewType.UniformBuffer)
            {
                if (Dx12Utility.IsConstantBuffer(buffer.Descriptor.UsageFlag))
                {
                    m_LifeState.x = true;

                    Vortice.Direct3D12.ConstantBufferViewDescription desc = new Vortice.Direct3D12.ConstantBufferViewDescription();
                    desc.SizeInBytes = (uint)descriptor.Stride;
                    desc.BufferLocation = m_Dx12Buffer.NativeResource.GPUVirtualAddress + (ulong)(descriptor.Stride * descriptor.Offset);

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateConstantBufferView(desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.AccelStruct)
            {
                if (Dx12Utility.IsAccelStruct(buffer.Descriptor.UsageFlag))
                {
                    m_LifeState.y = true;

                    Vortice.Direct3D12.ShaderResourceViewDescription desc = new Vortice.Direct3D12.ShaderResourceViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.RaytracingAccelerationStructure.Location = m_Dx12Buffer.NativeResource.GPUVirtualAddress;
                    desc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.RaytracingAccelerationStructure;
                    desc.Shader4ComponentMapping = 5768;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Buffer.NativeResource, desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.ShaderResource)
            {
                if (Dx12Utility.IsShaderResourceBuffer(buffer.Descriptor.UsageFlag))
                {
                    m_LifeState.z = true;

                    Vortice.Direct3D12.ShaderResourceViewDescription desc = new Vortice.Direct3D12.ShaderResourceViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Buffer;
                    desc.Shader4ComponentMapping = 5768;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Buffer.NativeResource, desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.UnorderedAccess)
            {
                if (Dx12Utility.IsUnorderedAccessBuffer(buffer.Descriptor.UsageFlag))
                {
                    m_LifeState.w = true;

                    Vortice.Direct3D12.UnorderedAccessViewDescription desc = new Vortice.Direct3D12.UnorderedAccessViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = Vortice.Direct3D12.UnorderedAccessViewDimension.Buffer;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateUnorderedAccessView(m_Dx12Buffer.NativeResource, null, desc, m_NativeCpuDescriptorHandle);
                }
            }
        }

        protected override void Release()
        {
            if (m_LifeState.x || m_LifeState.y || m_LifeState.z || m_LifeState.w)
            {
                m_Dx12Buffer.Dx12Device.FreeCbvSrvUavDescriptor(m_HeapIndex);
            }
        }
    }
}
