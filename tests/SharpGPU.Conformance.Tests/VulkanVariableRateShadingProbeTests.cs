using System;
using System.Collections.Generic;
using SharpGPU;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class VulkanVariableRateShadingProbeTests
{
    [Fact]
    public void MissingExtension_UsesExtensionPresenceQueryForAllFourCapabilities()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                new VulkanVariableRateShadingProbeState(
                    extensionPresent: false,
                    perDrawSupported: true,
                    perPrimitiveSupported: true,
                    attachmentHardwareSupported: true,
                    nonTrivialCombinerOps: true,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: 1UL,
                    minAttachmentTileWidth: 8,
                    minAttachmentTileHeight: 8,
                    maxAttachmentTileWidth: 16,
                    maxAttachmentTileHeight: 16));

        AssertExtensionPresenceUnavailable(capabilities.PerDraw);
        AssertExtensionPresenceUnavailable(capabilities.PerPrimitive);
        AssertExtensionPresenceUnavailable(capabilities.Attachment);
        AssertExtensionPresenceUnavailable(capabilities.Combiners);
    }

    [Fact]
    public void PresentExtension_UsesFeatureFieldSourceWhenEnumerationIsComplete()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: false,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: true));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.PerDraw.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.PipelineFragmentShadingRateSource,
            capabilities.PerDraw.Provenance.Source);
        Assert.Equal(
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.pipelineFragmentShadingRate is false.",
            capabilities.PerDraw.UnavailableReason);

        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.PrimitiveFragmentShadingRateSource,
            capabilities.PerPrimitive.Provenance.Source);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.AttachmentFragmentShadingRateSource,
            capabilities.Attachment.Provenance.Source);
    }

    [Fact]
    public void UnstableRateEnumeration_FailsClosedPerDrawWithoutFabricatingMask()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: true,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: false,
                    supportedShadingRateMask: 1UL << (byte)ERHIShadingRate.Rate2x2));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.PerDraw.Provenance.Kind);
        Assert.Equal(
            VulkanFragmentShadingRateEnumeration.FragmentShadingRatesQuerySource,
            capabilities.PerDraw.Provenance.Source);
        Assert.Equal(
            VulkanFragmentShadingRateEnumeration.UnstableCompleteRateListReason,
            capabilities.PerDraw.UnavailableReason);
        Assert.False(
            capabilities.PerDraw.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateMask,
                out _));
        Assert.NotEqual(ERHICapabilityTier.Unavailable, capabilities.PerPrimitive.Tier);
    }

    [Fact]
    public void FeatureBitFalse_ReportsFeatureSourceEvenWhenEnumerationIsUnstable()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: false,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: false,
                    supportedShadingRateMask: 0UL));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.PerDraw.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.PipelineFragmentShadingRateSource,
            capabilities.PerDraw.Provenance.Source);
        Assert.Equal(
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.pipelineFragmentShadingRate is false.",
            capabilities.PerDraw.UnavailableReason);
        Assert.False(
            capabilities.PerDraw.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateMask,
                out _));
    }

    [Fact]
    public void CompleteEmptyMask_FailsClosedPerDrawWithoutRequired1x1()
    {
        AssertPerDrawMissingRequired1x1(supportedShadingRateMask: 0UL);
    }

    [Fact]
    public void CompleteUnknownOnlyMask_FailsClosedPerDrawWithoutRequired1x1()
    {
        ulong unknownOnlyMask =
            VulkanFragmentShadingRateEnumeration.BuildSupportedShadingRateMask(
                new VkExtent2D[]
                {
                    new() { width = 3, height = 3 },
                    new() { width = 5, height = 1 },
                });
        Assert.Equal(0UL, unknownOnlyMask);
        Assert.Equal(
            0UL,
            unknownOnlyMask & (1UL << (byte)ERHIShadingRate.Pending));
        AssertPerDrawMissingRequired1x1(unknownOnlyMask);
    }

    [Fact]
    public void CompleteMaskMissing1x1_FailsClosedPerDraw()
    {
        AssertPerDrawMissingRequired1x1(
            1UL << (byte)ERHIShadingRate.Rate2x2);
    }

    [Fact]
    public void CompleteRateEnumeration_PublishesExactMappedMaskWithoutPending()
    {
        ulong expectedMask =
            (1UL << (byte)ERHIShadingRate.Rate1x1) |
            (1UL << (byte)ERHIShadingRate.Rate2x2);
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: expectedMask));

        Assert.NotEqual(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.True(
            capabilities.PerDraw.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateMask,
                out ulong reportedMask));
        Assert.Equal(expectedMask, reportedMask);
        Assert.Equal(0UL, reportedMask & (1UL << (byte)ERHIShadingRate.Pending));
    }

    [Fact]
    public void RateCount_RejectsOverflowAndOversizedLists()
    {
        Assert.True(
            VulkanFragmentShadingRateEnumeration.TryGetSafeCount(0, out int zeroCount));
        Assert.Equal(0, zeroCount);
        Assert.True(
            VulkanFragmentShadingRateEnumeration.TryGetSafeCount(7, out int mappedCount));
        Assert.Equal(7, mappedCount);

        Assert.False(
            VulkanFragmentShadingRateEnumeration.TryGetSafeCount(
                (uint)int.MaxValue + 1u,
                out int overflowCount));
        Assert.Equal(0, overflowCount);
        Assert.False(
            VulkanFragmentShadingRateEnumeration.TryGetSafeCount(
                (uint)VulkanFragmentShadingRateEnumeration.MaxEnumeratedRateCount + 1u,
                out int oversizedCount));
        Assert.Equal(0, oversizedCount);
    }

    [Fact]
    public void RateFetchResult_TreatsIncompleteOrGrownCountAsUnstable()
    {
        Assert.True(
            VulkanFragmentShadingRateEnumeration.IsStableCompleteResult(
                VkResult.Success,
                3,
                4));
        Assert.False(
            VulkanFragmentShadingRateEnumeration.IsStableCompleteResult(
                VkResult.Incomplete,
                4,
                4));
        Assert.False(
            VulkanFragmentShadingRateEnumeration.IsStableCompleteResult(
                VkResult.Success,
                5,
                4));
        Assert.True(
            VulkanFragmentShadingRateEnumeration.IsIncompleteOrTruncated(
                VkResult.Incomplete,
                4,
                4));
        Assert.True(
            VulkanFragmentShadingRateEnumeration.IsIncompleteOrTruncated(
                VkResult.Success,
                5,
                4));
        Assert.False(
            VulkanFragmentShadingRateEnumeration.IsIncompleteOrTruncated(
                VkResult.ErrorOutOfHostMemory,
                4,
                4));
    }

    [Fact]
    public void RateEnumeration_IncompleteFetch_RequeriesCountAndRetriesAtLargerCapacity()
    {
        ScriptedFragmentShadingRateQuery query = new(
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 2,
                result: VkResult.Success),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: false,
                count: 1,
                result: VkResult.Incomplete,
                fragmentSizes: new[] { new VkExtent2D { width = 1, height = 1 } }),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 4,
                result: VkResult.Success),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: false,
                count: 4,
                result: VkResult.Success,
                fragmentSizes: new[]
                {
                    new VkExtent2D { width = 1, height = 1 },
                    new VkExtent2D { width = 2, height = 1 },
                    new VkExtent2D { width = 2, height = 2 },
                    new VkExtent2D { width = 4, height = 4 },
                }));

        VulkanFragmentShadingRateMaskQuery maskQuery =
            VulkanFragmentShadingRateEnumeration.QuerySupportedMask(true, query);

        Assert.Equal(
            new[] { 0, 2, 0, 4 },
            query.ObservedCapacities);
        Assert.Equal(
            new[] { true, false, true, false },
            query.ObservedCountOnly);
        Assert.True(maskQuery.IsComplete);
        Assert.Equal(
            (1UL << (byte)ERHIShadingRate.Rate1x1) |
            (1UL << (byte)ERHIShadingRate.Rate2x1) |
            (1UL << (byte)ERHIShadingRate.Rate2x2) |
            (1UL << (byte)ERHIShadingRate.Rate4x4),
            maskQuery.SupportedMask);
        AssertPerDrawAvailableFromEnumeration(maskQuery);
    }

    [Fact]
    public void RateEnumeration_PersistentIncomplete_IsUnreliableAndFailsClosedPerDraw()
    {
        ScriptedFragmentShadingRateQuery query = new(
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 3,
                result: VkResult.Success),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: false,
                count: 1,
                result: VkResult.Incomplete,
                fragmentSizes: new[] { new VkExtent2D { width = 1, height = 1 } }),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 3,
                result: VkResult.Success),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: false,
                count: 2,
                result: VkResult.Incomplete,
                fragmentSizes: new[]
                {
                    new VkExtent2D { width = 1, height = 1 },
                    new VkExtent2D { width = 2, height = 2 },
                }));

        VulkanFragmentShadingRateMaskQuery maskQuery =
            VulkanFragmentShadingRateEnumeration.QuerySupportedMask(true, query);

        Assert.Equal(4, query.ObservedCountOnly.Count);
        Assert.False(maskQuery.IsComplete);
        Assert.Equal(0UL, maskQuery.SupportedMask);
        AssertPerDrawUnavailableFromUnreliableEnumeration(maskQuery);
    }

    [Fact]
    public void RateEnumeration_RecountZeroAfterIncomplete_IsUnreliableAndFailsClosedPerDraw()
    {
        ScriptedFragmentShadingRateQuery query = new(
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 2,
                result: VkResult.Success),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: false,
                count: 1,
                result: VkResult.Incomplete,
                fragmentSizes: new[] { new VkExtent2D { width = 1, height = 1 } }),
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: 0,
                result: VkResult.Success));

        VulkanFragmentShadingRateMaskQuery maskQuery =
            VulkanFragmentShadingRateEnumeration.QuerySupportedMask(true, query);

        Assert.Equal(new[] { true, false, true }, query.ObservedCountOnly);
        Assert.False(maskQuery.IsComplete);
        AssertPerDrawUnavailableFromUnreliableEnumeration(maskQuery);
    }

    [Fact]
    public void RateEnumeration_CountOverSafeLimit_IsUnreliableAndFailsClosedPerDraw()
    {
        ScriptedFragmentShadingRateQuery query = new(
            new ScriptedFragmentShadingRateResponse(
                isCountOnly: true,
                count: (uint)VulkanFragmentShadingRateEnumeration.MaxEnumeratedRateCount + 1u,
                result: VkResult.Success));

        VulkanFragmentShadingRateMaskQuery maskQuery =
            VulkanFragmentShadingRateEnumeration.QuerySupportedMask(true, query);

        Assert.Equal(new[] { true }, query.ObservedCountOnly);
        Assert.Empty(query.ObservedFetchCapacities);
        Assert.False(maskQuery.IsComplete);
        AssertPerDrawUnavailableFromUnreliableEnumeration(maskQuery);
    }

    [Fact]
    public void Combiners_PrimitiveFeatureTrue_UsesNativeFeatureSourceAndIsAvailable()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: true,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: 1UL << (byte)ERHIShadingRate.Rate1x1));

        Assert.NotEqual(ERHICapabilityTier.Unavailable, capabilities.Combiners.Tier);
        Assert.Equal(ERHICapabilityStrategy.NativeExtension, capabilities.Combiners.Strategy);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.Combiners.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.PrimitiveFragmentShadingRateSource,
            capabilities.Combiners.Provenance.Source);
        AssertCombinersAvailableImpliesExecutableSecondSource(capabilities);
        AssertSetShadingRateAllowsNonPassthrough(capabilities);
    }

    [Fact]
    public void Combiners_BothFeaturesFalse_UsesNativeFeatureQueryProvenance()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: 1UL << (byte)ERHIShadingRate.Rate1x1));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.Combiners.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.Combiners.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.CombinableFeatureFieldsSource,
            capabilities.Combiners.Provenance.Source);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.CombinersBothFeaturesUnsupportedReason,
            capabilities.Combiners.UnavailableReason);
        AssertCombinersAvailableImpliesExecutableSecondSource(capabilities);
        AssertSetShadingRateAllowsOnlyPassthroughKeep(capabilities);
    }

    [Fact]
    public void Combiners_AttachmentHardwareWithoutLowering_UsesBackendContractProvenance()
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: true,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: 1UL << (byte)ERHIShadingRate.Rate1x1));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.Combiners.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.BackendContract,
            capabilities.Combiners.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.RasterLoweringSource,
            capabilities.Combiners.Provenance.Source);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.CombinersAttachmentLoweringUnboundReason,
            capabilities.Combiners.UnavailableReason);
        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.Attachment.Tier);
        AssertCombinersAvailableImpliesExecutableSecondSource(capabilities);
        AssertSetShadingRateAllowsOnlyPassthroughKeep(capabilities);
    }

    [Fact]
    public void SupportedMask_MapsExactFragmentSizesAndLeavesPendingUnset()
    {
        VkExtent2D[] sizes =
        {
            new() { width = 1, height = 1 },
            new() { width = 2, height = 2 },
            new() { width = 3, height = 3 },
        };

        ulong mask = VulkanFragmentShadingRateEnumeration.BuildSupportedShadingRateMask(sizes);
        Assert.Equal(
            (1UL << (byte)ERHIShadingRate.Rate1x1) |
            (1UL << (byte)ERHIShadingRate.Rate2x2),
            mask);
        Assert.Equal(0UL, mask & (1UL << (byte)ERHIShadingRate.Pending));
    }

    private static VulkanVariableRateShadingProbeState CreatePresentExtensionState(
        bool perDrawSupported,
        bool perPrimitiveSupported,
        bool attachmentHardwareSupported,
        bool rateEnumerationComplete,
        ulong supportedShadingRateMask = 1UL)
    {
        return new VulkanVariableRateShadingProbeState(
            extensionPresent: true,
            perDrawSupported,
            perPrimitiveSupported,
            attachmentHardwareSupported,
            nonTrivialCombinerOps: false,
            rateEnumerationComplete,
            supportedShadingRateMask,
            minAttachmentTileWidth: 8,
            minAttachmentTileHeight: 8,
            maxAttachmentTileWidth: 32,
            maxAttachmentTileHeight: 32);
    }

    private static void AssertPerDrawMissingRequired1x1(ulong supportedShadingRateMask)
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: true,
                    supportedShadingRateMask: supportedShadingRateMask));

        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capabilities.PerDraw.Provenance.Kind);
        Assert.Equal(
            VulkanFragmentShadingRateEnumeration.FragmentShadingRatesQuerySource,
            capabilities.PerDraw.Provenance.Source);
        Assert.Equal(
            VulkanFragmentShadingRateEnumeration.MissingRequired1x1RateReason,
            capabilities.PerDraw.UnavailableReason);
        Assert.False(
            VulkanFragmentShadingRateEnumeration.ContainsRequired1x1Rate(
                supportedShadingRateMask));
        Assert.False(
            capabilities.PerDraw.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateMask,
                out _));
    }

    private static void AssertExtensionPresenceUnavailable(in RHICapability capability)
    {
        Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
        Assert.Equal(ERHICapabilityStrategy.Unavailable, capability.Strategy);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            capability.Provenance.Kind);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.ExtensionPresenceQuerySource,
            capability.Provenance.Source);
        Assert.Equal(
            VulkanVariableRateShadingCapabilityFactory.ExtensionUnsupportedReason,
            capability.UnavailableReason);
    }

    private static void AssertPerDrawAvailableFromEnumeration(
        in VulkanFragmentShadingRateMaskQuery maskQuery)
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: maskQuery.IsComplete,
                    supportedShadingRateMask: maskQuery.SupportedMask));
        Assert.NotEqual(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
    }

    private static void AssertPerDrawUnavailableFromUnreliableEnumeration(
        in VulkanFragmentShadingRateMaskQuery maskQuery)
    {
        VulkanVariableRateShadingCapabilities capabilities =
            VulkanVariableRateShadingCapabilityFactory.Create(
                CreatePresentExtensionState(
                    perDrawSupported: true,
                    perPrimitiveSupported: false,
                    attachmentHardwareSupported: false,
                    rateEnumerationComplete: maskQuery.IsComplete,
                    supportedShadingRateMask: maskQuery.SupportedMask));
        Assert.Equal(ERHICapabilityTier.Unavailable, capabilities.PerDraw.Tier);
        Assert.Throws<NotSupportedException>(
            () => VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
                capabilities.PerDraw,
                capabilities.Combiners,
                ERHIShadingRate.Rate1x1,
                ERHIShadingRateCombiner.Passthrough));
    }

    private static void AssertCombinersAvailableImpliesExecutableSecondSource(
        in VulkanVariableRateShadingCapabilities capabilities)
    {
        if (capabilities.Combiners.Tier == ERHICapabilityTier.Unavailable)
        {
            return;
        }

        Assert.True(
            capabilities.PerPrimitive.Tier != ERHICapabilityTier.Unavailable ||
            capabilities.Attachment.Tier != ERHICapabilityTier.Unavailable);
    }

    private static void AssertSetShadingRateAllowsOnlyPassthroughKeep(
        in VulkanVariableRateShadingCapabilities capabilities)
    {
        RHICapability perDraw = capabilities.PerDraw;
        RHICapability combiners = capabilities.Combiners;
        VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
            perDraw,
            combiners,
            ERHIShadingRate.Rate1x1,
            ERHIShadingRateCombiner.Passthrough);
        Assert.Throws<NotSupportedException>(
            () => VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
                perDraw,
                combiners,
                ERHIShadingRate.Rate1x1,
                ERHIShadingRateCombiner.Override));
        VulkanVariableRateShadingCommandPolicy.ResolveCombinerOps(
            capabilities.PerPrimitive,
            capabilities.Attachment,
            ERHIShadingRateCombiner.Override,
            out VkFragmentShadingRateCombinerOpKHR pipelineWithPrimitive,
            out VkFragmentShadingRateCombinerOpKHR resultWithAttachment);
        Assert.Equal(VkFragmentShadingRateCombinerOpKHR.Keep, pipelineWithPrimitive);
        Assert.Equal(VkFragmentShadingRateCombinerOpKHR.Keep, resultWithAttachment);
    }

    private static void AssertSetShadingRateAllowsNonPassthrough(
        in VulkanVariableRateShadingCapabilities capabilities)
    {
        VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
            capabilities.PerDraw,
            capabilities.Combiners,
            ERHIShadingRate.Rate1x1,
            ERHIShadingRateCombiner.Override);
        VulkanVariableRateShadingCommandPolicy.ResolveCombinerOps(
            capabilities.PerPrimitive,
            capabilities.Attachment,
            ERHIShadingRateCombiner.Override,
            out VkFragmentShadingRateCombinerOpKHR pipelineWithPrimitive,
            out VkFragmentShadingRateCombinerOpKHR resultWithAttachment);
        Assert.Equal(VkFragmentShadingRateCombinerOpKHR.Replace, pipelineWithPrimitive);
        Assert.Equal(VkFragmentShadingRateCombinerOpKHR.Keep, resultWithAttachment);
    }

    private readonly struct ScriptedFragmentShadingRateResponse
    {
        public bool IsCountOnly { get; }
        public uint Count { get; }
        public VkResult Result { get; }
        public VkExtent2D[] FragmentSizes { get; }

        public ScriptedFragmentShadingRateResponse(
            bool isCountOnly,
            uint count,
            VkResult result,
            VkExtent2D[]? fragmentSizes = null)
        {
            IsCountOnly = isCountOnly;
            Count = count;
            Result = result;
            FragmentSizes = fragmentSizes ?? Array.Empty<VkExtent2D>();
        }
    }

    private sealed class ScriptedFragmentShadingRateQuery : IVulkanFragmentShadingRateQuery
    {
        private readonly ScriptedFragmentShadingRateResponse[] m_Responses;
        private int m_Index;

        public ScriptedFragmentShadingRateQuery(
            params ScriptedFragmentShadingRateResponse[] responses)
        {
            m_Responses = responses;
        }

        public List<bool> ObservedCountOnly { get; } = new();
        public List<int> ObservedCapacities { get; } = new();
        public List<int> ObservedFetchCapacities { get; } = new();

        public VkResult QueryCount(out uint count)
        {
            ScriptedFragmentShadingRateResponse response = Next(countOnly: true, capacity: 0);
            count = response.Count;
            return response.Result;
        }

        public VkResult QueryFetch(
            Span<VkPhysicalDeviceFragmentShadingRateKHR> destination,
            out uint writtenCount)
        {
            ScriptedFragmentShadingRateResponse response =
                Next(countOnly: false, capacity: destination.Length);
            int writeCount = Math.Min(destination.Length, response.FragmentSizes.Length);
            for (int i = 0; i < writeCount; ++i)
            {
                destination[i].fragmentSize = response.FragmentSizes[i];
            }

            writtenCount = response.Count;
            return response.Result;
        }

        private ScriptedFragmentShadingRateResponse Next(bool countOnly, int capacity)
        {
            Assert.True(m_Index < m_Responses.Length, "Unexpected extra fragment shading rate query.");
            ScriptedFragmentShadingRateResponse response = m_Responses[m_Index++];
            Assert.Equal(countOnly, response.IsCountOnly);
            ObservedCountOnly.Add(countOnly);
            ObservedCapacities.Add(capacity);
            if (!countOnly)
            {
                ObservedFetchCapacities.Add(capacity);
            }

            return response;
        }
    }
}
