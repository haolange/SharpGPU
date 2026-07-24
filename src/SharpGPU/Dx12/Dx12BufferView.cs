using System;

namespace SharpGPU
{
    internal unsafe class Dx12BufferView : RHIBufferView, IDx12DescriptorView
    {
        public Dx12Device Device => m_Dx12Buffer.Dx12Device;
        public ERHIBufferViewType ViewType => m_ViewType;
        public Dx12DescriptorClass DescriptorClass => m_ViewType switch
        {
            ERHIBufferViewType.ShaderResource => Dx12DescriptorClass.ShaderResource,
            ERHIBufferViewType.UnorderedAccess => Dx12DescriptorClass.UnorderedAccess,
            ERHIBufferViewType.UniformBuffer => Dx12DescriptorClass.ConstantBuffer,
            ERHIBufferViewType.AccelStruct => Dx12DescriptorClass.AccelerationStructure,
            _ => throw new InvalidOperationException($"DX12 buffer view has unsupported descriptor class {m_ViewType}."),
        };

        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle
        {
            get
            {
                return m_Descriptors.Staging.CpuHandle;
            }
        }
        public Vortice.Direct3D12.GpuDescriptorHandle NativeGpuDescriptorHandle
        {
            get
            {
                return m_Descriptors.ShaderVisible.GpuHandle;
            }
        }

        private bool m_HasDescriptors;
        private ERHIBufferViewType m_ViewType;
        private Dx12Buffer m_Dx12Buffer;
        private Dx12DescriptorPair m_Descriptors;

        public Dx12BufferView(Dx12Buffer buffer, in RHIBufferViewDescriptor descriptor)
        {
            m_HasDescriptors = false;
            m_ViewType = descriptor.ViewType;
            m_Dx12Buffer = buffer;

            if (descriptor.ViewType == ERHIBufferViewType.UniformBuffer)
            {
                if (Dx12Utility.IsConstantBuffer(buffer.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.ConstantBufferViewDescription desc = new Vortice.Direct3D12.ConstantBufferViewDescription();
                    desc.SizeInBytes = (uint)descriptor.Stride;
                    desc.BufferLocation = m_Dx12Buffer.NativeResource.GPUVirtualAddress + (ulong)(descriptor.Stride * descriptor.Offset);

                    m_Descriptors = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateConstantBufferView(desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Buffer.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.AccelStruct)
            {
                if (Dx12Utility.IsAccelStruct(buffer.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.ShaderResourceViewDescription desc = new Vortice.Direct3D12.ShaderResourceViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.RaytracingAccelerationStructure.Location = m_Dx12Buffer.NativeResource.GPUVirtualAddress;
                    desc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.RaytracingAccelerationStructure;
                    desc.Shader4ComponentMapping = 5768;

                    m_Descriptors = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Buffer.NativeResource, desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Buffer.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.ShaderResource)
            {
                if (Dx12Utility.IsShaderResourceBuffer(buffer.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.ShaderResourceViewDescription desc = new Vortice.Direct3D12.ShaderResourceViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Buffer;
                    desc.Shader4ComponentMapping = 5768;

                    m_Descriptors = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateShaderResourceView(m_Dx12Buffer.NativeResource, desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Buffer.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }
            else if (descriptor.ViewType == ERHIBufferViewType.UnorderedAccess)
            {
                if (Dx12Utility.IsUnorderedAccessBuffer(buffer.Descriptor.UsageFlag))
                {
                    Vortice.Direct3D12.UnorderedAccessViewDescription desc = new Vortice.Direct3D12.UnorderedAccessViewDescription();
                    desc.Format = Vortice.DXGI.Format.Unknown;
                    desc.Buffer.NumElements = (uint)descriptor.Count;
                    desc.Buffer.FirstElement = (ulong)descriptor.Offset;
                    desc.Buffer.StructureByteStride = (uint)descriptor.Stride;
                    desc.ViewDimension = Vortice.Direct3D12.UnorderedAccessViewDimension.Buffer;

                    m_Descriptors = m_Dx12Buffer.Dx12Device.AllocateCbvSrvUavDescriptorPair();
                    m_HasDescriptors = true;
                    m_Dx12Buffer.Dx12Device.NativeDevice.CreateUnorderedAccessView(m_Dx12Buffer.NativeResource, null, desc, m_Descriptors.Staging.CpuHandle);
                    m_Dx12Buffer.Dx12Device.CopyDescriptorToShaderVisible(m_Descriptors);
                }
            }

            if (!m_HasDescriptors)
            {
                throw new ArgumentException($"DX12 cannot create {descriptor.ViewType} buffer view from usage {buffer.Descriptor.UsageFlag}.", nameof(descriptor));
            }
        }

        protected override void Release()
        {
            if (m_HasDescriptors)
            {
                m_Dx12Buffer.Dx12Device.FreeDescriptorPair(m_Descriptors);
                m_HasDescriptors = false;
            }
        }
    }
}
