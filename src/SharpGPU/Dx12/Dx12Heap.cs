using System.Diagnostics;

namespace SharpGPU
{
    internal unsafe class Dx12Heap : RHIHeap
    {
        public Vortice.Direct3D12.ID3D12Heap NativeHeap
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeHeap;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Heap m_NativeHeap;

        public Dx12Heap(Dx12Device device, in RHIHeapDescription descriptor)
            : base(device, descriptor, Dx12MemoryUtility.SelectCompatibilityBit(descriptor.Compatibility.CompatibilityMask))
        {
            m_Dx12Device = device;

            Vortice.Direct3D12.HeapDescription heapDesc = new Vortice.Direct3D12.HeapDescription();
            heapDesc.SizeInBytes = descriptor.Size;
            heapDesc.Alignment = descriptor.Compatibility.Alignment;
            heapDesc.Properties.Type = Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode);
            heapDesc.Flags = Dx12MemoryUtility.GetHeapFlags(CompatibilityBit);

            Vortice.Direct3D12.ID3D12Heap? nativeHeap;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateHeap(heapDesc, out nativeHeap);
            m_NativeHeap = Dx12Utility.RequireCreatedObject(
                nativeHeap,
                hResult,
                "ID3D12Device.CreateHeap");
        }

        protected override void Release()
        {
            m_NativeHeap.Release();
        }
    }
}
