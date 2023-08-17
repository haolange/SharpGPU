using System.Diagnostics;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CA1416, CS8602, CS8618
    internal unsafe class Dx12Instance : RHIInstance
    {
        public IDXGIFactory7* DXGIFactory
        {
            get
            {
                return m_DXGIFactory;
            }
        }
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.DirectX12;

        private List<Dx12Device> m_Devices;
        private IDXGIFactory7* m_DXGIFactory;

        public Dx12Instance(in RHIInstanceDescriptor descriptor)
        {
            CreateDX12Factory(descriptor);
            EnumerateAdapters(descriptor);
        }

        private void CreateDX12Factory(in RHIInstanceDescriptor descriptor)
        {
            uint factoryFlags = 0;

            if(descriptor.EnableDebugLayer)
            {
                ID3D12Debug* debug;
                if (SUCCEEDED(DirectX.D3D12GetDebugInterface(__uuidof<ID3D12Debug>(), (void**)&debug)))
                {
                    debug->EnableDebugLayer();
                    factoryFlags |= DXGI.DXGI_CREATE_FACTORY_DEBUG;

                    if (descriptor.EnableValidatior)
                    {
                        ID3D12Debug1* debug1;
                        if (SUCCEEDED(debug->QueryInterface(__uuidof<ID3D12Debug1>(), (void**)&debug1)))
                        {
                            debug1->SetEnableGPUBasedValidation(true);
                            debug1->Release();
                        }
                    }
                }
            }

            IDXGIFactory7* factory;
            HRESULT hResult = DirectX.CreateDXGIFactory2(factoryFlags, __uuidof<IDXGIFactory7>(), (void**)&factory);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DXGIFactory = factory;
        }

        private void EnumerateAdapters(in RHIInstanceDescriptor descriptor)
        {
            IDXGIAdapter1* adapter = null;
            m_Devices = new List<Dx12Device>(2);

            for (uint i = 0; SUCCEEDED(m_DXGIFactory->EnumAdapters1(i, &adapter)); ++i)
            {
                m_Devices.Add(new Dx12Device(this, adapter));
                adapter = null;
            }
        }

        public override RHIDevice GetDevice(in int index)
        {
            return m_Devices[index];
        }

        protected override void Release()
        {
            DXGIFactory->Release();

            for(int i = 0; i < m_Devices.Count; ++i)
            {
                m_Devices?[i].Dispose();
            }
        }
    }
#pragma warning restore CA1416, CS8602, CS8618
}
