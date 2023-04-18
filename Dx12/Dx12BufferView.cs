using Infinity.Mathmatics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
    internal unsafe class Dx12BufferView : RHIBufferView
    {
        public ID3D12DescriptorHeap* NativeDescriptorHeap
        {
            get
            {
                return m_NativeDescriptorHeap;
            }
        }
        public D3D12_CPU_DESCRIPTOR_HANDLE NativeCpuDescriptorHandle
        {
            get
            {
                return m_NativeCpuDescriptorHandle;
            }
        }
        public D3D12_GPU_DESCRIPTOR_HANDLE NativeGpuDescriptorHandle
        {
            get
            {
                return m_NativeGpuDescriptorHandle;
            }
        }

        private int m_HeapIndex;
        private bool4 m_LifeState;
        private Dx12Buffer m_Dx12Buffer;
        private ID3D12DescriptorHeap* m_NativeDescriptorHeap;
        private D3D12_CPU_DESCRIPTOR_HANDLE m_NativeCpuDescriptorHandle;
        private D3D12_GPU_DESCRIPTOR_HANDLE m_NativeGpuDescriptorHandle;

        public Dx12BufferView(Dx12Buffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_LifeState = false;
            m_Dx12Buffer = buffer;

            if (descriptor.ViewType == EBufferViewType.UniformBuffer)
            {
                if (Dx12Utility.IsConstantBuffer(buffer.Descriptor.Usage))
                {
                    m_LifeState.x = true;

                    D3D12_CONSTANT_BUFFER_VIEW_DESC desc = new D3D12_CONSTANT_BUFFER_VIEW_DESC();
                    desc.SizeInBytes = (uint)descriptor.Stride;
                    desc.BufferLocation = m_Dx12Buffer.NativeResource->GetGPUVirtualAddress() + (ulong)(descriptor.Stride * descriptor.Offset);

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice->CreateConstantBufferView(&desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == EBufferViewType.AccelStruct)
            {
                if (Dx12Utility.IsAccelStruct(buffer.Descriptor.Usage))
                {
                    m_LifeState.y = true;

                    D3D12_SHADER_RESOURCE_VIEW_DESC desc = new D3D12_SHADER_RESOURCE_VIEW_DESC();
                    desc.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER;
                    desc.Shader4ComponentMapping = 5768;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice->CreateShaderResourceView(m_Dx12Buffer.NativeResource, &desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == EBufferViewType.ShaderResource)
            {
                if (Dx12Utility.IsShaderResourceBuffer(buffer.Descriptor.Usage))
                {
                    m_LifeState.z = true;

                    D3D12_SHADER_RESOURCE_VIEW_DESC desc = new D3D12_SHADER_RESOURCE_VIEW_DESC();
                    desc.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_BUFFER;
                    desc.Shader4ComponentMapping = 5768;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice->CreateShaderResourceView(m_Dx12Buffer.NativeResource, &desc, m_NativeCpuDescriptorHandle);
                }
            }
            else if (descriptor.ViewType == EBufferViewType.UnorderedAccess)
            {
                if (Dx12Utility.IsUnorderedAccessBuffer(buffer.Descriptor.Usage))
                {
                    m_LifeState.w = true;

                    D3D12_UNORDERED_ACCESS_VIEW_DESC desc = new D3D12_UNORDERED_ACCESS_VIEW_DESC();
                    desc.Format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = D3D12_UAV_DIMENSION.D3D12_UAV_DIMENSION_BUFFER;

                    Dx12DescriptorInfo allocation = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptor(1);
                    m_HeapIndex = allocation.Index;
                    m_NativeDescriptorHeap = allocation.DescriptorHeap;
                    m_NativeCpuDescriptorHandle = allocation.CpuHandle;
                    m_NativeGpuDescriptorHandle = allocation.GpuHandle;
                    m_Dx12Buffer.Dx12Device.NativeDevice->CreateUnorderedAccessView(m_Dx12Buffer.NativeResource, null, &desc, m_NativeCpuDescriptorHandle);
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
