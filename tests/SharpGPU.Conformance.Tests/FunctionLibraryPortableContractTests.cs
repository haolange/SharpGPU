using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class FunctionLibraryPortableContractTests
{
    [Fact]
    public void FunctionLibraryView_CarriesLibraryDigestWithoutOwningBytecode()
    {
        byte[] payload = { 0x07, 0x23, 0x02, 0x03, 0x11, 0x22, 0x33, 0x44 };
        using TestFunctionLibrary library = new(payload, ERHIShaderPayloadKind.SpirV);
        using RHIFunction view = library.CreateFunction(new RHIFunctionViewDescriptor
        {
            EntryName = "main",
            Type = ERHIFunctionType.Compute,
        });

        Assert.Equal(ERHIFunctionSourceKind.LibraryView, view.SourceKind);
        Assert.Equal(IntPtr.Zero, view.Descriptor.ByteCode);
        Assert.Equal(0u, view.Descriptor.ByteSize);
        Assert.Equal("main", view.Descriptor.EntryName);
        Assert.Equal(ERHIFunctionType.Compute, view.Descriptor.Type);
        Assert.Equal(ERHIShaderPayloadKind.SpirV, view.Descriptor.PayloadKind);
        Assert.Equal(SHA256.HashData(payload), view.ContentDigest.ToArray());
        Assert.Equal(library.ContentDigest.ToArray(), view.ContentDigest.ToArray());

        using TestPipelineLayout layout = new(pushConstantSize: 0);
        string key = RHIPipelineCacheKeyBuilder.CreateComputeKey(
            new RHIComputePipelineDescriptor
            {
                ThreadSize = new SharpMath.uint3(1, 1, 1),
                ComputeFunction = view,
                PipelineLayout = layout,
            },
            new RHIPipelineCacheIdentity(ERHIBackend.Vulkan, 0x10de, 1, "driver"));
        Assert.False(string.IsNullOrWhiteSpace(key));

        using TestDirectFunction direct = new(
            ERHIFunctionType.Compute,
            "main",
            payload,
            ERHIShaderPayloadKind.SpirV);
        string directKey = RHIPipelineCacheKeyBuilder.CreateComputeKey(
            new RHIComputePipelineDescriptor
            {
                ThreadSize = new SharpMath.uint3(1, 1, 1),
                ComputeFunction = direct,
                PipelineLayout = layout,
            },
            new RHIPipelineCacheIdentity(ERHIBackend.Vulkan, 0x10de, 1, "driver"));
        Assert.Equal(view.ContentDigest.ToArray(), direct.ContentDigest.ToArray());
        Assert.NotEqual(key, directKey);
    }

    [Fact]
    public void DirectFunction_BindsDirectBytecodeSourceKind()
    {
        using TestDirectFunction function = new(
            ERHIFunctionType.Compute,
            "main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 1 });
        Assert.Equal(ERHIFunctionSourceKind.DirectBytecode, function.SourceKind);
        Assert.Equal(32, function.ContentDigest.Length);
    }

    [Fact]
    public void FunctionLibraryView_FailsClosedAfterLibraryDispose()
    {
        byte[] payload = { 0x01, 0x02, 0x03, 0x04 };
        TestFunctionLibrary library = new(payload, ERHIShaderPayloadKind.Dxil);
        RHIFunction view = library.CreateFunction(new RHIFunctionViewDescriptor
        {
            EntryName = "RayGen",
            Type = ERHIFunctionType.RayTracing,
        });
        library.Dispose();

        using TestPipelineLayout layout = new(pushConstantSize: 0);
        Assert.Throws<ObjectDisposedException>(() =>
            RHIPipelineCacheKeyBuilder.CreateComputeKey(
                new RHIComputePipelineDescriptor
                {
                    ThreadSize = new SharpMath.uint3(1, 1, 1),
                    ComputeFunction = view,
                    PipelineLayout = layout,
                },
                new RHIPipelineCacheIdentity(ERHIBackend.DirectX12, 1, 1, "driver")));
        view.Dispose();
    }

    [Fact]
    public void Dx12RasterAndComputeFunctionLibraryViews_RequireAndThrow()
    {
        RHIFunctionLibraryCapabilities dx12 = RHIFunctionLibraryCapabilities.CreateNative(
            "DxilLibraryDescription + state-object export",
            (ulong)ERHIFunctionLibraryReusablePipelineClass.Raytracing,
            RHIFunctionLibraryCapabilities.PayloadKindBit(ERHIShaderPayloadKind.Dxil),
            "CreateGraphicsPipelineState / CreateComputePipelineState only accept per-stage D3D12_SHADER_BYTECODE.");

        Assert.Equal(0UL, dx12.ReusablePipelineClasses & (ulong)ERHIFunctionLibraryReusablePipelineClass.Raster);
        Assert.Equal(0UL, dx12.ReusablePipelineClasses & (ulong)ERHIFunctionLibraryReusablePipelineClass.Compute);
        Assert.Equal(ERHICapabilityTier.Unavailable, dx12.RasterComputeReuse.Tier);
        Assert.Throws<NotSupportedException>(() =>
            dx12.RequireReusableClass(
                ERHIFunctionLibraryReusablePipelineClass.Raster,
                "DX12 raster / compute function-library views"));
        Assert.Throws<NotSupportedException>(() =>
            dx12.RequireReusableClass(
                ERHIFunctionLibraryReusablePipelineClass.Compute,
                "DX12 raster / compute function-library views"));

        bool backendSupported = RHIInstance.IsBackendSupported(
            ERHIBackend.DirectX12,
            out string supportedReason);
        if (!FeatureContractContext.TryCreateInstance(
                ERHIBackend.DirectX12,
                out RHIInstance? instance,
                out string createReason))
        {
            if (OperatingSystem.IsWindows() && backendSupported)
            {
                Assert.Fail(
                    $"DirectX12 was supported but TryCreateInstance failed: {supportedReason} {createReason}");
            }

            return;
        }

        using (instance)
        {
            RHIDevice device = instance.GetDevice(0);
            byte[] dummyDxil = { 0x44, 0x58, 0x49, 0x4c, 0x01, 0x02, 0x03, 0x04 };
            GCHandle pin = GCHandle.Alloc(dummyDxil, GCHandleType.Pinned);
            try
            {
                using RHIFunctionLibrary library = device.CreateFunctionLibrary(
                    new RHIFunctionLibraryDescriptor
                    {
                        ByteCode = pin.AddrOfPinnedObject(),
                        ByteSize = (uint)dummyDxil.Length,
                        PayloadKind = ERHIShaderPayloadKind.Dxil,
                    });
                Assert.Throws<NotSupportedException>(() =>
                    library.CreateFunction(new RHIFunctionViewDescriptor
                    {
                        EntryName = "vs_main",
                        Type = ERHIFunctionType.Vertex,
                    }));
                Assert.Throws<NotSupportedException>(() =>
                    library.CreateFunction(new RHIFunctionViewDescriptor
                    {
                        EntryName = "cs_main",
                        Type = ERHIFunctionType.Compute,
                    }));
            }
            finally
            {
                pin.Free();
            }
        }
    }

    [Fact]
    public void MetalPipelineCache_StaysUnavailable_PerAdr0065()
    {
        // ADR-0065: Metal pipeline cache stays honest Unavailable.
        // Do not implement temp-file or UrlArchive lowering.
        Assert.False(Enum.TryParse("UrlArchive", ignoreCase: false, out ERHICapabilityStrategy _));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreatePipelineCache)));

        RHIDeviceCapabilities unprobed = RHIDeviceCapabilities.CreateUnprobed("ADR-0065");
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            unprobed.PipelineCache.NativeCache.Tier);

        if (!FeatureContractContext.TryCreateInstance(
                ERHIBackend.Metal,
                out RHIInstance? instance,
                out _))
        {
            return;
        }

        using (instance)
        {
            RHIDevice device = instance.GetDevice(0);
            RHICapability cache = device.Capabilities.PipelineCache.NativeCache;
            Assert.Equal(ERHICapabilityTier.Unavailable, cache.Tier);
            Assert.Contains("ADR-0065", cache.UnavailableReason, StringComparison.Ordinal);
            Assert.DoesNotContain("temp-file", cache.UnavailableReason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UrlArchive", cache.UnavailableReason, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<NotSupportedException>(device.CreatePipelineCache);
        }
    }

    [Fact]
    public void CoreFenceAndSemaphore_RemainBinaryWithoutValue()
    {
        Assert.Null(
            typeof(RHIFence).GetProperty(
                "Value",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Null(
            typeof(RHISemaphore).GetProperty(
                "Value",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact]
    public void FunctionLibraryReusableClassBits_IntersectPipelineCapabilities()
    {
        foreach (ERHIBackend backend in new[]
                 {
                     ERHIBackend.DirectX12,
                     ERHIBackend.Vulkan,
                     ERHIBackend.Metal,
                 })
        {
            if (!FeatureContractContext.TryCreateInstance(backend, out RHIInstance? instance, out _))
            {
                continue;
            }

            using (instance)
            {
                RHIDevice device = instance.GetDevice(0);
                ulong reusable = device.Capabilities.FunctionLibrary.ReusablePipelineClasses;
                if ((reusable & (ulong)ERHIFunctionLibraryReusablePipelineClass.Raytracing) != 0)
                {
                    Assert.NotEqual(
                        ERHICapabilityTier.Unavailable,
                        device.Capabilities.RayTracing.Pipeline.Tier);
                }

                if ((reusable & (ulong)ERHIFunctionLibraryReusablePipelineClass.WorkGraph) != 0)
                {
                    Assert.NotEqual(
                        ERHICapabilityTier.Unavailable,
                        device.Capabilities.WorkGraph.Execution.Tier);
                }

                if (backend == ERHIBackend.DirectX12)
                {
                    Assert.Equal(
                        0UL,
                        reusable & (ulong)ERHIFunctionLibraryReusablePipelineClass.Raster);
                    Assert.Equal(
                        0UL,
                        reusable & (ulong)ERHIFunctionLibraryReusablePipelineClass.Compute);
                }
            }
        }
    }

    private sealed class TestFunctionLibrary : RHIFunctionLibrary
    {
        private IntPtr m_Payload;

        public TestFunctionLibrary(byte[] payload, ERHIShaderPayloadKind kind)
        {
            m_Payload = Marshal.AllocHGlobal(payload.Length);
            Marshal.Copy(payload, 0, m_Payload, payload.Length);
            BindLibraryPayload(new RHIFunctionLibraryDescriptor
            {
                ByteCode = m_Payload,
                ByteSize = checked((uint)payload.Length),
                PayloadKind = kind,
            });
        }

        public override RHIFunction CreateFunction(in RHIFunctionViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new TestLibraryView(this, descriptor);
        }

        protected override void Release()
        {
            if (m_Payload != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_Payload);
                m_Payload = IntPtr.Zero;
            }
        }
    }

    private sealed class TestLibraryView : RHIFunction
    {
        public TestLibraryView(RHIFunctionLibrary library, in RHIFunctionViewDescriptor view)
        {
            BindLibraryViewSource(library, view, library.Descriptor.PayloadKind, library.ContentDigest);
        }

        protected override void Release()
        {
        }
    }

    private sealed class TestDirectFunction : RHIFunction
    {
        private IntPtr m_ByteCode;

        public TestDirectFunction(
            ERHIFunctionType type,
            string entryName,
            byte[] byteCode,
            ERHIShaderPayloadKind payloadKind = ERHIShaderPayloadKind.Dxil)
        {
            m_ByteCode = Marshal.AllocHGlobal(byteCode.Length);
            Marshal.Copy(byteCode, 0, m_ByteCode, byteCode.Length);
            BindDirectBytecodeSource(new RHIFunctionDescriptor
            {
                ByteSize = checked((uint)byteCode.Length),
                ByteCode = m_ByteCode,
                EntryName = entryName,
                Type = type,
                PayloadKind = payloadKind,
            });
        }

        protected override void Release()
        {
            if (m_ByteCode != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_ByteCode);
                m_ByteCode = IntPtr.Zero;
            }
        }
    }

    private sealed class TestPipelineLayout : RHIPipelineLayout
    {
        public TestPipelineLayout(uint pushConstantSize)
        {
            InitializePipelineCacheIdentity(new RHIPipelineLayoutDescriptor
            {
                PushConstantSize = pushConstantSize,
                BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
            });
        }

        protected override void Release()
        {
        }
    }
}
