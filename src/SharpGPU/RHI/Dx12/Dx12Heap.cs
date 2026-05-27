using System.Diagnostics;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602
    internal unsafe class Dx12Heap : RHIHeap
    {
        public Vortice.Direct3D12.ID3D12Heap NativeHeap
        {
            get
            {
                return m_NativeHeap;
            }
        }

        private Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12Heap m_NativeHeap;

        public Dx12Heap(Dx12Device device, in RHIHeapDescription descriptor)
        {
            m_Dx12Device = device;

            Vortice.Direct3D12.HeapDescription heapDesc = new Vortice.Direct3D12.HeapDescription();
            heapDesc.SizeInBytes = descriptor.Size;
            heapDesc.Alignment = 65536; // D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT (64KB)
            heapDesc.Properties.Type = Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode);
            heapDesc.Flags = Vortice.Direct3D12.HeapFlags.None;

            Vortice.Direct3D12.ID3D12Heap nativeHeap;
            SharpGen.Runtime.Result hResult = m_Dx12Device.NativeDevice.CreateHeap(heapDesc, out nativeHeap);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeHeap = nativeHeap;
        }

        protected override void Release()
        {
            m_NativeHeap.Release();
        }
    }
#pragma warning restore CS8600, CS8602
}
