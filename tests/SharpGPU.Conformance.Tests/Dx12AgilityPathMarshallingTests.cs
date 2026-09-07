using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Xunit;

namespace SharpGPU.Tests
{
    public unsafe sealed class Dx12AgilityPathMarshallingTests
    {
        [ThreadStatic]
        private static string? s_ObservedPath;

        [Theory]
        [InlineData("D:/Products/D3D12/")]
        [InlineData("D:/独立 产品/渲染😀/D3D12/")]
        public void DeviceFactoryPath_ReachesNativeBoundaryAsUtf8(string path)
        {
            void** table = (void**)NativeMemory.AllocZeroed(6, (nuint)sizeof(void*));
            void** instance = (void**)NativeMemory.Alloc((nuint)sizeof(void*));
            table[2] = (void*)(delegate* unmanaged[Stdcall]<void*, uint>)&Release;
            table[4] = (void*)(delegate* unmanaged[Stdcall]<void*, uint, byte*, Guid*, nint*, int>)&CreateFactory;
            *instance = table;
            try
            {
                using ID3D12SDKConfiguration1 configuration = new((nint)instance);
                s_ObservedPath = null;
                var result = configuration.CreateDeviceFactory(619, path, out ID3D12DeviceFactory? factory);
                Assert.True(result.Failure);
                Assert.Null(factory);
                Assert.Equal(path, s_ObservedPath);
                Assert.Throws<ArgumentException>(() => configuration.CreateDeviceFactory(619, path + '\0' + "suffix"));
            }
            finally
            {
                NativeMemory.Free(instance);
                NativeMemory.Free(table);
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint Release(void* instance) => 0;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static int CreateFactory(void* instance, uint version, byte* path, Guid* iid, nint* factory)
        {
            s_ObservedPath = Marshal.PtrToStringUTF8((nint)path);
            *factory = 0;
            return unchecked((int)0x80004005);
        }
    }
}
