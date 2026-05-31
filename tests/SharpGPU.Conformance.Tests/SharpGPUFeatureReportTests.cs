using System;
using System.IO;
using System.Linq;
using SharpGPU;
using System.Text.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
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
        string path = ArtifactPath.Resolve("feature-report-win-x64.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(reports, JsonOptions.Indented));
        m_Output.WriteLine(path);

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains(reports, report => report.Backend == ERHIBackend.DirectX12.ToString() && report.DeviceIndex >= 0);
        }
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
            backend == ERHIBackend.DirectX12 && OperatingSystem.IsWindows() ? Dx12Agility.IsDeviceFactoryAvailable : null,
            backend == ERHIBackend.DirectX12 && OperatingSystem.IsWindows() ? Dx12Agility.Diagnostic : null,
            reason);
    }

    public static SharpGPUFeatureReport FromDevice(ERHIBackend backend, int deviceIndex, RHIDevice device)
    {
        string? workGraphsTier = null;
        string? highestShaderModel = null;

        if (device is Dx12Device dx12Device)
        {
            FeatureDataD3D12Options21 options21 = default;
            bool options21Supported = dx12Device.NativeDevice.CheckFeatureSupport(Feature.Options21, ref options21);
            workGraphsTier = options21Supported ? options21.WorkGraphsTier.ToString() : "Options21Unavailable";
            highestShaderModel = dx12Device.NativeDevice.CheckHighestShaderModel(ShaderModel.Model6_8).ToString();
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
            backend == ERHIBackend.DirectX12 ? Dx12Agility.IsDeviceFactoryAvailable : null,
            backend == ERHIBackend.DirectX12 ? Dx12Agility.Diagnostic : null,
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
