using System;
using System.Linq;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class PipelineStatisticsPortableContractTests
    {
        [Fact]
        public void StatisticsPublicApi_ShouldBeTypedWithoutStrideOrUlongCanonicalPath()
        {
            Assert.NotNull(typeof(ERHIPipelineStatisticCounter).GetCustomAttribute<FlagsAttribute>());
            Assert.Equal(0UL, (ulong)ERHIPipelineStatisticCounter.None);
            Assert.Contains(
                nameof(RHIQuery.TryGetRasterStatistics),
                typeof(RHIQuery).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => method.Name));
            Assert.Contains(
                nameof(RHIQuery.TryGetComputeStatistics),
                typeof(RHIQuery).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => method.Name));
            Assert.Contains(
                nameof(RHIQuery.TryGetRayTracingStatistics),
                typeof(RHIQuery).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(method => method.Name));

            foreach (string forbidden in new[]
                     {
                         "ResultStride",
                         "StatisticsStride",
                         "ResultStrideInBytes",
                         "PipelineStatisticsStride",
                     })
            {
                Assert.Null(typeof(RHIQuery).GetProperty(forbidden, BindingFlags.Public | BindingFlags.Instance));
                Assert.Null(typeof(RHIQuery).GetField(forbidden, BindingFlags.Public | BindingFlags.Instance));
                Assert.Null(typeof(RHIQueryDescriptor).GetProperty(forbidden, BindingFlags.Public | BindingFlags.Instance));
                Assert.Null(typeof(RHIQueryDescriptor).GetField(forbidden, BindingFlags.Public | BindingFlags.Instance));
            }

            Assert.Equal(
                ERHIPipelineStatisticCounter.None,
                RHIDevice.LegalCountersForDomain(ERHIPipelineStatisticsDomain.RayTracing));
        }

        [Fact]
        public void CreateQuery_Statistics_ShouldRejectEmptyMaskDomainMismatchAndMetalRay()
        {
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
                    RHIDevice device = instance.GetDevice(0);
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateQuery(new RHIQueryDescriptor
                        {
                            Count = 1,
                            Type = ERHIQueryType.Statistics,
                            Domain = ERHIPipelineStatisticsDomain.Raster,
                            CounterMask = ERHIPipelineStatisticCounter.None,
                        }));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateQuery(new RHIQueryDescriptor
                        {
                            Count = 1,
                            Type = ERHIQueryType.Statistics,
                            Domain = ERHIPipelineStatisticsDomain.Raster,
                            CounterMask = ERHIPipelineStatisticCounter.ComputeShaderInvocations,
                        }));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateQuery(new RHIQueryDescriptor
                        {
                            Count = 1,
                            Type = ERHIQueryType.Statistics,
                            Domain = ERHIPipelineStatisticsDomain.Compute,
                            CounterMask = ERHIPipelineStatisticCounter.PixelShaderInvocations,
                        }));
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.CreateQuery(new RHIQueryDescriptor
                        {
                            Count = 1,
                            Type = ERHIQueryType.Statistics,
                            Domain = ERHIPipelineStatisticsDomain.RayTracing,
                            CounterMask = ERHIPipelineStatisticCounter.PixelShaderInvocations,
                        }));

                    if (backend == ERHIBackend.Metal)
                    {
                        Assert.False(
                            device.Capabilities.Synchronization.PipelineStatisticsQueries.Limits.TryGetValue(
                                ERHICapabilityLimitKind.SupportedPipelineStatisticsDomainMask,
                                out ulong domainMask) &&
                            (domainMask & (1UL << (byte)ERHIPipelineStatisticsDomain.RayTracing)) != 0);
                        Assert.ThrowsAny<Exception>(
                            () => device.CreateQuery(new RHIQueryDescriptor
                            {
                                Count = 1,
                                Type = ERHIQueryType.Statistics,
                                Domain = ERHIPipelineStatisticsDomain.RayTracing,
                                CounterMask = ERHIPipelineStatisticCounter.None,
                            }));
                    }

                    if (device.Capabilities.Synchronization.PipelineStatisticsQueries.Tier ==
                        ERHICapabilityTier.Unavailable)
                    {
                        continue;
                    }

                    using RHIQuery raster = device.CreateQuery(new RHIQueryDescriptor
                    {
                        Count = 1,
                        Type = ERHIQueryType.Statistics,
                        Domain = ERHIPipelineStatisticsDomain.Raster,
                        CounterMask = ERHIPipelineStatisticCounter.PixelShaderInvocations,
                    });
                    Assert.Throws<InvalidOperationException>(() => _ = raster.Results);
                    Assert.Throws<InvalidOperationException>(
                        () => raster.TryGetComputeStatistics(0, out _));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.True(
                    probedAny,
                    "Windows host must create DX12 or Vulkan to reject illegal statistics queries.");
            }
        }

        [Fact]
        public void BeginEndStatistics_ShouldThrowWhenPassDidNotDeclareQueryOrDomainMismatches()
        {
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
                    RHIDevice device = instance.GetDevice(0);
                    RHICommandQueue? queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0);
                    if (queue == null)
                    {
                        continue;
                    }

                    using RHICommandBuffer undeclared = queue.CreateCommandBuffer();
                    undeclared.Begin("G2.Statistics.Undeclared");
                    RHIComputeEncoder compute = undeclared.BeginComputePass(
                        new RHIComputePassDescriptor
                        {
                            Name = "G2.Statistics.Undeclared",
                        });
                    Assert.Throws<InvalidOperationException>(() => compute.BeginStatistics(0));
                    Assert.Throws<InvalidOperationException>(() => compute.EndStatistics(0));
                    undeclared.EndComputePass();
                    undeclared.End();

                    if (device.Capabilities.Synchronization.PipelineStatisticsQueries.Tier ==
                        ERHICapabilityTier.Unavailable)
                    {
                        continue;
                    }

                    if (!device.Capabilities.Synchronization.PipelineStatisticsQueries.Limits.TryGetValue(
                            ERHICapabilityLimitKind.RasterPipelineStatisticCounterMask,
                            out ulong rasterMask) ||
                        (rasterMask & (ulong)ERHIPipelineStatisticCounter.PixelShaderInvocations) == 0)
                    {
                        continue;
                    }

                    using RHIQuery rasterQuery = device.CreateQuery(new RHIQueryDescriptor
                    {
                        Count = 1,
                        Type = ERHIQueryType.Statistics,
                        Domain = ERHIPipelineStatisticsDomain.Raster,
                        CounterMask = ERHIPipelineStatisticCounter.PixelShaderInvocations,
                    });
                    using RHICommandBuffer mismatched = queue.CreateCommandBuffer();
                    mismatched.Begin("G2.Statistics.DomainMismatch");
                    RHIComputeEncoder mismatchedCompute = mismatched.BeginComputePass(
                        new RHIComputePassDescriptor
                        {
                            Name = "G2.Statistics.DomainMismatch",
                            Statistics = new RHIStatisticsDescriptor
                            {
                                Query = rasterQuery,
                                WriteIndex = 0,
                            },
                        });
                    Assert.Throws<InvalidOperationException>(() => mismatchedCompute.BeginStatistics(0));
                    mismatched.EndComputePass();
                    mismatched.End();
                }
            }

            if (OperatingSystem.IsWindows())
            {
                Assert.True(
                    probedAny,
                    "Windows host must create DX12 or Vulkan to reject undeclared statistics encodes.");
            }
        }
    }
}
