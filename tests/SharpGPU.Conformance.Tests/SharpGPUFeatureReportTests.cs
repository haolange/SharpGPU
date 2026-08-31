using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SharpGPU;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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
        List<SharpGpuScenarioReport> scenarios = new();

        foreach (ERHIBackend backend in new[] { ERHIBackend.DirectX12, ERHIBackend.Vulkan, ERHIBackend.Metal })
        {
            if (!RHIInstance.IsBackendSupported(backend, out string reason))
            {
                reports.Add(SharpGPUFeatureReport.BackendUnavailable(
                    backend,
                    reason,
                    SharpGpuValidationOutcome.NotApplicable));
                continue;
            }

            try
            {
                using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
                {
                    Backend = backend,
                    SurfaceKind = ERHINativeSurfaceKind.Headless,
                    EnableDebugLayer = false,
                    EnableValidation = false,
                    ComputeQueueRequestCount = 0,
                    TransferQueueRequestCount = 0,
                    GraphicsQueueRequestCount = 1,
                });

                for (int deviceIndex = 0; deviceIndex < instance.DeviceCount; ++deviceIndex)
                {
                    RHIDevice device = instance.GetDevice(deviceIndex);
                    scenarios.Add(SharpGpuScenarioReport.Create(
                        $"{backend}.device-{deviceIndex}.create",
                        backend,
                        deviceIndex,
                        SharpGpuValidationOutcome.Passed,
                        capabilities: null,
                        evidence: "RHI instance enumerated and returned a live device."));
                    reports.Add(SharpGPUFeatureReport.FromDevice(
                        backend,
                        deviceIndex,
                        device,
                        scenarios));
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or DllNotFoundException or InvalidOperationException)
            {
                reports.Add(SharpGPUFeatureReport.BackendUnavailable(
                    backend,
                    ex.Message,
                    SharpGpuValidationOutcome.Failed));
            }
        }

        Assert.NotEmpty(reports);
        Assert.DoesNotContain(
            reports,
            report => report.Availability == SharpGpuValidationOutcome.Failed);
        SharpGPUFeatureReportDocument document = new(
            SharpGPUEnvironmentReport.Capture(),
            reports,
            scenarios);
        string path = ArtifactPath.Resolve(GetFeatureReportFileName());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(document, JsonOptions.Indented);
        Assert.DoesNotContain(ArtifactPath.RepositoryRoot, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"TimestampQueries\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"Capabilities\":", json, StringComparison.Ordinal);
        Assert.Contains("\"Scenarios\":", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"Name\": \"Raster.FramebufferReadWrite\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"Name\": \"Raster.RasterOrderedAccess\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"Name\": \"Raster.SampledFeedback\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"Name\": \"Raster.VariableRateShading\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Name\": \"Raster.VariableRateShadingPerDraw\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Name\": \"Raster.VariableRateShadingPerPrimitive\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Name\": \"Raster.VariableRateShadingAttachment\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Name\": \"Raster.VariableRateShadingCombiners\"",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "attachment_feedback_loop",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "fragmentShaderPixelInterlock",
            json,
            StringComparison.Ordinal);
        File.WriteAllText(path, json);
        m_Output.WriteLine(path);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(
                reports,
                report =>
                    report.Backend == ERHIBackend.DirectX12.ToString()
                    && report.DeviceIndex >= 0
                    && report.Availability == SharpGpuValidationOutcome.Passed);
        }
    }

    [Fact]
    public void FeatureReportSchema_ShouldRejectRetiredOrUnknownFields()
    {
        SharpGPUFeatureReportDocument document = new(
            SharpGPUEnvironmentReport.Capture(),
            Array.Empty<SharpGPUFeatureReport>(),
            Array.Empty<SharpGpuScenarioReport>());
        string canonical = JsonSerializer.Serialize(document, JsonOptions.Indented);
        SharpGPUFeatureReportDocument? roundTrip =
            JsonSerializer.Deserialize<SharpGPUFeatureReportDocument>(
                canonical,
                JsonOptions.Indented);

        Assert.NotNull(roundTrip);
        Assert.Equal(
            SharpGPUFeatureReportDocument.CurrentSchemaRevision,
            roundTrip.SchemaRevision);

        string retiredSchema = canonical.Replace(
            $"\"SchemaRevision\": {SharpGPUFeatureReportDocument.CurrentSchemaRevision}",
            "\"SchemaRevision\": 1",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<SharpGPUFeatureReportDocument>(
                retiredSchema,
                JsonOptions.Indented));

        string unknownField = canonical.Replace(
            "{",
            "{\"TimestampQueries\":true,",
            StringComparison.Ordinal);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SharpGPUFeatureReportDocument>(
                unknownField,
                JsonOptions.Indented));
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
            Assert.Equal(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.WorkGraph.Execution.Tier);
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
                EnableValidation = false,
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

internal sealed record SharpGPUFeatureReportDocument
{
    public const uint CurrentSchemaRevision = 2;

    public uint SchemaRevision { get; }
    public SharpGPUEnvironmentReport Environment { get; }
    public IReadOnlyList<SharpGPUFeatureReport> Backends { get; }
    public IReadOnlyList<SharpGpuScenarioReport> Scenarios { get; }

    public SharpGPUFeatureReportDocument(
        SharpGPUEnvironmentReport environment,
        IReadOnlyList<SharpGPUFeatureReport> backends,
        IReadOnlyList<SharpGpuScenarioReport>? scenarios = null,
        uint schemaRevision = CurrentSchemaRevision)
    {
        if (schemaRevision != CurrentSchemaRevision)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaRevision),
                schemaRevision,
                $"SharpGPU feature report schema must be {CurrentSchemaRevision}.");
        }

        SchemaRevision = schemaRevision;
        Environment = environment
            ?? throw new ArgumentNullException(nameof(environment));
        Backends = backends
            ?? throw new ArgumentNullException(nameof(backends));
        Scenarios = scenarios ?? Array.Empty<SharpGpuScenarioReport>();
    }
}

internal sealed record SharpGPUEnvironmentReport(
    string RuntimeIdentifier,
    string OS,
    string OSVersion,
    string OSArchitecture,
    string ProcessArchitecture)
{
    public static SharpGPUEnvironmentReport Capture()
    {
        return new SharpGPUEnvironmentReport(
            RuntimeInformation.RuntimeIdentifier,
            GetOperatingSystemName(),
            Environment.OSVersion.Version.ToString(),
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    private static string GetOperatingSystemName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }
}

internal sealed record SharpGPUFeatureReport(
    string Backend,
    SharpGpuValidationOutcome Availability,
    int DeviceIndex,
    string? AdapterName,
    string VendorId,
    string DeviceId,
    string DeviceType,
    string DriverVersion,
    string ApiVersion,
    IReadOnlyList<SharpGpuCapabilityReport> Capabilities,
    string? UnavailableReason)
{
    public static SharpGPUFeatureReport BackendUnavailable(
        ERHIBackend backend,
        string reason,
        SharpGpuValidationOutcome outcome)
    {
        if (outcome is not (
            SharpGpuValidationOutcome.Failed or
            SharpGpuValidationOutcome.NotApplicable))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "An unavailable backend must be Failed or NotApplicable.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new SharpGPUFeatureReport(
            backend.ToString(),
            outcome,
            -1,
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<SharpGpuCapabilityReport>(),
            reason);
    }

    public static SharpGPUFeatureReport FromDevice(
        ERHIBackend backend,
        int deviceIndex,
        RHIDevice device,
        IEnumerable<SharpGpuScenarioReport>? scenarios = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.IsDisposed)
        {
            throw new ObjectDisposedException(device.GetType().FullName);
        }
        if (deviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceIndex),
                "Device index must not be negative.");
        }
        if (device.BackendType != backend)
        {
            throw new ArgumentException(
                $"Report backend {backend} does not match device backend {device.BackendType}.",
                nameof(backend));
        }

        return new SharpGPUFeatureReport(
            backend.ToString(),
            SharpGpuValidationOutcome.Passed,
            deviceIndex,
            device.Name,
            device.VendorId.DecimalValue,
            device.DeviceId.DecimalValue,
            device.Type.ToString(),
            device.DriverVersion,
            GetApiVersion(device),
            SharpGpuCapabilityReportFactory.Create(device, deviceIndex, scenarios),
            null);
    }

    private static string GetApiVersion(RHIDevice device)
    {
        return device switch
        {
#if SHARPGPU_ENABLE_DX12
            Dx12Device => $"D3D12 Agility SDK {Dx12Agility.SDKVersion}",
#endif
            VulkanDevice vulkan => FormatVulkanVersion(vulkan.EffectiveApiVersion),
            MetalDevice => "Metal 4",
            _ => throw new NotSupportedException(
                $"No API-version formatter exists for {device.GetType().FullName}."),
        };
    }

    private static string FormatVulkanVersion(uint version)
    {
        uint major = version >> 22;
        uint minor = (version >> 12) & 0x3ff;
        uint patch = version & 0xfff;
        return $"Vulkan {major}.{minor}.{patch}";
    }
}

internal static class ArtifactPath
{
    public static string Resolve(string fileName)
    {
        string root = FindRepositoryRoot();
        return Path.Combine(root, "docs", "Artifacts", "SharpGPU", fileName);
    }

    public static string RepositoryRoot => FindRepositoryRoot();

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
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };
}
