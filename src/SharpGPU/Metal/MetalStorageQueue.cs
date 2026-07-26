using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalStorageQueue : RHIStorageQueue
    {
        internal MetalDevice MetalDevice => m_MetalDevice;

        private readonly MetalDevice m_MetalDevice;
        private MTLIOCommandQueue m_NativeQueue;
        private MTLIOCommandBuffer m_CurrentCommandBuffer;
        private MTLIOCommandBuffer m_LastSubmittedCommandBuffer;

        private readonly Dictionary<nint, MTLIOFileHandle> m_FileHandlesByKey = new();
        private readonly Dictionary<nint, string> m_FilePathByKey = new();
        private long m_NextFileHandle = 1;

        private static readonly Selector s_NewIOCommandQueueSelector =
            "newIOCommandQueueWithDescriptor:error:";
        private static readonly Selector s_NewIOFileHandleSelector =
            "newIOFileHandleWithURL:error:";
        private static readonly Selector s_RespondsToSelector = "respondsToSelector:";

        public MetalStorageQueue(MetalDevice device)
        {
            m_MetalDevice = device ?? throw new ArgumentNullException(nameof(device));
            InitializeNativeQueue();
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(absPath))
            {
                throw new ArgumentException("A non-empty absolute file path is required.", nameof(absPath));
            }

            if (!Path.IsPathFullyQualified(absPath))
            {
                throw new ArgumentException("Metal IO file paths must be absolute.", nameof(absPath));
            }

            NSURL url = NSURL.FileURLWithPath(new NSString(absPath));
            NSError error = default;
            try
            {
                IntPtr nativeHandle = m_MetalDevice.NativeDevice.NewIOFileHandle(url, ref error);
                if (nativeHandle == IntPtr.Zero)
                {
                    throw CreateNativeFailure(
                        ERHIErrorCode.NativeFailure,
                        error,
                        "Metal IO failed to open the requested source file.");
                }

                nint handleKey = (nint)Interlocked.Increment(ref m_NextFileHandle);
                MTLIOFileHandle fileHandle = new MTLIOFileHandle(nativeHandle);
                m_FileHandlesByKey.Add(handleKey, fileHandle);
                m_FilePathByKey.Add(handleKey, absPath);
                return new RHIStorageFileHandle { NativeHandle = handleKey };
            }
            finally
            {
                if (url.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(url);
                }
            }
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            ThrowIfDisposed();
            nint handleKey = (nint)fileHandle.NativeHandle;
            if (!m_FileHandlesByKey.Remove(handleKey, out MTLIOFileHandle nativeFileHandle))
            {
                throw new ArgumentException(
                    "The Metal IO file handle is not owned by this queue.",
                    nameof(fileHandle));
            }

            if (nativeFileHandle.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(nativeFileHandle);
            }

            m_FilePathByKey.Remove(handleKey);
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            ThrowIfDisposed();
            if (!m_FilePathByKey.TryGetValue((nint)fileHandle.NativeHandle, out string? absPath))
            {
                throw new ArgumentException(
                    "The Metal IO file handle is not owned by this queue.",
                    nameof(fileHandle));
            }

            FileInfo fileInfo = new FileInfo(absPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(
                    "The Metal IO source file no longer exists.",
                    absPath);
            }

            return checked((ulong)fileInfo.Length);
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            ThrowIfDisposed();
            EnqueueBufferLoad(in request);
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            ThrowIfDisposed();
            EnqueueTextureLoad(in request);
        }

        public override void Submit(RHIFence signalFence)
        {
            ThrowIfDisposed();
            if (signalFence is not MetalFence metalFence)
            {
                throw new ArgumentException(
                    "Metal IO submission requires a Metal completion fence.",
                    nameof(signalFence));
            }

            if (!ReferenceEquals(metalFence.OwnerDevice, m_MetalDevice))
            {
                throw new ArgumentException(
                    "The Metal IO completion fence belongs to a different Metal device.",
                    nameof(signalFence));
            }

            metalFence.ReserveSignal();
            try
            {
                ulong signalValue = metalFence.PrepareSignalValue();
                if (m_CurrentCommandBuffer.NativePtr != IntPtr.Zero)
                {
                    m_CurrentCommandBuffer.SignalEvent(metalFence.NativeEvent, signalValue);
                    m_CurrentCommandBuffer.Enqueue();
                    m_LastSubmittedCommandBuffer = m_CurrentCommandBuffer;
                    m_CurrentCommandBuffer = default;
                }
                else
                {
                    MTLSharedEvent nativeEvent = metalFence.NativeEvent;
                    nativeEvent.SignaledValue = signalValue;
                }
            }
            catch (Exception exception)
            {
                metalFence.RollbackSignal();
                throw WrapNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception,
                    "Metal IO failed to submit the native file queue.");
            }
        }

        public override void ThrowIfSubmissionFailed()
        {
            ThrowIfDisposed();
            if (m_LastSubmittedCommandBuffer.NativePtr == IntPtr.Zero)
            {
                return;
            }

            MTLIOStatus status = m_LastSubmittedCommandBuffer.Status;
            if (status == MTLIOStatus.Complete)
            {
                return;
            }

            if (status == MTLIOStatus.Pending)
            {
                return;
            }

            NSError error = m_LastSubmittedCommandBuffer.Error;
            throw CreateNativeFailure(
                ERHIErrorCode.SubmissionFailed,
                error,
                status == MTLIOStatus.Cancelled
                    ? "Metal IO cancelled the native file queue submission."
                    : "Metal IO reported a failed native file queue submission.");
        }

        protected override void Release()
        {
            foreach (MTLIOFileHandle fileHandle in m_FileHandlesByKey.Values)
            {
                if (fileHandle.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(fileHandle);
                }
            }

            m_FileHandlesByKey.Clear();
            m_FilePathByKey.Clear();

            if (m_CurrentCommandBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_CurrentCommandBuffer);
                m_CurrentCommandBuffer = default;
            }

            if (m_LastSubmittedCommandBuffer.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_LastSubmittedCommandBuffer);
                m_LastSubmittedCommandBuffer = default;
            }

            if (m_NativeQueue.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeQueue);
                m_NativeQueue = default;
            }
        }

        internal static bool TryProbeNativeSupport(MetalDevice device, out string reason)
        {
            ArgumentNullException.ThrowIfNull(device);

            if (!DeviceSupportsMetalIo(device))
            {
                reason =
                    "The active MTLDevice runtime does not expose newIOCommandQueueWithDescriptor:error:.";
                return false;
            }

            MTLIOCommandQueueDescriptor descriptor = default;
            MTLIOCommandQueue queue = default;
            try
            {
                CreateNativeQueueResources(device, out descriptor, out queue);
                reason =
                    "A native MTLIOCommandQueue was created for the current Metal device " +
                    "using the serial file-I/O strategy.";
                return true;
            }
            catch (Exception exception)
            {
                reason =
                    $"Native Metal IO file-queue probing failed " +
                    $"({exception.GetType().Name}, HRESULT 0x{exception.HResult:X8}).";
                return false;
            }
            finally
            {
                if (queue.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(queue);
                }

                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor);
                }
            }
        }

        private static bool DeviceSupportsMetalIo(MetalDevice device)
        {
            try
            {
                return ObjectiveCRuntime.bool_objc_msgSend(
                    device.NativeDevice.NativePtr,
                    s_RespondsToSelector,
                    s_NewIOCommandQueueSelector) &&
                    ObjectiveCRuntime.bool_objc_msgSend(
                        device.NativeDevice.NativePtr,
                        s_RespondsToSelector,
                        s_NewIOFileHandleSelector);
            }
            catch
            {
                return false;
            }
        }

        private void InitializeNativeQueue()
        {
            MTLIOCommandQueueDescriptor descriptor = default;
            MTLIOCommandQueue queue = default;
            try
            {
                CreateNativeQueueResources(m_MetalDevice, out descriptor, out queue);
                m_NativeQueue = queue;
                queue = default;
            }
            catch (NotSupportedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw WrapNativeFailure(
                    ERHIErrorCode.InitializationFailed,
                    exception,
                    "Metal StorageQueue could not initialize a native MTLIOCommandQueue.");
            }
            finally
            {
                if (queue.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(queue);
                }

                if (descriptor.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(descriptor);
                }
            }
        }

        private static void CreateNativeQueueResources(
            MetalDevice device,
            out MTLIOCommandQueueDescriptor descriptor,
            out MTLIOCommandQueue queue)
        {
            if (!DeviceSupportsMetalIo(device))
            {
                throw new NotSupportedException(
                    "Metal StorageQueue requires MTLIOCommandQueue support on the active device.");
            }

            descriptor = MTLIOCommandQueueDescriptor.New();
            descriptor.Type = MTLIOCommandQueueType.Serial;
            descriptor.Priority = MTLIOPriority.Normal;
            descriptor.MaxCommandsInFlight = 256;
            descriptor.MaxCommandBufferCount = 64;

            NSError error = default;
            IntPtr queuePtr = device.NativeDevice.NewIOCommandQueue(descriptor.NativePtr, ref error);
            if (queuePtr == IntPtr.Zero)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.InitializationFailed,
                    error,
                    "Metal StorageQueue could not create a native MTLIOCommandQueue.");
            }

            queue = new MTLIOCommandQueue(queuePtr);
        }

        private void EnqueueBufferLoad(in RHIStorageBufferRequest request)
        {
            ulong fileSize = ValidateFileSize(request.FileSize, nameof(request));
            MTLIOFileHandle storageFile = GetStorageFile(request.FileHandle, nameof(request));

            if (request.DestinationBuffer is not MetalBuffer metalBuffer ||
                !ReferenceEquals(metalBuffer.MetalDevice, m_MetalDevice))
            {
                throw new ArgumentException(
                    "The destination buffer must be owned by this Metal device.",
                    nameof(request));
            }

            if (metalBuffer.IsDisposed)
            {
                throw new ObjectDisposedException(metalBuffer.GetType().FullName);
            }

            if (metalBuffer.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new ArgumentException(
                    "Native Metal IO buffer requests require a GPU-local destination.",
                    nameof(request));
            }

            if ((metalBuffer.Descriptor.UsageFlag & ERHIBufferUsage.CopyDst) == 0)
            {
                throw new ArgumentException(
                    "Native Metal IO buffer destinations must declare CopyDst usage.",
                    nameof(request));
            }

            ulong destinationEnd;
            try
            {
                destinationEnd = checked(request.DestinationOffset + request.FileSize);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.DestinationOffset,
                    "The destination range overflows UInt64.");
            }

            if (destinationEnd > checked((ulong)metalBuffer.Descriptor.ByteSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    destinationEnd,
                    "The destination range exceeds the buffer size.");
            }

            ValidateSourceRange(in request.FileHandle, request.FileOffset, request.FileSize, nameof(request));

            MTLIOCommandBuffer commandBuffer = EnsureCommandBuffer();
            try
            {
                commandBuffer.LoadBuffer(
                    metalBuffer.NativeBuffer,
                    request.DestinationOffset,
                    fileSize,
                    storageFile.NativePtr,
                    request.FileOffset);
            }
            catch (Exception exception)
            {
                throw WrapNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception,
                    "Metal IO failed to enqueue a native buffer request.");
            }
        }

        private void EnqueueTextureLoad(in RHIStorageTextureRequest request)
        {
            ulong fileSize = ValidateFileSize(request.FileSize, nameof(request));
            MTLIOFileHandle storageFile = GetStorageFile(request.FileHandle, nameof(request));

            if (request.DestinationTexture is not MetalTexture metalTexture ||
                !ReferenceEquals(metalTexture.MetalDevice, m_MetalDevice))
            {
                throw new ArgumentException(
                    "The destination texture must be owned by this Metal device.",
                    nameof(request));
            }

            if (metalTexture.IsDisposed)
            {
                throw new ObjectDisposedException(metalTexture.GetType().FullName);
            }

            if (metalTexture.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new ArgumentException(
                    "Native Metal IO texture requests require a GPU-local destination.",
                    nameof(request));
            }

            if ((metalTexture.Descriptor.UsageFlag & ERHITextureUsage.CopyDst) == 0)
            {
                throw new ArgumentException(
                    "Native Metal IO texture destinations must declare CopyDst usage.",
                    nameof(request));
            }

            if (request.MipLevel >= metalTexture.Descriptor.MipCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.MipLevel,
                    "The mip level is outside the destination texture.");
            }

            uint arrayLayerCount;
            bool isVolume;
            switch (metalTexture.Descriptor.Dimension)
            {
                case ERHITextureDimension.Texture2D:
                    arrayLayerCount = 1;
                    isVolume = false;
                    break;

                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    arrayLayerCount = metalTexture.Descriptor.Extent.z;
                    isVolume = false;
                    break;

                case ERHITextureDimension.Texture3D:
                    arrayLayerCount = 1;
                    isVolume = true;
                    break;

                case ERHITextureDimension.Texture2DMS:
                case ERHITextureDimension.Texture2DArrayMS:
                    throw new NotSupportedException(
                        "Native Metal IO texture requests do not target multisampled resources.");

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        metalTexture.Descriptor.Dimension,
                        "The destination texture dimension is invalid.");
            }

            if (request.ArraySlice >= arrayLayerCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.ArraySlice,
                    "The array slice is outside the destination texture.");
            }

            ValidateSourceRange(in request.FileHandle, request.FileOffset, request.FileSize, nameof(request));

            uint mipLevel = request.MipLevel;
            uint width = Math.Max(1u, metalTexture.Descriptor.Extent.x >> (int)mipLevel);
            uint height = Math.Max(1u, metalTexture.Descriptor.Extent.y >> (int)mipLevel);
            uint depth = isVolume
                ? Math.Max(1u, metalTexture.Descriptor.Extent.z >> (int)mipLevel)
                : 1u;

            ulong conditionedSourceSize = ComputeConditionedTextureSourceSize(
                m_MetalDevice,
                metalTexture.Descriptor.Format,
                width,
                height,
                depth);
            if (request.FileSize != conditionedSourceSize)
            {
                throw new ArgumentException(
                    $"Native Metal IO texture source data must use the exact linear layout " +
                    $"({conditionedSourceSize} bytes for mip {mipLevel}); received {request.FileSize} bytes.",
                    nameof(request));
            }

            MTLPixelFormat nativeFormat =
                MetalUtility.ConvertToMetalPixelFormat(metalTexture.Descriptor.Format);
            ulong rowPitch = ComputeAlignedRowPitch(
                m_MetalDevice.NativeDevice,
                nativeFormat,
                width);
            ulong sourceBytesPerImage = isVolume
                ? rowPitch * depth
                : rowPitch * height;

            MTLIOCommandBuffer commandBuffer = EnsureCommandBuffer();
            try
            {
                commandBuffer.LoadTexture(
                    metalTexture.NativeTexture,
                    request.ArraySlice,
                    mipLevel,
                    new MTLSize(width, height, depth),
                    rowPitch,
                    sourceBytesPerImage,
                    new MTLOrigin(0, 0, 0),
                    storageFile.NativePtr,
                    request.FileOffset);
            }
            catch (Exception exception)
            {
                throw WrapNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception,
                    "Metal IO failed to enqueue a native texture request.");
            }
        }

        private MTLIOCommandBuffer EnsureCommandBuffer()
        {
            if (m_CurrentCommandBuffer.NativePtr != IntPtr.Zero)
            {
                return m_CurrentCommandBuffer;
            }

            if (m_NativeQueue.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("The Metal IO queue is not initialized.");
            }

            m_CurrentCommandBuffer = m_NativeQueue.CommandBuffer;
            if (m_CurrentCommandBuffer.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Metal IO failed to allocate a command buffer.");
            }

            return m_CurrentCommandBuffer;
        }

        private MTLIOFileHandle GetStorageFile(
            in RHIStorageFileHandle fileHandle,
            string parameterName)
        {
            if (!m_FileHandlesByKey.TryGetValue(
                    (nint)fileHandle.NativeHandle,
                    out MTLIOFileHandle storageFile))
            {
                throw new ArgumentException(
                    "The Metal IO file handle is not owned by this queue.",
                    parameterName);
            }

            return storageFile;
        }

        private static uint ValidateFileSize(ulong fileSize, string parameterName)
        {
            if (fileSize == 0 || fileSize > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    fileSize,
                    "Native Metal IO requests must contain between 1 and UInt32.MaxValue bytes.");
            }

            return (uint)fileSize;
        }

        private void ValidateSourceRange(
            in RHIStorageFileHandle fileHandle,
            ulong fileOffset,
            ulong fileSize,
            string parameterName)
        {
            ulong sourceEnd;
            try
            {
                sourceEnd = checked(fileOffset + fileSize);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    fileOffset,
                    "The source range overflows UInt64.");
            }

            if (sourceEnd > QueryFileSize(fileHandle))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    sourceEnd,
                    "The source range exceeds the Metal IO file size.");
            }
        }

        private static ulong ComputeAlignedRowPitch(
            MTLDevice device,
            MTLPixelFormat format,
            uint width)
        {
            uint bytesPerPixel = GetFormatBytesPerPixel(format);
            if (bytesPerPixel == 0)
            {
                throw new NotSupportedException(
                    "Native Metal IO texture requests do not support block-compressed formats.");
            }

            ulong tightRowBytes = checked((ulong)width * bytesPerPixel);
            ulong alignment = device.MinimumLinearTextureAlignmentForPixelFormat(format);
            if (alignment <= 1)
            {
                return tightRowBytes;
            }

            return ((tightRowBytes + alignment - 1) / alignment) * alignment;
        }

        private static ulong ComputeConditionedTextureSourceSize(
            MetalDevice device,
            ERHIPixelFormat format,
            uint width,
            uint height,
            uint depth)
        {
            MTLPixelFormat nativeFormat = MetalUtility.ConvertToMetalPixelFormat(format);
            ulong rowPitch = ComputeAlignedRowPitch(device.NativeDevice, nativeFormat, width);
            ulong tightRowBytes = checked((ulong)width * GetFormatBytesPerPixel(nativeFormat));
            if (depth > 1)
            {
                return checked(rowPitch * (depth - 1) + tightRowBytes);
            }

            return checked(rowPitch * (height - 1) + tightRowBytes);
        }

        private static uint GetFormatBytesPerPixel(MTLPixelFormat format)
        {
            switch (format)
            {
                case MTLPixelFormat.R8Unorm:
                case MTLPixelFormat.R8Snorm:
                case MTLPixelFormat.R8Uint:
                case MTLPixelFormat.R8Sint:
                    return 1;

                case MTLPixelFormat.R16Uint:
                case MTLPixelFormat.R16Sint:
                case MTLPixelFormat.R16Float:
                case MTLPixelFormat.RG8Unorm:
                case MTLPixelFormat.RG8Snorm:
                case MTLPixelFormat.RG8Uint:
                case MTLPixelFormat.RG8Sint:
                case MTLPixelFormat.Depth16Unorm:
                    return 2;

                case MTLPixelFormat.R32Uint:
                case MTLPixelFormat.R32Sint:
                case MTLPixelFormat.R32Float:
                case MTLPixelFormat.RG16Uint:
                case MTLPixelFormat.RG16Sint:
                case MTLPixelFormat.RG16Float:
                case MTLPixelFormat.RGBA8Uint:
                case MTLPixelFormat.RGBA8Sint:
                case MTLPixelFormat.RGBA8Unorm:
                case MTLPixelFormat.RGBA8UnormsRGB:
                case MTLPixelFormat.RGBA8Snorm:
                case MTLPixelFormat.BGRA8Unorm:
                case MTLPixelFormat.BGRA8UnormsRGB:
                case MTLPixelFormat.RGB10A2Uint:
                case MTLPixelFormat.RGB10A2Unorm:
                case MTLPixelFormat.RG11B10Float:
                case MTLPixelFormat.Depth32Float:
                case MTLPixelFormat.Depth24UnormStencil8:
                    return 4;

                case MTLPixelFormat.RG32Uint:
                case MTLPixelFormat.RG32Sint:
                case MTLPixelFormat.RG32Float:
                case MTLPixelFormat.RGBA16Uint:
                case MTLPixelFormat.RGBA16Sint:
                case MTLPixelFormat.RGBA16Float:
                case MTLPixelFormat.Depth32FloatStencil8:
                    return 8;

                case MTLPixelFormat.RGBA32Uint:
                case MTLPixelFormat.RGBA32Sint:
                case MTLPixelFormat.RGBA32Float:
                    return 16;

                default:
                    return 0;
            }
        }

        private static RHIException CreateNativeFailure(
            ERHIErrorCode errorCode,
            in NSError error,
            string nativeMessage)
        {
            long nativeCode = error.NativePtr != IntPtr.Zero ? error.Code : 0;
            if (error.NativePtr != IntPtr.Zero)
            {
                try
                {
                    string localized = error.LocalizedDescription.ToString();
                    if (!string.IsNullOrWhiteSpace(localized))
                    {
                        nativeMessage = $"{nativeMessage} {localized}";
                    }
                }
                catch
                {
                    // Keep the caller-facing message when NSError text is unavailable.
                }
            }

            return new RHIException(
                errorCode,
                ERHIBackend.Metal,
                nativeCode,
                nativeMessage,
                ERHIDeviceState.Operational);
        }

        private RHIException WrapNativeFailure(
            ERHIErrorCode errorCode,
            Exception exception,
            string nativeMessage)
        {
            return new RHIException(
                errorCode,
                ERHIBackend.Metal,
                exception.HResult,
                nativeMessage,
                ERHIDeviceState.Operational,
                exception);
        }
    }
}
