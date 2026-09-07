// Copyright (c) CGBull. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpGPU.Conformance.Tests;

#if SHARPGPU_SOURCE_LAYOUT_TESTS
public sealed class RhiDomainMirrorContractTests
{
    private static string ResolveSharpGpuRoot()
    {
        string? configuredRoot = Environment.GetEnvironmentVariable("INFINITYSTACK_SHARPGPU_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string configuredSourceRoot = Path.Combine(configuredRoot, "src", "SharpGPU");
            if (Directory.Exists(Path.Combine(configuredSourceRoot, "Abstract")))
            {
                return Path.GetFullPath(configuredSourceRoot);
            }
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string[] candidates =
            [
                Path.Combine(directory.FullName, "src", "SharpGPU"),
                Path.Combine(directory.FullName, "Engine", "Source", "Runtime", "Graphics", "SharpGPU"),
            ];
            string? sharpGpuRoot = candidates.FirstOrDefault(
                candidate => Directory.Exists(Path.Combine(candidate, "Abstract")));
            if (sharpGpuRoot != null)
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
            // Attachment shader ABI is a device query contract implemented by
            // each backend device, not a standalone backend domain type.
            .Where(name => !string.Equals(name, "AttachmentShaderAbi", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        HashSet<string> abstractDomains = new(rhiDomains, StringComparer.Ordinal);

        foreach (string backend in new[] { "Dx12", "Metal", "Vulkan" })
        {
            string backendDir = Path.Combine(sharpGpuRoot, backend);
            string[] backendDomains = Directory
                .GetFiles(backendDir, $"{backend}*.cs")
                .Select(path => Path.GetFileNameWithoutExtension(path).Substring(backend.Length))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                rhiDomains,
                backendDomains
                    .Where(abstractDomains.Contains)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
            string[] allowedBackendOnlyDomains = backend == "Vulkan"
                ? ["OpacityMicromap", "RayTracingMotionNative"]
                : Array.Empty<string>();
            Assert.All(
                backendDomains.Where(domain => !abstractDomains.Contains(domain)),
                domain => Assert.Contains(domain, allowedBackendOnlyDomains));
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
#endif
