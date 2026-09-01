using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class StorageIoPortableContractTests
{
    [Fact]
    public void StorageCapabilities_ShouldExposeIndependentNativeFacets()
    {
        AssertDirectStorageCapability(nameof(RHIStorageCapabilities.NativeGpuFileIo));
        AssertDirectStorageCapability(nameof(RHIStorageCapabilities.GpuDecompression));
        AssertDirectStorageCapability(nameof(RHIStorageCapabilities.RequestCancellation));
        AssertDirectStorageCapability(nameof(RHIStorageCapabilities.IoPriority));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MaximumStorageRequestBytes));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MaximumStorageConcurrentRequests));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedStorageCompressionFormatMask));
        Assert.True(Enum.IsDefined(ERHIStorageCompressionFormat.None));
        Assert.True(Enum.IsDefined(ERHIStorageCompressionFormat.GDeflate));
        Assert.Equal(
            2,
            Enum.GetValues<ERHIStorageCompressionFormat>().Length);
        Assert.NotNull(
            typeof(RHIStorageQueue).GetMethod(
                nameof(RHIStorageQueue.CancelRequestsWithTag),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIStorageQueue).GetMethod(
                nameof(RHIStorageQueue.CancelPending),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIStorageBufferRequest).GetField(nameof(RHIStorageBufferRequest.CompressionFormat)));
        Assert.NotNull(
            typeof(RHIStorageBufferRequest).GetField(nameof(RHIStorageBufferRequest.UncompressedSize)));
        Assert.NotNull(
            typeof(RHIStorageBufferRequest).GetField(nameof(RHIStorageBufferRequest.CancellationTag)));
        Assert.NotNull(
            typeof(RHIStorageTextureRequest).GetField(nameof(RHIStorageTextureRequest.CompressionFormat)));
        Assert.NotNull(
            typeof(RHIStorageTextureRequest).GetField(nameof(RHIStorageTextureRequest.UncompressedSize)));
        Assert.NotNull(
            typeof(RHIStorageTextureRequest).GetField(nameof(RHIStorageTextureRequest.CancellationTag)));
    }

    [Fact]
    public void CreateUnprobed_ShouldKeepAllStorageFacetsUnavailable()
    {
        RHIDeviceCapabilities unprobed = RHIDeviceCapabilities.CreateUnprobed("storage-io portable");
        AssertUnavailableStorageFacet(unprobed.Storage.NativeGpuFileIo);
        AssertUnavailableStorageFacet(unprobed.Storage.GpuDecompression);
        AssertUnavailableStorageFacet(unprobed.Storage.RequestCancellation);
        AssertUnavailableStorageFacet(unprobed.Storage.IoPriority);
    }

    [Fact]
    public void VulkanStorage_ShouldKeepAllFourFacetsUnavailable()
    {
        const string vulkanReason =
            "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.";

        if (!FeatureContractContext.TryCreateInstance(
                ERHIBackend.Vulkan,
                out RHIInstance? instance,
                out string createReason))
        {
            RHIDeviceCapabilities unprobed = RHIDeviceCapabilities.CreateUnprobed(
                "Vulkan storage contract");
            Assert.Equal(ERHICapabilityTier.Unavailable, unprobed.Storage.NativeGpuFileIo.Tier);
            _ = createReason;
            return;
        }

        using (instance)
        {
            Assert.True(instance.DeviceCount > 0, "Vulkan instance enumerated no devices.");
            for (int deviceIndex = 0; deviceIndex < instance.DeviceCount; ++deviceIndex)
            {
                RHIStorageCapabilities storage = instance.GetDevice(deviceIndex).Capabilities.Storage;
                AssertUnavailableWithReason(storage.NativeGpuFileIo, vulkanReason);
                AssertUnavailableWithReason(storage.GpuDecompression, vulkanReason);
                AssertUnavailableWithReason(storage.RequestCancellation, vulkanReason);
                AssertUnavailableWithReason(storage.IoPriority, vulkanReason);
            }
        }
    }

    [Fact]
    public void StorageFacets_ShouldStayIndependentOnCreatedDevices()
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
                Assert.True(instance.DeviceCount > 0, $"{backend} instance enumerated no devices.");
                for (int deviceIndex = 0; deviceIndex < instance.DeviceCount; ++deviceIndex)
                {
                    AssertIndependentStorageFacets(instance.GetDevice(deviceIndex));
                }
            }
        }
    }

    private static void AssertIndependentStorageFacets(RHIDevice device)
    {
        RHIStorageCapabilities storage = device.Capabilities.Storage;
        Assert.False(string.IsNullOrWhiteSpace(storage.NativeGpuFileIo.Provenance.Source));
        Assert.False(string.IsNullOrWhiteSpace(storage.GpuDecompression.Provenance.Source));
        Assert.False(string.IsNullOrWhiteSpace(storage.RequestCancellation.Provenance.Source));
        Assert.False(string.IsNullOrWhiteSpace(storage.IoPriority.Provenance.Source));

        if (device.BackendType == ERHIBackend.Vulkan)
        {
            const string vulkanReason =
                "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.";
            AssertUnavailableWithReason(storage.NativeGpuFileIo, vulkanReason);
            AssertUnavailableWithReason(storage.GpuDecompression, vulkanReason);
            AssertUnavailableWithReason(storage.RequestCancellation, vulkanReason);
            AssertUnavailableWithReason(storage.IoPriority, vulkanReason);
            return;
        }

        if (storage.NativeGpuFileIo.Tier != ERHICapabilityTier.Unavailable)
        {
            Assert.True(
                storage.NativeGpuFileIo.Limits.TryGetValue(
                    ERHICapabilityLimitKind.MaximumStorageRequestBytes,
                    out ulong maxRequestBytes) &&
                maxRequestBytes > 0);
            Assert.True(
                storage.NativeGpuFileIo.Limits.TryGetValue(
                    ERHICapabilityLimitKind.MaximumStorageConcurrentRequests,
                    out ulong maxConcurrent) &&
                maxConcurrent > 0);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(storage.NativeGpuFileIo.UnavailableReason));
        }

        if (storage.GpuDecompression.Tier != ERHICapabilityTier.Unavailable)
        {
            Assert.True(
                storage.GpuDecompression.Limits.TryGetValue(
                    ERHICapabilityLimitKind.SupportedStorageCompressionFormatMask,
                    out ulong formatMask));
            Assert.Equal((ulong)ERHIStorageCompressionFormat.GDeflate, formatMask);
            Assert.DoesNotContain(
                "CPU",
                storage.GpuDecompression.Provenance.Source,
                StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(storage.GpuDecompression.UnavailableReason));
            Assert.DoesNotContain(
                "supported",
                storage.GpuDecompression.UnavailableReason,
                StringComparison.OrdinalIgnoreCase);
        }

        if (device.BackendType == ERHIBackend.Metal &&
            storage.NativeGpuFileIo.Tier != ERHICapabilityTier.Unavailable)
        {
            Assert.Equal(ERHICapabilityTier.Unavailable, storage.GpuDecompression.Tier);
            Assert.Contains(
                "cannot be enqueued",
                storage.GpuDecompression.UnavailableReason,
                StringComparison.Ordinal);
        }
    }

    private static void AssertUnavailableStorageFacet(RHICapability capability)
    {
        Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
        Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
        Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
    }

    private static void AssertUnavailableWithReason(RHICapability capability, string reason)
    {
        AssertUnavailableStorageFacet(capability);
        Assert.Equal(reason, capability.UnavailableReason);
    }

    private static void AssertDirectStorageCapability(string propertyName)
    {
        PropertyInfo? property = typeof(RHIStorageCapabilities).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
    }
}
