#if SHARPGPU_VULKAN_QUALIFICATION_HOST
using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class VulkanDispatchTableLifetimeQualifiedTests
    {
        [Fact]
        [Trait("Category", "SharpGpuVulkanQualified")]
        public void Vulkan_RecreatingWithDifferentValidationModes_RebuildsDispatchTables()
        {
            Assert.True(
                OperatingSystem.IsWindows()
                || OperatingSystem.IsLinux()
                || OperatingSystem.IsAndroid(),
                "SharpGpuVulkanQualified requires a Vulkan qualification host.");

            Assert.True(
                RHIInstance.IsBackendSupported(ERHIBackend.Vulkan, out string reason),
                reason);

            CreateAndDispose(enableValidation: false);
            CreateAndDispose(enableValidation: true);
            CreateAndDispose(enableValidation: false);
            CreateAndDispose(enableValidation: true);
        }

        private static void CreateAndDispose(bool enableValidation)
        {
            using RHIInstance instance = RHIInstance.Create(
                new RHIInstanceDescriptor
                {
                    Backend = ERHIBackend.Vulkan,
                    SurfaceKind = ERHINativeSurfaceKind.Headless,
                    EnableDebugLayer = false,
                    EnableValidation = enableValidation,
                    GraphicsQueueRequestCount = 1,
                });

            Assert.True(instance.DeviceCount > 0, "Vulkan enumerated no devices.");
            RHIDevice device = instance.GetDevice(0);
            RHICommandQueue queue = device.GetCommandQueue(
                    ERHIPipelineType.Graphics,
                    0)
                ?? throw new InvalidOperationException(
                    "Vulkan graphics queue is unavailable.");

            using RHICommandBuffer commandBuffer = queue.CreateCommandBuffer();
        }
    }
}
#endif