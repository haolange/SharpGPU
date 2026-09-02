#if SHARPGPU_ENABLE_DX12
using Xunit;
using System;
using SharpGPU;
using Vortice.Direct3D12;

namespace SharpGPU.Conformance.Tests
{
    public sealed class OpacityMicromapWindowsQualifiedTests
    {
        [Fact]
        [Trait("Category", "SharpGpuWindowsQualified")]
        public void Dx12_OpacityMicromap_TinyTwoStateBuildAttachesToBlasOrFailClosed()
        {
            using FeatureContractContext context = RequireDx12();
            Dx12Device device = Assert.IsType<Dx12Device>(context.Device);
            RHICapability capability = device.Capabilities.RayTracing.OpacityMicromap;
            Assert.Equal(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.RayTracing.OpacityMicromapSerialization.Tier);

            FeatureDataD3D12Options5 options5 = default;
            Assert.True(
                device.NativeDevice.CheckFeatureSupport(
                    Feature.Options5,
                    ref options5),
                "Independent OPTIONS5 oracle query failed.");

            if (options5.RaytracingTier >= RaytracingTier.Tier1_2)
            {
                Assert.NotEqual(ERHICapabilityTier.Unavailable, capability.Tier);
                OpacityMicromapQualificationHelper.BuildTinyTwoStateOmmAndOneTriangleBlas(
                    device,
                    context.CommandQueue,
                    context.Fence);
                Assert.True(
                    device.NativeDevice.DeviceRemovedReason.Success,
                    $"DX12 device was removed after OMM+BLAS build: 0x{device.NativeDevice.DeviceRemovedReason.Code:X8}.");
                return;
            }

            Assert.Equal(ERHICapabilityTier.Unavailable, capability.Tier);
            OpacityMicromapQualificationHelper.AssertUnavailableFactoriesThrow(device);
        }

        private static FeatureContractContext RequireDx12()
        {
            Assert.True(
                FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out string reason),
                $"DX12 is required for the Windows-qualified opacity micromap gate: {reason}");
            Assert.NotNull(context);
            return context;
        }
    }
}
#endif
