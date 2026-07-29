using System;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    [Trait("Category", "SharpGpuPortable")]
    public sealed class PresentationStatusInjectionTests
    {
        [Theory]
        [InlineData(ERHISwapChainStatus.NotReady)]
        [InlineData(ERHISwapChainStatus.Timeout)]
        [InlineData(ERHISwapChainStatus.Occluded)]
        [InlineData(ERHISwapChainStatus.Suboptimal)]
        [InlineData(ERHISwapChainStatus.OutOfDate)]
        public void RecoverableResizeStatus_ShouldRemainTypedAndKeepDeviceOperational(
            ERHISwapChainStatus status)
        {
            using ScriptedDevice device = new();
            using ScriptedSwapChain swapChain = new(device);
            swapChain.ResizeResult =
                RHISwapChainOperationResult.FromStatus(status);
            RHISwapChainResizeDescriptor descriptor =
                CreateResizeDescriptor();

            RHISwapChainOperationResult result =
                swapChain.Resize(in descriptor);

            Assert.Equal(status, result.Status);
            Assert.Null(result.Diagnostic);
            Assert.Equal(ERHIDeviceState.Operational, device.State);
            Assert.Equal(1, swapChain.ResizeCoreCount);
        }

        [Fact]
        public void SuboptimalAcquire_ShouldCarryActualImageAndIndex()
        {
            using ScriptedDevice device = new();
            using ScriptedTexture texture = new();
            using ScriptedSwapChain swapChain = new(device)
            {
                AcquireResult =
                    RHISwapChainAcquireResult.Acquired(
                        texture,
                        imageIndex: 1,
                        suboptimal: true),
            };
            RHISwapChainAcquireDescriptor descriptor = new();

            RHISwapChainAcquireResult result =
                swapChain.AcquireBackBuffer(in descriptor);

            Assert.Equal(
                ERHISwapChainStatus.Suboptimal,
                result.Status);
            Assert.True(result.HasImage);
            Assert.Same(texture, result.Texture);
            Assert.Equal(1, result.ImageIndex);
            Assert.Equal(ERHIDeviceState.Operational, device.State);
        }

        [Fact]
        public void SurfaceLost_ShouldNotPoisonDeviceAndCallerCanInjectRecovery()
        {
            using ScriptedDevice device = new();
            using ScriptedSwapChain swapChain = new(device);
            RHIException diagnostic = CreateSurfaceLostDiagnostic();
            swapChain.ResizeResult =
                RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.SurfaceLost,
                    diagnostic);
            RHISwapChainResizeDescriptor descriptor =
                CreateResizeDescriptor();

            RHISwapChainOperationResult lost =
                swapChain.Resize(in descriptor);

            Assert.Equal(
                ERHISwapChainStatus.SurfaceLost,
                lost.Status);
            Assert.Same(diagnostic, lost.Diagnostic);
            Assert.Equal(ERHIDeviceState.Operational, device.State);

            swapChain.ResizeResult =
                RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.Success);
            RHISwapChainOperationResult recovered =
                swapChain.Resize(in descriptor);

            Assert.Equal(
                ERHISwapChainStatus.Success,
                recovered.Status);
            Assert.Equal(2, swapChain.ResizeCoreCount);
        }

        [Fact]
        public void DeviceLost_ShouldPoisonDeviceOwnedPresentationAndSynchronizationObjects()
        {
            using ScriptedDevice device = new();
            using ScriptedSwapChain swapChain = new(device);
            using ScriptedCommandQueue queue = new(device);
            using ScriptedFence fence = new(device);
            using ScriptedSemaphore semaphore = new(device);
            RHIException diagnostic = CreateDeviceLostDiagnostic();
            swapChain.ResizeResult =
                RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.DeviceLost,
                    diagnostic);
            RHISwapChainResizeDescriptor resize =
                CreateResizeDescriptor();

            RHISwapChainOperationResult result =
                swapChain.Resize(in resize);

            Assert.Equal(
                ERHISwapChainStatus.DeviceLost,
                result.Status);
            Assert.Same(diagnostic, result.Diagnostic);
            Assert.Equal(ERHIDeviceState.Lost, device.State);
            Assert.Same(diagnostic, device.DeviceLossDiagnostic);

            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(
                    () => swapChain.Resize(in resize)));
            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(
                    () => _ = swapChain.ImageCount));
            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(() => queue.WaitIdle()));
            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(() => _ = fence.Status));
            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(
                    () => semaphore.ReserveSignal()));
            Assert.Same(
                diagnostic,
                Assert.Throws<RHIException>(() => device.CreateFence()));

            Assert.Equal(1, swapChain.ResizeCoreCount);
            Assert.Equal(0, queue.WaitIdleCoreCount);
        }

        [Fact]
        public void TerminalPresentWithoutNativeEnqueue_ShouldRollbackWaitReservation()
        {
            using ScriptedDevice device = new();
            using ScriptedSemaphore wait = new(device);
            using ScriptedSwapChain swapChain = new(device)
            {
                PresentWaitsConsumed = false,
                PresentResult =
                    RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.SurfaceLost,
                        CreateSurfaceLostDiagnostic()),
            };
            wait.ReserveSignal();
            wait.CommitSignal();
            RHISemaphore[] waits = { wait };
            RHISwapChainPresentDescriptor descriptor = new(waits);

            RHISwapChainOperationResult result =
                swapChain.Present(in descriptor);

            Assert.Equal(
                ERHISwapChainStatus.SurfaceLost,
                result.Status);
            wait.ReserveWait();
            wait.RollbackWait();
        }

        [Fact]
        public void EnqueuedOutOfDatePresent_ShouldConsumeWaitReservation()
        {
            using ScriptedDevice device = new();
            using ScriptedSemaphore wait = new(device);
            using ScriptedSwapChain swapChain = new(device)
            {
                PresentWaitsConsumed = true,
                PresentResult =
                    RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.OutOfDate),
            };
            wait.ReserveSignal();
            wait.CommitSignal();
            RHISemaphore[] waits = { wait };
            RHISwapChainPresentDescriptor descriptor = new(waits);

            RHISwapChainOperationResult result =
                swapChain.Present(in descriptor);

            Assert.Equal(
                ERHISwapChainStatus.OutOfDate,
                result.Status);
            Assert.Throws<InvalidOperationException>(
                () => wait.ReserveWait());
            wait.ReserveSignal();
            wait.RollbackSignal();
        }

        private static RHISwapChainResizeDescriptor
            CreateResizeDescriptor()
        {
            uint2 extent = new(1280, 720);
            return new RHISwapChainResizeDescriptor(
                extent,
                ERHINativeSurfaceKind.Headless,
                new IntPtr(1),
                surfaceGeneration: 1);
        }

        private static RHIException CreateSurfaceLostDiagnostic()
        {
            return new RHIException(
                ERHIErrorCode.SurfaceLost,
                ERHIBackend.Vulkan,
                nativeCode: -1000000000,
                nativeMessage: "injected surface loss",
                ERHIDeviceState.Operational);
        }

        private static RHIException CreateDeviceLostDiagnostic()
        {
            return new RHIException(
                ERHIErrorCode.DeviceLost,
                ERHIBackend.Vulkan,
                nativeCode: -4,
                nativeMessage: "injected device loss",
                ERHIDeviceState.Lost);
        }

        private sealed class ScriptedSwapChain : RHISwapChain
        {
            internal RHISwapChainAcquireResult AcquireResult { get; set; }
            internal RHISwapChainOperationResult ResizeResult { get; set; }
            internal RHISwapChainOperationResult PresentResult { get; set; }
            internal bool PresentWaitsConsumed { get; set; }
            internal int ResizeCoreCount { get; private set; }

            internal ScriptedSwapChain(ScriptedDevice device)
                : base(device)
            {
                AcquireResult =
                    RHISwapChainAcquireResult.Unavailable(
                        ERHISwapChainStatus.NotReady);
                ResizeResult =
                    RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.Success);
                PresentResult =
                    RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.Success);
                PresentWaitsConsumed = true;
            }

            public override int BackTextureIndex
            {
                get
                {
                    ThrowIfSwapChainUnavailable();
                    return 0;
                }
            }

            public override int ImageCount
            {
                get
                {
                    ThrowIfSwapChainUnavailable();
                    return 2;
                }
            }

            public override RHISwapChainAcquireResult AcquireBackBuffer(
                in RHISwapChainAcquireDescriptor descriptor)
            {
                ThrowIfSwapChainUnavailable();
                ValidateAcquire(in descriptor);
                RHISwapChainAcquireResult result = AcquireResult;
                InvalidateDeviceIfNeeded(in result);
                return result;
            }

            public override RHISwapChainOperationResult Resize(
                in RHISwapChainResizeDescriptor descriptor)
            {
                ThrowIfSwapChainUnavailable();
                ++ResizeCoreCount;
                RHISwapChainOperationResult result = ResizeResult;
                InvalidateDeviceIfNeeded(in result);
                return result;
            }

            public override RHISwapChainOperationResult Present(
                in RHISwapChainPresentDescriptor descriptor)
            {
                ThrowIfSwapChainUnavailable();
                ValidatePresent(in descriptor);

                ReadOnlySpan<RHISemaphore> waits =
                    descriptor.WaitSemaphores.Span;
                for (int index = 0; index < waits.Length; ++index)
                {
                    waits[index].ReserveWait();
                }

                if (PresentWaitsConsumed)
                {
                    for (int index = 0; index < waits.Length; ++index)
                    {
                        waits[index].CommitWait();
                    }
                }
                else
                {
                    for (int index = waits.Length - 1; index >= 0; --index)
                    {
                        waits[index].RollbackWait();
                    }
                }

                RHISwapChainOperationResult result = PresentResult;
                InvalidateDeviceIfNeeded(in result);
                return result;
            }
        }

        private sealed class ScriptedCommandQueue : RHICommandQueue
        {
            private readonly ScriptedDevice m_Device;

            internal int WaitIdleCoreCount { get; private set; }

            internal ScriptedCommandQueue(ScriptedDevice device)
            {
                m_Device = device;
                m_PipelineType = ERHIPipelineType.Graphics;
            }

            public override ulong Frequency => 1;
            protected override object DeviceIdentity => m_Device;

            public override RHICommandBuffer CreateCommandBuffer()
            {
                m_Device.ThrowIfDeviceUnavailable();
                throw new NotSupportedException();
            }

            public override void Submit(
                in RHIQueueSubmitDescriptor descriptor)
            {
                ValidateSubmit(in descriptor);
                ReserveSubmit(in descriptor);
                CommitSubmit(in descriptor);
            }

            public override void WaitIdle()
            {
                ThrowIfDisposed();
                m_Device.ThrowIfDeviceUnavailable();
                ++WaitIdleCoreCount;
            }
        }

        private sealed class ScriptedFence : RHIFence
        {
            internal ScriptedFence(ScriptedDevice device)
                : base(device)
            {
            }

            public override ERHIFenceStatus Status
            {
                get
                {
                    ThrowIfSynchronizationDisposed();
                    return IsSignalKnownComplete
                        ? ERHIFenceStatus.Success
                        : ERHIFenceStatus.NotReady;
                }
            }

            public override void Reset()
            {
                if (!BeginReset())
                {
                    return;
                }
                CompleteReset();
            }

            public override ERHIFenceStatus Wait(
                ulong timeoutNanoseconds = ulong.MaxValue)
            {
                EnsureWaitable();
                MarkSignaled();
                return ERHIFenceStatus.Success;
            }
        }

        private sealed class ScriptedSemaphore : RHISemaphore
        {
            internal ScriptedSemaphore(ScriptedDevice device)
                : base(device)
            {
            }
        }

        private sealed class ScriptedTexture : RHITexture
        {
            internal ScriptedTexture()
            {
                m_Descriptor = new RHITextureDescriptor
                {
                    Extent = new uint3(1, 1, 1),
                    MipCount = 1,
                    Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                    SampleCount = ERHISampleCount.None,
                    StorageMode = ERHIStorageMode.GPULocal,
                    UsageFlag = ERHITextureUsage.RenderTarget,
                    Dimension = ERHITextureDimension.Texture2D,
                };
            }

            public override RHITextureView CreateTextureView(
                in RHITextureViewDescriptor descriptor)
            {
                ThrowIfDisposed();
                throw new NotSupportedException();
            }
        }

        private sealed class ScriptedDevice : RHIDevice
        {
            public override ERHIBackend BackendType =>
                ERHIBackend.Vulkan;

            public override RHICommandQueue? GetCommandQueue(
                in ERHIPipelineType pipeline,
                in int index)
            {
                ThrowIfDeviceUnavailable();
                return null;
            }

            public override RHISwapChain CreateSwapChain(
                in RHISwapChainDescriptor descriptor)
            {
                ThrowIfDeviceUnavailable();
                return new ScriptedSwapChain(this);
            }

            public override RHIFence CreateFence()
            {
                ThrowIfDeviceUnavailable();
                return new ScriptedFence(this);
            }

            public override RHISemaphore CreateSemaphore()
            {
                ThrowIfDeviceUnavailable();
                return new ScriptedSemaphore(this);
            }

            public override RHIStorageQueue CreateStorageQueue() =>
                Unsupported<RHIStorageQueue>();

            public override RHIQuery CreateQuery(
                in RHIQueryDescriptor descriptor) =>
                Unsupported<RHIQuery>();

            public override RHIHeap CreateHeap(
                in RHIHeapDescription descriptor) =>
                Unsupported<RHIHeap>();

            public override RHIBuffer CreateBuffer(
                in RHIBufferDescriptor descriptor) =>
                Unsupported<RHIBuffer>();

            public override RHITexture CreateTexture(
                in RHITextureDescriptor descriptor) =>
                Unsupported<RHITexture>();

            public override RHISampler CreateSampler(
                in RHISamplerDescriptor descriptor) =>
                Unsupported<RHISampler>();

            public override RHITopLevelAccelStruct
                CreateTopAccelerationStructure(
                    in RHITopLevelAccelStructDescriptor descriptor) =>
                Unsupported<RHITopLevelAccelStruct>();

            public override RHIBottomLevelAccelStruct
                CreateBottomAccelerationStructure(
                    in RHIBottomLevelAccelStructDescriptor descriptor) =>
                Unsupported<RHIBottomLevelAccelStruct>();

            public override RHIBindingTableLayout
                CreateBindingTableLayout(
                    in RHIBindingTableLayoutDescriptor descriptor) =>
                Unsupported<RHIBindingTableLayout>();

            public override RHIBindingTable CreateBindingTable(
                in RHIBindingTableDescriptor descriptor) =>
                Unsupported<RHIBindingTable>();

            public override RHIPipelineLayout CreatePipelineLayout(
                in RHIPipelineLayoutDescriptor descriptor) =>
                Unsupported<RHIPipelineLayout>();

            public override RHIFunction CreateFunction(
                in RHIFunctionDescriptor descriptor) =>
                Unsupported<RHIFunction>();

            public override RHIFunctionLibrary CreateFunctionLibrary(
                in RHIFunctionLibraryDescriptor descriptor) =>
                Unsupported<RHIFunctionLibrary>();

            public override RHIFunctionTable CreateFunctionTable() =>
                Unsupported<RHIFunctionTable>();

            public override RHIComputePipeline CreateComputePipeline(
                in RHIComputePipelineDescriptor descriptor) =>
                Unsupported<RHIComputePipeline>();

            public override RHIRaytracingPipeline
                CreateRaytracingPipeline(
                    in RHIRaytracingPipelineDescriptor descriptor) =>
                Unsupported<RHIRaytracingPipeline>();

            public override RHIRasterPipeline CreateRasterPipeline(
                in RHIRasterPipelineDescriptor descriptor) =>
                Unsupported<RHIRasterPipeline>();

            public override RHIPipelineCache CreatePipelineCache() =>
                Unsupported<RHIPipelineCache>();

            public override RHIIndirectCommandLayout CreateIndirectCommandLayout(
                in RHIIndirectCommandLayoutDescriptor descriptor) =>
                Unsupported<RHIIndirectCommandLayout>();

            public override RHIMLPipeline CreateMLPipeline(
                in RHIMLPipelineDescriptor descriptor) =>
                Unsupported<RHIMLPipeline>();

            public override RHIMLBindingTable CreateMLBindingTable(
                in RHIMLBindingTableDescriptor descriptor) =>
                Unsupported<RHIMLBindingTable>();

            public override RHITensor CreateTensor(
                in RHIMLTensorDescriptor descriptor) =>
                Unsupported<RHITensor>();

            public override RHIWorkGraphPipeline
                CreateWorkGraphPipeline(
                    in RHIWorkGraphPipelineDescriptor descriptor) =>
                Unsupported<RHIWorkGraphPipeline>();

            private T Unsupported<T>()
            {
                ThrowIfDeviceUnavailable();
                throw new NotSupportedException(
                    $"Scripted device does not create {typeof(T).Name}.");
            }
        }
    }
}
