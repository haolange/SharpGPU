using Xunit;
using System;
using SharpMetal.Metal;
using SharpGPU;

namespace SharpGPU.Conformance.Tests
{

    /// <summary>
    /// Unit tests for Metal backend logic that does not require a live GPU device.
    /// These cover binding helpers and ray function table validators
    /// — all exercisable without calling into actual Metal APIs.
    /// </summary>
    public class MetalBackendTests
    {
    // ── MetalBindingHelpers ──────────────────────────────────────────────────

    [Fact]
    public void BindingHelpers_RayFunctionTableSlotConflict_DetectsBufferSlots29And30()
    {
        // Slots 29 and 30 are reserved for ray function table (buffer bind types only)
        MetalBindInfo[] conflict29 = { new MetalBindInfo(29, 0, 1, ERHIBindType.UniformBuffer, ERHIShaderStageMask.Compute) };
        Assert.True(MetalBindingHelpers.HasRayFunctionTableSlotConflict(conflict29));

        MetalBindInfo[] conflict30 = { new MetalBindInfo(30, 0, 1, ERHIBindType.Buffer, ERHIShaderStageMask.Compute) };
        Assert.True(MetalBindingHelpers.HasRayFunctionTableSlotConflict(conflict30));
    }

    [Fact]
    public void BindingHelpers_RayFunctionTableSlotConflict_NoConflictForTexture()
    {
        // Texture at slot 29 is NOT a conflict (only buffer types conflict)
        MetalBindInfo[] noConflict = { new MetalBindInfo(29, 0, 1, ERHIBindType.Texture2D, ERHIShaderStageMask.Fragment) };
        Assert.False(MetalBindingHelpers.HasRayFunctionTableSlotConflict(noConflict));
    }

    [Fact]
    public void BindingHelpers_RayFunctionTableSlotConflict_NoConflictForSlots0To28()
    {
        MetalBindInfo[] binds = { new MetalBindInfo(0, 0, 1, ERHIBindType.Buffer, ERHIShaderStageMask.Compute) };
        Assert.False(MetalBindingHelpers.HasRayFunctionTableSlotConflict(binds));

        MetalBindInfo[] binds28 = { new MetalBindInfo(28, 0, 1, ERHIBindType.UniformBuffer, ERHIShaderStageMask.Compute) };
        Assert.False(MetalBindingHelpers.HasRayFunctionTableSlotConflict(binds28));
    }

    // ── MetalRayFunctionTableValidator ───────────────────────────────────────

    [Fact]
    public void RayFunctionTableValidator_ValidHitGroups_DoesNotThrow()
    {
        System.Collections.Generic.HashSet<string> known =
            new(System.StringComparer.Ordinal) { "HitTriangles", "HitCurves" };
        MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitTriangles", "HitCurves" }, known.Contains);
    }

    [Fact]
    public void RayFunctionTableValidator_DuplicateHitGroup_Throws()
    {
        System.Collections.Generic.HashSet<string> known =
            new(System.StringComparer.Ordinal) { "HitA" };
        Assert.Throws<InvalidOperationException>(() =>
            MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitA", "HitA" }, known.Contains));
    }

    [Fact]
    public void RayFunctionTableValidator_MissingHitGroup_Throws()
    {
        System.Collections.Generic.HashSet<string> known =
            new(System.StringComparer.Ordinal) { "HitA" };
        Assert.Throws<InvalidOperationException>(() =>
            MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitMissing" }, known.Contains));
    }

    [Fact]
    public void RayFunctionTableValidator_ValidMissEntries_DoesNotThrow()
    {
        System.Collections.Generic.HashSet<string> known =
            new(System.StringComparer.Ordinal) { "MissBlue", "MissOrange" };
        MetalRayFunctionTableValidator.ValidateMissExports(new[] { "MissBlue" }, known.Contains);
    }

    [Fact]
    public void RayFunctionTableValidator_MissingMissEntry_Throws()
    {
        System.Collections.Generic.HashSet<string> known =
            new(System.StringComparer.Ordinal) { "MissBlue" };
        Assert.Throws<InvalidOperationException>(() =>
            MetalRayFunctionTableValidator.ValidateMissExports(new[] { "MissUnknown" }, known.Contains));
    }

    [Fact]
    public void MetalStageMapping_ExtendedStagesUseConservativeMetal4Bits()
    {
        Assert.Equal(1UL << 28, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Transfer));
        Assert.Equal(1UL << 27, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Indirect));
        Assert.Equal(1UL << 0, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.IndexInput));
        Assert.Equal(1UL << 0, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.VertexInput));
        Assert.Equal(1UL << 29, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.AccelStructBuild));
        Assert.Equal(1UL << 29, MetalUtility.ConvertToMetal4Stages(ERHIStageMask.AccelStructCopy));

        ulong mask = MetalUtility.ConvertToMetal4Stages(
            ERHIStageMask.Transfer
            | ERHIStageMask.Indirect
            | ERHIStageMask.IndexInput
            | ERHIStageMask.VertexInput
            | ERHIStageMask.AccelStructBuild
            | ERHIStageMask.AccelStructCopy);
        Assert.True((mask & (1UL << 28)) != 0); // Transfer -> Blit
        Assert.True((mask & (1UL << 27)) != 0); // Indirect -> Dispatch
        Assert.True((mask & (1UL << 0)) != 0);
        Assert.True((mask & (1UL << 29)) != 0);
    }

    [Fact]
    public void Metal265Bindings_ExposeTensorAndRayTracingConstants()
    {
        Assert.Equal((long)MTLDataType.Float, (long)MTLTensorDataType.Float32);
        Assert.Equal((long)MTLDataType.Half, (long)MTLTensorDataType.Float16);
        Assert.Equal((long)MTLDataType.BFloat, (long)MTLTensorDataType.BFloat16);
        Assert.Equal(143L, (long)MTLTensorDataType.Int4);
        Assert.Equal(144L, (long)MTLTensorDataType.UInt4);

        Assert.Equal(1UL << 0, (ulong)MTLTensorUsage.Compute);
        Assert.Equal(1UL << 1, (ulong)MTLTensorUsage.Render);
        Assert.Equal(1UL << 2, (ulong)MTLTensorUsage.MachineLearning);

        Assert.Equal(139UL, (ulong)MTLDataType.DepthStencilState);
        Assert.Equal(140UL, (ulong)MTLDataType.Tensor);
        Assert.Equal(37L, (long)MTLBindingType.Tensor);

        Assert.Equal(1UL << 4, (ulong)MTLAccelerationStructureUsage.PreferFastIntersection);
        Assert.Equal(1UL << 5, (ulong)MTLAccelerationStructureUsage.MinimizeMemory);
        Assert.Equal(1UL << 8, (ulong)MTLIntersectionFunctionSignature.IntersectionFunctionBuffer);
        Assert.Equal(1UL << 9, (ulong)MTLIntersectionFunctionSignature.UserData);

        Assert.Equal(19UL, (ulong)MTLBlendFactor.Unspecialized);
        Assert.Equal(5UL, (ulong)MTLBlendOperation.Unspecialized);
        Assert.Equal(16UL, (ulong)MTLColorWriteMask.Unspecialized);
    }

    // ── MetalBarrierHelper planner (low-overhead merge/dedup) ──────────────

    [Fact]
    public void MetalBarrierPlanner_RepeatedIntraBarriers_MergesIntoSingleEmissionBucket()
    {
        RHIBarrier repeated = RHIBarrier.Global(
            ERHIStageMask.Compute,
            ERHIStageMask.Fragment,
            ERHIAccessMask.ShaderWrite,
            ERHIAccessMask.ShaderRead);
        RHIBarrier[] barriers = CreateRepeatedBarriers(repeated, 128);

        ulong seenStages = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute);
        var plan = MetalBarrierHelper.PlanBarriersForTesting(seenStages, barriers);

        Assert.Equal(128, plan.IntraBarrierCount);
        Assert.Equal(0, plan.QueueBarrierCount);
        Assert.NotEqual(0UL, plan.IntraAfterStages);
        Assert.NotEqual(0UL, plan.IntraBeforeStages);

        int emittedCalls = CountPlannedCalls(plan);
        Assert.Equal(1, emittedCalls);
    }

    [Fact]
    public void MetalBarrierPlanner_MixedIntraAndQueueBarriers_EmitsAtMostTwoCalls()
    {
        RHIBarrier intra = RHIBarrier.Buffer(
            (RHIBuffer)null!,
            RHIBufferRange.Whole(),
            ERHIStageMask.Compute,
            ERHIStageMask.Fragment,
            ERHIAccessMask.TransferWrite,
            ERHIAccessMask.ShaderRead);

        RHIBarrier queue = RHIBarrier.Texture(
            (RHITexture)null!,
            RHITextureSubresourceRange.Whole(),
            ERHITextureLayout.General,
            ERHITextureLayout.ShaderReadOnly,
            ERHIStageMask.RayTracing,
            ERHIStageMask.Fragment,
            ERHIAccessMask.ShaderWrite,
            ERHIAccessMask.ShaderRead);

        RHIBarrier[] barriers = new RHIBarrier[64];
        for (int i = 0; i < barriers.Length; ++i)
        {
            barriers[i] = (i & 1) == 0 ? intra : queue;
        }

        ulong seenStages = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute);
        var plan = MetalBarrierHelper.PlanBarriersForTesting(seenStages, barriers);

        Assert.True(plan.IntraBarrierCount > 0);
        Assert.True(plan.QueueBarrierCount > 0);
        Assert.NotEqual(0UL, plan.IntraAfterStages);
        Assert.NotEqual(0UL, plan.QueueAfterStages);

        int emittedCalls = CountPlannedCalls(plan);
        Assert.True(emittedCalls <= 2);
        Assert.Equal(2, emittedCalls);
    }

    [Fact]
    public void MetalBarrierPlanner_HasNoCrossCallCache()
    {
        RHIBarrier barrier = RHIBarrier.Global(
            ERHIStageMask.Compute,
            ERHIStageMask.Fragment,
            ERHIAccessMask.ShaderWrite,
            ERHIAccessMask.ShaderRead);

        RHIBarrier[] barriers = CreateRepeatedBarriers(barrier, 4);

        ulong seenCompute = MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Compute);
        var firstPlan = MetalBarrierHelper.PlanBarriersForTesting(seenCompute, barriers);
        Assert.True(firstPlan.IntraBarrierCount > 0);
        Assert.Equal(0, firstPlan.QueueBarrierCount);

        var secondPlan = MetalBarrierHelper.PlanBarriersForTesting(0, barriers);
        Assert.Equal(0, secondPlan.IntraBarrierCount);
        Assert.True(secondPlan.QueueBarrierCount > 0);
    }

    private static int CountPlannedCalls(MetalBarrierHelper.MetalBarrierBatchPlan plan)
    {
        int callCount = 0;
        if (plan.IntraAfterStages != 0 && plan.IntraBeforeStages != 0)
        {
            ++callCount;
        }

        if (plan.QueueAfterStages != 0 && plan.QueueBeforeStages != 0)
        {
            ++callCount;
        }

        return callCount;
    }

    private static RHIBarrier[] CreateRepeatedBarriers(RHIBarrier barrier, int count)
    {
        RHIBarrier[] barriers = new RHIBarrier[count];
        for (int i = 0; i < count; ++i)
        {
            barriers[i] = barrier;
        }

        return barriers;
    }

    }
}
