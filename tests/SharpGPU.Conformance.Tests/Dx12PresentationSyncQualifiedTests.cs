#if SHARPGPU_ENABLE_DX12
using System;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12PresentationSyncQualifiedTests
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int SwHide = 0;

    [Trait("Category", "SharpGpuWindowsQualified")]
    [Fact]
    public void Dx12_AcquireAndPresent_SignalAndWaitCallerOwnedSync()
    {
        bool initialized = FeatureContractContext.TryCreateDx12(
            out FeatureContractContext? context,
            out string reason);
        Assert.True(
            initialized,
            $"DX12 is required for the Windows-qualified presentation sync gate: {reason}");
        Assert.NotNull(context);

        using (context)
        {
            Assert.NotEqual(
                ERHICapabilityTier.Unavailable,
                context.Device.Capabilities.Presentation.SwapChain.Tier);

            IntPtr hwnd = CreateHiddenWindow();
            Assert.True(
                hwnd != IntPtr.Zero,
                $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
            try
            {
                RHISwapChainDescriptor swapChainDescriptor = new()
                {
                    FPS = 60,
                    Count = 2,
                    Extent = new uint2(64, 64),
                    Format = ERHISwapChainFormat.R8G8B8A8_UNorm,
                    SurfaceKind = ERHINativeSurfaceKind.Win32Hwnd,
                    WindowHandle = hwnd,
                    PresentMode = ERHIPresentMode.VSync,
                    PresentQueue = context.CommandQueue,
                    FrameBufferOnly = true,
                    SurfaceGeneration = 1,
                };
                using RHISwapChain swapChain =
                    context.Device.CreateSwapChain(in swapChainDescriptor);
                using RHISemaphore acquireSemaphore =
                    context.Device.CreateSemaphore();
                using RHIFence acquireFence = context.Device.CreateFence();
                using RHISemaphore renderComplete =
                    context.Device.CreateSemaphore();
                using RHIFence presentFence = context.Device.CreateFence();

                RHISwapChainAcquireResult acquire =
                    swapChain.AcquireBackBuffer(
                        new RHISwapChainAcquireDescriptor(
                            ulong.MaxValue,
                            acquireSemaphore,
                            acquireFence));
                Assert.Equal(ERHISwapChainStatus.Success, acquire.Status);
                Assert.Equal(ERHIFenceStatus.Success, acquireFence.Wait());

                RHIQueueSubmitDescriptor signalRenderComplete = new(
                    signalSemaphores: new[] { renderComplete });
                context.CommandQueue.Submit(in signalRenderComplete);

                RHISwapChainOperationResult present = swapChain.Present(
                    new RHISwapChainPresentDescriptor(
                        new[] { renderComplete },
                        presentFence));
                Assert.True(
                    present.Status is
                        ERHISwapChainStatus.Success or
                        ERHISwapChainStatus.Suboptimal or
                        ERHISwapChainStatus.Occluded,
                    $"Present returned '{present.Status}'.");
                Assert.Equal(ERHIFenceStatus.Success, presentFence.Wait());
            }
            finally
            {
                DestroyWindow(hwnd);
            }
        }
    }

    private static IntPtr CreateHiddenWindow()
    {
        IntPtr hwnd = CreateWindowEx(
            0,
            "STATIC",
            "SharpGPUPresentSync",
            WsPopup,
            0,
            0,
            64,
            64,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SwHide);
        }

        return hwnd;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
#endif
