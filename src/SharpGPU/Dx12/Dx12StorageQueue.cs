using System;
using System.IO;
using System.Threading;
using Vortice.Mathematics;
using Vortice.DirectStorage;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal unsafe class Dx12StorageQueue : RHIStorageQueue
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }

        private readonly Dx12Device m_Dx12Device;
        private IDStorageFactory? m_DStorageFactory;
        private IDStorageQueue? m_DStorageQueue;
        private Vortice.Direct3D12.ID3D12Device? m_DStorageDevice;

        private static readonly object s_DirectStorageResolverGate = new();
        private static bool s_DirectStorageResolverRegistered;
        private static bool s_DirectStorageRuntimeConfigured;
        private static nint s_DirectStorageCoreHandle;

        private readonly Dictionary<nint, IDStorageFile> m_DStorageFilesByHandle = new();
        private readonly Dictionary<nint, string> m_FilePathByHandle = new();
        private long m_NextFileHandle = 1;

        private const string DirectStorageRuntimeCore = "dstoragecore.dll";
        private const string DirectStorageRuntime = "dstorage.dll";

        public Dx12StorageQueue(Dx12Device device)
        {
            m_Dx12Device = device ?? throw new ArgumentNullException(nameof(device));

            InitializeDirectStorageQueue();
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
                throw new ArgumentException("DirectStorage file paths must be absolute.", nameof(absPath));
            }

            IDStorageFactory factory = m_DStorageFactory
                ?? throw new InvalidOperationException("The DirectStorage factory is not initialized.");
            try
            {
                string nativePath = Path.GetFullPath(absPath);
                if (nativePath.Length >= 260 && !nativePath.StartsWith(@"\\?\", StringComparison.Ordinal))
                {
                    nativePath = nativePath.StartsWith(@"\\", StringComparison.Ordinal)
                        ? @"\\?\UNC\" + nativePath.Substring(2)
                        : @"\\?\" + nativePath;
                }
                IDStorageFile storageFile = factory.OpenFile<IDStorageFile>(nativePath);
                nint handle = (nint)Interlocked.Increment(ref m_NextFileHandle);
                m_DStorageFilesByHandle.Add(handle, storageFile);
                m_FilePathByHandle.Add(handle, absPath);
                return new RHIStorageFileHandle { NativeHandle = handle };
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.NativeFailure,
                    exception.HResult,
                    "DirectStorage failed to open the requested source file.",
                    exception);
            }
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            ThrowIfDisposed();
            nint handleKey = (nint)fileHandle.NativeHandle;
            if (!m_DStorageFilesByHandle.Remove(handleKey, out IDStorageFile? storageFile))
            {
                throw new ArgumentException(
                    "The DirectStorage file handle is not owned by this queue.",
                    nameof(fileHandle));
            }

            storageFile.Dispose();
            m_FilePathByHandle.Remove(handleKey);
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            ThrowIfDisposed();
            if (!m_FilePathByHandle.TryGetValue((nint)fileHandle.NativeHandle, out string? absPath))
            {
                throw new ArgumentException(
                    "The DirectStorage file handle is not owned by this queue.",
                    nameof(fileHandle));
            }

            FileInfo fileInfo = new FileInfo(absPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException(
                    "The DirectStorage source file no longer exists.",
                    absPath);
            }

            return checked((ulong)fileInfo.Length);
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            ThrowIfDisposed();
            EnqueueDirectStorageBuffer(in request);
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            ThrowIfDisposed();
            EnqueueDirectStorageTexture(in request);
        }

        public override void Submit(RHIFence signalFence)
        {
            ThrowIfDisposed();
            IDStorageQueue queue = m_DStorageQueue
                ?? throw new InvalidOperationException("The DirectStorage queue is not initialized.");
            if (signalFence is not Dx12Fence dx12Fence)
            {
                throw new ArgumentException(
                    "DirectStorage submission requires a DX12 completion fence.",
                    nameof(signalFence));
            }
            if (!ReferenceEquals(dx12Fence.OwnerDevice, m_Dx12Device))
            {
                throw new ArgumentException(
                    "The DirectStorage completion fence belongs to a different DX12 device.",
                    nameof(signalFence));
            }

            dx12Fence.ReserveSignal();
            try
            {
                ulong signalValue = dx12Fence.PrepareSignalValue();
                queue.EnqueueSignal(dx12Fence.NativeFence, signalValue);
                queue.Submit();
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                dx12Fence.RollbackSignal();
                throw CreateNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception.HResult,
                    "DirectStorage failed to submit the native file queue.",
                    exception);
            }
            catch
            {
                dx12Fence.RollbackSignal();
                throw;
            }
        }

        public override void CancelRequestsWithTag(ulong mask, ulong value)
        {
            ThrowIfDisposed();
            m_Dx12Device.Capabilities.Storage.RequestCancellation.Require(
                "DirectStorage request cancellation");
            IDStorageQueue queue = m_DStorageQueue
                ?? throw new InvalidOperationException("The DirectStorage queue is not initialized.");
            try
            {
                queue.CancelRequestsWithTag(mask, value);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.NativeFailure,
                    exception.HResult,
                    "DirectStorage failed to cancel requests by CancellationTag.",
                    exception);
            }
        }

        public override void CancelPending()
        {
            CancelRequestsWithTag(0, 0);
        }

        public override void ThrowIfSubmissionFailed()
        {
            ThrowIfDisposed();
            IDStorageQueue queue = m_DStorageQueue
                ?? throw new InvalidOperationException("The DirectStorage queue is not initialized.");
            ErrorRecord errorRecord;
            try
            {
                errorRecord = queue.RetrieveErrorRecord();
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.NativeFailure,
                    exception.HResult,
                    "DirectStorage failed to retrieve the native queue error record.",
                    exception);
            }
            if (errorRecord.FailureCount == 0 ||
                !errorRecord.FirstFailure.HResult.Failure)
            {
                return;
            }

            int nativeCode = errorRecord.FirstFailure.HResult.Code;
            throw CreateNativeFailure(
                ERHIErrorCode.SubmissionFailed,
                nativeCode,
                $"DirectStorage reported {errorRecord.FailureCount} failed command(s); " +
                $"first command type was {errorRecord.FirstFailure.CommandType}.");
        }

        protected override void Release()
        {
            foreach (IDStorageFile storageFile in m_DStorageFilesByHandle.Values)
            {
                storageFile.Dispose();
            }
            m_DStorageFilesByHandle.Clear();
            m_FilePathByHandle.Clear();

            m_DStorageQueue?.Dispose();
            m_DStorageQueue = null;

            m_DStorageFactory?.Dispose();
            m_DStorageFactory = null;

            m_DStorageDevice?.Dispose();
            m_DStorageDevice = null;
        }

        internal static RHIStorageCapabilities CreateCapabilities(Dx12Device device)
        {
            ArgumentNullException.ThrowIfNull(device);

            ProbeNativeFacets(device, out Dx12StorageFacetProbe probe);
            RHICapabilityLimits nativeIoLimits = probe.NativeGpuFileIo
                ? new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.MaximumStorageRequestBytes,
                        probe.MaxRequestBytes),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.MaximumStorageConcurrentRequests,
                        probe.MaxConcurrentRequests))
                : RHICapabilityLimits.Empty;
            RHICapabilityLimits decompressionLimits = probe.GpuDecompression
                ? new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.SupportedStorageCompressionFormatMask,
                        (ulong)probe.CompressionFormatMask))
                : RHICapabilityLimits.Empty;

            return new RHIStorageCapabilities(
                nativeGpuFileIo: RHICapability.FromProbe(
                    probe.NativeGpuFileIo,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeLibrary,
                    ERHICapabilityProbeKind.RuntimeObjectProbe,
                    "DirectStorage native factory and file-queue probe",
                    probe.NativeGpuFileIoReason,
                    nativeIoLimits),
                gpuDecompression: RHICapability.FromProbe(
                    probe.GpuDecompression,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeLibrary,
                    ERHICapabilityProbeKind.RuntimeObjectProbe,
                    "IDStorageQueue2.GetCompressionSupport(GDeflate)",
                    probe.GpuDecompressionReason,
                    decompressionLimits),
                requestCancellation: RHICapability.FromProbe(
                    probe.RequestCancellation,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeLibrary,
                    ERHICapabilityProbeKind.RuntimeObjectProbe,
                    "IDStorageQueue.CancelRequestsWithTag",
                    probe.RequestCancellationReason),
                ioPriority: RHICapability.FromProbe(
                    probe.IoPriority,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeLibrary,
                    ERHICapabilityProbeKind.RuntimeObjectProbe,
                    "DSTORAGE_QUEUE_DESC.Priority",
                    probe.IoPriorityReason));
        }

        internal static bool TryProbeNativeSupport(Dx12Device device, out string reason)
        {
            ProbeNativeFacets(device, out Dx12StorageFacetProbe probe);
            reason = probe.NativeGpuFileIoReason;
            return probe.NativeGpuFileIo;
        }

        private static void ProbeNativeFacets(Dx12Device device, out Dx12StorageFacetProbe probe)
        {
            ArgumentNullException.ThrowIfNull(device);

            IDStorageFactory? factory = null;
            IDStorageQueue? queue = null;
            IDStorageQueue2? queue2 = null;
            Vortice.Direct3D12.ID3D12Device? storageDevice = null;
            try
            {
                CreateNativeQueueResources(
                    device,
                    out factory,
                    out queue,
                    out storageDevice);

                queue2 = queue.QueryInterfaceOrNull<IDStorageQueue2>();
                bool gpuDecompression = false;
                ERHIStorageCompressionFormat compressionMask = ERHIStorageCompressionFormat.None;
                string gpuDecompressionReason;
                if (queue2 == null)
                {
                    gpuDecompressionReason =
                        "IDStorageQueue2.GetCompressionSupport is not available on this DirectStorage queue.";
                }
                else
                {
                    CompressionSupport support = queue2.GetCompressionSupport(CompressionFormat.GDeflate);
                    bool gpuPath =
                        support.HasFlag(CompressionSupport.GpuOptimized) ||
                        support.HasFlag(CompressionSupport.GpuFallback);
                    if (gpuPath)
                    {
                        gpuDecompression = true;
                        compressionMask = ERHIStorageCompressionFormat.GDeflate;
                        gpuDecompressionReason =
                            "IDStorageQueue2.GetCompressionSupport(GDeflate) reported a GPU decompression path.";
                    }
                    else if (support.HasFlag(CompressionSupport.CpuFallback))
                    {
                        gpuDecompressionReason =
                            "IDStorageQueue2.GetCompressionSupport(GDeflate) reported CPU fallback only; " +
                            "GPU decompression is not available.";
                    }
                    else
                    {
                        gpuDecompressionReason =
                            $"IDStorageQueue2.GetCompressionSupport(GDeflate) returned {support}.";
                    }
                }

                probe = new Dx12StorageFacetProbe(
                    nativeGpuFileIo: true,
                    nativeGpuFileIoReason:
                    "A native Microsoft DirectStorage file queue was created for the current DX12 device " +
                    "using the explicitly configured file-buffered Tier-1 strategy.",
                    gpuDecompression: gpuDecompression,
                    gpuDecompressionReason: gpuDecompressionReason,
                    compressionFormatMask: compressionMask,
                    requestCancellation: true,
                    requestCancellationReason:
                    "IDStorageQueue.CancelRequestsWithTag is present on the created DirectStorage queue.",
                    ioPriority: true,
                    ioPriorityReason:
                    "DSTORAGE_QUEUE_DESC.Priority is set when the DirectStorage file queue is created.",
                    maxRequestBytes: uint.MaxValue,
                    maxConcurrentRequests: (ulong)(DirectStorage.MaxQueueCapacity - 1));
            }
            catch (Exception exception)
            {
                string failure =
                    $"Native Microsoft DirectStorage file-queue probing failed " +
                    $"({exception.GetType().Name}, HRESULT 0x{exception.HResult:X8}).";
                probe = new Dx12StorageFacetProbe(
                    nativeGpuFileIo: false,
                    nativeGpuFileIoReason: failure,
                    gpuDecompression: false,
                    gpuDecompressionReason: failure,
                    compressionFormatMask: ERHIStorageCompressionFormat.None,
                    requestCancellation: false,
                    requestCancellationReason: failure,
                    ioPriority: false,
                    ioPriorityReason: failure,
                    maxRequestBytes: 0,
                    maxConcurrentRequests: 0);
            }
            finally
            {
                queue2?.Dispose();
                queue?.Dispose();
                factory?.Dispose();
                storageDevice?.Dispose();
            }
        }

        private readonly struct Dx12StorageFacetProbe
        {
            public bool NativeGpuFileIo { get; }
            public string NativeGpuFileIoReason { get; }
            public bool GpuDecompression { get; }
            public string GpuDecompressionReason { get; }
            public ERHIStorageCompressionFormat CompressionFormatMask { get; }
            public bool RequestCancellation { get; }
            public string RequestCancellationReason { get; }
            public bool IoPriority { get; }
            public string IoPriorityReason { get; }
            public ulong MaxRequestBytes { get; }
            public ulong MaxConcurrentRequests { get; }

            public Dx12StorageFacetProbe(
                bool nativeGpuFileIo,
                string nativeGpuFileIoReason,
                bool gpuDecompression,
                string gpuDecompressionReason,
                ERHIStorageCompressionFormat compressionFormatMask,
                bool requestCancellation,
                string requestCancellationReason,
                bool ioPriority,
                string ioPriorityReason,
                ulong maxRequestBytes,
                ulong maxConcurrentRequests)
            {
                NativeGpuFileIo = nativeGpuFileIo;
                NativeGpuFileIoReason = nativeGpuFileIoReason;
                GpuDecompression = gpuDecompression;
                GpuDecompressionReason = gpuDecompressionReason;
                CompressionFormatMask = compressionFormatMask;
                RequestCancellation = requestCancellation;
                RequestCancellationReason = requestCancellationReason;
                IoPriority = ioPriority;
                IoPriorityReason = ioPriorityReason;
                MaxRequestBytes = maxRequestBytes;
                MaxConcurrentRequests = maxConcurrentRequests;
            }
        }

        private static bool ProbeDirectStorageRuntime()
        {
            EnsureDirectStorageResolverRegistered();

            string? resolvedPath = null;

            if (TryProbeRuntimeLibrary(DirectStorageRuntimeCore, out resolvedPath, out nint coreHandle))
            {
                NativeLibrary.Free(coreHandle);
                System.Diagnostics.Debug.WriteLine($"[Dx12StorageQueue] DirectStorage runtime found (via ThirdParty resolver): '{resolvedPath ?? DirectStorageRuntimeCore}'");
                return true;
            }

            if (TryProbeRuntimeLibrary(DirectStorageRuntime, out resolvedPath, out nint runtimeHandle))
            {
                NativeLibrary.Free(runtimeHandle);
                System.Diagnostics.Debug.WriteLine($"[Dx12StorageQueue] DirectStorage runtime found (via ThirdParty resolver): '{resolvedPath ?? DirectStorageRuntime}'");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"[Dx12StorageQueue] DirectStorage runtime not found via ThirdParty resolver.");
            return false;
        }

        private static bool TryProbeRuntimeLibrary(string libraryName, out string? resolvedPath, out nint handle)
        {
            if (ThirdPartyNativeLibraryResolver.TryResolve(libraryName, out handle, out resolvedPath))
            {
                return true;
            }

            resolvedPath = null;
            return false;
        }

        private static void EnsureDirectStorageResolverRegistered()
        {
            lock (s_DirectStorageResolverGate)
            {
                if (s_DirectStorageResolverRegistered &&
                    s_DirectStorageRuntimeConfigured)
                {
                    return;
                }

                if (!s_DirectStorageResolverRegistered)
                {
                    // Vortice.DirectStorage owns the DllImportResolver for its assembly.
                    // Subscribe to its supported resolution hook instead of trying to replace it.
                    DirectStorage.ResolveLibrary += ResolveDirectStorageLibrary;
                    s_DirectStorageResolverRegistered = true;
                }

                if (!s_DirectStorageRuntimeConfigured)
                {
                    // The bundled DirectStorage 1.2.1 runtime was qualified on this host with a
                    // file -> DEFAULT-heap -> readback test. Its default BypassIO path reported
                    // success while leaving the destination zeroed; DisableBypassIO alone behaved
                    // the same. The official file-buffered DirectStorage mode produced the exact
                    // bytes without introducing a SharpGPU CPU read/copy path.
                    Configuration1 configuration = new Configuration1
                    {
                        DisableBypassIO = true,
                        ForceFileBuffering = true,
                    };
                    DirectStorage.DStorageSetConfiguration1(configuration).CheckError();
                    s_DirectStorageRuntimeConfigured = true;
                }
            }
        }

        private static nint ResolveDirectStorageLibrary(
            string libraryName,
            System.Reflection.Assembly assembly,
            DllImportSearchPath? searchPath)
        {
            _ = assembly;
            _ = searchPath;
            if (!string.Equals(
                    libraryName,
                    DirectStorageRuntime,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            lock (s_DirectStorageResolverGate)
            {
                if (s_DirectStorageCoreHandle == 0 &&
                    !ThirdPartyNativeLibraryResolver.TryResolve(
                        DirectStorageRuntimeCore,
                        out s_DirectStorageCoreHandle,
                        out _))
                {
                    return 0;
                }

                return ThirdPartyNativeLibraryResolver.TryResolve(
                    DirectStorageRuntime,
                    out nint runtimeHandle,
                    out _)
                    ? runtimeHandle
                    : 0;
            }
        }

        private void InitializeDirectStorageQueue()
        {
            IDStorageFactory? factory = null;
            IDStorageQueue? queue = null;
            Vortice.Direct3D12.ID3D12Device? storageDevice = null;
            try
            {
                CreateNativeQueueResources(
                    m_Dx12Device,
                    out factory,
                    out queue,
                    out storageDevice);
                m_DStorageFactory = factory;
                m_DStorageQueue = queue;
                m_DStorageDevice = storageDevice;
            }
            catch (NotSupportedException)
            {
                queue?.Dispose();
                factory?.Dispose();
                storageDevice?.Dispose();

                throw;
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                queue?.Dispose();
                factory?.Dispose();
                storageDevice?.Dispose();

                throw CreateNativeFailure(
                    ERHIErrorCode.InitializationFailed,
                    exception.HResult,
                    "DX12 StorageQueue could not initialize a native DirectStorage file queue.",
                    exception);
            }
            catch (Exception exception)
            {
                queue?.Dispose();
                factory?.Dispose();
                storageDevice?.Dispose();

                throw CreateNativeFailure(
                    ERHIErrorCode.InitializationFailed,
                    exception.HResult,
                    "DX12 StorageQueue could not initialize a native DirectStorage file queue.",
                    exception);
            }
        }

        private static void CreateNativeQueueResources(
            Dx12Device device,
            out IDStorageFactory factory,
            out IDStorageQueue queue,
            out Vortice.Direct3D12.ID3D12Device storageDevice)
        {
            EnsureDirectStorageResolverRegistered();
            if (!ProbeDirectStorageRuntime())
            {
                throw new NotSupportedException(
                    "DX12 StorageQueue requires the native Microsoft DirectStorage runtime.");
            }

            factory = DirectStorage.DStorageGetFactory<IDStorageFactory>();

            storageDevice =
                device.NativeDevice.QueryInterface<Vortice.Direct3D12.ID3D12Device>();

            ushort queueCapacity =
                (ushort)Math.Clamp(
                    256,
                    DirectStorage.MinQueueCapacity,
                    DirectStorage.MaxQueueCapacity);
            QueueDesc queueDesc = new QueueDesc
            {
                SourceType = RequestSourceType.File,
                Capacity = queueCapacity,
                Priority = Priority.Normal,
                Name = "SharpGPU.Dx12StorageQueue",
                Device = storageDevice,
            };

            queue = factory.CreateQueue<IDStorageQueue>(queueDesc)
                ?? throw new NotSupportedException(
                    "The DirectStorage runtime did not create a native file queue.");
        }

        private void EnqueueDirectStorageBuffer(in RHIStorageBufferRequest request)
        {
            IDStorageQueue queue = m_DStorageQueue
                ?? throw new InvalidOperationException("The DirectStorage queue is not initialized.");
            uint fileSize = ValidateFileSize(request.FileSize, nameof(request));
            IDStorageFile storageFile = GetStorageFile(request.FileHandle, nameof(request));

            if (request.DestinationBuffer is not Dx12Buffer dx12Buffer ||
                !ReferenceEquals(dx12Buffer.Dx12Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    "The destination buffer must be owned by this DX12 device.",
                    nameof(request));
            }

            if (dx12Buffer.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Buffer.GetType().FullName);
            }

            if (dx12Buffer.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new ArgumentException(
                    "Native DirectStorage buffer requests require a GPU-local destination.",
                    nameof(request));
            }
            if ((dx12Buffer.Descriptor.UsageFlag & ERHIBufferUsage.CopyDst) == 0)
            {
                throw new ArgumentException(
                    "Native DirectStorage buffer destinations must declare CopyDst usage.",
                    nameof(request));
            }

            CompressionFormat compressionFormat = ResolveCompressionFormat(
                request.CompressionFormat,
                request.UncompressedSize,
                nameof(request));
            uint destinationSize = compressionFormat == CompressionFormat.None
                ? fileSize
                : request.UncompressedSize;

            ulong destinationEnd;
            try
            {
                destinationEnd = checked(request.DestinationOffset + destinationSize);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.DestinationOffset,
                    "The destination range overflows UInt64.");
            }

            if (destinationEnd > checked((ulong)dx12Buffer.Descriptor.ByteSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    destinationEnd,
                    "The destination range exceeds the buffer size.");
            }

            ValidateSourceRange(in request.FileHandle, request.FileOffset, request.FileSize, nameof(request));

            Request directStorageRequest = new Request();
            directStorageRequest.Options.SourceType = RequestSourceType.File;
            directStorageRequest.Options.DestinationType = RequestDestinationType.Buffer;
            directStorageRequest.Options.CompressionFormat = compressionFormat;
            directStorageRequest.Source.File.Source = storageFile;
            directStorageRequest.Source.File.Offset = request.FileOffset;
            directStorageRequest.Source.File.Size = fileSize;
            directStorageRequest.UncompressedSize = destinationSize;
            directStorageRequest.Destination.Buffer.Resource = dx12Buffer.NativeResource;
            directStorageRequest.Destination.Buffer.Offset = request.DestinationOffset;
            directStorageRequest.Destination.Buffer.Size = destinationSize;
            directStorageRequest.CancellationTag = request.CancellationTag;
            directStorageRequest.Name = null;

            try
            {
                queue.EnqueueRequest(directStorageRequest);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception.HResult,
                    "DirectStorage failed to enqueue a native buffer request.",
                    exception);
            }
        }

        private void EnqueueDirectStorageTexture(in RHIStorageTextureRequest request)
        {
            IDStorageQueue queue = m_DStorageQueue
                ?? throw new InvalidOperationException("The DirectStorage queue is not initialized.");
            uint fileSize = ValidateFileSize(request.FileSize, nameof(request));
            IDStorageFile storageFile = GetStorageFile(request.FileHandle, nameof(request));

            if (request.DestinationTexture is not Dx12Texture dx12Texture ||
                !ReferenceEquals(dx12Texture.Dx12Device, m_Dx12Device))
            {
                throw new ArgumentException(
                    "The destination texture must be owned by this DX12 device.",
                    nameof(request));
            }

            if (dx12Texture.IsDisposed)
            {
                throw new ObjectDisposedException(dx12Texture.GetType().FullName);
            }

            if (dx12Texture.Descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new ArgumentException(
                    "Native DirectStorage texture requests require a GPU-local destination.",
                    nameof(request));
            }
            if ((dx12Texture.Descriptor.UsageFlag & ERHITextureUsage.CopyDst) == 0)
            {
                throw new ArgumentException(
                    "Native DirectStorage texture destinations must declare CopyDst usage.",
                    nameof(request));
            }

            if (request.MipLevel >= dx12Texture.Descriptor.MipCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.MipLevel,
                    "The mip level is outside the destination texture.");
            }

            uint arrayLayerCount;
            bool isVolume;
            switch (dx12Texture.Descriptor.Dimension)
            {
                case ERHITextureDimension.Texture2D:
                    arrayLayerCount = 1;
                    isVolume = false;
                    break;

                case ERHITextureDimension.Texture2DArray:
                case ERHITextureDimension.TextureCube:
                case ERHITextureDimension.TextureCubeArray:
                    arrayLayerCount = dx12Texture.Descriptor.Extent.z;
                    isVolume = false;
                    break;

                case ERHITextureDimension.Texture3D:
                    arrayLayerCount = 1;
                    isVolume = true;
                    break;

                case ERHITextureDimension.Texture2DMS:
                case ERHITextureDimension.Texture2DArrayMS:
                    throw new NotSupportedException(
                        "Native DirectStorage texture requests do not target multisampled resources.");

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        dx12Texture.Descriptor.Dimension,
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
            uint subresourceIndex = isVolume
                ? mipLevel
                : mipLevel + (request.ArraySlice * dx12Texture.Descriptor.MipCount);
            uint width = Math.Max(1u, dx12Texture.Descriptor.Extent.x >> (int)mipLevel);
            uint height = Math.Max(1u, dx12Texture.Descriptor.Extent.y >> (int)mipLevel);
            uint depth = isVolume
                ? Math.Max(1u, dx12Texture.Descriptor.Extent.z >> (int)mipLevel)
                : 1u;
            m_Dx12Device.NativeDevice.GetCopyableFootprints(
                dx12Texture.NativeResource.Description,
                subresourceIndex,
                1,
                0,
                out ulong conditionedSourceSize);
            CompressionFormat compressionFormat = ResolveCompressionFormat(
                request.CompressionFormat,
                request.UncompressedSize,
                nameof(request));
            if (conditionedSourceSize > uint.MaxValue)
            {
                throw new ArgumentException(
                    $"Native DirectStorage texture source data must use the exact D3D12 " +
                    $"GetCopyableFootprints layout; subresource {subresourceIndex} overflows UInt32.",
                    nameof(request));
            }

            uint uncompressedSize = checked((uint)conditionedSourceSize);
            if (compressionFormat == CompressionFormat.None)
            {
                if (request.FileSize != conditionedSourceSize)
                {
                    throw new ArgumentException(
                        $"Native DirectStorage texture source data must use the exact D3D12 " +
                        $"GetCopyableFootprints layout ({conditionedSourceSize} bytes for subresource " +
                        $"{subresourceIndex}); received {request.FileSize} bytes.",
                        nameof(request));
                }
            }
            else if (request.UncompressedSize != uncompressedSize)
            {
                throw new ArgumentException(
                    $"Compressed DirectStorage texture UncompressedSize must equal the D3D12 " +
                    $"GetCopyableFootprints layout ({conditionedSourceSize} bytes for subresource " +
                    $"{subresourceIndex}); received {request.UncompressedSize} bytes.",
                    nameof(request));
            }

            Request directStorageRequest = new Request();
            directStorageRequest.Options.SourceType = RequestSourceType.File;
            directStorageRequest.Options.DestinationType = RequestDestinationType.TextureRegion;
            directStorageRequest.Options.CompressionFormat = compressionFormat;
            directStorageRequest.Source.File.Source = storageFile;
            directStorageRequest.Source.File.Offset = request.FileOffset;
            directStorageRequest.Source.File.Size = fileSize;
            directStorageRequest.UncompressedSize = uncompressedSize;
            directStorageRequest.Destination.Texture.Resource = dx12Texture.NativeResource;
            directStorageRequest.Destination.Texture.SubresourceIndex = subresourceIndex;
            directStorageRequest.Destination.Texture.Region =
                new Box(0, 0, 0, (int)width, (int)height, (int)depth);
            directStorageRequest.CancellationTag = request.CancellationTag;
            directStorageRequest.Name = null;

            try
            {
                queue.EnqueueRequest(directStorageRequest);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                throw CreateNativeFailure(
                    ERHIErrorCode.SubmissionFailed,
                    exception.HResult,
                    "DirectStorage failed to enqueue a native texture request.",
                    exception);
            }
        }

        private IDStorageFile GetStorageFile(
            in RHIStorageFileHandle fileHandle,
            string parameterName)
        {
            if (!m_DStorageFilesByHandle.TryGetValue(
                    (nint)fileHandle.NativeHandle,
                    out IDStorageFile? storageFile))
            {
                throw new ArgumentException(
                    "The DirectStorage file handle is not owned by this queue.",
                    parameterName);
            }

            return storageFile;
        }

        private CompressionFormat ResolveCompressionFormat(
            ERHIStorageCompressionFormat format,
            uint uncompressedSize,
            string parameterName)
        {
            if (format == ERHIStorageCompressionFormat.None)
            {
                return CompressionFormat.None;
            }

            m_Dx12Device.Capabilities.Storage.GpuDecompression.Require(
                "DirectStorage GPU decompression");
            if (format != ERHIStorageCompressionFormat.GDeflate)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    format,
                    "DirectStorage can enqueue only GDeflate compressed requests.");
            }

            if (!m_Dx12Device.Capabilities.Storage.GpuDecompression.Limits.TryGetValue(
                    ERHICapabilityLimitKind.SupportedStorageCompressionFormatMask,
                    out ulong formatMask) ||
                ((ulong)format & ~formatMask) != 0)
            {
                throw new NotSupportedException(
                    "DirectStorage GPU decompression does not include the requested compression format.");
            }

            if (uncompressedSize == 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    uncompressedSize,
                    "Compressed DirectStorage requests require a non-zero UncompressedSize.");
            }

            return CompressionFormat.GDeflate;
        }

        private static uint ValidateFileSize(ulong fileSize, string parameterName)
        {
            if (fileSize == 0 || fileSize > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    fileSize,
                    "Native DirectStorage requests must contain between 1 and UInt32.MaxValue bytes.");
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
                    "The source range exceeds the DirectStorage file size.");
            }
        }

        private RHIException CreateNativeFailure(
            ERHIErrorCode errorCode,
            int nativeCode,
            string nativeMessage,
            Exception? innerException = null)
        {
            SharpGen.Runtime.Result deviceRemovedReason =
                m_Dx12Device.NativeDevice.DeviceRemovedReason;
            ERHIDeviceState deviceState =
                deviceRemovedReason.Failure
                    ? ERHIDeviceState.Removed
                    : ERHIDeviceState.Operational;
            string message = deviceRemovedReason.Failure
                ? $"{nativeMessage} DeviceRemovedReason=0x{deviceRemovedReason.Code:X8}."
                : nativeMessage;
            return new RHIException(
                errorCode,
                ERHIBackend.DirectX12,
                nativeCode,
                message,
                deviceState,
                innerException);
        }
    }
#pragma warning restore CA1416
}
