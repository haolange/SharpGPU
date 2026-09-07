using System;
using System.Reflection;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class SamplerFeedbackPortableContractTests
{
    [Fact]
    public void RasterCapabilities_ShouldExposeSamplerFeedbackAndRejectRetiredSampledFeedback()
    {
        AssertDirectRasterCapability(nameof(RHIRasterCapabilities.SamplerFeedback));
        Assert.Null(
            typeof(RHIRasterCapabilities).GetProperty(
                "SampledFeedback",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedSamplerFeedbackModeMask));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedSamplerFeedbackOperationMask));
        Assert.NotNull(
            typeof(RHIDevice).GetMethod(
                nameof(RHIDevice.CreateSamplerFeedbackMap),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIComputeEncoder).GetMethod(
                nameof(RHIComputeEncoder.ClearSamplerFeedbackMap),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIComputeEncoder).GetMethod(
                nameof(RHIComputeEncoder.ResolveSamplerFeedbackMap),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIComputeEncoder).GetMethod(
                nameof(RHIComputeEncoder.DecodeSamplerFeedbackMap),
                BindingFlags.Public | BindingFlags.Instance));
        Assert.NotNull(
            typeof(RHIComputeEncoder).GetMethod(
                nameof(RHIComputeEncoder.CopySamplerFeedbackMap),
                BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact]
    public void SamplerFeedback_ShouldStayUnavailableOnVulkanAndMetalAndRequirePairing()
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
                RHICapability capability = device.Capabilities.Raster.SamplerFeedback;
                Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
                Assert.DoesNotContain(
                    "FramebufferLocalRead",
                    capability.Provenance.Source,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "attachment_feedback_loop",
                    capability.Provenance.Source,
                    StringComparison.OrdinalIgnoreCase);

                if (backend is ERHIBackend.Vulkan or ERHIBackend.Metal)
                {
                    Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
                    Assert.Contains(
                        "DX12-only",
                        capability.UnavailableReason,
                        StringComparison.Ordinal);
                    using RHITexture paired = CreateLegalPairedTexture(device);
                    Assert.Throws<NotSupportedException>(() =>
                        device.CreateSamplerFeedbackMap(new RHISamplerFeedbackMapDescriptor
                        {
                            PairedTexture = paired,
                            Mode = ERHISamplerFeedbackMode.MinMip,
                        }));
                    continue;
                }

                Assert.Contains(
                    "SamplerFeedbackTier",
                    capability.Provenance.Source,
                    StringComparison.Ordinal);
                if (capability.Tier == ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
                    continue;
                }

                ArgumentException missingPair = Assert.Throws<ArgumentException>(() =>
                    device.CreateSamplerFeedbackMap(default));
                Assert.Contains("pairing", missingPair.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static RHITexture CreateLegalPairedTexture(RHIDevice device)
    {
        return device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(64, 64, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = ERHITextureUsage.ShaderResource,
            Dimension = ERHITextureDimension.Texture2D,
        });
    }

    private static void AssertDirectRasterCapability(string propertyName)
    {
        PropertyInfo? property = typeof(RHIRasterCapabilities).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.Equal(typeof(RHIRasterCapabilities), property.DeclaringType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
    }
}
