// Copyright (c) Amer Koleci and Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repository root for more information.

namespace Vortice.Direct3D12;

public partial class ID3D12SDKConfiguration1
{
    private unsafe Result CreateDeviceFactory(uint sdkVersion, string sdkPath, Guid riid, out IntPtr factory)
    {
        ArgumentNullException.ThrowIfNull(sdkPath);
        if (sdkPath.Contains('\0'))
        {
            throw new ArgumentException("The SDK path must not contain a null character.", nameof(sdkPath));
        }
        IntPtr path = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(sdkPath);
        try
        {
            fixed (void* output = &factory)
            {
                return (Result)((delegate* unmanaged[Stdcall]<IntPtr, uint, void*, void*, void*, int>)this[4])(
                    NativePointer, sdkVersion, (void*)path, &riid, output);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path);
        }
    }

    public ID3D12DeviceFactory CreateDeviceFactory(uint sdkVersion, string sdkPath)
    {
        CreateDeviceFactory(sdkVersion, sdkPath, typeof(ID3D12DeviceFactory).GUID, out IntPtr nativePtr).CheckError();
        return new(nativePtr);
    }

    public Result CreateDeviceFactory(uint sdkVersion, string sdkPath, out ID3D12DeviceFactory? factory)
    {
        Result result = CreateDeviceFactory(sdkVersion, sdkPath, typeof(ID3D12DeviceFactory).GUID, out IntPtr nativePtr);

        if (result.Failure)
        {
            factory = null;
            return result;
        }

        factory = new(nativePtr);
        return result;
    }

    public T CreateDeviceFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(uint sdkVersion, string sdkPath) where T : ID3D12DeviceFactory
    {
        CreateDeviceFactory(sdkVersion, sdkPath, typeof(T).GUID, out IntPtr nativePtr).CheckError();
        return MarshallingHelpers.FromPointer<T>(nativePtr)!;
    }

    public Result CreateDeviceFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(uint sdkVersion, string sdkPath, out T? factory) where T : ID3D12DeviceFactory
    {
        Result result = CreateDeviceFactory(sdkVersion, sdkPath, typeof(T).GUID, out IntPtr nativePtr);

        if (result.Failure)
        {
            factory = null;
            return result;
        }

        factory = MarshallingHelpers.FromPointer<T>(nativePtr);
        return result;
    }
}
