using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class MeshShadingPortableContractTests
{
    [Fact]
    public void MeshCapabilities_ShouldExposeMeshShaderAndTaskShaderAndRejectRetiredShader()
    {
        Assert.Null(
            typeof(RHIMeshCapabilities).GetProperty(
                "Shader",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        AssertDirectMeshCapability(nameof(RHIMeshCapabilities.MeshShader));
        AssertDirectMeshCapability(nameof(RHIMeshCapabilities.TaskShader));
    }

    [Fact]
    public void CapabilityLimitKind_ShouldDefineMeshLimitKinds()
    {
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxOutputVertices));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxOutputPrimitives));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxPayloadBytes));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxWorkGroupSizeX));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxWorkGroupSizeY));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxWorkGroupSizeZ));
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.MeshMaxPerPrimitiveAttributes));
    }

    [Fact]
    public void CreateMeshPipeline_UnavailableMeshShader_ThrowsNotSupportedException()
    {
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            bool backendSupported = RHIInstance.IsBackendSupported(backend, out string supportedReason);
            if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out string createReason))
            {
                if (OperatingSystem.IsWindows() &&
                    backend == ERHIBackend.DirectX12 &&
                    backendSupported)
                {
                    Assert.Fail(
                        $"DirectX12 was supported but TryCreateInstance failed: {supportedReason} {createReason}");
                }

                continue;
            }

            using (instance)
            {
                if (instance.DeviceCount <= 0)
                {
                    continue;
                }

                RHIDevice device = instance.GetDevice(0);
                if (device.Capabilities.Mesh.MeshShader.Tier != ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(
                        device.Capabilities.Mesh.MeshShader.Provenance.Source));
                    continue;
                }

                using RHIPipelineLayout pipelineLayout = CreateEmptyPipelineLayout(device);
                NotSupportedException exception = Assert.Throws<NotSupportedException>(
                    () => device.CreateRasterPipeline(
                        CreateMeshPipelineDescriptor(pipelineLayout, meshFunction: null)));
                Assert.False(string.IsNullOrWhiteSpace(exception.Message));
            }
        }
    }

    [Fact]
    public void Dx12_WindowsHost_MeshShaderMustBeAvailableAndMissingMeshFunctionFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        bool backendSupported = RHIInstance.IsBackendSupported(
            ERHIBackend.DirectX12,
            out string supportedReason);
        if (!FeatureContractContext.TryCreateDx12Instance(out RHIInstance? instance, out string createReason))
        {
            if (backendSupported)
            {
                Assert.Fail(
                    $"DirectX12 was supported but device create failed: {supportedReason} {createReason}");
            }

            return;
        }

        using (instance)
        {
            Assert.True(instance.DeviceCount > 0, "DX12 instance enumerated no devices.");
            RHIDevice device = instance.GetDevice(0);
            Assert.NotEqual(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.Mesh.MeshShader.Tier);
            Assert.NotEqual(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.Mesh.TaskShader.Tier);
            Assert.Equal(
                device.Capabilities.Mesh.MeshShader.Tier,
                device.Capabilities.IndirectCommandBuffer.Tokens.DispatchMesh.Tier);

            using RHIPipelineLayout pipelineLayout = CreateEmptyPipelineLayout(device);
            Assert.ThrowsAny<Exception>(
                () => device.CreateRasterPipeline(
                    CreateMeshPipelineDescriptor(pipelineLayout, meshFunction: null)));
        }
    }

    private static void AssertDirectMeshCapability(string propertyName)
    {
        PropertyInfo? property = typeof(RHIMeshCapabilities).GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(typeof(RHICapability), property.PropertyType);
        Assert.Equal(typeof(RHIMeshCapabilities), property.DeclaringType);
        Assert.True(property.CanRead);
        Assert.False(property.CanWrite);
    }

    private static RHIPipelineLayout CreateEmptyPipelineLayout(RHIDevice device)
    {
        return device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
        {
            bLocalSignature = false,
            bUseVertexLayout = false,
            PushConstantSize = 0,
            BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
        });
    }

    private static RHIRasterPipelineDescriptor CreateMeshPipelineDescriptor(
        RHIPipelineLayout pipelineLayout,
        RHIFunction? meshFunction)
    {
        RHIStencilStateDescriptor keepStencilFace = new()
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilPassOp = ERHIStencilOp.Keep,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };
        return new RHIRasterPipelineDescriptor
        {
            SampleCount = ERHISampleCount.None,
            ColorFormats = Array.Empty<ERHIPixelFormat>(),
            PipelineLayout = pipelineLayout,
            RenderState = new RHIRenderStateDescriptor
            {
                DepthStencilState = new RHIDepthStencilStateDescriptor
                {
                    ComparisonMode = ERHIComparisonMode.Always,
                    FrontFace = keepStencilFace,
                    BackFace = keepStencilFace,
                },
            },
            PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
            {
                MeshletAssembler = new RHIMeshletAssemblerDescriptor(null, meshFunction),
            },
        };
    }
}
