using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class RayTracingOptionalFacetPortableContractTests
{
    [Fact]
    public void RayTracingCapabilities_ShouldExposeIndependentOptionalFacets()
    {
        AssertDirectRayTracingCapability(nameof(RHIRayTracingCapabilities.Pipeline));
        AssertDirectRayTracingCapability(nameof(RHIRayTracingCapabilities.Inline));
        AssertDirectRayTracingCapability(nameof(RHIRayTracingCapabilities.OpacityMicromap));
        AssertDirectRayTracingCapability(nameof(RHIRayTracingCapabilities.ShaderExecutionReordering));
        AssertDirectRayTracingCapability(nameof(RHIRayTracingCapabilities.Motion));
    }

    [Fact]
    public void PublicApi_ShouldNotExposeHollowOptionalFacetResourceTypes()
    {
        Type[] types = typeof(RHIDevice).Assembly.GetExportedTypes();
        Assert.DoesNotContain(types, static type => type.Name == "RHIOpacityMicromap");
        Assert.DoesNotContain(types, static type => type.Name == "RHIHitObject");
        Assert.DoesNotContain(types, static type => type.Name == "HitObject");
        Assert.DoesNotContain(types, static type => type.Name == "RHIMotionGeometry");
        Assert.DoesNotContain(types, static type => type.Name == "MotionGeometry");
    }

    [Fact]
    public void OptionalFacets_ShouldStayUnavailableOnEveryCreatedDevice()
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
                    AssertOptionalFacetsUnavailable(instance.GetDevice(deviceIndex));
                }
            }
        }
    }

    private static void AssertOptionalFacetsUnavailable(RHIDevice device)
    {
        RHICapability pipeline = device.Capabilities.RayTracing.Pipeline;
        RHICapability opacityMicromap = device.Capabilities.RayTracing.OpacityMicromap;
        RHICapability shaderExecutionReordering =
            device.Capabilities.RayTracing.ShaderExecutionReordering;
        RHICapability motion = device.Capabilities.RayTracing.Motion;

        AssertUnavailableFacet(opacityMicromap, nameof(RHIRayTracingCapabilities.OpacityMicromap));
        AssertUnavailableFacet(
            shaderExecutionReordering,
            nameof(RHIRayTracingCapabilities.ShaderExecutionReordering));
        AssertUnavailableFacet(motion, nameof(RHIRayTracingCapabilities.Motion));

        if (pipeline.Tier != ERHICapabilityTier.Unavailable)
        {
            Assert.Equal(ERHICapabilityTier.Unavailable, opacityMicromap.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, shaderExecutionReordering.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, motion.Tier);
        }
    }

    private static void AssertUnavailableFacet(RHICapability capability, string name)
    {
        Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
        Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
        Assert.DoesNotContain(
            "supported",
            capability.UnavailableReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Tier 1.2",
            capability.UnavailableReason,
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
        Assert.NotEqual(name, capability.UnavailableReason);
    }

    private static void AssertDirectRayTracingCapability(string propertyName)
    {
        PropertyInfo? property = typeof(RHIRayTracingCapabilities).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.Equal(typeof(RHIRayTracingCapabilities), property.DeclaringType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
    }
}
