#if SHARPGPU_ENABLE_DX12
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpGPU;
using SharpGPU.Mathematics;
using SharpShader.HLSLCrossCompiler;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class Dx12RasterSubpassQualifiedTests
{
    [Theory]
    [InlineData(false, false, ERHISampleCount.None, "Texture2D")]
    [InlineData(false, true, ERHISampleCount.None, "Texture2DArray")]
    [InlineData(false, false, ERHISampleCount.Count4, "Texture2DMS")]
    [InlineData(false, true, ERHISampleCount.Count4, "Texture2DMSArray")]
    [InlineData(true, false, ERHISampleCount.None,
        "RasterizerOrderedTexture2D")]
    [InlineData(true, true, ERHISampleCount.None,
        "RasterizerOrderedTexture2DArray")]
    [Trait("Category", "SharpGpuPortable")]
    public void RawAttachmentAbi_ChoosesExactTextureShape(
        bool readWrite,
        bool layered,
        ERHISampleCount sampleCount,
        string expected)
    {
        Assert.Equal(
            expected,
            Dx12Device.GetRawAttachmentTextureType(
                readWrite,
                layered,
                sampleCount));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void RawAttachmentAbi_RejectsMultisampledRovShape()
    {
        Assert.Throws<NotSupportedException>(() =>
            Dx12Device.GetRawAttachmentTextureType(
                readWrite: true,
                layered: false,
                ERHISampleCount.Count2));
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Lowering_FreezesPerPhaseTables_AndUsesExactPrivateAbiOffsets()
    {
        using PlannerTexture ordered = CreateTexture(
            ERHIPixelFormat.B8G8R8A8_UNorm,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.UnorderedAccess);
        using PlannerTexture sampled = CreateTexture(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.ShaderResource);
        using PlannerTexture local = CreateTexture(
            ERHIPixelFormat.R16G16B16A16_Float,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget);

        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.Lowering.ExactPrivateAbi",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                Attachment(
                    ordered,
                    ERHILoadAction.Load),
                Attachment(sampled, ERHILoadAction.Load),
                Attachment(local, ERHILoadAction.Load),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(
                    inputs: new[] { -1, 2, 0, 1 },
                    outputs: new[] { -1, 0 }),
                SubPass(
                    inputs: new[] { 0, -1, 2 },
                    outputs: new[] { 1 }),
            },
        };

        RHIRasterPassPlan passPlan = RHIRasterPassPlanner.Compile(in descriptor);
        Dx12RasterPassLowering lowering =
            Dx12RasterPassLowering.Compile(
                passPlan,
                supportsNativeRenderPass: true,
                supportsRasterOrderedViews: true,
                usesEnhancedBarriers: true);

        Assert.Equal(EDx12RasterPassStrategy.OmMultipass, lowering.Strategy);
        Assert.True(lowering.RequiresPrivateAttachmentTable);
        Assert.Equal(2, lowering.SubPasses.Length);

        ref readonly Dx12RasterSubPassLowering first =
            ref lowering.SubPasses.Span[0];
        Assert.Equal(new[] { -1, 0 }, first.OutputLogicalAttachments.ToArray());
        Assert.Equal(new[] { -1, -1 }, first.RenderTargetLogicalAttachments.ToArray());
        Assert.Equal(new[] { -1, 2, -1, 1 }, first.PrivateInputLogicalAttachments.ToArray());
        Assert.Equal(
            new[]
            {
                ERHIPixelFormat.B8G8R8A8_UNorm,
                ERHIPixelFormat.B8G8R8A8_UNorm,
            },
            first.OutputLocationFormats.ToArray());
        Assert.Equal((byte)((1 << 1) | (1 << 2)), first.PrivateShaderResourceMask);
        Assert.Equal((byte)(1 << 0), first.RasterOrderedMask);
        Assert.Equal(first.ShaderResourceMask, first.PrivateShaderResourceMask);

        Assert.Equal(0,
            Dx12RasterPassLowering.GetPrivateInputDescriptorOffset(0, 0));
        Assert.Equal(7,
            Dx12RasterPassLowering.GetPrivateInputDescriptorOffset(0, 7));
        Assert.Equal(8,
            Dx12RasterPassLowering.GetRasterOrderedDescriptorOffset(0, 0));
        Assert.Equal(15,
            Dx12RasterPassLowering.GetRasterOrderedDescriptorOffset(0, 7));
        Assert.Equal(16,
            Dx12RasterPassLowering.GetPrivateDescriptorTableOffset(1));
        Assert.Equal(18,
            Dx12RasterPassLowering.GetPrivateInputDescriptorOffset(1, 2));
        Assert.Equal(31,
            Dx12RasterPassLowering.GetRasterOrderedDescriptorOffset(1, 7));

        Assert.True(MemoryMarshal.TryGetArray(
            first.OutputLogicalAttachments,
            out ArraySegment<int> outputBackingA));
        Assert.True(MemoryMarshal.TryGetArray(
            first.OutputLogicalAttachments,
            out ArraySegment<int> outputBackingB));
        Assert.Same(outputBackingA.Array, outputBackingB.Array);

        Assert.True(MemoryMarshal.TryGetArray(
            first.PrivateInputLogicalAttachments,
            out ArraySegment<int> inputBackingA));
        Assert.True(MemoryMarshal.TryGetArray(
            first.PrivateInputLogicalAttachments,
            out ArraySegment<int> inputBackingB));
        Assert.Same(inputBackingA.Array, inputBackingB.Array);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Lowering_InfersOrderingButRequiresGenericWritableAllocation()
    {
        using PlannerTexture readOnlyAllocation = CreateTexture(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget);
        RHIRasterPassDescriptor readOnlyDescriptor = new()
        {
            Name = "Dx12.Lowering.ImplicitOrdering.ReadOnlyAllocation",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                Attachment(readOnlyAllocation, ERHILoadAction.Load),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(inputs: new[] { 0 }, outputs: new[] { 0 }),
            },
        };
        RHIRasterPassPlan readOnlyPlan =
            RHIRasterPassPlanner.Compile(in readOnlyDescriptor);
        NotSupportedException missingUsage =
            Assert.Throws<NotSupportedException>(() =>
                Dx12RasterPassLowering.Compile(
                    readOnlyPlan,
                    supportsNativeRenderPass: true,
                    supportsRasterOrderedViews: true,
                    usesEnhancedBarriers: true));
        Assert.Contains(
            nameof(ERHITextureUsage.UnorderedAccess),
            missingUsage.Message,
            StringComparison.Ordinal);

        using PlannerTexture writableAllocation = CreateTexture(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.UnorderedAccess);
        RHIRasterPassDescriptor writableDescriptor = new()
        {
            Name = "Dx12.Lowering.ImplicitOrdering.WritableAllocation",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                Attachment(writableAllocation, ERHILoadAction.Load),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(inputs: new[] { 0 }, outputs: new[] { 0 }),
            },
        };
        RHIRasterPassPlan writablePlan =
            RHIRasterPassPlanner.Compile(in writableDescriptor);
        Dx12RasterPassLowering lowering =
            Dx12RasterPassLowering.Compile(
                writablePlan,
                supportsNativeRenderPass: true,
                supportsRasterOrderedViews: true,
                usesEnhancedBarriers: true);
        Assert.Equal((byte)1, lowering.SubPasses.Span[0].RasterOrderedMask);
    }

    [Fact]
    [Trait("Category", "SharpGpuPortable")]
    public void Lowering_FailsClosed_ForUnrepresentableOutputAndRovContracts()
    {
        RHIAttachmentInterfaceSignature allHoleOutputs =
            new(
                1,
                RHIAttachmentIndexArray.Empty,
                new RHIAttachmentIndexArray(new[] { -1 }));
        Assert.Throws<ArgumentException>(() =>
            Dx12RasterSubPassLowering.ResolveOutputLocationFormats(
                in allHoleOutputs,
                new[] { ERHIPixelFormat.R8G8B8A8_UNorm }));

        using PlannerTexture orderedMsaa = CreateTexture(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.Count4,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.UnorderedAccess,
            ERHITextureDimension.Texture2DMS);
        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.Lowering.RovFailClosed",
            SampleCount = ERHISampleCount.Count4,
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                Attachment(
                    orderedMsaa,
                    ERHILoadAction.Load),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(inputs: new[] { 0 }, outputs: new[] { 0 }),
            },
        };
        RHIRasterPassPlan passPlan = RHIRasterPassPlanner.Compile(in descriptor);

        Assert.Throws<NotSupportedException>(() =>
            Dx12RasterPassLowering.Compile(
                passPlan,
                supportsNativeRenderPass: true,
                supportsRasterOrderedViews: false,
                usesEnhancedBarriers: true));
        Assert.Throws<NotSupportedException>(() =>
            Dx12RasterPassLowering.Compile(
                passPlan,
                supportsNativeRenderPass: true,
                supportsRasterOrderedViews: true,
                usesEnhancedBarriers: true));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Dx12RasterPassLowering.GetPrivateInputDescriptorOffset(0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Dx12RasterPassLowering.GetRasterOrderedDescriptorOffset(0, -1));

        const uint textureMipLevelCount = 8;
        const uint textureArrayLayerCount = 12;
        RHITextureSubresourceRange exactRange = new()
        {
            BaseMipLevel = 2,
            MipLevelCount = 3,
            BaseArrayLayer = 4,
            ArrayLayerCount = 5,
            AspectMask = ERHITextureAspectMask.Color,
        };
        Vortice.Direct3D12.BarrierSubresourceRange colorRange =
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Color);
        Assert.Equal(2u, colorRange.IndexOrFirstMipLevel);
        Assert.Equal(3u, colorRange.NumMipLevels);
        Assert.Equal(4u, colorRange.FirstArraySlice);
        Assert.Equal(5u, colorRange.NumArraySlices);
        Assert.Equal(0u, colorRange.FirstPlane);
        Assert.Equal(1u, colorRange.NumPlanes);

        exactRange.AspectMask = ERHITextureAspectMask.Depth;
        Vortice.Direct3D12.BarrierSubresourceRange depthRange =
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil);
        Assert.Equal(0u, depthRange.FirstPlane);
        Assert.Equal(1u, depthRange.NumPlanes);

        exactRange.AspectMask = ERHITextureAspectMask.Stencil;
        Vortice.Direct3D12.BarrierSubresourceRange stencilRange =
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil);
        Assert.Equal(1u, stencilRange.FirstPlane);
        Assert.Equal(1u, stencilRange.NumPlanes);

        exactRange.AspectMask =
            ERHITextureAspectMask.Depth |
            ERHITextureAspectMask.Stencil;
        Vortice.Direct3D12.BarrierSubresourceRange depthStencilRange =
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil);
        Assert.Equal(0u, depthStencilRange.FirstPlane);
        Assert.Equal(2u, depthStencilRange.NumPlanes);

        RHITextureSubresourceRange wholeStencil =
            RHITextureSubresourceRange.Whole(
                ERHITextureAspectMask.Stencil);
        Vortice.Direct3D12.BarrierSubresourceRange wholeStencilRange =
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in wholeStencil,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil);
        Assert.Equal(0u, wholeStencilRange.IndexOrFirstMipLevel);
        Assert.Equal(textureMipLevelCount, wholeStencilRange.NumMipLevels);
        Assert.Equal(0u, wholeStencilRange.FirstArraySlice);
        Assert.Equal(textureArrayLayerCount, wholeStencilRange.NumArraySlices);
        Assert.Equal(1u, wholeStencilRange.FirstPlane);
        Assert.Equal(1u, wholeStencilRange.NumPlanes);

        exactRange.AspectMask = ERHITextureAspectMask.None;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Color));

        exactRange.AspectMask = ERHITextureAspectMask.Color;
        Assert.Throws<ArgumentException>(() =>
            Dx12BarrierEmitter.ConvertToSubresourceRange(
                in exactRange,
                textureMipLevelCount,
                textureArrayLayerCount,
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil));
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Rtx5090_RasterSubpasses_DrawResolveAndOrderRov_WithZeroDebugErrors()
    {
        Assert.True(OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using Dx12QualifiedContext context =
            Dx12QualifiedContext.Create();
        Assert.Contains(
            "RTX 5090",
            context.Device.Name,
            StringComparison.OrdinalIgnoreCase);
        Vortice.Direct3D12.FeatureDataD3D12Options nativeOptions =
            default;
        Assert.True(
            context.Dx12Device.NativeDevice.CheckFeatureSupport(
                Vortice.Direct3D12.Feature.Options,
                ref nativeOptions),
            "RTX qualification requires D3D12_FEATURE_D3D12_OPTIONS.");
        bool nativeRovSupported = nativeOptions.ROVsSupported;
        Assert.Equal(
            nativeRovSupported,
            context.Device.Capabilities.Raster.FramebufferReadWrite.Tier !=
                ERHICapabilityTier.Unavailable);
        Assert.True(
            nativeRovSupported,
            "RTX 5090 native ROV probe must be available; no capability " +
            "override or lowering bypass is permitted.");
        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            context.Device.Capabilities.Raster.FramebufferReadWrite.Tier);
        Assert.Equal(
            ERHICapabilityProbeKind.NativeFeatureQuery,
            context.Device.Capabilities.Raster.FramebufferReadWrite
                .Provenance.Kind);

        RHIBlendDescriptor shaderOnlyBlend =
            CreateDefaultRenderState().BlendState.BlendDescriptor0;
        RHIRasterAttachmentSupportQuery shaderOnlyQuery = new(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.None,
            isInput: true,
            isOutput: true,
            in shaderOnlyBlend);
        Assert.NotEqual(
            ERHICapabilityTier.Unavailable,
            context.Device.QueryRasterAttachmentSupport(
                in shaderOnlyQuery).Tier);

        RHIBlendDescriptor doubleRmwBlend = shaderOnlyBlend;
        doubleRmwBlend.BlendEnable = true;
        doubleRmwBlend.SrcBlendColor = ERHIBlendMode.SrcAlpha;
        doubleRmwBlend.DstBlendColor = ERHIBlendMode.OneMinusSrcAlpha;
        RHIRasterAttachmentSupportQuery doubleRmwQuery = new(
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHISampleCount.None,
            isInput: true,
            isOutput: true,
            in doubleRmwBlend);
        RHICapability doubleRmwSupport =
            context.Device.QueryRasterAttachmentSupport(
                in doubleRmwQuery);
        Assert.Equal(
            ERHICapabilityTier.Unavailable,
            doubleRmwSupport.Tier);
        Assert.Contains(
            "hardware blend",
            doubleRmwSupport.UnavailableReason,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(doubleRmwBlend.BlendEnable);

        context.ClearDebugMessages();
        RunSparseOutputDraw(context);
        context.AssertDeviceAlive("sparse output-location draw");
        context.AssertNoDebugErrors("sparse output-location draw");

        RunMultisampleLocalReadAndResolve(context);
        context.AssertDeviceAlive("multisample local-read resolve");
        context.AssertNoDebugErrors("multisample local-read resolve");

        RunDepthStencilArrayLayerSequence(context);
        context.AssertDeviceAlive("depth-stencil array-layer sequence");
        context.AssertNoDebugErrors("depth-stencil array-layer sequence");

        RunRasterOrderedPhaseSequence(context);
        context.AssertDeviceAlive("ROV phase ordering");
        context.AssertNoDebugErrors("ROV phase ordering");
    }

    [Fact]
    [Trait("Category", "SharpGpuWindowsQualified")]
    public void Rtx5090_BeginRasterPassFailures_RollBackAllPrivateDescriptors()
    {
        Assert.True(OperatingSystem.IsWindows(),
            "SharpGpuWindowsQualified requires a Windows qualification host.");
        Assert.True(
            RHIInstance.IsBackendSupported(
                ERHIBackend.DirectX12,
                out string backendReason),
            backendReason);

        using Dx12QualifiedContext local =
            Dx12QualifiedContext.Create();
        using Dx12QualifiedContext foreign =
            Dx12QualifiedContext.Create();
        Assert.Contains(
            "RTX 5090",
            local.Device.Name,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "RTX 5090",
            foreign.Device.Name,
            StringComparison.OrdinalIgnoreCase);
        local.ClearDebugMessages();

        using RHITexture localColor = CreateGpuTexture(
            local.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget);
        using RHITexture foreignDepth = CreateGpuTexture(
            foreign.Device,
            ERHISampleCount.None,
            ERHITextureUsage.DepthStencil,
            format: ERHIPixelFormat.D32_Float);
        RHIRasterPassDescriptor foreignDepthPass = new()
        {
            Name = "Dx12.BeginRollback.ForeignDepth",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(localColor),
            },
            DepthStencilAttachment = new RHIDepthStencilAttachmentDescriptor
            {
                RenderTarget = foreignDepth,
                DepthLoadOp = ERHILoadAction.Load,
                DepthStoreOp = ERHIStoreAction.Store,
                StencilLoadOp = ERHILoadAction.DontCare,
                StencilStoreOp = ERHIStoreAction.DontCare,
            },
        };

        int rtvBefore = local.Dx12Device
            .DescriptorHeapHeapRTV.AvailableDescriptorCount;
        int dsvBefore = local.Dx12Device
            .DescriptorHeapDSV.AvailableDescriptorCount;
        using (RHICommandBuffer command =
            local.Queue.CreateCommandBuffer())
        {
            command.Begin("Dx12.BeginRollback.ForeignDepth");
            Assert.Throws<ArgumentException>(() =>
                command.BeginRasterPass(in foreignDepthPass));
            Assert.Equal(
                rtvBefore,
                local.Dx12Device.DescriptorHeapHeapRTV
                    .AvailableDescriptorCount);
            Assert.Equal(
                dsvBefore,
                local.Dx12Device.DescriptorHeapDSV
                    .AvailableDescriptorCount);
            command.End();
        }

        using RHITexture ordered = CreateGpuTexture(
            local.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.UnorderedAccess);
        RHIRasterPassDescriptor privateAllocationPass = new()
        {
            Name = "Dx12.BeginRollback.PrivateDescriptorExhaustion",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(
                    ordered,
                    ERHILoadAction.Load),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(
                    inputs: new[] { 0 },
                    outputs: new[] { -1, 0 }),
            },
        };

        int shaderVisibleBefore = local.Dx12Device
            .DescriptorHeapCbvSrvUav.AvailableDescriptorCount;
        Assert.True(
            shaderVisibleBefore >
                Dx12RasterPassLowering
                    .PrivateDescriptorCountPerSubPass,
            $"DX12 shader-visible heap is unexpectedly small: {shaderVisibleBefore}.");
        int retainedCount = shaderVisibleBefore - 8;
        Dx12DescriptorInfo retained = local.Dx12Device
            .AllocateCbvSrvUavDescriptor(retainedCount);
        try
        {
            int exhaustedRtvBefore = local.Dx12Device
                .DescriptorHeapHeapRTV.AvailableDescriptorCount;
            int exhaustedDsvBefore = local.Dx12Device
                .DescriptorHeapDSV.AvailableDescriptorCount;
            int exhaustedShaderVisibleBefore = local.Dx12Device
                .DescriptorHeapCbvSrvUav.AvailableDescriptorCount;
            using RHICommandBuffer command =
                local.Queue.CreateCommandBuffer();
            command.Begin(
                "Dx12.BeginRollback.PrivateDescriptorExhaustion");
            Assert.Throws<InvalidOperationException>(() =>
                command.BeginRasterPass(in privateAllocationPass));
            Assert.Equal(
                exhaustedRtvBefore,
                local.Dx12Device.DescriptorHeapHeapRTV
                    .AvailableDescriptorCount);
            Assert.Equal(
                exhaustedDsvBefore,
                local.Dx12Device.DescriptorHeapDSV
                    .AvailableDescriptorCount);
            Assert.Equal(
                exhaustedShaderVisibleBefore,
                local.Dx12Device.DescriptorHeapCbvSrvUav
                    .AvailableDescriptorCount);
            command.End();
        }
        finally
        {
            local.Dx12Device.FreeCbvSrvUavDescriptor(
                retained.Index,
                retainedCount);
        }
        Assert.Equal(
            shaderVisibleBefore,
            local.Dx12Device.DescriptorHeapCbvSrvUav
                .AvailableDescriptorCount);
        local.AssertDeviceAlive("BeginRasterPass rollback probes");
        local.AssertNoDebugErrors("BeginRasterPass rollback probes");
    }

    private static void RunSparseOutputDraw(
        Dx12QualifiedContext context)
    {
        using RHITexture target = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.CopySrc);
        using RHIBuffer readback = CreateReadbackBuffer(context.Device);
        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.SparseOutputLocation.TypedNullDraw",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(target),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(outputs: new[] { -1, 0 }),
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        using RHIPipelineLayout layout = CreatePipelineLayout(
            context.Device);
        using RHIFunction vertex = CompileFunction(
            context.Device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        using RHIRasterPipeline pipeline = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(0).AttachmentInterface,
            ERHISampleCount.None,
            new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
            SparseTargetOneShader,
            "ps_main");
        using RHICommandBuffer command =
            context.Queue.CreateCommandBuffer();
        command.Begin("Dx12.SparseOutputLocation.TypedNullDraw");
        PrepareRenderTargets(command, target);

        RHIRasterEncoder raster =
            command.BeginRasterPass(in descriptor);
        raster.SetPipeline(pipeline);
        SetFourByFourViewport(raster);
        raster.Draw(3, 1, 0, 0);
        raster.EndPass();

        CopyTextureToReadback(
            command,
            target,
            ERHITextureLayout.RenderTarget,
            ERHIAccessMask.RenderTargetWrite,
            readback);
        command.End();
        context.SubmitAndWait(command);

        (byte r, byte g, byte b, byte a) = ReadFirstPixel(readback);
        Assert.InRange(r, (byte)63, (byte)65);
        Assert.InRange(g, (byte)127, (byte)129);
        Assert.InRange(b, (byte)190, (byte)192);
        Assert.Equal(byte.MaxValue, a);
    }
    private static void RunMultisampleLocalReadAndResolve(
        Dx12QualifiedContext context)
    {
        using RHITexture first = CreateGpuTexture(
            context.Device,
            ERHISampleCount.Count4,
            ERHITextureUsage.RenderTarget,
            dimension: ERHITextureDimension.Texture2DMS);
        using RHITexture second = CreateGpuTexture(
            context.Device,
            ERHISampleCount.Count4,
            ERHITextureUsage.RenderTarget,
            dimension: ERHITextureDimension.Texture2DMS);
        using RHITexture resolved = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.ResolveTarget |
                ERHITextureUsage.CopySrc);
        using RHIBuffer readback = CreateReadbackBuffer(context.Device);
        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.Multisample.LocalRead.Resolve",
            SampleCount = ERHISampleCount.Count4,
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(first),
                GpuAttachment(
                    second,
                    resolveTarget: resolved,
                    storeAction: ERHIStoreAction.StoreAndResolve),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(outputs: new[] { 0 }),
                SubPass(inputs: new[] { 0 }, outputs: new[] { 1 }),
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        using RHIPipelineLayout layout = CreatePipelineLayout(
            context.Device);
        using RHIFunction vertex = CompileFunction(
            context.Device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        using RHIRasterPipeline firstPipeline = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(0).AttachmentInterface,
            ERHISampleCount.Count4,
            new[]
            {
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHIPixelFormat.R8G8B8A8_UNorm,
            },
            ConstantQuarterShader,
            "ps_main");
        using RHIRasterPipeline secondPipeline = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(1).AttachmentInterface,
            ERHISampleCount.Count4,
            new[]
            {
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHIPixelFormat.R8G8B8A8_UNorm,
            },
            MultisampleLocalReadShader,
            "ps_main");
        using RHICommandBuffer command =
            context.Queue.CreateCommandBuffer();
        command.Begin("Dx12.Multisample.LocalRead.Resolve");
        PrepareRenderTargets(command, first, second);
        PrepareResolveDestination(command, resolved);

        RHIRasterEncoder raster =
            command.BeginRasterPass(in descriptor);
        raster.SetPipeline(firstPipeline);
        SetFourByFourViewport(raster);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(secondPipeline);
        SetFourByFourViewport(raster);
        raster.Draw(3, 1, 0, 0);
        raster.EndPass();

        CopyTextureToReadback(
            command,
            resolved,
            ERHITextureLayout.ResolveDestination,
            ERHIAccessMask.ResolveWrite,
            readback);
        command.End();
        context.SubmitAndWait(command);

        (byte r, byte g, byte b, byte a) = ReadFirstPixel(readback);
        Assert.InRange(r, (byte)126, (byte)130);
        Assert.Equal((byte)0, g);
        Assert.Equal((byte)0, b);
        Assert.Equal(byte.MaxValue, a);
    }

    private static void RunRasterOrderedPhaseSequence(
        Dx12QualifiedContext context)
    {
        using RHITexture ordered = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.UnorderedAccess |
                ERHITextureUsage.CopySrc);
        using RHITexture intermediate = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget);
        using RHIBuffer readback = CreateReadbackBuffer(context.Device);
        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.Rov.PhaseOrdering",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                GpuAttachment(ordered),
                GpuAttachment(intermediate),
            },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(inputs: new[] { 0 }, outputs: new[] { 0 }),
                SubPass(inputs: new[] { 0 }, outputs: new[] { 1 }),
                SubPass(outputs: new[] { 0 }),
                SubPass(inputs: new[] { 0 }, outputs: new[] { 1 }),
                SubPass(inputs: new[] { 0, 1 }, outputs: new[] { 0 }),
                SubPass(inputs: new[] { 0 }, outputs: new[] { 0 }),
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        using RHIPipelineLayout layout = CreatePipelineLayout(
            context.Device);
        using RHIFunction vertex = CompileFunction(
            context.Device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        ERHIPixelFormat[] formats =
        {
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHIPixelFormat.R8G8B8A8_UNorm,
        };
        using RHIRasterPipeline phase0 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(0).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            RovInitializeShader,
            "ps_main");
        using RHIRasterPipeline phase1 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(1).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            LocalAddT0Shader,
            "ps_main");
        using RHIRasterPipeline phase2 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(2).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            ConstantQuarterShader,
            "ps_main");
        using RHIRasterPipeline phase3 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(3).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            LocalAddT0Shader,
            "ps_main");
        using RHIRasterPipeline phase4 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(4).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            RovCombineT1Shader,
            "ps_main");
        using RHIRasterPipeline phase5 = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(5).AttachmentInterface,
            ERHISampleCount.None,
            formats,
            RovIncrementShader,
            "ps_main");
        using RHICommandBuffer command =
            context.Queue.CreateCommandBuffer();
        command.Begin("Dx12.Rov.PhaseOrdering");
        PrepareRenderTargets(command, ordered, intermediate);

        RHIRasterEncoder raster =
            command.BeginRasterPass(in descriptor);
        SetFourByFourViewport(raster);
        raster.SetPipeline(phase0);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(phase1);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(phase2);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(phase3);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(phase4);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(phase5);
        raster.Draw(3, 1, 0, 0);
        raster.EndPass();

        CopyTextureToReadback(
            command,
            ordered,
            ERHITextureLayout.RenderTarget,
            ERHIAccessMask.RenderTargetWrite,
            readback);
        command.End();
        context.SubmitAndWait(command);

        (byte r, byte g, byte b, byte a) = ReadFirstPixel(readback);
        Assert.InRange(r, (byte)177, (byte)181);
        Assert.Equal((byte)0, g);
        Assert.Equal((byte)0, b);
        Assert.Equal(byte.MaxValue, a);
    }

    private static void RunDepthStencilArrayLayerSequence(
        Dx12QualifiedContext context)
    {
        const uint selectedLayer = 1;
        RHITextureSubresourceRange colorLayer = new()
        {
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = selectedLayer,
            ArrayLayerCount = 1,
            AspectMask = ERHITextureAspectMask.Color,
        };
        RHITextureSubresourceRange depthStencilLayer = new()
        {
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = selectedLayer,
            ArrayLayerCount = 1,
            AspectMask =
                ERHITextureAspectMask.Depth |
                ERHITextureAspectMask.Stencil,
        };

        using RHITexture color = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.RenderTarget |
                ERHITextureUsage.CopySrc,
            dimension: ERHITextureDimension.Texture2DArray,
            arrayLayers: 2);
        using RHITexture depthStencil = CreateGpuTexture(
            context.Device,
            ERHISampleCount.None,
            ERHITextureUsage.DepthStencil,
            format: ERHIPixelFormat.D24_UNorm_S8_UInt,
            dimension: ERHITextureDimension.Texture2DArray,
            arrayLayers: 2);
        using RHIBuffer readback = CreateReadbackBuffer(context.Device);

        RHIRasterPassDescriptor descriptor = new()
        {
            Name = "Dx12.DepthStencil.ArrayLayer",
            ColorAttachments = new RHIColorAttachmentDescriptor[]
            {
                new()
                {
                    RenderTarget = color,
                    SubresourceRange = colorLayer,
                    LoadAction = ERHILoadAction.Clear,
                    StoreAction = ERHIStoreAction.Store,
                    ClearValue = new float4(0, 0, 0, 1),
                },
            },
            DepthStencilAttachment =
                new RHIDepthStencilAttachmentDescriptor
                {
                    RenderTarget = depthStencil,
                    SubresourceRange = depthStencilLayer,
                    DepthClearValue = 1.0f,
                    DepthLoadOp = ERHILoadAction.Clear,
                    DepthStoreOp = ERHIStoreAction.Store,
                    StencilClearValue = 0,
                    StencilLoadOp = ERHILoadAction.Clear,
                    StencilStoreOp = ERHIStoreAction.Store,
                },
            SubPassDescriptors = new RHISubPassDescriptor[]
            {
                SubPass(outputs: new[] { 0 }),
                SubPass(
                    outputs: new[] { 0 },
                    flags: ERHISubPassFlags.ReadOnlyDepthStencil),
            },
        };
        RHIRasterPassPlan plan = RHIRasterPassPlanner.Compile(in descriptor);
        using RHIPipelineLayout layout = CreatePipelineLayout(
            context.Device);
        using RHIFunction vertex = CompileFunction(
            context.Device,
            ERHIFunctionType.Vertex,
            ShaderStageKind.Vertex,
            "vs_main",
            FullscreenVertexShader);
        using RHIRasterPipeline writePipeline = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(0).AttachmentInterface,
            ERHISampleCount.None,
            new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
            ConstantRedShader,
            "ps_main",
            ERHIPixelFormat.D24_UNorm_S8_UInt,
            CreateDepthStencilWriteState());
        using RHIRasterPipeline readPipeline = CreateRasterPipeline(
            context.Device,
            layout,
            vertex,
            plan.GetSubPass(1).AttachmentInterface,
            ERHISampleCount.None,
            new[] { ERHIPixelFormat.R8G8B8A8_UNorm },
            ConstantGreenShader,
            "ps_main",
            ERHIPixelFormat.D24_UNorm_S8_UInt,
            CreateDepthStencilReadState());

        using RHICommandBuffer command =
            context.Queue.CreateCommandBuffer();
        command.Begin("Dx12.DepthStencil.ArrayLayer");
        PrepareAttachmentRanges(
            command,
            color,
            colorLayer,
            depthStencil,
            depthStencilLayer);

        RHIRasterEncoder raster =
            command.BeginRasterPass(in descriptor);
        raster.SetStencilRef(7);
        raster.SetPipeline(writePipeline);
        SetFourByFourViewport(raster);
        raster.Draw(3, 1, 0, 0);
        AdvanceSubPassWithoutManagedAllocation(raster);
        raster.SetPipeline(readPipeline);
        SetFourByFourViewport(raster);
        raster.Draw(3, 1, 0, 0);
        raster.EndPass();

        CopyTextureLayerToReadback(
            command,
            color,
            colorLayer,
            ERHITextureLayout.RenderTarget,
            ERHIAccessMask.RenderTargetWrite,
            readback);
        command.End();
        context.SubmitAndWait(command);

        (byte r, byte g, byte b, byte a) = ReadFirstPixel(readback);
        Assert.Equal((byte)0, r);
        Assert.Equal(byte.MaxValue, g);
        Assert.Equal((byte)0, b);
        Assert.Equal(byte.MaxValue, a);
    }

    private static void AdvanceSubPassWithoutManagedAllocation(
        RHIRasterEncoder raster)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        raster.NextSubPass();
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(before, after);
    }
    private static RHITexture CreateGpuTexture(
        RHIDevice device,
        ERHISampleCount samples,
        ERHITextureUsage usage,
        ERHIPixelFormat format = ERHIPixelFormat.R8G8B8A8_UNorm,
        ERHITextureDimension dimension =
            ERHITextureDimension.Texture2D,
        uint arrayLayers = 1)
    {
        return device.CreateTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(4, 4, arrayLayers),
            Format = format,
            SampleCount = samples,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = usage,
            Dimension = dimension,
        });
    }

    private static RHIColorAttachmentDescriptor GpuAttachment(
        RHITexture texture,
        ERHILoadAction loadAction = ERHILoadAction.Clear,
        RHITexture? resolveTarget = null,
        ERHIStoreAction storeAction = ERHIStoreAction.Store)
    {
        return new RHIColorAttachmentDescriptor
        {
            RenderTarget = texture,
            LoadAction = loadAction,
            StoreAction = storeAction,
            ResolveTarget = resolveTarget,
            ClearValue = new float4(0, 0, 0, 1),
        };
    }

    private static RHIBuffer CreateReadbackBuffer(RHIDevice device)
    {
        return device.CreateBuffer(new RHIBufferDescriptor
        {
            ByteSize = 256 * 4,
            Format = ERHIBufferFormat.Undefine,
            UsageFlag = ERHIBufferUsage.CopyDst,
            StorageMode = ERHIStorageMode.Readback,
        });
    }

    private static RHIPipelineLayout CreatePipelineLayout(
        RHIDevice device)
    {
        return device.CreatePipelineLayout(
            new RHIPipelineLayoutDescriptor
            {
                bLocalSignature = false,
                bUseVertexLayout = false,
                PushConstantSize = 0,
                BindingTableLayouts =
                    Array.Empty<RHIBindingTableLayout>(),
            });
    }

    private static RHIRasterPipeline CreateRasterPipeline(
        RHIDevice device,
        RHIPipelineLayout layout,
        RHIFunction vertex,
        in RHIAttachmentInterfaceSignature attachmentInterface,
        ERHISampleCount sampleCount,
        ERHIPixelFormat[] logicalFormats,
        string pixelSource,
        string pixelEntry,
        ERHIPixelFormat depthFormat = ERHIPixelFormat.Unknown,
        RHIRenderStateDescriptor? renderState = null)
    {
        using RHIFunction pixel = CompileFunction(
            device,
            ERHIFunctionType.Fragment,
            ShaderStageKind.Pixel,
            pixelEntry,
            pixelSource);
        RHIRasterPipelineDescriptor descriptor = new()
        {
            SampleCount = sampleCount,
            DepthFormat = depthFormat,
            ColorFormats = logicalFormats,
            AttachmentInterface = attachmentInterface,
            PipelineLayout = layout,
            FragmentFunction = pixel,
            PrimitiveAssembler =
                new RHIPrimitiveAssemblerDescriptor
                {
                    PrimitiveTopology =
                        ERHIPrimitiveTopology.TriangleList,
                    VertexAssembler =
                        new RHIVertexAssemblerDescriptor(
                            vertex,
                            Array.Empty<
                                RHIVertexLayoutDescriptor>()),
                },
            RenderState =
                renderState ?? CreateDefaultRenderState(),
        };
        if (attachmentInterface.ColorInputMask != 0)
        {
            RHIRasterAttachmentShaderAbiDescriptor abiDescriptor = new()
            {
                PipelineLayout = layout,
                SampleCount = sampleCount,
                ColorFormats = logicalFormats,
                AttachmentInterface = attachmentInterface,
            };
            descriptor.AttachmentShaderAbiClaim = device
                .QueryRasterAttachmentShaderAbi(in abiDescriptor)
                .CreateClaim();
        }
        return device.CreateRasterPipeline(in descriptor);
    }

    private static RHIFunction CompileFunction(
        RHIDevice device,
        ERHIFunctionType functionType,
        ShaderStageKind stage,
        string entryPoint,
        string source)
    {
        ShaderCompileResult result = HLSLCrossCompiler.Compile(
            new ShaderCompileRequest
            {
                Source = source,
                SourceName =
                    "Dx12RasterSubpassQualifiedTests.hlsl",
                EntryPoint = entryPoint,
                Stage = stage,
                ShaderModel = new ShaderModelVersion(6, 6),
                Target = ShaderTargetKind.Dxil,
            });
        if (result.Bytecode.Length == 0)
        {
            throw new InvalidOperationException(
                $"DXIL compilation for {entryPoint} returned no bytecode.");
        }

        IntPtr pointer = Marshal.AllocHGlobal(result.Bytecode.Length);
        try
        {
            Marshal.Copy(
                result.Bytecode,
                0,
                pointer,
                result.Bytecode.Length);
            return device.CreateFunction(new RHIFunctionDescriptor
            {
                ByteCode = pointer,
                ByteSize = checked((uint)result.Bytecode.Length),
                EntryName = entryPoint,
                Type = functionType,
                PayloadKind = ERHIShaderPayloadKind.Dxil,
            });
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static RHIRenderStateDescriptor CreateDefaultRenderState()
    {
        RHIStencilStateDescriptor keep = new()
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilPassOp = ERHIStencilOp.Keep,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };
        return new RHIRenderStateDescriptor
        {
            BlendState = new RHIBlendStateDescriptor
            {
                IndependentBlend = false,
                BlendDescriptor0 = new RHIBlendDescriptor
                {
                    BlendEnable = false,
                    BlendOpColor = ERHIBlendOp.Add,
                    SrcBlendColor = ERHIBlendMode.One,
                    DstBlendColor = ERHIBlendMode.Zero,
                    BlendOpAlpha = ERHIBlendOp.Add,
                    SrcBlendAlpha = ERHIBlendMode.One,
                    DstBlendAlpha = ERHIBlendMode.Zero,
                    ColorWriteChannel = ERHIColorWriteChannel.All,
                },
            },
            RasterizerState = new RHIRasterizerStateDescriptor
            {
                FillMode = ERHIFillMode.Solid,
                CullMode = ERHICullMode.None,
                DepthClipEnable = true,
                FrontCounterClockwise = false,
            },
            DepthStencilState = new RHIDepthStencilStateDescriptor
            {
                DepthEnable = false,
                DepthWriteMask = false,
                StencilEnable = false,
                ComparisonMode = ERHIComparisonMode.Always,
                FrontFace = keep,
                BackFace = keep,
            },
        };
    }

    private static RHIRenderStateDescriptor
        CreateDepthStencilWriteState()
    {
        RHIStencilStateDescriptor replace = new()
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilPassOp = ERHIStencilOp.Replace,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };
        RHIRenderStateDescriptor state = CreateDefaultRenderState();
        state.DepthStencilState = new RHIDepthStencilStateDescriptor
        {
            DepthEnable = true,
            DepthWriteMask = true,
            StencilEnable = true,
            StencilReadMask = byte.MaxValue,
            StencilWriteMask = byte.MaxValue,
            ComparisonMode = ERHIComparisonMode.Less,
            FrontFace = replace,
            BackFace = replace,
        };
        return state;
    }

    private static RHIRenderStateDescriptor
        CreateDepthStencilReadState()
    {
        RHIStencilStateDescriptor equal = new()
        {
            ComparisonMode = ERHIComparisonMode.Equal,
            StencilPassOp = ERHIStencilOp.Keep,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };
        RHIRenderStateDescriptor state = CreateDefaultRenderState();
        state.DepthStencilState = new RHIDepthStencilStateDescriptor
        {
            DepthEnable = true,
            DepthWriteMask = false,
            StencilEnable = true,
            StencilReadMask = byte.MaxValue,
            StencilWriteMask = 0,
            ComparisonMode = ERHIComparisonMode.Equal,
            FrontFace = equal,
            BackFace = equal,
        };
        return state;
    }

    private static void PrepareRenderTargets(
        RHICommandBuffer command,
        params RHITexture[] textures)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "PrepareRenderTargets",
            });
        foreach (RHITexture texture in textures)
        {
            transfer.Barrier(RHIBarrier.Texture(
                texture,
                RHITextureSubresourceRange.Whole(
                    ERHITextureAspectMask.Color),
                ERHITextureLayout.Undefined,
                ERHITextureLayout.RenderTarget,
                ERHIStageMask.None,
                ERHIStageMask.Fragment,
                ERHIAccessMask.None,
                ERHIAccessMask.RenderTargetRead |
                    ERHIAccessMask.RenderTargetWrite));
        }
        transfer.EndPass();
    }

    private static void PrepareResolveDestination(
        RHICommandBuffer command,
        RHITexture texture)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "PrepareResolveDestination",
            });
        transfer.Barrier(RHIBarrier.Texture(
            texture,
            RHITextureSubresourceRange.Whole(
                ERHITextureAspectMask.Color),
            ERHITextureLayout.Undefined,
            ERHITextureLayout.ResolveDestination,
            ERHIStageMask.None,
            ERHIStageMask.Transfer,
            ERHIAccessMask.None,
            ERHIAccessMask.ResolveWrite));
        transfer.EndPass();
    }

    private static void PrepareAttachmentRanges(
        RHICommandBuffer command,
        RHITexture color,
        in RHITextureSubresourceRange colorRange,
        RHITexture depthStencil,
        in RHITextureSubresourceRange depthStencilRange)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "PrepareAttachmentRanges",
            });
        transfer.Barrier(RHIBarrier.Texture(
            color,
            colorRange,
            ERHITextureLayout.Undefined,
            ERHITextureLayout.RenderTarget,
            ERHIStageMask.None,
            ERHIStageMask.Fragment,
            ERHIAccessMask.None,
            ERHIAccessMask.RenderTargetRead |
                ERHIAccessMask.RenderTargetWrite));
        transfer.Barrier(RHIBarrier.Texture(
            depthStencil,
            depthStencilRange,
            ERHITextureLayout.Undefined,
            ERHITextureLayout.DepthStencilWrite,
            ERHIStageMask.None,
            ERHIStageMask.Fragment,
            ERHIAccessMask.None,
            ERHIAccessMask.DepthStencilWrite));
        transfer.EndPass();
    }

    private static void CopyTextureToReadback(
        RHICommandBuffer command,
        RHITexture texture,
        ERHITextureLayout layoutBefore,
        ERHIAccessMask accessBefore,
        RHIBuffer readback)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "Readback",
            });
        transfer.Barrier(RHIBarrier.Texture(
            texture,
            RHITextureSubresourceRange.Whole(
                ERHITextureAspectMask.Color),
            layoutBefore,
            ERHITextureLayout.CopySource,
            ERHIStageMask.Fragment |
                ERHIStageMask.Transfer,
            ERHIStageMask.Transfer,
            accessBefore,
            ERHIAccessMask.TransferRead));
        transfer.Barrier(RHIBarrier.Buffer(
            readback,
            RHIBufferRange.Whole(),
            ERHIStageMask.None,
            ERHIStageMask.Transfer,
            ERHIAccessMask.None,
            ERHIAccessMask.TransferWrite));
        transfer.CopyTextureToBuffer(
            new RHITextureCopyDescriptor
            {
                Texture = texture,
                MipLevel = 0,
                SliceBase = 0,
                SliceCount = 1,
                Origin = new uint3(0, 0, 0),
            },
            new RHIBufferCopyDescriptor
            {
                Buffer = readback,
                Offset = 0,
                RowPitch = 256,
                TextureHeight = new uint3(4, 4, 1),
            },
            new int3(4, 4, 1));
        transfer.EndPass();
    }

    private static void CopyTextureLayerToReadback(
        RHICommandBuffer command,
        RHITexture texture,
        in RHITextureSubresourceRange range,
        ERHITextureLayout layoutBefore,
        ERHIAccessMask accessBefore,
        RHIBuffer readback)
    {
        RHITransferEncoder transfer = command.BeginTransferPass(
            new RHITransferPassDescriptor
            {
                Name = "ReadbackArrayLayer",
            });
        transfer.Barrier(RHIBarrier.Texture(
            texture,
            range,
            layoutBefore,
            ERHITextureLayout.CopySource,
            ERHIStageMask.Fragment,
            ERHIStageMask.Transfer,
            accessBefore,
            ERHIAccessMask.TransferRead));
        transfer.Barrier(RHIBarrier.Buffer(
            readback,
            RHIBufferRange.Whole(),
            ERHIStageMask.None,
            ERHIStageMask.Transfer,
            ERHIAccessMask.None,
            ERHIAccessMask.TransferWrite));
        transfer.CopyTextureToBuffer(
            new RHITextureCopyDescriptor
            {
                Texture = texture,
                MipLevel = range.BaseMipLevel,
                SliceBase = range.BaseArrayLayer,
                SliceCount = 1,
                Origin = new uint3(0, 0, 0),
            },
            new RHIBufferCopyDescriptor
            {
                Buffer = readback,
                Offset = 0,
                RowPitch = 256,
                TextureHeight = new uint3(4, 4, 1),
            },
            new int3(4, 4, 1));
        transfer.EndPass();
    }

    private static void SetFourByFourViewport(
        RHIRasterEncoder raster)
    {
        raster.SetViewport(new Viewport(0, 0, 4, 4, 0, 1));
        raster.SetScissor(new Rect(0, 0, 4, 4));
    }

    private static (byte R, byte G, byte B, byte A)
        ReadFirstPixel(RHIBuffer readback)
    {
        IntPtr pointer = readback.Map(0, 256 * 4);
        try
        {
            return (
                Marshal.ReadByte(pointer, 0),
                Marshal.ReadByte(pointer, 1),
                Marshal.ReadByte(pointer, 2),
                Marshal.ReadByte(pointer, 3));
        }
        finally
        {
            readback.UnMap(0, 0);
        }
    }

    private sealed class Dx12QualifiedContext : IDisposable
    {
        internal RHIInstance Instance { get; }
        internal RHIDevice Device { get; }
        internal Dx12Device Dx12Device { get; }
        internal RHICommandQueue Queue { get; }
        internal RHIFence Fence { get; }

        private Vortice.Direct3D12.Debug.ID3D12InfoQueue?
            m_InfoQueue;
        private bool m_Disposed;

        private Dx12QualifiedContext(
            RHIInstance instance,
            RHIDevice device,
            Dx12Device dx12Device,
            RHICommandQueue queue,
            RHIFence fence,
            Vortice.Direct3D12.Debug.ID3D12InfoQueue infoQueue)
        {
            Instance = instance;
            Device = device;
            Dx12Device = dx12Device;
            Queue = queue;
            Fence = fence;
            m_InfoQueue = infoQueue;
        }

        internal static Dx12QualifiedContext Create()
        {
            RHIInstance instance = RHIInstance.Create(
                new RHIInstanceDescriptor
                {
                    Backend = ERHIBackend.DirectX12,
                    EnableDebugLayer = true,
                    EnableValidation = false,
                    GraphicsQueueRequestCount = 1,
                });
            try
            {
                RHIDevice device = Enumerable
                    .Range(0, instance.DeviceCount)
                    .Select(index => instance.GetDevice(index))
                    .FirstOrDefault(candidate =>
                        candidate?.Name?.Contains(
                            "RTX 5090",
                            StringComparison.OrdinalIgnoreCase) == true)
                    ?? throw new InvalidOperationException(
                        "SharpGpuWindowsQualified requires the confirmed " +
                        "RTX 5090 adapter; no adapter fallback is allowed.");
                Dx12Device dx12Device = device as Dx12Device
                    ?? throw new InvalidOperationException(
                        "The selected qualification adapter is not a " +
                        "Dx12Device.");
                RHICommandQueue queue = device.GetCommandQueue(
                    ERHIPipelineType.Graphics,
                    0) ?? throw new InvalidOperationException(
                        $"DX12 graphics queue is unavailable for " +
                        $"'{device.Name}'.");
                Vortice.Direct3D12.Debug.ID3D12InfoQueue?
                    infoQueue = dx12Device.NativeDevice
                        .QueryInterfaceOrNull<Vortice.Direct3D12.Debug
                            .ID3D12InfoQueue>();
                if (infoQueue == null)
                {
                    throw new InvalidOperationException(
                        "DX12 debug-layer qualification requires the " +
                        "selected device to expose ID3D12InfoQueue.");
                }
                RHIFence fence = device.CreateFence();
                return new Dx12QualifiedContext(
                    instance,
                    device,
                    dx12Device,
                    queue,
                    fence,
                    infoQueue);
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }

        internal void ClearDebugMessages()
        {
            RequireInfoQueue().ClearStoredMessages();
        }

        internal void SubmitAndWait(RHICommandBuffer command)
        {
            Fence.Reset();
            Queue.Submit(new RHIQueueSubmitDescriptor(
                new[] { command },
                completionFence: Fence));
            Fence.Wait();
        }

        internal void AssertDeviceAlive(string scope)
        {
            SharpGen.Runtime.Result reason =
                Dx12Device.NativeDevice.DeviceRemovedReason;
            Assert.True(
                reason.Success,
                $"DX12 device was removed after {scope}: " +
                $"0x{reason.Code:X8}.");
        }

        internal void AssertNoDebugErrors(string scope)
        {
            Vortice.Direct3D12.Debug.ID3D12InfoQueue infoQueue =
                RequireInfoQueue();
            List<string> messages = new();
            ulong discardedMessageCount =
                infoQueue.NumMessagesDiscardedByMessageCountLimit;
            ulong count =
                infoQueue.NumStoredMessagesAllowedByRetrievalFilter;
            for (ulong index = 0; index < count; ++index)
            {
                Vortice.Direct3D12.Debug.Message message =
                    infoQueue.GetMessage(index);
                if (message.Severity is
                    Vortice.Direct3D12.Debug.MessageSeverity.Error or
                    Vortice.Direct3D12.Debug.MessageSeverity.Corruption)
                {
                    messages.Add(
                        $"[{message.Severity}] {message.Description}");
                }
            }
            infoQueue.ClearStoredMessages();
            Assert.True(
                messages.Count == 0 && discardedMessageCount == 0,
                $"DX12 Debug Layer reported errors during {scope}: " +
                $"discardedMessages={discardedMessageCount}" +
                Environment.NewLine +
                string.Join(Environment.NewLine, messages));
        }

        private Vortice.Direct3D12.Debug.ID3D12InfoQueue
            RequireInfoQueue() =>
            m_InfoQueue ?? throw new ObjectDisposedException(
                nameof(Dx12QualifiedContext));

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;
            Vortice.Direct3D12.Debug.ID3D12InfoQueue? infoQueue =
                m_InfoQueue;
            m_InfoQueue = null;
            infoQueue?.Release();
            Fence.Dispose();
            Instance.Dispose();
        }
    }

    private const string FullscreenVertexShader = """
struct VSOut
{
    float4 position : SV_Position;
};

VSOut vs_main(uint vertexId : SV_VertexID)
{
    float2 positions[3] =
    {
        float2(-1.0, -1.0),
        float2(-1.0, 3.0),
        float2(3.0, -1.0)
    };
    VSOut output;
    output.position = float4(positions[vertexId], 0.0, 1.0);
    return output;
}
""";

    private const string SparseTargetOneShader = """
float4 ps_main() : SV_Target1
{
    return float4(0.25, 0.5, 0.75, 1.0);
}
""";

    private const string ConstantQuarterShader = """
float4 ps_main() : SV_Target0
{
    return float4(0.25, 0.0, 0.0, 1.0);
}
""";

    private const string ConstantRedShader = """
float4 ps_main() : SV_Target0
{
    return float4(1.0, 0.0, 0.0, 1.0);
}
""";

    private const string ConstantGreenShader = """
float4 ps_main() : SV_Target0
{
    return float4(0.0, 1.0, 0.0, 1.0);
}
""";

    private const string MultisampleLocalReadShader = """
struct PSIn
{
    float4 position : SV_Position;
};
Texture2DMS<float4> LocalColor : register(t0, space65535);

float4 ps_main(PSIn input) : SV_Target0
{
    uint2 pixel = uint2(input.position.xy);
    float red = LocalColor.Load(pixel, 0).r + 0.25;
    return float4(red, 0.0, 0.0, 1.0);
}
""";

    private const string RovInitializeShader = """
struct PSIn
{
    float4 position : SV_Position;
};
RasterizerOrderedTexture2D<float4> OrderedColor :
    register(u0, space65535);

void ps_main(PSIn input)
{
    uint2 pixel = uint2(input.position.xy);
    OrderedColor[pixel] = float4(0.1, 0.0, 0.0, 1.0);
}
""";

    private const string LocalAddT0Shader = """
struct PSIn
{
    float4 position : SV_Position;
};
Texture2D<float4> LocalColor : register(t0, space65535);

float4 ps_main(PSIn input) : SV_Target0
{
    uint2 pixel = uint2(input.position.xy);
    return float4(
        LocalColor.Load(int3(pixel, 0)).r + 0.1,
        0.0,
        0.0,
        1.0);
}
""";

    private const string RovCombineT1Shader = """
struct PSIn
{
    float4 position : SV_Position;
};
Texture2D<float4> LocalColor : register(t1, space65535);
RasterizerOrderedTexture2D<float4> OrderedColor :
    register(u0, space65535);

void ps_main(PSIn input)
{
    uint2 pixel = uint2(input.position.xy);
    float red = OrderedColor[pixel].r +
        LocalColor.Load(int3(pixel, 0)).r;
    OrderedColor[pixel] = float4(red, 0.0, 0.0, 1.0);
}
""";

    private const string RovIncrementShader = """
struct PSIn
{
    float4 position : SV_Position;
};
RasterizerOrderedTexture2D<float4> OrderedColor :
    register(u0, space65535);

void ps_main(PSIn input)
{
    uint2 pixel = uint2(input.position.xy);
    float red = OrderedColor[pixel].r + 0.1;
    OrderedColor[pixel] = float4(red, 0.0, 0.0, 1.0);
}
""";
    private static RHISubPassDescriptor SubPass(
        int[]? inputs = null,
        int[]? outputs = null,
        ERHISubPassFlags flags = ERHISubPassFlags.None)
    {
        return new RHISubPassDescriptor
        {
            ColorInputs = inputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(inputs),
            ColorOutputs = outputs == null
                ? RHIAttachmentIndexArray.Empty
                : new RHIAttachmentIndexArray(outputs),
            Flags = flags,
        };
    }

    private static RHIColorAttachmentDescriptor Attachment(
        RHITexture texture,
        ERHILoadAction loadAction)
    {
        return new RHIColorAttachmentDescriptor
        {
            RenderTarget = texture,
            LoadAction = loadAction,
            StoreAction = ERHIStoreAction.Store,
        };
    }

    private static PlannerTexture CreateTexture(
        ERHIPixelFormat format,
        ERHISampleCount samples,
        ERHITextureUsage usage,
        ERHITextureDimension dimension =
            ERHITextureDimension.Texture2D)
    {
        return new PlannerTexture(new RHITextureDescriptor
        {
            MipCount = 1,
            Extent = new uint3(4, 4, 1),
            Format = format,
            SampleCount = samples,
            StorageMode = ERHIStorageMode.GPULocal,
            UsageFlag = usage,
            Dimension = dimension,
        });
    }

    private sealed class PlannerTexture : RHITexture
    {
        internal PlannerTexture(in RHITextureDescriptor descriptor)
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
#endif
