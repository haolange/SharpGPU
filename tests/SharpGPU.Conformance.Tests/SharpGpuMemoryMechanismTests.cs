using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class SharpGpuMemoryMechanismTests
{
    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void HeapPlacement_EnforcesAlignmentRangeOverlapKindDeviceAndRollback()
    {
        object owner = new();
        RHIResourceMemoryRequirements bufferRequirements =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Buffer);
        using FakeHeap heap = new(
            owner,
            new RHIHeapDescription(256, bufferRequirements),
            compatibilityBit: 1);

        using RHIHeapPlacement first =
            heap.ReserveForTest(0, bufferRequirements);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => heap.ReserveForTest(32, bufferRequirements));
        Assert.Throws<InvalidOperationException>(
            () => heap.ReserveForTest(0, bufferRequirements));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => heap.ReserveForTest(256, bufferRequirements));

        RHIResourceMemoryRequirements foreignRequirements =
            CreateRequirements(
                new object(),
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Buffer);
        Assert.Throws<ArgumentException>(
            () => heap.ReserveForTest(64, foreignRequirements));

        RHIResourceMemoryRequirements textureRequirements =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Texture);
        Assert.Throws<ArgumentException>(
            () => heap.ReserveForTest(64, textureRequirements));

        RHIResourceMemoryRequirements incompatibleNativeFlags =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Buffer,
                nativeAllocationFlags: 2);
        Assert.Throws<ArgumentException>(
            () => heap.ReserveForTest(64, incompatibleNativeFlags));

        Assert.Throws<InvalidOperationException>(() => heap.Dispose());
        Assert.False(heap.IsDisposed);
        first.Dispose();
        using RHIHeapPlacement reused =
            heap.ReserveForTest(0, bufferRequirements);
        reused.Dispose();
        heap.Dispose();
        Assert.True(heap.IsDisposed);

        ExerciseHeapDisposeReserveRace(owner, bufferRequirements);
    }

    private static void ExerciseHeapDisposeReserveRace(
        object owner,
        RHIResourceMemoryRequirements requirements)
    {
        for (int iteration = 0; iteration < 64; ++iteration)
        {
            FakeHeap raceHeap = new(
                owner,
                new RHIHeapDescription(256, requirements),
                compatibilityBit: 1);
            RHIHeapPlacement? placement = null;
            Exception? reserveFailure = null;
            Exception? disposeFailure = null;
            using ManualResetEventSlim start = new(false);

            Task reserve = Task.Run(
                () =>
                {
                    start.Wait();
                    try
                    {
                        placement =
                            raceHeap.ReserveForTest(0, requirements);
                    }
                    catch (Exception exception)
                    {
                        reserveFailure = exception;
                    }
                });
            Task dispose = Task.Run(
                () =>
                {
                    start.Wait();
                    try
                    {
                        raceHeap.Dispose();
                    }
                    catch (Exception exception)
                    {
                        disposeFailure = exception;
                    }
                });

            start.Set();
            Task.WaitAll(reserve, dispose);
            if (placement != null)
            {
                Assert.Null(reserveFailure);
                Assert.IsType<InvalidOperationException>(disposeFailure);
                Assert.False(raceHeap.IsDisposed);
                placement.Dispose();
                raceHeap.Dispose();
                Assert.True(raceHeap.IsDisposed);
            }
            else
            {
                Assert.Null(disposeFailure);
                Assert.True(raceHeap.IsDisposed);
                Assert.IsAssignableFrom<InvalidOperationException>(
                    reserveFailure);
            }
        }
    }

    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void SparseHeapSpan_ValidatesExactPhysicalRangeWithoutRetainingMappingPolicy()
    {
        object owner = new();
        RHIResourceMemoryRequirements sparseCompatibility =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Texture,
                nativeAllocationFlags: 4);
        using FakeHeap heap = new(
            owner,
            new RHIHeapDescription(256, sparseCompatibility),
            compatibilityBit: 1);

        heap.ValidateSparseSpanForTest(0, 64, sparseCompatibility);
        heap.ValidateSparseSpanForTest(64, 128, sparseCompatibility);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => heap.ValidateSparseSpanForTest(32, 64, sparseCompatibility));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => heap.ValidateSparseSpanForTest(192, 128, sparseCompatibility));

        RHIResourceMemoryRequirements wrongKind =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Buffer,
                nativeAllocationFlags: 4);
        Assert.Throws<ArgumentException>(
            () => heap.ValidateSparseSpanForTest(0, 64, wrongKind));

        Assert.DoesNotContain(
            typeof(RHIHeap)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public),
            method =>
                method.Name.Contains("Map", StringComparison.Ordinal) ||
                method.Name.Contains("Allocate", StringComparison.Ordinal) ||
                method.Name.Contains("Retire", StringComparison.Ordinal));
    }

    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void SparseDescriptors_AreMechanismOnlyAndFailClosed()
    {
        object owner = new();
        using FakeTexture texture = new();
        RHIResourceMemoryRequirements textureCompatibility =
            CreateRequirements(
                owner,
                size: 64,
                alignment: 64,
                compatibilityMask: 1,
                ERHIMemoryResourceKind.Texture);
        using FakeHeap heap = new(
            owner,
            new RHIHeapDescription(128, textureCompatibility),
            compatibilityBit: 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHISparseTextureTileBinding(
                (ERHISparseBindingOperation)byte.MaxValue,
                texture,
                ERHITextureAspectMask.Color,
                0,
                0,
                default,
                new uint3(1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHISparseTextureTileBinding(
                ERHISparseBindingOperation.Unbind,
                texture,
                ERHITextureAspectMask.Color | ERHITextureAspectMask.Depth,
                0,
                0,
                default,
                new uint3(1, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RHISparseTextureTileBinding(
                ERHISparseBindingOperation.Unbind,
                texture,
                ERHITextureAspectMask.Color,
                0,
                0,
                default,
                default));
        Assert.Throws<ArgumentNullException>(
            () => new RHISparseTextureTileBinding(
                ERHISparseBindingOperation.Bind,
                texture,
                ERHITextureAspectMask.Color,
                0,
                0,
                default,
                new uint3(1, 1, 1)));
        Assert.Throws<ArgumentException>(
            () => new RHISparseTextureMipTailBinding(
                ERHISparseBindingOperation.Unbind,
                texture,
                0,
                heap,
                0));

        PropertyInfo[] publicProperties =
            typeof(RHISparseBindDescriptor).GetProperties(
                BindingFlags.Instance | BindingFlags.Public);
        Assert.Equal(
            new[]
            {
                nameof(RHISparseBindDescriptor.CompletionFence),
                nameof(RHISparseBindDescriptor.MipTailBindings),
                nameof(RHISparseBindDescriptor.SignalSemaphores),
                nameof(RHISparseBindDescriptor.TileBindings),
                nameof(RHISparseBindDescriptor.WaitSemaphores),
            },
            publicProperties
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void SparseQueueSynchronization_UsesStrictBinaryStateAndRollsBackReservationFailure()
    {
        object owner = new();
        object foreignOwner = new();
        using FakeSparseQueue queue = new(owner);
        using FakeTexture texture = new();
        using FakeSemaphore signal = new(owner);
        using FakeSemaphore foreign = new(foreignOwner);
        using FakeSemaphore disposed = new(owner);
        disposed.Dispose();

        RHISparseTextureTileBinding operation =
            new(
                ERHISparseBindingOperation.Unbind,
                texture,
                ERHITextureAspectMask.Color,
                0,
                0,
                default,
                new uint3(1, 1, 1));
        RHISparseBindDescriptor empty = default;
        Assert.Throws<ArgumentException>(
            () => queue.BindSparse(in empty));

        RHISparseBindDescriptor crossDevice =
            new(
                tileBindings: new[] { operation },
                signalSemaphores: new RHISemaphore[] { foreign });
        Assert.Throws<ArgumentException>(
            () => queue.BindSparse(in crossDevice));
        RHISparseBindDescriptor disposedSignal =
            new(
                tileBindings: new[] { operation },
                signalSemaphores: new RHISemaphore[] { disposed });
        Assert.Throws<ObjectDisposedException>(
            () => queue.BindSparse(in disposedSignal));

        RHISparseBindDescriptor signalDescriptor =
            new(
                tileBindings: new[] { operation },
                signalSemaphores: new RHISemaphore[] { signal });
        queue.BindSparse(in signalDescriptor);
        Assert.Throws<InvalidOperationException>(
            () => queue.BindSparse(in signalDescriptor));

        RHISparseBindDescriptor waitDescriptor =
            new(
                tileBindings: new[] { operation },
                waitSemaphores: new RHISemaphore[] { signal });
        queue.BindSparse(in waitDescriptor);
        Assert.Throws<InvalidOperationException>(
            () => queue.BindSparse(in waitDescriptor));
        queue.BindSparse(in signalDescriptor);
    }

    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void VulkanMemoryBudgetCapability_IsExtensionAccurateAndFailClosed()
    {
        RHICapability unavailable =
            VulkanMemoryBudgetUtility.CreateCapability(
                extensionEnabled: false,
                memoryHeapCount: 2);
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            unavailable.Tier);
        Assert.Equal(
            ERHICapabilityStrategy.Unavailable,
            unavailable.Strategy);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeExtensionQuery,
            unavailable.Provenance.Kind);
        Assert.Contains(
            VulkanMemoryBudgetUtility.ExtensionName,
            unavailable.Provenance.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            VulkanMemoryBudgetUtility.ExtensionName,
            unavailable.UnavailableReason,
            StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(
            () => unavailable.Require("Vulkan memory budget"));

        RHICapability available =
            VulkanMemoryBudgetUtility.CreateCapability(
                extensionEnabled: true,
                memoryHeapCount: 3);
        Assert.Equal(ERHICapabilityTier.Tier1, available.Tier);
        Assert.Equal(
            ERHICapabilityStrategy.NativeExtension,
            available.Strategy);
        Assert.True(
            available.Limits.TryGetValue(
                ERHICapabilityLimitKind.MemoryHeapCount,
                out ulong heapCount));
        Assert.Equal(3UL, heapCount);

        Assert.Equal(
            Vortice.Vulkan.VkMemoryPropertyFlags.DeviceLocal |
                Vortice.Vulkan.VkMemoryPropertyFlags.LazilyAllocated,
            VulkanUtility.ConvertToVkMemoryProperty(
                ERHIStorageMode.Memoryless));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanUtility.ConvertToVkMemoryProperty(
                ERHIStorageMode.Pending));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VulkanUtility.ConvertToVkMemoryProperty(
                (ERHIStorageMode)byte.MaxValue));
        Assert.Throws<NotSupportedException>(
            () => VulkanMemoryUtility.BuildBufferCreateInfo(
                new RHIBufferDescriptor
                {
                    ByteSize = 256,
                    Format = ERHIBufferFormat.Undefine,
                    UsageFlag = ERHIBufferUsage.CopySrc,
                    StorageMode =
                        ERHIStorageMode.Memoryless,
                }));
    }

    [Trait("Category", "SharpGpuPortable")]
    [Fact]
    public void PublicMemorySurface_ExposesCommittedPlacedSparseBudgetAndResidencyWithoutPolicyTypes()
    {
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreateBuffer)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreatePlacedBuffer)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreateTexture)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreatePlacedTexture)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreateSparseTexture)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.GetSparseTextureMemoryRequirements)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.QueryMemoryBudget)));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.RequestResidency)));
        Assert.NotNull(typeof(RHICommandQueue).GetMethod(nameof(RHICommandQueue.BindSparse)));

        string[] forbiddenPublicTypes =
        {
            "RHIFrameResource",
            "RHIResourceLease",
            "RHISparseAllocator",
            "RHISparseMapping",
            "RHIResidencyManager",
        };
        string[] exportedNames = typeof(RHIDevice).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToArray();
        foreach (string forbidden in forbiddenPublicTypes)
        {
            Assert.DoesNotContain(forbidden, exportedNames);
        }
    }

    private static RHIResourceMemoryRequirements CreateRequirements(
        object owner,
        ulong size,
        ulong alignment,
        ulong compatibilityMask,
        ERHIMemoryResourceKind kind,
        ulong nativeAllocationFlags = 0)
    {
        return new RHIResourceMemoryRequirements(
            owner,
            size,
            alignment,
            ERHIStorageMode.GPULocal,
            compatibilityMask,
            kind,
            nativeAllocationFlags);
    }

    private sealed class FakeHeap : RHIHeap
    {
        internal FakeHeap(
            object owner,
            in RHIHeapDescription descriptor,
            ulong compatibilityBit)
            : base(owner, descriptor, compatibilityBit)
        {
        }

        internal RHIHeapPlacement ReserveForTest(
            ulong offset,
            in RHIResourceMemoryRequirements requirements) =>
            ReservePlacement(offset, requirements);

        internal void ValidateSparseSpanForTest(
            ulong offset,
            ulong size,
            in RHIResourceMemoryRequirements requirements) =>
            ValidateSparseSpan(offset, size, requirements);
    }

    private sealed class FakeTexture : RHITexture
    {
        public override RHITextureView CreateTextureView(
            in RHITextureViewDescriptor descriptor) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSparseQueue : RHICommandQueue
    {
        private readonly object m_Owner;

        internal FakeSparseQueue(object owner)
        {
            m_Owner = owner;
            m_PipelineType = ERHIPipelineType.Graphics;
        }

        public override ulong Frequency => 1;
        protected override object DeviceIdentity => m_Owner;

        public override RHICommandBuffer CreateCommandBuffer() =>
            throw new NotSupportedException();

        public override void Submit(in RHIQueueSubmitDescriptor descriptor) =>
            throw new NotSupportedException();

        public override void BindSparse(in RHISparseBindDescriptor descriptor)
        {
            ValidateSparseBind(in descriptor);
            ReserveSparseBind(in descriptor);
            CommitSparseBind(in descriptor);
        }
    }

    private sealed class FakeSemaphore : RHISemaphore
    {
        internal FakeSemaphore(object owner)
            : base(owner)
        {
        }
    }
}
