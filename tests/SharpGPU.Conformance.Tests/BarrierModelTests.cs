using System;
using Xunit;
using SharpGPU;

namespace SharpGPU.Conformance.Tests
{
    public class BarrierModelTests
    {
        [Fact]
        public void BufferRange_WholeRangeDefaultsToWholeSize()
        {
            RHIBufferRange range = RHIBufferRange.Whole();
            Assert.True(range.IsWholeRange);
            Assert.Equal(0UL, range.Offset);
            Assert.Equal(RHIBufferRange.WholeSize, range.Size);
        }

        [Fact]
        public void TextureSubresourceRange_WholeDefaultsToAllCounts()
        {
            RHITextureSubresourceRange range = RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil);
            Assert.Equal(0U, range.BaseMipLevel);
            Assert.Equal(RHITextureSubresourceRange.All, range.MipLevelCount);
            Assert.Equal(0U, range.BaseArrayLayer);
            Assert.Equal(RHITextureSubresourceRange.All, range.ArrayLayerCount);
            Assert.Equal(ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil, range.AspectMask);
        }

        [Fact]
        public void GlobalBarrier_FactoryPreservesSyncAndAccessMasks()
        {
            RHIBarrier barrier = RHIBarrier.Global(
                ERHIStageMask.Compute,
                ERHIStageMask.Fragment,
                ERHIAccessMask.ShaderWrite,
                ERHIAccessMask.ShaderRead);

            Assert.Equal(ERHIBarrierKind.Global, barrier.Kind);
            Assert.Equal(ERHIStageMask.Compute, barrier.GlobalBarrier.StageBefore);
            Assert.Equal(ERHIStageMask.Fragment, barrier.GlobalBarrier.StageAfter);
            Assert.Equal(ERHIAccessMask.ShaderWrite, barrier.GlobalBarrier.AccessBefore);
            Assert.Equal(ERHIAccessMask.ShaderRead, barrier.GlobalBarrier.AccessAfter);
        }

        [Fact]
        public void BufferBarrier_FactoryPreservesExplicitState()
        {
            RHIBarrier barrier = RHIBarrier.Buffer(
                (RHIBuffer)null!,
                RHIBufferRange.Whole(),
                ERHIStageMask.Transfer,
                ERHIStageMask.VertexInput,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.VertexRead);

            Assert.Equal(ERHIBarrierKind.Buffer, barrier.Kind);
            Assert.True(barrier.BufferBarrier.Range.IsWholeRange);
            Assert.Equal(ERHIStageMask.Transfer, barrier.BufferBarrier.StageBefore);
            Assert.Equal(ERHIStageMask.VertexInput, barrier.BufferBarrier.StageAfter);
            Assert.Equal(ERHIAccessMask.TransferWrite, barrier.BufferBarrier.AccessBefore);
            Assert.Equal(ERHIAccessMask.VertexRead, barrier.BufferBarrier.AccessAfter);
        }

        [Fact]
        public void BufferBarrier_QueueOwnershipPreservesLogicalQueues()
        {
            RHIBarrier barrier = RHIBarrier.Buffer(
                (RHIBuffer)null!,
                RHIBufferRange.Whole(),
                ERHIStageMask.Transfer,
                ERHIStageMask.Compute,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.ShaderRead,
                ERHIPipelineType.Transfer,
                ERHIPipelineType.Compute);

            Assert.Equal(ERHIPipelineType.Transfer, barrier.BufferBarrier.SourceQueue);
            Assert.Equal(ERHIPipelineType.Compute, barrier.BufferBarrier.DestinationQueue);
            RHIBarrierUtility.ValidateQueueOwnership(in barrier, ERHIPipelineType.Transfer);
            RHIBarrierUtility.ValidateQueueOwnership(in barrier, ERHIPipelineType.Compute);
        }

        [Fact]
        public void QueueOwnership_RejectsIncompleteSameAndUnrelatedQueueDeclarations()
        {
            RHIBarrier incomplete = RHIBarrier.Buffer(
                (RHIBuffer)null!,
                RHIBufferRange.Whole(),
                ERHIStageMask.Transfer,
                ERHIStageMask.Compute,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.ShaderRead,
                ERHIPipelineType.Transfer);
            Assert.Throws<ArgumentException>(
                () => RHIBarrierUtility.ValidateQueueOwnership(in incomplete, ERHIPipelineType.Transfer));

            RHIBarrier sameQueue = RHIBarrier.Buffer(
                (RHIBuffer)null!,
                RHIBufferRange.Whole(),
                ERHIStageMask.Transfer,
                ERHIStageMask.Transfer,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.TransferRead,
                ERHIPipelineType.Transfer,
                ERHIPipelineType.Transfer);
            Assert.Throws<ArgumentException>(
                () => RHIBarrierUtility.ValidateQueueOwnership(in sameQueue, ERHIPipelineType.Transfer));

            RHIBarrier unrelatedQueue = RHIBarrier.Texture(
                (RHITexture)null!,
                RHITextureSubresourceRange.Whole(),
                ERHITextureLayout.CopyDestination,
                ERHITextureLayout.ShaderReadOnly,
                ERHIStageMask.Transfer,
                ERHIStageMask.Fragment,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.ShaderRead,
                ERHIPipelineType.Transfer,
                ERHIPipelineType.Graphics);
            Assert.Throws<InvalidOperationException>(
                () => RHIBarrierUtility.ValidateQueueOwnership(in unrelatedQueue, ERHIPipelineType.Compute));
        }

        [Fact]
        public void MetalBarrierPlanner_QueueOwnershipNeverCollapsesIntoIntraEncoderBarrier()
        {
            RHIBarrier ownershipTransfer = RHIBarrier.Buffer(
                (RHIBuffer)null!,
                RHIBufferRange.Whole(),
                ERHIStageMask.Compute,
                ERHIStageMask.Fragment,
                ERHIAccessMask.ShaderWrite,
                ERHIAccessMask.ShaderRead,
                ERHIPipelineType.Compute,
                ERHIPipelineType.Graphics);

            ulong seenComputeStages = MetalUtility.ConvertToMetal4Stages(
                ERHIStageMask.Compute);
            MetalBarrierHelper.MetalBarrierBatchPlan plan =
                MetalBarrierHelper.PlanBarriersForTesting(
                    seenComputeStages,
                    new[] { ownershipTransfer });

            Assert.Equal(0, plan.IntraBarrierCount);
            Assert.Equal(1, plan.QueueBarrierCount);
            Assert.Equal(0UL, plan.IntraAfterStages);
            Assert.NotEqual(0UL, plan.QueueAfterStages);
            Assert.NotEqual(0UL, plan.QueueBeforeStages);
        }

        [Fact]
        public void MetalBarrierEmitter_RejectsMissingNativeEncoder()
        {
            IntPtr missingEncoder = IntPtr.Zero;
            ulong computeStages = MetalUtility.ConvertToMetal4Stages(
                ERHIStageMask.Compute);

            Assert.Throws<InvalidOperationException>(
                () => MetalBarrierHelper.ApplyEncoderBarrier(
                    in missingEncoder,
                    in computeStages,
                    in computeStages));
        }

        [Fact]
        public void TextureBarrier_FactoryPreservesLayoutAndSubresource()
        {
            RHITextureSubresourceRange subresourceRange = new RHITextureSubresourceRange
            {
                BaseMipLevel = 1,
                MipLevelCount = 2,
                BaseArrayLayer = 3,
                ArrayLayerCount = 4,
                AspectMask = ERHITextureAspectMask.Color
            };

            RHIBarrier barrier = RHIBarrier.Texture(
                (RHITexture)null!,
                subresourceRange,
                ERHITextureLayout.CopyDestination,
                ERHITextureLayout.ShaderReadOnly,
                ERHIStageMask.Transfer,
                ERHIStageMask.Fragment,
                ERHIAccessMask.TransferWrite,
                ERHIAccessMask.ShaderRead);

            Assert.Equal(ERHIBarrierKind.Texture, barrier.Kind);
            Assert.Equal(ERHITextureLayout.CopyDestination, barrier.TextureBarrier.LayoutBefore);
            Assert.Equal(ERHITextureLayout.ShaderReadOnly, barrier.TextureBarrier.LayoutAfter);
            Assert.Equal(1U, barrier.TextureBarrier.SubresourceRange.BaseMipLevel);
            Assert.Equal(2U, barrier.TextureBarrier.SubresourceRange.MipLevelCount);
            Assert.Equal(3U, barrier.TextureBarrier.SubresourceRange.BaseArrayLayer);
            Assert.Equal(4U, barrier.TextureBarrier.SubresourceRange.ArrayLayerCount);
        }

        [Fact]
        public void SyncStageMask_CoversExtendedStages()
        {
            ERHIStageMask mask = ERHIStageMask.Transfer
                                     | ERHIStageMask.Indirect
                                     | ERHIStageMask.IndexInput
                                     | ERHIStageMask.VertexInput
                                     | ERHIStageMask.AccelStructBuild
                                     | ERHIStageMask.AccelStructCopy;

            Assert.True((mask & ERHIStageMask.Transfer) != 0);
            Assert.True((mask & ERHIStageMask.Indirect) != 0);
            Assert.True((mask & ERHIStageMask.IndexInput) != 0);
            Assert.True((mask & ERHIStageMask.VertexInput) != 0);
            Assert.True((mask & ERHIStageMask.AccelStructBuild) != 0);
            Assert.True((mask & ERHIStageMask.AccelStructCopy) != 0);
        }
    }
}
