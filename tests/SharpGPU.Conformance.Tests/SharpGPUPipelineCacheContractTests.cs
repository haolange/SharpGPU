using System;
using System.Runtime.InteropServices;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

[Trait("Category", "SharpGpuPortable")]
public sealed class SharpGPUPipelineCacheContractTests
{
    [Fact]
    public void PublicContract_RemovesRetiredCacheSurface()
    {
        string retiredTypeName = "SharpGPU.RHI" + "Pipeline" + "Library";
        string retiredFactoryName = "Create" + "Pipeline" + "Library";

        Assert.Null(
            typeof(RHIDevice).Assembly.GetType(
                retiredTypeName,
                throwOnError: false,
                ignoreCase: false));
        Assert.Null(
            typeof(RHIDevice).GetMethod(
                retiredFactoryName,
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public));
        Assert.NotNull(typeof(RHIDevice).GetMethod(nameof(RHIDevice.CreatePipelineCache)));
    }

    [Fact]
    public void PipelineCacheBlob_RoundTripsCompatibleCallerOwnedPayload()
    {
        RHIPipelineCacheIdentity identity = new(
            ERHIBackend.Vulkan,
            0x10de,
            0x2684,
            "driver-A");
        byte[] nativePayload = { 1, 3, 5, 7, 9 };

        byte[] blob = RHIPipelineCacheBlob.Encode(identity, nativePayload);
        RHIPipelineCacheImportResult result =
            RHIPipelineCacheBlob.TryDecode(blob, identity, out byte[] decoded);

        Assert.Equal(ERHIPipelineCacheImportStatus.Loaded, result.Status);
        Assert.Equal(nativePayload, decoded);
        Assert.NotSame(nativePayload, decoded);
    }

    [Fact]
    public void PipelineCacheBlob_DistinguishesCorruptFromIncompatible()
    {
        RHIPipelineCacheIdentity identity = new(
            ERHIBackend.DirectX12,
            0x1002,
            0x744c,
            "driver-A");
        byte[] blob = RHIPipelineCacheBlob.Encode(identity, new byte[] { 2, 4, 6, 8 });

        byte[] corrupt = (byte[])blob.Clone();
        corrupt[^1] ^= 0x7f;
        RHIPipelineCacheImportResult corruptResult =
            RHIPipelineCacheBlob.TryDecode(corrupt, identity, out _);
        Assert.Equal(ERHIPipelineCacheImportStatus.Corrupt, corruptResult.Status);

        RHIPipelineCacheIdentity differentDevice = new(
            ERHIBackend.DirectX12,
            0x1002,
            0x744d,
            "driver-A");
        RHIPipelineCacheImportResult deviceResult =
            RHIPipelineCacheBlob.TryDecode(blob, differentDevice, out _);
        Assert.Equal(ERHIPipelineCacheImportStatus.Incompatible, deviceResult.Status);

        RHIPipelineCacheIdentity differentDriver = new(
            ERHIBackend.DirectX12,
            0x1002,
            0x744c,
            "driver-B");
        RHIPipelineCacheImportResult driverResult =
            RHIPipelineCacheBlob.TryDecode(blob, differentDriver, out _);
        Assert.Equal(ERHIPipelineCacheImportStatus.Incompatible, driverResult.Status);

        RHIPipelineCacheIdentity differentBackend = new(
            ERHIBackend.Vulkan,
            0x1002,
            0x744c,
            "driver-A");
        RHIPipelineCacheImportResult backendResult =
            RHIPipelineCacheBlob.TryDecode(blob, differentBackend, out _);
        Assert.Equal(ERHIPipelineCacheImportStatus.Incompatible, backendResult.Status);

        byte[] differentAbi = (byte[])blob.Clone();
        differentAbi[12] ^= 0x01;
        RHIPipelineCacheImportResult abiResult =
            RHIPipelineCacheBlob.TryDecode(differentAbi, identity, out _);
        Assert.Equal(ERHIPipelineCacheImportStatus.Incompatible, abiResult.Status);
    }

    [Fact]
    public void PipelineLayoutIdentity_UsesImmutableLogicalBindingTableSnapshot()
    {
        RHIBindingTableLayoutElement canonicalElement = new()
        {
            Slot = 3,
            Count = 2,
            Type = ERHIBindType.StorageBuffer,
            Stages = ERHIShaderStageMask.Compute,
            Requirement = ERHIBindingRequirement.Required,
        };
        RHIBindingTableLayoutElement[] sourceElements = { canonicalElement };
        RHIBindingTableLayoutDescriptor sourceDescriptor = new()
        {
            Index = 4,
            Elements = sourceElements,
        };

        using TestBindingTableLayout snapshottedLayout = new(sourceDescriptor);
        sourceElements[0].Slot = 99;
        sourceElements[0].Count = 7;

        using AlternateTestBindingTableLayout equivalentLayout = new(
            new RHIBindingTableLayoutDescriptor
            {
                Index = 4,
                Elements = new[] { canonicalElement },
            });
        using TestBindingTableLayout changedLayout = new(
            new RHIBindingTableLayoutDescriptor
            {
                Index = 4,
                Elements = sourceElements,
            });
        using TestPipelineLayout snapshottedPipelineLayout =
            new(0, snapshottedLayout);
        using TestPipelineLayout equivalentPipelineLayout =
            new(0, equivalentLayout);
        using TestPipelineLayout changedPipelineLayout =
            new(0, changedLayout);

        Assert.Equal(
            equivalentPipelineLayout.PipelineCacheIdentity.ToArray(),
            snapshottedPipelineLayout.PipelineCacheIdentity.ToArray());
        Assert.NotEqual(
            changedPipelineLayout.PipelineCacheIdentity.ToArray(),
            snapshottedPipelineLayout.PipelineCacheIdentity.ToArray());
    }

    [Fact]
    public void PipelineLayoutIdentity_RejectsDisposedBindingTableLayout()
    {
        TestBindingTableLayout bindingTableLayout = new(
            new RHIBindingTableLayoutDescriptor
            {
                Index = 1,
                Elements = Array.Empty<RHIBindingTableLayoutElement>(),
            });
        bindingTableLayout.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => new TestPipelineLayout(0, bindingTableLayout));
    }

    [Fact]
    public void ComputePipelineKey_CoversBytecodeThreadSizeAndLayout()
    {
        using TestPipelineLayout layoutA = new(pushConstantSize: 0);
        using TestPipelineLayout layoutB = new(pushConstantSize: 16);
        using TestFunction shaderA = new(
            ERHIFunctionType.Compute,
            "main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 1 });
        using TestFunction shaderB = new(
            ERHIFunctionType.Compute,
            "main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 2 });

        RHIComputePipelineDescriptor baseline = new()
        {
            ThreadSize = new SharpGPU.Mathematics.uint3(8, 8, 1),
            ComputeFunction = shaderA,
            PipelineLayout = layoutA,
        };
        RHIComputePipelineDescriptor differentBytecode = baseline;
        differentBytecode.ComputeFunction = shaderB;
        RHIComputePipelineDescriptor differentThreads = baseline;
        differentThreads.ThreadSize = new SharpGPU.Mathematics.uint3(16, 8, 1);
        RHIComputePipelineDescriptor differentLayout = baseline;
        differentLayout.PipelineLayout = layoutB;

        RHIPipelineCacheIdentity identity = new(
            ERHIBackend.DirectX12,
            0x10de,
            0x2684,
            "driver-A");
        RHIPipelineCacheIdentity differentBackend = new(
            ERHIBackend.Vulkan,
            identity.VendorId,
            identity.DeviceId,
            identity.DriverVersion);
        RHIPipelineCacheIdentity differentDevice = new(
            identity.Backend,
            identity.VendorId,
            identity.DeviceId + 1,
            identity.DriverVersion);
        RHIPipelineCacheIdentity differentDriver = new(
            identity.Backend,
            identity.VendorId,
            identity.DeviceId,
            "driver-B");

        string key = RHIPipelineCacheKeyBuilder.CreateComputeKey(baseline, identity);
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateComputeKey(differentBytecode, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateComputeKey(differentThreads, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateComputeKey(differentLayout, identity));
        Assert.NotEqual(key, RHIPipelineCacheKeyBuilder.CreateComputeKey(baseline, differentBackend));
        Assert.NotEqual(key, RHIPipelineCacheKeyBuilder.CreateComputeKey(baseline, differentDevice));
        Assert.NotEqual(key, RHIPipelineCacheKeyBuilder.CreateComputeKey(baseline, differentDriver));
    }

    [Fact]
    public void RasterPipelineKey_CoversShaderStateFormatsAndVertexLayout()
    {
        using TestPipelineLayout layout = new(pushConstantSize: 0);
        using TestFunction vertex = new(
            ERHIFunctionType.Vertex,
            "vs_main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 1 });
        using TestFunction fragmentA = new(
            ERHIFunctionType.Fragment,
            "ps_main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 2 });
        using TestFunction fragmentB = new(
            ERHIFunctionType.Fragment,
            "ps_main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 3 });

        RHIRasterPipelineDescriptor baseline = CreateRasterDescriptor(
            layout,
            vertex,
            fragmentA,
            ERHIPixelFormat.R8G8B8A8_UNorm,
            stride: 16,
            sampleMask: uint.MaxValue);
        RHIRasterPipelineDescriptor differentShader = baseline;
        differentShader.FragmentFunction = fragmentB;
        RHIRasterPipelineDescriptor differentFormat = CreateRasterDescriptor(
            layout,
            vertex,
            fragmentA,
            ERHIPixelFormat.R16G16B16A16_Float,
            stride: 16,
            sampleMask: uint.MaxValue);
        RHIRasterPipelineDescriptor differentState = CreateRasterDescriptor(
            layout,
            vertex,
            fragmentA,
            ERHIPixelFormat.R8G8B8A8_UNorm,
            stride: 16,
            sampleMask: 0x00ff00ff);
        RHIRasterPipelineDescriptor differentVertexLayout = CreateRasterDescriptor(
            layout,
            vertex,
            fragmentA,
            ERHIPixelFormat.R8G8B8A8_UNorm,
            stride: 32,
            sampleMask: uint.MaxValue);
        RHIRasterPipelineDescriptor differentAttachment = baseline;
        differentAttachment.AttachmentInterface =
            new RHIAttachmentInterfaceSignature(
                colorAttachmentCount: 1,
                colorInputs: new RHIAttachmentIndexArray(new[] { 0 }),
                colorOutputs: new RHIAttachmentIndexArray(new[] { 0 }));
        RHIRasterPipelineDescriptor layeredAttachment = differentAttachment;
        layeredAttachment.AttachmentInterface =
            new RHIAttachmentInterfaceSignature(
                colorAttachmentCount: 1,
                colorInputs: new RHIAttachmentIndexArray(new[] { 0 }),
                colorOutputs: new RHIAttachmentIndexArray(new[] { 0 }),
                layeredAccessMask: 1);
        RHIRasterPipelineDescriptor explicitDefaultAttachment = baseline;
        explicitDefaultAttachment.AttachmentInterface =
            new RHIAttachmentInterfaceSignature(
                colorAttachmentCount: 1,
                colorInputs: RHIAttachmentIndexArray.Empty,
                colorOutputs: new RHIAttachmentIndexArray(new[] { 0 }));

        RHIPipelineCacheIdentity identity = new(
            ERHIBackend.DirectX12,
            0x10de,
            0x2684,
            "driver-A");

        string key = RHIPipelineCacheKeyBuilder.CreateRasterKey(baseline, identity);
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(differentShader, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(differentFormat, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(differentState, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(differentVertexLayout, identity));
        Assert.NotEqual(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(differentAttachment, identity));
        Assert.NotEqual(
            RHIPipelineCacheKeyBuilder.CreateRasterKey(
                differentAttachment,
                identity),
            RHIPipelineCacheKeyBuilder.CreateRasterKey(
                layeredAttachment,
                identity));
        Assert.Equal(
            key,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(explicitDefaultAttachment, identity));
    }

    [Fact]
    public void RasterPipelineKey_CoversOrderedAttachmentSlotsAndHoles()
    {
        using TestPipelineLayout layout = new(pushConstantSize: 0);
        using TestFunction vertex = new(
            ERHIFunctionType.Vertex,
            "vs_main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 1 });
        using TestFunction fragment = new(
            ERHIFunctionType.Fragment,
            "ps_main",
            new byte[] { 0x44, 0x58, 0x49, 0x4c, 2 });
        RHIRasterPipelineDescriptor ordered = CreateRasterDescriptor(
            layout,
            vertex,
            fragment,
            ERHIPixelFormat.R8G8B8A8_UNorm,
            stride: 16,
            sampleMask: uint.MaxValue);
        ordered.ColorFormats = new[]
        {
            ERHIPixelFormat.R8G8B8A8_UNorm,
            ERHIPixelFormat.R16G16B16A16_Float,
        };
        ordered.AttachmentInterface = new RHIAttachmentInterfaceSignature(
            2,
            RHIAttachmentIndexArray.Empty,
            new RHIAttachmentIndexArray(new[] { 0, 1 }));

        RHIRasterPipelineDescriptor reordered = ordered;
        reordered.AttachmentInterface = new RHIAttachmentInterfaceSignature(
            2,
            RHIAttachmentIndexArray.Empty,
            new RHIAttachmentIndexArray(new[] { 1, 0 }));
        RHIRasterPipelineDescriptor sparse = ordered;
        sparse.AttachmentInterface = new RHIAttachmentInterfaceSignature(
            2,
            RHIAttachmentIndexArray.Empty,
            new RHIAttachmentIndexArray(new[] { 0, -1, 1 }));

        RHIPipelineCacheIdentity identity = new(
            ERHIBackend.Vulkan,
            0x10de,
            0x2684,
            "driver-A");
        string orderedKey =
            RHIPipelineCacheKeyBuilder.CreateRasterKey(ordered, identity);
        Assert.NotEqual(
            orderedKey,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(reordered, identity));
        Assert.NotEqual(
            orderedKey,
            RHIPipelineCacheKeyBuilder.CreateRasterKey(sparse, identity));
    }

    [Fact]
    public void RasterAttachmentSupportQuery_PreservesExactLayeredBlendFacts()
    {
        RHIBlendDescriptor blend = new()
        {
            BlendEnable = true,
            BlendOpColor = ERHIBlendOp.Add,
            SrcBlendColor = ERHIBlendMode.SrcAlpha,
            DstBlendColor = ERHIBlendMode.OneMinusSrcAlpha,
            ColorWriteChannel = ERHIColorWriteChannel.All,
        };
        RHIRasterAttachmentSupportQuery query = new(
            ERHIPixelFormat.R16G16B16A16_Float,
            ERHISampleCount.Count4,
            isInput: true,
            isOutput: true,
            in blend,
            alphaToCoverage: true,
            isLayered: true);

        Assert.True(query.IsInput);
        Assert.True(query.IsOutput);
        Assert.True(query.IsLayered);
        Assert.True(query.Blend.BlendEnable);
        Assert.True(query.AlphaToCoverage);
        Assert.Equal(ERHISampleCount.Count4, query.SampleCount);
        Assert.Equal(
            ERHIPixelFormat.R16G16B16A16_Float,
            query.Format);
        Assert.Throws<ArgumentException>(() =>
            new RHIRasterAttachmentSupportQuery(
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHISampleCount.None,
                isInput: false,
                isOutput: true,
                in blend,
                isLayered: true));
    }

    [Fact]
    public void RasterAttachmentShaderAbi_IsVersionedHashedAndDefensivelyCopied()
    {
        using TestPipelineLayout layout = new(pushConstantSize: 16);
        RHIAttachmentInterfaceSignature signature = new(
            colorAttachmentCount: 3,
            colorInputs: new RHIAttachmentIndexArray(new[] { 0, 2 }),
            colorOutputs: new RHIAttachmentIndexArray(new[] { 2 }));
        RHIRasterAttachmentShaderAbiDescriptor descriptor = new()
        {
            PipelineLayout = layout,
            SampleCount = ERHISampleCount.None,
            ColorFormats = new[]
            {
                ERHIPixelFormat.R8G8B8A8_UNorm,
                ERHIPixelFormat.R16G16B16A16_Float,
                ERHIPixelFormat.R32_UInt,
            },
            AttachmentInterface = signature,
        };
        RHIRasterAttachmentShaderBinding[] bindings =
            CreateRawAttachmentBindings();

        RHIRasterAttachmentShaderAbi abi =
            RHIRasterAttachmentShaderAbiFactory.Create(
                ERHIBackend.Vulkan,
                in descriptor,
                bindings);

        Assert.Equal(
            RHIRasterAttachmentShaderAbi.CurrentRevision,
            abi.Revision);
        Assert.Equal(64, abi.ContractHash.Length);
        Assert.Equal(
            abi.ContractHash,
            Convert.ToHexString(Convert.FromHexString(abi.ContractHash)));
        Assert.Equal(2, abi.Bindings.Length);
        Assert.Equal(
            ERHIRawShaderBindingKind.InputAttachment,
            abi.GetBinding(0).Input.Kind);
        Assert.Equal(
            ERHIRawShaderBindingKind.UnorderedAccess,
            abi.GetBinding(2).Output.Kind);

        bindings[0] = default;
        Assert.Equal(0, abi.GetBinding(0).LogicalAttachment);
        RHIRasterAttachmentShaderAbi equivalent =
            RHIRasterAttachmentShaderAbiFactory.Create(
                ERHIBackend.Vulkan,
                in descriptor,
                CreateRawAttachmentBindings());
        RHIRasterAttachmentShaderAbiDescriptor changedDescriptor =
            descriptor;
        changedDescriptor.ColorFormats =
            (ERHIPixelFormat[])descriptor.ColorFormats.Clone();
        changedDescriptor.ColorFormats[2] = ERHIPixelFormat.R32_SInt;
        RHIRasterAttachmentShaderAbi changedFormat =
            RHIRasterAttachmentShaderAbiFactory.Create(
                ERHIBackend.Vulkan,
                in changedDescriptor,
                CreateRawAttachmentBindings());
        Assert.Equal(abi.ContractHash, equivalent.ContractHash);
        Assert.NotEqual(abi.ContractHash, changedFormat.ContractHash);
        Assert.Equal(abi.CreateClaim(), equivalent.CreateClaim());
        Assert.Null(
            typeof(RHIRasterAttachmentShaderAbi).GetProperty(
                "Target" + "Preamble",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public));

        Assert.Throws<ArgumentException>(() =>
            RHIRasterAttachmentShaderAbiFactory.Create(
                ERHIBackend.Vulkan,
                in descriptor,
                new[] { CreateRawAttachmentBindings()[0] }));
    }

    [Fact]
    public void RasterAttachmentShaderAbiClaim_RejectsNonHexAndNormalizesCase()
    {
        Assert.Throws<ArgumentException>(() =>
            new RHIRasterAttachmentShaderAbiClaim(
                ERHIBackend.DirectX12,
                RHIRasterAttachmentShaderAbi.CurrentRevision,
                new string('G', 64)));

        RHIRasterAttachmentShaderAbiClaim claim = new(
            ERHIBackend.Metal,
            RHIRasterAttachmentShaderAbi.CurrentRevision,
            new string('a', 64));
        Assert.Equal(new string('A', 64), claim.ContractHash);
    }

    private static RHIRasterAttachmentShaderBinding[]
        CreateRawAttachmentBindings()
    {
        return new[]
        {
            new RHIRasterAttachmentShaderBinding(
                logicalAttachment: 0,
                inputSlot: 0,
                outputLocation: -1,
                new RHIRawShaderBindingLocation(
                    ERHIRawShaderBindingKind.InputAttachment,
                    index: 0,
                    setOrSpace: 31),
                default),
            new RHIRasterAttachmentShaderBinding(
                logicalAttachment: 2,
                inputSlot: 1,
                outputLocation: 0,
                new RHIRawShaderBindingLocation(
                    ERHIRawShaderBindingKind.UnorderedAccess,
                    index: 2,
                    setOrSpace: 31),
                new RHIRawShaderBindingLocation(
                    ERHIRawShaderBindingKind.UnorderedAccess,
                    index: 2,
                    setOrSpace: 31)),
        };
    }

    private static RHIRasterPipelineDescriptor CreateRasterDescriptor(
        RHIPipelineLayout layout,
        RHIFunction vertex,
        RHIFunction fragment,
        in ERHIPixelFormat colorFormat,
        in uint stride,
        in uint sampleMask)
    {
        RHIStencilStateDescriptor keepStencilFace = new()
        {
            ComparisonMode = ERHIComparisonMode.Always,
            StencilPassOp = ERHIStencilOp.Keep,
            StencilFailOp = ERHIStencilOp.Keep,
            StencilDepthFailOp = ERHIStencilOp.Keep,
        };

        return new RHIRasterPipelineDescriptor
        {
            SampleCount = ERHISampleCount.None,
            DepthFormat = ERHIPixelFormat.Unknown,
            ColorFormats = new[] { colorFormat },
            RenderState = new RHIRenderStateDescriptor
            {
                SampleMask = sampleMask,
                DepthStencilState = new RHIDepthStencilStateDescriptor
                {
                    ComparisonMode = ERHIComparisonMode.Always,
                    FrontFace = keepStencilFace,
                    BackFace = keepStencilFace,
                },
            },
            FragmentFunction = fragment,
            PipelineLayout = layout,
            PrimitiveAssembler = new RHIPrimitiveAssemblerDescriptor
            {
                PrimitiveTopology = ERHIPrimitiveTopology.TriangleList,
                VertexAssembler = new RHIVertexAssemblerDescriptor(
                    vertex,
                    new[]
                    {
                        new RHIVertexLayoutDescriptor
                        {
                            Index = 0,
                            Stride = stride,
                            StepRate = 1,
                            StepMode = ERHIVertexStepMode.PerVertex,
                            VertexElements = new[]
                            {
                                new RHIVertexElementDescriptor
                                {
                                    Slot = 0,
                                    Offset = 0,
                                    Type = ERHISemanticType.Position,
                                    Format = ERHISemanticFormat.Float4,
                                },
                            },
                        },
                    }),
            },
        };
    }

    private sealed class TestBindingTableLayout : RHIBindingTableLayout
    {
        public TestBindingTableLayout(
            in RHIBindingTableLayoutDescriptor descriptor)
            : base(descriptor)
        {
        }
    }

    private sealed class AlternateTestBindingTableLayout : RHIBindingTableLayout
    {
        public AlternateTestBindingTableLayout(
            in RHIBindingTableLayoutDescriptor descriptor)
            : base(descriptor)
        {
        }
    }

    private sealed class TestPipelineLayout : RHIPipelineLayout
    {
        public TestPipelineLayout(
            in uint pushConstantSize,
            params RHIBindingTableLayout[] bindingTableLayouts)
        {
            InitializePipelineCacheIdentity(new RHIPipelineLayoutDescriptor
            {
                PushConstantSize = pushConstantSize,
                BindingTableLayouts = bindingTableLayouts,
            });
        }
    }

    private sealed class TestFunction : RHIFunction
    {
        private IntPtr m_ByteCode;

        public TestFunction(
            in ERHIFunctionType type,
            string entryName,
            byte[] byteCode)
        {
            m_ByteCode = Marshal.AllocHGlobal(byteCode.Length);
            Marshal.Copy(byteCode, 0, m_ByteCode, byteCode.Length);
            m_Descriptor = new RHIFunctionDescriptor
            {
                ByteSize = checked((uint)byteCode.Length),
                ByteCode = m_ByteCode,
                EntryName = entryName,
                Type = type,
                PayloadKind = ERHIShaderPayloadKind.Dxil,
            };
        }

        protected override void Release()
        {
            if (m_ByteCode != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(m_ByteCode);
                m_ByteCode = IntPtr.Zero;
            }
        }
    }
}
