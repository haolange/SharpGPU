using System;
using System.Diagnostics;
using SharpGPU.Mathematics;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12Buffer : RHIBuffer
    {
        public Dx12Device Dx12Device
        {
            get
            {
                ThrowIfDisposed(); return m_Dx12Device;
            }
        }
        public Vortice.Direct3D12.ID3D12Resource NativeResource
        {
            get
            {
                ThrowIfDisposed(); return m_NativeResource;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Resource m_NativeResource;
        private RHIHeapPlacement? m_Placement;

        public Dx12Buffer(Dx12Device device, in RHIBufferDescriptor descriptor)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;

            Vortice.Direct3D12.ResourceDescription resourceDesc =
                Dx12MemoryUtility.BuildBufferDescription(descriptor);
            Vortice.Direct3D12.HeapProperties heapProperties = new Vortice.Direct3D12.HeapProperties(Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode));

            Vortice.Direct3D12.ID3D12Resource? dx12Resource;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateCommittedResource(
                heapProperties,
                Vortice.Direct3D12.HeapFlags.None,
                resourceDesc,
                Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode),
                null,
                out dx12Resource);
            m_NativeResource = Dx12Utility.RequireCreatedObject(
                dx12Resource,
                hResult,
                "ID3D12Device.CreateCommittedResource(buffer)");
        }

        internal Dx12Buffer(
            Dx12Device device,
            in RHIBufferDescriptor descriptor,
            Dx12Heap heap,
            ulong heapOffset,
            RHIHeapPlacement placement)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_AllocationMode = ERHIResourceAllocationMode.Placed;
            Vortice.Direct3D12.ResourceDescription resourceDescription =
                Dx12MemoryUtility.BuildBufferDescription(descriptor);
            SharpGen.Runtime.Result result = device.NativeDevice.CreatePlacedResource(
                heap.NativeHeap,
                heapOffset,
                resourceDescription,
                Dx12Utility.ConvertToDx12ResourceStateFormStorageMode(descriptor.StorageMode),
                out Vortice.Direct3D12.ID3D12Resource? resource);
            Dx12Utility.CHECK_HR(result);
            m_NativeResource = resource ?? throw new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.DirectX12,
                result.Code,
                "CreatePlacedResource returned a null DX12 buffer.",
                ERHIDeviceState.Operational);
            m_Placement = placement;
        }

        public override IntPtr Map(in uint readBegin, in uint readEnd)
        {
            ThrowIfDisposed();
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPULocal)
            {
                throw new InvalidOperationException("A GPU-local DX12 buffer cannot be mapped.");
            }
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (readBegin > byteSize || (readEnd != 0 && (readEnd < readBegin || readEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(readEnd), "The read range must be within the buffer.");
            }

            void* data = null;
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(readBegin, math.min(readEnd, (uint)m_Descriptor.ByteSize));
            SharpGen.Runtime.Result hResult = m_NativeResource.Map(0, range, &data);
            Dx12Utility.CHECK_HR(hResult);
            return new IntPtr(data);
        }

        public override void UnMap(in uint writeBegin, in uint writeEnd)
        {
            ThrowIfDisposed();
            if (m_Descriptor.StorageMode == ERHIStorageMode.GPULocal)
            {
                throw new InvalidOperationException("A GPU-local DX12 buffer cannot be unmapped.");
            }
            uint byteSize = checked((uint)m_Descriptor.ByteSize);
            if (writeBegin > byteSize || (writeEnd != 0 && (writeEnd < writeBegin || writeEnd > byteSize)))
            {
                throw new ArgumentOutOfRangeException(nameof(writeEnd), "The write range must be within the buffer.");
            }
            Vortice.Direct3D12.Range range = new Vortice.Direct3D12.Range(writeBegin, math.min(writeEnd, (uint)m_Descriptor.ByteSize));
            m_NativeResource.Unmap(0, range);
        }

        public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12BufferView(this, descriptor);
        }

        protected override void Release()
        {
            m_NativeResource.Release();
            m_Placement?.Dispose();
            m_Placement = null;
        }
    }
#pragma warning restore CA1416
}
