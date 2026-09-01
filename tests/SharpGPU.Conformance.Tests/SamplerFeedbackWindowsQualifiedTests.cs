using System;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;
#if SHARPGPU_ENABLE_DX12
using Vortice.Direct3D12;
#endif

namespace SharpGPU.Conformance.Tests;

public sealed class SamplerFeedbackWindowsQualifiedTests
{
    [Trait("Category", "SharpGpuWindowsQualified")]
    [Fact]
    public void Dx12_SamplerFeedback_CreateRequiresPairingAndSucceedsOnLegal2DTexture()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            FeatureContractContext.TryCreateDx12(
                out FeatureContractContext? context,
                out string reason),
            $"DX12 is required for the Windows-qualified sampler-feedback gate: {reason}");
        Assert.NotNull(context);

        using (context)
        {
            RHICapability capability = context.Device.Capabilities.Raster.SamplerFeedback;
            Assert.Contains(
                "SamplerFeedbackTier",
                capability.Provenance.Source,
                StringComparison.Ordinal);
#if SHARPGPU_ENABLE_DX12
            if (context.Device is Dx12Device dx12Device)
            {
                FeatureDataD3D12Options7 options7 = default;
                Assert.True(
                    dx12Device.NativeDevice.CheckFeatureSupport(
                        Feature.Options7,
                        ref options7),
                    "Independent OPTIONS7 oracle query failed.");
                int nativeTier = (int)options7.SamplerFeedbackTier;
                if (nativeTier == 0)
                {
                    Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
                }
                else if (nativeTier == 90)
                {
                    Assert.Equal(ERHICapabilityTier.Tier1, capability.Tier);
                }
                else if (nativeTier == 100)
                {
                    Assert.Equal(ERHICapabilityTier.Tier2, capability.Tier);
                }
            }
#endif

            if (capability.Tier == ERHICapabilityTier.Unavailable)
            {
                using RHITexture paired = CreateLegalPairedTexture(context.Device);
                NotSupportedException unavailable = Assert.Throws<NotSupportedException>(() =>
                    context.Device.CreateSamplerFeedbackMap(new RHISamplerFeedbackMapDescriptor
                    {
                        PairedTexture = paired,
                        Mode = ERHISamplerFeedbackMode.MinMip,
                    }));
                Assert.Contains("SamplerFeedback", unavailable.Message, StringComparison.Ordinal);
                return;
            }

            ArgumentException missingPair = Assert.Throws<ArgumentException>(() =>
                context.Device.CreateSamplerFeedbackMap(default));
            Assert.Contains("pairing", missingPair.Message, StringComparison.OrdinalIgnoreCase);

            using RHITexture sampled = CreateLegalPairedTexture(context.Device);
            RHITexture feedbackMap;
            try
            {
                feedbackMap = context.Device.CreateSamplerFeedbackMap(
                    new RHISamplerFeedbackMapDescriptor
                    {
                        PairedTexture = sampled,
                        Mode = ERHISamplerFeedbackMode.MinMip,
                        MipRegion = new uint3(4, 4, 1),
                    });
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    "CreateSamplerFeedbackMap with a legal paired 2D texture failed with a native or runtime reason: " +
                    exception);
                return;
            }

            using (feedbackMap)
            {
                Assert.True(feedbackMap.IsSamplerFeedbackMap);
                Assert.Same(sampled, feedbackMap.PairedSamplerFeedbackTexture);
                Assert.Equal(ERHISamplerFeedbackMode.MinMip, feedbackMap.SamplerFeedbackMode);
                Assert.True(
                    RHITexture.IsSamplerFeedbackOpaqueFormat(feedbackMap.Descriptor.Format));
                Assert.False(sampled.IsDisposed);

                using RHITextureView uav = feedbackMap.CreateTextureView(
                    new RHITextureViewDescriptor
                    {
                        MipCount = 1,
                        ArrayCount = 1,
                        ViewType = ERHITextureViewType.UnorderedAccess,
                    });
                Assert.NotNull(uav);
            }

            Assert.False(sampled.IsDisposed);
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
}
