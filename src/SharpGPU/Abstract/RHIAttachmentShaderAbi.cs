using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SharpGPU
{
    /// <summary>
    /// Identifies the target-language namespace used by a raw fragment-shader
    /// attachment location. Native ordering mechanisms remain backend-private.
    /// </summary>
    public enum ERHIRawShaderBindingKind : byte
    {
        /// <summary>No target binding is present.</summary>
        None = 0,
        /// <summary>A fragment color input or output location.</summary>
        ColorAttachment = 1,
        /// <summary>A read-only shader resource location.</summary>
        ShaderResource = 2,
        /// <summary>A writable shader resource location.</summary>
        UnorderedAccess = 3,
        /// <summary>A framebuffer-local input-attachment location.</summary>
        InputAttachment = 4,
    }

    /// <summary>
    /// A query-only physical location for raw target shader interop.
    /// Index is a color location, register, or descriptor binding; SetOrSpace
    /// is zero for color locations and otherwise the descriptor set/register
    /// space selected by the backend.
    /// </summary>
    public readonly struct RHIRawShaderBindingLocation :
        IEquatable<RHIRawShaderBindingLocation>
    {
        /// <summary>Gets the target binding namespace.</summary>
        public ERHIRawShaderBindingKind Kind { get; }
        /// <summary>Gets the color location, register, or descriptor binding.</summary>
        public uint Index { get; }
        /// <summary>Gets the descriptor set or register space, when applicable.</summary>
        public uint SetOrSpace { get; }
        /// <summary>Gets whether this location names a target binding.</summary>
        public bool IsBound => Kind != ERHIRawShaderBindingKind.None;

        /// <summary>Creates a validated target shader binding location.</summary>
        public RHIRawShaderBindingLocation(
            in ERHIRawShaderBindingKind kind,
            in uint index,
            in uint setOrSpace = 0)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (kind == ERHIRawShaderBindingKind.None &&
                (index != 0 || setOrSpace != 0))
            {
                throw new ArgumentException(
                    "An unbound raw shader location cannot carry an index or set/space.",
                    nameof(kind));
            }
            if (kind == ERHIRawShaderBindingKind.ColorAttachment &&
                setOrSpace != 0)
            {
                throw new ArgumentException(
                    "A color-attachment location does not use a descriptor set or register space.",
                    nameof(setOrSpace));
            }

            Kind = kind;
            Index = index;
            SetOrSpace = setOrSpace;
        }

        public bool Equals(RHIRawShaderBindingLocation other) =>
            Kind == other.Kind &&
            Index == other.Index &&
            SetOrSpace == other.SetOrSpace;

        public override bool Equals(object? obj) =>
            obj is RHIRawShaderBindingLocation other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Kind, Index, SetOrSpace);

        public static bool operator ==(
            RHIRawShaderBindingLocation left,
            RHIRawShaderBindingLocation right) => left.Equals(right);

        public static bool operator !=(
            RHIRawShaderBindingLocation left,
            RHIRawShaderBindingLocation right) => !left.Equals(right);
    }

    /// <summary>
    /// Maps one logical color attachment to the raw fragment-shader input and
    /// output locations selected for a concrete backend pipeline layout.
    /// </summary>
    public readonly struct RHIRasterAttachmentShaderBinding
    {
        /// <summary>Gets the logical color attachment identifier.</summary>
        public int LogicalAttachment { get; }
        /// <summary>Gets the logical input slot, or -1 when absent.</summary>
        public int InputSlot { get; }
        /// <summary>Gets the logical output location, or -1 when absent.</summary>
        public int OutputLocation { get; }
        /// <summary>Gets the target input binding.</summary>
        public RHIRawShaderBindingLocation Input { get; }
        /// <summary>Gets the target output binding.</summary>
        public RHIRawShaderBindingLocation Output { get; }
        /// <summary>Gets whether the attachment is declared as an input.</summary>
        public bool IsInput => InputSlot >= 0;
        /// <summary>Gets whether the attachment is declared as an output.</summary>
        public bool IsOutput => OutputLocation >= 0;

        /// <summary>Creates one logical-to-target attachment binding.</summary>
        public RHIRasterAttachmentShaderBinding(
            in int logicalAttachment,
            in int inputSlot,
            in int outputLocation,
            in RHIRawShaderBindingLocation input,
            in RHIRawShaderBindingLocation output)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            ValidateOptionalOrdinal(inputSlot, nameof(inputSlot));
            ValidateOptionalOrdinal(outputLocation, nameof(outputLocation));
            if ((inputSlot >= 0) != input.IsBound)
            {
                throw new ArgumentException(
                    "The raw input location must be bound exactly when an input slot is declared.",
                    nameof(input));
            }
            if ((outputLocation >= 0) != output.IsBound)
            {
                throw new ArgumentException(
                    "The raw output location must be bound exactly when an output location is declared.",
                    nameof(output));
            }
            if (inputSlot < 0 && outputLocation < 0)
            {
                throw new ArgumentException(
                    "An attachment shader binding must declare an input, an output, or both.");
            }

            LogicalAttachment = logicalAttachment;
            InputSlot = inputSlot;
            OutputLocation = outputLocation;
            Input = input;
            Output = output;
        }

        private static void ValidateOptionalOrdinal(
            int value,
            string parameterName)
        {
            if (value < -1 ||
                value >= RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "Attachment shader ordinals must be -1 or in [0, 8).");
            }
        }
    }

    /// <summary>
    /// Describes the logical attachment interface for a target-specific raw
    /// fragment-shader ABI query.
    /// </summary>
    public struct RHIRasterAttachmentShaderAbiDescriptor
    {
        /// <summary>The pipeline layout whose target binding namespace is queried.</summary>
        public RHIPipelineLayout PipelineLayout;
        /// <summary>The raster sample count.</summary>
        public ERHISampleCount SampleCount;
        /// <summary>The logical color attachment formats.</summary>
        public ERHIPixelFormat[] ColorFormats;
        /// <summary>The public Inputs/Outputs attachment interface.</summary>
        public RHIAttachmentInterfaceSignature AttachmentInterface;
    }

    /// <summary>
    /// Immutable proof embedded in a raw fragment function and validated when
    /// its raster pipeline is created.
    /// </summary>
    public readonly struct RHIRasterAttachmentShaderAbiClaim :
        IEquatable<RHIRasterAttachmentShaderAbiClaim>
    {
        /// <summary>Gets the backend this claim was generated for.</summary>
        public ERHIBackend Backend { get; }
        /// <summary>Gets the SharpGPU raw attachment ABI revision.</summary>
        public uint Revision { get; }
        /// <summary>Gets the uppercase SHA-256 contract hash.</summary>
        public string ContractHash { get; }

        /// <summary>Creates a validated raw attachment ABI claim.</summary>
        public RHIRasterAttachmentShaderAbiClaim(
            in ERHIBackend backend,
            in uint revision,
            string contractHash)
        {
            if (!Enum.IsDefined(backend))
            {
                throw new ArgumentOutOfRangeException(nameof(backend));
            }
            if (revision == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revision));
            }
            if (string.IsNullOrWhiteSpace(contractHash) ||
                contractHash.Length != 64)
            {
                throw new ArgumentException(
                    "An attachment shader ABI claim requires a 64-character SHA-256 hash.",
                    nameof(contractHash));
            }
            try
            {
                _ = Convert.FromHexString(contractHash);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    "An attachment shader ABI claim hash must contain only hexadecimal digits.",
                    nameof(contractHash),
                    exception);
            }

            Backend = backend;
            Revision = revision;
            ContractHash = contractHash.ToUpperInvariant();
        }

        public bool Equals(RHIRasterAttachmentShaderAbiClaim other) =>
            Backend == other.Backend &&
            Revision == other.Revision &&
            string.Equals(
                ContractHash,
                other.ContractHash,
                StringComparison.Ordinal);

        public override bool Equals(object? obj) =>
            obj is RHIRasterAttachmentShaderAbiClaim other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Backend, Revision, ContractHash);

        public static bool operator ==(
            RHIRasterAttachmentShaderAbiClaim left,
            RHIRasterAttachmentShaderAbiClaim right) => left.Equals(right);

        public static bool operator !=(
            RHIRasterAttachmentShaderAbiClaim left,
            RHIRasterAttachmentShaderAbiClaim right) => !left.Equals(right);
    }

    /// <summary>
    /// The versioned, target-specific raw fragment-shader attachment ABI. It
    /// exposes only locations needed by raw shader authors; native ordering,
    /// layout, access flags, and backend resource construction stay private.
    /// </summary>
    public sealed class RHIRasterAttachmentShaderAbi
    {
        /// <summary>The current raw attachment shader ABI revision.</summary>
        public const uint CurrentRevision = 1;

        /// <summary>Gets the backend this ABI targets.</summary>
        public ERHIBackend Backend { get; }
        /// <summary>Gets the raw attachment ABI revision.</summary>
        public uint Revision => CurrentRevision;
        /// <summary>Gets the SHA-256 hash of all target-visible ABI facts.</summary>
        public string ContractHash { get; }
        /// <summary>Gets the immutable logical-to-target binding table.</summary>
        public ReadOnlyMemory<RHIRasterAttachmentShaderBinding> Bindings =>
            m_Bindings;

        private readonly RHIRasterAttachmentShaderBinding[] m_Bindings;

        internal RHIRasterAttachmentShaderAbi(
            in ERHIBackend backend,
            RHIRasterAttachmentShaderBinding[] bindings,
            string contractHash)
        {
            Backend = backend;
            m_Bindings =
                (RHIRasterAttachmentShaderBinding[])bindings.Clone();
            ContractHash = contractHash;
        }

        /// <summary>Gets the binding for one logical color attachment.</summary>
        public RHIRasterAttachmentShaderBinding GetBinding(
            in int logicalAttachment)
        {
            for (int i = 0; i < m_Bindings.Length; ++i)
            {
                if (m_Bindings[i].LogicalAttachment == logicalAttachment)
                {
                    return m_Bindings[i];
                }
            }
            throw new ArgumentOutOfRangeException(
                nameof(logicalAttachment),
                logicalAttachment,
                "The logical attachment is not part of this shader ABI.");
        }

        /// <summary>Creates the claim that must accompany a matching pipeline.</summary>
        public RHIRasterAttachmentShaderAbiClaim CreateClaim() =>
            new RHIRasterAttachmentShaderAbiClaim(
                Backend,
                Revision,
                ContractHash);
    }

    internal static class RHIRasterAttachmentShaderAbiFactory
    {
        internal static RHIRasterAttachmentShaderAbi Create(
            in ERHIBackend backend,
            in RHIRasterAttachmentShaderAbiDescriptor descriptor,
            RHIRasterAttachmentShaderBinding[] bindings)
        {
            ValidateDescriptor(in descriptor);
            ArgumentNullException.ThrowIfNull(bindings);
            ValidateBindings(
                in descriptor.AttachmentInterface,
                bindings);
            string hash = ComputeHash(
                backend,
                in descriptor,
                bindings);
            return new RHIRasterAttachmentShaderAbi(
                backend,
                bindings,
                hash);
        }

        internal static int FindInputSlot(
            in RHIAttachmentInterfaceSignature signature,
            int logicalAttachment)
        {
            for (int inputSlot = 0;
                 inputSlot < signature.ColorInputSlotCount;
                 ++inputSlot)
            {
                if (signature.GetColorInputLogicalAttachment(inputSlot) ==
                    logicalAttachment)
                {
                    return inputSlot;
                }
            }
            return -1;
        }

        internal static int FindOutputLocation(
            in RHIAttachmentInterfaceSignature signature,
            int logicalAttachment)
        {
            for (int outputLocation = 0;
                 outputLocation < signature.ColorOutputLocationCount;
                 ++outputLocation)
            {
                if (signature.GetColorOutputLogicalAttachment(
                        outputLocation) == logicalAttachment)
                {
                    return outputLocation;
                }
            }
            return -1;
        }

        private static void ValidateDescriptor(
            in RHIRasterAttachmentShaderAbiDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor.PipelineLayout);
            if (descriptor.PipelineLayout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(descriptor.PipelineLayout));
            }
            ERHIPixelFormat[] formats = descriptor.ColorFormats ??
                throw new ArgumentException(
                    "Attachment shader ABI ColorFormats cannot be null.",
                    nameof(descriptor));
            if (formats.Length !=
                descriptor.AttachmentInterface.ColorAttachmentCount)
            {
                throw new ArgumentException(
                    "Attachment shader ABI formats must match the logical color attachment count.",
                    nameof(descriptor));
            }
            _ = RHIRasterPipelineContract.ValidateSampleCount(
                descriptor.SampleCount,
                nameof(descriptor.SampleCount));
            for (int i = 0; i < formats.Length; ++i)
            {
                if (formats[i] == ERHIPixelFormat.Unknown ||
                    RHIBarrierUtility.InferAspectMask(formats[i]) !=
                        ERHITextureAspectMask.Color)
                {
                    throw new ArgumentException(
                        $"Attachment shader ABI format {i} is not a color format.",
                        nameof(descriptor));
                }
            }
        }

        private static void ValidateBindings(
            in RHIAttachmentInterfaceSignature signature,
            ReadOnlySpan<RHIRasterAttachmentShaderBinding> bindings)
        {
            byte activeMask = checked((byte)(
                signature.ColorInputMask |
                signature.ColorOutputMask));
            byte visitedMask = 0;
            for (int i = 0; i < bindings.Length; ++i)
            {
                ref readonly RHIRasterAttachmentShaderBinding binding =
                    ref bindings[i];
                byte bit = checked((byte)(1 << binding.LogicalAttachment));
                if ((activeMask & bit) == 0 ||
                    (visitedMask & bit) != 0)
                {
                    throw new ArgumentException(
                        $"Attachment shader ABI binding {i} is inactive or duplicated.",
                        nameof(bindings));
                }
                int expectedInput =
                    FindInputSlot(
                        in signature,
                        binding.LogicalAttachment);
                int expectedOutput =
                    FindOutputLocation(
                        in signature,
                        binding.LogicalAttachment);
                if (binding.InputSlot != expectedInput ||
                    binding.OutputLocation != expectedOutput)
                {
                    throw new ArgumentException(
                        $"Attachment shader ABI binding {i} does not match the logical interface.",
                        nameof(bindings));
                }
                visitedMask |= bit;
            }
            if (visitedMask != activeMask)
            {
                throw new ArgumentException(
                    "Attachment shader ABI bindings do not cover the logical interface.",
                    nameof(bindings));
            }
        }

        private static string ComputeHash(
            ERHIBackend backend,
            in RHIRasterAttachmentShaderAbiDescriptor descriptor,
            ReadOnlySpan<RHIRasterAttachmentShaderBinding> bindings)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(
                stream,
                Encoding.UTF8,
                leaveOpen: true);
            writer.Write(RHIRasterAttachmentShaderAbi.CurrentRevision);
            writer.Write((byte)backend);
            writer.Write((uint)descriptor.SampleCount);
            writer.Write(descriptor.ColorFormats.Length);
            for (int i = 0; i < descriptor.ColorFormats.Length; ++i)
            {
                writer.Write((uint)descriptor.ColorFormats[i]);
            }
            RHIAttachmentInterfaceSignature signature =
                descriptor.AttachmentInterface;
            writer.Write(signature.ColorAttachmentCount);
            writer.Write(signature.ColorInputSlotCount);
            for (int i = 0; i < signature.ColorInputSlotCount; ++i)
            {
                writer.Write(signature.GetColorInputLogicalAttachment(i));
            }
            writer.Write(signature.ColorOutputLocationCount);
            for (int i = 0; i < signature.ColorOutputLocationCount; ++i)
            {
                writer.Write(signature.GetColorOutputLogicalAttachment(i));
            }
            writer.Write(signature.LayeredAccessMask);
            writer.Write((byte)signature.DepthStencilFlags);
            writer.Write(signature.UsesDualSourceColor);
            writer.Write(bindings.Length);
            for (int i = 0; i < bindings.Length; ++i)
            {
                ref readonly RHIRasterAttachmentShaderBinding binding =
                    ref bindings[i];
                writer.Write(binding.LogicalAttachment);
                writer.Write(binding.InputSlot);
                writer.Write(binding.OutputLocation);
                WriteLocation(writer, binding.Input);
                WriteLocation(writer, binding.Output);
            }
            ReadOnlySpan<byte> layoutIdentity =
                descriptor.PipelineLayout.PipelineCacheIdentity.Span;
            writer.Write(layoutIdentity.Length);
            writer.Write(layoutIdentity);
            writer.Flush();
            return Convert.ToHexString(
                SHA256.HashData(stream.GetBuffer().AsSpan(
                    0,
                    checked((int)stream.Length))));
        }

        private static void WriteLocation(
            BinaryWriter writer,
            RHIRawShaderBindingLocation location)
        {
            writer.Write((byte)location.Kind);
            writer.Write(location.Index);
            writer.Write(location.SetOrSpace);
        }
    }
}
