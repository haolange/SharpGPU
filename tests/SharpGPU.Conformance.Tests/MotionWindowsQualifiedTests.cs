#if SHARPGPU_ENABLE_DX12
using Xunit;
using System;
using SharpGPU;

namespace SharpGPU.Conformance.Tests
{
    public sealed class MotionWindowsQualifiedTests
    {
        [Fact]
        [Trait("Category", "SharpGpuWindowsQualified")]
        public void Dx12_Motion_CreateIsFailClosed()
        {
            using FeatureContractContext context = RequireDx12();
            Dx12Device device = Assert.IsType<Dx12Device>(context.Device);
            RHICapability capability = device.Capabilities.RayTracing.Motion;
            Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
            Assert.Contains(
                "no standard motion-blur",
                capability.UnavailableReason,
                StringComparison.OrdinalIgnoreCase);
            MotionQualificationHelper.AssertUnavailableMotionCreateThrows(device);
        }

        private static FeatureContractContext RequireDx12()
        {
            Assert.True(
                FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out string reason),
                $"DX12 is required for the Windows-qualified motion gate: {reason}");
            Assert.NotNull(context);
            return context;
        }
    }
}
#endif
