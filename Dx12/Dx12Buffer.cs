using Infinity.Mathmatics;
using System;
using System.Diagnostics;
using TerraFX.Interop.DirectX;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class Dx12Buffer : RHIBuffer
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }
        public ID3D12Resource* NativeResource
        {
            get
            {
                return m_NativeResource;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12Resource* m_NativeResource;

        public Dx12Buffer(Dx12Device device, in RHIBufferDescriptor descriptor)
        {
            m_Dx12Device = device;
            //m_State = RHIUtility.ConvertToBufferStateFormStorageMode(descriptor.StorageMode);
            m_Descriptor = descriptor;

            D3D12_RESOURCE_DESC resourceDesc = D3D12_RESOURCE_DESC.Buffer((ulong)descriptor.ByteSize, Dx12Utility.ConvertToDx12BufferFlag(descriptor.UsageFlag));
            D3D12_HEAP_PROPERTIES heapProperties = new D3D12_HEAP_PROPERTIES(Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode));

            ID3D12Resource* dx12Resource;
            bool success = SUCCEEDED(m_Dx12Device.NativeDevice->CreateCommittedResource(&heapProperties, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, &resourceDesc, Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode), null, __uuidof<ID3D12Resource>(), (void**)&dx12Resource));
#if DEBUG
            Debug.Assert(success);
#endif
            m_NativeResource = dx12Resource;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != EStorageMode.GPULocal);
#endif

            void* data;
            D3D12_RANGE range = new D3D12_RANGE(readBegin, math.min(readEnd, (uint)m_Descriptor.ByteSize));
            bool success = SUCCEEDED(m_NativeResource->Map(0, &range, &data));
#if DEBUG
            Debug.Assert(success);
#endif
            return new IntPtr(data);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != EStorageMode.GPULocal);
#endif
            D3D12_RANGE range = new D3D12_RANGE(writeBegin, math.min(writeEnd, (uint)m_Descriptor.ByteSize));
            m_NativeResource->Unmap(0, &range);
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return new Dx12BufferView(this, descriptor);
        }

        protected override void Release()
        {
            m_NativeResource->Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
