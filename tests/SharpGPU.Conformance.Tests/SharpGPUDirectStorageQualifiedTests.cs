using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGPUDirectStorageQualifiedTests
{
#if SHARPGPU_ENABLE_DX12
    [Fact]
    [Trait("Category", "SharpGpuDirectStorageQualified")]
    public void Dx12_NativeDirectStorage_ShouldReadGpuLocalBufferAndRoundTripToCpu()
    {
        if (!FeatureContractContext.TryCreateDx12(
                out FeatureContractContext? context,
                out string contextReason))
        {
            Assert.Fail($"SharpGpuDirectStorageQualified requires a usable DX12 device: {contextReason}");
        }

        FeatureContractContext qualifiedContext = context!;
        using (qualifiedContext)
        {
            RHICapability nativeStorage = qualifiedContext.Device.Capabilities.Storage.NativeGpuFileIo;
            Assert.True(
                nativeStorage.Tier != ERHICapabilityTier.Unavailable,
                $"DX12 native GPU file I/O must be available for this qualified gate: {nativeStorage.UnavailableReason}");

            const int byteCount = 64 * 1024;
            byte[] expected = new byte[byteCount];
            for (int index = 0; index < expected.Length; ++index)
            {
                expected[index] = unchecked((byte)((index * 31) ^ (index >> 3)));
            }

            string sourcePath = Path.Combine(
                AppContext.BaseDirectory,
                $"SharpGPU.DirectStorage.{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(sourcePath, expected);

                using RHIStorageQueue storageQueue = qualifiedContext.Device.CreateStorageQueue();
                using RHIBuffer destination = CreateBuffer(
                    qualifiedContext.Device,
                    byteCount,
                    ERHIBufferUsage.CopySrc | ERHIBufferUsage.CopyDst,
                    ERHIStorageMode.GPULocal);
                using RHIBuffer readback = CreateBuffer(
                    qualifiedContext.Device,
                    byteCount,
                    ERHIBufferUsage.CopyDst,
                    ERHIStorageMode.Readback);

                RHIStorageFileHandle fileHandle =
                    storageQueue.OpenFile(Path.GetFullPath(sourcePath));
                bool fileOpened = true;
                try
                {
                    Assert.Equal((ulong)byteCount, storageQueue.QueryFileSize(fileHandle));

                    storageQueue.RequestBuffer(new RHIStorageBufferRequest
                    {
                        FileHandle = fileHandle,
                        FileOffset = 0,
                        FileSize = byteCount,
                        DestinationBuffer = destination,
                        DestinationOffset = 0,
                    });
                    storageQueue.Submit(qualifiedContext.Fence);
                    Assert.Equal(ERHIFenceStatus.Success, qualifiedContext.Fence.Wait());
                    storageQueue.ThrowIfSubmissionFailed();
                    storageQueue.CloseFile(fileHandle);
                    fileOpened = false;
                    qualifiedContext.Fence.Reset();
                    using RHICommandBuffer commandBuffer = qualifiedContext.CommandQueue.CreateCommandBuffer();
                    commandBuffer.Begin("qualified.direct-storage.readback");
                    RHITransferEncoder transfer = commandBuffer.BeginTransferPass(
                        new RHITransferPassDescriptor
                        {
                            Name = "qualified.direct-storage.readback.copy",
                        });
                    transfer.Barrier(RHIBarrier.Buffer(
                        destination,
                        RHIBufferRange.Whole(),
                        ERHIStageMask.None,
                        ERHIStageMask.Transfer,
                        ERHIAccessMask.None,
                        ERHIAccessMask.TransferRead));
                    transfer.CopyBufferToBuffer(destination, 0, readback, 0, byteCount);
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();

                    qualifiedContext.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                        new RHICommandBuffer[] { commandBuffer },
                        completionFence: qualifiedContext.Fence));
                    Assert.Equal(ERHIFenceStatus.Success, qualifiedContext.Fence.Wait());

                    IntPtr mapped = readback.Map(0, byteCount);
                    byte[] actual = new byte[byteCount];
                    Marshal.Copy(mapped, actual, 0, actual.Length);
                    readback.UnMap(0, 0);
                    Assert.Equal(expected, actual);
                }
                finally
                {
                    if (fileOpened)
                    {
                        storageQueue.CloseFile(fileHandle);
                    }
                }
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "SharpGpuDirectStorageQualified")]
    public void Dx12_NativeDirectStorage_ShouldReadTextureAndRoundTripToCpu()
    {
        if (!FeatureContractContext.TryCreateDx12(
                out FeatureContractContext? context,
                out string contextReason))
        {
            Assert.Fail($"SharpGpuDirectStorageQualified requires a usable DX12 device: {contextReason}");
        }

        FeatureContractContext qualifiedContext = context!;
        using (qualifiedContext)
        {
            RHICapability nativeStorage = qualifiedContext.Device.Capabilities.Storage.NativeGpuFileIo;
            Assert.True(
                nativeStorage.Tier != ERHICapabilityTier.Unavailable,
                $"DX12 native GPU file I/O must be available for this qualified gate: {nativeStorage.UnavailableReason}");

            const int width = 4;
            const int height = 4;
            const int bytesPerPixel = 4;
            const int tightRowBytes = width * bytesPerPixel;
            const int rowPitch = 256;
            byte[] expected = new byte[width * height * bytesPerPixel];
            for (int index = 0; index < expected.Length; ++index)
            {
                expected[index] = unchecked((byte)((index * 13) + 7));
            }
            int conditionedSourceSize = rowPitch * (height - 1) + tightRowBytes;
            byte[] conditionedSource = new byte[conditionedSourceSize];
            for (int row = 0; row < height; ++row)
            {
                expected.AsSpan(row * tightRowBytes, tightRowBytes)
                    .CopyTo(conditionedSource.AsSpan(row * rowPitch, tightRowBytes));
            }

            string sourcePath = Path.Combine(
                AppContext.BaseDirectory,
                $"SharpGPU.DirectStorage.Texture.{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(sourcePath, conditionedSource);

                using RHIStorageQueue storageQueue = qualifiedContext.Device.CreateStorageQueue();
                using RHITexture destination = qualifiedContext.Device.CreateTexture(
                    new RHITextureDescriptor
                    {
                        MipCount = 1,
                        Extent = new uint3(width, height, 1),
                        Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                        SampleCount = ERHISampleCount.None,
                        StorageMode = ERHIStorageMode.GPULocal,
                        UsageFlag = ERHITextureUsage.CopySrc | ERHITextureUsage.CopyDst,
                        Dimension = ERHITextureDimension.Texture2D,
                    });
                using RHIBuffer readback = CreateBuffer(
                    qualifiedContext.Device,
                    rowPitch * height,
                    ERHIBufferUsage.CopyDst,
                    ERHIStorageMode.Readback);

                RHIStorageFileHandle fileHandle =
                    storageQueue.OpenFile(Path.GetFullPath(sourcePath));
                bool fileOpened = true;
                try
                {
                    storageQueue.RequestTexture(new RHIStorageTextureRequest
                    {
                        FileHandle = fileHandle,
                        FileOffset = 0,
                        FileSize = (ulong)conditionedSource.Length,
                        DestinationTexture = destination,
                        MipLevel = 0,
                        ArraySlice = 0,
                    });
                    storageQueue.Submit(qualifiedContext.Fence);
                    Assert.Equal(ERHIFenceStatus.Success, qualifiedContext.Fence.Wait());
                    storageQueue.ThrowIfSubmissionFailed();
                    storageQueue.CloseFile(fileHandle);
                    fileOpened = false;

                    qualifiedContext.Fence.Reset();
                    using RHICommandBuffer commandBuffer = qualifiedContext.CommandQueue.CreateCommandBuffer();
                    commandBuffer.Begin("qualified.direct-storage.texture-readback");
                    RHITransferEncoder transfer = commandBuffer.BeginTransferPass(
                        new RHITransferPassDescriptor
                        {
                            Name = "qualified.direct-storage.texture-readback.copy",
                        });
                    transfer.Barrier(RHIBarrier.Texture(
                        destination,
                        RHITextureSubresourceRange.Whole(ERHITextureAspectMask.Color),
                        ERHITextureLayout.Common,
                        ERHITextureLayout.CopySource,
                        ERHIStageMask.None,
                        ERHIStageMask.Transfer,
                        ERHIAccessMask.None,
                        ERHIAccessMask.TransferRead));
                    transfer.CopyTextureToBuffer(
                        new RHITextureCopyDescriptor
                        {
                            Texture = destination,
                            MipLevel = 0,
                            SliceBase = 0,
                            SliceCount = 1,
                            Origin = new uint3(0, 0, 0),
                        },
                        new RHIBufferCopyDescriptor
                        {
                            Buffer = readback,
                            Offset = 0,
                            RowPitch = rowPitch,
                            TextureHeight = new uint3(width, height, 1),
                        },
                        new int3(width, height, 1));
                    commandBuffer.EndTransferPass();
                    commandBuffer.End();

                    qualifiedContext.CommandQueue.Submit(new RHIQueueSubmitDescriptor(
                        new RHICommandBuffer[] { commandBuffer },
                        completionFence: qualifiedContext.Fence));
                    Assert.Equal(ERHIFenceStatus.Success, qualifiedContext.Fence.Wait());

                    IntPtr mapped = readback.Map(0, rowPitch * height);
                    byte[] actualRow = new byte[tightRowBytes];
                    for (int row = 0; row < height; ++row)
                    {
                        Marshal.Copy(mapped + (row * rowPitch), actualRow, 0, actualRow.Length);
                        Assert.Equal(
                            expected.AsSpan(row * tightRowBytes, tightRowBytes).ToArray(),
                            actualRow);
                    }
                    readback.UnMap(0, 0);
                }
                finally
                {
                    if (fileOpened)
                    {
                        storageQueue.CloseFile(fileHandle);
                    }
                }
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "SharpGpuDirectStorageQualified")]
    public void Dx12_NativeDirectStorage_ShouldRejectNonGpuLocalDestination()
    {
        if (!FeatureContractContext.TryCreateDx12(
                out FeatureContractContext? context,
                out string contextReason))
        {
            Assert.Fail($"SharpGpuDirectStorageQualified requires a usable DX12 device: {contextReason}");
        }

        FeatureContractContext qualifiedContext = context!;
        using (qualifiedContext)
        {
            RHICapability nativeStorage = qualifiedContext.Device.Capabilities.Storage.NativeGpuFileIo;
            Assert.True(
                nativeStorage.Tier != ERHICapabilityTier.Unavailable,
                $"DX12 native GPU file I/O must be available for this qualified gate: {nativeStorage.UnavailableReason}");

            string sourcePath = Path.Combine(
                Path.GetTempPath(),
                $"SharpGPU.DirectStorage.Validation.{Guid.NewGuid():N}.bin");
            try
            {
                File.WriteAllBytes(sourcePath, new byte[4096]);
                using RHIStorageQueue storageQueue = qualifiedContext.Device.CreateStorageQueue();
                using RHIBuffer hostUpload = CreateBuffer(
                    qualifiedContext.Device,
                    4096,
                    ERHIBufferUsage.CopyDst,
                    ERHIStorageMode.HostUpload);
                RHIStorageFileHandle fileHandle = storageQueue.OpenFile(Path.GetFullPath(sourcePath));
                try
                {
                    Assert.Throws<ArgumentException>(() =>
                        storageQueue.RequestBuffer(new RHIStorageBufferRequest
                        {
                            FileHandle = fileHandle,
                            FileOffset = 0,
                            FileSize = 4096,
                            DestinationBuffer = hostUpload,
                            DestinationOffset = 0,
                        }));
                }
                finally
                {
                    storageQueue.CloseFile(fileHandle);
                }
            }
            finally
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }
    }
#endif

    private static RHIBuffer CreateBuffer(
        RHIDevice device,
        int byteSize,
        ERHIBufferUsage usage,
        ERHIStorageMode storageMode)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = byteSize,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = usage,
            StorageMode = storageMode,
        });
    }
}
