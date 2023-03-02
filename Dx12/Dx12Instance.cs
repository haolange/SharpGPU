using System.Diagnostics;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using static TerraFX.Interop.Windows.Windows;

namespace Infinity.Graphics
{
#pragma warning disable CA1416
    internal unsafe class Dx12Instance : RHIInstance
    {
        public IDXGIFactory7* DXGIFactory
        {
            get
            {
                return m_DXGIFactory;
            }
        }
        public override int GpuCount => m_GPUs.Count;
        public override ERHIBackend RHIType => ERHIBackend.DirectX12;

        private List<Dx12GPU> m_GPUs;
        private IDXGIFactory7* m_DXGIFactory;

        public Dx12Instance(in RHIInstanceDescriptor descriptor)
        {
            m_GPUs = new List<Dx12GPU>(4);
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

                    if (descriptor.EnableGpuValidatior)
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
            bool success =  SUCCEEDED(DirectX.CreateDXGIFactory2(factoryFlags, __uuidof<IDXGIFactory7>(), (void**)&factory));
            Debug.Assert(success);
            m_DXGIFactory = factory;
        }

        private void EnumerateAdapters(in RHIInstanceDescriptor descriptor)
        {
            IDXGIAdapter1* adapter = null;

            for (uint i = 0; SUCCEEDED(m_DXGIFactory->EnumAdapters1(i, &adapter)); ++i)
            {
                m_GPUs.Add(new Dx12GPU(this, adapter));
                adapter = null;
            }
        }

        public override RHIGPU GetGpu(in int index)
        {
            return m_GPUs[index];
        }

        protected override void Release()
        {
            DXGIFactory->Release();

            for(int i = 0; i < m_GPUs?.Count; ++i)
            {
                m_GPUs?[i].Dispose();
            }
        }
    }
#pragma warning restore CA1416
}
