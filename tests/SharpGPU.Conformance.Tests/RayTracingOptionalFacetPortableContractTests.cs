using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class RayTracingOptionalFacetPortableContractTests
{
    [Fact]
    public void WithdrawnP3AndP8Symbols_AreAbsentFromPublicSurface()
    {
        Assert.Null(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "OpacityMicromap"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "ShaderExecutionReordering"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "Motion"));
        Assert.NotNull(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "Pipeline"));
        Assert.NotNull(GetPublicInstanceProperty(typeof(RHIRayTracingCapabilities), "Inline"));

        Type[] exported = typeof(RHIDevice).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalFence64");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalFence64CreateDescriptor");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalFence64ImportDescriptor");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalFence64Export");
        Assert.DoesNotContain(exported, static type => type.Name == "ERHIExternalFence64Direction");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIMultiGpuCapabilities");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalNtHandle");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalResourceExport");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalBufferImportDescriptor");
        Assert.DoesNotContain(exported, static type => type.Name == "RHIExternalTextureImportDescriptor");
        Assert.DoesNotContain(exported, static type => type.Name == "ERHIExternalHandleKind");

        Assert.Null(typeof(RHIDevice).GetMethod("CreateExternalFence64"));
        Assert.Null(typeof(RHIDevice).GetMethod("ImportExternalFence64"));
        Assert.Null(typeof(RHIDevice).GetMethod("ExportExternalFence64"));
        Assert.Null(typeof(RHIDevice).GetMethod("ExportBufferNtHandle"));
        Assert.Null(typeof(RHIDevice).GetMethod("ExportTextureNtHandle"));
        Assert.Null(typeof(RHIDevice).GetMethod("ImportBufferNtHandle"));
        Assert.Null(typeof(RHIDevice).GetMethod("ImportTextureNtHandle"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHIDeviceCapabilities), "MultiGpu"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHISynchronizationCapabilities), "ExternalFence64"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHIMemoryCapabilities), "ExternalImport"));
        Assert.Null(GetPublicInstanceProperty(typeof(RHIMemoryCapabilities), "ExternalExport"));
        Assert.False(Enum.TryParse("MultiGpuNodeMask", ignoreCase: false, out ERHICapabilityLimitKind _));
    }

    private static PropertyInfo? GetPublicInstanceProperty(Type type, string name)
    {
        return type.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }
}
