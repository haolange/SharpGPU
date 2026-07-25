using System;
using SharpGPU;
using SharpGPU.Mathematics;
using Vortice.Vulkan;
using Xunit;

namespace SharpGPU.Conformance.Tests;

/// <summary>
/// Real-device behavioral coverage for the Vulkan backend-private descriptor
/// clone used by an ordinary SampledFeedback argument table. The caller table
/// remains caller-owned and is never rewritten by the HAL.
/// </summary>
public sealed class VulkanSampledFeedbackCloneQualifiedGpuTests
{
#if SHARPGPU_VULKAN_QUALIFICATION_HOST
    [Fact]
    [Trait("Category", "SharpGpuVulkanQualified")]
    public void ArrayExactClone_PreservesCallerTableAndRollsBackPartialOverlap()
    {
        Assert.True(
            OperatingSystem.IsWindows() || OperatingSystem.IsLinux(),
            "This qualification requires a matching Windows or Linux Vulkan host.");

        using RHIInstance instance = RHIInstance.Create(
            new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.Vulkan,
                SurfaceKind = RHINativeSurfaceKind.Headless,
                EnableDebugLayer = true,
                EnableValidation = true,
                GraphicsQueueRequestCount = 1,
            });
        VulkanInstance vulkanInstance = Assert.IsType<VulkanInstance>(instance);
        Assert.True(vulkanInstance.HasDebugUtils);
#if DEBUG
        Assert.True(vulkanInstance.HasValidationLayerEnabled);
#endif
        VulkanValidationDiagnostics diagnostics = Assert.IsType<
            VulkanValidationDiagnostics>(vulkanInstance.ValidationDiagnostics);
        VulkanDevice device = SelectDevice(instance);
        try
        {
            using RHITexture texture = device.CreateTexture(
                new RHITextureDescriptor
                {
                    Extent = new uint3(4, 4, 2),
                    MipCount = 2,
                    Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                    SampleCount = ERHISampleCount.None,
                    StorageMode = ERHIStorageMode.GPULocal,
                    UsageFlag = ERHITextureUsage.ShaderResource,
                    Dimension = ERHITextureDimension.Texture2DArray,
                });
            using RHITextureView exactView = texture.CreateTextureView(
                new RHITextureViewDescriptor
                {
                    BaseMipLevel = 0,
                    MipCount = 1,
                    BaseArraySlice = 1,
                    ArrayCount = 1,
                    ViewType = ERHITextureViewType.ShaderResource,
                });
            using RHITextureView disjointView = texture.CreateTextureView(
                new RHITextureViewDescriptor
                {
                    BaseMipLevel = 1,
                    MipCount = 1,
                    BaseArraySlice = 0,
                    ArrayCount = 1,
                    ViewType = ERHITextureViewType.ShaderResource,
                });
            using RHIArgumentTableLayout layout =
                device.CreateArgumentTableLayout(
                    new RHIArgumentTableLayoutDescriptor
                    {
                        Index = 0,
                        Elements = new RHIArgumentTableLayoutElement[]
                        {
                            new RHIArgumentTableLayoutElement
                            {
                                Slot = 7,
                                Count = 2,
                                Type = ERHIBindType.Texture2DArray,
                                Stages = ERHIShaderStageMask.Fragment,
                                Requirement =
                                    ERHIArgumentBindingRequirement.Required,
                            },
                        },
                    });
            using RHIArgumentTable publicTable = device.CreateArgumentTable(
                new RHIArgumentTableDescriptor
                {
                    Layout = layout,
                    Elements = Array.Empty<RHIArgumentTableElement>(),
                });
            VulkanArgumentTable table = Assert.IsType<VulkanArgumentTable>(
                publicTable);
            table.SetBindElement(
                new RHIArgumentTableElement { TextureView = exactView },
                ERHIBindType.Texture2DArray,
                slot: 7,
                arrayIndex: 0);
            table.SetBindElement(
                new RHIArgumentTableElement { TextureView = disjointView },
                ERHIBindType.Texture2DArray,
                slot: 7,
                arrayIndex: 1);

            VulkanTexture nativeTexture = Assert.IsType<VulkanTexture>(texture);
            RHITextureSubresourceRange exactRange = new()
            {
                AspectMask = ERHITextureAspectMask.Color,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 1,
                ArrayLayerCount = 1,
            };
            VulkanSampledFeedbackAttachmentFact exact =
                new(nativeTexture.NativeImage, in exactRange, attachmentMask: 1);
            VkDescriptorSet originalSet = table.NativeDescriptorSet;
            ulong originalRevision = table.DescriptorRevision;
            int initialAllocatedSetCount =
                device.DescriptorPoolAllocator.AllocatedSetCount;

            VulkanDescriptorSetLease clone = table.CloneForSampledFeedback(
                [exact],
                out byte exactMask,
                out ulong cloneRevision);
            try
            {
                Assert.NotEqual(originalSet, clone.Set);
                Assert.Equal((byte)1, exactMask);
                Assert.Equal(originalRevision, cloneRevision);
                Assert.Equal(originalSet, table.NativeDescriptorSet);
                Assert.Equal(originalRevision, table.DescriptorRevision);
                Assert.Equal(
                    initialAllocatedSetCount + 1,
                    device.DescriptorPoolAllocator.AllocatedSetCount);
            }
            finally
            {
                device.DescriptorPoolAllocator.Free(in clone);
            }
            Assert.Equal(
                initialAllocatedSetCount,
                device.DescriptorPoolAllocator.AllocatedSetCount);

            RHITextureSubresourceRange partiallyOverlappingRange = new()
            {
                AspectMask = ERHITextureAspectMask.Color,
                BaseMipLevel = 0,
                MipLevelCount = 1,
                BaseArrayLayer = 0,
                ArrayLayerCount = 2,
            };
            VulkanSampledFeedbackAttachmentFact partial =
                new(
                    nativeTexture.NativeImage,
                    in partiallyOverlappingRange,
                    attachmentMask: 1);
            Assert.Throws<ArgumentException>(
                () => table.CloneForSampledFeedback(
                    [partial],
                    out _,
                    out _));
            Assert.Equal(
                initialAllocatedSetCount,
                device.DescriptorPoolAllocator.AllocatedSetCount);
            Assert.Equal(originalSet, table.NativeDescriptorSet);
            Assert.Equal(originalRevision, table.DescriptorRevision);
        }
        finally
        {
            device.Dispose();
            diagnostics.ThrowIfErrors(
                "Vulkan SampledFeedback clone and device teardown");
        }
    }
#endif

    private static VulkanDevice SelectDevice(RHIInstance instance)
    {
        for (int index = 0; index < instance.DeviceCount; ++index)
        {
            if (instance.GetDevice(index) is VulkanDevice device)
            {
                return device;
            }
        }

        throw new NotSupportedException(
            "No Vulkan device is available for SampledFeedback clone qualification.");
    }
}
