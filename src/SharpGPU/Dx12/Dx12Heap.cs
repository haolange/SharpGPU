using System.Diagnostics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602
    internal unsafe class Dx12Heap : RHIHeap
    {
        public ID3D12Heap* NativeHeap
        {
            get
            {
                return m_NativeHeap;
            }
        }

        private Dx12Device m_Dx12Device;
        private ID3D12Heap* m_NativeHeap;

        public Dx12Heap(Dx12Device device, in RHIHeapDescription descriptor)
        {
            m_Dx12Device = device;

            D3D12_HEAP_DESC heapDesc = new D3D12_HEAP_DESC();
            heapDesc.SizeInBytes = descriptor.Size;
            heapDesc.Alignment = 65536; // D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT (64KB)
            heapDesc.Properties.Type = Dx12Utility.ConvertToDx12HeapTypeByStorage(descriptor.StorageMode);
            heapDesc.Flags = D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE;

            ID3D12Heap* nativeHeap;
            HRESULT hResult = m_Dx12Device.NativeDevice->CreateHeap(&heapDesc, __uuidof<ID3D12Heap>(), (void**)&nativeHeap);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_NativeHeap = nativeHeap;
        }

        protected override void Release()
        {
            m_NativeHeap->Release();
        }
    }
#pragma warning restore CS8600, CS8602
}
