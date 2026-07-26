// Copyright (c) CGBull. All rights reserved.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class RhiDomainMirrorContractTests
{
    private static string ResolveSharpGpuRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string sharpGpuRoot = Path.Combine(
                directory.FullName,
                "Engine",
                "Source",
                "Runtime",
                "Graphics",
                "SharpGPU");
            if (Directory.Exists(Path.Combine(sharpGpuRoot, "Abstract")))
            {
                return sharpGpuRoot;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Failed to locate SharpGPU source root from test output directory.");
    }

    [Fact]
    public void RhiDomainMirror_ShouldAlignBackendDomainFilesWithAbstract()
    {
        string sharpGpuRoot = ResolveSharpGpuRoot();
        string abstractDir = Path.Combine(sharpGpuRoot, "Abstract");

        string[] rhiDomains = Directory
            .GetFiles(abstractDir, "RHI*.cs")
            .Select(path => Path.GetFileNameWithoutExtension(path).Substring(3))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        foreach (string backend in new[] { "Dx12", "Metal", "Vulkan" })
        {
            string backendDir = Path.Combine(sharpGpuRoot, backend);
            string[] backendDomains = Directory
                .GetFiles(backendDir, $"{backend}*.cs")
                .Select(path => Path.GetFileNameWithoutExtension(path).Substring(backend.Length))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(rhiDomains, backendDomains);
        }
    }

    [Fact]
    public void RhiDomainMirror_ShouldNotRetainStandaloneRasterPassSourceFiles()
    {
        string sharpGpuRoot = ResolveSharpGpuRoot();
        string[] rasterPassFiles = Directory
            .GetFiles(sharpGpuRoot, "*RasterPass*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sharpGpuRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            rasterPassFiles.Length == 0,
            "Standalone RasterPass domain files remain under SharpGPU:\n"
                + string.Join("\n", rasterPassFiles));
    }

    [Fact]
    public void RhiDomainMirror_ShouldNotRetainInternalDirectory()
    {
        string sharpGpuRoot = ResolveSharpGpuRoot();
        string internalDir = Path.Combine(sharpGpuRoot, "Internal");
        Assert.False(
            Directory.Exists(internalDir),
            $"Forbidden SharpGPU/Internal/ directory remains at '{internalDir}'.");
    }
}
