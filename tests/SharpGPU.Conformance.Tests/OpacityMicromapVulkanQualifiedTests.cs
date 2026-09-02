#if SHARPGPU_VULKAN_QUALIFICATION_HOST
using Xunit;
using System;
using SharpGPU;

namespace SharpGPU.Conformance.Tests
{
    public sealed class OpacityMicromapVulkanQualifiedTests
    {
        [Fact]
        [Trait("Category", "SharpGpuVulkanQualified")]
        public void Vulkan_OpacityMicromap_TinyTwoStateBuildAttachesToBlasOrFailClosed()
        {
            Assert.True(
                OperatingSystem.IsWindows() ||
                OperatingSystem.IsLinux() ||
                OperatingSystem.IsAndroid(),
                "SharpGpuVulkanQualified requires a Vulkan qualification host.");

            using RHIInstance instance = RHIInstance.Create(
                new RHIInstanceDescriptor
                {
                    Backend = ERHIBackend.Vulkan,
                    SurfaceKind = ERHINativeSurfaceKind.Headless,
                    EnableDebugLayer = false,
                    EnableValidation = false,
                    GraphicsQueueRequestCount = 1,
                });
            Assert.True(instance.DeviceCount > 0, "Vulkan instance enumerated no devices.");

            RHIDevice? device = null;
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice candidate = instance.GetDevice(i);
                if (candidate.GetCommandQueue(ERHIPipelineType.Graphics, 0) != null)
                {
                    device = candidate;
                    break;
                }
            }

            Assert.NotNull(device);
            RHICommandQueue queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0)
                ?? throw new InvalidOperationException("Vulkan graphics queue is unavailable.");
            using RHIFence fence = device.CreateFence();

            RHICapability capability = device.Capabilities.RayTracing.OpacityMicromap;
            Assert.Equal(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.RayTracing.OpacityMicromapSerialization.Tier);

            if (capability.Tier != ERHICapabilityTier.Unavailable)
            {
                OpacityMicromapQualificationHelper.BuildTinyTwoStateOmmAndOneTriangleBlas(
                    device,
                    queue,
                    fence);
                return;
            }

            OpacityMicromapQualificationHelper.AssertUnavailableFactoriesThrow(device);
        }
    }
}
#endif
