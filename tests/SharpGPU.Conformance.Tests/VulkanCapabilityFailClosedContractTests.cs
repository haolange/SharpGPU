using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class VulkanCapabilityFailClosedContractTests
{
    [Fact]
    public void MeshShader_PublicCapability_ReportsNativeProbeWhenExtensionMissing()
    {
        RHICapability mesh = VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(false);
        Assert.Equal(ERHICapabilityTier.Unavailable, mesh.Tier);
        Assert.Equal(ERHICapabilityStrategy.Unavailable, mesh.Strategy);
        Assert.Equal(ERHICapabilityProbeKind.NativeExtensionQuery, mesh.Provenance.Kind);
        Assert.Equal(VulkanMeshCapabilityFactory.ProbeSource, mesh.Provenance.Source);
        Assert.Equal(VulkanMeshCapabilityFactory.UnavailableReason, mesh.UnavailableReason);
        Assert.DoesNotContain(
            "factory lowering is not implemented",
            mesh.UnavailableReason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MeshShader_PublicCapability_IsAvailableWhenExtensionAndFactoryExist()
    {
        RHICapability mesh = VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(true);
        Assert.Equal(ERHICapabilityTier.Tier1, mesh.Tier);
        Assert.Equal(ERHICapabilityStrategy.NativeExtension, mesh.Strategy);
        Assert.Equal(VulkanMeshCapabilityFactory.ProbeSource, mesh.Provenance.Source);
        Assert.True(string.IsNullOrEmpty(mesh.UnavailableReason));
    }

    [Fact]
    public void MeshRasterPipeline_OrdinaryVertexPath_DoesNotRequireMeshCapability()
    {
        RHIRasterPipelineDescriptor descriptor = default;
        Assert.False(VulkanMeshCapabilityFactory.RequestsMeshPath(in descriptor));
        RHICapability unavailable = VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(false);
        VulkanMeshCapabilityFactory.RequireMeshRasterPipeline(
            in descriptor,
            unavailable,
            unavailable);
    }

    [Fact]
    public void MeshRasterPipeline_MeshletPath_FailClosedWithoutNativePipeline()
    {
        RHIRasterPipelineDescriptor descriptor = new()
        {
            PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
            {
                MeshletAssembler = new RHIMeshletAssemblerDescriptor(null, null),
            },
        };
        Assert.True(VulkanMeshCapabilityFactory.RequestsMeshPath(in descriptor));
        RHICapability unavailable = VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(false);
        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => VulkanMeshCapabilityFactory.RequireMeshRasterPipeline(
                in descriptor,
                unavailable,
                unavailable));
        Assert.Contains(
            VulkanMeshCapabilityFactory.UnavailableReason,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchMesh_UnavailableCapability_FailClosedBeforeNativeCall()
    {
        RHICapability unavailable = VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(false);
        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => VulkanMeshCommandPolicy.RequireDispatch(unavailable));
        Assert.Contains(
            VulkanMeshCapabilityFactory.UnavailableReason,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StorageQueue_UnavailableNativeGpuFileIo_FailClosedWithoutReturningQueue()
    {
        RHICapability unavailable = RHICapability.Unavailable(
            "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.",
            ERHICapabilityProbeKind.BackendContract,
            "Vulkan storage contract");
        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => VulkanStorageQueueFactoryPolicy.RequireNativeGpuFileIo(unavailable));
        Assert.Contains(
            VulkanStorageQueueFactoryPolicy.CapabilityName,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreateQuery_DispatchesCapabilityAndRejectsPendingWithoutNativePool()
    {
        RHISynchronizationCapabilities unavailable = CreateUnavailableSynchronization();
        Assert.Throws<NotSupportedException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                ERHIQueryType.Occlusion,
                unavailable));
        Assert.Throws<NotSupportedException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                ERHIQueryType.Statistics,
                unavailable));
        Assert.Throws<NotSupportedException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                ERHIQueryType.Timestamp,
                unavailable));
        Assert.Throws<NotSupportedException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                ERHIQueryType.TimestampTransfer,
                unavailable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                ERHIQueryType.Pending,
                unavailable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                (ERHIQueryType)255,
                unavailable));
    }

    [Fact]
    public void BarycentricCoordinates_RemainUnavailableWithDeviceCreateContractProvenance()
    {
        RHICapability barycentric = VulkanBarycentricCapabilityFactory.CreatePublicCapability();
        Assert.Equal(ERHICapabilityTier.Unavailable, barycentric.Tier);
        Assert.Equal(ERHICapabilityProbeKind.BackendContract, barycentric.Provenance.Kind);
        Assert.Equal(
            VulkanBarycentricCapabilityFactory.ProbeSource,
            barycentric.Provenance.Source);
        Assert.Equal(
            VulkanBarycentricCapabilityFactory.UnavailableReason,
            barycentric.UnavailableReason);
        Assert.DoesNotContain(
            "VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR.fragmentShaderBarycentric",
            barycentric.Provenance.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "hardware",
            barycentric.UnavailableReason,
            StringComparison.OrdinalIgnoreCase);
    }

#if SHARPGPU_ENABLE_DX12
    [Fact]
    public void AtomicUInt64_RequiresTypedAndGroupShared()
    {
        Assert.True(Dx12AtomicUInt64CapabilityFactory.IsAvailable(true, true, true));
        Assert.False(Dx12AtomicUInt64CapabilityFactory.IsAvailable(false, true, true));
        Assert.False(Dx12AtomicUInt64CapabilityFactory.IsAvailable(true, false, true));
        Assert.False(Dx12AtomicUInt64CapabilityFactory.IsAvailable(true, true, false));
        Assert.Equal(
            Dx12AtomicUInt64CapabilityFactory.QueryFailedReason,
            Dx12AtomicUInt64CapabilityFactory.CreateUnavailableReason(false, true, true));
        Assert.Contains(
            Dx12AtomicUInt64CapabilityFactory.TypedResourceField,
            Dx12AtomicUInt64CapabilityFactory.CreateUnavailableReason(true, false, true),
            StringComparison.Ordinal);
        Assert.Contains(
            Dx12AtomicUInt64CapabilityFactory.GroupSharedField,
            Dx12AtomicUInt64CapabilityFactory.CreateUnavailableReason(true, true, false),
            StringComparison.Ordinal);
    }
#endif

    private static RHISynchronizationCapabilities CreateUnavailableSynchronization()
    {
        RHICapability unavailable = RHICapability.Unavailable(
            "portable Vulkan query factory guard",
            ERHICapabilityProbeKind.BackendContract,
            "portable Vulkan query factory");
        return new RHISynchronizationCapabilities(
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable,
            unavailable);
    }
}
