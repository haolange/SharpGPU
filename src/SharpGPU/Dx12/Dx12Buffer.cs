using System;
using System.Diagnostics;
using Infinity.Mathmatics;

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
        public Vortice.Direct3D12.ID3D12Resource NativeResource
        {
            get
            {
                return m_NativeResource;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResource;

        public Dx12Buffer(Dx12Device device, in RHIBufferDescriptor descriptor)
        {
            m_Dx12Device = device;
            //m_State = RHIUtility.ConvertToBufferStateFormStorageMode(descriptor.StorageMode);
            m_Descriptor = descriptor;

            Vortice.Direct3D12.ResourceDescription resourceDesc = Vortice.Direct3D12.ResourceDescription.Buffer((ulong)descriptor.ByteSize, Dx12Utility.ConvertToDx12BufferFlag(descriptor.UsageFlag));
            Vortice.Direct3D12.HeapProperties heapProperties = new Vortice.Direct3D12.HeapProperties(Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode));

            Vortice.Direct3D12.ID3D12Resource dx12Resource;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                resourceDesc,
                Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode),
                null,
                out dx12Resource);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeResource = dx12Resource;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != ERHIStorageMode.GPULocal, "StorageMode is GPULocal it can't use Map()");
#endif

            void* data = null;
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(readBegin, math.min(readEnd, (uint)m_Descriptor.ByteSize));
            SharpGen.Runtime.Result hResult = m_NativeResource.Map(0, range, &data);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            return new IntPtr(data);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
#if DEBUG
            Debug.Assert(m_Descriptor.StorageMode != ERHIStorageMode.GPULocal, "StorageMode is GPULocal it can't use UnMap()");
#endif
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(writeBegin, math.min(writeEnd, (uint)m_Descriptor.ByteSize));
            m_NativeResource.Unmap(0, range);
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            return new Dx12BufferView(this, descriptor);
        }

        protected override void Release()
        {
            m_NativeResource.Release();
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
