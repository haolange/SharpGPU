using System;
using System.Reflection;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class FormatSupportPortableContractTests
{
    [Fact]
    public void QueryFormatSupport_ShouldBeDistinctFromRasterAttachmentQuery()
    {
        MethodInfo format = typeof(RHIDevice).GetMethod(
            nameof(RHIDevice.QueryFormatSupport),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("QueryFormatSupport was not found.");
        MethodInfo raster = typeof(RHIDevice).GetMethod(
            nameof(RHIDevice.QueryRasterAttachmentSupport),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("QueryRasterAttachmentSupport was not found.");
        MethodInfo resolve = typeof(RHIDevice).GetMethod(
            nameof(RHIDevice.QueryResolveSupport),
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("QueryResolveSupport was not found.");

        Assert.NotEqual(format, raster);
        Assert.NotEqual(format, resolve);
        Assert.NotEqual(raster, resolve);
        Assert.Equal(typeof(RHICapability), format.ReturnType);
        Assert.Equal(typeof(RHICapability), raster.ReturnType);
        Assert.Equal(typeof(RHICapability), resolve.ReturnType);
        Assert.True(Enum.IsDefined(ERHICapabilityLimitKind.SupportedFormatOperationMask));
    }

    [Fact]
    public void FormatSupportQuery_ShouldRejectPendingAndUnknownFormat()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.Pending,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.Unknown,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                (ERHIPixelFormat)byte.MaxValue,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHITextureUsage.None,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                (ERHITextureUsage)0x80,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Pending,
                ERHISampleCount.None,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.Pending,
                ERHITextureTiling.Optimal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIFormatSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHITextureUsage.ShaderResource,
                ERHITextureDimension.Texture2D,
                ERHISampleCount.None,
                ERHITextureTiling.Pending));
    }

    [Fact]
    public void QueryFormatSupport_ShouldReturnTypedCapabilityNeverRawBool()
    {
        RHIFormatSupportQuery query = CreateRgba8ShaderResourceQuery();
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
            {
                Assert.False(
                    RHIInstance.IsBackendSupported(backend, out _),
                    $"{backend} reported supported but did not create a device.");
                continue;
            }

            using (instance)
            {
                RHICapability capability = device.QueryFormatSupport(in query);
                Assert.IsType<RHICapability>(capability);
                Assert.True(Enum.IsDefined(capability.Tier));
                Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
                if (capability.Tier == ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
                }
                else
                {
                    Assert.True(
                        capability.Limits.TryGetValue(
                            ERHICapabilityLimitKind.SupportedFormatOperationMask,
                            out ulong mask));
                    Assert.NotEqual(0UL, mask);
                }

                RHICapability rgba8Depth = device.QueryFormatSupport(
                    new RHIFormatSupportQuery(
                        ERHIPixelFormat.R8G8B8A8_UNorm,
                        ERHITextureUsage.DepthStencil,
                        ERHITextureDimension.Texture2D,
                        ERHISampleCount.None,
                        ERHITextureTiling.Optimal));
                if (rgba8Depth.Tier == ERHICapabilityTier.Unavailable)
                {
                    Assert.False(string.IsNullOrWhiteSpace(rgba8Depth.UnavailableReason));
                }
                else
                {
                    Assert.True(
                        rgba8Depth.Limits.TryGetValue(
                            ERHICapabilityLimitKind.SupportedFormatOperationMask,
                            out ulong depthMask));
                    Assert.False(
                        ((ERHIFormatSupportOperation)depthMask).HasFlag(
                            ERHIFormatSupportOperation.DepthStencilAttachment),
                        $"{backend} must not claim DepthStencilAttachment for RGBA8.");
                }
            }
        }
    }

    [Fact]
    public void Metal_LegalRgba8Query_ShouldNotThrowWhenBackendExists()
    {
        if (!TryCreateDevice(ERHIBackend.Metal, out RHIInstance? instance, out RHIDevice? device))
        {
            if (RHIInstance.IsBackendSupported(ERHIBackend.Metal, out string reason))
            {
                Assert.Fail($"Metal was supported but no device was created: {reason}");
            }

            return;
        }

        using (instance)
        {
            RHIFormatSupportQuery query = CreateRgba8ShaderResourceQuery();
            RHICapability capability = device.QueryFormatSupport(in query);
            Assert.True(Enum.IsDefined(capability.Tier));
            if (capability.Tier == ERHICapabilityTier.Unavailable)
            {
                Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
            }
            else
            {
                Assert.True(
                    capability.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedFormatOperationMask,
                        out ulong mask));
                Assert.False(
                    ((ERHIFormatSupportOperation)mask).HasFlag(
                        ERHIFormatSupportOperation.LinearFilter),
                    "Metal ShaderResource creation does not prove LinearFilter without a distinct filter probe.");
                Assert.True(
                    ((ERHIFormatSupportOperation)mask).HasFlag(
                        ERHIFormatSupportOperation.Sample));
            }
        }
    }

    [Fact]
    public void WindowsDx12AndVulkan_Rgba8Optimal_ShouldBeAvailableWithSample()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        bool probedAny = false;
        foreach (ERHIBackend backend in new[] { ERHIBackend.DirectX12, ERHIBackend.Vulkan })
        {
            bool backendSupported = RHIInstance.IsBackendSupported(backend, out string supportedReason);
            if (!TryCreateDevice(backend, out RHIInstance? instance, out RHIDevice? device))
            {
                if (backendSupported)
                {
                    Assert.Fail(
                        $"{backend} was supported but no device was created: {supportedReason}");
                }

                continue;
            }

            probedAny = true;
            using (instance)
            {
                RHICapability capability = device.QueryFormatSupport(
                    CreateRgba8ShaderResourceQuery());
                Assert.NotEqual(ERHICapabilityTier.Unavailable, capability.Tier);
                Assert.True(
                    capability.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedFormatOperationMask,
                        out ulong mask));
                Assert.True(
                    ((ERHIFormatSupportOperation)mask).HasFlag(
                        ERHIFormatSupportOperation.Sample),
                    $"{backend} RGBA8 2D Optimal ShaderResource mask must include Sample.");

                RHICapability linear = device.QueryFormatSupport(
                    new RHIFormatSupportQuery(
                        ERHIPixelFormat.R8G8B8A8_UNorm,
                        ERHITextureUsage.ShaderResource,
                        ERHITextureDimension.Texture2D,
                        ERHISampleCount.None,
                        ERHITextureTiling.Linear));
                if (backend == ERHIBackend.DirectX12)
                {
                    Assert.Equal(ERHICapabilityTier.Unavailable, linear.Tier);
                    Assert.Contains(
                        "linear image tiling query",
                        linear.UnavailableReason,
                        StringComparison.OrdinalIgnoreCase);
                }

                RHICapability depthAsColor = device.QueryFormatSupport(
                    new RHIFormatSupportQuery(
                        ERHIPixelFormat.D32_Float,
                        ERHITextureUsage.DepthStencil,
                        ERHITextureDimension.Texture2D,
                        ERHISampleCount.None,
                        ERHITextureTiling.Optimal));
                if (depthAsColor.Tier != ERHICapabilityTier.Unavailable)
                {
                    Assert.True(
                        depthAsColor.Limits.TryGetValue(
                            ERHICapabilityLimitKind.SupportedFormatOperationMask,
                            out ulong depthMask));
                    Assert.False(
                        ((ERHIFormatSupportOperation)depthMask).HasFlag(
                            ERHIFormatSupportOperation.ColorAttachment),
                        $"{backend} must not claim ColorAttachment for D32_Float.");
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(depthAsColor.UnavailableReason));
                }
            }
        }

        Assert.True(
            probedAny,
            "Windows host must create DX12 or Vulkan to exercise the format-support oracle.");
    }

    [Fact]
    public void WindowsVulkan_Rgba8CubeOptimal_ShouldNotSilentlyFallbackToSampled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!RHIInstance.IsBackendSupported(ERHIBackend.Vulkan, out string reason))
        {
            return;
        }

        if (!TryCreateDevice(ERHIBackend.Vulkan, out RHIInstance? instance, out RHIDevice? device))
        {
            Assert.Fail($"Vulkan was supported but no device was created: {reason}");
            return;
        }

        using (instance)
        {
            RHICapability capability = device.QueryFormatSupport(
                new RHIFormatSupportQuery(
                    ERHIPixelFormat.R8G8B8A8_UNorm,
                    ERHITextureUsage.ShaderResource,
                    ERHITextureDimension.TextureCube,
                    ERHISampleCount.None,
                    ERHITextureTiling.Optimal));
            if (capability.Tier == ERHICapabilityTier.Unavailable)
            {
                Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
                Assert.DoesNotContain(
                    "Sampled fallback",
                    capability.UnavailableReason,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(
                    capability.UnavailableReason.Contains("cube", StringComparison.OrdinalIgnoreCase) ||
                    capability.UnavailableReason.Contains("flag", StringComparison.OrdinalIgnoreCase) ||
                    capability.UnavailableReason.Contains("CubeCompatible", StringComparison.Ordinal) ||
                    capability.UnavailableReason.Contains("image usage", StringComparison.OrdinalIgnoreCase),
                    "Vulkan Cube Unavailable reason must mention the cube/flag/image query, not a silent Sampled fallback.");
            }
            else
            {
                Assert.True(
                    capability.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedFormatOperationMask,
                        out ulong mask));
                Assert.True(
                    ((ERHIFormatSupportOperation)mask).HasFlag(
                        ERHIFormatSupportOperation.Sample));
            }
        }
    }

    private static RHIFormatSupportQuery CreateRgba8ShaderResourceQuery()
    {
        return new RHIFormatSupportQuery(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHITextureUsage.ShaderResource,
            ERHITextureDimension.Texture2D,
            ERHISampleCount.None,
            ERHITextureTiling.Optimal);
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
