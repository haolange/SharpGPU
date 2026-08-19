using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SharpGPU;

namespace SharpGPU.Conformance.Tests;

internal enum SharpGpuValidationOutcome : byte
{
    Passed = 0,
    Failed = 1,
    Unverified = 2,
    NotApplicable = 3,
}

internal sealed record SharpGpuCapabilityLimitReport(
    string Kind,
    ulong Value);

internal sealed record SharpGpuCapabilityProbeReport(
    string Kind,
    string Source);

internal sealed record SharpGpuCapabilityReport(
    string Name,
    string Tier,
    string Strategy,
    string? MaintenanceStrategy,
    IReadOnlyList<SharpGpuCapabilityLimitReport> Limits,
    string? UnavailableReason,
    SharpGpuCapabilityProbeReport Provenance,
    SharpGpuValidationOutcome Validation,
    IReadOnlyList<string> Evidence);

internal sealed record SharpGpuScenarioReport(
    string Id,
    string Backend,
    int DeviceIndex,
    SharpGpuValidationOutcome Outcome,
    IReadOnlyList<string> Capabilities,
    string Evidence,
    string? Detail)
{
    public static SharpGpuScenarioReport Create(
        string id,
        ERHIBackend backend,
        int deviceIndex,
        SharpGpuValidationOutcome outcome,
        IEnumerable<string>? capabilities,
        string evidence,
        string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(backend) || backend == ERHIBackend.Pending)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backend),
                backend,
                "Scenario backend must be a defined runtime backend.");
        }
        if (deviceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceIndex),
                "Scenario device index must not be negative.");
        }
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "Scenario outcome is not defined.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);

        string[] capabilityCopy = capabilities is null
            ? Array.Empty<string>()
            : capabilities
                .Select(static value =>
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(value);
                    return value.Trim();
                })
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();

        return new SharpGpuScenarioReport(
            id.Trim(),
            backend.ToString(),
            deviceIndex,
            outcome,
            capabilityCopy,
            evidence.Trim(),
            string.IsNullOrWhiteSpace(detail) ? null : detail.Trim());
    }
}

internal static class SharpGpuCapabilityReportFactory
{
    public static IReadOnlyList<SharpGpuCapabilityReport> Create(
        RHIDevice device,
        int deviceIndex,
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

        SharpGpuScenarioReport[] scenarioCopy = scenarios is null
            ? Array.Empty<SharpGpuScenarioReport>()
            : scenarios
                .Where(scenario =>
                    string.Equals(
                        scenario.Backend,
                        device.BackendType.ToString(),
                        StringComparison.Ordinal))
                .Where(scenario => scenario.DeviceIndex == deviceIndex)
                .ToArray();

        List<SharpGpuCapabilityReport> reports = new();
        PropertyInfo[] domains = typeof(RHIDeviceCapabilities)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (PropertyInfo domain in domains)
        {
            object domainValue = domain.GetValue(device.Capabilities)
                ?? throw new InvalidOperationException(
                    $"Capability domain '{domain.Name}' returned null.");
            PropertyInfo[] capabilities = domain.PropertyType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(static property => property.PropertyType == typeof(RHICapability))
                .OrderBy(static property => property.Name, StringComparer.Ordinal)
                .ToArray();

            foreach (PropertyInfo capabilityProperty in capabilities)
            {
                RHICapability capability = (RHICapability)(
                    capabilityProperty.GetValue(domainValue)
                    ?? throw new InvalidOperationException(
                        $"Capability '{domain.Name}.{capabilityProperty.Name}' returned null."));
                string name = $"{domain.Name}.{capabilityProperty.Name}";
                SharpGpuScenarioReport[] matchingScenarios = scenarioCopy
                    .Where(scenario => scenario.Capabilities.Contains(
                        name,
                        StringComparer.Ordinal))
                    .ToArray();

                SharpGpuValidationOutcome validation = ResolveValidation(
                    capability,
                    matchingScenarios);
                string[] evidence = matchingScenarios
                    .Where(static scenario =>
                        scenario.Outcome is
                            SharpGpuValidationOutcome.Passed or
                            SharpGpuValidationOutcome.Failed)
                    .Select(static scenario => scenario.Id)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray();

                ReadOnlySpan<RHICapabilityLimit> nativeLimits =
                    capability.Limits.Values;
                SharpGpuCapabilityLimitReport[] limits =
                    new SharpGpuCapabilityLimitReport[nativeLimits.Length];
                for (int index = 0; index < nativeLimits.Length; ++index)
                {
                    limits[index] = new SharpGpuCapabilityLimitReport(
                        nativeLimits[index].Kind.ToString(),
                        nativeLimits[index].Value);
                }
                Array.Sort(
                    limits,
                    static (left, right) =>
                        string.CompareOrdinal(left.Kind, right.Kind));

                reports.Add(new SharpGpuCapabilityReport(
                    name,
                    capability.Tier.ToString(),
                    capability.Strategy.ToString(),
                    null,
                    limits,
                    capability.UnavailableReason,
                    new SharpGpuCapabilityProbeReport(
                        capability.Provenance.Kind.ToString(),
                        capability.Provenance.Source),
                    validation,
                    evidence));
            }
        }

        return reports;
    }

    private static SharpGpuValidationOutcome ResolveValidation(
        in RHICapability capability,
        IReadOnlyList<SharpGpuScenarioReport> scenarios)
    {
        if (capability.Tier == ERHICapabilityTier.Unavailable)
        {
            return SharpGpuValidationOutcome.NotApplicable;
        }

        if (scenarios.Any(static scenario =>
                scenario.Outcome == SharpGpuValidationOutcome.Failed))
        {
            return SharpGpuValidationOutcome.Failed;
        }

        return scenarios.Any(static scenario =>
                scenario.Outcome == SharpGpuValidationOutcome.Passed)
            ? SharpGpuValidationOutcome.Passed
            : SharpGpuValidationOutcome.Unverified;
    }
}
