using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class VulkanInstanceVersionContractTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void LoaderVersionNegotiation_ShouldUseHighestKnownVersion()
    {
        Assert.Equal(
            VulkanUtility.Version(1, 0, 0),
            VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 0, 0),
                EOSPlatform.Windows));
        Assert.Equal(
            VulkanUtility.Version(1, 2, 0),
            VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 2, 0),
                EOSPlatform.Linux));
        Assert.Equal(
            VulkanUtility.Version(1, 4, 7),
            VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 4, 7),
                EOSPlatform.Windows));
        Assert.Equal(
            VulkanUtility.Version(1, 27, 4095),
            VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 27, 4095),
                EOSPlatform.Linux));
        Assert.Throws<NotSupportedException>(
            () => VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(2, 0, 0),
                EOSPlatform.Windows));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void HeadlessInstance_DoesNotRequestSwapchainDeviceExtension()
    {
        Assert.False(
            VulkanInstance.RequiresSwapchainDeviceExtension(
                ERHINativeSurfaceKind.Headless));
        Assert.True(
            VulkanInstance.RequiresSwapchainDeviceExtension(
                ERHINativeSurfaceKind.Win32Hwnd));
        Assert.True(
            VulkanInstance.RequiresSwapchainDeviceExtension(
                ERHINativeSurfaceKind.X11Window));
        Assert.True(
            VulkanInstance.RequiresSwapchainDeviceExtension(
                ERHINativeSurfaceKind.AndroidNativeWindow));
    }

    [Fact]
    [Trait("Category", "SharpGpuAndroidQualified")]
    public void AndroidLoaderVersionNegotiation_ShouldRequireVulkan11()
    {
        Assert.Throws<NotSupportedException>(
            () => VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 0, 0),
                EOSPlatform.Android));
        Assert.Equal(
            VulkanUtility.Version(1, 1, 0),
            VulkanInstance.SelectInstanceApiVersion(
                VulkanUtility.Version(1, 1, 0),
                EOSPlatform.Android));
    }
}
