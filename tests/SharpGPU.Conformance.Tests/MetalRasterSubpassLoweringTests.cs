using System;
using System.Collections.Generic;
using SharpGPU;
using SharpMath;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class MetalRasterSubpassLoweringTests
{
    private static readonly MetalRasterCapabilities s_AllCapabilities =
        new(
            colorOutputMapping: true,
            framebufferLocalRead: true);

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void SparseInputAndOutputSlotsRemainIndependent()
    {
        using TestTexture input = CreateColorTexture();
        using TestTexture unused = CreateColorTexture();
        using TestTexture output = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(input),
                CreateAttachment(unused),
                CreateAttachment(output),
            },
            CreateSubPass(
                inputs: new[] { -1, 0 },
                outputs: new[] { -1, -1, 2 }));

        MetalRasterPassLowering lowering =
            MetalRasterPassLowering.Compile(
                plan,
                in s_AllCapabilities);
        MetalRasterSubPassLowering phase =
            lowering.SubPasses.Span[0];

        Assert.True(lowering.RequiresFramebufferLocalRead);
        Assert.True(lowering.RequiresColorAttachmentMapping);
        Assert.Equal(2, phase.LocalInputSlotCount);
        Assert.Equal(-1, phase.GetLocalInputPhysicalAttachment(0));
        Assert.Equal(0, phase.GetLocalInputPhysicalAttachment(1));
        Assert.Equal(3, phase.OutputLocationCount);
        Assert.Equal(
            ulong.MaxValue,
            phase.GetPhysicalOutputAttachmentIndex(0));
        Assert.Equal(2ul, phase.GetPhysicalOutputAttachmentIndex(2));
        Assert.Equal(0b0000_0101, lowering.OrdinaryAttachmentMask);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void LocalReadAndOutputMappingConflictFailsClosed()
    {
        using TestTexture input = CreateColorTexture();
        using TestTexture output = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(input),
                CreateAttachment(output),
            },
            CreateSubPass(
                inputs: new[] { 0 },
                outputs: new[] { 1 }));

        NotSupportedException error =
            Assert.Throws<NotSupportedException>(
                () => MetalRasterPassLowering.Compile(
                    plan,
                    in s_AllCapabilities));
        Assert.Contains(
            "independently map",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void CapabilitiesAreCheckedByIndependentDomain()
    {
        using TestTexture input = CreateColorTexture();
        using TestTexture output = CreateColorTexture();
        RHIRasterPassPlan localRead = Compile(
            new[]
            {
                CreateAttachment(input),
                CreateAttachment(output),
            },
            CreateSubPass(
                inputs: new[] { 0 },
                outputs: new[] { -1, 1 }));
        MetalRasterCapabilities noLocalRead =
            new(
                colorOutputMapping: true,
                framebufferLocalRead: false);
        Assert.Throws<NotSupportedException>(
            () => MetalRasterPassLowering.Compile(
                localRead,
                in noLocalRead));

        RHIRasterPassPlan outputMapping = Compile(
            new[]
            {
                CreateAttachment(input),
                CreateAttachment(output),
            },
            CreateSubPass(outputs: new[] { 1 }));
        MetalRasterCapabilities noOutputMapping =
            new(
                colorOutputMapping: false,
                framebufferLocalRead: true);
        Assert.Throws<NotSupportedException>(
            () => MetalRasterPassLowering.Compile(
                outputMapping,
                in noOutputMapping));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FramebufferReadWriteRemainsAnOrdinaryColorAttachment()
    {
        using TestTexture readWrite = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(readWrite),
            },
            CreateSubPass(
                inputs: new[] { 0 },
                outputs: new[] { 0 }));

        MetalRasterPassLowering lowering =
            MetalRasterPassLowering.Compile(
                plan,
                in s_AllCapabilities);
        MetalRasterSubPassLowering phase =
            lowering.SubPasses.Span[0];

        Assert.False(lowering.RequiresFramebufferLocalRead);
        Assert.False(lowering.RequiresColorAttachmentMapping);
        Assert.Equal(1, lowering.OrdinaryAttachmentMask);
        Assert.Equal(0, phase.LocalInputMask);
        Assert.Equal(1, phase.OrdinaryOutputMask);
        Assert.Equal(0ul, phase.GetPhysicalOutputAttachmentIndex(0));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void NativeTransientBatchIsBoundedByRecordingReuse()
    {
        List<IntPtr> released = new();
        using MetalTransientNativeBatch batch =
            new(released.Add);

        batch.RetainOwnership(new IntPtr(1));
        batch.RetainOwnership(new IntPtr(2));
        batch.RetainOwnership(new IntPtr(3));
        Assert.Equal(3, batch.Count);
        Assert.Empty(released);

        batch.ReleaseForCommandBufferReuse();
        Assert.Equal(0, batch.Count);
        Assert.Equal(
            new[] { new IntPtr(3), new IntPtr(2), new IntPtr(1) },
            released);

        batch.RetainOwnership(new IntPtr(4));
        Assert.Equal(1, batch.Count);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void MappingCapabilityRequiresTheCompleteSelectorTriplet()
    {
        MetalRasterCapabilities capabilities =
            MetalRasterCapabilities.FromSelectorProbes(
                passMappingSelector: true,
                mapEntrySelector: true,
                encoderMappingSelector: false);

        Assert.False(capabilities.ColorOutputMapping);
        Assert.False(capabilities.FramebufferLocalRead);
        Assert.False(capabilities.EncoderMappingSelector);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void FrozenSubPassMappingLookupDoesNotAllocate()
    {
        using TestTexture input = CreateColorTexture();
        using TestTexture output = CreateColorTexture();
        RHIRasterPassPlan plan = Compile(
            new[]
            {
                CreateAttachment(input),
                CreateAttachment(output),
            },
            CreateSubPass(
                inputs: new[] { 0 },
                outputs: new[] { 0, 1 }));
        MetalRasterPassLowering lowering =
            MetalRasterPassLowering.Compile(
                plan,
                in s_AllCapabilities);
        MetalRasterSubPassLowering phase =
            lowering.SubPasses.Span[0];

        _ = phase.GetPhysicalAttachmentForMappingIndex(0);
        _ = phase.GetPhysicalOutputAttachmentIndex(1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ulong checksum = 0;
        for (int iteration = 0; iteration < 10_000; ++iteration)
        {
            checksum ^=
                phase.GetPhysicalAttachmentForMappingIndex(0);
            checksum ^=
                phase.GetPhysicalOutputAttachmentIndex(1);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
        GC.KeepAlive(checksum);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void NativeTransientCheckpointRollsBackOnlyTheFailedAttempt()
    {
        List<IntPtr> released = new();
        using MetalTransientNativeBatch batch =
            new(released.Add);

        batch.RetainOwnership(new IntPtr(10));
        int checkpoint = batch.CaptureCheckpoint();
        batch.RetainOwnership(new IntPtr(20));
        batch.RetainOwnership(new IntPtr(30));

        batch.RollbackTo(checkpoint);

        Assert.Equal(1, batch.Count);
        Assert.Equal(
            new[] { new IntPtr(30), new IntPtr(20) },
            released);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void InactiveNativeEncoderValidationFailsFast()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MetalEncoderStateValidation.RequireActive(
                IntPtr.Zero,
                "barrier emission"));
        MetalEncoderStateValidation.RequireActive(
            new IntPtr(1),
            "barrier emission");
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void VariableRateShadingAttachmentRejection_DoesNotWritePassState()
    {
        using TestTexture shadingRate = CreateColorTexture();
        using TestTexture color = CreateColorTexture();
        RHICapability attachment = CreateUnavailableVariableRateShadingAttachment();
        RHIRasterPassPlan? plan = null;

        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () =>
            {
                MetalRasterPassBeginGuard.RejectUnsupportedAttachment(
                    new RHIRasterPassDescriptor
                    {
                        ColorAttachments = new[] { CreateAttachment(color) },
                        ShadingRateTexture = shadingRate,
                    },
                    in attachment);
                plan = Compile(
                    new[] { CreateAttachment(color) });
            });

        Assert.Null(plan);
        Assert.Contains(
            MetalRasterPassBeginGuard.VariableRateShadingAttachmentCapabilityName,
            error.Message,
            StringComparison.Ordinal);

        MetalRasterPassBeginGuard.RejectUnsupportedAttachment(
            new RHIRasterPassDescriptor
            {
                ColorAttachments = new[] { CreateAttachment(color) },
            },
            in attachment);
        plan = Compile(new[] { CreateAttachment(color) });
        Assert.NotNull(plan);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void VariableRateShadingAttachmentGuard_AllowsRetryAfterFailClosedReject()
    {
        using TestTexture shadingRate = CreateColorTexture();
        using TestTexture color = CreateColorTexture();
        RHICapability attachment = CreateUnavailableVariableRateShadingAttachment();
        GuardedMetalRasterBeginSequence sequence = new();

        Assert.Throws<NotSupportedException>(
            () => sequence.Begin(
                new RHIRasterPassDescriptor
                {
                    ColorAttachments = new[] { CreateAttachment(color) },
                    ShadingRateTexture = shadingRate,
                },
                in attachment));
        Assert.False(sequence.HasActivePass);

        sequence.Begin(
            new RHIRasterPassDescriptor
            {
                ColorAttachments = new[] { CreateAttachment(color) },
            },
            in attachment);
        Assert.True(sequence.HasActivePass);
    }

    private static RHICapability CreateUnavailableVariableRateShadingAttachment()
    {
        return RHICapability.Unavailable(
            "Variable-rate shading is not exposed by the Metal backend.",
            ERHICapabilityProbeKind.BackendContract,
            "SharpGPU Metal variable-rate rasterization lowering");
    }

    private sealed class GuardedMetalRasterBeginSequence
    {
        private RHIRasterPassPlan? m_RasterPassPlan;

        internal bool HasActivePass => m_RasterPassPlan != null;

        internal void Begin(
            in RHIRasterPassDescriptor descriptor,
            in RHICapability variableRateShadingAttachment)
        {
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException(
                    "A raster pass is already active on this encoder.");
            }

            MetalRasterPassBeginGuard.RejectUnsupportedAttachment(
                in descriptor,
                in variableRateShadingAttachment);
            m_RasterPassPlan = RHIRasterPassPlanner.Compile(in descriptor);
        }
    }

    private static RHIRasterPassPlan Compile(
        RHIColorAttachmentDescriptor[] attachments,
        params RHISubPassDescriptor[] subPasses)
    {
        RHIRasterPassDescriptor descriptor =
            new()
            {
                ColorAttachments = attachments,
                SubPassDescriptors = subPasses,
            };
        return RHIRasterPassPlanner.Compile(in descriptor);
    }

    private static RHIColorAttachmentDescriptor CreateAttachment(
        RHITexture texture) =>
        new()
        {
            RenderTarget = texture,
            SubresourceRange =
                RHITextureSubresourceRange.Whole(),
            LoadAction = ERHILoadAction.Load,
            StoreAction = ERHIStoreAction.Store,
        };

    private static RHISubPassDescriptor CreateSubPass(
        int[]? inputs = null,
        int[]? outputs = null) =>
        new()
        {
            ColorInputs = inputs is null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs is null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
        };

    private static TestTexture CreateColorTexture(
        ERHITextureUsage usage =
            ERHITextureUsage.RenderTarget |
            ERHITextureUsage.ShaderResource) =>
        new(
            new RHITextureDescriptor
            {
                Extent = new uint3(16, 16, 1),
                MipCount = 1,
                Dimension = ERHITextureDimension.Texture2D,
                Format = ERHIPixelFormat.R8G8B8A8_UNorm,
                SampleCount = ERHISampleCount.None,
                StorageMode = ERHIStorageMode.GPULocal,
                UsageFlag = usage,
            });

    private sealed class TestTexture : RHITexture
    {
        internal TestTexture(in RHITextureDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override RHITextureView CreateTextureView(
            in RHITextureViewDescriptor descriptor) =>
            throw new NotSupportedException();

        protected override void Release()
        {
        }
    }
}
