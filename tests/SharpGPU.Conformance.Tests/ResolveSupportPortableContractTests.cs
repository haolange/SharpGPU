using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class ResolveSupportPortableContractTests
    {
        [Fact]
        public void FormatSupportOperation_ShouldExposePairedResolveBitsWithoutLegacyResolve()
        {
            string[] names = Enum.GetNames<ERHIFormatSupportOperation>();
            Assert.DoesNotContain("Resolve", names);
            Assert.Contains(nameof(ERHIFormatSupportOperation.ResolveSource), names);
            Assert.Contains(nameof(ERHIFormatSupportOperation.ResolveDestination), names);
            Assert.Equal(1UL << 7, (ulong)ERHIFormatSupportOperation.ResolveSource);
            Assert.Equal(1UL << 11, (ulong)ERHIFormatSupportOperation.ResolveDestination);
            Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedResolveModeMask));
        }

        [Fact]
        public void ResolveSupportQuery_ShouldRejectNonMsaaSourceSingleSampleDestinationAndUnknownFormat()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateQuery(
                    ERHIPixelFormat.Unknown,
                    ERHISampleCount.Count4,
                    ERHISampleCount.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateQuery(
                    ERHIPixelFormat.Pending,
                    ERHISampleCount.Count4,
                    ERHISampleCount.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHISampleCount.None,
                    ERHISampleCount.None));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CreateQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHISampleCount.Count4,
                    ERHISampleCount.Count4));
        }

        [Fact]
        public void ResolveSupportQuery_ShouldRejectFormatMismatchAndMissingResolveTarget()
        {
            Assert.Throws<ArgumentException>(
                () => new RHIResolveSupportQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHITextureUsage.RenderTarget,
                    ERHITextureDimension.Texture2D,
                    ERHISampleCount.Count4,
                    ERHITextureTiling.Optimal,
                    ERHIPixelFormat.R16G16B16A16_Float,
                    ERHITextureUsage.RenderTarget | ERHITextureUsage.ResolveTarget,
                    ERHITextureDimension.Texture2D,
                    ERHISampleCount.None,
                    ERHITextureTiling.Optimal,
                    ERHITextureAspectMask.Color,
                    ERHIResolveMode.Sample0));
            Assert.Throws<ArgumentException>(
                () => new RHIResolveSupportQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHITextureUsage.RenderTarget,
                    ERHITextureDimension.Texture2D,
                    ERHISampleCount.Count4,
                    ERHITextureTiling.Optimal,
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHITextureUsage.RenderTarget,
                    ERHITextureDimension.Texture2D,
                    ERHISampleCount.None,
                    ERHITextureTiling.Optimal,
                    ERHITextureAspectMask.Color,
                    ERHIResolveMode.Sample0));
        }

        [Fact]
        public void VulkanQueryFormatSupport_ShouldLeaveResolveBitsUnset()
        {
            if (!FeatureContractContext.TryCreateInstance(
                    ERHIBackend.Vulkan,
                    out RHIInstance? instance,
                    out _))
            {
                return;
            }

            using (instance)
            {
                Assert.True(instance.DeviceCount > 0);
                RHIDevice device = instance.GetDevice(0);
                foreach (ERHISampleCount sampleCount in new[]
                         {
                             ERHISampleCount.None,
                             ERHISampleCount.Count4,
                         })
                {
                    RHIFormatSupportQuery formatQuery = new(
                        ERHIPixelFormat.R8G8B8A8_UNorm,
                        ERHITextureUsage.RenderTarget,
                        sampleCount == ERHISampleCount.None
                            ? ERHITextureDimension.Texture2D
                            : ERHITextureDimension.Texture2DMS,
                        sampleCount,
                        ERHITextureTiling.Optimal);
                    RHICapability formatCapability = device.QueryFormatSupport(in formatQuery);
                    if (formatCapability.Tier == ERHICapabilityTier.Unavailable)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(formatCapability.UnavailableReason));
                        continue;
                    }

                    Assert.True(
                        formatCapability.Limits.TryGetValue(
                            ERHICapabilityLimitKind.SupportedFormatOperationMask,
                            out ulong mask));
                    Assert.Equal(
                        0UL,
                        mask & (ulong)ERHIFormatSupportOperation.ResolveSource);
                    Assert.Equal(
                        0UL,
                        mask & (ulong)ERHIFormatSupportOperation.ResolveDestination);
                }

                RHIResolveSupportQuery pair = CreateQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHISampleCount.Count4,
                    ERHISampleCount.None);
                RHICapability resolve = device.QueryResolveSupport(in pair);
                Assert.IsType<RHICapability>(resolve);
                Assert.True(Enum.IsDefined(resolve.Tier));
                if (resolve.Tier != ERHICapabilityTier.Unavailable)
                {
                    Assert.True(
                        resolve.Limits.TryGetValue(
                            ERHICapabilityLimitKind.SupportedResolveModeMask,
                            out ulong modeMask));
                    Assert.NotEqual(0UL, modeMask);
                }
            }
        }

        [Fact]
        public void QueryResolveSupport_ShouldReturnTypedCapabilityForMsaaPairAndUnavailableForUnknownTableFormat()
        {
            RHIResolveSupportQuery positive = CreateQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHISampleCount.Count4,
                ERHISampleCount.None);
            bool probedAny = false;
            foreach (ERHIBackend backend in new[]
                     {
                         ERHIBackend.DirectX12,
                         ERHIBackend.Vulkan,
                         ERHIBackend.Metal,
                     })
            {
                if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
                {
                    continue;
                }

                probedAny = true;
                using (instance)
                {
                    Assert.True(instance.DeviceCount > 0);
                    RHIDevice device = instance.GetDevice(0);
                    RHICapability capability = device.QueryResolveSupport(in positive);
                    Assert.IsType<RHICapability>(capability);
                    Assert.True(Enum.IsDefined(capability.Tier));
                    if (capability.Tier != ERHICapabilityTier.Unavailable)
                    {
                        Assert.True(
                            capability.Limits.TryGetValue(
                                ERHICapabilityLimitKind.SupportedResolveModeMask,
                                out ulong modeMask));
                        Assert.NotEqual(0UL, modeMask);
                    }

                    if (backend == ERHIBackend.Metal)
                    {
                        RHIResolveSupportQuery unknownPair = CreateQuery(
                            ERHIPixelFormat.RGBA_DXT1_UNorm,
                            ERHISampleCount.Count4,
                            ERHISampleCount.None);
                        RHICapability unknown = device.QueryResolveSupport(in unknownPair);
                        Assert.Equal(ERHICapabilityTier.Unavailable, unknown.Tier);
                        Assert.False(string.IsNullOrWhiteSpace(unknown.UnavailableReason));
                    }
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.True(
                    probedAny,
                    "Windows host must create DX12 or Vulkan to probe QueryResolveSupport.");
            }
        }

        [Fact]
        public void VulkanResolveQuery_ShouldMarkResolvedRangeWithoutResettingPool()
        {
            if (!FeatureContractContext.TryCreateInstance(
                    ERHIBackend.Vulkan,
                    out RHIInstance? instance,
                    out _))
            {
                return;
            }

            using (instance)
            {
                Assert.True(instance.DeviceCount > 0);
                RHIDevice device = instance.GetDevice(0);
                if (device.Capabilities.Synchronization.TimestampQueries.Tier ==
                    ERHICapabilityTier.Unavailable)
                {
                    return;
                }

                RHICommandQueue? queue =
                    device.GetCommandQueue(ERHIPipelineType.Graphics, 0) ??
                    device.GetCommandQueue(ERHIPipelineType.Transfer, 0);
                Assert.NotNull(queue);

                using RHIQuery query = device.CreateQuery(new RHIQueryDescriptor
                {
                    Count = 2,
                    Type = ERHIQueryType.Timestamp,
                });
                using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
                commandBuffer.Begin("G2.Vulkan.ResolveQueryRange");
                RHITransferEncoder transfer = commandBuffer.BeginTransferPass(
                    new RHITransferPassDescriptor
                    {
                        Name = "G2.Vulkan.ResolveQueryRange",
                    });
                transfer.ResolveQuery(query, 1, 1);
                commandBuffer.EndTransferPass();
                commandBuffer.End();

                VulkanQuery vulkanQuery = Assert.IsType<VulkanQuery>(query);
                Assert.Equal(1u, vulkanQuery.ResolvedStartIndex);
                Assert.Equal(1u, vulkanQuery.ResolvedQueryCount);
            }
        }

        private static RHIResolveSupportQuery CreateQuery(
            ERHIPixelFormat format,
            ERHISampleCount sourceSamples,
            ERHISampleCount destinationSamples)
        {
            return new RHIResolveSupportQuery(
                format,
                ERHITextureUsage.RenderTarget,
                ERHITextureDimension.Texture2D,
                sourceSamples,
                ERHITextureTiling.Optimal,
                format,
                ERHITextureUsage.RenderTarget | ERHITextureUsage.ResolveTarget,
                ERHITextureDimension.Texture2D,
                destinationSamples,
                ERHITextureTiling.Optimal,
                ERHITextureAspectMask.Color,
                ERHIResolveMode.Sample0);
        }
    }
}
