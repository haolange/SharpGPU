using System;
using System.Linq;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class IndirectCommandLayoutBoundaryTests
{
    [Fact]
    public void IndirectCommandLayoutAndWorkGraph_ShouldRemainDistinctPublicTypes()
    {
        Type[] exported = typeof(RHIDevice).Assembly.GetExportedTypes();
        Assert.Contains(exported, type => type.Name == nameof(RHIIndirectCommandLayout));
        Assert.Contains(exported, type => type.Name == nameof(RHIWorkGraphPipeline));
        Assert.Contains(exported, type => type.Name == nameof(RHIWorkGraphCapabilities));
        Assert.NotEqual(
            typeof(RHIIndirectCommandLayout),
            typeof(RHIWorkGraphPipeline));
    }

    [Fact]
    public void PublicApi_ShouldNotExposeDeviceGeneratedCommandsOrPreprocessBuffer()
    {
        Type[] publicTypes = typeof(RHIDevice).Assembly.GetExportedTypes();
        string[] forbidden =
        [
            "DeviceGeneratedCommands",
            "PreprocessBuffer",
            "DeviceGeneratedCommand",
        ];

        foreach (Type type in publicTypes)
        {
            Assert.DoesNotContain(
                forbidden,
                name => type.Name.Contains(name, StringComparison.Ordinal));
            foreach (MemberInfo member in type.GetMembers(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbidden,
                    name => member.Name.Contains(name, StringComparison.Ordinal));
            }
        }

        Assert.Contains(
            typeof(RHIDevice).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(RHIDevice.CreateIndirectCommandLayout));
        Assert.Contains(
            typeof(RHIDevice).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(RHIDevice.CreateWorkGraphPipeline));
    }
}
