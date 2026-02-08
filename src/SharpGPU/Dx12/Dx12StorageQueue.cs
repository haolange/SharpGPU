using System;
using System.IO;
using Infinity.Core;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static TerraFX.Interop.Windows.Windows;

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

        private Dx12Device m_Dx12Device;
        private List<Action> m_PendingRequests;

        // Win32 file constants
        private const uint GENERIC_READ_ACCESS = 0x80000000;
        private const uint FILE_SHARE_READ_FLAG = 0x00000001;
        private const uint OPEN_EXISTING_DISP = 3;
        private const uint FILE_ATTRIBUTE_NORMAL_FLAG = 0x00000080;
        private const uint FILE_FLAG_OVERLAPPED_FLAG = 0x40000000;
        private const uint FILE_FLAG_NO_BUFFERING_FLAG = 0x20000000;

        public Dx12StorageQueue(Dx12Device device)
        {
            m_Dx12Device = device;
            m_PendingRequests = new List<Action>();
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            fixed (char* pPath = absPath)
            {
                HANDLE fileHandle = CreateFileW(
                    pPath,
                    GENERIC_READ_ACCESS,
                    FILE_SHARE_READ_FLAG,
                    null,
                    OPEN_EXISTING_DISP,
                    FILE_ATTRIBUTE_NORMAL_FLAG | FILE_FLAG_OVERLAPPED_FLAG | FILE_FLAG_NO_BUFFERING_FLAG,
                    HANDLE.NULL);

                RHIStorageFileHandle result;
                result.NativeHandle = (IntPtr)fileHandle.Value;
                return result;
            }
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            HANDLE handle = new HANDLE((void*)fileHandle.NativeHandle);
            CloseHandle(handle);
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            HANDLE handle = new HANDLE((void*)fileHandle.NativeHandle);
            LARGE_INTEGER fileSize;
            GetFileSizeEx(handle, &fileSize);
            return (ulong)fileSize.QuadPart;
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            // Queue the request for batch submission
            RHIStorageBufferRequest capturedRequest = request;
            m_PendingRequests.Add(() =>
            {
                // For now, use CPU-side file read as fallback
                // A full DirectStorage implementation would use IDStorageQueue
                Dx12Buffer dx12Buffer = capturedRequest.DestinationBuffer as Dx12Buffer;
                HANDLE handle = new HANDLE((void*)capturedRequest.FileHandle.NativeHandle);

                // Read file data into staging memory
                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                uint bytesRead;
                OVERLAPPED overlapped = new OVERLAPPED();
                overlapped.Anonymous.Anonymous.Offset = (uint)(capturedRequest.FileOffset & 0xFFFFFFFF);
                overlapped.Anonymous.Anonymous.OffsetHigh = (uint)(capturedRequest.FileOffset >> 32);

                fixed (byte* pBuffer = tempBuffer)
                {
                    ReadFile(handle, pBuffer, (uint)capturedRequest.FileSize, &bytesRead, &overlapped);
                }
            });
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            // Queue the request for batch submission
            RHIStorageTextureRequest capturedRequest = request;
            m_PendingRequests.Add(() =>
            {
                // For now, use CPU-side file read as fallback
                Dx12Texture dx12Texture = capturedRequest.DestinationTexture as Dx12Texture;
                HANDLE handle = new HANDLE((void*)capturedRequest.FileHandle.NativeHandle);

                byte[] tempBuffer = new byte[capturedRequest.FileSize];
                uint bytesRead;
                OVERLAPPED overlapped = new OVERLAPPED();
                overlapped.Anonymous.Anonymous.Offset = (uint)(capturedRequest.FileOffset & 0xFFFFFFFF);
                overlapped.Anonymous.Anonymous.OffsetHigh = (uint)(capturedRequest.FileOffset >> 32);

                fixed (byte* pBuffer = tempBuffer)
                {
                    ReadFile(handle, pBuffer, (uint)capturedRequest.FileSize, &bytesRead, &overlapped);
                }
            });
        }

        public override void Submit(RHIFence signalFence)
        {
            // Execute all pending requests
            foreach (Action request in m_PendingRequests)
            {
                request();
            }
            m_PendingRequests.Clear();

            // Signal the fence
            if (signalFence != null)
            {
                Dx12Fence dx12Fence = signalFence as Dx12Fence;
                dx12Fence.NativeFence->Signal(1);
            }
        }

        protected override void Release()
        {
            m_PendingRequests.Clear();
        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
