using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class OpacityMicromapPortableContractTests
    {
        [Fact]
        public void OpacityMicromap_ObjectModelSymbolsExist()
        {
            Assert.NotNull(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "OpacityMicromap"));
            Assert.NotNull(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "OpacityMicromapSerialization"));
            Assert.NotNull(GetPublicInstanceField(typeof(RHIAccelStructTriangles), "OpacityMicromap"));
            Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.GetOpacityMicromapMemoryRequirements)));
            Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreateOpacityMicromap)));
            Assert.NotNull(typeof(RHIRaytracingEncoder).GetMethod(nameof(RHIRaytracingEncoder.BuildOpacityMicromap)));
            Assert.NotNull(typeof(RHIRaytracingEncoder).GetMethod(nameof(RHIRaytracingEncoder.CompactOpacityMicromap)));

            Type[] exported = typeof(RHIDevice).Assembly.GetExportedTypes();
            Assert.Contains(exported, static type => type.Name == nameof(RHIOpacityMicromap));
            Assert.Contains(exported, static type => type.Name == nameof(RHIOpacityMicromapBuildDescriptor));
            Assert.Contains(exported, static type => type.Name == nameof(RHIOpacityMicromapMemoryRequirements));
            Assert.Contains(exported, static type => type.Name == nameof(RHIOpacityMicromapUsageCount));
            Assert.Contains(exported, static type => type.Name == nameof(ERHIOpacityMicromapFormat));
            Assert.Contains(exported, static type => type.Name == nameof(ERHIOpacityMicromapSpecialIndex));
            Assert.True(Enum.IsDefined(ERHIAccelStructInstanceFlag.ForceOmm2State));
            Assert.True(Enum.IsDefined(ERHIAccelStructInstanceFlag.DisableOmms));
            Assert.Equal(0x10, (int)ERHIAccelStructInstanceFlag.ForceOmm2State);
            Assert.Equal(0x20, (int)ERHIAccelStructInstanceFlag.DisableOmms);
        }

        [Fact]
        public void UnprobedAndUnavailableBackends_OpacityMicromapFactoriesThrow()
        {
            RHICapability unavailable = RHICapability.Unavailable(
                "unprobed",
                ERHICapabilityProbeKind.BackendContract,
                "OpacityMicromapPortableContractTests");
            RHIRayTracingCapabilities unprobed = new(
                unavailable,
                unavailable,
                unavailable,
                unavailable);
            Assert.Equal(ERHICapabilityTier.Unavailable, unprobed.OpacityMicromap.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, unprobed.OpacityMicromapSerialization.Tier);
            Assert.Throws<NotSupportedException>(() => unprobed.OpacityMicromap.Require("RayTracing.OpacityMicromap"));

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

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    RHIDevice device = instance.GetDevice(0);
                    Assert.Equal(
                        ERHICapabilityTier.Unavailable,
                        device.Capabilities.RayTracing.OpacityMicromapSerialization.Tier);
                    if (device.Capabilities.RayTracing.OpacityMicromap.Tier != ERHICapabilityTier.Unavailable)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(
                            device.Capabilities.RayTracing.OpacityMicromap.Provenance.Source));
                        continue;
                    }

                    RHIOpacityMicromapBuildDescriptor descriptor = default;
                    Assert.Throws<NotSupportedException>(
                        () => device.GetOpacityMicromapMemoryRequirements(in descriptor));
                    Assert.Throws<NotSupportedException>(
                        () => device.CreateOpacityMicromap(in descriptor));
                }
            }
        }

        [Fact]
        public void UnknownOpacityMicromapFormat_IsFailClosed()
        {
            bool probed = false;
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

                using (instance)
                {
                    if (instance.DeviceCount <= 0)
                    {
                        continue;
                    }

                    probed = true;
                    RHIDevice device = instance.GetDevice(0);
                    using RHIBuffer input = device.CreateBuffer(new RHIBufferDescriptor
                    {
                        ByteSize = 128,
                        UsageFlag = ERHIBufferUsage.ShaderResource,
                        StorageMode = ERHIStorageMode.HostUpload,
                    });
                    using RHIBuffer triangleArray = device.CreateBuffer(new RHIBufferDescriptor
                    {
                        ByteSize = 8,
                        UsageFlag = ERHIBufferUsage.ShaderResource,
                        StorageMode = ERHIStorageMode.HostUpload,
                    });
                    RHIOpacityMicromapBuildDescriptor descriptor = new()
                    {
                        UsageCounts = new[]
                        {
                            new RHIOpacityMicromapUsageCount
                            {
                                Count = 1,
                                SubdivisionLevel = 0,
                                Format = (ERHIOpacityMicromapFormat)99,
                            },
                        },
                        InputBuffer = input,
                        TriangleArrayBuffer = triangleArray,
                        TriangleArrayStride = 8,
                    };
                    Assert.Throws<ArgumentOutOfRangeException>(
                        () => device.GetOpacityMicromapMemoryRequirements(in descriptor));
                }
            }

            Assert.True(probed, "Unknown-format fail-closed requires at least one live RHI device.");
        }

        private static PropertyInfo? GetPublicInstanceProperty(Type type, string name)
        {
            return type.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }

        private static FieldInfo? GetPublicInstanceField(Type type, string name)
        {
            return type.GetField(
                name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }
    }
}
