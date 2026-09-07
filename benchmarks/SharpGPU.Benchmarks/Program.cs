using System;
using System.IO;
using SharpGPU;
using SharpGPU.Scopes;
using System.Diagnostics;
using SharpGPU.Builders;
using System.Globalization;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using SharpShader.Compilation;
using SharpShader.HLSLCrossCompiler;
using SharpShader.SharpGPU;

namespace SharpGPU.Benchmarks
{
    internal static class Program
    {
        private const int DefaultIterations = 10_000;
        private const int DefaultWarmup = 512;
        private const string StatusPassed = "passed";
        private const string StatusSkipped = "skipped";

        public static int Main(string[] args)
        {
            Options options = Options.Parse(args);
            List<BenchmarkCase> cases = CreateCases();
            List<BenchmarkResult> results = new(cases.Count);

            foreach (BenchmarkCase benchmarkCase in cases)
            {
                if (!options.ShouldRun(benchmarkCase))
                {
                    continue;
                }

                results.Add(Run(benchmarkCase, options));
            }

            WriteConsole(results);
            if (!string.IsNullOrWhiteSpace(options.CsvPath))
            {
                WriteCsv(options.CsvPath, results);
            }

            if (!string.IsNullOrWhiteSpace(options.JsonPath))
            {
                WriteJson(options.JsonPath, results);
            }

            if (options.FailOnRegression && !string.IsNullOrWhiteSpace(options.BaselinePath))
            {
                return CheckBaseline(options.BaselinePath, results);
            }

            return 0;
        }

        private static List<BenchmarkCase> CreateCases()
        {
            return new List<BenchmarkCase>
            {
                new("default_builder_buffer", "api", "none", "none", CreateDefaultBuilderBuffer),
                new("scoped_helper_transfer", "api", "none", "none", CreateScopedHelperTransfer),
                new("storage_queue_enqueue", "api", "none", "none", CreateStorageQueueEnqueue),
                new("sharpshader_generated_binding_lookup", "api", "none", "none", CreateSharpShaderGeneratedBindingLookup),
                new("dx12_command_begin_end_real", "backend", "dx12", "none", CreateDx12CommandBeginEnd),
                new("dx12_transfer_pass_begin_end_real", "backend", "dx12", "none", CreateDx12TransferPassBeginEnd),
                new("dx12_scoped_transfer_pass_real", "backend", "dx12", "none", CreateDx12ScopedTransferPass),
                new("dx12_barrier_encode_real", "backend", "dx12", "none", CreateDx12BarrierEncode),
                new("dx12_transfer_copy_encode_real", "backend", "dx12", "none", CreateDx12TransferCopyEncode),
                new("dx12_argument_table_update_real", "backend", "dx12", "none", CreateDx12BindingTableUpdate),
                new("dx12_argument_table_bind_real", "backend", "dx12", "none", CreateDx12BindingTableBind),
                new("dx12_timestamp_write_resolve_encode_real", "backend", "dx12", "none", CreateDx12TimestampEncode),
                new("dx12_workgraph_set_dispatch_encode_real", "backend", "dx12", "none", CreateDx12WorkGraphEncode),
                new("dx12_workload_bindless_heavy_record_real", "workload", "dx12", "none", CreateDx12WorkloadBindlessHeavyRecord),
                new("dx12_workload_upload_heavy_record_real", "workload", "dx12", "none", CreateDx12WorkloadUploadHeavyRecord),
                new("dx12_workload_workgraph_heavy_record_real", "workload", "dx12", "none", CreateDx12WorkloadWorkGraphHeavyRecord),
                new("dx12_empty_submit_fence_real", "submit", "dx12", "wait", CreateDx12EmptySubmitFence),
            };
        }

        private static BenchmarkResult Run(BenchmarkCase benchmarkCase, Options options)
        {
            using PreparedBenchmark prepared = benchmarkCase.Create(options);
            if (!string.IsNullOrWhiteSpace(prepared.SkipReason))
            {
                return BenchmarkResult.Skipped(benchmarkCase, options.Iterations, prepared.SkipReason);
            }

            Action body = prepared.Body ?? throw new InvalidOperationException($"Benchmark '{benchmarkCase.Name}' did not provide a body.");
            for (int i = 0; i < options.Warmup; ++i)
            {
                body();
            }

            long[] samples = new long[options.Iterations];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < options.Iterations; ++i)
            {
                long start = Stopwatch.GetTimestamp();
                body();
                samples[i] = Stopwatch.GetTimestamp() - start;
            }
            long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            long allocatedBytes = Math.Max(0, allocatedAfter - allocatedBefore);
            if (prepared.RequireZeroAllocation && allocatedBytes != 0)
            {
                throw new InvalidOperationException(
                    $"Benchmark '{benchmarkCase.Name}' requires zero steady-state managed allocation, but allocated {allocatedBytes} bytes.");
            }

            Array.Sort(samples);
            return BenchmarkResult.Passed(
                benchmarkCase,
                options.Iterations,
                Average(samples),
                Percentile(samples, 0.50),
                Percentile(samples, 0.95),
                Percentile(samples, 0.99),
                ToMicroseconds(samples[^1]),
                allocatedBytes,
                prepared.AdapterName);
        }

        private static PreparedBenchmark CreateDefaultBuilderBuffer(Options options)
        {
            return new PreparedBenchmark
            {
                Body = () => _ = RHIDescriptorDefaults.Buffer(256, ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource),
            };
        }

        private static PreparedBenchmark CreateScopedHelperTransfer(Options options)
        {
            FakeCommandBuffer commandBuffer = new();
            FakeBuffer source = new(RHIDescriptorDefaults.Buffer(256, ERHIBufferUsage.CopySrc, ERHIStorageMode.HostUpload));
            FakeBuffer target = new(RHIDescriptorDefaults.Buffer(256, ERHIBufferUsage.CopyDst));
            RHITransferPassDescriptor descriptor = RHIDescriptorDefaults.TransferPass("bench");
            return new PreparedBenchmark
            {
                Body = () =>
                {
                    using RHITransferPassScope pass = commandBuffer.BeginScopedTransferPass(descriptor);
                    pass.Encoder.CopyBufferToBuffer(source, 0, target, 0, 256);
                },
                Cleanup = () =>
                {
                    target.Dispose();
                    source.Dispose();
                    commandBuffer.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateStorageQueueEnqueue(Options options)
        {
            FakeStorageQueue queue = new(options.Iterations + options.Warmup + 16);
            RHIStorageBufferRequest request = new()
            {
                FileHandle = default,
                FileOffset = 0,
                FileSize = 4096,
                DestinationBuffer = null!,
                DestinationOffset = 0,
            };

            return new PreparedBenchmark
            {
                Body = () => queue.RequestBuffer(request),
                Cleanup = queue.Dispose,
            };
        }

        private static PreparedBenchmark CreateSharpShaderGeneratedBindingLookup(Options options)
        {
            ShaderBindingKey key = new(
                4,
                0,
                ShaderBindingClass.ShaderResource);
            ShaderInterfaceLayout logicalLayout = new(new[]
            {
                new ShaderLogicalBinding(
                    key,
                    "Input",
                    aliases: null,
                    new ShaderResourceShape(
                        ShaderResourceKind.StructuredBuffer,
                        ShaderResourceDimension.Buffer,
                        ShaderResourceAccess.ReadOnly,
                        structureStride: sizeof(uint)),
                    ShaderStageMask.Compute,
                    provenance: ShaderBindingProvenance.ExplicitSource),
            });
            ShaderBackendLayouts backendLayouts = new(
                logicalLayout.Signature,
                dx12: new Dx12ShaderBackendLayout(new[]
                {
                    new Dx12ShaderBindingMapping(
                        key,
                        key.Table,
                        key.Slot,
                        key.Type),
                }));
            SharpGpuBindingTableLayoutPlan plan =
                SharpGpuShaderInterfaceAdapter.CreateBindingTableLayoutPlan(
                    logicalLayout,
                    backendLayouts,
                    ERHIBackend.DirectX12);
            SharpGpuBindingLocation sink = plan.GetBinding(key);
            if (sink.LogicalBinding != key)
            {
                throw new InvalidOperationException(
                    "SharpShader generated-binding benchmark preflight resolved the wrong logical key.");
            }

            return new PreparedBenchmark
            {
                RequireZeroAllocation = true,
                Body = () => sink = plan.GetBinding(key),
            };
        }

        private static PreparedBenchmark CreateDx12CommandBeginEnd(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.command");
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12TransferPassBeginEnd(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHITransferPassDescriptor descriptor = new() { Name = "bench.transfer" };
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.transfer");
                    commandBuffer.BeginTransferPass(descriptor);
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12ScopedTransferPass(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHITransferPassDescriptor descriptor = RHIDescriptorDefaults.TransferPass("bench.scoped");
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.scoped");
                    using (commandBuffer.BeginScopedTransferPass(descriptor))
                    {
                    }
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12BarrierEncode(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHIBuffer barrierBuffer = CreateGpuBuffer(context.Device, 256, ERHIBufferUsage.CopyDst | ERHIBufferUsage.CopySrc);
            RHITransferPassDescriptor descriptor = new() { Name = "bench.barrier" };
            RHIBarrier barrier = RHIBarrier.Buffer(
                barrierBuffer,
                RHIBufferRange.Whole(),
                ERHIStageMask.None,
                ERHIStageMask.Transfer,
                ERHIAccessMask.None,
                ERHIAccessMask.TransferWrite);
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.barrier");
                    RHITransferEncoder encoder = commandBuffer.BeginTransferPass(descriptor);
                    encoder.Barrier(barrier);
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    commandBuffer.Dispose();
                    barrierBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12TransferCopyEncode(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHIBuffer source = CreateUploadBuffer(context.Device, 256);
            RHIBuffer destination = CreateReadbackBuffer(context.Device, 256);
            RHITransferPassDescriptor descriptor = new() { Name = "bench.copy" };
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.copy");
                    RHITransferEncoder encoder = commandBuffer.BeginTransferPass(descriptor);
                    encoder.CopyBufferToBuffer(source, 0, destination, 0, 256);
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    destination.Dispose();
                    source.Dispose();
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12BindingTableUpdate(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHIBindingTableLayout layout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    new RHIBindingTableLayoutElement
                    {
                        Slot = 0,
                        Count = 1,
                        Type = ERHIBindType.StorageBuffer,
                        Stages = ERHIShaderStageMask.Compute,
                    },
                },
            });
            RHIBuffer buffer = CreateGpuBuffer(context.Device, 256, ERHIBufferUsage.UnorderedAccess);
            RHIBufferView view = buffer.CreateBufferView(new RHIBufferViewDescriptor
            {
                Count = 64,
                Offset = 0,
                Stride = sizeof(uint),
                ViewType = ERHIBufferViewType.UnorderedAccess,
            });
            RHIBindingTableElement element = new() { BufferView = view };
            RHIBindingTable table = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = new[] { element },
            });

            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () => table.SetBindElement(element, ERHIBindType.StorageBuffer, 0),
                Cleanup = () =>
                {
                    table.Dispose();
                    view.Dispose();
                    buffer.Dispose();
                    layout.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12BindingTableBind(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHIBindingTableLayout layout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    new RHIBindingTableLayoutElement
                    {
                        Slot = 0,
                        Count = 1,
                        Type = ERHIBindType.StorageBuffer,
                        Stages = ERHIShaderStageMask.Compute,
                    },
                    new RHIBindingTableLayoutElement
                    {
                        Slot = 0,
                        Count = 1,
                        Type = ERHIBindType.Sampler,
                        Stages = ERHIShaderStageMask.Compute,
                    },
                },
            });
            RHIBuffer buffer = CreateGpuBuffer(context.Device, 256, ERHIBufferUsage.UnorderedAccess);
            RHIBufferView view = buffer.CreateBufferView(new RHIBufferViewDescriptor
            {
                Count = 64,
                Offset = 0,
                Stride = sizeof(uint),
                ViewType = ERHIBufferViewType.UnorderedAccess,
            });
            RHISampler sampler = context.Device.CreateSampler(new RHISamplerDescriptor
            {
                LodMin = 0,
                LodMax = 0,
                Anisotropy = 1,
                MinFilter = ERHIFilterMode.Point,
                MagFilter = ERHIFilterMode.Point,
                MipFilter = ERHIFilterMode.Point,
                AddressModeU = ERHIAddressMode.ClampToEdge,
                AddressModeV = ERHIAddressMode.ClampToEdge,
                AddressModeW = ERHIAddressMode.ClampToEdge,
                ComparisonMode = ERHIComparisonMode.Never,
            });
            RHIBindingTable table = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = new[]
                {
                    new RHIBindingTableElement { BufferView = view },
                    new RHIBindingTableElement { Sampler = sampler },
                },
            });
            RHIPipelineLayout pipelineLayout = context.Device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
            {
                BindingTableLayouts = new[] { layout },
            });
            RHIFunction function = CompileBindingTableBenchmarkFunction(context.Device);
            RHIComputePipeline pipeline = context.Device.CreateComputePipeline(new RHIComputePipelineDescriptor
            {
                ThreadSize = new SharpMath.uint3(1, 1, 1),
                ComputeFunction = function,
                PipelineLayout = pipelineLayout,
            });
            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            commandBuffer.Begin("SharpGPU.Benchmark.BindingTableBind");
            RHIComputeEncoder encoder = commandBuffer.BeginComputePass(new RHIComputePassDescriptor
            {
                Name = "bench.argument-table-bind",
            });
            encoder.SetPipeline(pipeline);

            Dx12BindingTable nativeTable = (Dx12BindingTable)table;
            Dx12PipelineLayout nativePipelineLayout = (Dx12PipelineLayout)pipelineLayout;
            Dx12CommandBuffer nativeCommandBuffer = (Dx12CommandBuffer)commandBuffer;
            int rootTableCallCount = Dx12BindingTableBinder.BindCompute(
                nativeCommandBuffer.NativeCommandList,
                nativePipelineLayout,
                table,
                nativeTable.BindingTableLayout.Index);
            if (rootTableCallCount != nativeTable.GroupCount)
            {
                throw new InvalidOperationException(
                    $"DX12 BindingTable binder emitted {rootTableCallCount} root-table calls for {nativeTable.GroupCount} compiled groups.");
            }

            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                RequireZeroAllocation = true,
                Body = () => encoder.SetBindingTable(table, nativeTable.BindingTableLayout.Index),
                Cleanup = () =>
                {
                    commandBuffer.EndComputePass();
                    commandBuffer.End();
                    commandBuffer.Dispose();
                    pipeline.Dispose();
                    function.Dispose();
                    pipelineLayout.Dispose();
                    table.Dispose();
                    sampler.Dispose();
                    view.Dispose();
                    buffer.Dispose();
                    layout.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12TimestampEncode(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHIQuery query = context.Device.CreateQuery(new RHIQueryDescriptor
            {
                Count = 2,
                Type = ERHIQueryType.Timestamp,
            });
            RHITransferPassDescriptor descriptor = new()
            {
                Name = "bench.timestamp",
                Timestamp = new RHITimestampDescriptor { Query = query, BeginIndex = 0, EndIndex = 1 },
            };

            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.timestamp");
                    RHITransferEncoder encoder = commandBuffer.BeginTransferPass(descriptor);
                    encoder.WriteTimestamp(0);
                    encoder.ResolveQuery(query, 0, 1);
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    query.Dispose();
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12WorkGraphEncode(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            if (context.Device.Capabilities.WorkGraph.Execution.Tier == ERHICapabilityTier.Unavailable)
            {
                string reason = $"DX12 WorkGraph is not supported by adapter '{context.Device.Name}'.";
                context.Dispose();
                return PreparedBenchmark.Skipped(reason);
            }

            WorkGraphEncodeFixture? fixture = null;
            try
            {
                fixture = WorkGraphEncodeFixture.Create(context);
            }
            catch (Exception ex) when (ex is ShaderCompilerException or NotSupportedException or InvalidOperationException)
            {
                context.Dispose();
                return PreparedBenchmark.Skipped($"DX12 WorkGraph setup failed: {ex.Message}");
            }

            WorkGraphEncodeFixture capturedFixture = fixture;
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    RHICommandBuffer commandBuffer = capturedFixture.NextCommandBuffer();
                    commandBuffer.Begin("bench.workgraph");
                    RHIWorkGraphEncoder encoder = commandBuffer.BeginWorkGraphPass(capturedFixture.PassDescriptor);
                    encoder.SetPipeline(capturedFixture.Pipeline);
                    encoder.SetBackingMemory(capturedFixture.BackingMemory, 0, (ulong)capturedFixture.BackingMemorySize);
                    encoder.DispatchGraph("WorkNode", 1, WorkGraphEncodeFixture.InputRecordStride, capturedFixture.InputRecordBuffer);
                    commandBuffer.EndWorkGraphPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    capturedFixture.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12WorkloadBindlessHeavyRecord(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            const int BindlessCount = 1024;
            RHIBindingTableLayout layout = context.Device.CreateBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = 0,
                Elements = new[]
                {
                    new RHIBindingTableLayoutElement
                    {
                        Slot = 0,
                        Count = BindlessCount,
                        Type = ERHIBindType.StorageBuffer,
                        Stages = ERHIShaderStageMask.Compute,
                    },
                },
            });
            RHIBuffer buffer = CreateGpuBuffer(context.Device, 4096, ERHIBufferUsage.UnorderedAccess);
            RHIBufferView view = buffer.CreateBufferView(new RHIBufferViewDescriptor
            {
                Count = 1024,
                Offset = 0,
                Stride = sizeof(uint),
                ViewType = ERHIBufferViewType.UnorderedAccess,
            });
            RHIBindingTableElement element = new() { BufferView = view };
            RHIBindingTable table = context.Device.CreateBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = new[] { element },
            });

            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    for (int i = 0; i < BindlessCount; ++i)
                    {
                        table.SetBindElement(element, ERHIBindType.StorageBuffer, 0, i);
                    }
                },
                Cleanup = () =>
                {
                    table.Dispose();
                    view.Dispose();
                    buffer.Dispose();
                    layout.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12WorkloadUploadHeavyRecord(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            const int CopyCount = 128;
            const int CopyBytes = 256;
            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHITransferPassDescriptor descriptor = new() { Name = "bench.workload.upload" };
            RHIBuffer[] sources = new RHIBuffer[CopyCount];
            RHIBuffer[] destinations = new RHIBuffer[CopyCount];

            for (int i = 0; i < CopyCount; ++i)
            {
                sources[i] = CreateUploadBuffer(context.Device, CopyBytes);
                destinations[i] = CreateGpuBuffer(context.Device, CopyBytes, ERHIBufferUsage.CopyDst | ERHIBufferUsage.CopySrc);
            }

            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.workload.upload");
                    RHITransferEncoder encoder = commandBuffer.BeginTransferPass(descriptor);
                    for (int i = 0; i < CopyCount; ++i)
                    {
                        encoder.CopyBufferToBuffer(sources[i], 0, destinations[i], 0, CopyBytes);
                    }
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    for (int i = 0; i < CopyCount; ++i)
                    {
                        destinations[i]?.Dispose();
                        sources[i]?.Dispose();
                    }
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12WorkloadWorkGraphHeavyRecord(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            if (context.Device.Capabilities.WorkGraph.Execution.Tier == ERHICapabilityTier.Unavailable)
            {
                string reason = $"DX12 WorkGraph is not supported by adapter '{context.Device.Name}'.";
                context.Dispose();
                return PreparedBenchmark.Skipped(reason);
            }

            const int DispatchCount = 64;
            WorkGraphEncodeFixture? fixture = null;
            try
            {
                fixture = WorkGraphEncodeFixture.Create(context);
            }
            catch (Exception ex) when (ex is ShaderCompilerException or NotSupportedException or InvalidOperationException)
            {
                context.Dispose();
                return PreparedBenchmark.Skipped($"DX12 WorkGraph setup failed: {ex.Message}");
            }

            WorkGraphEncodeFixture capturedFixture = fixture;
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    RHICommandBuffer commandBuffer = capturedFixture.NextCommandBuffer();
                    commandBuffer.Begin("bench.workload.workgraph");
                    RHIWorkGraphEncoder encoder = commandBuffer.BeginWorkGraphPass(capturedFixture.PassDescriptor);
                    encoder.SetPipeline(capturedFixture.Pipeline);
                    encoder.SetBackingMemory(capturedFixture.BackingMemory, 0, (ulong)capturedFixture.BackingMemorySize);
                    for (int i = 0; i < DispatchCount; ++i)
                    {
                        encoder.DispatchGraph("WorkNode", 1, WorkGraphEncodeFixture.InputRecordStride, capturedFixture.InputRecordBuffer);
                    }
                    commandBuffer.EndWorkGraphPass();
                    commandBuffer.End();
                },
                Cleanup = () =>
                {
                    capturedFixture.Dispose();
                    context.Dispose();
                },
            };
        }

        private static PreparedBenchmark CreateDx12EmptySubmitFence(Options options)
        {
            if (!Dx12BenchmarkContext.TryCreate(options, out Dx12BenchmarkContext? context, out string skipReason))
            {
                return PreparedBenchmark.Skipped(skipReason);
            }

            RHICommandBuffer commandBuffer = context.CommandQueue.CreateCommandBuffer();
            RHIQueueSubmitDescriptor submitDescriptor = new RHIQueueSubmitDescriptor(
                new RHICommandBuffer[] { commandBuffer },
                completionFence: context.Fence);
            return new PreparedBenchmark
            {
                AdapterName = context.Device.Name,
                Body = () =>
                {
                    commandBuffer.Begin("bench.submit");
                    commandBuffer.End();
                    context.Fence.Reset();
                    context.CommandQueue.Submit(in submitDescriptor);
                    context.Fence.Wait();
                },
                Cleanup = () =>
                {
                    commandBuffer.Dispose();
                    context.Dispose();
                },
            };
        }

        private static RHIBuffer CreateUploadBuffer(RHIDevice device, int byteSize)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = ERHIBufferUsage.CopySrc | ERHIBufferUsage.ShaderResource,
                StorageMode = ERHIStorageMode.HostUpload,
            });
        }

        private static RHIBuffer CreateReadbackBuffer(RHIDevice device, int byteSize)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = ERHIBufferUsage.CopyDst,
                StorageMode = ERHIStorageMode.Readback,
            });
        }

        private static RHIBuffer CreateGpuBuffer(RHIDevice device, int byteSize, ERHIBufferUsage usage)
        {
            return device.CreateBuffer(new RHIBufferDescriptor
            {
                ByteSize = byteSize,
                Format = ERHIBufferFormat.Undefine,
                UsageFlag = usage,
                StorageMode = ERHIStorageMode.GPULocal,
            });
        }

        private static RHIFunction CompileBindingTableBenchmarkFunction(RHIDevice device)
        {
            ShaderCompileResult result = HLSLCrossCompiler.Compile(new ShaderCompileRequest
            {
                Source = """
RWStructuredBuffer<uint> Output : register(u0, space0);

[numthreads(1, 1, 1)]
void main(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    Output[0] = dispatchThreadId.x;
}
""",
                SourceName = "SharpGPU.BindingTableBenchmark.hlsl",
                EntryPoint = "main",
                Stage = ShaderStageKind.Compute,
                ShaderModel = new ShaderModelVersion(6, 6),
                Target = ShaderTargetKind.Dxil,
            });
            if (result.Bytecode.Length == 0)
            {
                throw new InvalidOperationException("DX12 BindingTable benchmark shader compilation produced no bytecode.");
            }

            IntPtr pointer = Marshal.AllocHGlobal(result.Bytecode.Length);
            try
            {
                Marshal.Copy(result.Bytecode, 0, pointer, result.Bytecode.Length);
                return device.CreateFunction(new RHIFunctionDescriptor
                {
                    ByteSize = checked((uint)result.Bytecode.Length),
                    ByteCode = pointer,
                    EntryName = "main",
                    Type = ERHIFunctionType.Compute,
                    PayloadKind = ERHIShaderPayloadKind.Dxil,
                });
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        private static int CheckBaseline(string path, IReadOnlyList<BenchmarkResult> results)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Baseline file not found: {path}");
                return 2;
            }

            BenchmarkResult[]? baseline = JsonSerializer.Deserialize<BenchmarkResult[]>(File.ReadAllText(path));
            if (baseline == null)
            {
                Console.Error.WriteLine($"Baseline file is invalid: {path}");
                return 2;
            }

            Dictionary<string, BenchmarkResult> byName = new(StringComparer.Ordinal);
            foreach (BenchmarkResult result in baseline)
            {
                byName[result.Name] = result;
            }

            int failures = 0;
            foreach (BenchmarkResult result in results)
            {
                if (!byName.TryGetValue(result.Name, out BenchmarkResult? expected))
                {
                    continue;
                }

                bool expectedPassed = string.IsNullOrWhiteSpace(expected.Status) || expected.Status == StatusPassed;
                if (expectedPassed && result.Status != StatusPassed)
                {
                    Console.Error.WriteLine($"{result.Name}: expected passed result but current status is {result.Status} ({result.SkipReason})");
                    failures++;
                    continue;
                }

                if (result.Status != StatusPassed)
                {
                    continue;
                }

                if (result.AllocatedBytes > expected.AllocatedBytes && result.AllocatedBytes > 0)
                {
                    Console.Error.WriteLine($"{result.Name}: allocation regression {result.AllocatedBytes} > {expected.AllocatedBytes}");
                    failures++;
                }

                double p50Limit = Math.Max(expected.P50Microseconds + 0.25, expected.P50Microseconds * 1.10);
                double p95Limit = Math.Max(expected.P95Microseconds + 1.00, expected.P95Microseconds * 1.10);
                if (result.P50Microseconds > p50Limit || result.P95Microseconds > p95Limit)
                {
                    Console.Error.WriteLine($"{result.Name}: CPU regression p50 {result.P50Microseconds:F3}/{p50Limit:F3}, p95 {result.P95Microseconds:F3}/{p95Limit:F3}");
                    failures++;
                }
            }

            return failures == 0 ? 0 : 1;
        }

        private static void WriteConsole(IReadOnlyList<BenchmarkResult> results)
        {
            Console.WriteLine("name,mode,backend,submit,status,iterations,mean_us,p50_us,p95_us,p99_us,max_us,allocated_bytes,adapter,skip_reason");
            foreach (BenchmarkResult result in results)
            {
                Console.WriteLine(result.ToCsv());
            }
        }

        private static void WriteCsv(string path, IReadOnlyList<BenchmarkResult> results)
        {
            EnsureDirectory(path);
            using StreamWriter writer = new(path);
            writer.WriteLine("name,mode,backend,submit,status,iterations,mean_us,p50_us,p95_us,p99_us,max_us,allocated_bytes,adapter,skip_reason");
            foreach (BenchmarkResult result in results)
            {
                writer.WriteLine(result.ToCsv());
            }
        }

        private static void WriteJson(string path, IReadOnlyList<BenchmarkResult> results)
        {
            EnsureDirectory(path);
            File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void EnsureDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static double Average(long[] samples)
        {
            long total = 0;
            foreach (long sample in samples)
            {
                total += sample;
            }

            return ToMicroseconds(total / (double)samples.Length);
        }

        private static double Percentile(long[] samples, double percentile)
        {
            int index = Math.Clamp((int)Math.Ceiling(samples.Length * percentile) - 1, 0, samples.Length - 1);
            return ToMicroseconds(samples[index]);
        }

        private static double ToMicroseconds(double ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;

        internal sealed class Options
        {
            public int Iterations { get; private init; }
            public int Warmup { get; private init; }
            public string Mode { get; private init; } = "api";
            public string Backend { get; private init; } = "auto";
            public string? AdapterSubstring { get; private init; }
            public string? CsvPath { get; private init; }
            public string? JsonPath { get; private init; }
            public string? BaselinePath { get; private init; }
            public string? Filter { get; private init; }
            public bool FailOnRegression { get; private init; }

            public static Options Parse(string[] args)
            {
                return new Options
                {
                    Iterations = ParseInt(args, "--iterations", DefaultIterations),
                    Warmup = ParseInt(args, "--warmup", DefaultWarmup),
                    Mode = ParseString(args, "--mode") ?? "api",
                    Backend = ParseString(args, "--backend") ?? "auto",
                    AdapterSubstring = ParseString(args, "--adapter-substring"),
                    CsvPath = ParseString(args, "--csv"),
                    JsonPath = ParseString(args, "--json"),
                    BaselinePath = ParseString(args, "--baseline"),
                    Filter = ParseString(args, "--filter"),
                    FailOnRegression = HasFlag(args, "--fail-on-regression"),
                };
            }

            public bool ShouldRun(BenchmarkCase benchmarkCase)
            {
                if (!string.Equals(Mode, "all", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Mode, benchmarkCase.Mode, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.Equals(Backend, "auto", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Backend, "all", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(benchmarkCase.Backend, "none", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Backend, benchmarkCase.Backend, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(Filter)
                    && !benchmarkCase.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }

            private static int ParseInt(string[] args, string name, int defaultValue)
            {
                string? value = ParseString(args, name);
                return int.TryParse(value, out int parsed) && parsed > 0 ? parsed : defaultValue;
            }

            private static string? ParseString(string[] args, string name)
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i + 1];
                    }
                }

                return null;
            }

            private static bool HasFlag(string[] args, string name)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal sealed class BenchmarkCase
        {
            public string Name { get; }
            public string Mode { get; }
            public string Backend { get; }
            public string Submit { get; }
            public Func<Options, PreparedBenchmark> Create { get; }

            public BenchmarkCase(string name, string mode, string backend, string submit, Func<Options, PreparedBenchmark> create)
            {
                Name = name;
                Mode = mode;
                Backend = backend;
                Submit = submit;
                Create = create;
            }
        }

        internal sealed class PreparedBenchmark : IDisposable
        {
            public Action? Body { get; init; }
            public bool RequireZeroAllocation { get; init; }
            public Action? Cleanup { get; init; }
            public string? SkipReason { get; init; }
            public string? AdapterName { get; init; }

            public static PreparedBenchmark Skipped(string reason)
            {
                return new PreparedBenchmark { SkipReason = reason };
            }

            public void Dispose()
            {
                Cleanup?.Invoke();
            }
        }

        internal sealed class BenchmarkResult
        {
            public string Name { get; set; } = string.Empty;
            public string Mode { get; set; } = "api";
            public string Backend { get; set; } = "none";
            public string Submit { get; set; } = "none";
            public string Status { get; set; } = StatusPassed;
            public int Iterations { get; set; }
            public double MeanMicroseconds { get; set; }
            public double P50Microseconds { get; set; }
            public double P95Microseconds { get; set; }
            public double P99Microseconds { get; set; }
            public double MaxMicroseconds { get; set; }
            public long AllocatedBytes { get; set; }
            public string? Adapter { get; set; }
            public string? SkipReason { get; set; }

            internal static BenchmarkResult Passed(
                BenchmarkCase benchmarkCase,
                int iterations,
                double mean,
                double p50,
                double p95,
                double p99,
                double max,
                long allocatedBytes,
                string? adapter)
            {
                return new BenchmarkResult
                {
                    Name = benchmarkCase.Name,
                    Mode = benchmarkCase.Mode,
                    Backend = benchmarkCase.Backend,
                    Submit = benchmarkCase.Submit,
                    Status = StatusPassed,
                    Iterations = iterations,
                    MeanMicroseconds = mean,
                    P50Microseconds = p50,
                    P95Microseconds = p95,
                    P99Microseconds = p99,
                    MaxMicroseconds = max,
                    AllocatedBytes = allocatedBytes,
                    Adapter = adapter,
                };
            }

            internal static BenchmarkResult Skipped(BenchmarkCase benchmarkCase, int iterations, string reason)
            {
                return new BenchmarkResult
                {
                    Name = benchmarkCase.Name,
                    Mode = benchmarkCase.Mode,
                    Backend = benchmarkCase.Backend,
                    Submit = benchmarkCase.Submit,
                    Status = StatusSkipped,
                    Iterations = iterations,
                    SkipReason = reason,
                };
            }

            public string ToCsv()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4},{5},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11},{12},{13}",
                    Csv(Name),
                    Csv(Mode),
                    Csv(Backend),
                    Csv(Submit),
                    Csv(Status),
                    Iterations,
                    MeanMicroseconds,
                    P50Microseconds,
                    P95Microseconds,
                    P99Microseconds,
                    MaxMicroseconds,
                    AllocatedBytes,
                    Csv(Adapter ?? string.Empty),
                    Csv(SkipReason ?? string.Empty));
            }

            private static string Csv(string value)
            {
                if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
                {
                    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                }

                return value;
            }
        }

        private sealed class Dx12BenchmarkContext : IDisposable
        {
            public RHIInstance Instance { get; }
            public RHIDevice Device { get; }
            public RHICommandQueue CommandQueue { get; }
            public RHIFence Fence { get; }

            private Dx12BenchmarkContext(RHIInstance instance, RHIDevice device, RHICommandQueue commandQueue, RHIFence fence)
            {
                Instance = instance;
                Device = device;
                CommandQueue = commandQueue;
                Fence = fence;
            }

            public static bool TryCreate(Options options, [NotNullWhen(true)] out Dx12BenchmarkContext? context, out string skipReason)
            {
                context = null;
                skipReason = string.Empty;

                if (!OperatingSystem.IsWindows())
                {
                    skipReason = "DX12 backend benchmarks require Windows.";
                    return false;
                }

                RHIInstance instance;
                try
                {
                    instance = RHIInstance.Create(new RHIInstanceDescriptor
                    {
                        Backend = ERHIBackend.DirectX12,
                        EnableDebugLayer = false,
                        EnableValidation = false,
                        ComputeQueueRequestCount = 0,
                        TransferQueueRequestCount = 0,
                        GraphicsQueueRequestCount = 1,
                    });
                }
                catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or DllNotFoundException)
                {
                    skipReason = ex.Message;
                    return false;
                }

                RHIDevice? selectedDevice = null;
                for (int i = 0; i < instance.DeviceCount; ++i)
                {
                    RHIDevice candidate = instance.GetDevice(i);
                    if (!string.IsNullOrWhiteSpace(options.AdapterSubstring)
                        && candidate.Name?.Contains(options.AdapterSubstring, StringComparison.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    selectedDevice = candidate;
                    break;
                }

                if (selectedDevice == null)
                {
                    skipReason = string.IsNullOrWhiteSpace(options.AdapterSubstring)
                        ? "No DX12 adapter was enumerated."
                        : $"No DX12 adapter matched '{options.AdapterSubstring}'.";
                    instance.Dispose();
                    return false;
                }

                RHICommandQueue? queue = selectedDevice.GetCommandQueue(ERHIPipelineType.Graphics, 0);
                if (queue == null)
                {
                    skipReason = $"DX12 graphics queue is unavailable for '{selectedDevice.Name}'.";
                    instance.Dispose();
                    return false;
                }

                context = new Dx12BenchmarkContext(instance, selectedDevice, queue, selectedDevice.CreateFence());
                return true;
            }

            public void Dispose()
            {
                Fence.Dispose();
                Instance.Dispose();
            }
        }

        private sealed class WorkGraphEncodeFixture : IDisposable
        {
            public const int InputRecordStride = 16;
            private const int CommandBufferPoolSize = 64;

            public RHICommandBuffer[] CommandBuffers { get; }
            public RHIPipelineLayout PipelineLayout { get; }
            public RHIFunctionLibrary FunctionLibrary { get; }
            public RHIWorkGraphPipeline Pipeline { get; }
            public RHIBuffer BackingMemory { get; }
            public RHIBuffer InputRecordBuffer { get; }
            public int BackingMemorySize { get; }
            public RHIWorkGraphPassDescriptor PassDescriptor { get; } = new() { Name = "bench.workgraph" };
            private int m_CommandBufferIndex;

            private WorkGraphEncodeFixture(
                RHICommandBuffer[] commandBuffers,
                RHIPipelineLayout pipelineLayout,
                RHIFunctionLibrary functionLibrary,
                RHIWorkGraphPipeline pipeline,
                RHIBuffer backingMemory,
                RHIBuffer inputRecordBuffer,
                int backingMemorySize)
            {
                CommandBuffers = commandBuffers;
                PipelineLayout = pipelineLayout;
                FunctionLibrary = functionLibrary;
                Pipeline = pipeline;
                BackingMemory = backingMemory;
                InputRecordBuffer = inputRecordBuffer;
                BackingMemorySize = backingMemorySize;
            }

            public RHICommandBuffer NextCommandBuffer()
            {
                int index = unchecked(m_CommandBufferIndex++);
                return CommandBuffers[index & (CommandBufferPoolSize - 1)];
            }

            public static WorkGraphEncodeFixture Create(Dx12BenchmarkContext context)
            {
                byte[] dxil = CompileWorkGraphShader();
                RHIPipelineLayout pipelineLayout = context.Device.CreatePipelineLayout(new RHIPipelineLayoutDescriptor
                {
                    bLocalSignature = false,
                    bUseVertexLayout = false,
                    PushConstantSize = 0,
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                });
                RHIFunctionLibrary functionLibrary = CreateFunctionLibrary(context.Device, dxil);
                RHIWorkGraphPipeline pipeline = context.Device.CreateWorkGraphPipeline(new RHIWorkGraphPipelineDescriptor
                {
                    Name = "SharpGPU.Benchmark.WorkGraph",
                    FunctionLibrary = functionLibrary,
                    PipelineLayout = pipelineLayout,
                });
                int backingMemorySize = CalculateBackingMemoryByteSize(pipeline.MemoryRequirements);
                RHIBuffer backingMemory = CreateGpuBuffer(context.Device, backingMemorySize, ERHIBufferUsage.UnorderedAccess);
                RHIBuffer inputRecordBuffer = CreateUploadBuffer(context.Device, InputRecordStride);
                WriteInputRecord(inputRecordBuffer);
                RHICommandBuffer[] commandBuffers = new RHICommandBuffer[CommandBufferPoolSize];
                for (int i = 0; i < commandBuffers.Length; ++i)
                {
                    commandBuffers[i] = context.CommandQueue.CreateCommandBuffer();
                }

                return new WorkGraphEncodeFixture(
                    commandBuffers,
                    pipelineLayout,
                    functionLibrary,
                    pipeline,
                    backingMemory,
                    inputRecordBuffer,
                    backingMemorySize);
            }

            public void Dispose()
            {
                foreach (RHICommandBuffer commandBuffer in CommandBuffers)
                {
                    commandBuffer.Dispose();
                }
                InputRecordBuffer.Dispose();
                BackingMemory.Dispose();
                Pipeline.Dispose();
                FunctionLibrary.Dispose();
                PipelineLayout.Dispose();
            }

            private static byte[] CompileWorkGraphShader()
            {
                ShaderCompileResult result = HLSLCrossCompiler.Compile(new ShaderCompileRequest
                {
                    Source = """
struct InputRecord
{
    uint Value;
    uint Padding0;
    uint Padding1;
    uint Padding2;
};

[Shader("node")]
[NodeLaunch("thread")]
[NodeIsProgramEntry]
void WorkNode(ThreadNodeInputRecord<InputRecord> input)
{
    uint value = input.Get().Value;
}
""",
                    SourceName = "SharpGPU.Benchmark.WorkGraph.hlsl",
                    EntryPoint = string.Empty,
                    Stage = ShaderStageKind.Library,
                    ShaderModel = new ShaderModelVersion(6, 8),
                    Target = ShaderTargetKind.Dxil,
                });
                return result.Bytecode;
            }

            private static RHIFunctionLibrary CreateFunctionLibrary(RHIDevice device, byte[] bytecode)
            {
                IntPtr pointer = Marshal.AllocHGlobal(bytecode.Length);
                try
                {
                    Marshal.Copy(bytecode, 0, pointer, bytecode.Length);
                    return device.CreateFunctionLibrary(new RHIFunctionLibraryDescriptor
                    {
                        ByteSize = checked((uint)bytecode.Length),
                        ByteCode = pointer,
                        PayloadKind = ERHIShaderPayloadKind.Dxil,
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }

            private static int CalculateBackingMemoryByteSize(RHIWorkGraphMemoryRequirements requirements)
            {
                ulong size = requirements.MinSizeInBytes;
                if (size == 0)
                {
                    size = Math.Max(64 * 1024UL, requirements.SizeGranularityInBytes);
                }

                if (requirements.SizeGranularityInBytes != 0)
                {
                    ulong granularity = requirements.SizeGranularityInBytes;
                    size = ((size + granularity - 1) / granularity) * granularity;
                }

                if (size == 0 || size > int.MaxValue)
                {
                    throw new InvalidOperationException($"Unsupported WorkGraph backing memory size: {size}.");
                }

                return checked((int)size);
            }

            private static void WriteInputRecord(RHIBuffer inputRecordBuffer)
            {
                IntPtr pointer = inputRecordBuffer.Map(0, 0);
                try
                {
                    Marshal.WriteInt32(pointer, 1);
                    Marshal.WriteInt32(pointer + 4, 0);
                    Marshal.WriteInt32(pointer + 8, 0);
                    Marshal.WriteInt32(pointer + 12, 0);
                }
                finally
                {
                    inputRecordBuffer.UnMap(0, InputRecordStride);
                }
            }
        }

        private struct FakeBindingState
        {
            private uint m_Table;
            private ulong m_Address;

            public void Bind(uint table, ulong address)
            {
                m_Table = table;
                m_Address = address;
            }
        }

        private sealed class FakeStorageQueue : RHIStorageQueue
        {
            private readonly RHIStorageBufferRequest[] m_Requests;
            private int m_Count;

            public FakeStorageQueue(int capacity)
            {
                m_Requests = new RHIStorageBufferRequest[capacity];
            }

            public override RHIStorageFileHandle OpenFile(string absPath) => default;
            public override void CloseFile(in RHIStorageFileHandle fileHandle) { }
            public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle) => 0;
            public override void RequestBuffer(in RHIStorageBufferRequest request)
            {
                if (m_Count >= m_Requests.Length)
                {
                    m_Count = 0;
                }

                m_Requests[m_Count++] = request;
            }
            public override void RequestTexture(in RHIStorageTextureRequest request) { }
            public override void Submit(RHIFence signalFence) { }
            public override void ThrowIfSubmissionFailed() { }
            public override void CancelRequestsWithTag(ulong mask, ulong value) { }
            public override void CancelPending() { }
            protected override void Release() { }
        }

        private sealed class FakeCommandBuffer : RHICommandBuffer
        {
            public FakeTransferEncoder TransferEncoder { get; }

            public FakeCommandBuffer()
            {
                TransferEncoder = new FakeTransferEncoder(this);
            }

            public override void Begin(string name) { }
            public override void End() { }
            public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
            {
                TransferEncoder.BeginPass(descriptor);
                return TransferEncoder;
            }
            public override void EndTransferPass() => TransferEncoder.EndPass();
            public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndComputePass() => throw new NotSupportedException();
            public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndRaytracingPass() => throw new NotSupportedException();
            public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndRasterPass() => throw new NotSupportedException();
            public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndMLPass() => throw new NotSupportedException();
            public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndWorkGraphPass() => throw new NotSupportedException();
            public override RHITransferEncoder GetTransferEncoder() => TransferEncoder;
            public override RHIComputeEncoder GetComputeEncoder() => throw new NotSupportedException();
            public override RHIRaytracingEncoder GetRaytracingEncoder() => throw new NotSupportedException();
            public override RHIRasterEncoder GetRasterEncoder() => throw new NotSupportedException();
            public override RHIMLEncoder GetMLEncoder() => throw new NotSupportedException();
            public override RHIWorkGraphEncoder GetWorkGraphEncoder() => throw new NotSupportedException();
            protected override void Release() => TransferEncoder.Dispose();
        }

        private sealed class FakeTransferEncoder : RHITransferEncoder
        {
            private int m_CopyBytes;

            public FakeTransferEncoder(RHICommandBuffer commandBuffer)
            {
                m_CommandBuffer = commandBuffer;
            }

            internal override void BeginPass(in RHITransferPassDescriptor descriptor) { }
            public override void Barrier(in RHIBarrier barrier) { }
            public override void Barriers(ReadOnlySpan<RHIBarrier> barriers) { }
            public override void PushDebugGroup(string name) { }
            public override void PopDebugGroup() { }
            public override void WriteTimestamp(in uint index) { }
            public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount) { }
            public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size) => m_CopyBytes += size;
            public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void EndPass() { }
            protected override void Release() => _ = m_CopyBytes;
        }

        private sealed class FakeBuffer : RHIBuffer
        {
            public FakeBuffer(in RHIBufferDescriptor descriptor)
            {
                m_Descriptor = descriptor;
            }

            public override IntPtr Map(in uint readBegin, in uint readEnd) => IntPtr.Zero;
            public override void UnMap(in uint writeBegin, in uint writeEnd) { }
            public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor) => throw new NotSupportedException();
            protected override void Release() { }
        }
    }
}
