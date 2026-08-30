using System;
using System.IO;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUFeatureMatrixDocumentationTests
{
    [Fact]
    public void FeatureMatrix_ShouldContainPublicContractRows()
    {
        string path = Path.Combine(FindRepositoryRoot(), "docs", "SharpGPU", "FeatureMatrix.md");
        Assert.True(File.Exists(path), $"Missing SharpGPU feature matrix: {path}");

        string markdown = File.ReadAllText(path);
        string[] requiredRows =
        {
            "TimestampQueries",
            "OcclusionQueries",
            "PipelineStatisticsQueries",
            "MachineLearning",
            "IndirectCommandBuffer",
            "Raytracing",
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

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("Failed to locate repository root from test output directory.");
    }
}
