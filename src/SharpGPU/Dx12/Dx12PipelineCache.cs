using System;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal sealed unsafe class Dx12PipelineCache : RHIPipelineCache
    {
        private const int EInvalidArg = unchecked((int)0x80070057);
        private const int ENoInterface = unchecked((int)0x80004002);
        private const int EFail = unchecked((int)0x80004005);
        private const int EOutOfMemory = unchecked((int)0x8007000E);
        private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

        private readonly object m_Gate = new object();
        private readonly Dx12Device m_Dx12Device;
        private Vortice.Direct3D12.ID3D12PipelineLibrary1? m_NativePipelineCache;

        private Vortice.Direct3D12.ID3D12PipelineLibrary1 NativePipelineCache =>
            m_NativePipelineCache ?? throw new ObjectDisposedException(GetType().FullName);

        private int m_NativeHitCount;
        private int m_NativeMissCount;
        private int m_NativeStoreCount;

        internal int NativeHitCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeHitCount;
                }
            }
        }

        internal int NativeMissCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeMissCount;
                }
            }
        }

        internal int NativeStoreCount
        {
            get
            {
                lock (m_Gate)
                {
                    return m_NativeStoreCount;
                }
            }
        }

        internal static bool TryProbeNativeSupport(Dx12Device device, out string reason)
        {
            ArgumentNullException.ThrowIfNull(device);

            Vortice.Direct3D12.ID3D12PipelineLibrary? baseCache = null;
            Vortice.Direct3D12.ID3D12PipelineLibrary1? nativeCache = null;
            try
            {
                SharpGen.Runtime.Result result =
                    ((Vortice.Direct3D12.ID3D12Device2)device.NativeDevice)
                    .CreatePipelineLibrary(
                        Array.Empty<byte>().AsSpan(),
                        out baseCache);
                if (result.Failure || baseCache == null)
                {
                    reason =
                        $"ID3D12Device2.CreatePipelineLibrary(empty) failed with HRESULT=0x{result.Code:X8}.";
                    return false;
                }

                nativeCache =
                    baseCache.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
                if (nativeCache == null)
                {
                    reason =
                        "The runtime-created DX12 pipeline library does not expose ID3D12PipelineLibrary1.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                reason =
                    $"DX12 pipeline-library runtime probing threw HRESULT=0x{exception.HResult:X8}.";
                return false;
            }
            finally
            {
                nativeCache?.Release();
                baseCache?.Release();
            }
        }

        internal Dx12PipelineCache(Dx12Device device)
            : base(device)
        {
            m_Dx12Device = device;
            if (!TryCreateNativeCache(
                    ReadOnlySpan<byte>.Empty,
                    out m_NativePipelineCache,
                    out string reason,
                    out int nativeCode))
            {
                throw CreateNativeException(
                    ERHIErrorCode.InitializationFailed,
                    nativeCode,
                    $"DX12 pipeline-cache initialization failed: {reason}");
            }
        }

        public override RHIComputePipeline CreateComputePipeline(
            in RHIComputePipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12ComputePipeline(m_Dx12Device, descriptor, this);
        }

        public override RHIRasterPipeline CreateRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new Dx12RasterPipeline(m_Dx12Device, descriptor, this);
        }

        internal Vortice.Direct3D12.ID3D12PipelineState CreateComputePipelineState(
            in RHIComputePipelineDescriptor descriptor,
            in Vortice.Direct3D12.ComputePipelineStateDescription nativeDescriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            string key = BuildComputePipelineCacheKey(descriptor);

            lock (m_Gate)
            {
                try
                {
                    Vortice.Direct3D12.ID3D12PipelineState? nativePipeline =
                        NativePipelineCache.LoadComputePipeline(
                        key,
                        nativeDescriptor);
                    if (nativePipeline == null)
                    {
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            EFail,
                            $"DX12 compute pipeline cache returned a null state for key {key}.");
                    }
                    ++m_NativeHitCount;
                    return nativePipeline;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                    when (exception.HResult == EInvalidArg)
                {
                    // E_INVALIDARG is the documented cache-miss signal for Load*Pipeline.
                    ++m_NativeMissCount;
                    SharpGen.Runtime.Result createResult =
                        m_Dx12Device.NativeDevice.CreateComputePipelineState(
                            nativeDescriptor,
                            out Vortice.Direct3D12.ID3D12PipelineState? nativePipeline);
                    if (createResult.Failure || nativePipeline == null)
                    {
                        nativePipeline?.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            createResult.Code,
                            $"DX12 compute pipeline creation failed while populating cache key {key}. "
                            + $"HRESULT=0x{createResult.Code:X8}.");
                    }

                    try
                    {
                        NativePipelineCache.StorePipeline(key, nativePipeline);
                        ++m_NativeStoreCount;
                        return nativePipeline;
                    }
                    catch (SharpGen.Runtime.SharpGenException storeException)
                    {
                        nativePipeline.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            storeException.HResult,
                            $"DX12 compute pipeline cache store failed for key {key}.",
                            storeException);
                    }
                    catch
                    {
                        nativePipeline.Release();
                        throw;
                    }
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        $"DX12 compute pipeline cache lookup failed for key {key}.",
                        exception);
                }
            }
        }

        internal Vortice.Direct3D12.ID3D12PipelineState CreateRasterPipelineState(
            in RHIRasterPipelineDescriptor descriptor,
            in Vortice.Direct3D12.GraphicsPipelineStateDescription nativeDescriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            string key = BuildRasterPipelineCacheKey(descriptor);

            lock (m_Gate)
            {
                try
                {
                    Vortice.Direct3D12.ID3D12PipelineState? nativePipeline =
                        NativePipelineCache.LoadGraphicsPipeline(
                        key,
                        nativeDescriptor);
                    if (nativePipeline == null)
                    {
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            EFail,
                            $"DX12 raster pipeline cache returned a null state for key {key}.");
                    }
                    ++m_NativeHitCount;
                    return nativePipeline;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                    when (exception.HResult == EInvalidArg)
                {
                    // E_INVALIDARG is the documented cache-miss signal for Load*Pipeline.
                    ++m_NativeMissCount;
                    SharpGen.Runtime.Result createResult =
                        m_Dx12Device.NativeDevice.CreateGraphicsPipelineState(
                            nativeDescriptor,
                            out Vortice.Direct3D12.ID3D12PipelineState? nativePipeline);
                    if (createResult.Failure || nativePipeline == null)
                    {
                        nativePipeline?.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            createResult.Code,
                            $"DX12 raster pipeline creation failed while populating cache key {key}. "
                            + $"HRESULT=0x{createResult.Code:X8}.");
                    }

                    try
                    {
                        NativePipelineCache.StorePipeline(key, nativePipeline);
                        ++m_NativeStoreCount;
                        return nativePipeline;
                    }
                    catch (SharpGen.Runtime.SharpGenException storeException)
                    {
                        nativePipeline.Release();
                        throw CreateNativeException(
                            ERHIErrorCode.NativeFailure,
                            storeException.HResult,
                            $"DX12 raster pipeline cache store failed for key {key}.",
                            storeException);
                    }
                    catch
                    {
                        nativePipeline.Release();
                        throw;
                    }
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        $"DX12 raster pipeline cache lookup failed for key {key}.",
                        exception);
                }
            }
        }

        protected override bool TryReplaceNativePayload(
            ReadOnlySpan<byte> nativePayload,
            out string reason)
        {
            ThrowIfDisposed();
            if (!TryCreateNativeCache(
                    nativePayload,
                    out Vortice.Direct3D12.ID3D12PipelineLibrary1? replacement,
                    out reason,
                    out int nativeCode))
            {
                if (nativePayload.IsEmpty || IsFatalNativeFailure(nativeCode))
                {
                    throw CreateNativeException(
                        ERHIErrorCode.InitializationFailed,
                        nativeCode,
                        $"DX12 pipeline-cache import initialization failed: {reason}");
                }

                // A compatible wrapper whose backend-private payload is rejected is corrupt.
                return false;
            }

            lock (m_Gate)
            {
                Vortice.Direct3D12.ID3D12PipelineLibrary1? previous =
                    m_NativePipelineCache;
                m_NativePipelineCache = replacement;
                previous?.Release();
            }
            return true;
        }

        protected override byte[] ExportNativePayload()
        {
            ThrowIfDisposed();
            lock (m_Gate)
            {
                try
                {
                    SharpGen.Runtime.PointerUSize nativeSize =
                        NativePipelineCache.SerializedSize;
                    nuint byteCount = nativeSize;
                    if (byteCount > int.MaxValue)
                    {
                        throw new InvalidOperationException(
                            $"DX12 pipeline cache is {byteCount} bytes, exceeding the managed blob limit.");
                    }

                    byte[] payload = new byte[(int)byteCount];
                    if (payload.Length == 0)
                    {
                        return payload;
                    }

                    fixed (byte* payloadPointer = payload)
                    {
                        NativePipelineCache.Serialize(
                            (IntPtr)payloadPointer,
                            nativeSize);
                    }
                    return payload;
                }
                catch (SharpGen.Runtime.SharpGenException exception)
                {
                    throw CreateNativeException(
                        ERHIErrorCode.NativeFailure,
                        exception.HResult,
                        "DX12 pipeline-cache export failed.",
                        exception);
                }
            }
        }

        protected override void Release()
        {
            lock (m_Gate)
            {
                if (m_NativePipelineCache != null)
                {
                    m_NativePipelineCache.Release();
                    m_NativePipelineCache = null;
                }
            }
        }

        private bool TryCreateNativeCache(
            ReadOnlySpan<byte> nativePayload,
            out Vortice.Direct3D12.ID3D12PipelineLibrary1? nativeCache,
            out string reason,
            out int nativeCode)
        {
            nativeCache = null;
            reason = string.Empty;
            nativeCode = 0;
            byte[] payloadCopy = nativePayload.ToArray();
            SharpGen.Runtime.Result createResult;
            Vortice.Direct3D12.ID3D12PipelineLibrary? baseCache = null;
            try
            {
                createResult =
                    ((Vortice.Direct3D12.ID3D12Device2)m_Dx12Device.NativeDevice)
                    .CreatePipelineLibrary(
                        payloadCopy.AsSpan(),
                        out baseCache);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                baseCache?.Release();
                nativeCode = exception.HResult;
                reason =
                    $"ID3D12Device2.CreatePipelineLibrary threw HRESULT=0x{nativeCode:X8}.";
                return false;
            }
            if (createResult.Failure || baseCache == null)
            {
                nativeCode = createResult.Code;
                reason =
                    $"ID3D12Device2.CreatePipelineLibrary failed with HRESULT=0x{createResult.Code:X8}.";
                baseCache?.Release();
                return false;
            }

            try
            {
                nativeCache =
                    baseCache.QueryInterfaceOrNull<Vortice.Direct3D12.ID3D12PipelineLibrary1>();
                if (nativeCache == null)
                {
                    nativeCode = ENoInterface;
                    reason =
                        "The created DX12 cache does not expose ID3D12PipelineLibrary1.";
                    return false;
                }
                return true;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                nativeCache?.Release();
                nativeCache = null;
                nativeCode = exception.HResult;
                reason =
                    $"ID3D12PipelineLibrary1 query failed with HRESULT=0x{nativeCode:X8}.";
                return false;
            }
            finally
            {
                baseCache.Release();
            }
        }

        private bool IsFatalNativeFailure(in int nativeCode)
        {
            return nativeCode == EOutOfMemory
                || GetDeviceState(out _) != ERHIDeviceState.Operational;
        }

        private RHIException CreateNativeException(
            in ERHIErrorCode requestedErrorCode,
            in long nativeCode,
            string message,
            Exception? innerException = null)
        {
            ERHIDeviceState deviceState = GetDeviceState(out int deviceNativeCode);
            ERHIErrorCode errorCode =
                deviceState != ERHIDeviceState.Operational
                    ? ERHIErrorCode.DeviceLost
                    : nativeCode == EOutOfMemory
                        ? ERHIErrorCode.OutOfMemory
                        : requestedErrorCode;
            long effectiveNativeCode =
                nativeCode != 0 ? nativeCode : deviceNativeCode;
            return new RHIException(
                errorCode,
                ERHIBackend.DirectX12,
                effectiveNativeCode,
                message,
                deviceState,
                innerException);
        }

        private ERHIDeviceState GetDeviceState(out int nativeCode)
        {
            try
            {
                SharpGen.Runtime.Result removalReason =
                    m_Dx12Device.NativeDevice.DeviceRemovedReason;
                nativeCode = removalReason.Code;
                if (removalReason.Success)
                {
                    return ERHIDeviceState.Operational;
                }

                return nativeCode == DxgiErrorDeviceReset
                    ? ERHIDeviceState.Reset
                    : ERHIDeviceState.Removed;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                nativeCode = exception.HResult;
                return nativeCode == DxgiErrorDeviceReset
                    ? ERHIDeviceState.Reset
                    : nativeCode == DxgiErrorDeviceRemoved
                        ? ERHIDeviceState.Removed
                        : ERHIDeviceState.Unknown;
            }
        }

        private void ValidateLayoutDevice(RHIPipelineLayout? pipelineLayout)
        {
            Dx12PipelineLayout dx12Layout = pipelineLayout as Dx12PipelineLayout
                ?? throw new ArgumentException(
                    "DX12 pipeline cache requires a Dx12PipelineLayout.",
                    nameof(pipelineLayout));
            if (dx12Layout.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Layout.GetType().FullName);
            }
            if (!ReferenceEquals(dx12Layout.Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    "DX12 pipeline cache cannot create a pipeline from a different device's layout.",
                    nameof(pipelineLayout));
            }
        }
    }
#pragma warning restore CA1416
}
