using System;
using System.Collections.Generic;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class VariableRateShadingPortableContractTests
{
    [Fact]
    public void RasterCapabilities_ShouldExposeFourDirectVariableRateShadingProperties()
    {
        AssertDirectRasterCapability(nameof(RHIRasterCapabilities.VariableRateShadingPerDraw));
        AssertDirectRasterCapability(nameof(RHIRasterCapabilities.VariableRateShadingPerPrimitive));
        AssertDirectRasterCapability(nameof(RHIRasterCapabilities.VariableRateShadingAttachment));
        AssertDirectRasterCapability(nameof(RHIRasterCapabilities.VariableRateShadingCombiners));
    }

    [Fact]
    public void RasterCapabilities_ShouldNotExposeRetiredVariableRateShadingProperty()
    {
        Assert.Null(
            typeof(RHIRasterCapabilities).GetProperty(
                "VariableRateShading",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact]
    public void CapabilityLimitKind_ShouldDefineVariableRateShadingLimitKinds()
    {
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMin));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMin));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMax));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMax));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedShadingRateMask));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedShadingRateCombinerMask));
    }

    [Fact]
    public void ShadingRateEnums_ShouldUseNonOverlappingMaskSafeEncodings()
    {
        AssertMaskSafeUniqueEnumValues<ERHIShadingRate>(ERHIShadingRate.Pending);
        AssertMaskSafeUniqueEnumValues<ERHIShadingRateCombiner>(ERHIShadingRateCombiner.Pending);
    }

    [Fact]
    public void CapabilityFactories_ShouldPublishFailClosedAvailableAndUnavailableState()
    {
        RHICapability available = RHICapability.Available(
            ERHICapabilityTier.Tier1,
            ERHICapabilityStrategy.NativeExtension,
            ERHICapabilityProbeKind.NativeExtensionQuery,
            "variable-rate shading portable factory");
        Assert.NotEqual(ERHICapabilityTier.Unavailable, available.Tier);
        Assert.NotEqual(ERHICapabilityStrategy.Unavailable, available.Strategy);
        Assert.True(string.IsNullOrEmpty(available.UnavailableReason));

        RHICapability unavailable = RHICapability.Unavailable(
            "variable-rate shading portable factory unavailable",
            ERHICapabilityProbeKind.BackendContract,
            "variable-rate shading portable factory");
        Assert.Equal(ERHICapabilityTier.Unavailable, unavailable.Tier);
        Assert.Equal(ERHICapabilityStrategy.Unavailable, unavailable.Strategy);
        Assert.False(string.IsNullOrWhiteSpace(unavailable.UnavailableReason));
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

    private static void AssertMaskSafeUniqueEnumValues<TEnum>(TEnum pending)
        where TEnum : struct, Enum
    {
        HashSet<byte> encodings = new();
        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            if (EqualityComparer<TEnum>.Default.Equals(value, pending))
            {
                continue;
            }

            byte encoding = Convert.ToByte(value);
            Assert.True(
                encoding < 64,
                $"{typeof(TEnum).Name}.{value} encoding {encoding} must stay below 64 so 1UL << encoding does not overflow.");
            Assert.True(
                encodings.Add(encoding),
                $"{typeof(TEnum).Name} encoding {encoding} is reused by {value}.");
        }
    }
}
