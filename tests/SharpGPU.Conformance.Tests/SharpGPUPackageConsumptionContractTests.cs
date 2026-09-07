using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUPackageConsumptionContractTests
{
    [Fact]
    public void Package_ShouldCarryFeatureAndNativeManifestsAsAssemblyResources()
    {
        Assembly assembly = typeof(RHIInstance).Assembly;
        Assert.NotNull(assembly.GetManifestResourceStream("SharpGPU.FeatureMatrix.md"));
        using Stream? stream = assembly.GetManifestResourceStream("SharpGPU.native.assets.json");
        Assert.NotNull(stream);
        using JsonDocument document = JsonDocument.Parse(stream!);
        JsonElement assets = document.RootElement.GetProperty("assets");
        Assert.True(assets.GetArrayLength() >= 14);
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string path = asset.GetProperty("path").GetString() ?? string.Empty;
            Assert.Contains("runtimes/", path, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(asset.GetProperty("sha256").GetString()));
        }
    }

    [Fact]
    public void NativeDeployment_ShouldExposeApplicationSdkAndPreservePackageRidLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string applicationSdk = Path.Combine(AppContext.BaseDirectory, "D3D12");
        Assert.True(File.Exists(Path.Combine(applicationSdk, "D3D12Core.dll")));
        Assert.True(File.Exists(Path.Combine(applicationSdk, "d3d12SDKLayers.dll")));
#if !SHARPGPU_SOURCE_LAYOUT_TESTS
        string nativeDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "runtimes",
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "win-arm64"
                : "win-x64",
            "native");
        Assert.True(
            File.Exists(Path.Combine(nativeDirectory, "D3D12Core.dll")),
            $"SharpGPU native runtime is missing from '{nativeDirectory}'.");
#endif
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "D3D12Core.dll")));
    }
}
