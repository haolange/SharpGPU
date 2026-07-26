using System;
using System.IO;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class PresentationLifecycleContractTests
{
    [Fact]
    public void PresentationMaintenanceStrategy_ShouldBeExactAndFailClosed()
    {
        RHICapability available = RHICapability.Available(
            ERHICapabilityTier.Tier1,
            ERHICapabilityStrategy.CoreApi,
            ERHICapabilityProbeKind.BackendContract,
            "presentation lifecycle contract test");
        RHICapability unavailable = RHICapability.Unavailable(
            "unavailable for presentation lifecycle contract test",
            ERHICapabilityProbeKind.BackendContract,
            "presentation lifecycle contract test");

        RHIPresentationCapabilities queueIdle =
            new(
                swapChain: available,
                acquireSignal: available,
                presentWait: available,
                presentCompletion: unavailable,
                maintenance: available,
                maintenanceStrategy:
                    ERHIPresentationMaintenanceStrategy.QueueIdle,
                hdr: unavailable);
        Assert.Equal(
            ERHIPresentationMaintenanceStrategy.QueueIdle,
            queueIdle.MaintenanceStrategy);

        RHIPresentationCapabilities presentFence =
            new(
                swapChain: available,
                acquireSignal: available,
                presentWait: available,
                presentCompletion: available,
                maintenance: available,
                maintenanceStrategy:
                    ERHIPresentationMaintenanceStrategy.PresentFence,
                hdr: unavailable);
        Assert.Equal(
            ERHIPresentationMaintenanceStrategy.PresentFence,
            presentFence.MaintenanceStrategy);

        Assert.Throws<ArgumentException>(
            () => new RHIPresentationCapabilities(
                available,
                available,
                available,
                unavailable,
                available,
                ERHIPresentationMaintenanceStrategy.PresentFence,
                unavailable));
        Assert.Throws<ArgumentException>(
            () => new RHIPresentationCapabilities(
                available,
                available,
                available,
                available,
                available,
                ERHIPresentationMaintenanceStrategy.Unavailable,
                unavailable));
        Assert.Throws<ArgumentException>(
            () => new RHIPresentationCapabilities(
                available,
                available,
                available,
                available,
                unavailable,
                ERHIPresentationMaintenanceStrategy.QueueIdle,
                unavailable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHIPresentationCapabilities(
                available,
                available,
                available,
                available,
                available,
                (ERHIPresentationMaintenanceStrategy)byte.MaxValue,
                unavailable));
    }

    [Fact]
    public void WaitIdle_ShouldBeExplicitAndRejectUseAfterDispose()
    {
        using CountingCommandQueue queue = new();

        queue.WaitIdle();

        Assert.Equal(1, queue.WaitIdleCount);
        Assert.Equal(0, queue.SubmitCount);

        RHIQueueSubmitDescriptor descriptor =
            new(completionFence: null);
        queue.Submit(in descriptor);

        Assert.Equal(1, queue.WaitIdleCount);
        Assert.Equal(1, queue.SubmitCount);

        queue.Dispose();
        Assert.Throws<ObjectDisposedException>(() => queue.WaitIdle());
    }

    [Fact]
    public void SwapChainStatus_ShouldRequireTypedTerminalDiagnostics()
    {
        RHISwapChainOperationResult outOfDate =
            RHISwapChainOperationResult.FromStatus(
                ERHISwapChainStatus.OutOfDate);
        outOfDate.Validate(ERHIBackend.Vulkan);

        RHIException surfaceLost = new(
            ERHIErrorCode.SurfaceLost,
            ERHIBackend.Vulkan,
            nativeCode: -1000000000,
            "VK_ERROR_SURFACE_LOST_KHR",
            ERHIDeviceState.Operational);
        RHISwapChainOperationResult.FromStatus(
                ERHISwapChainStatus.SurfaceLost,
                surfaceLost)
            .Validate(ERHIBackend.Vulkan);

        RHIException deviceLost = new(
            ERHIErrorCode.DeviceLost,
            ERHIBackend.Vulkan,
            nativeCode: -4,
            "VK_ERROR_DEVICE_LOST",
            ERHIDeviceState.Lost);
        RHISwapChainOperationResult.FromStatus(
                ERHISwapChainStatus.DeviceLost,
                deviceLost)
            .Validate(ERHIBackend.Vulkan);

        Assert.Throws<InvalidOperationException>(
            () => RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.SurfaceLost)
                .Validate(ERHIBackend.Vulkan));
        Assert.Throws<InvalidOperationException>(
            () => RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.DeviceLost,
                    surfaceLost)
                .Validate(ERHIBackend.Vulkan));
        Assert.Throws<InvalidOperationException>(
            () => RHISwapChainOperationResult.FromStatus(
                    ERHISwapChainStatus.Success,
                    deviceLost)
                .Validate(ERHIBackend.Vulkan));
    }

    [Fact]
    public void QueueIdle_ShouldAppearOnlyInExplicitRendererLifecycleDrain()
    {
        string root = FindRepositoryRoot();
        string sharpGpu = Path.Combine(
            root,
            "Engine",
            "Source",
            "Runtime",
            "Graphics",
            "SharpGPU");
        string renderContextPath = Path.Combine(
            root,
            "Engine",
            "Source",
            "Runtime",
            "Rendering",
            "Core",
            "RenderContext.cs");
        string renderContext = File.ReadAllText(renderContextPath);

        string endFrame = ExtractMethodBody(
            renderContext,
            "public void EndFrame()");
        string beginFrame = ExtractMethodBody(
            renderContext,
            "public bool BeginFrame()");
        Assert.Contains(
            "HasAcquireWait",
            beginFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "Submit(finalizePresentation: false);",
            beginFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "SubmitTerminalFences(m_CommandQueues);",
            beginFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "Renderer frame acquisition and acquire-signal",
            beginFrame,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".WaitIdle(",
            endFrame,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DrainPresentation(",
            endFrame,
            StringComparison.Ordinal);

        int endFrameTry = endFrame.IndexOf(
            "try",
            StringComparison.Ordinal);
        int frameSubmit = endFrame.IndexOf(
            "Submit(finalizePresentation: true);",
            StringComparison.Ordinal);
        int clearExecutionPool = endFrame.IndexOf(
            "m_CommandBufferExecutePool.Clear();",
            StringComparison.Ordinal);
        int clearActiveFrame = endFrame.LastIndexOf(
            "m_ActiveFrameResourceIndex = -1;",
            StringComparison.Ordinal);
        Assert.True(endFrameTry >= 0);
        Assert.True(frameSubmit > endFrameTry);
        Assert.True(clearExecutionPool > frameSubmit);
        Assert.True(clearActiveFrame > clearExecutionPool);
        Assert.Contains(
            "if (slot.HasAcquireWait)",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_Device.State == ERHIDeviceState.Operational",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "imageState.HasRenderCompleteWait",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "imageState.MarkRenderCompleteWaitSubmitted();",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "new(waitSemaphores: waits)",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "slot.MarkQueueUsed(",
            endFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExceptionDispatchInfo",
            endFrame,
            StringComparison.Ordinal);

        string drainPresentation = ExtractMethodBody(
            renderContext,
            "private void DrainPresentation()");
        Assert.Contains(
            "ERHIPresentationMaintenanceStrategy.PresentFence",
            drainPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "ERHIPresentationMaintenanceStrategy.QueueIdle",
            drainPresentation,
            StringComparison.Ordinal);
        Assert.Contains(
            ".WaitIdle();",
            drainPresentation,
            StringComparison.Ordinal);
        int presentFenceBranchStart = drainPresentation.IndexOf(
            "case ERHIPresentationMaintenanceStrategy.PresentFence:",
            StringComparison.Ordinal);
        int queueIdleBranchStart = drainPresentation.IndexOf(
            "case ERHIPresentationMaintenanceStrategy.QueueIdle:",
            StringComparison.Ordinal);
        int unavailableBranchStart = drainPresentation.IndexOf(
            "case ERHIPresentationMaintenanceStrategy.Unavailable:",
            StringComparison.Ordinal);
        Assert.True(presentFenceBranchStart >= 0);
        Assert.True(queueIdleBranchStart > presentFenceBranchStart);
        Assert.True(unavailableBranchStart > queueIdleBranchStart);

        string presentFenceBranch = drainPresentation[
            presentFenceBranchStart..queueIdleBranchStart];
        Assert.DoesNotContain(
            "WaitIdle",
            presentFenceBranch,
            StringComparison.Ordinal);
        Assert.Contains(
            "DrainPresentCompletions();",
            presentFenceBranch,
            StringComparison.Ordinal);

        string queueIdleBranch = drainPresentation[
            queueIdleBranchStart..unavailableBranchStart];
        Assert.Contains(
            ".WaitIdle();",
            queueIdleBranch,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DrainPresentCompletions();",
            queueIdleBranch,
            StringComparison.Ordinal);

        Assert.Equal(
            4,
            CountOccurrences(
                renderContext,
                "DrainPresentation();"));

        foreach (string path in Directory.EnumerateFiles(
                     sharpGpu,
                     "*SwapChain*.cs",
                     SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain(
                ".WaitIdle(",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "QueueWaitIdle(",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "DeviceWaitIdle(",
                source,
                StringComparison.Ordinal);
        }

        string[] backendQueuePaths =
        {
            Path.Combine(sharpGpu, "Dx12", "Dx12CommandQueue.cs"),
            Path.Combine(sharpGpu, "Vulkan", "VulkanCommandQueue.cs"),
            Path.Combine(sharpGpu, "Metal", "MetalCommandQueue.cs"),
        };
        foreach (string path in backendQueuePaths)
        {
            string submit = ExtractMethodBody(
                File.ReadAllText(path),
                "Submit(in RHIQueueSubmitDescriptor descriptor)");
            Assert.DoesNotContain(
                "WaitIdle",
                submit,
                StringComparison.Ordinal);
        }

        string reclaim = ExtractMethodBody(
            renderContext,
            "internal void Reclaim(");
        Assert.Contains(
            "m_QueueUsed[queueIndex]",
            reclaim,
            StringComparison.Ordinal);
        Assert.Contains(
            "before its terminal fence is submitted",
            reclaim,
            StringComparison.Ordinal);

        string frameSlotDispose = ExtractMethodBody(
            renderContext,
            "public void Dispose()");
        Assert.Contains(
            "m_Device.State == ERHIDeviceState.Operational",
            frameSlotDispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_CommandBuffers[i].Dispose();",
            frameSlotDispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_CompletionCallbacks[i]();",
            frameSlotDispose,
            StringComparison.Ordinal);
        Assert.Contains(
            "must be fence-drained before disposal",
            frameSlotDispose,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationPaths_ShouldFailClosedAndGuardPublicState()
    {
        string root = FindRepositoryRoot();
        string sharpGpu = Path.Combine(
            root,
            "Engine",
            "Source",
            "Runtime",
            "Graphics",
            "SharpGPU");
        string renderContext = File.ReadAllText(Path.Combine(
            root,
            "Engine",
            "Source",
            "Runtime",
            "Rendering",
            "Core",
            "RenderContext.cs"));

        string resolveBackend = ExtractMethodBody(
            renderContext,
            "private static ERHIBackend ResolveBackend()");
        Assert.DoesNotContain(
            "Falling back",
            resolveBackend,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "throw new ArgumentException(",
            resolveBackend,
            StringComparison.Ordinal);
        Assert.Contains(
            "throw new NotSupportedException(",
            resolveBackend,
            StringComparison.Ordinal);

        string beginFrame = ExtractMethodBody(
            renderContext,
            "public bool BeginFrame()");
        Assert.Contains(
            "m_ScreenSize.x == 0",
            beginFrame,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_ScreenSize.y == 0",
            beginFrame,
            StringComparison.Ordinal);

        string swapChainBase = File.ReadAllText(Path.Combine(
            sharpGpu,
            "Abstract",
            "RHISwapChain.cs"));
        string unavailableGuard = ExtractMethodBody(
            swapChainBase,
            "protected void ThrowIfSwapChainUnavailable()");
        Assert.Contains(
            "ThrowIfDisposed();",
            unavailableGuard,
            StringComparison.Ordinal);
        Assert.Contains(
            "ThrowIfDeviceUnavailable();",
            unavailableGuard,
            StringComparison.Ordinal);

        string[] backendSwapChains =
        {
            Path.Combine(sharpGpu, "Dx12", "Dx12SwapChain.cs"),
            Path.Combine(sharpGpu, "Vulkan", "VulkanSwapChain.cs"),
            Path.Combine(sharpGpu, "Metal", "MetalSwapChain.cs"),
        };
        foreach (string path in backendSwapChains)
        {
            string source = File.ReadAllText(path);
            Assert.True(
                CountOccurrences(
                    source,
                    "ThrowIfSwapChainUnavailable();") >= 2,
                $"{path} must guard public swapchain properties.");
        }

        string dx12 = File.ReadAllText(backendSwapChains[0]);
        Assert.DoesNotContain(
            "FallbackBackTextureIndex",
            dx12,
            StringComparison.Ordinal);
        Assert.Contains(
            "IDXGISwapChain3 is required",
            dx12,
            StringComparison.Ordinal);

        string vulkan = File.ReadAllText(backendSwapChains[1]);
        Assert.Contains(
            "Vulkan swapchain format",
            vulkan,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_ => ERHIPixelFormat.B8G8R8A8_UNorm",
            vulkan,
            StringComparison.Ordinal);

        Assert.Contains(
            "Surface generation cannot move backwards.",
            dx12,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReleaseNativeSwapChain();",
            dx12,
            StringComparison.Ordinal);
        Assert.Contains(
            "ERHISwapChainStatus.OutOfDate",
            dx12,
            StringComparison.Ordinal);
        Assert.Contains(
            "nativeBackBuffer.Release();",
            dx12,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_RenderCompleteWaitPending",
            renderContext,
            StringComparison.Ordinal);
        Assert.Contains(
            "must be consumed before disposal",
            renderContext,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VulkanPresentationCapability_ShouldFollowEnumeratedDeviceFacts()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "Engine",
            "Source",
            "Runtime",
            "Graphics",
            "SharpGPU",
            "Vulkan",
            "VulkanDevice.cs");
        string source = File.ReadAllText(path);

        Assert.True(
            CountOccurrences(
                source,
                "vkEnumerateDeviceExtensionProperties(") >= 2);
        int enumerateExtensions = source.IndexOf(
            "vkEnumerateDeviceExtensionProperties(",
            StringComparison.Ordinal);
        int hasSwapchainProbe = source.IndexOf(
            "bool hasSwapchainExtension =",
            StringComparison.Ordinal);
        int requiresSwapchain = source.IndexOf(
            "VulkanInstance.RequiresSwapchainDeviceExtension(",
            StringComparison.Ordinal);
        int enableSwapchain = source.IndexOf(
            "deviceExtensions.Add(\"VK_KHR_swapchain\");",
            StringComparison.Ordinal);
        Assert.True(enumerateExtensions >= 0);
        Assert.True(hasSwapchainProbe > enumerateExtensions);
        Assert.True(requiresSwapchain > hasSwapchainProbe);
        Assert.True(enableSwapchain > requiresSwapchain);
        Assert.Contains(
            "m_SwapchainSupported = hasSwapchainExtension;",
            source,
            StringComparison.Ordinal);

        int presentationStart = source.IndexOf(
            "presentation: new RHIPresentationCapabilities(",
            StringComparison.Ordinal);
        int rayTracingStart = source.IndexOf(
            "rayTracing: new RHIRayTracingCapabilities(",
            presentationStart,
            StringComparison.Ordinal);
        Assert.True(presentationStart >= 0);
        Assert.True(rayTracingStart > presentationStart);
        string presentation = source[presentationStart..rayTracingStart];
        Assert.Contains(
            "swapChain: Probe(",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_SwapchainSupported",
            presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "swapChain: RHICapability.Available(",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "? ERHIPresentationMaintenanceStrategy.Unavailable",
            presentation,
            StringComparison.Ordinal);
        Assert.Contains(
            "m_Capabilities.Presentation.SwapChain.Require(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidSurfaceLifecycle_ShouldSerializeGenerationAndDrainBeforeRelease()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "Engine",
            "Source",
            "Programs",
            "AndroidGame",
            "MainActivity.cs");
        string source = File.ReadAllText(path);

        Assert.Contains(
            "ConfigChanges.Orientation",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConfigChanges.ScreenSize",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_surfaceTransitionSequence",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "BeginSurfaceGeneration()",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".GetAwaiter().GetResult()",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "async void",
            source,
            StringComparison.Ordinal);

        string update = ExtractMethodBody(
            source,
            "private async Task StartOrResizeSessionAsync(");
        Assert.Contains(
            "announcedGeneration < _surfaceGeneration",
            update,
            StringComparison.Ordinal);
        Assert.Contains(
            "ref _surfaceTransitionSequence",
            update,
            StringComparison.Ordinal);
        Assert.Contains(
            "candidateWindowOwned = true;",
            update,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (candidateWindowOwned)",
            update,
            StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(
                update,
                "ref _surfaceTransitionSequence") >= 3);

        int acquireWindow = update.IndexOf(
            "ANativeWindow_fromSurface(",
            StringComparison.Ordinal);
        int startSession = update.IndexOf(
            "await session.StartAsync();",
            StringComparison.Ordinal);
        int postStartSequenceCheck = update.IndexOf(
            "sequence != Volatile.Read(",
            startSession,
            StringComparison.Ordinal);
        int candidateCleanup = update.LastIndexOf(
            "ReleaseNativeWindow(candidateWindow);",
            StringComparison.Ordinal);
        Assert.True(acquireWindow >= 0);
        Assert.True(startSession > acquireWindow);
        Assert.True(postStartSequenceCheck > startSession);
        Assert.True(candidateCleanup > postStartSequenceCheck);

        string destroy = ExtractMethodBody(
            source,
            "private async Task DestroySurfaceAsync(long sequence)");
        int suspend = destroy.IndexOf(
            "SuspendRenderSurface()",
            StringComparison.Ordinal);
        int release = destroy.IndexOf(
            "ReleaseNativeWindow()",
            StringComparison.Ordinal);
        Assert.True(suspend >= 0);
        Assert.True(release > suspend);
    }

    [Fact]
    public void Dx12AndVulkanQueueDeviceLoss_ShouldPoisonOwnerAfterRollback()
    {
        string sharpGpu = Path.Combine(
            FindRepositoryRoot(),
            "Engine",
            "Source",
            "Runtime",
            "Graphics",
            "SharpGPU");
        string[] backendQueuePaths =
        {
            Path.Combine(sharpGpu, "Dx12", "Dx12CommandQueue.cs"),
            Path.Combine(sharpGpu, "Vulkan", "VulkanCommandQueue.cs"),
        };

        foreach (string path in backendQueuePaths)
        {
            string source = File.ReadAllText(path);
            string submit = ExtractMethodBody(
                source,
                "public override void Submit(");
            AssertFailurePoisonsOwnerAfterRollback(
                submit,
                "RollbackSubmit(in descriptor);");

            string bindSparse = ExtractMethodBody(
                source,
                "public override void BindSparse(");
            AssertFailurePoisonsOwnerAfterRollback(
                bindSparse,
                "RollbackSparseBind(in descriptor);");
        }

        string device = File.ReadAllText(Path.Combine(
            sharpGpu,
            "Abstract",
            "RHIDevice.cs"));
        string transition = ExtractMethodBody(
            device,
            "internal RHIException MarkDeviceLost(");
        Assert.Contains(
            "lock (m_DeviceStateLock)",
            transition,
            StringComparison.Ordinal);
        Assert.Contains(
            "Volatile.Write(",
            transition,
            StringComparison.Ordinal);

        string queue = File.ReadAllText(Path.Combine(
            sharpGpu,
            "Abstract",
            "RHICommandQueue.cs"));
        Assert.Contains(
            "ThrowIfOwnerDeviceUnavailable();",
            ExtractMethodBody(
                queue,
                "protected void ValidateSubmit("),
            StringComparison.Ordinal);
        Assert.Contains(
            "ThrowIfOwnerDeviceUnavailable();",
            ExtractMethodBody(
                queue,
                "protected void ValidateSparseBind("),
            StringComparison.Ordinal);
    }

    private static void AssertFailurePoisonsOwnerAfterRollback(
        string method,
        string rollback)
    {
        int rollbackIndex = method.IndexOf(
            rollback,
            StringComparison.Ordinal);
        int markLostIndex = method.IndexOf(
            ".MarkDeviceLost(deviceLoss);",
            StringComparison.Ordinal);
        Assert.True(rollbackIndex >= 0);
        Assert.True(markLostIndex > rollbackIndex);
        Assert.Contains(
            "ErrorCode: ERHIErrorCode.DeviceLost",
            method,
            StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(
        string source,
        string signature)
    {
        int signatureIndex = source.IndexOf(
            signature,
            StringComparison.Ordinal);
        Assert.True(
            signatureIndex >= 0,
            $"Missing expected method signature '{signature}'.");
        int openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0);

        int depth = 0;
        for (int index = openBrace; index < source.Length; ++index)
        {
            switch (source[index])
            {
                case '{':
                    ++depth;
                    break;
                case '}':
                    --depth;
                    if (depth == 0)
                    {
                        return source.Substring(
                            openBrace,
                            index - openBrace + 1);
                    }
                    break;
            }
        }

        throw new InvalidOperationException(
            $"Method '{signature}' has an unterminated body.");
    }

    private static int CountOccurrences(
        string source,
        string value)
    {
        int count = 0;
        int start = 0;
        while ((start = source.IndexOf(
                    value,
                    start,
                    StringComparison.Ordinal)) >= 0)
        {
            ++count;
            start += value.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "InfinityBrowser.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Failed to locate repository root from test output directory.");
    }

    private sealed class CountingCommandQueue : RHICommandQueue
    {
        public int SubmitCount { get; private set; }
        public int WaitIdleCount { get; private set; }

        public override ulong Frequency => 1;
        protected override object DeviceIdentity => this;

        public override RHICommandBuffer CreateCommandBuffer() =>
            throw new NotSupportedException();

        public override void Submit(
            in RHIQueueSubmitDescriptor descriptor)
        {
            ++SubmitCount;
        }

        public override void WaitIdle()
        {
            ThrowIfDisposed();
            ++WaitIdleCount;
        }
    }
}
