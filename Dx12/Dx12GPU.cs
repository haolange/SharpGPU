using TerraFX.Interop.DirectX;

namespace Infinity.Graphics
{
#pragma warning disable CA1416
    internal unsafe class Dx12GPU : RHIGPU
    {
        public Dx12Instance Dx12Instance
        {
            get
            {
                return m_Dx12Instance;
            }
        }
        public IDXGIAdapter1* DXGIAdapter
        {
            get
            {
                return m_DXGIAdapter;
            }
        }

        private Dx12Instance m_Dx12Instance;
        private IDXGIAdapter1* m_DXGIAdapter;

        public Dx12GPU(Dx12Instance instance, in IDXGIAdapter1* adapter)
        {
            m_DXGIAdapter = adapter;
            m_Dx12Instance = instance;
        }

        public override RHIGpuProperty GetProperty()
        {
            DXGI_ADAPTER_DESC1 desc;
            m_DXGIAdapter->GetDesc1(&desc);

            RHIGpuProperty property = new RHIGpuProperty();
            property.VendorId = desc.VendorId;
            property.DeviceId = desc.DeviceId;
            property.Type = (desc.Flags & (uint)DXGI_ADAPTER_FLAG.DXGI_ADAPTER_FLAG_SOFTWARE) == 1 ? EGpuType.Software : EGpuType.Hardware;
            return property;
        }

        public override RHIDevice CreateDevice(in RHIDeviceDescriptor descriptor)
        {
            return new Dx12Device(this, descriptor);
        }

        protected override void Release()
        {
            m_DXGIAdapter->Release();
        }
    }
#pragma warning restore CA1416
}
