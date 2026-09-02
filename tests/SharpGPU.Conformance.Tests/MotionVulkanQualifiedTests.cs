#if SHARPGPU_VULKAN_QUALIFICATION_HOST
using Xunit;
using System;
using SharpGPU;
using Vortice.Vulkan;

namespace SharpGPU.Conformance.Tests
{
    public sealed class MotionVulkanQualifiedTests
    {
        [Fact]
        [Trait("Category", "SharpGpuVulkanQualified")]
        public void Vulkan_Motion_TinyInstanceBuildOrFailClosed()
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

            RHICapability capability = device.Capabilities.RayTracing.Motion;
            if (capability.Tier != ERHICapabilityTier.Unavailable)
            {
                Assert.Contains(
                    VulkanRayTracingMotionNative.ExtensionName,
                    capability.Provenance.Source,
                    StringComparison.Ordinal);
                MotionQualificationHelper.BuildTinyMotionInstanceTlas(device, queue, fence);
                VulkanDevice vulkanDevice = Assert.IsType<VulkanDevice>(device);
                VulkanNative.vkDeviceWaitIdle(vulkanDevice.NativeDevice);
                return;
            }

            MotionQualificationHelper.AssertUnavailableMotionCreateThrows(device);
        }

        [Fact]
        [Trait("Category", "SharpGpuVulkanQualified")]
        public void Vulkan_Motion_TinyTriangleBlasOrFailClosed()
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

            RHICapability capability = device.Capabilities.RayTracing.Motion;
            if (capability.Tier != ERHICapabilityTier.Unavailable)
            {
                Assert.Contains(
                    VulkanRayTracingMotionNative.ExtensionName,
                    capability.Provenance.Source,
                    StringComparison.Ordinal);
                MotionQualificationHelper.BuildTinyMotionTriangleBlas(device, queue, fence);
                VulkanDevice vulkanDevice = Assert.IsType<VulkanDevice>(device);
                VulkanNative.vkDeviceWaitIdle(vulkanDevice.NativeDevice);
                return;
            }

            MotionQualificationHelper.AssertUnavailableMotionCreateThrows(device);
        }
    }
}
#endif
