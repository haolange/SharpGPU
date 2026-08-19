using System;
using SharpGPU.Mathematics;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12SwapChain : RHISwapChain
    {
        public override int BackTextureIndex
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                Vortice.DXGI.IDXGISwapChain3 swapChain =
                    m_NativeSwapChain3 ??
                    throw new InvalidOperationException(
                        "The DXGI swapchain3 interface is unavailable.");
                return checked((int)swapChain.CurrentBackBufferIndex);
            }
        }

        public override int ImageCount
        {
            get
            {
                ThrowIfSwapChainUnavailable();
                return m_Textures.Length;
            }
        }

        private Dx12Device m_Dx12Device;
        private Dx12Texture[] m_Textures;
        private Vortice.DXGI.IDXGISwapChain1 m_NativeSwapChain;
        private Vortice.DXGI.IDXGISwapChain3? m_NativeSwapChain3;
        private RHISwapChainDescriptor m_Descriptor;
        private bool m_HasAcquiredImage;
        private ERHISwapChainStatus m_TerminalStatus;
        private RHIException? m_TerminalDiagnostic;

        public Dx12SwapChain(
            Dx12Device device,
            in RHISwapChainDescriptor descriptor)
            : base(device)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_Textures = Array.Empty<Dx12Texture>();
            ValidateCreateDescriptor(in descriptor);
            CreateDX12SwapChain(descriptor);
            try
            {
                FetchDx12Textures(descriptor);
            }
            catch
            {
                ReleaseNativeSwapChain();
                throw;
            }
        }

        public override RHISwapChainAcquireResult AcquireBackBuffer(
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

                RHISwapChainAcquireResult result;
                if (m_TerminalStatus != ERHISwapChainStatus.Undefined)
                {
                    result = TerminalAcquireResult();
                }
                else if (m_HasAcquiredImage)
                {
                    throw new InvalidOperationException(
                        "The DX12 back buffer has already been acquired for this frame.");
                }
                else
                {
                    int imageIndex = BackTextureIndex;
                    if ((uint)imageIndex >= (uint)m_Textures.Length)
                    {
                        throw new InvalidOperationException(
                            $"DXGI returned back-buffer index {imageIndex}, " +
                            $"but exposes {m_Textures.Length} images.");
                    }

                    m_HasAcquiredImage = true;
                    SignalAcquire(
                        descriptor.SignalSemaphore,
                        descriptor.CompletionFence);
                    result = RHISwapChainAcquireResult.Acquired(
                        m_Textures[imageIndex],
                        imageIndex);
                }

                bool signalSubmitted = result.Status is
                    ERHISwapChainStatus.Success or
                    ERHISwapChainStatus.Suboptimal;

                if (signalSubmitted)
                {
                    descriptor.SignalSemaphore?.CommitSignal();
                    semaphoreReserved = false;
                    fenceReserved = false;
                }
                else
                {
                    if (fenceReserved)
                    {
                        descriptor.CompletionFence!.RollbackSignal();
                        fenceReserved = false;
                    }
                    if (semaphoreReserved)
                    {
                        descriptor.SignalSemaphore!.RollbackSignal();
                        semaphoreReserved = false;
                    }
                }

                result.Validate(OwnerDevice.BackendType, ImageCount);
                InvalidateDeviceIfNeeded(in result);
                return result;
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
                    OwnerDevice.MarkDeviceLost(exception);
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

        public override RHISwapChainOperationResult Resize(
            in RHISwapChainResizeDescriptor descriptor)
        {
            ThrowIfSwapChainUnavailable();
            try
            {
                RHISwapChainOperationResult result;
                if (m_TerminalStatus != ERHISwapChainStatus.Undefined)
                {
                    result = TerminalOperationResult();
                }
                else if (m_HasAcquiredImage)
                {
                    throw new InvalidOperationException(
                        "A DX12 swapchain cannot be resized while an image is acquired.");
                }
                else if (descriptor.SurfaceGeneration <
                    m_Descriptor.SurfaceGeneration)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(descriptor),
                        "Surface generation cannot move backwards.");
                }
                else if (descriptor.Extent.x == 0 || descriptor.Extent.y == 0)
                {
                    result = RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.NotReady);
                }
                else if (descriptor.SurfaceKind != ERHINativeSurfaceKind.Win32Hwnd ||
                    descriptor.WindowHandle == IntPtr.Zero)
                {
                    result = EnterTerminal(
                        ERHISwapChainStatus.SurfaceLost,
                        CreateSurfaceLostDiagnostic(
                            "DX12 resize requires a valid Win32 HWND."));
                }
                else if (descriptor.WindowHandle != m_Descriptor.WindowHandle)
                {
                    result = EnterTerminal(
                        ERHISwapChainStatus.SurfaceLost,
                        CreateSurfaceLostDiagnostic(
                            "DXGI ResizeBuffers cannot replace the swapchain HWND; " +
                            "the Renderer must create a new swapchain."));
                }
                else
                {
                    RHISwapChainDescriptor previousDescriptor = m_Descriptor;
                    ReleaseBackBufferTextures();

                    Vortice.DXGI.SwapChainDescription desc = m_NativeSwapChain.Description;
                    SharpGen.Runtime.Result nativeResult = m_NativeSwapChain.ResizeBuffers(
                        m_Descriptor.Count,
                        descriptor.Extent.x,
                        descriptor.Extent.y,
                        desc.BufferDescription.Format,
                        desc.Flags);
                    if (nativeResult.Failure)
                    {
                        RHISwapChainOperationResult? typedFailure =
                            TryMapFailure(
                                nativeResult.Code,
                                "IDXGISwapChain::ResizeBuffers");
                        if (typedFailure.HasValue)
                        {
                            result = EnterTerminal(
                                typedFailure.Value.Status,
                                typedFailure.Value.Diagnostic);
                        }
                        else
                        {
                            try
                            {
                                FetchDx12Textures(previousDescriptor);
                            }
                            catch
                            {
                                _ = EnterTerminal(
                                    ERHISwapChainStatus.OutOfDate,
                                    diagnostic: null);
                                throw;
                            }
                            Dx12Utility.CHECK_HR(nativeResult);
                            result = RHISwapChainOperationResult.FromStatus(
                                ERHISwapChainStatus.Success);
                        }
                    }
                    else
                    {
                        m_Descriptor.Extent = descriptor.Extent;
                        m_Descriptor.SurfaceGeneration =
                            descriptor.SurfaceGeneration;
                        try
                        {
                            FetchDx12Textures(m_Descriptor);
                        }
                        catch
                        {
                            _ = EnterTerminal(
                                ERHISwapChainStatus.OutOfDate,
                                diagnostic: null);
                            throw;
                        }
                        result = RHISwapChainOperationResult.FromStatus(
                            ERHISwapChainStatus.Success);
                    }
                }

                result.Validate(OwnerDevice.BackendType);
                InvalidateDeviceIfNeeded(in result);
                return result;
            }
            catch (RHIException exception)
            {
                if (exception.ErrorCode == ERHIErrorCode.DeviceLost)
                {
                    OwnerDevice.MarkDeviceLost(exception);
                }
                throw;
            }
        }

        public override RHISwapChainOperationResult Present(
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

                RHISwapChainOperationResult result;
                bool waitsConsumed;
                if (m_TerminalStatus != ERHISwapChainStatus.Undefined)
                {
                    result = TerminalOperationResult();
                    waitsConsumed = false;
                }
                else if (!m_HasAcquiredImage)
                {
                    throw new InvalidOperationException(
                        "Present requires one successfully acquired DX12 back buffer.");
                }
                else
                {
                    RequirePresentQueue().WaitPresentation(
                        descriptor.WaitSemaphores.Span);
                    SharpGen.Runtime.Result nativeResult = m_NativeSwapChain.Present(
                        Dx12Utility.ConvertToDx12SyncInterval(
                            m_Descriptor.PresentMode),
                        0);
                    m_HasAcquiredImage = false;

                    if (nativeResult.Code == DxgiStatusOccluded)
                    {
                        result = RHISwapChainOperationResult.FromStatus(
                            ERHISwapChainStatus.Occluded);
                    }
                    else if (nativeResult.Failure)
                    {
                        RHISwapChainOperationResult? typedFailure =
                            TryMapFailure(
                                nativeResult.Code,
                                "IDXGISwapChain::Present");
                        if (typedFailure.HasValue)
                        {
                            result = EnterTerminal(
                                typedFailure.Value.Status,
                                typedFailure.Value.Diagnostic);
                        }
                        else
                        {
                            Dx12Utility.CHECK_HR(nativeResult);
                            result = RHISwapChainOperationResult.FromStatus(
                                ERHISwapChainStatus.Success);
                        }
                    }
                    else
                    {
                        result = RHISwapChainOperationResult.FromStatus(
                            ERHISwapChainStatus.Success);
                    }

                    SignalPresentCompletion(descriptor.CompletionFence);
                    waitsConsumed = true;
                }

                if (waitsConsumed)
                {
                    for (int index = 0; index < waits.Length; ++index)
                    {
                        waits[index].CommitWait();
                    }
                    fenceReserved = false;
                }
                else
                {
                    for (int index = waits.Length - 1; index >= 0; --index)
                    {
                        waits[index].RollbackWait();
                    }
                    if (fenceReserved)
                    {
                        descriptor.CompletionFence!.RollbackSignal();
                        fenceReserved = false;
                    }
                }
                reservedWaits = 0;

                result.Validate(OwnerDevice.BackendType);
                InvalidateDeviceIfNeeded(in result);
                return result;
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
                    OwnerDevice.MarkDeviceLost(exception);
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

        private Dx12CommandQueue RequirePresentQueue()
        {
            if (m_Descriptor.PresentQueue is not Dx12CommandQueue queue ||
                !ReferenceEquals(queue.Dx12Device, m_Dx12Device))
            {
                throw new InvalidOperationException(
                    "DX12 presentation requires a present queue from this device.");
            }

            return queue;
        }

        private void SignalAcquire(
            RHISemaphore? signalSemaphore,
            RHIFence? completionFence)
        {
            Dx12Semaphore? semaphore = null;
            if (signalSemaphore != null)
            {
                semaphore = signalSemaphore as Dx12Semaphore ??
                    throw new ArgumentException(
                        "The acquire signal semaphore is not a DX12 semaphore.");
            }

            Dx12Fence? fence = null;
            if (completionFence != null)
            {
                fence = completionFence as Dx12Fence ??
                    throw new ArgumentException(
                        "The acquire completion fence is not a DX12 fence.");
            }

            RequirePresentQueue().SignalPresentation(semaphore, fence);
        }

        private void SignalPresentCompletion(RHIFence? completionFence)
        {
            if (completionFence == null)
            {
                return;
            }

            Dx12Fence fence = completionFence as Dx12Fence ??
                throw new ArgumentException(
                    "The present completion fence is not a DX12 fence.");
            RequirePresentQueue().SignalPresentation(semaphore: null, fence);
        }

        private void CreateDX12SwapChain(in RHISwapChainDescriptor descriptor)
        {
            Dx12CommandQueue dx12Queue = (Dx12CommandQueue)descriptor.PresentQueue;
            Dx12Instance dx12Instance = m_Dx12Device.Dx12Instance;

            Vortice.DXGI.SwapChainDescription1 desc = new Vortice.DXGI.SwapChainDescription1();
            desc.BufferCount = descriptor.Count;
            desc.Width = descriptor.Extent.x;
            desc.Height = descriptor.Extent.y;
            desc.Format = Dx12Utility.ConvertToDx12ViewFormat(RHIUtility.ConvertToPixelFormat(descriptor.Format));
            desc.SampleDescription = new Vortice.DXGI.SampleDescription(1, 0);
            desc.SwapEffect = Dx12Utility.ConvertToDx12SwapEffect(m_Descriptor.PresentMode);
            desc.BufferUsage = descriptor.FrameBufferOnly ? Vortice.DXGI.Usage.RenderTargetOutput : (Vortice.DXGI.Usage.ShaderInput | Vortice.DXGI.Usage.RenderTargetOutput);

            Vortice.DXGI.IDXGISwapChain1 dx12SwapChain1 = dx12Instance.DXGIFactory.CreateSwapChainForHwnd(
                dx12Queue.NativeCommandQueue,
                descriptor.WindowHandle,
                desc,
                null,
                null);
            m_NativeSwapChain = dx12SwapChain1;
            m_NativeSwapChain3 = dx12SwapChain1.QueryInterfaceOrNull<Vortice.DXGI.IDXGISwapChain3>();
            if (m_NativeSwapChain3 == null)
            {
                m_NativeSwapChain.Release();
                throw new NotSupportedException(
                    "DXGI 1.4 IDXGISwapChain3 is required for exact back-buffer indexing.");
            }
        }

        private void FetchDx12Textures(in RHISwapChainDescriptor descriptor)
        {
            RHITextureDescriptor textureDescriptor;
            {
                textureDescriptor.Extent = new uint3(descriptor.Extent.xy, 1);
                textureDescriptor.MipCount = 1;
                textureDescriptor.SampleCount = ERHISampleCount.None;
                textureDescriptor.Format = RHIUtility.ConvertToPixelFormat(descriptor.Format);
                textureDescriptor.UsageFlag = ERHITextureUsage.RenderTarget;
                textureDescriptor.Dimension = ERHITextureDimension.Texture2D;
                textureDescriptor.StorageMode = ERHIStorageMode.GPULocal;
            }

            int imageCount = checked(
                (int)m_NativeSwapChain.Description.BufferCount);
            if (imageCount <= 0)
            {
                throw new InvalidOperationException(
                    "DXGI reported a swapchain with no images.");
            }

            Dx12Texture[] textures = new Dx12Texture[imageCount];
            int createdCount = 0;
            try
            {
                for (; createdCount < imageCount; ++createdCount)
                {
                    SharpGen.Runtime.Result nativeResult =
                        m_NativeSwapChain.GetBuffer(
                            (uint)createdCount,
                            out Vortice.Direct3D12.ID3D12Resource? resource);
                    Vortice.Direct3D12.ID3D12Resource nativeBackBuffer =
                        Dx12Utility.RequireCreatedObject(
                            resource,
                            nativeResult,
                            $"IDXGISwapChain.GetBuffer({createdCount})");
                    try
                    {
                        textures[createdCount] = new Dx12Texture(
                            m_Dx12Device,
                            textureDescriptor,
                            nativeBackBuffer);
                    }
                    catch
                    {
                        nativeBackBuffer.Release();
                        throw;
                    }
                }
                m_Textures = textures;
            }
            catch
            {
                for (int index = 0; index < createdCount; ++index)
                {
                    textures[index].Dispose();
                }
                throw;
            }
        }

        private void ReleaseBackBufferTextures()
        {
            for (int i = 0; i < m_Textures.Length; ++i)
            {
                m_Textures[i].Dispose();
            }
            m_Textures = Array.Empty<Dx12Texture>();
        }

        private RHISwapChainOperationResult? TryMapFailure(
            int nativeCode,
            string operation)
        {
            switch (nativeCode)
            {
                case DxgiErrorDeviceRemoved:
                case DxgiErrorDeviceHung:
                case DxgiErrorDeviceReset:
                    return RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.DeviceLost,
                        Dx12DeviceLossDiagnostics.Capture(
                            m_Dx12Device,
                            nativeCode,
                            operation));
                case DxgiErrorAccessLost:
                    return RHISwapChainOperationResult.FromStatus(
                        ERHISwapChainStatus.SurfaceLost,
                        new RHIException(
                            ERHIErrorCode.SurfaceLost,
                            ERHIBackend.DirectX12,
                            unchecked((uint)nativeCode),
                            $"{operation} reported that the DXGI presentation surface was lost.",
                            ERHIDeviceState.Operational));
                default:
                    return null;
            }
        }

        private static RHIException CreateSurfaceLostDiagnostic(
            string message)
        {
            return new RHIException(
                ERHIErrorCode.SurfaceLost,
                ERHIBackend.DirectX12,
                nativeCode: 0,
                nativeMessage: message,
                deviceState: ERHIDeviceState.Operational);
        }

        private RHISwapChainOperationResult EnterTerminal(
            ERHISwapChainStatus status,
            RHIException? diagnostic)
        {
            if (status is not
                (ERHISwapChainStatus.OutOfDate or
                 ERHISwapChainStatus.SurfaceLost or
                 ERHISwapChainStatus.DeviceLost))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status,
                    "DX12 terminal status must be OutOfDate, SurfaceLost, or DeviceLost.");
            }
            m_TerminalStatus = status;
            m_TerminalDiagnostic = diagnostic;
            m_HasAcquiredImage = false;
            return TerminalOperationResult();
        }

        private RHISwapChainAcquireResult TerminalAcquireResult()
        {
            return RHISwapChainAcquireResult.Unavailable(
                m_TerminalStatus,
                m_TerminalDiagnostic);
        }

        private RHISwapChainOperationResult TerminalOperationResult()
        {
            return RHISwapChainOperationResult.FromStatus(
                m_TerminalStatus,
                m_TerminalDiagnostic);
        }

        private void ValidateCreateDescriptor(
            in RHISwapChainDescriptor descriptor)
        {
            if (descriptor.SurfaceKind !=
                ERHINativeSurfaceKind.Win32Hwnd)
            {
                throw new NotSupportedException(
                    "DX12 swapchains require a Win32 HWND surface.");
            }
            if (descriptor.WindowHandle == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "DX12 swapchain HWND must not be null.",
                    nameof(descriptor));
            }
            if (descriptor.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "DX12 swapchain image count must be positive.");
            }
            if (descriptor.Extent.x == 0 ||
                descriptor.Extent.y == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "DX12 swapchain extent must be non-zero.");
            }
            if (descriptor.PresentQueue is not
                    Dx12CommandQueue queue ||
                !ReferenceEquals(queue.Dx12Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    "DX12 present queue must be a graphics queue from the same device.",
                    nameof(descriptor));
            }
            if (queue.PipelineType !=
                ERHIPipelineType.Graphics)
            {
                throw new ArgumentException(
                    "DX12 present queue must be a graphics queue.",
                    nameof(descriptor));
            }
        }

        protected override void Release()
        {
            ReleaseBackBufferTextures();
            ReleaseNativeSwapChain();
        }

        private void ReleaseNativeSwapChain()
        {
            if (m_NativeSwapChain3 != null)
            {
                m_NativeSwapChain3.Release();
                m_NativeSwapChain3 = null;
            }
            m_NativeSwapChain.Release();
        }

        private const int DxgiStatusOccluded =
            unchecked((int)0x087A0001);
        private const int DxgiErrorDeviceRemoved =
            unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceHung =
            unchecked((int)0x887A0006);
        private const int DxgiErrorDeviceReset =
            unchecked((int)0x887A0007);
        private const int DxgiErrorAccessLost =
            unchecked((int)0x887A0026);
    }
#pragma warning restore CA1416
}
