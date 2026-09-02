#if SHARPGPU_ENABLE_DX12
using Xunit;
using System;
using SharpGPU;
using Vortice.Direct3D12;
using Xunit.Abstractions;

namespace SharpGPU.Conformance.Tests
{
    public sealed class Dxr12BindingWindowsQualifiedTests
    {
        private readonly ITestOutputHelper m_Output;

        public Dxr12BindingWindowsQualifiedTests(ITestOutputHelper output)
        {
            m_Output = output;
        }

        [Fact]
        [Trait("Category", "SharpGpuWindowsQualified")]
        public void Dx12_Options5_RaytracingTier_QueryCompilesAndReturnsDefinedValue()
        {
            using FeatureContractContext context = RequireDx12();
            Dx12Device device = Assert.IsType<Dx12Device>(context.Device);

            FeatureDataD3D12Options5 options5 = default;
            Assert.True(
                device.NativeDevice.CheckFeatureSupport(Feature.Options5, ref options5),
                "Independent OPTIONS5 oracle query failed.");

            RaytracingTier tier = options5.RaytracingTier;
            int numeric = (int)tier;
            m_Output.WriteLine($"D3D12_FEATURE_D3D12_OPTIONS5.RaytracingTier = {tier} ({numeric})");
            Assert.True(
                tier == RaytracingTier.NotSupported
                || tier == RaytracingTier.Tier1_0
                || tier == RaytracingTier.Tier1_1
                || tier == RaytracingTier.Tier1_2,
                $"OPTIONS5 RaytracingTier must be a defined enum value, got {numeric}.");

            _ = RaytracingTier.Tier1_2;
            _ = RaytracingAccelerationStructureType.OpacityMicromapArray;
            _ = RaytracingGeometryType.OmmTriangles;
            _ = RaytracingPipelineFlags.AllowOpacityMicromaps;
            _ = RaytracingAccelerationStructureBuildFlags.AllowOmmUpdate;
            _ = RayFlags.ForceOmm2State;
            _ = RaytracingInstanceFlags.ForceOmm2State;
            _ = RaytracingInstanceFlags.DisableOmms;
            _ = RaytracingOpacityMicromapFormat.Oc1_2State;
            _ = D3D12.RaytracingOpacityMicromapArrayByteAlignment;
        }

        private static FeatureContractContext RequireDx12()
        {
            Assert.True(
                FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out string reason),
                $"DX12 is required for the Windows-qualified DXR 1.2 binding probe: {reason}");
            Assert.NotNull(context);
            return context;
        }
    }
}
#endif
