using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
            if (context.Device.Feature?.IsTimestampQueriesSupported != true)
            {
                Assert.Throws<NotSupportedException>(() => context.Device.CreateQuery(new RHIQueryDescriptor
                {
                    Count = 1,
                    Type = ERHIQueryType.TimestampGenerice,
                }));
                return;
            }

            using RHIQuery query = context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.TimestampGenerice,
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
            context.CommandQueue.Submit(commandBuffer, context.Fence, null!, null!);
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
                        Stage = ERHIShaderStage.Compute,
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
    public void Dx12_StorageQueueBuffer_ShouldSubmitAndReadBackMappableDestination()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        {
            byte[] expected =
            {
                0x10, 0x21, 0x32, 0x43,
                0x54, 0x65, 0x76, 0x87,
                0x98, 0xa9, 0xba, 0xcb,
                0xdc, 0xed, 0xfe, 0x0f,
            };
            string sourcePath = Path.Combine(Path.GetTempPath(), "SharpGPU", "storagequeue-source.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllBytes(sourcePath, expected);

            try
            {
                using RHIBuffer destination = CreateBuffer(context.Device, expected.Length, ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource, ERHIStorageMode.HostUpload);
                using RHIStorageQueue queue = context.Device.CreateStorageQueue();
                RHIStorageFileHandle fileHandle = queue.OpenFile(sourcePath);
                Assert.Equal((ulong)expected.Length, queue.QueryFileSize(fileHandle));

                queue.RequestBuffer(new RHIStorageBufferRequest
                {
                    FileHandle = fileHandle,
                    FileOffset = 0,
                    FileSize = (ulong)expected.Length,
                    DestinationBuffer = destination,
                    DestinationOffset = 0,
                });
                context.Fence.Reset();
                queue.Submit(context.Fence);
                context.Fence.Wait();
                queue.CloseFile(fileHandle);

                byte[] actual = new byte[expected.Length];
                IntPtr mapped = destination.Map(0, (uint)expected.Length);
                Marshal.Copy(mapped, actual, 0, actual.Length);
                destination.UnMap(0, 0);
                Assert.Equal(expected, actual);
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }
    }

    [Fact]
    public void Dx12_PipelineLibrary_ShouldCreateAndSerialize()
    {
        if (!FeatureContractContext.TryCreateDx12(out FeatureContractContext? context, out _))
        {
            return;
        }

        using (context)
        using (RHIPipelineLibrary library = context.Device.CreatePipelineLibrary(default))
        {
            RHIPipelineLibraryResult result = library.Serialize();
            try
            {
                Assert.True(result.ByteSize >= 0);
            }
            finally
            {
                if (result.ByteCode != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(result.ByteCode);
                }
            }
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
                Assert.False(device.Feature?.IsMeshShadingSupported == true, $"DX12 mesh feature must remain false until native mesh pipeline conformance exists on '{device.Name}'.");

                using RHIPipelineLayout pipelineLayout = device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
                {
                    bLocalSignature = false,
                    bUseVertexLayout = false,
                    PushConstantSize = 0,
                    ArgumentTableLayouts = Array.Empty<RHIArgumentTableLayout>(),
                });
                RHIRasterPipelineDescriptor descriptor = new()
                {
                    SampleCount = ERHISampleCount.None,
                    ColorFormats = Array.Empty<ERHIPixelFormat>(),
                    PipelineLayout = pipelineLayout,
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
                if (device.Feature?.IsRaytracingSupported == true)
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
                if (device.Feature?.IsMLSupported == true)
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
                Assert.False(device.Feature?.IsMLSupported == true, $"Vulkan ML v1 must remain false until VK_ARM_tensors/data_graph capability probing is implemented on '{device.Name}'.");
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
                EnableValidatior = false,
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
