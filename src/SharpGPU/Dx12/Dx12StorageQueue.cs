using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.DirectStorage;
using Vortice.Mathematics;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
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
        private readonly List<Action> m_PendingRequests;

        private bool m_DirectStorageRuntimeAvailable;
        private IDStorageFactory? m_DStorageFactory;
        private IDStorageQueue? m_DStorageQueue;
        private Vortice.Direct3D12.ID3D12Device? m_DStorageDevice;

        // Keep wrappers alive so GC/finalizer cannot release wrapped native objects unexpectedly.
        private readonly Dictionary<nint, Vortice.Direct3D12.ID3D12Resource> m_DStorageResources = new();
        private readonly Dictionary<nint, Vortice.Direct3D12.ID3D12Fence> m_DStorageFences = new();
        private readonly Dictionary<nint, IDStorageFile> m_DStorageFilesByHandle = new();
        private readonly Dictionary<nint, string> m_FilePathByHandle = new();
        private long m_NextFileHandle = 1;

        // Win32 file constants
        private const uint GENERIC_READ_ACCESS = 0x80000000;
        private const uint FILE_SHARE_READ_FLAG = 0x00000001;
        private const uint OPEN_EXISTING_DISP = 3;
        private const uint FILE_ATTRIBUTE_NORMAL_FLAG = 0x00000080;
        private const uint FILE_FLAG_OVERLAPPED_FLAG = 0x40000000;
        private const uint FILE_FLAG_NO_BUFFERING_FLAG = 0x20000000;
        private const string DirectStorageRuntimeCore = "dstoragecore.dll";
        private const string DirectStorageRuntime = "dstorage.dll";

        public Dx12StorageQueue(Dx12Device device)
        {
            m_Dx12Device = device;
            m_PendingRequests = new List<Action>();

            m_DirectStorageRuntimeAvailable = ProbeDirectStorageRuntime();
            if (m_DirectStorageRuntimeAvailable)
            {
                InitializeDirectStorageQueue();
            }
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            RHIStorageFileHandle result;
            result.NativeHandle = (IntPtr)Interlocked.Increment(ref m_NextFileHandle);
            m_FilePathByHandle[(nint)result.NativeHandle] = absPath;
            return result;
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            nint handleKey = (nint)fileHandle.NativeHandle;
            if (m_DStorageFilesByHandle.TryGetValue(handleKey, out IDStorageFile? storageFile))
            {
                storageFile.Dispose();
                m_DStorageFilesByHandle.Remove(handleKey);
            }
            m_FilePathByHandle.Remove(handleKey);
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            if (!m_FilePathByHandle.TryGetValue((nint)fileHandle.NativeHandle, out string? absPath))
            {
                return 0;
            }

            FileInfo fileInfo = new FileInfo(absPath);
            return fileInfo.Exists ? (ulong)fileInfo.Length : 0;
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            RHIStorageBufferRequest capturedRequest = request;
            if (TryEnqueueDirectStorageBuffer(in capturedRequest))
            {
                return;
            }

            // CPU fallback path (behavior kept consistent with existing implementation).
            m_PendingRequests.Add(() =>
            {
                if (!m_FilePathByHandle.TryGetValue((nint)capturedRequest.FileHandle.NativeHandle, out string? absPath))
                {
                    return;
                }

                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                using FileStream fileStream = File.OpenRead(absPath);
                fileStream.Seek((long)capturedRequest.FileOffset, SeekOrigin.Begin);
                _ = fileStream.Read(tempBuffer, 0, tempBuffer.Length);

                // TODO(UNVERIFIED): Upload fallback data to destination buffer through a copy queue path.
            });
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            RHIStorageTextureRequest capturedRequest = request;
            if (TryEnqueueDirectStorageTexture(in capturedRequest))
            {
                return;
            }

            // CPU fallback path (behavior kept consistent with existing implementation).
            m_PendingRequests.Add(() =>
            {
                if (!m_FilePathByHandle.TryGetValue((nint)capturedRequest.FileHandle.NativeHandle, out string? absPath))
                {
                    return;
                }

                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                using FileStream fileStream = File.OpenRead(absPath);
                fileStream.Seek((long)capturedRequest.FileOffset, SeekOrigin.Begin);
                _ = fileStream.Read(tempBuffer, 0, tempBuffer.Length);

                // TODO(UNVERIFIED): Upload fallback data to destination texture through a copy queue path.
            });
        }

        public override void Submit(RHIFence signalFence)
        {
            foreach (Action request in m_PendingRequests)
            {
                request();
            }
            m_PendingRequests.Clear();

            if (m_DStorageQueue != null)
            {
                if (signalFence != null)
                {
                    Dx12Fence dx12Fence = signalFence as Dx12Fence;
                    if (dx12Fence != null && dx12Fence.NativeFence != null)
                    {
                        Vortice.Direct3D12.ID3D12Fence wrappedFence = GetOrCreateFenceWrapper(dx12Fence.NativeFence);
                        m_DStorageQueue.EnqueueSignal(wrappedFence, 1);
                    }
                }

                m_DStorageQueue.Submit();
                return;
            }

            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                dx12Fence.NativeFence.Signal(1);
            }
        }

        protected override void Release()
        {
            m_PendingRequests.Clear();

            foreach (IDStorageFile storageFile in m_DStorageFilesByHandle.Values)
            {
                storageFile.Dispose();
            }
            m_DStorageFilesByHandle.Clear();
            m_FilePathByHandle.Clear();

            foreach (Vortice.Direct3D12.ID3D12Fence fence in m_DStorageFences.Values)
            {
                fence.Dispose();
            }
            m_DStorageFences.Clear();

            foreach (Vortice.Direct3D12.ID3D12Resource resource in m_DStorageResources.Values)
            {
                resource.Dispose();
            }
            m_DStorageResources.Clear();

            m_DStorageQueue?.Dispose();
            m_DStorageQueue = null;

            m_DStorageFactory?.Dispose();
            m_DStorageFactory = null;

            m_DStorageDevice?.Dispose();
            m_DStorageDevice = null;
        }

        private static bool ProbeDirectStorageRuntime()
        {
            if (NativeLibrary.TryLoad(DirectStorageRuntimeCore, out nint coreHandle))
            {
                NativeLibrary.Free(coreHandle);
                return true;
            }

            if (NativeLibrary.TryLoad(DirectStorageRuntime, out nint runtimeHandle))
            {
                NativeLibrary.Free(runtimeHandle);
                return true;
            }

            return false;
        }

        private void InitializeDirectStorageQueue()
        {
            try
            {
                m_DStorageFactory = DirectStorage.DStorageGetFactory<IDStorageFactory>();

                m_Dx12Device.NativeDevice.AddRef();
                m_DStorageDevice = new Vortice.Direct3D12.ID3D12Device((nint)m_Dx12Device.NativeDevice);

                ushort queueCapacity = (ushort)Math.Clamp(256, DirectStorage.MinQueueCapacity, DirectStorage.MaxQueueCapacity);
                QueueDesc queueDesc = new QueueDesc
                {
                    SourceType = RequestSourceType.File,
                    Capacity = queueCapacity,
                    Priority = Priority.Normal,
                    Name = "SharpGPU.Dx12StorageQueue",
                    Device = m_DStorageDevice,
                };

                m_DStorageQueue = m_DStorageFactory.CreateQueue<IDStorageQueue>(queueDesc);
                m_DirectStorageRuntimeAvailable = m_DStorageQueue != null;
            }
            catch (Exception ex)
            {
                m_DirectStorageRuntimeAvailable = false;
                _ = ex;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"DirectStorage initialization failed: {ex}");
#endif

                m_DStorageQueue?.Dispose();
                m_DStorageQueue = null;

                m_DStorageFactory?.Dispose();
                m_DStorageFactory = null;

                m_DStorageDevice?.Dispose();
                m_DStorageDevice = null;
            }
        }

        private bool TryEnqueueDirectStorageBuffer(in RHIStorageBufferRequest request)
        {
            if (m_DStorageQueue == null || request.FileSize > uint.MaxValue)
            {
                return false;
            }

            if (!TryGetStorageFile(request.FileHandle, out IDStorageFile storageFile))
            {
                return false;
            }

            Dx12Buffer dx12Buffer = request.DestinationBuffer as Dx12Buffer;
            if (dx12Buffer == null || dx12Buffer.NativeResource == null)
            {
                return false;
            }

            Vortice.Direct3D12.ID3D12Resource wrappedResource = GetOrCreateResourceWrapper(dx12Buffer.NativeResource);

            RequestOptions options = default;
            options.SourceType = RequestSourceType.File;
            options.DestinationType = RequestDestinationType.Buffer;
            options.CompressionFormat = CompressionFormat.None;

            Source source = default;
            source.File = new SourceFile
            {
                Source = storageFile,
                Offset = request.FileOffset,
                Size = (uint)request.FileSize,
            };

            Destination destination = default;
            destination.Buffer = new DestinationBuffer
            {
                Resource = wrappedResource,
                Offset = request.DestinationOffset,
                Size = (uint)request.FileSize,
            };

            Request directStorageRequest = new Request
            {
                Options = options,
                Source = source,
                Destination = destination,
                UncompressedSize = (uint)request.FileSize,
                CancellationTag = 0,
                Name = "SharpGPU.Dx12.BufferUpload",
            };

            m_DStorageQueue.EnqueueRequest(directStorageRequest);
            return true;
        }

        private bool TryEnqueueDirectStorageTexture(in RHIStorageTextureRequest request)
        {
            if (m_DStorageQueue == null || request.FileSize > uint.MaxValue)
            {
                return false;
            }

            if (!TryGetStorageFile(request.FileHandle, out IDStorageFile storageFile))
            {
                return false;
            }

            Dx12Texture dx12Texture = request.DestinationTexture as Dx12Texture;
            if (dx12Texture == null || dx12Texture.NativeResource == null)
            {
                return false;
            }

            Vortice.Direct3D12.ID3D12Resource wrappedResource = GetOrCreateResourceWrapper(dx12Texture.NativeResource);

            uint mipLevel = request.MipLevel;
            uint subresourceIndex = mipLevel + (request.ArraySlice * dx12Texture.Descriptor.MipCount);

            uint width = Math.Max(1u, dx12Texture.Descriptor.Extent.x >> (int)mipLevel);
            uint height = Math.Max(1u, dx12Texture.Descriptor.Extent.y >> (int)mipLevel);
            uint depth = Math.Max(1u, dx12Texture.Descriptor.Extent.z >> (int)mipLevel);

            RequestOptions options = default;
            options.SourceType = RequestSourceType.File;
            options.DestinationType = RequestDestinationType.TextureRegion;
            options.CompressionFormat = CompressionFormat.None;

            Source source = default;
            source.File = new SourceFile
            {
                Source = storageFile,
                Offset = request.FileOffset,
                Size = (uint)request.FileSize,
            };

            Destination destination = default;
            destination.Texture = new DestinationTextureRegion
            {
                Resource = wrappedResource,
                SubresourceIndex = subresourceIndex,
                Region = new Box(0, 0, 0, (int)width, (int)height, (int)depth),
            };

            Request directStorageRequest = new Request
            {
                Options = options,
                Source = source,
                Destination = destination,
                UncompressedSize = (uint)request.FileSize,
                CancellationTag = 0,
                Name = "SharpGPU.Dx12.TextureUpload",
            };

            m_DStorageQueue.EnqueueRequest(directStorageRequest);
            return true;
        }

        private bool TryGetStorageFile(in RHIStorageFileHandle fileHandle, out IDStorageFile storageFile)
        {
            storageFile = null!;
            if (m_DStorageFactory == null)
            {
                return false;
            }

            nint handleKey = (nint)fileHandle.NativeHandle;
            if (m_DStorageFilesByHandle.TryGetValue(handleKey, out IDStorageFile existingFile))
            {
                storageFile = existingFile;
                return true;
            }

            if (!m_FilePathByHandle.TryGetValue(handleKey, out string? absPath))
            {
                return false;
            }

            try
            {
                IDStorageFile openedFile = m_DStorageFactory.OpenFile<IDStorageFile>(absPath);
                m_DStorageFilesByHandle[handleKey] = openedFile;
                storageFile = openedFile;
                return true;
            }
            catch (Exception ex)
            {
                _ = ex;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"DirectStorage OpenFile failed: {absPath}. {ex}");
#endif
                return false;
            }
        }

        private Vortice.Direct3D12.ID3D12Resource GetOrCreateResourceWrapper(Vortice.Direct3D12.ID3D12Resource nativeResource)
        {
            nint key = (nint)nativeResource;
            if (m_DStorageResources.TryGetValue(key, out Vortice.Direct3D12.ID3D12Resource existingResource))
            {
                return existingResource;
            }

            nativeResource.AddRef();
            Vortice.Direct3D12.ID3D12Resource wrappedResource = new Vortice.Direct3D12.ID3D12Resource(key);
            m_DStorageResources[key] = wrappedResource;
            return wrappedResource;
        }

        private Vortice.Direct3D12.ID3D12Fence GetOrCreateFenceWrapper(Vortice.Direct3D12.ID3D12Fence nativeFence)
        {
            nint key = (nint)nativeFence;
            if (m_DStorageFences.TryGetValue(key, out Vortice.Direct3D12.ID3D12Fence existingFence))
            {
                return existingFence;
            }

            nativeFence.AddRef();
            Vortice.Direct3D12.ID3D12Fence wrappedFence = new Vortice.Direct3D12.ID3D12Fence(key);
            m_DStorageFences[key] = wrappedFence;
            return wrappedFence;
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
