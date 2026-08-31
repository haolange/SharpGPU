using System;
using System.Diagnostics.CodeAnalysis;
using SharpGPU;
#if SHARPGPU_ENABLE_DX12
using Vortice.Direct3D;
using Vortice.Direct3D12;
#endif
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUFeatureContractMatrixTests
{
    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void VariableRateShading_PerDrawUnavailable_SetShadingRateFailClosedWithoutDevice()
    {
        RHICapability perDraw = RHICapability.Unavailable(
            "per-draw variable-rate shading is unavailable.",
            ERHICapabilityProbeKind.NativeExtensionQuery,
            "portable VRS SetShadingRate guard");
        RHICapability combiners = RHICapability.Unavailable(
            "combiners unused when per-draw is unavailable.",
            ERHICapabilityProbeKind.NativeExtensionQuery,
            "portable VRS SetShadingRate guard");

        Assert.Throws<NotSupportedException>(
            () => VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
                perDraw,
                combiners,
                ERHIShadingRate.Rate1x1,
                ERHIShadingRateCombiner.Passthrough));
    }

#if SHARPGPU_ENABLE_DX12
    [Fact]
    public void Dx12_TimestampQuery_ShouldCreateExecuteSubmitAndReadback()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            if (context.Device.Capabilities.Synchronization.TimestampQueries.Tier == ERHICapabilityTier.Unavailable)
            {
                Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
                {
                    Count = 1,
                    Type = ERHIQueryType.Timestamp,
                }));
                return;
            }

            using RHIQuery query = context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.Timestamp,
            });
            using RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();

            commandBuffer.Begin("conformance.query.timestamp");
            commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
            {
                Name = "conformance.query.timestamp.write",
                Timestamp = new RHITimestampDescriptor
                {
                    Query = query,
                    BeginIndex = 0,
                    EndIndex = 1,
                },
            });
            commandBuffer.EndTransferPass();

            RHITransferEncoder resolveEncoder = commandBuffer.BeginTransferPass(
                new RHITransferPassDescriptor
                {
                    Name = "conformance.query.timestamp.resolve",
                });
            resolveEncoder.ResolveQuery(query, 0, 2);
            commandBuffer.EndTransferPass();
            commandBuffer.End();

            context.Fence.Reset();
            context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: context.Fence));
            context.Fence.Wait();

            Assert.Equal(ERHIQueryResultStatus.Ready, query.ResolveData());
            Assert.True(query.Results.Length >= 1);
        }
    }

    [Fact]
    public void Dx12_DescriptorArrayBindingTable_ShouldCreateAndUpdateArraySlots()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            using RHIBindingTableLayout layout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    new RHIBindingTableLayoutElement
                    {
                        Slot = 0,
                        Count = 4,
                        Type = ERHIBindType.StorageBuffer,
                        Stages = ERHIShaderStageMask.Compute,
                    },
                },
            });
            using RHIBuffer buffer = CreateBuffer(context.Device, 1024, ERHIBufferUsage.UnorderedAccess, ERHIStorageMode.GPULocal);
            using RHIBufferView view = buffer.CreateBufferView(new RHIBufferViewDescriptor
            {
                Count = 256,
                Offset = 0,
                Stride = sizeof(uint),
                ViewType = ERHIBufferViewType.UnorderedAccess,
            });

            RHIBindingTableElement element = new() { BufferView = view };
            using RHIBindingTable table = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = new[] { element },
            });

            Dx12BindingTable dx12Table = Assert.IsType<Dx12BindingTable>(table);
            Assert.Equal(1, dx12Table.GroupCount);
            Assert.NotEqual(default, dx12Table.GetGroupGpuHandle(0));
            table.SetBindElement(element, ERHIBindType.StorageBuffer, 0, 0);
            table.SetBindElement(element, ERHIBindType.StorageBuffer, 0, 1);
            table.SetBindElement(element, ERHIBindType.StorageBuffer, 0, 2);
            table.SetBindElement(element, ERHIBindType.StorageBuffer, 0, 3);
        }
    }

    [Fact]
    public void Dx12_PipelineCache_ShouldExportImportAndRejectCorruption()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            using RHIPipelineCache coldCache = context.Device.CreatePipelineCache();
            byte[] blob = coldCache.Export();
            Assert.NotEmpty(blob);

            using RHIPipelineCache restartedCache = context.Device.CreatePipelineCache();
            RHIPipelineCacheImportResult loaded = restartedCache.Import(blob);
            Assert.Equal(ERHIPipelineCacheImportStatus.Loaded, loaded.Status);

            byte[] corrupt = (byte[])blob.Clone();
            corrupt[^1] ^= 0x5a;
            RHIPipelineCacheImportResult rejected = restartedCache.Import(corrupt);
            Assert.Equal(ERHIPipelineCacheImportStatus.Corrupt, rejected.Status);

            RHIPipelineCacheImportResult emptied =
                restartedCache.Import(ReadOnlyMemory<byte>.Empty);
            Assert.Equal(ERHIPipelineCacheImportStatus.Empty, emptied.Status);
        }
    }

    [Fact]
    public void Dx12_MeshFeatureFlag_ShouldBeFalseUntilNativePipelineConformanceExists()
    {
        if (!FeatureContractContext.TryCreateDx12Instance(out RHIInstance? instance, out _))
        {
            return;
        }

        using (instance)
        {
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice device = instance.GetDevice(i);
                Assert.Equal(
                    ERHICapabilityTier.Unavailable,
                    device.Capabilities.Mesh.Shader.Tier);

                using RHIPipelineLayout pipelineLayout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
                {
                    bLocalSignature = false,
                    bUseVertexLayout = false,
                    PushConstantSize = 0,
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                });
                RHIStencilStateDescriptor keepStencilFace = new()
                {
                    ComparisonMode = ERHIComparisonMode.Always,
                    StencilPassOp = ERHIStencilOp.Keep,
                    StencilFailOp = ERHIStencilOp.Keep,
                    StencilDepthFailOp = ERHIStencilOp.Keep,
                };
                RHIRasterPipelineDescriptor descriptor = new()
                {
                    SampleCount = ERHISampleCount.None,
                    ColorFormats = Array.Empty<ERHIPixelFormat>(),
                    PipelineLayout = pipelineLayout,
                    RenderState = new RHIRenderStateDescriptor
                    {
                        DepthStencilState =
                            new RHIDepthStencilStateDescriptor
                            {
                                ComparisonMode = ERHIComparisonMode.Always,
                                FrontFace = keepStencilFace,
                                BackFace = keepStencilFace,
                            },
                    },
                    PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
                    {
                        MeshletAssembler = new RHIMeshletAssemblerDescriptor(null!, null!),
                    },
                };

                Assert.Throws<NotSupportedException>(() => device.CreateRasterPipeline(descriptor));
            }
        }
    }

    [Fact]
    public void Dx12_RaytracingUnsupportedFeature_ShouldThrowNotSupported()
    {
        if (!FeatureContractContext.TryCreateDx12Instance(out RHIInstance? instance, out _))
        {
            return;
        }

        using (instance)
        {
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice device = instance.GetDevice(i);
                if (device.Capabilities.RayTracing.Pipeline.Tier != ERHICapabilityTier.Unavailable)
                {
                    continue;
                }

                Assert.Throws<NotSupportedException>(() => device.CreateRaytracingPipeline(default));
            }
        }
    }

    [Fact]
    public void Dx12_MLUnsupportedFeature_ShouldThrowNotSupported()
    {
        if (!FeatureContractContext.TryCreateDx12Instance(out RHIInstance? instance, out _))
        {
            return;
        }

        using (instance)
        {
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice device = instance.GetDevice(i);
                if (device.Capabilities.MachineLearning.Execution.Tier != ERHICapabilityTier.Unavailable)
                {
                    continue;
                }

                Assert.Throws<NotSupportedException>(() => device.CreateMLPipeline(default));
                Assert.Throws<NotSupportedException>(() => device.CreateMLBindingTable(default));
                Assert.Throws<NotSupportedException>(() => device.CreateTensor(default));
            }
        }
    }
#endif

#if SHARPGPU_METAL_QUALIFICATION_HOST
    [Fact]
    [Trait("Category", "SharpGpuMetalQualified")]
    public void Metal_VariableRateShading_ShouldReportUnavailable()
    {
        Assert.True(
            OperatingSystem.IsMacOS(),
            "SharpGpuMetalQualified requires a Metal qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.Metal,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.Metal,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIRasterCapabilities raster = instance.GetDevice(i).Capabilities.Raster;
            Assert.Equal(ERHICapabilityTier.Unavailable, raster.VariableRateShadingPerDraw.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, raster.VariableRateShadingPerPrimitive.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, raster.VariableRateShadingAttachment.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, raster.VariableRateShadingCombiners.Tier);
            Assert.Equal(
                ERHICapabilityProbeKind.BackendContract,
                raster.VariableRateShadingPerDraw.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.BackendContract,
                raster.VariableRateShadingPerPrimitive.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.BackendContract,
                raster.VariableRateShadingAttachment.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.BackendContract,
                raster.VariableRateShadingCombiners.Provenance.Kind);
            AssertVariableRateShadingInvariants(raster, ERHIBackend.Metal);
        }
    }
#endif

#if SHARPGPU_ENABLE_DX12
    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_VariableRateShading_ShouldUseOptions6ProbeWithoutContradictions()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            SurfaceKind = ERHINativeSurfaceKind.Headless,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        const string options6Source = "D3D12_FEATURE_D3D12_OPTIONS6.VariableShadingRateTier";
        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIRasterCapabilities raster = instance.GetDevice(i).Capabilities.Raster;
            Assert.Equal(options6Source, raster.VariableRateShadingPerDraw.Provenance.Source);
            Assert.Equal(options6Source, raster.VariableRateShadingPerPrimitive.Provenance.Source);
            Assert.Equal(options6Source, raster.VariableRateShadingAttachment.Provenance.Source);
            Assert.Equal(options6Source, raster.VariableRateShadingCombiners.Provenance.Source);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                raster.VariableRateShadingPerDraw.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                raster.VariableRateShadingPerPrimitive.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                raster.VariableRateShadingAttachment.Provenance.Kind);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                raster.VariableRateShadingCombiners.Provenance.Kind);
            AssertVariableRateShadingInvariants(raster, ERHIBackend.DirectX12);
        }
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_VariableRateShading_ShouldMatchIndependentOptions6Oracle()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            SurfaceKind = ERHINativeSurfaceKind.Headless,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
            FeatureDataD3D12Options6 options6 = default;
            Assert.True(
                dx12Device.NativeDevice.CheckFeatureSupport(
                    Feature.Options6,
                    ref options6),
                "Independent VRS oracle requires D3D12_FEATURE_D3D12_OPTIONS6.");

            RHIRasterCapabilities raster = device.Capabilities.Raster;
            bool expectPerDraw =
                options6.VariableShadingRateTier != VariableShadingRateTier.NotSupported;
            bool expectTier2 =
                options6.VariableShadingRateTier == VariableShadingRateTier.Tier2;
            ERHICapabilityTier expectedPerDrawTier = options6.VariableShadingRateTier switch
            {
                VariableShadingRateTier.Tier1 => ERHICapabilityTier.Tier1,
                VariableShadingRateTier.Tier2 => ERHICapabilityTier.Tier2,
                _ => ERHICapabilityTier.Unavailable,
            };

            Assert.Equal(expectedPerDrawTier, raster.VariableRateShadingPerDraw.Tier);
            Assert.Equal(
                expectTier2 ? ERHICapabilityTier.Tier2 : ERHICapabilityTier.Unavailable,
                raster.VariableRateShadingPerPrimitive.Tier);
            Assert.Equal(
                expectTier2 ? ERHICapabilityTier.Tier2 : ERHICapabilityTier.Unavailable,
                raster.VariableRateShadingAttachment.Tier);
            Assert.Equal(
                expectTier2 ? ERHICapabilityTier.Tier2 : ERHICapabilityTier.Unavailable,
                raster.VariableRateShadingCombiners.Tier);

            if (expectPerDraw)
            {
                ulong expectedRateMask =
                    (1UL << (byte)ERHIShadingRate.Rate1x1) |
                    (1UL << (byte)ERHIShadingRate.Rate1x2) |
                    (1UL << (byte)ERHIShadingRate.Rate2x1) |
                    (1UL << (byte)ERHIShadingRate.Rate2x2);
                if (options6.AdditionalShadingRatesSupported)
                {
                    expectedRateMask |=
                        (1UL << (byte)ERHIShadingRate.Rate2x4) |
                        (1UL << (byte)ERHIShadingRate.Rate4x2) |
                        (1UL << (byte)ERHIShadingRate.Rate4x4);
                }

                Assert.True(
                    raster.VariableRateShadingPerDraw.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedShadingRateMask,
                        out ulong reportedRateMask));
                Assert.Equal(expectedRateMask, reportedRateMask);
            }

            if (expectTier2)
            {
                ulong expectedCombinerMask =
                    (1UL << (byte)ERHIShadingRateCombiner.Min) |
                    (1UL << (byte)ERHIShadingRateCombiner.Max) |
                    (1UL << (byte)ERHIShadingRateCombiner.Sum) |
                    (1UL << (byte)ERHIShadingRateCombiner.Override) |
                    (1UL << (byte)ERHIShadingRateCombiner.Passthrough);
                Assert.True(
                    raster.VariableRateShadingCombiners.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedShadingRateCombinerMask,
                        out ulong reportedCombinerMask));
                Assert.Equal(expectedCombinerMask, reportedCombinerMask);

                ulong tileSize = options6.ShadingRateImageTileSize;
                Assert.True(
                    raster.VariableRateShadingAttachment.Limits.TryGetValue(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMin,
                        out ulong tileWidthMin));
                Assert.True(
                    raster.VariableRateShadingAttachment.Limits.TryGetValue(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMin,
                        out ulong tileHeightMin));
                Assert.True(
                    raster.VariableRateShadingAttachment.Limits.TryGetValue(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMax,
                        out ulong tileWidthMax));
                Assert.True(
                    raster.VariableRateShadingAttachment.Limits.TryGetValue(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMax,
                        out ulong tileHeightMax));
                Assert.Equal(tileSize, tileWidthMin);
                Assert.Equal(tileSize, tileHeightMin);
                Assert.Equal(tileSize, tileWidthMax);
                Assert.Equal(tileSize, tileHeightMax);
            }

            AssertVariableRateShadingInvariants(raster, ERHIBackend.DirectX12);
        }
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_AtomicUInt64_ShouldMatchIndependentOptions9Oracle()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            SurfaceKind = ERHINativeSurfaceKind.Headless,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
            using ID3D12Device oracleDevice = CreateIndependentDx12Device(dx12Device);
            FeatureDataD3D12Options9 options9 = default;
            bool options9Supported = oracleDevice.CheckFeatureSupport(
                Feature.Options9,
                ref options9);
            bool expectAvailable = Dx12AtomicUInt64CapabilityFactory.IsAvailable(
                options9Supported,
                options9.AtomicInt64OnTypedResourceSupported,
                options9.AtomicInt64OnGroupSharedSupported);
            RHICapability atomic = device.Capabilities.Binding.AtomicUInt64;

            Assert.Equal(
                Dx12AtomicUInt64CapabilityFactory.ProbeSource,
                atomic.Provenance.Source);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                atomic.Provenance.Kind);
            Assert.Equal(
                expectAvailable ? ERHICapabilityTier.Tier1 : ERHICapabilityTier.Unavailable,
                atomic.Tier);
            if (!expectAvailable)
            {
                Assert.Equal(
                    Dx12AtomicUInt64CapabilityFactory.CreateUnavailableReason(
                        options9Supported,
                        options9.AtomicInt64OnTypedResourceSupported,
                        options9.AtomicInt64OnGroupSharedSupported),
                    atomic.UnavailableReason);
            }
        }
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Dx12_UnifiedMemory_ShouldMatchIndependentArchitecture1Oracle()
    {
        Assert.True(
            OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.DirectX12,
            SurfaceKind = ERHINativeSurfaceKind.Headless,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            Dx12Device dx12Device = Assert.IsType<Dx12Device>(device);
            using ID3D12Device oracleDevice = CreateIndependentDx12Device(dx12Device);
            FeatureDataArchitecture1 architecture1 = default;
            architecture1.NodeIndex = 0;
            bool architecture1Supported = oracleDevice.CheckFeatureSupport(
                Feature.Architecture1,
                ref architecture1);
            bool expectUma = architecture1Supported && architecture1.Uma;
            RHICapability unifiedMemory = device.Capabilities.Memory.UnifiedMemory;

            Assert.Equal(
                Dx12UnifiedMemoryCapabilityFactory.ProbeSource,
                unifiedMemory.Provenance.Source);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeFeatureQuery,
                unifiedMemory.Provenance.Kind);
            Assert.Equal(
                expectUma ? ERHICapabilityTier.Tier1 : ERHICapabilityTier.Unavailable,
                unifiedMemory.Tier);
            if (!expectUma)
            {
                Assert.Equal(
                    architecture1Supported
                        ? Dx12UnifiedMemoryCapabilityFactory.UmaFalseReason
                        : Dx12UnifiedMemoryCapabilityFactory.QueryFailedReason,
                    unifiedMemory.UnavailableReason);
            }
        }
    }

    private static ID3D12Device CreateIndependentDx12Device(Dx12Device dx12Device)
    {
        SharpGen.Runtime.Result result = D3D12.D3D12CreateDevice(
            dx12Device.DXGIAdapter,
            FeatureLevel.Level_12_0,
            out ID3D12Device? oracleDevice);
        Assert.True(
            result.Success && oracleDevice != null,
            "Independent D3D12 oracle device creation must succeed on a Windows DX12 host.");
        return oracleDevice;
    }
#endif

#if SHARPGPU_VULKAN_QUALIFICATION_HOST
    [Fact]
    [Trait("Category", "SharpGpuVulkanQualified")]
    public void Vulkan_VariableRateShading_ShouldMatchNativeQueryAndBackendContractProvenance()
    {
        Assert.True(
            OperatingSystem.IsWindows() ||
            OperatingSystem.IsLinux() ||
            OperatingSystem.IsAndroid(),
            "SharpGpuVulkanQualified requires a Vulkan qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.Vulkan,
                out string backendReason),
            backendReason);

        using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
        {
            Backend = ERHIBackend.Vulkan,
            SurfaceKind = ERHINativeSurfaceKind.Headless,
            EnableDebugLayer = false,
            EnableValidation = false,
            ComputeQueueRequestCount = 0,
            TransferQueueRequestCount = 0,
            GraphicsQueueRequestCount = 1,
        });
        Assert.NotNull(instance);
        Assert.True(instance.DeviceCount > 0);

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIRasterCapabilities raster = instance.GetDevice(i).Capabilities.Raster;
            AssertVulkanVariableRateShadingProbeKind(raster.VariableRateShadingPerDraw);
            AssertVulkanVariableRateShadingProbeKind(raster.VariableRateShadingPerPrimitive);
            AssertVulkanVariableRateShadingProbeKind(raster.VariableRateShadingAttachment);
            AssertVulkanVariableRateShadingProbeKind(raster.VariableRateShadingCombiners);
            AssertVariableRateShadingInvariants(raster, ERHIBackend.Vulkan);
        }
    }
#endif

    private static void AssertVariableRateShadingInvariants(
        in RHIRasterCapabilities raster,
        ERHIBackend backend)
    {
        AssertVariableRateShadingCapabilityState(raster.VariableRateShadingPerDraw);
        AssertVariableRateShadingCapabilityState(raster.VariableRateShadingPerPrimitive);
        AssertVariableRateShadingCapabilityState(raster.VariableRateShadingAttachment);
        AssertVariableRateShadingCapabilityState(raster.VariableRateShadingCombiners);

        if (raster.VariableRateShadingPerDraw.Tier == ERHICapabilityTier.Unavailable)
        {
            RHICapability perDraw = raster.VariableRateShadingPerDraw;
            RHICapability combiners = raster.VariableRateShadingCombiners;
            Assert.Throws<NotSupportedException>(
                () => VulkanVariableRateShadingCommandPolicy.ValidateSetShadingRate(
                    perDraw,
                    combiners,
                    ERHIShadingRate.Rate1x1,
                    ERHIShadingRateCombiner.Passthrough));
        }

        if (raster.VariableRateShadingCombiners.Tier != ERHICapabilityTier.Unavailable)
        {
            Assert.True(
                raster.VariableRateShadingPerPrimitive.Tier != ERHICapabilityTier.Unavailable ||
                raster.VariableRateShadingAttachment.Tier != ERHICapabilityTier.Unavailable);
        }

        AssertVariableRateShadingProvenance(raster.VariableRateShadingPerDraw, backend);
        AssertVariableRateShadingProvenance(raster.VariableRateShadingPerPrimitive, backend);
        AssertVariableRateShadingProvenance(raster.VariableRateShadingAttachment, backend);
        AssertVariableRateShadingProvenance(raster.VariableRateShadingCombiners, backend);

        if (raster.VariableRateShadingPerDraw.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateMask,
                out ulong rateMask))
        {
            Assert.NotEqual(0UL, rateMask & (1UL << (byte)ERHIShadingRate.Rate1x1));
            Assert.Equal(0UL, rateMask & (1UL << (byte)ERHIShadingRate.Pending));
        }

        if (raster.VariableRateShadingCombiners.Limits.TryGetValue(
                ERHICapabilityLimitKind.SupportedShadingRateCombinerMask,
                out ulong combinerMask))
        {
            Assert.Equal(0UL, combinerMask & (1UL << (byte)ERHIShadingRateCombiner.Pending));
            if (backend == ERHIBackend.Vulkan)
            {
                Assert.Equal(0UL, combinerMask & (1UL << (byte)ERHIShadingRateCombiner.Sum));
            }
        }
    }

    private static void AssertVariableRateShadingCapabilityState(in RHICapability capability)
    {
        if (capability.Tier == ERHICapabilityTier.Unavailable)
        {
            Assert.Equal(ERHICapabilityStrategy.Unavailable, capability.Strategy);
            Assert.False(string.IsNullOrWhiteSpace(capability.UnavailableReason));
            return;
        }

        Assert.NotEqual(ERHICapabilityStrategy.Unavailable, capability.Strategy);
        Assert.True(string.IsNullOrEmpty(capability.UnavailableReason));
    }

    private static void AssertVariableRateShadingProvenance(
        in RHICapability capability,
        ERHIBackend backend)
    {
        Assert.False(string.IsNullOrWhiteSpace(capability.Provenance.Source));
        switch (backend)
        {
            case ERHIBackend.DirectX12:
                Assert.Equal(
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    capability.Provenance.Kind);
                break;
            case ERHIBackend.Metal:
                Assert.Equal(
                    ERHICapabilityProbeKind.BackendContract,
                    capability.Provenance.Kind);
                break;
            case ERHIBackend.Vulkan:
                AssertVulkanVariableRateShadingProbeKind(capability);
                break;
            default:
                Assert.Fail($"Unsupported VRS provenance backend {backend}.");
                break;
        }
    }

    private static void AssertVulkanVariableRateShadingProbeKind(in RHICapability capability)
    {
        bool backendContractSource = capability.Provenance.Source.Contains(
            "SharpGPU Vulkan",
            StringComparison.Ordinal);
        if (backendContractSource)
        {
            Assert.Equal(
                ERHICapabilityProbeKind.BackendContract,
                capability.Provenance.Kind);
            return;
        }

        Assert.True(
            capability.Provenance.Kind == ERHICapabilityProbeKind.NativeExtensionQuery ||
            capability.Provenance.Kind == ERHICapabilityProbeKind.NativeFeatureQuery,
            $"Vulkan VRS source '{capability.Provenance.Source}' must use a native query kind, not {capability.Provenance.Kind}.");
    }

    [Fact]
    public void Vulkan_MLUnsupportedContract_ShouldReportUnavailableAndThrow()
    {
        if (!FeatureContractContext.TryCreateInstance(ERHIBackend.Vulkan, out RHIInstance? instance, out _))
        {
            return;
        }

        using (instance)
        {
            for (int i = 0; i < instance.DeviceCount; ++i)
            {
                RHIDevice device = instance.GetDevice(i);
                Assert.Equal(
                    ERHICapabilityTier.Unavailable,
                    device.Capabilities.MachineLearning.Execution.Tier);
                Assert.Throws<NotSupportedException>(() => device.CreateMLPipeline(default));
                Assert.Throws<NotSupportedException>(() => device.CreateMLBindingTable(default));
                Assert.Throws<NotSupportedException>(() => device.CreateTensor(default));
            }
        }
    }

    private static RHIBuffer CreateBuffer(RHIDevice device, int byteSize, ERHIBufferUsage usage, ERHIStorageMode storageMode)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = usage,
            StorageMode = storageMode,
        });
    }
}

internal sealed class FeatureContractContext : IDisposable
{
    public RHIInstance Instance { get; }
    public RHIDevice Device { get; }
    public RHICommandQueue CommandQueue { get; }
    public RHIFence Fence { get; }

    private FeatureContractContext(RHIInstance instance, RHIDevice device, RHICommandQueue commandQueue, RHIFence fence)
    {
        Instance = instance;
        Device = device;
        CommandQueue = commandQueue;
        Fence = fence;
    }

    public static bool TryCreateDx12([NotNullWhen(true)] out FeatureContractContext? context, out string reason)
    {
        context = null;
        if (!TryCreateDx12Instance(out RHIInstance? instance, out reason))
        {
            return false;
        }

        for (int i = 0; i < instance.DeviceCount; ++i)
        {
            RHIDevice device = instance.GetDevice(i);
            RHICommandQueue? queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0);
            if (queue == null)
            {
                continue;
            }

            context = new FeatureContractContext(instance, device, queue, device.CreateFence());
            return true;
        }

        reason = "DX12 graphics queue is unavailable.";
        instance.Dispose();
        return false;
    }

    public static bool TryCreateDx12Instance([NotNullWhen(true)] out RHIInstance? instance, out string reason)
    {
        return TryCreateInstance(ERHIBackend.DirectX12, out instance, out reason);
    }

    public static bool TryCreateInstance(ERHIBackend backend, [NotNullWhen(true)] out RHIInstance? instance, out string reason)
    {
        instance = null;
        reason = string.Empty;

        if (!RHIInstance.IsBackendSupported(backend, out reason))
        {
            return false;
        }

        try
        {
            instance = RHIInstance.Create(new RHIInstanceDescriptor
            {
                Backend = backend,
                SurfaceKind = ERHINativeSurfaceKind.Headless,
                EnableDebugLayer = false,
                EnableValidation = false,
                ComputeQueueRequestCount = 0,
                TransferQueueRequestCount = 0,
                GraphicsQueueRequestCount = 1,
            });
            return instance != null;
        }
        catch (Exception ex) when (ex is NotSupportedException or DllNotFoundException or InvalidOperationException)
        {
            reason = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        Fence.Dispose();
        Instance.Dispose();
    }
}
