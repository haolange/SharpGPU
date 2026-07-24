using System;
using System.IO;
using Vortice.Direct3D12;

namespace SharpGPU
{
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
}
