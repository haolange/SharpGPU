using System.Diagnostics;
using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602
    internal unsafe class Dx12Heap : RHIHeap
    {
        public Dx12Heap(Dx12Device device, in RHIHeapDescription descriptor)
        {

        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602
}
