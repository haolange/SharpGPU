using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpGPU;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if SHARPGPU_ENABLE_DX12
using Vortice.Direct3D12;
#endif
using Xunit;
using Xunit.Abstractions;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUFeatureReportTests
{
    private readonly ITestOutputHelper m_Output;

    public SharpGPUFeatureReportTests(ITestOutputHelper output)
    {
        m_Output = output;
    }

    [Fact]
    public void FeatureReport_ShouldWriteJson()
    {
        List<SharpGPUFeatureReport> reports = new();

        foreach (ERHIBackend backend in new[] { ERHIBackend.DirectX12, ERHIBackend.Vulkan, ERHIBackend.Metal })
        {
            if (!RHIInstance.IsBackendSupported(backend, out string reason))
            {
                reports.Add(SharpGPUFeatureReport.BackendUnavailable(backend, reason));
                continue;
            }

            try
            {
                using RHIInstance? instance = RHIInstance.Create(new RHIInstanceDescriptor
                {
                    Backend = backend,
                    EnableDebugLayer = false,
                    EnableValidatior = false,
                    ComputeQueueRequestCount = 0,
                    TransferQueueRequestCount = 0,
                    GraphicsQueueRequestCount = 1,
                });

                if (instance == null)
                {
                    reports.Add(SharpGPUFeatureReport.BackendUnavailable(backend, "RHIInstance.Create returned null."));
                    continue;
                }

                for (int deviceIndex = 0; deviceIndex < instance.DeviceCount; ++deviceIndex)
                {
                    reports.Add(SharpGPUFeatureReport.FromDevice(backend, deviceIndex, instance.GetDevice(deviceIndex)));
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or DllNotFoundException or InvalidOperationException)
            {
                reports.Add(SharpGPUFeatureReport.BackendUnavailable(backend, ex.Message));
            }
        }

        Assert.NotEmpty(reports);
        SharpGPUFeatureReportDocument document = new(SharpGPUEnvironmentReport.Capture(), reports);
        string path = ArtifactPath.Resolve(GetFeatureReportFileName());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions.Indented));
        m_Output.WriteLine(path);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(reports, report => report.Backend == ERHIBackend.DirectX12.ToString() && report.DeviceIndex >= 0);
        }
    }

    private static string GetFeatureReportFileName()
    {
        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "feature-report-macos-arm64.json"
                : "feature-report-macos-x64.json";
        }

        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "feature-report-win-x64.json"
                : $"feature-report-win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}.json";
        }

        return $"feature-report-{RuntimeInformation.RuntimeIdentifier}.json";
    }
}

public sealed class UnsupportedBackendContractTests
{
    [Theory]
    [InlineData(ERHIBackend.Vulkan)]
    [InlineData(ERHIBackend.Metal)]
    public void WorkGraph_UnsupportedBackend_ShouldReportFalseAndThrow(ERHIBackend backend)
    {
        if (!RHIInstance.IsBackendSupported(backend, out _))
        {
            return;
        }

        using RHIInstance? instance = TryCreateInstance(backend);
        if (instance == null)
        {
            return;
        }

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            Assert.False(device.Feature?.IsWorkgraphSupported == true, $"{backend} unexpectedly reports WorkGraph support on {device.Name}.");
            Assert.Throws<NotSupportedException>(() => device.CreateWorkGraphPipeline(default));
        }
    }

    private static RHIInstance? TryCreateInstance(ERHIBackend backend)
    {
        try
        {
            return RHIInstance.Create(new RHIInstanceDescriptor
            {
                Backend = backend,
                EnableDebugLayer = false,
                EnableValidatior = false,
                ComputeQueueRequestCount = 0,
                TransferQueueRequestCount = 0,
                GraphicsQueueRequestCount = 1,
            });
        }
        catch (Exception ex) when (ex is NotSupportedException or DllNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed record SharpGPUFeatureReportDocument(
    SharpGPUEnvironmentReport Environment,
    IReadOnlyList<SharpGPUFeatureReport> Backends);

internal sealed record SharpGPUEnvironmentReport(
    string RuntimeIdentifier,
    string OSDescription,
    string OSArchitecture,
    string ProcessArchitecture,
    string? MacOSProductVersion,
    string? MacOSBuildVersion,
    string? Gpu,
    string? XcodeVersion,
    string? XcodeBuildVersion,
    string? MacOSSDKVersion,
    string? MacOSSDKPath,
    string? MetalToolchainStatus)
{
    public static SharpGPUEnvironmentReport Capture()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new SharpGPUEnvironmentReport(
                RuntimeInformation.RuntimeIdentifier,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        CommandResult swVersProduct = RunCommand("sw_vers", "-productVersion");
        CommandResult swVersBuild = RunCommand("sw_vers", "-buildVersion");
        CommandResult gpuReport = RunCommand("system_profiler", "SPDisplaysDataType");
        CommandResult xcodeVersion = RunCommand("xcodebuild", "-version");
        CommandResult sdkVersion = RunCommand("xcrun", "--sdk macosx --show-sdk-version");
        CommandResult sdkPath = RunCommand("xcrun", "--show-sdk-path");
        CommandResult metalVersion = RunCommand("xcrun", "metal -v");

        string[] xcodeLines = SplitLines(xcodeVersion.StdOut);
        return new SharpGPUEnvironmentReport(
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            EmptyToNull(swVersProduct.StdOut),
            EmptyToNull(swVersBuild.StdOut),
            EmptyToNull(SummarizeGpuReport(gpuReport.StdOut)),
            xcodeLines.Length > 0 ? xcodeLines[0] : EmptyToNull(xcodeVersion.StdOut),
            xcodeLines.Length > 1 ? xcodeLines[1] : null,
            EmptyToNull(sdkVersion.StdOut),
            EmptyToNull(sdkPath.StdOut),
            metalVersion.ExitCode == 0
                ? EmptyToNull(metalVersion.StdOut)
                : EmptyToNull($"exit={metalVersion.ExitCode}; {metalVersion.StdErr}"));
    }

    private static CommandResult RunCommand(string fileName, string arguments)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }
            };

            process.Start();
            if (!process.WaitForExit(10_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return new CommandResult(-1, string.Empty, "timeout");
            }

            return new CommandResult(
                process.ExitCode,
                process.StandardOutput.ReadToEnd().Trim(),
                process.StandardError.ReadToEnd().Trim());
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message);
        }
    }

    private static string SummarizeGpuReport(string report)
    {
        string[] lines = SplitLines(report);
        string[] interesting = lines
            .Select(line => line.Trim())
            .Where(line =>
                line.StartsWith("Chipset Model:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Total Number of Cores:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Metal Support:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return string.Join("; ", interesting);
    }

    private static string[] SplitLines(string value)
    {
        return value
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
}

internal sealed record SharpGPUFeatureReport(
    string Backend,
    int DeviceIndex,
    string? AdapterName,
    string VendorId,
    string DeviceId,
    string DeviceType,
    bool TimestampQueries,
    bool OcclusionQueries,
    bool PipelineStatisticsQueries,
    bool MachineLearning,
    bool Raytracing,
    bool MeshShading,
    bool WorkGraph,
    string? WorkGraphsTier,
    string? HighestShaderModel,
    string? RuntimeIdentifier,
    bool? Dx12AgilityDeviceFactory,
    string? Dx12AgilityDiagnostic,
    string? TimestampQueriesUnavailableReason,
    string? MetalMLUnavailableReason,
    string? UnavailableReason)
{
    public static SharpGPUFeatureReport BackendUnavailable(ERHIBackend backend, string reason)
    {
        return new SharpGPUFeatureReport(
            backend.ToString(),
            -1,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            false,
            false,
            false,
            false,
            false,
            false,
            null,
            null,
            RuntimeInformation.RuntimeIdentifier,
#if SHARPGPU_ENABLE_DX12
            backend == ERHIBackend.DirectX12 && OperatingSystem.IsWindows() ? Dx12Agility.IsDeviceFactoryAvailable : null,
            backend == ERHIBackend.DirectX12 && OperatingSystem.IsWindows() ? Dx12Agility.Diagnostic : null,
#else
            null,
            null,
#endif
            null,
            null,
            reason);
    }

    public static SharpGPUFeatureReport FromDevice(ERHIBackend backend, int deviceIndex, RHIDevice device)
    {
        string? workGraphsTier = null;
        string? highestShaderModel = null;

#if SHARPGPU_ENABLE_DX12
        if (device is Dx12Device dx12Device)
        {
            FeatureDataD3D12Options21 options21 = default;
            bool options21Supported = dx12Device.NativeDevice.CheckFeatureSupport(Feature.Options21, ref options21);
            workGraphsTier = options21Supported ? options21.WorkGraphsTier.ToString() : "Options21Unavailable";
            highestShaderModel = dx12Device.NativeDevice.CheckHighestShaderModel(ShaderModel.Model6_8).ToString();
        }
#endif

        string? timestampQueriesUnavailableReason = null;
        string? metalMLUnavailableReason = null;
        if (device is MetalDevice metalDevice)
        {
            timestampQueriesUnavailableReason = metalDevice.TimestampQueriesUnavailableReason;
            metalMLUnavailableReason = metalDevice.MetalMLUnavailableReason;
        }

        RHIDeviceFeature? feature = device.Feature;
        return new SharpGPUFeatureReport(
            backend.ToString(),
            deviceIndex,
            device.Name,
            device.VendorId.DecimalValue,
            device.DeviceId.DecimalValue,
            device.Type.ToString(),
            feature?.IsTimestampQueriesSupported == true,
            feature?.IsOcclusionQueriesSupported == true,
            feature?.IsPipelineStatsQueriesSupported == true,
            feature?.IsMLSupported == true,
            feature?.IsRaytracingSupported == true,
            feature?.IsMeshShadingSupported == true,
            feature?.IsWorkgraphSupported == true,
            workGraphsTier,
            highestShaderModel,
            RuntimeInformation.RuntimeIdentifier,
#if SHARPGPU_ENABLE_DX12
            backend == ERHIBackend.DirectX12 ? Dx12Agility.IsDeviceFactoryAvailable : null,
            backend == ERHIBackend.DirectX12 ? Dx12Agility.Diagnostic : null,
#else
            null,
            null,
#endif
            timestampQueriesUnavailableReason,
            metalMLUnavailableReason,
            null);
    }
}

internal static class ArtifactPath
{
    public static string Resolve(string fileName)
    {
        string root = FindRepositoryRoot();
        return Path.Combine(root, "Engine", "Artifacts", "SharpGPU", fileName);
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

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
    };
}
