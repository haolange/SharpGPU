using Xunit;
using System;
using Vortice.Vulkan;
using SharpGPU;
using System.Reflection;

namespace SharpGPU.Conformance.Tests
{
    public class VulkanBarrierMappingTests
    {
        private static readonly Type s_EmitterType = typeof(RHIBarrier).Assembly.GetType("SharpGPU.VulkanBarrierEmitter", throwOnError: true)!;

        [Fact]
        public void Sync1StageMapping_MapsExtendedSyncMaskStages()
        {
            VkPipelineStageFlags stages = VulkanUtility.ConvertToVkPipelineStage(
                ERHIStageMask.Transfer
                | ERHIStageMask.Indirect
                | ERHIStageMask.IndexInput
                | ERHIStageMask.VertexInput
                | ERHIStageMask.AccelStructBuild
                | ERHIStageMask.AccelStructCopy,
                ERHIPipelineType.Graphics);

            Assert.True((stages & VkPipelineStageFlags.Transfer) != 0);
            Assert.True((stages & VkPipelineStageFlags.DrawIndirect) != 0);
            Assert.True((stages & VkPipelineStageFlags.VertexInput) != 0);
            Assert.True((stages & VkPipelineStageFlags.AccelerationStructureBuildKHR) != 0);
        }

        [Fact]
        public void Sync2StageAndAccessMapping_MapsExtendedBits()
        {
            VkPipelineStageFlags2 stages = VulkanUtility.ConvertToVkPipelineStage2(
                ERHIStageMask.Transfer | ERHIStageMask.AccelStructCopy | ERHIStageMask.RayTracing,
                ERHIPipelineType.Compute);
            Assert.True((stages & VkPipelineStageFlags2.AllTransfer) != 0);
            Assert.True((stages & VkPipelineStageFlags2.AccelerationStructureCopyKHR) != 0);
            Assert.True((stages & VkPipelineStageFlags2.RayTracingShaderKHR) != 0);

            VkAccessFlags2 access = VulkanUtility.ConvertToVkAccessFlags2(
                ERHIAccessMask.ShaderRead | ERHIAccessMask.TransferWrite | ERHIAccessMask.AccelStructWrite);
            Assert.True((access & VkAccessFlags2.ShaderRead) != 0);
            Assert.True((access & VkAccessFlags2.TransferWrite) != 0);
            Assert.True((access & VkAccessFlags2.AccelerationStructureWriteKHR) != 0);
        }

        [Fact]
        public void Sync1BucketPlan_GroupsBySrcDstStagePair()
        {
            RHIBarrier[] barriers =
            {
                RHIBarrier.Global(
                    ERHIStageMask.Transfer,
                    ERHIStageMask.Compute,
                    ERHIAccessMask.TransferWrite,
                    ERHIAccessMask.ShaderRead),
                RHIBarrier.Buffer(
                    (RHIBuffer)null!,
                    RHIBufferRange.Whole(),
                    ERHIStageMask.Transfer,
                    ERHIStageMask.Compute,
                    ERHIAccessMask.TransferWrite,
                    ERHIAccessMask.ShaderRead),
                RHIBarrier.Texture(
                    (RHITexture)null!,
                    RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
                    ERHITextureLayout.CopyDestination,
                    ERHITextureLayout.ShaderReadOnly,
                    ERHIStageMask.Transfer,
                    ERHIStageMask.Compute,
                    ERHIAccessMask.TransferWrite,
                    ERHIAccessMask.ShaderRead),
                RHIBarrier.Global(
                    ERHIStageMask.Compute,
                    ERHIStageMask.Fragment,
                    ERHIAccessMask.ShaderWrite,
                    ERHIAccessMask.ShaderRead)
            };

            var plans = VulkanBarrierEmitter.PlanSync1BucketsForTesting(ERHIPipelineType.Graphics, barriers);

            Assert.Equal(2, plans.Length);
            Assert.Contains(plans, plan => plan.BarrierCount == 3);
            Assert.Contains(plans, plan => plan.BarrierCount == 1);
        }

        [Fact]
        public void AspectFallback_UsesFormatWhenAspectMaskIsNone()
        {
            VkImageAspectFlags aspect = VulkanUtility.ConvertToVkImageAspect(ERHITextureAspectMask.None, ERHIPixelFormat.D24_UNorm_S8_UInt);
            Assert.True((aspect & VkImageAspectFlags.Depth) != 0);
            Assert.True((aspect & VkImageAspectFlags.Stencil) != 0);
        }

        [Fact]
        public void ImageSubresourceRangeMapping_ConvertsAllCountsWithoutPlaneDimension()
        {
            RHITextureSubresourceRange wholeRange = RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color);
            VkImageSubresourceRange whole = InvokePrivate<VkImageSubresourceRange>(
                "ConvertToVkSubresourceRange",
                new[] { typeof(RHITextureSubresourceRange).MakeByRefType()!, typeof(ERHIPixelFormat) },
                wholeRange,
                ERHIPixelFormat.R8G8B8A8_UNorm);

            Assert.Equal(uint.MaxValue, whole.levelCount);
            Assert.Equal(uint.MaxValue, whole.layerCount);
            Assert.Equal(VkImageAspectFlags.Color, whole.aspectMask);

            RHITextureSubresourceRange partialRange = new RHITextureSubresourceRange
            {
                BaseMipLevel = 1,
                MipLevelCount = 2,
                BaseArrayLayer = 3,
                ArrayLayerCount = 4,
                AspectMask = ERHITextureAspectMask.Color
            };

            VkImageSubresourceRange partial = InvokePrivate<VkImageSubresourceRange>(
                "ConvertToVkSubresourceRange",
                new[] { typeof(RHITextureSubresourceRange).MakeByRefType()!, typeof(ERHIPixelFormat) },
                partialRange,
                ERHIPixelFormat.R8G8B8A8_UNorm);

            Assert.Equal(1U, partial.baseMipLevel);
            Assert.Equal(2U, partial.levelCount);
            Assert.Equal(3U, partial.baseArrayLayer);
            Assert.Equal(4U, partial.layerCount);
            Assert.Equal(VkImageAspectFlags.Color, partial.aspectMask);
        }

        private static T InvokePrivate<T>(string methodName, Type[] parameterTypes, params object[] args)
        {
            MethodInfo? method = s_EmitterType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic, binder: null, types: parameterTypes, modifiers: null);
            Assert.NotNull(method);
            object? result = method!.Invoke(null, args);
            return (T)result!;
        }
    }
}
