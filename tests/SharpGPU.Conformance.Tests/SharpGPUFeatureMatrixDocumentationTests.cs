using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUFeatureMatrixDocumentationTests
{
    [Fact]
    public void FeatureMatrix_ShouldContainPublicContractRows()
    {
        string markdown = LoadFeatureMatrix();
        string[] requiredRows =
        {
            "TimestampQueries",
            "OcclusionQueries",
            "PipelineStatisticsQueries",
            "MachineLearning",
            "IndirectCommandBuffer",
            "Raytracing",
            "OpacityMicromap",
            "Motion",
            "MeshShading",
            "DescriptorIndexing",
            "StorageQueue",
            "PipelineCache",
            "WorkGraph",
            "FramebufferReadWrite",
        };

        foreach (string row in requiredRows)
        {
            Assert.Contains(row, markdown, StringComparison.Ordinal);
        }
    }

    private static string LoadFeatureMatrix()
    {
        string? repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot != null)
        {
            string path = Path.Combine(repositoryRoot, "docs", "SharpGPU", "FeatureMatrix.md");
            Assert.True(File.Exists(path), $"Missing SharpGPU feature matrix: {path}");
            return File.ReadAllText(path);
        }

        using Stream? resource = typeof(RHIInstance).Assembly.GetManifestResourceStream("SharpGPU.FeatureMatrix.md");
        Assert.NotNull(resource);
        using StreamReader reader = new(resource!);
        return reader.ReadToEnd();
    }

    private static string? TryFindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "InfinityBrowser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
