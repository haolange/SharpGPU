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
    public void PublicContract_ShouldExportIndirectCommandBufferFamiliesWithExactBackends()
    {
        Type[] exportedTypes = typeof(RHIDevice).Assembly.GetExportedTypes();
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIComputeIndirectCommandBuffer));
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIRasterIndirectCommandBuffer));
        Assert.Contains(
            exportedTypes,
            type => type.Name == nameof(RHIRayTracingIndirectCommandBuffer));

        MethodInfo[] deviceFactories = typeof(RHIDevice).GetMethods(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly);
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateComputeIndirectCommandBuffer));
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateRasterIndirectCommandBuffer));
        Assert.Contains(
            deviceFactories,
            method => method.Name == nameof(RHIDevice.CreateRayTracingIndirectCommandBuffer));

        Assert.Equal(
            typeof(RHICapability),
            typeof(RHIIndirectCommandBufferCapabilities)
                .GetProperty(nameof(RHIIndirectCommandBufferCapabilities.Execution))!
                .PropertyType);

        Type[] implementationTypes = typeof(RHIDevice).Assembly.GetTypes();
        Assert.Contains(implementationTypes, type => type.Name == "Dx12ComputeIndirectCommandBuffer");
        Assert.Contains(implementationTypes, type => type.Name == "MetalComputeIndirectCommandBuffer");
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
        Assert.Contains(nameof(RHIDevice.CreateComputeIndirectCommandBuffer), factoryNames);

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
        Assert.DoesNotContain(
            implementationTypes,
            type =>
                type.Name.StartsWith("Vulkan", StringComparison.Ordinal) &&
                (type.Name.Contains("MLPipeline", StringComparison.Ordinal) ||
                 type.Name.Contains("MLBinding", StringComparison.Ordinal) ||
                 type.Name.Contains("WorkGraph", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            implementationTypes,
            type =>
                type.Name.StartsWith("Metal", StringComparison.Ordinal) &&
                type.Name.Contains("WorkGraph", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductSources_ShouldNotRetainAvailableWithoutImplementationPlaceholders()
    {
        string sharpGpuRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Runtime",
                "Graphics",
                "SharpGPU"));
        Assert.True(
            Directory.Exists(sharpGpuRoot),
            $"SharpGPU root not found at '{sharpGpuRoot}'.");

        string[] hits = Directory
            .EnumerateFiles(sharpGpuRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadAllLines(path).Select(line => (path, line)))
            .Where(tuple =>
                tuple.line.Contains("available without a", StringComparison.OrdinalIgnoreCase) ||
                tuple.line.Contains("available without an", StringComparison.OrdinalIgnoreCase))
            .Select(tuple => $"{Path.GetFileName(tuple.path)}: {tuple.line.Trim()}")
            .ToArray();

        Assert.True(
            hits.Length == 0,
            "Forbidden forever-throw placeholder copy remains:\n" + string.Join("\n", hits));
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
            MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Task));
        Assert.Equal(
            0UL,
            MetalUtility.ConvertToMetal4Stages(ERHISyncStageMask.Mesh));
        Assert.Equal(
            1UL << 30,
            MetalUtility.ConvertToMetal4Stages(
                ERHISyncStageMask.MachineLearning));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetalUtility.ConvertToMetal4Stages(
                (ERHISyncStageMask)(1UL << 63)));

        ulong supportedGraphicsStages =
            MetalUtility.ConvertToMetal4Stages(
                ERHISyncStageMask.AllGraphics);
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
            nameof(RHIDevice.CreateComputeIndirectCommandBuffer),
            nameof(RHIDevice.CreateRasterIndirectCommandBuffer),
            nameof(RHIDevice.CreateRayTracingIndirectCommandBuffer),
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
