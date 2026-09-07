using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class MlArchitectureContractTests
    {
        [Fact]
        public void Dx12MlDescriptorRollback_ShouldRemainStateBasedAndIdempotent()
        {
            // Descriptor rollback is owned by the GPU backend.
            string source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "SharpGPU",
                "Dx12",
                "Dx12Pipeline.cs"));

            Assert.DoesNotContain("createdExecutionTables", source, StringComparison.Ordinal);
            Assert.Equal(
                2,
                Regex.Matches(
                    source,
                    @"^\s*ReleaseExecutionBindingsAndDescriptors\(\);\s*$",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant).Count);
            Assert.Contains("for (int i = 0; i < m_ExecutionDescriptorCounts.Length; ++i)", source, StringComparison.Ordinal);
            Assert.True(
                source.Contains("m_ExecutionBindingTables[i] = null!;", StringComparison.Ordinal)
                || source.Contains("m_ExecutionBindingTables[i] = null;", StringComparison.Ordinal),
                "Execution binding tables must be cleared during descriptor rollback.");
            Assert.Contains("m_ExecutionDescriptorCounts[i] = 0;", source, StringComparison.Ordinal);
            Assert.Equal(
                2,
                Regex.Matches(
                    source,
                    @"^\s*m_InitializerDescriptorCount = 0;\s*$",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant).Count);
        }

        [Fact]
        public void RuntimeSource_ShouldContainNoRetiredNeuralContracts()
        {
            (string Name, string Pattern)[] retiredContracts =
            [
                ("ENeuralOpKind", @"\bENeuralOpKind\b"),
                ("ENhiOperationKind", @"\bENhiOperationKind\b"),
                ("OpAttribute", @"\bOpAttribute\b"),
                ("numeric artifact magic", @"\bSNHIART[0-9]+\b"),
                ("CreateGemmAddRelu", @"\bCreateGemmAddRelu\b"),
                ("Dx12MLProgramKind", @"\bDx12MLProgramKind\b"),
                ("IntermediateTensorDescriptor", @"\bIntermediateTensorDescriptor\b"),
                ("SharpGpu ML route", @"\b(?:SharpGpuMlCompilePolicy|SharpGpuMlExecutable|SharpGpuMlLowerer)\b"),
                ("SharpGpu route policy", @"\b(?:ESharpGpuEngineMode|ForceGeneral|ForceCompute)\b"),
            ];
            foreach (string path in Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src", "SharpGPU"), "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);
                foreach ((string name, string pattern) in retiredContracts)
                {
                    Assert.False(Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant), $"Retired {name} in {path}.");
                }
            }
        }

        private static string FindRepositoryRoot()
        {
            string? configuredRoot = Environment.GetEnvironmentVariable("INFINITYSTACK_SHARPGPU_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot)
                && File.Exists(Path.Combine(configuredRoot, "SharpGPU.product.props")))
            {
                return Path.GetFullPath(configuredRoot);
            }

            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SharpGPU.product.props"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("SharpGPU source checkout was not found.");
        }
    }
}
