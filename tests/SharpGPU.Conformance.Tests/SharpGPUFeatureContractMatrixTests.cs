using System;
using System.Diagnostics.CodeAnalysis;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUFeatureContractMatrixTests
{
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
            commandBuffer.TimestampQueryHeap = query;
            RHITransferEncoder encoder = commandBuffer.BeginTransferPass(new RHITransferPassDescriptor
            {
                Name = "conformance.query.timestamp.transfer",
            });
            encoder.WriteTimestamp(0);
            encoder.ResolveQuery(query, 0, 1);
            commandBuffer.TimestampQueryHeap = null;
            commandBuffer.EndTransferPass();
            commandBuffer.End();

            context.Fence.Reset();
            context.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: context.Fence));
            context.Fence.Wait();

            Assert.True(query.ResolveData());
            Assert.True(query.Results.Length >= 1);
        }
    }

    [Fact]
    public void Dx12_BindlessArgumentTable_ShouldCreateAndUpdateArraySlots()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            using RHIArgumentTableLayout layout = context.Device.CreateArgumentTableLayout(new RHIArgumentTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    new RHIArgumentTableLayoutElement
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

            RHIArgumentTableElement element = new() { BufferView = view };
            using RHIArgumentTable table = context.Device.CreateArgumentTable(new RHIArgumentTableDescriptor
            {
                Layout = layout,
                Elements = new[] { element },
            });

            Dx12ArgumentTable dx12Table = Assert.IsType<Dx12ArgumentTable>(table);
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
                    ArgumentTableLayouts = Array.Empty<RHIArgumentTableLayout>(),
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
                        MeshletAssembler = new RHIMeshletAssemblerDescriptor(null, null),
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
                Assert.Throws<NotSupportedException>(() => device.CreateMLBindingSet(default));
                Assert.Throws<NotSupportedException>(() => device.CreateTensor(default));
            }
        }
    }
#endif

    [Fact]
    public void Vulkan_MLUnsupportedContract_ShouldReportFalseAndThrow()
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
                Assert.Throws<NotSupportedException>(() => device.CreateMLBindingSet(default));
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
