using System;
using System.Collections.Generic;

namespace SharpGPU
{
#pragma warning disable CA1416, CS8602, CS8618
    internal unsafe class Dx12Instance : RHIInstance
    {
        public Vortice.DXGI.IDXGIFactory7 DXGIFactory
        {
            get
            {
                return m_DXGIFactory;
            }
        }
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.DirectX12;

        private List<Dx12Device> m_Devices;
        private Vortice.DXGI.IDXGIFactory7 m_DXGIFactory;

        public Dx12Instance(in RHIInstanceDescriptor descriptor)
        {
            CreateDX12Factory(descriptor);
            EnumerateAdapters(descriptor);
        }

        private void CreateDX12Factory(in RHIInstanceDescriptor descriptor)
        {
            Dx12Agility.EnsureInitialized();

            uint factoryFlags = 0;

            if(descriptor.EnableDebugLayer)
            {
                if (Vortice.Direct3D12.D3D12.D3D12GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug debug).Success)
                {
                    debug.EnableDebugLayer();
                    factoryFlags |= Vortice.DXGI.DXGI.CreateFactoryDebug;

                    if (descriptor.EnableValidatior)
                    {
                        Vortice.Direct3D12.Debug.ID3D12Debug1 debug1 = debug.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12Debug1>();
                        if (debug1 != null)
                        {
                            debug1.SetEnableGPUBasedValidation(true);
                            debug1.Release();
                        }
                    }

                    debug.Release();
                }
            }

            Vortice.DXGI.IDXGIFactory7 factory;
            SharpGen.Runtime.Result hResult = Vortice.DXGI.DXGI.CreateDXGIFactory2((factoryFlags & Vortice.DXGI.DXGI.CreateFactoryDebug) != 0, out factory);
#if DEBUG
            Dx12Utility.CHECK_HR(hResult);
#endif
            m_DXGIFactory = factory;
        }

        private void EnumerateAdapters(in RHIInstanceDescriptor descriptor)
        {
            m_Devices = new List<Dx12Device>(2);

            for (uint i = 0; ; ++i)
            {
                SharpGen.Runtime.Result hResult = m_DXGIFactory.EnumAdapters1(i, out Vortice.DXGI.IDXGIAdapter1 adapter);
                if (hResult.Failure)
                {
                    break;
                }

                Vortice.DXGI.AdapterDescription1 adapterDesc = adapter.Description1;
                try
                {
                    m_Devices.Add(new Dx12Device(this, adapter, descriptor.ComputeQueueRequestCount, descriptor.TransferQueueRequestCount, descriptor.GraphicsQueueRequestCount));
                }
                catch (Exception) when ((adapterDesc.Flags & Vortice.DXGI.AdapterFlags.Software) != 0)
                {
                    adapter.Release();
                    continue;
                }
                catch (Exception ex)
                {
                    adapter.Release();
                    throw new InvalidOperationException($"Failed to create DX12 device for adapter '{adapterDesc.Description}' (Vendor=0x{adapterDesc.VendorId:X4}, Device=0x{adapterDesc.DeviceId:X4}).", ex);
                }
            }
        }

        public override RHIDevice GetDevice(in int index)
        {
            return m_Devices[index];
        }

        protected override void Release()
        {
            for(int i = 0; i < m_Devices.Count; ++i)
            {
                m_Devices?[i].Dispose();
            }

            DXGIFactory.Release();
        }
    }
#pragma warning restore CA1416, CS8602, CS8618
}
