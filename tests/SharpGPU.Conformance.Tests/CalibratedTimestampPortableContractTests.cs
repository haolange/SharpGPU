using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class CalibratedTimestampPortableContractTests
{
    [Fact]
    public void SynchronizationCapabilities_ShouldExposeCalibratedTimestamps()
    {
        PropertyInfo? property = typeof(RHISynchronizationCapabilities).GetProperty(
            nameof(RHISynchronizationCapabilities.CalibratedTimestamps),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
        Assert.True(Enum.IsDefined(ERHITimeDomain.Device));
        Assert.True(Enum.IsDefined(ERHITimeDomain.QueryPerformanceCounter));
        Assert.True(Enum.IsDefined(ERHITimeDomain.ClockMonotonic));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.GpuTimestampFrequency));
    }

    [Fact]
    public void ClockCalibrationConstructor_ShouldRejectPendingQueueAndZeroFrequency()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIClockCalibration(
                1,
                1,
                1,
                ERHITimeDomain.Pending,
                ERHIPipelineType.Graphics,
                0,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIClockCalibration(
                1,
                1,
                1,
                ERHITimeDomain.Device,
                ERHIPipelineType.Pending,
                0,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIClockCalibration(
                1,
                1,
                1,
                ERHITimeDomain.Device,
                ERHIPipelineType.Graphics,
                -1,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIClockCalibration(
                1,
                1,
                0,
                ERHITimeDomain.Device,
                ERHIPipelineType.Graphics,
                0,
                0));
    }

    [Fact]
    public void QueryClockCalibration_ShouldThrowWhenUnavailableAndReturnNativeResultWhenAvailable()
    {
        bool probedAny = false;
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            bool backendSupported = RHIInstance.IsBackendSupported(backend, out string supportedReason);
            if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
            {
                if (OperatingSystem.IsWindows() &&
                    backend == ERHIBackend.DirectX12 &&
                    backendSupported)
                {
                    Assert.Fail(
                        $"DirectX12 was supported but TryCreateInstance failed: {supportedReason}");
                }

                continue;
            }

            using (instance)
            {
                if (instance.DeviceCount <= 0)
                {
                    continue;
                }

                probedAny = true;
                RHIDevice device = instance.GetDevice(0);
                RHICapability capability =
                    device.Capabilities.Synchronization.CalibratedTimestamps;
                Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
                if (capability.Tier == ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
                    Assert.Throws<NotSupportedException>(
                        () => device.QueryClockCalibration(ERHIPipelineType.Graphics));
                    continue;
                }

                RHIClockCalibration calibration =
                    device.QueryClockCalibration(ERHIPipelineType.Graphics);
                Assert.NotEqual(0UL, calibration.GpuTimestampFrequency);
                Assert.NotEqual(ERHITimeDomain.Pending, calibration.TimeDomain);
                Assert.Equal(ERHIPipelineType.Graphics, calibration.Queue);
                Assert.Equal(0, calibration.QueueIndex);
                Assert.DoesNotContain(
                    "DateTime",
                    capability.Provenance.Source,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(
                    "Stopwatch",
                    capability.Provenance.Source,
                    StringComparison.OrdinalIgnoreCase);

                Assert.True(
                    capability.Limits.TryGetValue(
                        ERHICapabilityLimitKind.GpuTimestampFrequency,
                        out ulong reportedFrequency));
                Assert.Equal(calibration.GpuTimestampFrequency, reportedFrequency);

                if (backend == ERHIBackend.DirectX12)
                {
                    Assert.Contains(
                        "GetClockCalibration",
                        capability.Provenance.Source,
                        StringComparison.Ordinal);
                    Assert.Equal(
                        ERHITimeDomain.QueryPerformanceCounter,
                        calibration.TimeDomain);
                    Assert.False(
                        capability.Limits.TryGetValue(
                            ERHICapabilityLimitKind.CalibratedTimestampMaxDeviation,
                            out _));
                }

                if (backend == ERHIBackend.Vulkan)
                {
                    Assert.True(
                        capability.Limits.TryGetValue(
                            ERHICapabilityLimitKind.CalibratedTimestampMaxDeviation,
                            out _));
                    string source = capability.Provenance.Source;
                    bool usesExt = source.Contains(
                        "VK_EXT_calibrated_timestamps",
                        StringComparison.Ordinal);
                    bool usesKhr = source.Contains(
                        "VK_KHR_calibrated_timestamps",
                        StringComparison.Ordinal);
                    Assert.True(usesExt || usesKhr);
                    if (usesExt)
                    {
                        Assert.Contains(
                            "vkGetCalibratedTimestampsEXT",
                            source,
                            StringComparison.Ordinal);
                        Assert.DoesNotContain(
                            "vkGetCalibratedTimestampsKHR",
                            source,
                            StringComparison.Ordinal);
                    }
                    else
                    {
                        Assert.Contains(
                            "vkGetCalibratedTimestampsKHR",
                            source,
                            StringComparison.Ordinal);
                    }
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.True(
                probedAny,
                "Windows host must create at least one backend to exercise calibrated timestamps.");
        }
    }
}
