using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum ERHIPipelineCacheImportStatus : byte
    {
        Empty = 0,
        Loaded = 1,
        Incompatible = 2,
        Corrupt = 3,
    }

    public readonly struct RHIPipelineCacheImportResult
    {
        public ERHIPipelineCacheImportStatus Status { get; }
        public string Reason { get; }
        public bool IsLoaded => Status == ERHIPipelineCacheImportStatus.Loaded;

        internal RHIPipelineCacheImportResult(
            in ERHIPipelineCacheImportStatus status,
            string reason)
        {
            Status = status;
            Reason = reason ?? string.Empty;
        }
    }

    public abstract class RHIPipelineCache : Disposal
    {
        private readonly RHIPipelineCacheIdentity m_Identity;

        internal RHIPipelineCache(RHIDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (device.IsDisposed)
            {
                throw new ObjectDisposedException(device.GetType().FullName);
            }

            m_Identity = RHIPipelineCacheIdentity.FromDevice(device);
        }

        public RHIPipelineCacheImportResult Import(in ReadOnlyMemory<byte> blob)
        {
            ThrowIfDisposed();

            if (blob.IsEmpty)
            {
                if (!TryReplaceNativePayload(ReadOnlySpan<byte>.Empty, out string reason))
                {
                    return new RHIPipelineCacheImportResult(
                        ERHIPipelineCacheImportStatus.Corrupt,
                        string.IsNullOrWhiteSpace(reason)
                            ? "The backend rejected an empty pipeline cache."
                            : reason);
                }

                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Empty,
                    "An empty cache was initialized.");
            }

            RHIPipelineCacheImportResult decodeResult = RHIPipelineCacheBlob.TryDecode(
                blob.Span,
                m_Identity,
                out byte[] nativePayload);
            if (decodeResult.Status != ERHIPipelineCacheImportStatus.Loaded)
            {
                return decodeResult;
            }

            if (!TryReplaceNativePayload(nativePayload, out string nativeReason))
            {
                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Corrupt,
                    string.IsNullOrWhiteSpace(nativeReason)
                        ? "The backend rejected the compatible native pipeline-cache payload."
                        : nativeReason);
            }

            return decodeResult;
        }

        public byte[] Export()
        {
            ThrowIfDisposed();
            byte[] nativePayload = ExportNativePayload();
            return RHIPipelineCacheBlob.Encode(m_Identity, nativePayload);
        }

        public abstract RHIComputePipeline CreateComputePipeline(
            in RHIComputePipelineDescriptor descriptor);

        public abstract RHIRasterPipeline CreateRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor);

        protected string BuildComputePipelineCacheKey(
            in RHIComputePipelineDescriptor descriptor)
        {
            return RHIPipelineCacheKeyBuilder.CreateComputeKey(descriptor, m_Identity);
        }

        protected string BuildRasterPipelineCacheKey(
            in RHIRasterPipelineDescriptor descriptor)
        {
            return RHIPipelineCacheKeyBuilder.CreateRasterKey(descriptor, m_Identity);
        }

        protected abstract bool TryReplaceNativePayload(
            ReadOnlySpan<byte> nativePayload,
            out string reason);

        protected abstract byte[] ExportNativePayload();
    }

    internal readonly struct RHIPipelineCacheIdentity
    {
        public const uint CurrentSchemaRevision = 1;
        public const uint CurrentPipelineAbiRevision = 4;

        public ERHIBackend Backend { get; }
        public uint VendorId { get; }
        public uint DeviceId { get; }
        public string DriverVersion { get; }

        public RHIPipelineCacheIdentity(
            in ERHIBackend backend,
            in uint vendorId,
            in uint deviceId,
            string driverVersion)
        {
            Backend = backend;
            VendorId = vendorId;
            DeviceId = deviceId;
            DriverVersion = driverVersion ?? string.Empty;
        }

        public static RHIPipelineCacheIdentity FromDevice(RHIDevice device)
        {
            return new RHIPipelineCacheIdentity(
                device.BackendType,
                device.VendorId.IntValue,
                device.DeviceId.IntValue,
                device.DriverVersion);
        }
    }

    internal static class RHIPipelineCacheBlob
    {
        private static readonly byte[] s_Magic = Encoding.ASCII.GetBytes("SGPUCACH");
        private const int DigestSize = 32;
        private const int MaxDriverVersionByteCount = 4096;

        public static byte[] Encode(
            in RHIPipelineCacheIdentity identity,
            ReadOnlySpan<byte> nativePayload)
        {
            byte[] payloadDigest = SHA256.HashData(nativePayload);
            byte[] driverBytes = Encoding.UTF8.GetBytes(identity.DriverVersion);
            if (driverBytes.Length > MaxDriverVersionByteCount)
            {
                throw new InvalidOperationException(
                    $"Driver identity is {driverBytes.Length} bytes, exceeding the {MaxDriverVersionByteCount}-byte pipeline-cache limit.");
            }

            using MemoryStream stream = new MemoryStream(
                checked(8 + 4 + 4 + 1 + 4 + 4 + 4 + driverBytes.Length + 4 + DigestSize + nativePayload.Length));
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(s_Magic);
            writer.Write(RHIPipelineCacheIdentity.CurrentSchemaRevision);
            writer.Write(RHIPipelineCacheIdentity.CurrentPipelineAbiRevision);
            writer.Write((byte)identity.Backend);
            writer.Write(identity.VendorId);
            writer.Write(identity.DeviceId);
            writer.Write(driverBytes.Length);
            writer.Write(driverBytes);
            writer.Write(nativePayload.Length);
            writer.Write(payloadDigest);
            writer.Write(nativePayload);
            writer.Flush();
            return stream.ToArray();
        }

        public static RHIPipelineCacheImportResult TryDecode(
            ReadOnlySpan<byte> blob,
            in RHIPipelineCacheIdentity expectedIdentity,
            out byte[] nativePayload)
        {
            nativePayload = Array.Empty<byte>();
            try
            {
                using MemoryStream stream = new MemoryStream(blob.ToArray(), writable: false);
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                byte[] magic = reader.ReadBytes(s_Magic.Length);
                if (magic.Length != s_Magic.Length || !magic.AsSpan().SequenceEqual(s_Magic))
                {
                    return Corrupt("Pipeline-cache magic is invalid.");
                }

                uint schemaRevision = reader.ReadUInt32();
                uint pipelineAbiRevision = reader.ReadUInt32();
                byte backendValue = reader.ReadByte();
                uint vendorId = reader.ReadUInt32();
                uint deviceId = reader.ReadUInt32();

                int driverByteCount = reader.ReadInt32();
                if (driverByteCount < 0 || driverByteCount > MaxDriverVersionByteCount)
                {
                    return Corrupt("Pipeline-cache driver identity length is invalid.");
                }

                byte[] driverBytes = reader.ReadBytes(driverByteCount);
                if (driverBytes.Length != driverByteCount)
                {
                    return Corrupt("Pipeline-cache driver identity is truncated.");
                }

                string driverVersion = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true).GetString(driverBytes);

                int payloadLength = reader.ReadInt32();
                if (payloadLength < 0)
                {
                    return Corrupt("Pipeline-cache native payload length is invalid.");
                }

                byte[] expectedDigest = reader.ReadBytes(DigestSize);
                if (expectedDigest.Length != DigestSize)
                {
                    return Corrupt("Pipeline-cache payload digest is truncated.");
                }

                long remaining = stream.Length - stream.Position;
                if (remaining != payloadLength)
                {
                    return Corrupt(
                        $"Pipeline-cache native payload length is {payloadLength}, but {remaining} bytes remain.");
                }

                nativePayload = reader.ReadBytes(payloadLength);
                if (!CryptographicOperations.FixedTimeEquals(
                        expectedDigest,
                        SHA256.HashData(nativePayload)))
                {
                    nativePayload = Array.Empty<byte>();
                    return Corrupt("Pipeline-cache native payload checksum does not match.");
                }

                if (schemaRevision != RHIPipelineCacheIdentity.CurrentSchemaRevision)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache schema revision {schemaRevision} does not match {RHIPipelineCacheIdentity.CurrentSchemaRevision}.");
                }

                if (pipelineAbiRevision != RHIPipelineCacheIdentity.CurrentPipelineAbiRevision)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline ABI revision {pipelineAbiRevision} does not match {RHIPipelineCacheIdentity.CurrentPipelineAbiRevision}.");
                }

                if (!Enum.IsDefined(typeof(ERHIBackend), backendValue)
                    || (ERHIBackend)backendValue != expectedIdentity.Backend)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache backend {(ERHIBackend)backendValue} does not match {expectedIdentity.Backend}.");
                }

                if (vendorId != expectedIdentity.VendorId || deviceId != expectedIdentity.DeviceId)
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible(
                        $"Pipeline-cache adapter {vendorId:X8}:{deviceId:X8} does not match {expectedIdentity.VendorId:X8}:{expectedIdentity.DeviceId:X8}.");
                }

                if (!string.Equals(
                        driverVersion,
                        expectedIdentity.DriverVersion,
                        StringComparison.Ordinal))
                {
                    nativePayload = Array.Empty<byte>();
                    return Incompatible("Pipeline-cache driver identity does not match the active device.");
                }

                return new RHIPipelineCacheImportResult(
                    ERHIPipelineCacheImportStatus.Loaded,
                    "The compatible pipeline cache was loaded.");
            }
            catch (Exception exception) when (
                exception is EndOfStreamException
                or IOException
                or DecoderFallbackException
                or ArgumentException
                or OverflowException)
            {
                nativePayload = Array.Empty<byte>();
                return Corrupt($"Pipeline-cache blob is malformed: {exception.Message}");
            }
        }

        private static RHIPipelineCacheImportResult Corrupt(string reason)
        {
            return new RHIPipelineCacheImportResult(
                ERHIPipelineCacheImportStatus.Corrupt,
                reason);
        }

        private static RHIPipelineCacheImportResult Incompatible(string reason)
        {
            return new RHIPipelineCacheImportResult(
                ERHIPipelineCacheImportStatus.Incompatible,
                reason);
        }
    }

    internal static unsafe class RHIPipelineCacheKeyBuilder
    {
        public static byte[] CreatePipelineLayoutIdentity(
            in RHIPipelineLayoutDescriptor descriptor)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(descriptor.bLocalSignature);
            writer.Write(descriptor.bUseVertexLayout);
            writer.Write(descriptor.PushConstantSize);

            RHIArgumentTableLayout[] layouts =
                descriptor.ArgumentTableLayouts ?? Array.Empty<RHIArgumentTableLayout>();
            writer.Write(layouts.Length);
            for (int index = 0; index < layouts.Length; ++index)
            {
                WriteArgumentTableLayout(writer, layouts[index], index);
            }

            if (descriptor.StaticSamplers.HasValue)
            {
                Span<RHIStaticSamplerDescriptor> staticSamplers =
                    descriptor.StaticSamplers.Value.Span;
                writer.Write(staticSamplers.Length);
                for (int samplerGroupIndex = 0;
                     samplerGroupIndex < staticSamplers.Length;
                     ++samplerGroupIndex)
                {
                    ref RHIStaticSamplerDescriptor samplerGroup =
                        ref staticSamplers[samplerGroupIndex];
                    writer.Write(samplerGroup.Index);
                    Span<RHIStaticSamplerElement> elements = samplerGroup.Elements.Span;
                    writer.Write(elements.Length);
                    for (int elementIndex = 0;
                         elementIndex < elements.Length;
                         ++elementIndex)
                    {
                        ref RHIStaticSamplerElement element = ref elements[elementIndex];
                        writer.Write(element.BindSlot);
                        WriteSampler(writer, element.SamplerDescriptor);
                    }
                }
            }
            else
            {
                writer.Write(0);
            }

            writer.Flush();
            return SHA256.HashData(stream.ToArray());
        }

        public static string CreateComputeKey(
            in RHIComputePipelineDescriptor descriptor,
            in RHIPipelineCacheIdentity identity)
        {
            ValidatePipelineLayout(descriptor.PipelineLayout);
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write("SharpGPU.ComputePipeline");
            WriteCacheIdentity(writer, identity);
            writer.Write(descriptor.ThreadSize.x);
            writer.Write(descriptor.ThreadSize.y);
            writer.Write(descriptor.ThreadSize.z);
            WriteFunction(writer, descriptor.ComputeFunction, "compute");
            writer.Write(descriptor.PipelineLayout.PipelineCacheIdentity.Span);
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        public static string CreateRasterKey(
            in RHIRasterPipelineDescriptor descriptor,
            in RHIPipelineCacheIdentity identity)
        {
            ValidatePipelineLayout(descriptor.PipelineLayout);
            RHIRasterPipelineDescriptor snapshot =
                RHIRasterPipelineContract.SnapshotAndValidate(in descriptor);
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write("SharpGPU.RasterPipeline");
            WriteCacheIdentity(writer, identity);
            writer.Write((byte)snapshot.SampleCount);
            writer.Write((ushort)snapshot.DepthFormat);

            ERHIPixelFormat[] colorFormats = snapshot.ColorFormats;
            writer.Write(colorFormats.Length);
            for (int index = 0; index < colorFormats.Length; ++index)
            {
                writer.Write((ushort)colorFormats[index]);
            }

            RHIAttachmentInterfaceSignature attachment =
                snapshot.AttachmentInterface;
            writer.Write(attachment.ColorAttachmentCount);
            writer.Write(attachment.ColorInputSlotCount);
            for (int inputIndex = 0;
                 inputIndex < attachment.ColorInputSlotCount;
                 ++inputIndex)
            {
                writer.Write(attachment.GetColorInputLogicalAttachment(inputIndex));
            }
            writer.Write(attachment.ColorOutputLocationCount);
            for (int outputLocation = 0;
                 outputLocation < attachment.ColorOutputLocationCount;
                 ++outputLocation)
            {
                writer.Write(
                    attachment.GetColorOutputLogicalAttachment(outputLocation));
            }
            writer.Write(attachment.SampledFeedbackSlotCount);
            for (int sampledOrdinal = 0;
                 sampledOrdinal < attachment.SampledFeedbackSlotCount;
                 ++sampledOrdinal)
            {
                writer.Write(
                    attachment.GetSampledFeedbackLogicalAttachment(sampledOrdinal));
            }
            writer.Write(attachment.RasterOrderedReadWriteMask);
            writer.Write(attachment.LayeredAccessMask);
            writer.Write((byte)attachment.DepthStencilFlags);
            writer.Write(attachment.UsesDualSourceColor);

            WriteRenderState(writer, snapshot.RenderState);
            WritePrimitiveAssembler(writer, snapshot.PrimitiveAssembler);
            WriteFunction(writer, snapshot.FragmentFunction, "fragment");
            RHIPipelineLayout pipelineLayout = snapshot.PipelineLayout
                ?? throw new ArgumentNullException(nameof(snapshot), "Raster pipeline cache key requires a PipelineLayout.");
            ValidatePipelineLayout(pipelineLayout);
            writer.Write(pipelineLayout.PipelineCacheIdentity.Span);
            writer.Flush();
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        private static void WriteCacheIdentity(
            BinaryWriter writer,
            in RHIPipelineCacheIdentity identity)
        {
            writer.Write(RHIPipelineCacheIdentity.CurrentSchemaRevision);
            writer.Write(RHIPipelineCacheIdentity.CurrentPipelineAbiRevision);
            writer.Write((byte)identity.Backend);
            writer.Write(identity.VendorId);
            writer.Write(identity.DeviceId);
            writer.Write(identity.DriverVersion);
        }

        private static void ValidatePipelineLayout(RHIPipelineLayout? layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            if (layout.IsDisposed)
            {
                throw new ObjectDisposedException(layout.GetType().FullName);
            }
            if (layout.PipelineCacheIdentity.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"{layout.GetType().Name} has no canonical pipeline-layout cache identity.");
            }
        }

        private static void WriteArgumentTableLayout(
            BinaryWriter writer,
            RHIArgumentTableLayout? layout,
            in int layoutIndex)
        {
            ArgumentNullException.ThrowIfNull(layout);
            if (layout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    layout.GetType().FullName,
                    $"Pipeline argument-table layout {layoutIndex} is disposed.");
            }

            writer.Write(layout.CanonicalIndex);
            ReadOnlySpan<RHIArgumentTableLayoutElement> elements =
                layout.CanonicalElements;
            writer.Write(elements.Length);
            for (int index = 0; index < elements.Length; ++index)
            {
                ref readonly RHIArgumentTableLayoutElement element =
                    ref elements[index];
                WriteArgumentElement(
                    writer,
                    element.Slot,
                    element.Count,
                    element.Type,
                    element.Stages,
                    element.Requirement);
            }
        }

        private static void WriteArgumentElement(
            BinaryWriter writer,
            in uint slot,
            in uint count,
            in ERHIBindType type,
            in ERHIShaderStageMask stages,
            in ERHIArgumentBindingRequirement requirement)
        {
            writer.Write(slot);
            writer.Write(count);
            writer.Write((byte)type);
            writer.Write((ushort)stages);
            writer.Write((byte)requirement);
        }

        private static void WriteSampler(
            BinaryWriter writer,
            in RHISamplerDescriptor descriptor)
        {
            writer.Write(descriptor.LodMin);
            writer.Write(descriptor.LodMax);
            writer.Write(descriptor.MipLODBias);
            writer.Write(descriptor.Anisotropy);
            writer.Write((byte)descriptor.MinFilter);
            writer.Write((byte)descriptor.MagFilter);
            writer.Write((byte)descriptor.MipFilter);
            writer.Write((byte)descriptor.AddressModeU);
            writer.Write((byte)descriptor.AddressModeV);
            writer.Write((byte)descriptor.AddressModeW);
            writer.Write((byte)descriptor.ComparisonMode);
        }

        private static void WriteFunction(
            BinaryWriter writer,
            RHIFunction? function,
            string role)
        {
            writer.Write(function != null);
            if (function == null)
            {
                return;
            }
            if (function.IsDisposed)
            {
                throw new ObjectDisposedException(
                    function.GetType().FullName,
                    $"The {role} shader function is disposed.");
            }

            RHIFunctionDescriptor descriptor = function.Descriptor;
            writer.Write((byte)descriptor.Type);
            writer.Write((byte)descriptor.PayloadKind);
            writer.Write(descriptor.EntryName ?? string.Empty);
            writer.Write(descriptor.ByteSize);
            if (descriptor.ByteSize == 0 || descriptor.ByteCode == IntPtr.Zero)
            {
                throw new ArgumentException(
                    $"The {role} shader function has no bytecode.",
                    nameof(function));
            }
            if (descriptor.ByteSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(function),
                    $"The {role} shader function is larger than the supported cache-key input.");
            }

            ReadOnlySpan<byte> byteCode = new ReadOnlySpan<byte>(
                descriptor.ByteCode.ToPointer(),
                checked((int)descriptor.ByteSize));
            writer.Write(SHA256.HashData(byteCode));
        }

        private static void WritePrimitiveAssembler(
            BinaryWriter writer,
            in RHIPrimitiveAssemblerDescriptor descriptor)
        {
            writer.Write((byte)descriptor.PrimitiveType);
            writer.Write((byte)descriptor.PrimitiveTopology);
            writer.Write(descriptor.VertexAssembler.HasValue);
            if (descriptor.VertexAssembler.HasValue)
            {
                RHIVertexAssemblerDescriptor vertexAssembler =
                    descriptor.VertexAssembler.Value;
                WriteFunction(writer, vertexAssembler.VertexFunction, "vertex");
                Span<RHIVertexLayoutDescriptor> layouts =
                    vertexAssembler.VertexLayouts.Span;
                writer.Write(layouts.Length);
                for (int layoutIndex = 0; layoutIndex < layouts.Length; ++layoutIndex)
                {
                    ref RHIVertexLayoutDescriptor layout = ref layouts[layoutIndex];
                    writer.Write(layout.Index);
                    writer.Write(layout.Stride);
                    writer.Write(layout.StepRate);
                    writer.Write((byte)layout.StepMode);
                    Span<RHIVertexElementDescriptor> elements =
                        layout.VertexElements.Span;
                    writer.Write(elements.Length);
                    for (int elementIndex = 0;
                         elementIndex < elements.Length;
                         ++elementIndex)
                    {
                        ref RHIVertexElementDescriptor element =
                            ref elements[elementIndex];
                        writer.Write(element.Slot);
                        writer.Write(element.Offset);
                        writer.Write((byte)element.Type);
                        writer.Write((byte)element.Format);
                    }
                }
            }

            writer.Write(descriptor.MeshletAssembler.HasValue);
            if (descriptor.MeshletAssembler.HasValue)
            {
                RHIMeshletAssemblerDescriptor meshletAssembler =
                    descriptor.MeshletAssembler.Value;
                WriteFunction(writer, meshletAssembler.TaskFunction, "task");
                WriteFunction(writer, meshletAssembler.MeshFunction, "mesh");
            }
        }

        private static void WriteRenderState(
            BinaryWriter writer,
            in RHIRenderStateDescriptor descriptor)
        {
            writer.Write(descriptor.SampleMask.HasValue);
            if (descriptor.SampleMask.HasValue)
            {
                writer.Write(descriptor.SampleMask.Value);
            }

            writer.Write(descriptor.BlendState.AlphaToCoverage);
            writer.Write(descriptor.BlendState.IndependentBlend);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor0);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor1);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor2);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor3);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor4);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor5);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor6);
            WriteBlend(writer, descriptor.BlendState.BlendDescriptor7);

            RHIRasterizerStateDescriptor rasterizer = descriptor.RasterizerState;
            writer.Write((byte)rasterizer.FillMode);
            writer.Write((byte)rasterizer.CullMode);
            writer.Write(rasterizer.DepthClipEnable);
            writer.Write(rasterizer.ConservativeRaster);
            writer.Write(rasterizer.AntialiasedLineEnable);
            writer.Write(rasterizer.FrontCounterClockwise);
            writer.Write(rasterizer.DepthBias);
            writer.Write(rasterizer.DepthBiasClamp);
            writer.Write(rasterizer.SlopeScaledDepthBias);

            RHIDepthStencilStateDescriptor depthStencil =
                descriptor.DepthStencilState;
            writer.Write(depthStencil.DepthEnable);
            writer.Write(depthStencil.DepthWriteMask);
            writer.Write(depthStencil.StencilEnable);
            writer.Write(depthStencil.StencilReadMask);
            writer.Write(depthStencil.StencilWriteMask);
            writer.Write((byte)depthStencil.ComparisonMode);
            WriteStencil(writer, depthStencil.FrontFace);
            WriteStencil(writer, depthStencil.BackFace);
        }

        private static void WriteBlend(
            BinaryWriter writer,
            in RHIBlendDescriptor descriptor)
        {
            writer.Write(descriptor.BlendEnable);
            writer.Write((byte)descriptor.BlendOpColor);
            writer.Write((byte)descriptor.SrcBlendColor);
            writer.Write((byte)descriptor.DstBlendColor);
            writer.Write((byte)descriptor.BlendOpAlpha);
            writer.Write((byte)descriptor.SrcBlendAlpha);
            writer.Write((byte)descriptor.DstBlendAlpha);
            writer.Write((byte)descriptor.ColorWriteChannel);
        }

        private static void WriteStencil(
            BinaryWriter writer,
            in RHIStencilStateDescriptor descriptor)
        {
            writer.Write((byte)descriptor.StencilPassOp);
            writer.Write((byte)descriptor.StencilFailOp);
            writer.Write((byte)descriptor.StencilDepthFailOp);
            writer.Write((byte)descriptor.ComparisonMode);
        }
    }
}
