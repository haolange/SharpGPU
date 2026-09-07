// Copyright (c) CGBull. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class W11AdvancedSurfaceContractTests
{
    [Fact]
    public void PublicContract_ShouldExposeLayoutStreamIndirectExecutionWithExactBackends()
    {
        Type[] exportedTypes = typeof(RHIDevice).Assembly.GetExportedTypes();
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIIndirectCommandLayout));
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIIndirectCommandLayout));
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIIndirectCommandLayout));

        MethodInfo[] deviceFactories = typeof(RHIDevice).GetMethods(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly);
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateIndirectCommandLayout));
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateIndirectCommandLayout));
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateIndirectCommandLayout));

        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIIndirectCommandBufferCapabilities)
                .GetProperty(nameof(RHIIndirectCommandBufferCapabilities.Execution))!
                .PropertyType);

        Type[] implementationTypes = typeof(RHIDevice).Assembly.GetTypes();
        Assert.Contains(implementationTypes, type => type.Name == "Dx12IndirectCommandLayout");
        Assert.Contains(implementationTypes, type => type.Name == "MetalIndirectCommandLayout");
        Assert.DoesNotContain(
            implementationTypes,
            type =>
                type.Name.StartsWith("Vulkan", StringComparison.Ordinal) &&
                type.Name.Contains("IndirectCommandBuffer", StringComparison.Ordinal));
    }

    [Fact]
    public void PublicContract_ShouldRetainOnlyAdvancedFamiliesWithAnExactBackend()
    {
        string[] factoryNames = typeof(RHIDevice)
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ToArray();

        Assert.Contains(nameof(RHIDevice.CreateMLPipeline), factoryNames);
        Assert.Contains(nameof(RHIDevice.CreateMLBindingTable), factoryNames);
        Assert.Contains(nameof(RHIDevice.CreateTensor), factoryNames);
        Assert.DoesNotContain("CreateMLProgram", factoryNames);
        Assert.Contains(nameof(RHIDevice.CreateWorkGraphPipeline), factoryNames);
        Assert.Contains(nameof(RHIDevice.CreateIndirectCommandLayout), factoryNames);

        Type[] implementationTypes = typeof(RHIDevice).Assembly.GetTypes();
        Assert.Contains(
            implementationTypes,
            type => type.Name == "Dx12MLPipeline");
        Assert.Contains(
            implementationTypes,
            type => type.Name == "Dx12WorkGraphPipeline");
        Assert.Contains(
            implementationTypes,
            type => type.Name == "MetalMLPipeline");
        // ADR-0054: Vulkan mirrors ML/Tensor/StorageQueue domain types with
        // NotSupported internals; WorkGraph remains Dx12-only.
        Assert.Contains(
            implementationTypes,
            type => type.Name == "VulkanMLPipeline");
        Assert.Contains(
            implementationTypes,
            type => type.Name == "VulkanMLBindingTable");
        Assert.Contains(
            implementationTypes,
            type => type.Name == "VulkanTensor");
        Assert.Contains(
            implementationTypes,
            type => type.Name == "VulkanStorageQueue");
        Assert.DoesNotContain(
            implementationTypes,
            type =>
                type.Name.StartsWith("Vulkan", StringComparison.Ordinal) &&
                type.Name.Contains("WorkGraph", StringComparison.Ordinal));
        Assert.DoesNotContain(
            implementationTypes,
            type =>
                type.Name.StartsWith("Metal", StringComparison.Ordinal) &&
                type.Name.Contains("WorkGraph", StringComparison.Ordinal));
    }

#if SHARPGPU_SOURCE_LAYOUT_TESTS
    [Fact]
    public void ProductSources_ShouldNotRetainAvailableWithoutImplementationPlaceholders()
    {
        string sharpGpuRoot = Path.GetFullPath(
            ResolveSharpGpuRoot());
        Assert.True(
            Directory.Exists(sharpGpuRoot),
            $"SharpGPU root not found at '{sharpGpuRoot}'.");

        string[] hits = Directory
            .EnumerateFiles(sharpGpuRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path).Select(line => (path, line)))
            .Where(tuple =>
                tuple.line.Contains("available without implementation", StringComparison.OrdinalIgnoreCase) ||
                tuple.line.Contains("available without an implementation", StringComparison.OrdinalIgnoreCase))
            .Select(tuple => $"{Path.GetFileName(tuple.path)}: {tuple.line.Trim()}")
            .ToArray();

        Assert.True(
            hits.Length == 0,
            "Forbidden forever-throw placeholder copy remains:\n" + string.Join("\n", hits));
    }
#endif

    private static string ResolveSharpGpuRoot()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("INFINITYSTACK_SHARPGPU_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string configuredSourceRoot = Path.Combine(configuredRoot, "src", "SharpGPU");
            if (Directory.Exists(configuredSourceRoot))
            {
                return Path.GetFullPath(configuredSourceRoot);
            }
        }

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent)
        {
            string[] candidates =
            [
                Path.Combine(directory.FullName, "src", "SharpGPU"),
                Path.Combine(directory.FullName, "Engine", "Source", "Runtime", "Graphics", "SharpGPU"),
            ];
            string? root = candidates.FirstOrDefault(Directory.Exists);
            if (root != null)
            {
                return root;
            }
        }

        throw new InvalidOperationException("Failed to locate SharpGPU source root from test output directory.");
    }

    [Fact]
    public void QueryContract_ShouldExposeTypedStatusAndReadOnlyResults()
    {
        MethodInfo resolve = typeof(RHIQuery).GetMethod(
            nameof(RHIQuery.ResolveData),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "RHIQuery.ResolveData was not found.");
        Assert.Equal(typeof(ERHIQueryResultStatus), resolve.ReturnType);

        PropertyInfo results = typeof(RHIQuery).GetProperty(
            nameof(RHIQuery.Results),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "RHIQuery.Results was not found.");
        Assert.Equal(typeof(ReadOnlyMemory<ulong>), results.PropertyType);
        Assert.False(results.CanWrite);
    }

    [Fact]
    public void CapabilityContract_ShouldKeepIndirectAndBindingFactsTyped()
    {
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIRasterCapabilities)
                .GetProperty(nameof(RHIRasterCapabilities.DrawIndirect))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIRasterCapabilities)
                .GetProperty(nameof(RHIRasterCapabilities.MultiDrawIndirect))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIBindingCapabilities)
                .GetProperty(nameof(RHIBindingCapabilities.DescriptorIndexing))!
                .PropertyType);

        RHICapability capability = RHICapability.Available(
            ERHICapabilityTier.Tier1,
            ERHICapabilityStrategy.CoreApi,
            ERHICapabilityProbeKind.NativeFeatureQuery,
            "test native feature query",
            new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumRootConstantBytes,
                    128),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.RootConstantAlignmentBytes,
                    4)));

        Assert.Equal(
            ERHICapabilityProbeKind.NativeFeatureQuery,
            capability.Provenance.Kind);
        Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
        Assert.True(
            capability.Limits.TryGetValue(
                ERHICapabilityLimitKind.MaximumRootConstantBytes,
                out ulong maximumBytes));
        Assert.Equal(128UL, maximumBytes);
    }

    [Fact]
    public void MetalAdvancedStageConversion_ShouldFailClosed()
    {
        // Task/Mesh remain unavailable on Metal — strip to zero rather than throw
        // so aggregates that embed those bits can still lower the supported remainder.
        // MachineLearning maps to MTLStageMachineLearning (bit 30); do not strip it.
        Assert.Equal(
            0UL,
            MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Task));
        Assert.Equal(
            0UL,
            MetalUtility.ConvertToMetal4Stages(ERHIStageMask.Mesh));
        Assert.Equal(
            1UL << 30,
            MetalUtility.ConvertToMetal4Stages(
                ERHIStageMask.MachineLearning));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetalUtility.ConvertToMetal4Stages(
                (ERHIStageMask)(1UL << 63)));

        ulong supportedGraphicsStages =
            MetalUtility.ConvertToMetal4Stages(
                ERHIStageMask.AllGraphics);
        Assert.Equal(
            (1UL << 0) | (1UL << 1) | (1UL << 27),
            supportedGraphicsStages);
    }

    [Fact]
    public void AdvancedFactories_ShouldBeExplicitPerBackendAndCapabilityTyped()
    {
        PropertyInfo memoryRequirements =
            typeof(RHIWorkGraphPipeline).GetProperty(
                nameof(RHIWorkGraphPipeline.MemoryRequirements),
                BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "RHIWorkGraphPipeline.MemoryRequirements was not found.");
        Assert.True(memoryRequirements.GetMethod!.IsAbstract);

        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIMachineLearningCapabilities)
                .GetProperty(
                    nameof(RHIMachineLearningCapabilities.Execution))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.Execution))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.BroadcastNodes))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.ThreadNodes))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.Recursion))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.MeshNodes))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.GpuInput))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.BackingMemory))!
                .PropertyType);
        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIWorkGraphCapabilities)
                .GetProperty(nameof(RHIWorkGraphCapabilities.EntryRecords))!
                .PropertyType);

        Assembly assembly = typeof(RHIDevice).Assembly;
        string[] backendTypeNames =
        [
            "SharpGPU.Dx12Device",
            "SharpGPU.VulkanDevice",
            "SharpGPU.MetalDevice",
        ];
        string[] advancedFactoryNames =
        [
            nameof(RHIDevice.CreateMLPipeline),
            nameof(RHIDevice.CreateMLBindingTable),
            nameof(RHIDevice.CreateTensor),
            nameof(RHIDevice.CreateWorkGraphPipeline),
            nameof(RHIDevice.CreateIndirectCommandLayout),
            nameof(RHIDevice.CreateIndirectCommandLayout),
            nameof(RHIDevice.CreateIndirectCommandLayout),
        ];

        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIIndirectCommandBufferCapabilities)
                .GetProperty(nameof(RHIIndirectCommandBufferCapabilities.Execution))!
                .PropertyType);

        foreach (string backendTypeName in backendTypeNames)
        {
            Type backendType = assembly.GetType(
                backendTypeName,
                throwOnError: true)!;
            foreach (string factoryName in advancedFactoryNames)
            {
                MethodInfo? factory = backendType.GetMethod(
                    factoryName,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly);
                Assert.NotNull(factory);
                Assert.Equal(backendType, factory!.DeclaringType);
            }
        }
    }
    [Fact]
    public void IndirectArgumentValidation_ShouldRejectUsageAlignmentRangeAndDisposedState()
    {
        using FakeBuffer valid = new(
            64,
            ERHIBufferUsage.IndirectBuffer);
        RHIIndirectArgumentValidator.ValidateDispatch(
            valid,
            0,
            nameof(valid),
            "test dispatch");
        RHIIndirectArgumentValidator.ValidateDraw(
            valid,
            0,
            3,
            indexed: false,
            nameof(valid),
            "test draw");

        using FakeBuffer wrongUsage = new(
            64,
            ERHIBufferUsage.ShaderResource);
        Assert.Throws<ArgumentException>(
            () => RHIIndirectArgumentValidator.ValidateDispatch(
                wrongUsage,
                0,
                nameof(wrongUsage),
                "test dispatch"));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RHIIndirectArgumentValidator.ValidateDispatch(
                valid,
                2,
                nameof(valid),
                "test dispatch"));

        using FakeBuffer tooSmall = new(
            39,
            ERHIBufferUsage.IndirectBuffer);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RHIIndirectArgumentValidator.ValidateDraw(
                tooSmall,
                0,
                2,
                indexed: true,
                nameof(tooSmall),
                "test indexed draw"));

        FakeBuffer disposed = new(
            64,
            ERHIBufferUsage.IndirectBuffer);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => RHIIndirectArgumentValidator.ValidateDispatch(
                disposed,
                0,
                nameof(disposed),
                "test dispatch"));
    }

    private sealed class FakeBuffer : RHIBuffer
    {
        internal FakeBuffer(
            int byteSize,
            ERHIBufferUsage usage)
        {
            m_Descriptor = new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = usage,
            };
        }

        public override IntPtr Map(
            in uint readBegin,
            in uint readEnd)
        {
            throw new NotSupportedException();
        }

        public override void UnMap(
            in uint writeBegin,
            in uint writeEnd)
        {
            throw new NotSupportedException();
        }

        public override RHIBufferView CreateBufferView(
            in RHIBufferViewDescriptor descriptor)
        {
            throw new NotSupportedException();
        }
    }
}
