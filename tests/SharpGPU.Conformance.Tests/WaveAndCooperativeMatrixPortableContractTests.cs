using System;
using System.Reflection;
using SharpGPU;
using Xunit;
#if SHARPGPU_ENABLE_DX12
using Vortice.Direct3D12;
#endif

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class WaveAndCooperativeMatrixPortableContractTests
{
    [Fact]
    public void ComputeCapabilities_ShouldExposeWaveAndCooperativeMatrixOnComputeNotMachineLearning()
    {
        AssertDirectComputeCapability(nameof(RHIComputeCapabilities.WaveOperations));
        AssertDirectComputeCapability(nameof(RHIComputeCapabilities.VariableSubgroupSize));
        AssertDirectComputeCapability(nameof(RHIComputeCapabilities.CooperativeMatrix));
        Assert.Null(
            typeof(RHIMachineLearningCapabilities).GetProperty(
                "CooperativeMatrix",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.WaveStageMask));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.CooperativeMatrixConfigCount));
        Assert.NotNull(
            typeof(RHIDevice).GetMethod(
                nameof(RHIDevice.QueryCooperativeMatrixConfigs),
                BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void WaveSizesAndCooperativeMatrix_ShouldMatchNativeProbesAndRequireCapability()
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
                RHIDevice device = instance.GetDevice(0);
                AssertWaveProbe(backend, device);
                AssertCooperativeMatrixQuery(device);
            }
        }
    }

    private static void AssertWaveProbe(ERHIBackend backend, RHIDevice device)
    {
        RHICapability wave = device.Capabilities.Compute.WaveOperations;
        Assert.False(string.IsNullOrWhiteSpace(wave.Provenance.Source));
        Assert.DoesNotContain(
            "minStorageBufferOffsetAlignment",
            wave.Provenance.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "storage-buffer",
            wave.Provenance.Source,
            StringComparison.OrdinalIgnoreCase);

        switch (backend)
        {
            case ERHIBackend.DirectX12:
                AssertDx12WaveOracle(device, wave);
                break;
            case ERHIBackend.Vulkan:
                Assert.True(
                    wave.Provenance.Source.Contains("SubgroupProperties", StringComparison.Ordinal) ||
                    wave.Provenance.Source.Contains("Vulkan11Properties", StringComparison.Ordinal),
                    $"Vulkan wave provenance must mention SubgroupProperties or Vulkan11Properties: {wave.Provenance.Source}");
                break;
            case ERHIBackend.Metal:
                Assert.Equal(ERHICapabilityProbeKind.BackendContract, wave.Provenance.Kind);
                Assert.Contains("MTLGPUFamily", wave.Provenance.Source, StringComparison.Ordinal);
                Assert.Equal(32, device.Limit?.MinWavefrontSize);
                Assert.Equal(64, device.Limit?.MaxWavefrontSize);
                break;
        }
    }

    private static void AssertDx12WaveOracle(RHIDevice device, RHICapability wave)
    {
#if SHARPGPU_ENABLE_DX12
        Assert.Contains("WaveLaneCountMin", wave.Provenance.Source, StringComparison.Ordinal);
        if (device is not Dx12Device dx12Device)
        {
            Assert.Fail("Windows DX12 device was not a Dx12Device.");
            return;
        }

        FeatureDataD3D12Options1 options1 = default;
        bool queried = dx12Device.NativeDevice.CheckFeatureSupport(
            Feature.Options1,
            ref options1);
        Assert.True(queried, "Independent Options1 oracle query failed.");
        if (!options1.WaveOps)
        {
            Assert.Equal(ERHICapabilityTier.Unavailable, wave.Tier);
            return;
        }

        Assert.NotEqual(ERHICapabilityTier.Unavailable, wave.Tier);
        Assert.Equal((int)options1.WaveLaneCountMin, device.Limit?.MinWavefrontSize);
        Assert.Equal((int)options1.WaveLaneCountMax, device.Limit?.MaxWavefrontSize);
        Assert.True(
            wave.Limits.TryGetValue(ERHICapabilityLimitKind.MinimumWavefrontSize, out ulong minLimit));
        Assert.True(
            wave.Limits.TryGetValue(ERHICapabilityLimitKind.MaximumWavefrontSize, out ulong maxLimit));
        Assert.Equal(options1.WaveLaneCountMin, minLimit);
        Assert.Equal(options1.WaveLaneCountMax, maxLimit);
#else
        Assert.Contains("WaveLaneCountMin", wave.Provenance.Source, StringComparison.Ordinal);
        Assert.Contains("OPTIONS1", wave.Provenance.Source, StringComparison.OrdinalIgnoreCase);
#endif
    }

    private static void AssertCooperativeMatrixQuery(RHIDevice device)
    {
        RHICapability capability = device.Capabilities.Compute.CooperativeMatrix;
        Assert.NotNull(capability.Provenance.Source);
        Assert.Null(
            typeof(RHIMachineLearningCapabilities).GetProperty(
                nameof(RHIComputeCapabilities.CooperativeMatrix)));

        if (capability.Tier == ERHICapabilityTier.Unavailable)
        {
            Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
            Assert.Throws<NotSupportedException>(
                () => device.QueryCooperativeMatrixConfigs(Span<RHICooperativeMatrixConfig>.Empty));
            return;
        }

        Assert.Equal(0, device.QueryCooperativeMatrixConfigs(Span<RHICooperativeMatrixConfig>.Empty));

        RHICooperativeMatrixConfig[] oversized = new RHICooperativeMatrixConfig[16];
        int written = device.QueryCooperativeMatrixConfigs(oversized);
        Assert.True(written >= 1, "Available CooperativeMatrix must enumerate at least one native config.");
        if (written >= 2)
        {
            RHICooperativeMatrixConfig[] tooSmall = new RHICooperativeMatrixConfig[written - 1];
            Assert.Equal(written, device.QueryCooperativeMatrixConfigs(tooSmall));
        }
        for (int i = 0; i < written; ++i)
        {
            Assert.NotEqual(0u, oversized[i].M);
            Assert.NotEqual(0u, oversized[i].N);
            Assert.NotEqual(0u, oversized[i].K);
        }
    }

    private static void AssertDirectComputeCapability(string propertyName)
    {
        PropertyInfo? property = typeof(RHIComputeCapabilities).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.Equal(typeof(RHIComputeCapabilities), property.DeclaringType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
    }
}
