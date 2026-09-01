using System;
using System.Reflection;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class SparseCapabilityPortableContractTests
{
    [Fact]
    public void SparseBuffer_ShouldStayUnavailableWithoutCreateSparseBuffer()
    {
        MethodInfo? factory = typeof(RHIDevice).GetMethod(
            "CreateSparseBuffer",
            BindingFlags.Public | BindingFlags.Instance);
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
            {
                if (OperatingSystem.IsWindows() &&
                    backend == ERHIBackend.DirectX12 &&
                    RHIInstance.IsBackendSupported(backend, out string reason))
                {
                    Assert.Fail(
                        $"DirectX12 was supported but no device was created: {reason}");
                }

                continue;
            }

            using (instance)
            {
                RHICapability sparseBuffer = device.Capabilities.Memory.SparseBuffer;
                if (sparseBuffer.Tier != ERHICapabilityTier.Unavailable)
                {
                    Assert.NotNull(factory);
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(sparseBuffer.UnavailableReason));
                    if (factory != null)
                    {
                        Assert.Throws<NotSupportedException>(
                            () => factory.Invoke(device, Array.Empty<object>()));
                    }
                }
            }
        }

        Assert.Null(factory);
        Assert.Equal(
            "SparseBuffer",
            nameof(RHIMemoryCapabilities.SparseBuffer));
        Assert.Equal(
            "SparseTexture2D",
            nameof(RHIMemoryCapabilities.SparseTexture2D));
        Assert.Equal(
            "BudgetQuery",
            nameof(RHIMemoryCapabilities.BudgetQuery));
    }

    [Fact]
    public void SparseMsaa_WhenUnavailable_CreateSparseTextureMustRequireAndThrow()
    {
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
            {
                continue;
            }

            using (instance)
            {
                RHICapability sparseMsaa = device.Capabilities.Memory.SparseMsaa;
                RHITextureDescriptor descriptor = new()
                {
                    MipCount = 1,
                    Extent = new uint3(64, 64, 1),
                    Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                    SampleCount = ERHISampleCount.Count4,
                    StorageMode = ERHIStorageMode.GPULocal,
                    UsageFlag = ERHITextureUsage.ShaderResource,
                    Dimension = ERHITextureDimension.Texture2DMS,
                };

                if (sparseMsaa.Tier == ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(sparseMsaa.UnavailableReason));
                    NotSupportedException ex = Assert.Throws<NotSupportedException>(
                        () => device.CreateSparseTexture(descriptor));
                    Assert.Contains(
                        sparseMsaa.UnavailableReason,
                        ex.Message,
                        StringComparison.Ordinal);
                    if (backend == ERHIBackend.Vulkan)
                    {
                        Assert.Contains(
                            "rejects MSAA",
                            sparseMsaa.UnavailableReason,
                            StringComparison.Ordinal);
                    }
                }
                else
                {
                    try
                    {
                        using RHITexture texture = device.CreateSparseTexture(descriptor);
                        Assert.Equal(
                            ERHIResourceAllocationMode.Sparse,
                            texture.AllocationMode);
                    }
                    catch (NotSupportedException ex)
                    {
                        Assert.DoesNotContain(
                            "not implemented",
                            ex.Message,
                            StringComparison.OrdinalIgnoreCase);
                        throw;
                    }
                }
            }
        }
    }

    private static bool TryCreateDevice(
        ERHIBackend backend,
        out RHIInstance? instance,
        out RHIDevice? device)
    {
        instance = null;
        device = null;
        if (!FeatureContractContext.TryCreateInstance(backend, out instance, out _))
        {
            return false;
        }

        if (instance.DeviceCount <= 0)
        {
            instance.Dispose();
            instance = null;
            return false;
        }

        device = instance.GetDevice(0);
        return device != null;
    }
}
