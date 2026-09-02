using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class AdapterIdentityPortableContractTests
    {
        [Fact]
        public void AdapterIdentity_IsPublicReadableDeviceFact()
        {
            PropertyInfo? property = typeof(RHIDevice).GetProperty(
                nameof(RHIDevice.AdapterIdentity),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.NotNull(property);
            Assert.Equal(typeof(RHIAdapterIdentity), property.PropertyType);
            Assert.True(property.CanRead);
            Assert.False(property.CanWrite);
            Assert.Null(GetPublicInstanceProperty(typeof(RHIDeviceCapabilities), "MultiGpu"));
        }

        [Fact]
        public void AdapterIdentity_IsReadableOnCreatedDevices_Dx12LuidIsNonZero()
        {
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                bool backendSupported = RHIInstance.IsBackendSupported(backend, out string supportedReason);
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out string createReason))
                {
                    if (OperatingSystem.IsWindows() &&
                        backend == ERHIBackend.DirectX12 &&
                        backendSupported)
                    {
                        Assert.Fail(
                            $"DirectX12 was supported but TryCreateInstance failed: {supportedReason} {createReason}");
                    }

                    continue;
                }

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        if (OperatingSystem.IsWindows() && backend == ERHIBackend.DirectX12)
                        {
                            Assert.Fail("DirectX12 instance enumerated no devices.");
                        }

                        continue;
                    }

                    for (int deviceIndex = 0; deviceIndex < instance.DeviceCount; ++deviceIndex)
                    {
                        RHIDevice device = instance.GetDevice(deviceIndex);
                        RHIAdapterIdentity identity = device.AdapterIdentity;
                        if (backend == ERHIBackend.DirectX12 && device.Type == ERHIDeviceType.Hardware)
                        {
                            Assert.True(identity.HasLuid);
                            Assert.NotEqual(0L, identity.Luid);
                        }

                        if (backend == ERHIBackend.Vulkan)
                        {
                            Assert.True(identity.HasDeviceUuid);
                        }
                    }
                }
            }
        }

        private static PropertyInfo? GetPublicInstanceProperty(Type type, string name)
        {
            return type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
    }
}
