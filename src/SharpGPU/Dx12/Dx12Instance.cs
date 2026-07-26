using System;
using System.Collections.Generic;
using System.IO;
using Vortice.Direct3D12;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12Instance : RHIInstance
    {
        public Vortice.DXGI.IDXGIFactory7 DXGIFactory =>
            m_DXGIFactory ?? throw new ObjectDisposedException(GetType().FullName);

        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.DirectX12;

        private List<Dx12Device> m_Devices = new List<Dx12Device>();
        private Vortice.DXGI.IDXGIFactory7? m_DXGIFactory;

        public Dx12Instance(in RHIInstanceDescriptor descriptor)
        {
            CreateDX12Factory(descriptor);
            EnumerateAdapters(descriptor);
        }

        private void CreateDX12Factory(in RHIInstanceDescriptor descriptor)
        {
            Dx12Agility.EnsureInitialized();
            Dx12DeviceLossDiagnostics.Configure(
                descriptor.EnableDebugLayer ||
                descriptor.EnableValidation);

            uint factoryFlags = 0;

            if (descriptor.EnableDebugLayer)
            {
                SharpGen.Runtime.Result debugResult =
                    Dx12Agility.GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug? debug);
                if (debugResult.Failure || debug == null)
                {
                    throw new InvalidOperationException(
                        $"DX12 debug layer was requested but its Agility SDK configuration interface is unavailable (HRESULT=0x{(int)debugResult:X8}; {Dx12Agility.Diagnostic}).");
                }

                try
                {
                    debug.EnableDebugLayer();
                    factoryFlags |= Vortice.DXGI.DXGI.CreateFactoryDebug;

                    if (descriptor.EnableValidation)
                    {
                        Vortice.Direct3D12.Debug.ID3D12Debug1? debug1 =
                            debug.QueryInterfaceOrNull<Vortice.Direct3D12.Debug.ID3D12Debug1>();
                        try
                        {
                            debug1?.SetEnableGPUBasedValidation(true);
                        }
                        finally
                        {
                            debug1?.Release();
                        }
                    }
                }
                finally
                {
                    debug.Release();
                }
            }

            Vortice.DXGI.IDXGIFactory7? factory;
            SharpGen.Runtime.Result hResult = Vortice.DXGI.DXGI.CreateDXGIFactory2((factoryFlags & Vortice.DXGI.DXGI.CreateFactoryDebug) != 0, out factory);
            m_DXGIFactory = Dx12Utility.RequireCreatedObject(
                factory,
                hResult,
                "CreateDXGIFactory2");
        }

        private void EnumerateAdapters(in RHIInstanceDescriptor descriptor)
        {
            m_Devices = new List<Dx12Device>(2);

            for (uint i = 0; ; ++i)
            {
                SharpGen.Runtime.Result hResult = DXGIFactory.EnumAdapters1(i, out Vortice.DXGI.IDXGIAdapter1 adapter);
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
            ThrowIfDisposed();
            if ((uint)index >= (uint)m_Devices.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"Device index must be in [0, {m_Devices.Count}).");
            }

            return m_Devices[index];
        }

        protected override void Release()
        {
            for(int i = 0; i < m_Devices.Count; ++i)
            {
                m_Devices[i].Dispose();
            }

            DXGIFactory.Release();
        }
    }
#pragma warning restore CA1416

    #region Agility
#pragma warning disable CA1416
internal static class Dx12Agility
    {
        internal const uint SDKVersion = 619;
        internal const string SDKPath = ".\\D3D12\\";
        private static readonly Guid s_D3D12DebugClassId = new Guid("F2352AEB-DD84-49FE-B97B-A9DCFDCC1B4F");

        private static readonly object s_Lock = new object();
        private static bool s_Initialized;
        private static ID3D12DeviceFactory? s_DeviceFactory;
        private static string s_Diagnostic = "DX12 Agility SDK has not been initialized.";

        internal static bool IsDeviceFactoryAvailable
        {
            get
            {
                EnsureInitialized();
                return s_DeviceFactory != null;
            }
        }

        internal static string Diagnostic
        {
            get
            {
                EnsureInitialized();
                return s_Diagnostic;
            }
        }

        internal static bool TryGetDeviceFactory(out ID3D12DeviceFactory? deviceFactory)
        {
            EnsureInitialized();
            deviceFactory = s_DeviceFactory;
            return deviceFactory != null;
        }

        internal static SharpGen.Runtime.Result GetDebugInterface(out Vortice.Direct3D12.Debug.ID3D12Debug? debug)
        {
            EnsureInitialized();
            if (s_DeviceFactory != null)
            {
                return s_DeviceFactory.GetConfigurationInterface(s_D3D12DebugClassId, out debug);
            }

            return D3D12.D3D12GetDebugInterface(out debug);
        }

        internal static void EnsureInitialized()
        {
            if (s_Initialized)
            {
                return;
            }

            lock (s_Lock)
            {
                if (s_Initialized)
                {
                    return;
                }

                s_Initialized = true;

                if (!OperatingSystem.IsWindows())
                {
                    s_Diagnostic = "DX12 Agility SDK is only initialized on Windows.";
                    return;
                }

                try
                {
                    SharpGen.Runtime.Result getConfigResult = D3D12.D3D12GetInterface(D3D12.D3D12SDKConfigurationClsId, out ID3D12SDKConfiguration1? sdkConfiguration);
                    if (getConfigResult.Failure || sdkConfiguration == null)
                    {
                        s_Diagnostic = $"D3D12GetInterface(ID3D12SDKConfiguration1) failed with HRESULT=0x{getConfigResult.Code:X8}.";
                        return;
                    }

                    try
                    {
                        string sdkPath = ResolveSDKPath();
                        SharpGen.Runtime.Result createFactoryResult = sdkConfiguration.CreateDeviceFactory(SDKVersion, sdkPath, out ID3D12DeviceFactory? deviceFactory);
                        if (createFactoryResult.Failure || deviceFactory == null)
                        {
                            s_Diagnostic = $"ID3D12SDKConfiguration1.CreateDeviceFactory({SDKVersion}, \"{sdkPath}\") failed with HRESULT=0x{createFactoryResult.Code:X8}. Ensure D3D12Core.dll exists under the output D3D12 folder.";
                            return;
                        }

                        s_DeviceFactory = deviceFactory;
                        s_Diagnostic = $"ID3D12SDKConfiguration1.CreateDeviceFactory({SDKVersion}, \"{sdkPath}\") succeeded.";
                    }
                    finally
                    {
                        sdkConfiguration.Release();
                    }
                }
                catch (Exception ex)
                {
                    s_Diagnostic = $"DX12 Agility SDK initialization failed: {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        private static string ResolveSDKPath()
        {
            string outputRelativePath = Path.Combine(AppContext.BaseDirectory, "D3D12") + Path.DirectorySeparatorChar;
            return File.Exists(Path.Combine(outputRelativePath, "D3D12Core.dll"))
                ? outputRelativePath
                : SDKPath;
        }
    }
#pragma warning restore CA1416
    #endregion
}
