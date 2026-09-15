using System;
using System.Collections.Generic;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12DescriptorAllocatorTests
{
    [Fact]
    public void SizeClass_MapsCountsToStableTiers()
    {
        Assert.Equal(0, Dx12DescriptorSizeClass.Of(1));
        Assert.Equal(1, Dx12DescriptorSizeClass.Of(2));
        Assert.Equal(1, Dx12DescriptorSizeClass.Of(16));
        Assert.Equal(2, Dx12DescriptorSizeClass.Of(17));
        Assert.Equal(2, Dx12DescriptorSizeClass.Of(64));
        Assert.Equal(3, Dx12DescriptorSizeClass.Of(65));
        Assert.Equal(3, Dx12DescriptorSizeClass.Of(256));
        Assert.Equal(4, Dx12DescriptorSizeClass.Of(257));
        Assert.Equal(4, Dx12DescriptorSizeClass.Of(1024));
    }

    [Fact]
    public void RangeAllocator_PrefersMatchingClassOverCarvingALargeBlock()
    {
        Dx12DescriptorRangeAllocator allocator = new Dx12DescriptorRangeAllocator(1024);
        int large = allocator.Allocate(300);
        int smallA = allocator.Allocate(1);
        int smallB = allocator.Allocate(1);
        Assert.Equal(0, large);
        Assert.Equal(300, smallA);
        Assert.Equal(301, smallB);

        allocator.Free(smallA, 1);

        int reusedSmall = allocator.Allocate(1);
        Assert.Equal(smallA, reusedSmall);
        Assert.Equal(722, allocator.AvailableDescriptorCount);
    }

    [Fact]
    public void RangeAllocator_CoalescesAcrossClassesAndRejectsDoubleFree()
    {
        Dx12DescriptorRangeAllocator allocator = new Dx12DescriptorRangeAllocator(32);
        int first = allocator.Allocate(8);
        int second = allocator.Allocate(8);
        Assert.Equal(0, first);
        Assert.Equal(8, second);
        allocator.Free(first, 8);
        allocator.Free(second, 8);
        Assert.Equal(0, allocator.Allocate(16));
        Assert.Equal(16, allocator.AvailableDescriptorCount);
        allocator.Free(0, 16);
        Assert.Throws<InvalidOperationException>(() => allocator.Free(0, 8));
    }

    [Fact]
    public void DirtyRanges_CoalesceAndChooseSingleSpanWhenCoverageIsHigh()
    {
        Dx12DescriptorDirtyRanges dirty = new Dx12DescriptorDirtyRanges();
        dirty.Add(0, 1);
        dirty.Add(2, 1);
        dirty.Add(1, 1);
        Assert.Equal(1, dirty.RangeCount);
        Assert.True(dirty.ShouldCopyAsSingleSpan(out int start, out int count));
        Assert.Equal(0, start);
        Assert.Equal(3, count);

        dirty.Clear();
        dirty.Add(0, 1);
        dirty.Add(100, 1);
        Assert.Equal(2, dirty.RangeCount);
        Assert.False(dirty.ShouldCopyAsSingleSpan(out start, out count));
        Assert.Equal(0, start);
        Assert.Equal(101, count);

        List<(int Start, int Count)> copied = new List<(int Start, int Count)>();
        dirty.CopyTo(copied);
        Assert.Equal(new[] { (0, 1), (100, 1) }, copied);
    }

    [Fact]
    public void SamplerInternKey_MatchesCompleteDescriptorAndSlotRefCountReachesZero()
    {
        RHISamplerDescriptor descriptor = new RHISamplerDescriptor
        {
            LodMin = 0,
            LodMax = 8,
            MipLODBias = 0.25f,
            Anisotropy = 4,
            MinFilter = ERHIFilterMode.Linear,
            MagFilter = ERHIFilterMode.Linear,
            MipFilter = ERHIFilterMode.Point,
            AddressModeU = ERHIAddressMode.ClampToEdge,
            AddressModeV = ERHIAddressMode.Repeat,
            AddressModeW = ERHIAddressMode.MirrorRepeat,
            ComparisonMode = ERHIComparisonMode.Less,
        };
        Dx12SamplerInternKey left = Dx12SamplerInternKey.From(descriptor);
        Dx12SamplerInternKey right = Dx12SamplerInternKey.From(descriptor);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        descriptor.Anisotropy = 8;
        Assert.NotEqual(left, Dx12SamplerInternKey.From(descriptor));

        Dx12SamplerInternSlot slot = new Dx12SamplerInternSlot(default);
        Assert.Equal(1, slot.RefCount);
        slot.AddRef();
        Assert.Equal(2, slot.RefCount);
        Assert.False(slot.ReleaseRef());
        Assert.True(slot.ReleaseRef());
        Assert.Equal(0, slot.RefCount);
        Assert.Throws<InvalidOperationException>(() => slot.ReleaseRef());
    }
}
