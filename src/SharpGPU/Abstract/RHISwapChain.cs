using System;
using SharpGPU.Core;
using SharpMath;

namespace SharpGPU
{
    public enum ERHISwapChainStatus : byte
    {
        Undefined = 0,
        Success,
        NotReady,
        Timeout,
        Occluded,
        Suboptimal,
        OutOfDate,
        SurfaceLost,
        DeviceLost
    }

    public enum ERHINativeSurfaceKind : byte
    {
        Unknown = 0,
        Win32Hwnd,
        AppKitNsWindow,
        X11Window,
        WaylandSurface,
        UIKitUiWindow,
        AndroidNativeWindow,
        Headless,
    }

    public struct RHISwapChainDescriptor
    {
        public bool FrameBufferOnly;
        public uint FPS;
        public uint Count;
        public uint2 Extent;
        public ERHINativeSurfaceKind SurfaceKind;
        public IntPtr WindowHandle;
        public IntPtr DisplayHandle;
        public IntPtr InstanceHandle;
        public uint SurfaceGeneration;
        public ERHIPresentMode PresentMode;
        public ERHISwapChainFormat Format;
        public RHICommandQueue PresentQueue;
    }

    public readonly struct RHISwapChainAcquireDescriptor
    {
        public ulong TimeoutNanoseconds { get; }
        public RHISemaphore? SignalSemaphore { get; }
        public RHIFence? CompletionFence { get; }

        public RHISwapChainAcquireDescriptor(
            ulong timeoutNanoseconds = 0,
            RHISemaphore? signalSemaphore = null,
            RHIFence? completionFence = null)
        {
            TimeoutNanoseconds = timeoutNanoseconds;
            SignalSemaphore = signalSemaphore;
            CompletionFence = completionFence;
        }
    }

    public readonly struct RHISwapChainPresentDescriptor
    {
        public ReadOnlyMemory<RHISemaphore> WaitSemaphores { get; }
        public RHIFence? CompletionFence { get; }

        public RHISwapChainPresentDescriptor(
            ReadOnlyMemory<RHISemaphore> waitSemaphores = default,
            RHIFence? completionFence = null)
        {
            WaitSemaphores = waitSemaphores;
            CompletionFence = completionFence;
        }
    }

    public readonly struct RHISwapChainResizeDescriptor
    {
        public uint2 Extent { get; }
        public ERHINativeSurfaceKind SurfaceKind { get; }
        public IntPtr WindowHandle { get; }
        public IntPtr DisplayHandle { get; }
        public IntPtr InstanceHandle { get; }
        public uint SurfaceGeneration { get; }

        public RHISwapChainResizeDescriptor(
            in uint2 extent,
            ERHINativeSurfaceKind surfaceKind,
            IntPtr windowHandle,
            IntPtr displayHandle = default,
            IntPtr instanceHandle = default,
            uint surfaceGeneration = 0)
        {
            if (!Enum.IsDefined(surfaceKind) ||
                surfaceKind == ERHINativeSurfaceKind.Unknown)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(surfaceKind),
                    surfaceKind,
                    "A swapchain resize must name a known native surface kind.");
            }

            Extent = extent;
            SurfaceKind = surfaceKind;
            WindowHandle = windowHandle;
            DisplayHandle = displayHandle;
            InstanceHandle = instanceHandle;
            SurfaceGeneration = surfaceGeneration;
        }
    }

    public readonly struct RHISwapChainAcquireResult
    {
        public ERHISwapChainStatus Status { get; }
        public RHITexture? Texture { get; }
        public int ImageIndex { get; }
        public RHIException? Diagnostic { get; }
        public bool HasImage =>
            Status is ERHISwapChainStatus.Success or
                ERHISwapChainStatus.Suboptimal;

        private RHISwapChainAcquireResult(
            ERHISwapChainStatus status,
            RHITexture? texture,
            int imageIndex,
            RHIException? diagnostic)
        {
            Status = status;
            Texture = texture;
            ImageIndex = imageIndex;
            Diagnostic = diagnostic;
        }

        internal static RHISwapChainAcquireResult Acquired(
            RHITexture texture,
            int imageIndex,
            bool suboptimal = false)
        {
            ArgumentNullException.ThrowIfNull(texture);
            if (imageIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageIndex),
                    imageIndex,
                    "An acquired swapchain image index cannot be negative.");
            }

            return new RHISwapChainAcquireResult(
                suboptimal
                    ? ERHISwapChainStatus.Suboptimal
                    : ERHISwapChainStatus.Success,
                texture,
                imageIndex,
                diagnostic: null);
        }

        internal static RHISwapChainAcquireResult Unavailable(
            ERHISwapChainStatus status,
            RHIException? diagnostic = null)
        {
            if (status is ERHISwapChainStatus.Success or
                ERHISwapChainStatus.Suboptimal)
            {
                throw new ArgumentException(
                    "An acquired status must carry an image.",
                    nameof(status));
            }

            return new RHISwapChainAcquireResult(
                status,
                texture: null,
                imageIndex: -1,
                diagnostic);
        }

        internal void Validate(
            ERHIBackend backend,
            int imageCount)
        {
            RHISwapChainStatusContract.Validate(
                Status,
                Diagnostic,
                backend);

            if (HasImage)
            {
                if (Texture == null)
                {
                    throw new InvalidOperationException(
                        "A successful swapchain acquire must return a texture.");
                }
                if (Texture.IsDisposed)
                {
                    throw new InvalidOperationException(
                        "A successful swapchain acquire returned a disposed texture.");
                }
                if ((uint)ImageIndex >= (uint)imageCount)
                {
                    throw new InvalidOperationException(
                        $"Swapchain acquire returned image index {ImageIndex}, " +
                        $"but the native swapchain exposes {imageCount} images.");
                }
            }
            else if (Texture != null || ImageIndex != -1)
            {
                throw new InvalidOperationException(
                    "An unsuccessful swapchain acquire cannot return an image.");
            }
        }
    }

    public readonly struct RHISwapChainOperationResult
    {
        public ERHISwapChainStatus Status { get; }
        public RHIException? Diagnostic { get; }
        public bool IsSuccess =>
            Status is ERHISwapChainStatus.Success or
                ERHISwapChainStatus.Suboptimal;

        private RHISwapChainOperationResult(
            ERHISwapChainStatus status,
            RHIException? diagnostic)
        {
            Status = status;
            Diagnostic = diagnostic;
        }

        internal static RHISwapChainOperationResult FromStatus(
            ERHISwapChainStatus status,
            RHIException? diagnostic = null)
        {
            return new RHISwapChainOperationResult(status, diagnostic);
        }

        internal void Validate(ERHIBackend backend)
        {
            RHISwapChainStatusContract.Validate(
                Status,
                Diagnostic,
                backend);
        }
    }

    internal static class RHISwapChainStatusContract
    {
        internal static void Validate(
            ERHISwapChainStatus status,
            RHIException? diagnostic,
            ERHIBackend backend)
        {
            if (!Enum.IsDefined(status) ||
                status == ERHISwapChainStatus.Undefined)
            {
                throw new InvalidOperationException(
                    $"A backend returned unknown swapchain status '{status}'.");
            }

            if (diagnostic != null && diagnostic.Backend != backend)
            {
                throw new InvalidOperationException(
                    "A swapchain diagnostic belongs to a different backend.");
            }

            switch (status)
            {
                case ERHISwapChainStatus.SurfaceLost:
                    if (diagnostic == null ||
                        diagnostic.ErrorCode != ERHIErrorCode.SurfaceLost ||
                        diagnostic.DeviceState != ERHIDeviceState.Operational)
                    {
                        throw new InvalidOperationException(
                            "SurfaceLost must carry an operational-device SurfaceLost diagnostic.");
                    }
                    break;
                case ERHISwapChainStatus.DeviceLost:
                    if (diagnostic == null ||
                        diagnostic.ErrorCode != ERHIErrorCode.DeviceLost ||
                        diagnostic.DeviceState is
                            ERHIDeviceState.Unknown or
                            ERHIDeviceState.Operational)
                    {
                        throw new InvalidOperationException(
                            "DeviceLost must carry a non-operational DeviceLost diagnostic.");
                    }
                    break;
                default:
                    if (diagnostic != null)
                    {
                        throw new InvalidOperationException(
                            $"{status} must not carry a native failure diagnostic.");
                    }
                    break;
            }
        }
    }

    public abstract class RHISwapChain : Disposal
    {
        private readonly RHIDevice m_OwnerDevice;

        protected RHISwapChain(RHIDevice ownerDevice)
        {
            m_OwnerDevice = ownerDevice ??
                throw new ArgumentNullException(nameof(ownerDevice));
        }

        protected RHIDevice OwnerDevice => m_OwnerDevice;

        public abstract int BackTextureIndex
        {
            get;
        }

        public abstract int ImageCount
        {
            get;
        }

        protected void ThrowIfSwapChainUnavailable()
        {
            ThrowIfDisposed();
            m_OwnerDevice.ThrowIfDeviceUnavailable();
        }

        public virtual RHISwapChainAcquireResult AcquireBackBuffer(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            ValidateAcquire(in descriptor);

            bool semaphoreReserved = false;
            bool fenceReserved = false;
            try
            {
                if (descriptor.SignalSemaphore != null)
                {
                    descriptor.SignalSemaphore.ReserveSignal();
                    semaphoreReserved = true;
                }
                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    fenceReserved = true;
                }

                throw new NotSupportedException(
                    $"{GetType().Name} does not implement swapchain back-buffer acquisition.");
            }
            catch (RHIException exception)
            {
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (semaphoreReserved)
                {
                    descriptor.SignalSemaphore!.RollbackSignal();
                }
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    m_OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
            catch
            {
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (semaphoreReserved)
                {
                    descriptor.SignalSemaphore!.RollbackSignal();
                }
                throw;
            }
        }

        public virtual RHISwapChainOperationResult Resize(
            in RHISwapChainResizeDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            try
            {
                throw new NotSupportedException(
                    $"{GetType().Name} does not implement swapchain resize.");
            }
            catch (RHIException exception)
            {
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    m_OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
        }

        public virtual RHISwapChainOperationResult Present(
            in RHISwapChainPresentDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            ValidatePresent(in descriptor);

            int reservedWaits = 0;
            bool fenceReserved = false;
            try
            {
                ReadOnlySpan<RHISemaphore> waits =
                    descriptor.WaitSemaphores.Span;
                for (; reservedWaits < waits.Length; ++reservedWaits)
                {
                    waits[reservedWaits].ReserveWait();
                }
                if (descriptor.CompletionFence != null)
                {
                    descriptor.CompletionFence.ReserveSignal();
                    fenceReserved = true;
                }

                throw new NotSupportedException(
                    $"{GetType().Name} does not implement swapchain presentation.");
            }
            catch (RHIException exception)
            {
                RollbackPresentWaits(
                    descriptor.WaitSemaphores.Span,
                    reservedWaits);
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    m_OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
            catch
            {
                RollbackPresentWaits(
                    descriptor.WaitSemaphores.Span,
                    reservedWaits);
                if (fenceReserved)
                {
                    descriptor.CompletionFence!.RollbackSignal();
                }
                throw;
            }
        }

        protected void ValidateAcquire(
            in RHISwapChainAcquireDescriptor descriptor)
        {
            ValidateSynchronization(
                descriptor.SignalSemaphore,
                "acquire signal semaphore");
            ValidateSynchronization(
                descriptor.CompletionFence,
                "acquire completion fence");
        }

        protected void ValidatePresent(
            in RHISwapChainPresentDescriptor descriptor)
        {
            ReadOnlySpan<RHISemaphore> waits =
                descriptor.WaitSemaphores.Span;
            for (int index = 0; index < waits.Length; ++index)
            {
                RHISemaphore semaphore = waits[index] ??
                    throw new ArgumentException(
                        $"Present wait semaphore at index {index} is null.",
                        nameof(descriptor));
                ValidateSynchronization(
                    semaphore,
                    $"present wait semaphore at index {index}");

                for (int prior = 0; prior < index; ++prior)
                {
                    if (ReferenceEquals(waits[prior], semaphore))
                    {
                        throw new ArgumentException(
                            $"Present wait semaphore at index {index} is duplicated.",
                            nameof(descriptor));
                    }
                }
            }
            ValidateSynchronization(
                descriptor.CompletionFence,
                "present completion fence");
        }

        private void ValidateSynchronization(
            RHISemaphore? semaphore,
            string name)
        {
            if (semaphore == null)
            {
                return;
            }
            if (semaphore.IsDisposed)
            {
                throw new ObjectDisposedException(
                    semaphore.GetType().FullName);
            }
            if (!ReferenceEquals(
                    semaphore.OwnerDevice,
                    m_OwnerDevice))
            {
                throw new ArgumentException(
                    $"The {name} was created by a different device.");
            }
        }

        private void ValidateSynchronization(
            RHIFence? fence,
            string name)
        {
            if (fence == null)
            {
                return;
            }
            if (fence.IsDisposed)
            {
                throw new ObjectDisposedException(
                    fence.GetType().FullName);
            }
            if (!ReferenceEquals(
                    fence.OwnerDevice,
                    m_OwnerDevice))
            {
                throw new ArgumentException(
                    $"The {name} was created by a different device.");
            }
        }

        protected static void RollbackPresentWaits(
            ReadOnlySpan<RHISemaphore> waits,
            int reservedWaits)
        {
            for (int index = reservedWaits - 1; index >= 0; --index)
            {
                waits[index].RollbackWait();
            }
        }

        protected void InvalidateDeviceIfNeeded(
            in RHISwapChainAcquireResult result)
        {
            if (result.Status == ERHISwapChainStatus.DeviceLost)
            {
                m_OwnerDevice.MarkDeviceLost(result.Diagnostic!);
            }
        }

        protected void InvalidateDeviceIfNeeded(
            in RHISwapChainOperationResult result)
        {
            if (result.Status == ERHISwapChainStatus.DeviceLost)
            {
                m_OwnerDevice.MarkDeviceLost(result.Diagnostic!);
            }
        }
    }
}
