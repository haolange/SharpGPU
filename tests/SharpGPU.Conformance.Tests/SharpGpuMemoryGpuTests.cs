using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGpuMemoryGpuTests
{
#if SHARPGPU_ENABLE_DX12
    [Trait("Category", "SharpGpuWindowsQualified")]
    [Fact]
    public void Dx12_PlacedSparseBudgetAndResidency_UseNativeMechanisms()
    {
        bool initialized = FeatureContractContext.TryCreateDx12(
            out FeatureContractContext? context,
            out string reason);
        Assert.True(
            initialized,
            $"DX12 is required for the Windows-qualified memory gate: {reason}");
        Assert.NotNull(context);

        using (context)
        {
            ExercisePlacedBuffer(context.Device);

            RHIMemoryBudget budget =
                context.Device.QueryMemoryBudget(ERHIStorageMode.GPULocal);
            Assert.True(budget.BudgetBytes > 0);
            Assert.True(
                context.Device.Capabilities.Memory.BudgetQuery.Tier !=
                    ERHICapabilityTier.Unavailable);

            ExerciseDx12Residency(context.Device);
            ExerciseSparseTexture(
                context.Device,
                context.CommandQueue);
        }
    }
#endif

#if SHARPGPU_VULKAN_QUALIFICATION_HOST
    [Trait("Category", "SharpGpuVulkanQualified")]
    [Fact]
    public void Vulkan_PlacedAndSparse_UseNativeMechanismsAndUnsupportedResidencyFailsClosed()
    {
        using RHIInstance instance = RHIInstance.Create(
            new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.Vulkan,
                SurfaceKind = GetVulkanSurfaceKind(),
                EnableDebugLayer = false,
                EnableValidation = false,
                GraphicsQueueRequestCount = 1,
                ComputeQueueRequestCount = 1,
                TransferQueueRequestCount = 1,
            });
        Assert.True(instance.DeviceCount > 0);
        RHIDevice device = instance.GetDevice(0);

        ExercisePlacedBuffer(device);
        RHICapability budgetCapability =
            device.Capabilities.Memory.BudgetQuery;
#if SHARPGPU_ENABLE_DX12
        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            budgetCapability.Tier);
#endif
        if (budgetCapability.Tier ==
            ERHICapabilityTier.Unavailable)
        {
            Assert.Equal(
                ERHICapabilityStrategy.Unavailable,
                budgetCapability.Strategy);
            Assert.Contains(
                VulkanMemoryBudgetUtility.ExtensionName,
                budgetCapability.UnavailableReason,
                StringComparison.Ordinal);
            Assert.Throws<NotSupportedException>(
                () => device.QueryMemoryBudget(
                    ERHIStorageMode.GPULocal));
        }
        else
        {
            Assert.Equal(
                ERHICapabilityStrategy.NativeExtension,
                budgetCapability.Strategy);
            Assert.Equal(
                ERHICapabilityProbeKind.NativeExtensionQuery,
                budgetCapability.Provenance.Kind);
            Assert.Contains(
                VulkanMemoryBudgetUtility.ExtensionName,
                budgetCapability.Provenance.Source,
                StringComparison.Ordinal);
            Assert.True(
                budgetCapability.Limits.TryGetValue(
                    ERHICapabilityLimitKind.MemoryHeapCount,
                    out ulong heapCount));
            Assert.True(heapCount > 0);
            RHIMemoryBudget budget =
                device.QueryMemoryBudget(ERHIStorageMode.GPULocal);
            Assert.True(budget.BudgetBytes > 0);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => device.QueryMemoryBudget(
                    (ERHIStorageMode)byte.MaxValue));
        }
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            device.Capabilities.Memory.Residency.Tier);
        Assert.Throws<NotSupportedException>(
            () => device.RequestResidency(default));

        RHICommandQueue queue = SelectVulkanSparseQueueOrGraphics(device);
        ExerciseSparseTexture(device, queue);
    }
#endif

#if SHARPGPU_METAL_QUALIFICATION_HOST
    [Trait("Category", "SharpGpuMetalQualified")]
    [Fact]
    public void Metal_PlacedSparseAndBudget_UseNativeMechanismsWhenRuntimeReportsThem()
    {
        using MetalTestContext context = MetalTestContext.Create();
        ExercisePlacedBuffer(context.Device);

        if (context.Device.Capabilities.Memory.BudgetQuery.Tier ==
            ERHICapabilityTier.Unavailable)
        {
            Assert.Throws<NotSupportedException>(
                () => context.Device.QueryMemoryBudget(
                    ERHIStorageMode.GPULocal));
        }
        else
        {
            RHIMemoryBudget budget =
                context.Device.QueryMemoryBudget(
                    ERHIStorageMode.GPULocal);
            Assert.True(budget.BudgetBytes > 0);
        }

        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            context.Device.Capabilities.Memory.Residency.Tier);
        Assert.Throws<NotSupportedException>(
            () => context.Device.RequestResidency(default));
        ExerciseSparseTexture(context.Device, context.Queue);
    }
#endif

    private static void ExercisePlacedBuffer(RHIDevice device)
    {
        Assert.True(
            device.Capabilities.Memory.PlacedResources.Tier !=
                ERHICapabilityTier.Unavailable);
        RHIBufferDescriptor descriptor = new()
        {
            ByteSize = 256,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = ERHIBufferUsage.CopySrc,
            StorageMode = ERHIStorageMode.HostUpload,
        };
        RHIResourceMemoryRequirements requirements =
            device.GetBufferMemoryRequirements(descriptor);
        Assert.True(requirements.Size >= (ulong)descriptor.ByteSize);
        Assert.True(requirements.Alignment > 0);
        Assert.Equal(
            0UL,
            requirements.Alignment &
                (requirements.Alignment - 1));

        ulong secondOffset = AlignUp(
            requirements.Size,
            requirements.Alignment);
        ulong heapSize = AlignUp(
            checked(secondOffset + requirements.Size),
            requirements.Alignment);
        using RHIHeap heap = device.CreateHeap(
            new RHIHeapDescription(heapSize, requirements));
        using RHIBuffer first =
            device.CreatePlacedBuffer(heap, 0, descriptor);
        using RHIBuffer second =
            device.CreatePlacedBuffer(
                heap,
                secondOffset,
                descriptor);
        Assert.Equal(
            ERHIResourceAllocationMode.Placed,
            first.AllocationMode);
        Assert.Throws<InvalidOperationException>(
            () => device.CreatePlacedBuffer(heap, 0, descriptor));

        IntPtr firstMapped = first.Map(0, 0);
        IntPtr secondMapped = second.Map(0, 0);
        Assert.NotEqual(IntPtr.Zero, firstMapped);
        Assert.NotEqual(IntPtr.Zero, secondMapped);
        Assert.NotEqual(firstMapped, secondMapped);
        Assert.Throws<InvalidOperationException>(() => heap.Dispose());
        Assert.False(heap.IsDisposed);
        Assert.Equal(firstMapped, first.Map(0, 0));
        Assert.Equal(secondMapped, second.Map(0, 0));
        Marshal.WriteInt32(firstMapped, unchecked((int)0x7a31c50d));
        Marshal.WriteInt32(secondMapped, unchecked((int)0x219cab74));
        second.UnMap(0, sizeof(int));
        first.UnMap(0, sizeof(int));

        first.Dispose();
        using RHIBuffer reused =
            device.CreatePlacedBuffer(heap, 0, descriptor);
        Assert.Equal(
            ERHIResourceAllocationMode.Placed,
            reused.AllocationMode);

        reused.Dispose();
        second.Dispose();
        heap.Dispose();
        Assert.True(heap.IsDisposed);
    }

#if SHARPGPU_ENABLE_DX12
    private static void ExerciseDx12Residency(RHIDevice device)
    {
        Assert.True(
            device.Capabilities.Memory.Residency.Tier !=
                ERHICapabilityTier.Unavailable);
        RHIBufferDescriptor descriptor = new()
        {
            ByteSize = 64 * 1024,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = ERHIBufferUsage.CopyDst,
            StorageMode = ERHIStorageMode.GPULocal,
        };
        RHIResourceMemoryRequirements requirements =
            device.GetBufferMemoryRequirements(descriptor);
        using RHIHeap heap = device.CreateHeap(
            new RHIHeapDescription(
                AlignUp(requirements.Size, requirements.Alignment),
                requirements));
        using RHIFence fence = device.CreateFence();

        RHIResidencyRequestDescriptor evict = new(
            ERHIResidencyOperation.Evict,
            new RHIHeap[] { heap },
            fence);
        device.RequestResidency(evict);
        Assert.Equal(ERHIFenceStatus.Success, fence.Wait(10_000_000_000));
        fence.Reset();

        RHIResidencyRequestDescriptor makeResident = new(
            ERHIResidencyOperation.MakeResident,
            new RHIHeap[] { heap },
            fence);
        device.RequestResidency(makeResident);
        Assert.Throws<InvalidOperationException>(
            () => device.RequestResidency(makeResident));
        Assert.Equal(ERHIFenceStatus.Success, fence.Wait(10_000_000_000));
        fence.Reset();
    }
#endif

    private static void ExerciseSparseTexture(
        RHIDevice device,
        RHICommandQueue queue)
    {
        RHITextureDescriptor descriptor =
            CreateSparseTextureDescriptor();
        if (device.Capabilities.Memory.SparseBinding.Tier ==
            ERHICapabilityTier.Unavailable)
        {
            Assert.Throws<NotSupportedException>(
                () => device.GetSparseTextureMemoryRequirements(
                    descriptor));
            Assert.Throws<NotSupportedException>(
                () => device.CreateSparseTexture(descriptor));
            Assert.Throws<NotSupportedException>(
                () => queue.BindSparse(default));
        }
        else
        {
            ExerciseAvailableSparseTexture(device, queue, descriptor);
        }
    }

    private static void ExerciseAvailableSparseTexture(
        RHIDevice device,
        RHICommandQueue queue,
        RHITextureDescriptor descriptor)
    {
        RHISparseTextureMemoryRequirements requirements =
            device.GetSparseTextureMemoryRequirements(descriptor);
        Assert.True(requirements.VirtualSizeBytes > 0);
        Assert.True(requirements.TileSizeBytes > 0);
        Assert.True(requirements.TileExtent.x > 0);
        Assert.True(requirements.TileExtent.y > 0);
        Assert.True(requirements.TileExtent.z > 0);
        Assert.NotEmpty(requirements.Subresources.Span.ToArray());

        RHISparseTextureSubresourceTiling subresource =
            requirements.Subresources.Span[0];
        Assert.True(subresource.TileCount.x > 0);
        Assert.True(subresource.TileCount.y > 0);
        Assert.True(subresource.TileCount.z > 0);

        ulong tailSize = requirements.MipTails.IsEmpty
            ? 0
            : requirements.MipTails.Span[0].SizeBytes;
        ulong heapSize = AlignUp(
            checked(requirements.TileSizeBytes + tailSize),
            requirements.HeapCompatibility.Alignment);
        using RHIHeap heap = device.CreateHeap(
            new RHIHeapDescription(
                heapSize,
                requirements.HeapCompatibility));
        using RHITexture texture =
            device.CreateSparseTexture(descriptor);
        Assert.Equal(
            ERHIResourceAllocationMode.Sparse,
            texture.AllocationMode);
        using RHISemaphore semaphore = device.CreateSemaphore();
        using RHIFence fence = device.CreateFence();

        List<RHISparseTextureTileBinding> bindTiles =
        [
            new RHISparseTextureTileBinding(
                ERHISparseBindingOperation.Bind,
                texture,
                subresource.Aspect,
                subresource.MipLevel,
                subresource.ArrayLayer,
                default,
                new uint3(1, 1, 1),
                heap,
                0),
        ];
        List<RHISparseTextureMipTailBinding> bindTails = [];
        if (!requirements.MipTails.IsEmpty)
        {
            RHISparseTextureMipTail tail =
                requirements.MipTails.Span[0];
            bindTails.Add(
                new RHISparseTextureMipTailBinding(
                    ERHISparseBindingOperation.Bind,
                    texture,
                    tail.Index,
                    heap,
                    requirements.TileSizeBytes));
        }

        RHISparseBindDescriptor bind = new(
            tileBindings: bindTiles.ToArray(),
            mipTailBindings: bindTails.ToArray(),
            signalSemaphores: new RHISemaphore[] { semaphore },
            completionFence: fence);
        queue.BindSparse(bind);
        Assert.Equal(ERHIFenceStatus.Success, fence.Wait(10_000_000_000));
        fence.Reset();

        RHISparseTextureTileBinding[] unbindTiles =
            bindTiles
                .Select(binding =>
                    new RHISparseTextureTileBinding(
                        ERHISparseBindingOperation.Unbind,
                        binding.Texture,
                        binding.Aspect,
                        binding.MipLevel,
                        binding.ArrayLayer,
                        binding.TileOffset,
                        binding.TileExtent))
                .ToArray();
        RHISparseTextureMipTailBinding[] unbindTails =
            bindTails
                .Select(binding =>
                    new RHISparseTextureMipTailBinding(
                        ERHISparseBindingOperation.Unbind,
                        binding.Texture,
                        binding.MipTailIndex))
                .ToArray();
        RHISparseBindDescriptor unbind = new(
            tileBindings: unbindTiles,
            mipTailBindings: unbindTails,
            waitSemaphores: new RHISemaphore[] { semaphore },
            completionFence: fence);
        queue.BindSparse(unbind);
        Assert.Equal(ERHIFenceStatus.Success, fence.Wait(10_000_000_000));
        fence.Reset();
    }

    private static RHITextureDescriptor
        CreateSparseTextureDescriptor() =>
        new()
        {
            MipCount = 12,
            Extent = new uint3(2048, 2048, 1),
            Format = ERHIPixelFormat.R8G8B8A8_UNorm,
            SampleCount = ERHISampleCount.None,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag =
                ERHITextureUsage.CopyDst |
                ERHITextureUsage.ShaderResource,
            Dimension = ERHITextureDimension.Texture2D,
        };

    private static RHICommandQueue SelectVulkanSparseQueueOrGraphics(
        RHIDevice device)
    {
        VulkanDevice vulkanDevice = Assert.IsType<VulkanDevice>(device);
        foreach (ERHIPipelineType pipeline in new[]
                 {
                     ERHIPipelineType.Graphics,
                     ERHIPipelineType.Compute,
                     ERHIPipelineType.Transfer,
                 })
        {
            RHICommandQueue? queue =
                device.GetCommandQueue(pipeline, 0);
            if (queue is VulkanCommandQueue vulkanQueue &&
                vulkanDevice.SupportsSparseQueueFamily(
                    vulkanQueue.QueueFamilyIndex))
            {
                return queue;
            }
        }

        if (device.Capabilities.Memory.SparseBinding.Tier !=
            ERHICapabilityTier.Unavailable)
        {
            throw new InvalidOperationException(
                "Vulkan reports sparse binding but exposes no sparse-capable created queue.");
        }

        return device.GetCommandQueue(
                   ERHIPipelineType.Graphics,
                   0)
               ?? throw new InvalidOperationException(
                   "Vulkan did not expose a graphics queue.");
    }

    private static ERHINativeSurfaceKind GetVulkanSurfaceKind()
    {
        if (OperatingSystem.IsWindows())
        {
            return ERHINativeSurfaceKind.Win32Hwnd;
        }
        if (OperatingSystem.IsLinux())
        {
            return string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "WAYLAND_DISPLAY"))
                ? ERHINativeSurfaceKind.X11Window
                : ERHINativeSurfaceKind.WaylandSurface;
        }
        if (OperatingSystem.IsAndroid())
        {
            return ERHINativeSurfaceKind.AndroidNativeWindow;
        }
        throw new PlatformNotSupportedException();
    }

    private static ulong AlignUp(ulong value, ulong alignment) =>
        checked((value + alignment - 1) &
                ~(alignment - 1));
}
