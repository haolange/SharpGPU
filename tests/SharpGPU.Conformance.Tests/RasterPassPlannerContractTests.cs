using System;
using System.Reflection;
using SharpGPU;
using SharpGPU.Mathematics;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class RasterPassPlannerContractTests
{
    [Fact]
    public void Compile_DeepSnapshotsAndNormalizesAttachmentState()
    {
        using TestTexture original = CreateColorTexture(
            width: 32,
            height: 16,
            layers: 2,
            mipCount: 2,
            dimension: ERHITextureDimension.Texture2DArray);
        using TestTexture replacement = CreateColorTexture(
            width: 32,
            height: 16,
            layers: 2,
            mipCount: 2,
            dimension: ERHITextureDimension.Texture2DArray);

        RHIColorAttachmentDescriptor[] attachments =
        {
            CreateColorAttachment(
                original,
                new RHITextureSubresourceRange
                {
                    BaseMipLevel = 1,
                    BaseArrayLayer = 0,
                }),
        };
        RHISubPassDescriptor[] subPasses =
        {
            CreateSubPass(outputs: new[] { 0 }),
        };
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            Name = "snapshot",
            ArrayLength = 2,
            ColorAttachments = attachments,
            SubPassDescriptors = subPasses,
        };

        RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);

        attachments[0].RenderTarget = replacement;
        subPasses[0].ColorOutputs[0] = -1;
        RHIRasterPassDescriptor backendSnapshot = plan.DescriptorSnapshot;
        backendSnapshot.ColorAttachments.Span[0].RenderTarget = replacement;
        backendSnapshot.SubPassDescriptors.Span[0].ColorOutputs[0] = -1;

        Assert.Equal("snapshot", plan.Name);
        Assert.Equal(2u, plan.ArrayLength);
        Assert.Equal(ERHISampleCount.None, plan.SampleCount);
        Assert.Equal(16u, plan.Width);
        Assert.Equal(8u, plan.Height);
        Assert.Same(original, plan.GetColorAttachment(0).RenderTarget);
        Assert.Equal(
            ERHITextureAspectMask.Color,
            plan.GetColorAttachment(0).SubresourceRange.AspectMask);
        Assert.Equal(1u, plan.GetColorAttachment(0).SubresourceRange.BaseMipLevel);
        Assert.Equal(1u, plan.GetColorAttachment(0).SubresourceRange.MipLevelCount);
        Assert.Equal(2u, plan.GetColorAttachment(0).SubresourceRange.ArrayLayerCount);
        Assert.Equal((byte)1, plan.GetSubPass(0).WriteMask);

        RHIRasterPassDescriptor secondSnapshot = plan.DescriptorSnapshot;
        Assert.Same(original, secondSnapshot.ColorAttachments.Span[0].RenderTarget);
        Assert.Equal(0, secondSnapshot.SubPassDescriptors.Span[0].ColorOutputs[0]);
    }

    [Fact]
    public void Compile_RejectsReadBeforeWriteAndAttachmentOverlap()
    {
        using TestTexture texture = CreateColorTexture();
        RHIColorAttachmentDescriptor unavailable =
            CreateColorAttachment(texture, loadAction: ERHILoadAction.DontCare);
        RHIRasterPassDescriptor readBeforeWrite = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { unavailable },
            SubPassDescriptors = new[]
            {
                CreateSubPass(inputs: new[] { 0 }),
            },
        };

        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in readBeforeWrite));

        RHIRasterPassDescriptor overlap = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(texture),
                CreateColorAttachment(texture),
            },
        };

        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in overlap));
    }

    [Fact]
    public void Compile_RejectsAspectSampleExtentAndLayerMismatches()
    {
        using TestTexture depthAsColor = CreateTexture(
            ERHIPixelFormat.D32_Float,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget,
            16,
            16);
        RHIRasterPassDescriptor wrongFormat = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { CreateColorAttachment(depthAsColor) },
        };
        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in wrongFormat));

        using TestTexture singleSample = CreateColorTexture();
        using TestTexture multiSample = CreateColorTexture(
            sampleCount: ERHISampleCount.Count4,
            dimension: ERHITextureDimension.Texture2DMS);
        RHIRasterPassDescriptor sampleMismatch = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(singleSample),
                CreateColorAttachment(multiSample),
            },
        };
        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in sampleMismatch));

        using TestTexture differentExtent = CreateColorTexture(width: 8);
        RHIRasterPassDescriptor extentMismatch = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(singleSample),
                CreateColorAttachment(differentExtent),
            },
        };
        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in extentMismatch));

        RHIRasterPassDescriptor aspectMismatch = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(
                    singleSample,
                    new RHITextureSubresourceRange
                    {
                        MipLevelCount = 1,
                        ArrayLayerCount = 1,
                        AspectMask = ERHITextureAspectMask.Depth,
                    }),
            },
        };
        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in aspectMismatch));

        RHIRasterPassDescriptor layerMismatch = new RHIRasterPassDescriptor
        {
            ArrayLength = 2,
            ColorAttachments = new[] { CreateColorAttachment(singleSample) },
        };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RasterPassPlanner.Compile(in layerMismatch));
    }

    [Fact]
    public void Compile_ValidatesIndependentDepthStencilResolve()
    {
        using TestTexture source = CreateTexture(
            ERHIPixelFormat.D24_UNorm_S8_UInt,
            ERHISampleCount.Count4,
            ERHITextureUsage.DepthStencil,
            16,
            16,
            dimension: ERHITextureDimension.Texture2DMS);
        using TestTexture destination = CreateTexture(
            ERHIPixelFormat.D24_UNorm_S8_UInt,
            ERHISampleCount.None,
            ERHITextureUsage.ResolveTarget,
            16,
            16);
        RHIDepthStencilAttachmentDescriptor attachment =
            new RHIDepthStencilAttachmentDescriptor
            {
                DepthClearValue = 1f,
                DepthLoadOp = ERHILoadAction.Clear,
                DepthStoreOp = ERHIStoreAction.Resolve,
                StencilClearValue = 0,
                StencilLoadOp = ERHILoadAction.Clear,
                StencilStoreOp = ERHIStoreAction.StoreAndResolve,
                RenderTarget = source,
                DepthResolveMode = EResolveMode.Sample0,
                StencilResolveMode = EResolveMode.Min,
                ResolveTarget = destination,
            };
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            DepthStencilAttachment = attachment,
        };

        RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
        RHIDepthStencilAttachmentDescriptor normalized =
            plan.DescriptorSnapshot.DepthStencilAttachment!.Value;

        Assert.Equal(
            ERHITextureAspectMask.Depth | ERHITextureAspectMask.Stencil,
            normalized.SubresourceRange.AspectMask);
        Assert.Equal(EResolveMode.Sample0, normalized.DepthResolveMode);
        Assert.Equal(EResolveMode.Min, normalized.StencilResolveMode);
        Assert.Same(destination, normalized.ResolveTarget);

        attachment.DepthResolveMode = EResolveMode.None;
        descriptor.DepthStencilAttachment = attachment;
        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in descriptor));
    }

    [Fact]
    public void Compile_DerivesInterfacePreserveAndTransitionMasks()
    {
        using TestTexture ordered = CreateColorTexture(
            usage: ERHITextureUsage.RenderTarget |
                   ERHITextureUsage.RasterizerOrdered);
        using TestTexture feedback = CreateColorTexture(
            usage: ERHITextureUsage.RenderTarget |
                   ERHITextureUsage.ShaderResource);
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(
                    ordered,
                    access: ERHIRasterAttachmentAccess.RasterOrderedReadWrite),
                CreateColorAttachment(
                    feedback,
                    loadAction: ERHILoadAction.Load),
            },
            SubPassDescriptors = new[]
            {
                CreateSubPass(outputs: new[] { 0 }),
                CreateSubPass(
                    inputs: new[] { 0 },
                    outputs: new[] { 0 },
                    sampledFeedback: new[] { 1 }),
            },
        };

        RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
        ref readonly RasterSubPassPlan first = ref plan.GetSubPass(0);
        ref readonly RasterSubPassPlan second = ref plan.GetSubPass(1);

        Assert.Equal((byte)2, first.PreserveMask);
        Assert.Equal((byte)0, first.TransitionMask);
        Assert.Equal((byte)3, second.ReadMask);
        Assert.Equal((byte)1, second.WriteMask);
        Assert.Equal((byte)1, second.ReadWriteMask);
        Assert.Equal((byte)1, second.TransitionMask);
        Assert.Equal((byte)3, second.AvailableOnEntryMask);
        Assert.Equal((byte)1, second.AttachmentInterface.ColorInputMask);
        Assert.Equal((byte)1, second.AttachmentInterface.ColorOutputMask);
        Assert.Equal(
            (byte)1,
            second.AttachmentInterface.RasterOrderedReadWriteMask);
        Assert.Equal((byte)2, second.AttachmentInterface.SampledFeedbackMask);
        Assert.Equal((byte)0, second.AttachmentInterface.LayeredAccessMask);
    }
    [Fact]
    public void Compile_DerivesLayeredAccessOnlyForSpecialAttachmentReads()
    {
        using TestTexture ordered = CreateColorTexture(
            layers: 2,
            usage: ERHITextureUsage.RenderTarget |
                   ERHITextureUsage.RasterizerOrdered,
            dimension: ERHITextureDimension.Texture2DArray);
        using TestTexture feedback = CreateColorTexture(
            layers: 2,
            usage: ERHITextureUsage.RenderTarget |
                   ERHITextureUsage.ShaderResource,
            dimension: ERHITextureDimension.Texture2DArray);
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            ArrayLength = 2,
            ColorAttachments = new[]
            {
                CreateColorAttachment(
                    ordered,
                    access: ERHIRasterAttachmentAccess.RasterOrderedReadWrite),
                CreateColorAttachment(feedback),
            },
            SubPassDescriptors = new[]
            {
                CreateSubPass(
                    inputs: new[] { 0 },
                    outputs: new[] { 0 },
                    sampledFeedback: new[] { 1 }),
            },
        };

        RHIAttachmentInterfaceSignature signature =
            RasterPassPlanner.Compile(in descriptor)
                .GetSubPass(0)
                .AttachmentInterface;

        Assert.Equal((byte)3, signature.LayeredAccessMask);
        Assert.Equal((byte)1, signature.RasterOrderedReadWriteMask);
        Assert.Equal((byte)2, signature.SampledFeedbackMask);
    }


    [Fact]
    public void Compile_PreservesSparseOrderedAttachmentSlots()
    {
        using TestTexture output = CreateColorTexture();
        using TestTexture sampled = CreateColorTexture(
            usage: ERHITextureUsage.RenderTarget |
                   ERHITextureUsage.ShaderResource);
        using TestTexture input = CreateColorTexture();
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[]
            {
                CreateColorAttachment(output),
                CreateColorAttachment(sampled),
                CreateColorAttachment(input),
            },
            SubPassDescriptors = new[]
            {
                CreateSubPass(
                    inputs: new[] { -1, 2 },
                    outputs: new[] { -1, 0 },
                    sampledFeedback: new[] { -1, -1, 1 }),
            },
        };

        RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
        RHIAttachmentInterfaceSignature signature =
            plan.GetSubPass(0).AttachmentInterface;

        Assert.Equal(2, signature.ColorInputSlotCount);
        Assert.Equal(2, signature.ColorOutputLocationCount);
        Assert.Equal(3, signature.SampledFeedbackSlotCount);
        Assert.Equal(-1, signature.GetColorInputLogicalAttachment(0));
        Assert.Equal(2, signature.GetColorInputLogicalAttachment(1));
        Assert.Equal(-1, signature.GetColorOutputLogicalAttachment(0));
        Assert.Equal(0, signature.GetColorOutputLogicalAttachment(1));
        Assert.Equal(-1, signature.GetSampledFeedbackLogicalAttachment(0));
        Assert.Equal(-1, signature.GetSampledFeedbackLogicalAttachment(1));
        Assert.Equal(1, signature.GetSampledFeedbackLogicalAttachment(2));
        Assert.Equal((byte)4, signature.ColorInputMask);
        Assert.Equal((byte)1, signature.ColorOutputMask);
        Assert.Equal((byte)2, signature.SampledFeedbackMask);
    }

    [Fact]
    public void Compile_RejectsDuplicateAndInvalidSparseAttachmentSlots()
    {
        using TestTexture first = CreateColorTexture();
        using TestTexture second = CreateColorTexture();
        RHIColorAttachmentDescriptor[] attachments =
        {
            CreateColorAttachment(first),
            CreateColorAttachment(second),
        };
        RHIRasterPassDescriptor duplicate = new RHIRasterPassDescriptor
        {
            ColorAttachments = attachments,
            SubPassDescriptors = new[]
            {
                CreateSubPass(inputs: new[] { 0, -1, 0 }),
            },
        };
        RHIRasterPassDescriptor invalidNegative = new RHIRasterPassDescriptor
        {
            ColorAttachments = attachments,
            SubPassDescriptors = new[]
            {
                CreateSubPass(outputs: new[] { -2 }),
            },
        };
        RHIRasterPassDescriptor outOfRange = new RHIRasterPassDescriptor
        {
            ColorAttachments = attachments,
            SubPassDescriptors = new[]
            {
                CreateSubPass(sampledFeedback: new[] { -1, 2 }),
            },
        };

        Assert.Throws<ArgumentException>(
            () => RasterPassPlanner.Compile(in duplicate));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RasterPassPlanner.Compile(in invalidNegative));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RasterPassPlanner.Compile(in outOfRange));
    }

    [Fact]
    public void AttachmentSignature_IsOrderedDeepValueAndDefinesLocalInputBinding()
    {
        RHIAttachmentIndexArray inputs =
            new RHIAttachmentIndexArray(new[] { -1, 2 });
        RHIAttachmentIndexArray outputs =
            new RHIAttachmentIndexArray(new[] { 1, -1, 0 });
        RHIAttachmentIndexArray sampled =
            new RHIAttachmentIndexArray(new[] { -1, 3 });
        RHIAttachmentInterfaceSignature signature =
            new RHIAttachmentInterfaceSignature(
                4,
                inputs,
                outputs,
                sampled);

        inputs[1] = 0;
        outputs[0] = 0;
        sampled[1] = 0;

        Assert.Equal(2, signature.GetColorInputLogicalAttachment(1));
        Assert.Equal(1, signature.GetColorOutputLogicalAttachment(0));
        Assert.Equal(-1, signature.GetColorOutputLogicalAttachment(1));
        Assert.Equal(0, signature.GetColorOutputLogicalAttachment(2));
        Assert.Equal(3, signature.GetSampledFeedbackLogicalAttachment(1));
        Assert.Equal(0, RHIAttachmentInterfaceSignature.GetPrivateInputAttachmentBinding(0));
        Assert.Equal(7, RHIAttachmentInterfaceSignature.GetPrivateInputAttachmentBinding(7));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => signature.GetColorInputLogicalAttachment(2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RHIAttachmentInterfaceSignature.GetPrivateInputAttachmentBinding(8));

        RHIAttachmentInterfaceSignature reordered =
            new RHIAttachmentInterfaceSignature(
                4,
                new RHIAttachmentIndexArray(new[] { 2, -1 }),
                new RHIAttachmentIndexArray(new[] { 1, -1, 0 }),
                new RHIAttachmentIndexArray(new[] { -1, 3 }));
        Assert.NotEqual(signature, reordered);

        RHIAttachmentInterfaceSignature dualSource =
            new RHIAttachmentInterfaceSignature(
                2,
                RHIAttachmentIndexArray.Empty,
                new RHIAttachmentIndexArray(new[] { 1 }),
                RHIAttachmentIndexArray.Empty,
                usesDualSourceColor: true);
        Assert.Equal(1, dualSource.GetColorOutputLogicalAttachment(0, 0));
        Assert.Equal(1, dualSource.GetColorOutputLogicalAttachment(0, 1));
        Assert.Equal(
            -1,
            signature.GetColorOutputLogicalAttachment(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => dualSource.GetColorOutputLogicalAttachment(0, 2));
        Assert.Throws<ArgumentException>(
            () => _ = new RHIAttachmentInterfaceSignature(
                2,
                RHIAttachmentIndexArray.Empty,
                new RHIAttachmentIndexArray(new[] { -1, 1 }),
                RHIAttachmentIndexArray.Empty,
                usesDualSourceColor: true));
        Assert.Throws<ArgumentException>(
            () => _ = new RHIAttachmentInterfaceSignature(
                2,
                RHIAttachmentIndexArray.Empty,
                new RHIAttachmentIndexArray(new[] { 0, 1 }),
                RHIAttachmentIndexArray.Empty,
                layeredAccessMask: 1));

        RHIAttachmentInterfaceSignature layered =
            new RHIAttachmentInterfaceSignature(
                4,
                new RHIAttachmentIndexArray(new[] { -1, 2 }),
                new RHIAttachmentIndexArray(new[] { 1, -1, 0 }),
                new RHIAttachmentIndexArray(new[] { -1, 3 }),
                layeredAccessMask: 12);
        Assert.False(signature.IsPassCompatibleWith(layered));
        Assert.False(layered.IsPassCompatibleWith(signature));
        Assert.NotEqual(signature, layered);
    }

    [Fact]
    public void NextSubPass_SteadyStateAllocatesNothingAndChecksBounds()
    {
        using TestTexture texture = CreateColorTexture();
        RHISubPassDescriptor[] warmupSubPasses =
        {
            CreateSubPass(outputs: new[] { 0 }),
            CreateSubPass(outputs: new[] { 0 }),
        };
        TestRasterEncoder warmup = new TestRasterEncoder();
        RHIRasterPassDescriptor warmupDescriptor = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { CreateColorAttachment(texture) },
            SubPassDescriptors = warmupSubPasses,
        };
        warmup.BeginPass(in warmupDescriptor);
        warmup.NextSubPass();
        warmup.Dispose();

        const int subPassCount = 128;
        RHISubPassDescriptor[] subPasses = new RHISubPassDescriptor[subPassCount];
        for (int i = 0; i < subPasses.Length; ++i)
        {
            subPasses[i] = CreateSubPass(outputs: new[] { 0 });
        }
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { CreateColorAttachment(texture) },
            SubPassDescriptors = subPasses,
        };
        TestRasterEncoder encoder = new TestRasterEncoder();
        encoder.BeginPass(in descriptor);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 1; i < subPassCount; ++i)
        {
            encoder.NextSubPass();
        }
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(allocatedBefore, allocatedAfter);
        Assert.Equal(subPassCount - 1, encoder.NextCoreCallCount);
        Assert.Equal(subPassCount - 1, encoder.CurrentSubPassIndex);
        Assert.Throws<InvalidOperationException>(encoder.NextSubPass);
        encoder.Dispose();
    }

    [Fact]
    public void NextSubPass_BackendFailureDoesNotAdvanceState()
    {
        using TestTexture texture = CreateColorTexture();
        RHIRasterPassDescriptor descriptor = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { CreateColorAttachment(texture) },
            SubPassDescriptors = new[]
            {
                CreateSubPass(outputs: new[] { 0 }),
                CreateSubPass(outputs: new[] { 0 }),
            },
        };
        TestRasterEncoder encoder = new TestRasterEncoder
        {
            ThrowOnNext = true,
        };
        encoder.BeginPass(in descriptor);

        Assert.Throws<NotSupportedException>(encoder.NextSubPass);
        Assert.Equal(0, encoder.CurrentSubPassIndex);

        encoder.ThrowOnNext = false;
        encoder.NextSubPass();
        Assert.Equal(1, encoder.CurrentSubPassIndex);
        encoder.Dispose();
    }

    [Fact]
    public void PipelineContract_DeepSnapshotsAndEncoderRejectsMismatch()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new RHIAttachmentInterfaceSignature(
                1,
                new RHIAttachmentIndexArray(new[] { 1 }),
                RHIAttachmentIndexArray.Empty,
                RHIAttachmentIndexArray.Empty));
        Assert.Throws<ArgumentException>(
            () => _ = new RHIAttachmentInterfaceSignature(
                1,
                new RHIAttachmentIndexArray(new[] { 0 }),
                RHIAttachmentIndexArray.Empty,
                RHIAttachmentIndexArray.Empty,
                rasterOrderedReadWriteMask: 1));

        ERHIPixelFormat[] sourceFormats = { ERHIPixelFormat.R8G8B8A8_UNorm };
        RHIRasterPipelineDescriptor sourceDescriptor =
            CreatePipelineDescriptor(sourceFormats);
        RHIRasterPipelineDescriptor snapshot =
            RHIRasterPipelineContract.SnapshotAndValidate(in sourceDescriptor);
        sourceFormats[0] = ERHIPixelFormat.B8G8R8A8_UNorm;

        Assert.Equal(ERHIPixelFormat.R8G8B8A8_UNorm, snapshot.ColorFormats[0]);
        Assert.Equal(1, snapshot.AttachmentInterface.ColorAttachmentCount);
        Assert.Equal((byte)1, snapshot.AttachmentInterface.ColorOutputMask);

        using TestTexture texture = CreateColorTexture();
        RHIRasterPassDescriptor pass = new RHIRasterPassDescriptor
        {
            ColorAttachments = new[] { CreateColorAttachment(texture) },
        };
        TestRasterEncoder encoder = new TestRasterEncoder();
        encoder.BeginPass(in pass);
        using TestRasterPipeline compatible = new TestRasterPipeline(
            CreatePipelineDescriptor(
                new[] { ERHIPixelFormat.R8G8B8A8_UNorm }));

        RHIRasterPipelineDescriptor publicSnapshot = compatible.Descriptor;
        publicSnapshot.ColorFormats[0] = ERHIPixelFormat.B8G8R8A8_UNorm;
        Assert.Equal(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            compatible.Descriptor.ColorFormats[0]);

        encoder.SetPipeline(compatible);
        encoder.Draw(3, 1, 0, 0);
        Assert.Equal(1, encoder.DrawCallCount);

        using TestRasterPipeline wrongSample = new TestRasterPipeline(
            CreatePipelineDescriptor(
                new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
                ERHISampleCount.Count4));
        Assert.Throws<ArgumentException>(() => encoder.SetPipeline(wrongSample));

        using TestRasterPipeline wrongFormat = new TestRasterPipeline(
            CreatePipelineDescriptor(
                new[] { ERHIPixelFormat.B8G8R8A8_UNorm }));
        Assert.Throws<ArgumentException>(() => encoder.SetPipeline(wrongFormat));

        using TestRasterPipeline wrongInterface = new TestRasterPipeline(
            CreatePipelineDescriptor(
                new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
                attachmentInterface: new RHIAttachmentInterfaceSignature(
                    1,
                    new RHIAttachmentIndexArray(new[] { 0 }),
                    new RHIAttachmentIndexArray(new[] { 0 }),
                    RHIAttachmentIndexArray.Empty)));
        Assert.Throws<ArgumentException>(() => encoder.SetPipeline(wrongInterface));

        compatible.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => encoder.Draw(3, 1, 0, 0));
        encoder.Dispose();
    }

    [Fact]
    public void PipelineDepthStencilContract_UsesEffectiveWritesAndSelectedAspects()
    {
        RHIAttachmentInterfaceSignature readOnlyDepth =
            CreateDepthStencilInterface(ERHISubPassFlags.ReadOnlyDepth);
        RHIAttachmentInterfaceSignature readOnlyStencil =
            CreateDepthStencilInterface(ERHISubPassFlags.ReadOnlyStencil);

        RHIDepthStencilStateDescriptor depthDisabledWithLatentMask =
            CreateDepthStencilState(
                depthEnable: false,
                depthWriteMask: true);
        RHIRasterPipelineDescriptor descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyDepth,
            depthFormat: ERHIPixelFormat.D32_Float,
            depthStencilState: depthDisabledWithLatentMask);
        RHIRasterPipelineDescriptor snapshot =
            RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
        Assert.False(
            RHIRasterPipelineContract.HasDepthWrites(
                in snapshot.RenderState.DepthStencilState));

        RHIDepthStencilStateDescriptor depthWrites =
            CreateDepthStencilState(
                depthEnable: true,
                depthWriteMask: true);
        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyDepth,
            depthFormat: ERHIPixelFormat.D32_Float,
            depthStencilState: depthWrites);
        Assert.Throws<ArgumentException>(
            () => RHIRasterPipelineContract.SnapshotAndValidate(in descriptor));

        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            depthFormat: ERHIPixelFormat.Unknown,
            depthStencilState: CreateDepthStencilState(depthEnable: true));
        Assert.Throws<ArgumentException>(
            () => RHIRasterPipelineContract.SnapshotAndValidate(in descriptor));

        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyStencil,
            depthFormat: ERHIPixelFormat.D32_Float);
        Assert.Throws<ArgumentException>(
            () => RHIRasterPipelineContract.SnapshotAndValidate(in descriptor));

        RHIDepthStencilStateDescriptor keepWithNonzeroWriteMask =
            CreateDepthStencilState(
                stencilEnable: true,
                stencilWriteMask: byte.MaxValue,
                stencilPassOperation: ERHIStencilOp.Keep);
        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyStencil,
            depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
            depthStencilState: keepWithNonzeroWriteMask);
        snapshot = RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
        Assert.False(
            RHIRasterPipelineContract.HasStencilWrites(
                in snapshot.RenderState.DepthStencilState));

        RHIDepthStencilStateDescriptor replaceWithNonzeroWriteMask =
            CreateDepthStencilState(
                stencilEnable: true,
                stencilWriteMask: byte.MaxValue,
                stencilPassOperation: ERHIStencilOp.Replace);
        Assert.True(
            RHIRasterPipelineContract.HasStencilWrites(
                in replaceWithNonzeroWriteMask));
        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyStencil,
            depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
            depthStencilState: replaceWithNonzeroWriteMask);
        Assert.Throws<ArgumentException>(
            () => RHIRasterPipelineContract.SnapshotAndValidate(in descriptor));

        RHIDepthStencilStateDescriptor replaceWithZeroWriteMask =
            CreateDepthStencilState(
                stencilEnable: true,
                stencilWriteMask: 0,
                stencilPassOperation: ERHIStencilOp.Replace);
        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            attachmentInterface: readOnlyStencil,
            depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
            depthStencilState: replaceWithZeroWriteMask);
        snapshot = RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
        Assert.False(
            RHIRasterPipelineContract.HasStencilWrites(
                in snapshot.RenderState.DepthStencilState));

        descriptor = CreatePipelineDescriptor(
            Array.Empty<ERHIPixelFormat>(),
            depthFormat: ERHIPixelFormat.D32_Float,
            depthStencilState: keepWithNonzeroWriteMask);
        Assert.Throws<ArgumentException>(
            () => RHIRasterPipelineContract.SnapshotAndValidate(in descriptor));
    }

    [Fact]
    public void PipelineDepthStencilContract_FailsClosedForUnknownEnumsWhenDisabled()
    {
        static void AssertRejected(
            RHIDepthStencilStateDescriptor state)
        {
            RHIRasterPipelineDescriptor descriptor =
                CreatePipelineDescriptor(
                    Array.Empty<ERHIPixelFormat>(),
                    depthStencilState: state);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RHIRasterPipelineContract.SnapshotAndValidate(
                    in descriptor));
        }

        RHIDepthStencilStateDescriptor state =
            CreateDepthStencilState();
        Assert.False(state.DepthEnable);
        Assert.False(state.StencilEnable);

        state.ComparisonMode = ERHIComparisonMode.Pending;
        AssertRejected(state);

        state = CreateDepthStencilState();
        RHIStencilStateDescriptor face = state.FrontFace;
        face.ComparisonMode = (ERHIComparisonMode)byte.MaxValue;
        state.FrontFace = face;
        AssertRejected(state);

        state = CreateDepthStencilState();
        face = state.BackFace;
        face.ComparisonMode = ERHIComparisonMode.Pending;
        state.BackFace = face;
        AssertRejected(state);

        state = CreateDepthStencilState();
        face = state.FrontFace;
        face.StencilPassOp = (ERHIStencilOp)0;
        state.FrontFace = face;
        AssertRejected(state);

        state = CreateDepthStencilState();
        face = state.BackFace;
        face.StencilDepthFailOp = ERHIStencilOp.Pending;
        state.BackFace = face;
        AssertRejected(state);

        state = CreateDepthStencilState();
        face = state.FrontFace;
        face.StencilFailOp = (ERHIStencilOp)byte.MaxValue;
        state.FrontFace = face;
        AssertRejected(state);
    }

    [Fact]
    public void EncoderDepthStencilContract_RevalidatesAtSetAndDraw()
    {
        using TestTexture texture = CreateTexture(
            ERHIPixelFormat.D24_UNorm_S8_UInt,
            ERHISampleCount.None,
            ERHITextureUsage.DepthStencil,
            16,
            16);
        RHIAttachmentInterfaceSignature readOnlyDepthStencil =
            CreateDepthStencilInterface(
                ERHISubPassFlags.ReadOnlyDepthStencil);
        RHIDepthStencilStateDescriptor readOnlyState =
            CreateDepthStencilState(
                depthEnable: true,
                depthWriteMask: false,
                stencilEnable: true,
                stencilWriteMask: byte.MaxValue,
                stencilPassOperation: ERHIStencilOp.Keep);
        using TestRasterPipeline pipeline = new TestRasterPipeline(
            CreatePipelineDescriptor(
                Array.Empty<ERHIPixelFormat>(),
                attachmentInterface: readOnlyDepthStencil,
                depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
                depthStencilState: readOnlyState));
        RHIRasterPassDescriptor readOnlyPass = new RHIRasterPassDescriptor
        {
            DepthStencilAttachment =
                CreateDepthStencilAttachment(
                    texture,
                    ERHITextureAspectMask.Depth |
                    ERHITextureAspectMask.Stencil),
            SubPassDescriptors = new[]
            {
                CreateSubPass(flags: ERHISubPassFlags.ReadOnlyDepthStencil),
            },
        };

        TestRasterEncoder encoder = new TestRasterEncoder();
        encoder.BeginPass(in readOnlyPass);
        encoder.SetPipeline(pipeline);
        encoder.Draw(3, 1, 0, 0);

        RHIDepthStencilStateDescriptor depthWriteState = readOnlyState;
        depthWriteState.DepthWriteMask = true;
        pipeline.SetDepthStencilStateForTest(in depthWriteState);
        Assert.Throws<ArgumentException>(
            () => encoder.Draw(3, 1, 0, 0));
        encoder.Dispose();

        pipeline.SetDepthStencilStateForTest(in readOnlyState);
        RHIDepthStencilStateDescriptor stencilWriteState = readOnlyState;
        RHIStencilStateDescriptor modifyingFace = stencilWriteState.FrontFace;
        modifyingFace.StencilPassOp = ERHIStencilOp.Replace;
        stencilWriteState.FrontFace = modifyingFace;
        pipeline.SetDepthStencilStateForTest(in stencilWriteState);
        TestRasterEncoder setEncoder = new TestRasterEncoder();
        setEncoder.BeginPass(in readOnlyPass);
        Assert.Throws<ArgumentException>(
            () => setEncoder.SetPipeline(pipeline));
        setEncoder.Dispose();

        RHIRasterPassDescriptor depthOnlyPass = new RHIRasterPassDescriptor
        {
            DepthStencilAttachment =
                CreateDepthStencilAttachment(
                    texture,
                    ERHITextureAspectMask.Depth),
            SubPassDescriptors = new[]
            {
                CreateSubPass(flags: ERHISubPassFlags.ReadOnlyDepth),
            },
        };
        RHIDepthStencilStateDescriptor depthReadState =
            CreateDepthStencilState(
                depthEnable: true,
                depthWriteMask: false);
        using TestRasterPipeline depthOnlyPipeline = new TestRasterPipeline(
            CreatePipelineDescriptor(
                Array.Empty<ERHIPixelFormat>(),
                attachmentInterface: CreateDepthStencilInterface(
                    ERHISubPassFlags.ReadOnlyDepth),
                depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
                depthStencilState: depthReadState));
        TestRasterEncoder depthOnlyEncoder = new TestRasterEncoder();
        depthOnlyEncoder.BeginPass(in depthOnlyPass);
        depthOnlyEncoder.SetPipeline(depthOnlyPipeline);
        depthOnlyEncoder.Draw(3, 1, 0, 0);
        depthOnlyEncoder.Dispose();

        RHIRasterPassDescriptor stencilOnlyPass =
            new RHIRasterPassDescriptor
            {
                DepthStencilAttachment =
                    CreateDepthStencilAttachment(
                        texture,
                        ERHITextureAspectMask.Stencil),
                SubPassDescriptors = new[]
                {
                    CreateSubPass(
                        flags: ERHISubPassFlags.ReadOnlyStencil),
                },
            };
        RHIDepthStencilStateDescriptor stencilReadState =
            CreateDepthStencilState(
                stencilEnable: true,
                stencilWriteMask: byte.MaxValue,
                stencilPassOperation: ERHIStencilOp.Keep);
        using TestRasterPipeline stencilOnlyPipeline =
            new TestRasterPipeline(
                CreatePipelineDescriptor(
                    Array.Empty<ERHIPixelFormat>(),
                    attachmentInterface: CreateDepthStencilInterface(
                        ERHISubPassFlags.ReadOnlyStencil),
                    depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
                    depthStencilState: stencilReadState));
        TestRasterEncoder stencilOnlyEncoder = new TestRasterEncoder();
        stencilOnlyEncoder.BeginPass(in stencilOnlyPass);
        stencilOnlyEncoder.SetPipeline(stencilOnlyPipeline);
        stencilOnlyEncoder.Draw(3, 1, 0, 0);
        stencilOnlyEncoder.Dispose();

        using TestRasterPipeline incompatibleStencilPipeline =
            new TestRasterPipeline(
                CreatePipelineDescriptor(
                    Array.Empty<ERHIPixelFormat>(),
                    attachmentInterface: CreateDepthStencilInterface(
                        ERHISubPassFlags.ReadOnlyDepth),
                    depthFormat: ERHIPixelFormat.D24_UNorm_S8_UInt,
                    depthStencilState: readOnlyState));
        TestRasterEncoder aspectEncoder = new TestRasterEncoder();
        aspectEncoder.BeginPass(in depthOnlyPass);
        Assert.Throws<ArgumentException>(
            () => aspectEncoder.SetPipeline(
                incompatibleStencilPipeline));
        aspectEncoder.Dispose();
    }

    [Fact]
    public void PublicApi_ExposesOnlyLockedRasterContracts()
    {
        Assembly assembly = typeof(RHIRasterPassDescriptor).Assembly;
        string[] forbiddenTypeNames =
        {
            "RHIRasterPassLayout",
            "RHIRasterPassBindings",
            "RHISubPassDependencyDescriptor",
        };
        foreach (string forbiddenTypeName in forbiddenTypeNames)
        {
            Assert.Null(assembly.GetType($"SharpGPU.{forbiddenTypeName}"));
        }

        foreach (Type type in assembly.GetExportedTypes())
        {
            foreach (MemberInfo member in type.GetMembers(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                Assert.False(
                    ExposesForbiddenType(member, forbiddenTypeNames),
                    $"{type.FullName}.{member.Name} exposes a forbidden raster abstraction.");
            }
        }

        Type colorAttachment = typeof(RHIColorAttachmentDescriptor);
        Assert.Null(colorAttachment.GetField("MipLevel"));
        Assert.Null(colorAttachment.GetField("ArraySlice"));
        Assert.Null(colorAttachment.GetField("ResolveMipLevel"));
        Assert.Null(colorAttachment.GetField("ResolveArraySlice"));
        Assert.NotNull(colorAttachment.GetField("SubresourceRange"));
        Assert.NotNull(colorAttachment.GetField("ResolveSubresourceRange"));

        Type depthStencil = typeof(RHIDepthStencilAttachmentDescriptor);
        Assert.Null(depthStencil.GetField("ResolveMode"));
        Assert.NotNull(depthStencil.GetField("DepthResolveMode"));
        Assert.NotNull(depthStencil.GetField("StencilResolveMode"));

        Assert.True(typeof(RHIAttachmentInterfaceSignature).IsPublic);
        Assert.True(typeof(RHIAttachmentInterfaceSignature).IsValueType);
        Assert.DoesNotContain(
            typeof(RHIAttachmentInterfaceSignature).GetMembers(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly),
            member =>
                member.Name.Contains(
                    "Private",
                    StringComparison.OrdinalIgnoreCase) ||
                member.Name.Contains(
                    "Physical",
                    StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(typeof(ERHISubPassFlags).GetCustomAttribute<FlagsAttribute>());
        Assert.NotNull(
            typeof(ERHIRasterAttachmentAccess).GetCustomAttribute<FlagsAttribute>());
    }

    private static bool ExposesForbiddenType(
        MemberInfo member,
        string[] forbiddenTypeNames)
    {
        switch (member)
        {
            case FieldInfo field:
                return IsForbidden(field.FieldType, forbiddenTypeNames);
            case PropertyInfo property:
                return IsForbidden(property.PropertyType, forbiddenTypeNames);
            case EventInfo eventInfo:
                return IsForbidden(eventInfo.EventHandlerType, forbiddenTypeNames);
            case MethodInfo method:
                if (IsForbidden(method.ReturnType, forbiddenTypeNames))
                {
                    return true;
                }
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    if (IsForbidden(parameter.ParameterType, forbiddenTypeNames))
                    {
                        return true;
                    }
                }
                return false;
            case ConstructorInfo constructor:
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    if (IsForbidden(parameter.ParameterType, forbiddenTypeNames))
                    {
                        return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    private static bool IsForbidden(Type? type, string[] forbiddenTypeNames)
    {
        if (type == null)
        {
            return false;
        }
        if (type.IsByRef || type.IsPointer || type.IsArray)
        {
            return IsForbidden(type.GetElementType(), forbiddenTypeNames);
        }
        foreach (string forbiddenTypeName in forbiddenTypeNames)
        {
            if (string.Equals(type.Name, forbiddenTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                if (IsForbidden(argument, forbiddenTypeNames))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static RHIColorAttachmentDescriptor CreateColorAttachment(
        RHITexture texture,
        RHITextureSubresourceRange subresourceRange = default,
        ERHILoadAction loadAction = ERHILoadAction.Clear,
        ERHIRasterAttachmentAccess access = ERHIRasterAttachmentAccess.None)
    {
        return new RHIColorAttachmentDescriptor
        {
            RenderTarget = texture,
            SubresourceRange = subresourceRange,
            LoadAction = loadAction,
            StoreAction = ERHIStoreAction.Store,
            Access = access,
        };
    }

    private static RHIDepthStencilAttachmentDescriptor
        CreateDepthStencilAttachment(
            RHITexture texture,
            ERHITextureAspectMask aspects)
    {
        return new RHIDepthStencilAttachmentDescriptor
        {
            RenderTarget = texture,
            SubresourceRange = new RHITextureSubresourceRange
            {
                AspectMask = aspects,
            },
            DepthLoadOp = ERHILoadAction.Load,
            DepthStoreOp = ERHIStoreAction.Store,
            StencilLoadOp = ERHILoadAction.Load,
            StencilStoreOp = ERHIStoreAction.Store,
        };
    }

    private static RHIAttachmentInterfaceSignature
        CreateDepthStencilInterface(ERHISubPassFlags flags)
    {
        return new RHIAttachmentInterfaceSignature(
            0,
            RHIAttachmentIndexArray.Empty,
            RHIAttachmentIndexArray.Empty,
            RHIAttachmentIndexArray.Empty,
            depthStencilFlags: flags);
    }

    private static RHIDepthStencilStateDescriptor CreateDepthStencilState(
        bool depthEnable = false,
        bool depthWriteMask = false,
        bool stencilEnable = false,
        byte stencilWriteMask = 0,
        ERHIStencilOp stencilPassOperation = ERHIStencilOp.Keep)
    {
        RHIStencilStateDescriptor face = new RHIStencilStateDescriptor
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
            StencilPassOp = stencilPassOperation,
        };
        return new RHIDepthStencilStateDescriptor
        {
            DepthEnable = depthEnable,
            DepthWriteMask = depthWriteMask,
            ComparisonMode = ERHIComparisonMode.Always,
            StencilEnable = stencilEnable,
            StencilReadMask = byte.MaxValue,
            StencilWriteMask = stencilWriteMask,
            FrontFace = face,
            BackFace = face,
        };
    }

    private static RHISubPassDescriptor CreateSubPass(
        int[]? inputs = null,
        int[]? outputs = null,
        int[]? sampledFeedback = null,
        ERHISubPassFlags flags = ERHISubPassFlags.None)
    {
        return new RHISubPassDescriptor
        {
            Flags = flags,
            ColorInputs = inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
            SampledFeedbackInputs = sampledFeedback == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(sampledFeedback),
        };
    }

    private static RHIRasterPipelineDescriptor CreatePipelineDescriptor(
        ERHIPixelFormat[] formats,
        ERHISampleCount sampleCount = ERHISampleCount.None,
        RHIAttachmentInterfaceSignature attachmentInterface = default,
        ERHIPixelFormat depthFormat = ERHIPixelFormat.Unknown,
        RHIDepthStencilStateDescriptor? depthStencilState = null)
    {
        return new RHIRasterPipelineDescriptor
        {
            SampleCount = sampleCount,
            DepthFormat = depthFormat,
            ColorFormats = formats,
            AttachmentInterface = attachmentInterface,
            FragmentFunction = null,
            PipelineLayout = null,
            RenderState = new RHIRenderStateDescriptor
            {
                DepthStencilState =
                    depthStencilState ?? CreateDepthStencilState(),
            },
        };
    }

    private static TestTexture CreateColorTexture(
        uint width = 16,
        uint height = 16,
        uint layers = 1,
        uint mipCount = 1,
        ERHISampleCount sampleCount = ERHISampleCount.None,
        ERHITextureUsage usage = ERHITextureUsage.RenderTarget,
        ERHITextureDimension dimension = ERHITextureDimension.Texture2D)
    {
        return CreateTexture(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            sampleCount,
            usage,
            width,
            height,
            layers,
            mipCount,
            dimension);
    }

    private static TestTexture CreateTexture(
        ERHIPixelFormat format,
        ERHISampleCount sampleCount,
        ERHITextureUsage usage,
        uint width,
        uint height,
        uint layers = 1,
        uint mipCount = 1,
        ERHITextureDimension dimension = ERHITextureDimension.Texture2D)
    {
        return new TestTexture(new RHITextureDescriptor
        {
            MipCount = mipCount,
            Extent = new uint3(width, height, layers),
            Format = format,
            SampleCount = sampleCount,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = usage,
            Dimension = dimension,
        });
    }

    private sealed class TestTexture : RHITexture
    {
        internal TestTexture(in RHITextureDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        public override RHITextureView CreateTextureView(
            in RHITextureViewDescriptor descriptor)
        {
            throw new NotSupportedException();
        }

        protected override void Release()
        {
        }
    }

    private sealed class TestRasterPipeline : RHIRasterPipeline
    {
        internal TestRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            m_Descriptor =
                RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
        }

        internal void SetDepthStencilStateForTest(
            in RHIDepthStencilStateDescriptor state)
        {
            m_Descriptor.RenderState.DepthStencilState = state;
        }

        protected override void Release()
        {
        }
    }

    private sealed class TestRasterEncoder : RHIRasterEncoder
    {
        internal bool ThrowOnNext { get; set; }
        internal int NextCoreCallCount { get; private set; }
        internal int DrawCallCount { get; private set; }

        internal override void BeginPass(in RHIRasterPassDescriptor descriptor)
        {
            ThrowIfDisposed();
            if (m_RasterPassPlan != null)
            {
                throw new InvalidOperationException("A raster pass is already active on this encoder.");
            }

            RasterPassPlan plan = RasterPassPlanner.Compile(in descriptor);
            m_RasterPassPlan = plan;
            m_CurrentSubPassIndex = 0;
            m_PipelineSubPassIndex = -1;
            m_CachedPipeline = null;
        }

        public override void Barrier(in RHIBarrier barrier)
        {
        }

        public override void Barriers(ReadOnlySpan<RHIBarrier> barriers)
        {
        }

        public override void PushDebugGroup(string name)
        {
        }

        public override void PopDebugGroup()
        {
        }

        public override void WriteTimestamp(in uint index)
        {
        }

        public override void BeginOcclusion(in uint index)
        {
        }

        public override void EndOcclusion(in uint index)
        {
        }

        public override void BeginStatistics(in uint index)
        {
        }

        public override void EndStatistics(in uint index)
        {
        }

        public override void NextSubPass()
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            int nextSubPassIndex = m_CurrentSubPassIndex + 1;
            if (nextSubPassIndex >= plan.SubPassCount)
            {
                throw new InvalidOperationException(
                    $"Raster pass '{plan.Name}' has no subpass after index {m_CurrentSubPassIndex}.");
            }

            if (ThrowOnNext)
            {
                throw new NotSupportedException("test backend rejection");
            }

            NextCoreCallCount++;
            m_CurrentSubPassIndex = nextSubPassIndex;
            m_PipelineSubPassIndex = -1;
        }

        public override void SetScissor(in Rect rect)
        {
        }

        public override void SetScissors(in Memory<Rect> rects)
        {
        }

        public override void SetViewport(in Viewport viewport)
        {
        }

        public override void SetViewports(in Memory<Viewport> viewports)
        {
        }

        public override void SetStencilRef(in uint value)
        {
        }

        public override void SetBlendFactor(in float4 value)
        {
        }

        public override void SetPipeline(RHIRasterPipeline pipeline)
        {
            ThrowIfDisposed();
            RasterPassPlan plan = RequireActiveRasterPass();
            ValidatePipelineCompatibility(plan, m_CurrentSubPassIndex, pipeline);
            m_CachedPipeline = pipeline;
            m_PipelineSubPassIndex = m_CurrentSubPassIndex;
        }

        public override void SetBindingTable(
            RHIBindingTable resourceTable,
            in uint tableIndex)
        {
        }

        public override void SetPushConstants(
            IntPtr data,
            in uint size,
            in uint offset = 0)
        {
        }

        public override void SetIndexBuffer(RHIBuffer buffer, in uint offset)
        {
        }

        public override void SetVertexBuffer(
            RHIBuffer buffer,
            in uint slot,
            in uint offset)
        {
        }

        public override void SetShadingRate(
            in ERHIShadingRate shadingRate,
            in ERHIShadingRateCombiner shadingRateCombiner)
        {
        }

        public override void Draw(
            in uint vertexCount,
            in uint instanceCount,
            in uint firstVertex,
            in uint firstInstance)
        {
            ValidateDrawState();
            DrawCallCount++;
        }

        public override void DrawIndexed(
            in uint indexCount,
            in uint instanceCount,
            in uint firstIndex,
            in uint baseVertex,
            in uint firstInstance)
        {
            ValidateDrawState();
        }

        public override void DrawIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            ValidateDrawState();
        }

        public override void DrawIndexedIndirect(
            RHIBuffer argsBuffer,
            in uint offset,
            in uint drawCount)
        {
            ValidateDrawState();
        }

        public override void DispatchMesh(
            in uint groupCountX,
            in uint groupCountY,
            in uint groupCountZ)
        {
            ValidateDrawState();
        }

        public override void DispatchMeshIndirect(
            RHIBuffer argsBuffer,
            in uint argsOffset)
        {
            ValidateDrawState();
        }

        public override void ExecuteIndirectCommandBuffer(
            RHIRasterIndirectCommandBuffer indirectCmdBuffer)
        {
            ValidateDrawState();
        }

        public override void EndPass()
        {
            ClearRasterPassState();
        }

        protected override void Release()
        {
        }
    }
}
