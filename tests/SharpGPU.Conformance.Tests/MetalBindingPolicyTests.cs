using Xunit;
using System;
using SharpGPU;
using System.Collections.Generic;

namespace SharpGPU.Conformance.Tests
{
    public class MetalBindingPolicyTests
    {
        [Fact]
        public void HasRayFunctionTableSlotConflict_ShouldDetectReservedBufferSlots()
        {
            MetalBindInfo[] conflictAt29 = { new MetalBindInfo(29, 0, 1, ERHIBindType.UniformBuffer, ERHIShaderStageMask.Compute) };
            Assert.True(MetalBindingHelpers.HasRayFunctionTableSlotConflict(conflictAt29));

            MetalBindInfo[] conflictAt30 = { new MetalBindInfo(30, 0, 1, ERHIBindType.Buffer, ERHIShaderStageMask.Compute) };
            Assert.True(MetalBindingHelpers.HasRayFunctionTableSlotConflict(conflictAt30));

            MetalBindInfo[] noConflictTexture = { new MetalBindInfo(29, 0, 1, ERHIBindType.Texture2D, ERHIShaderStageMask.Compute) };
            Assert.False(MetalBindingHelpers.HasRayFunctionTableSlotConflict(noConflictTexture));
        }

        [Fact]
        public void HitGroupValidator_ShouldRejectInvalidAndMissingExports()
        {
            HashSet<string> hitGroups = new HashSet<string>(StringComparer.Ordinal) { "HitA", "HitB" };
            MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitA", "HitB" }, hitGroups.Contains);

            Assert.Throws<InvalidOperationException>(() =>
                MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitA", "HitA" }, hitGroups.Contains));

            Assert.Throws<InvalidOperationException>(() =>
                MetalRayFunctionTableValidator.ValidateHitGroupExports(new[] { "HitMissing" }, hitGroups.Contains));
        }

        [Fact]
        public void MissValidator_ShouldRejectMissingExports()
        {
            HashSet<string> missEntries = new HashSet<string>(StringComparer.Ordinal) { "MissBlue", "MissOrange" };
            MetalRayFunctionTableValidator.ValidateMissExports(new[] { "MissBlue" }, missEntries.Contains);

            Assert.Throws<InvalidOperationException>(() =>
                MetalRayFunctionTableValidator.ValidateMissExports(new[] { "MissUnknown" }, missEntries.Contains));
        }
    }
}
