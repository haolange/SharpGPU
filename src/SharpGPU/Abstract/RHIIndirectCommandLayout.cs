using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    public enum ERHIIndirectDomain : byte
    {
        Compute = 0,
        Raster = 1
    }

    public enum ERHIIndirectTokenType : byte
    {
        VertexBuffer = 0,
        IndexBuffer = 1,
        Draw = 16,
        DrawIndexed = 17,
        Dispatch = 18,
        DispatchMesh = 19
    }

    public enum ERHIIndirectIndexFormat : uint
    {
        UInt32 = 42,
        UInt16 = 57
    }

    public readonly struct RHIIndirectTokenDescriptor
    {
        public ERHIIndirectTokenType Type { get; }
        public uint VertexBufferSlot { get; }

        public RHIIndirectTokenDescriptor(
            ERHIIndirectTokenType type,
            uint vertexBufferSlot = 0)
        {
            if (!Enum.IsDefined(type))
            {
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown indirect token type.");
            }
            if (type != ERHIIndirectTokenType.VertexBuffer &&
                vertexBufferSlot != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(vertexBufferSlot),
                    "Only a vertex-buffer token may specify a vertex-buffer slot.");
            }

            Type = type;
            VertexBufferSlot = vertexBufferSlot;
        }
    }

    public readonly struct RHIIndirectCommandLayoutDescriptor
    {
        public ERHIIndirectDomain Domain { get; }
        public ReadOnlyMemory<RHIIndirectTokenDescriptor> Tokens { get; }

        public RHIIndirectCommandLayoutDescriptor(
            ERHIIndirectDomain domain,
            ReadOnlyMemory<RHIIndirectTokenDescriptor> tokens)
        {
            if (!Enum.IsDefined(domain))
            {
                throw new ArgumentOutOfRangeException(nameof(domain), domain, "Unknown indirect command domain.");
            }

            Domain = domain;
            Tokens = tokens;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RHIIndirectVertexBufferArgs
    {
        public ulong GpuVirtualAddress;
        public uint SizeInBytes;
        public uint StrideInBytes;

        public RHIIndirectVertexBufferArgs(
            ulong gpuVirtualAddress,
            uint sizeInBytes,
            uint strideInBytes)
        {
            if (gpuVirtualAddress == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gpuVirtualAddress));
            }
            if (sizeInBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
            }
            if (strideInBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(strideInBytes));
            }

            GpuVirtualAddress = gpuVirtualAddress;
            SizeInBytes = sizeInBytes;
            StrideInBytes = strideInBytes;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RHIIndirectIndexBufferArgs
    {
        public ulong GpuVirtualAddress;
        public uint SizeInBytes;
        public ERHIIndirectIndexFormat Format;

        public RHIIndirectIndexBufferArgs(
            ulong gpuVirtualAddress,
            uint sizeInBytes,
            ERHIIndirectIndexFormat format)
        {
            if (gpuVirtualAddress == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(gpuVirtualAddress));
            }
            if (sizeInBytes == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeInBytes));
            }
            if (format is not ERHIIndirectIndexFormat.UInt16 and not ERHIIndirectIndexFormat.UInt32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "Indirect index records require UInt16 or UInt32 index data.");
            }

            GpuVirtualAddress = gpuVirtualAddress;
            SizeInBytes = sizeInBytes;
            Format = format;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct RHIIndirectDispatchMeshArgs
    {
        public uint GroupCountX;
        public uint GroupCountY;
        public uint GroupCountZ;

        public RHIIndirectDispatchMeshArgs(
            uint groupCountX,
            uint groupCountY,
            uint groupCountZ)
        {
            GroupCountX = groupCountX;
            GroupCountY = groupCountY;
            GroupCountZ = groupCountZ;
        }
    }

    public abstract class RHIIndirectCommandLayout : SharpGPU.Core.Disposal
    {
        private readonly RHIIndirectTokenDescriptor[] m_Tokens;
        private readonly uint[] m_TokenOffsets;

        public ERHIIndirectDomain Domain { get; }
        public ReadOnlyMemory<RHIIndirectTokenDescriptor> Tokens => m_Tokens;
        public uint RecordStride { get; }

        internal ERHIIndirectTokenType ActionTokenType =>
            m_Tokens[m_Tokens.Length - 1].Type;
        internal int TokenCount => m_Tokens.Length;

        protected RHIIndirectCommandLayout(
            in RHIIndirectCommandLayoutDescriptor descriptor)
        {
            if (!Enum.IsDefined(descriptor.Domain))
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor));
            }

            RHIIndirectTokenDescriptor[] tokens = descriptor.Tokens.ToArray();
            if (tokens.Length == 0)
            {
                throw new ArgumentException("An indirect command layout requires at least one token.", nameof(descriptor));
            }

            uint[] offsets = new uint[tokens.Length];
            HashSet<uint> vertexSlots = new();
            bool hasIndex = false;
            int actionCount = 0;
            uint stride = 0;
            for (int tokenIndex = 0; tokenIndex < tokens.Length; ++tokenIndex)
            {
                RHIIndirectTokenDescriptor token = tokens[tokenIndex];
                if (!Enum.IsDefined(token.Type))
                {
                    throw new ArgumentOutOfRangeException(nameof(descriptor), "An indirect layout contains an unknown token.");
                }

                bool isAction = IsAction(token.Type);
                if (isAction)
                {
                    ++actionCount;
                    if (tokenIndex != tokens.Length - 1)
                    {
                        throw new ArgumentException("The indirect action token must be last.", nameof(descriptor));
                    }
                    ValidateActionDomain(descriptor.Domain, token.Type);
                }
                else if (descriptor.Domain == ERHIIndirectDomain.Compute)
                {
                    throw new ArgumentException("Compute indirect layouts only support a terminal Dispatch token.", nameof(descriptor));
                }

                if (token.Type == ERHIIndirectTokenType.VertexBuffer &&
                    !vertexSlots.Add(token.VertexBufferSlot))
                {
                    throw new ArgumentException("Vertex-buffer token slots must be unique.", nameof(descriptor));
                }
                if (token.Type == ERHIIndirectTokenType.IndexBuffer)
                {
                    if (hasIndex)
                    {
                        throw new ArgumentException("An indirect layout may contain only one index-buffer token.", nameof(descriptor));
                    }
                    hasIndex = true;
                }

                offsets[tokenIndex] = stride;
                stride = checked(stride + GetTokenSize(token.Type));
            }

            if (actionCount != 1)
            {
                throw new ArgumentException("An indirect layout requires exactly one terminal action token.", nameof(descriptor));
            }
            if ((ActionRequiresIndex(tokens[tokens.Length - 1].Type) && !hasIndex) ||
                (!ActionRequiresIndex(tokens[tokens.Length - 1].Type) && hasIndex))
            {
                throw new ArgumentException(
                    "An index-buffer token is required only for DrawIndexed indirect layouts.",
                    nameof(descriptor));
            }
            if ((stride & 3u) != 0)
            {
                throw new InvalidOperationException("Indirect records must have four-byte alignment.");
            }

            Domain = descriptor.Domain;
            RecordStride = stride;
            m_Tokens = tokens;
            m_TokenOffsets = offsets;
        }

        public uint GetTokenOffset(uint tokenIndex)
        {
            ThrowIfDisposed();
            if (tokenIndex >= (uint)m_TokenOffsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(tokenIndex));
            }
            return m_TokenOffsets[tokenIndex];
        }

        internal void ValidateForDevice(RHIDevice device)
        {
            ArgumentNullException.ThrowIfNull(device);
            device.ThrowIfDeviceUnavailable();
            RHIIndirectCommandBufferCapabilities capabilities =
                device.Capabilities.IndirectCommandBuffer;
            capabilities.Execution.Require("indirect command layout execution");
            for (int i = 0; i < m_Tokens.Length; ++i)
            {
                GetTokenCapability(capabilities.Tokens, m_Tokens[i].Type).Require(
                    $"indirect {m_Tokens[i].Type} token");
            }

            int maximumVertexBindings = device.Limit?.MaxVertexInputBindings ?? 0;
            if (maximumVertexBindings > 0)
            {
                for (int i = 0; i < m_Tokens.Length; ++i)
                {
                    if (m_Tokens[i].Type == ERHIIndirectTokenType.VertexBuffer &&
                        m_Tokens[i].VertexBufferSlot >= (uint)maximumVertexBindings)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(m_Tokens),
                            "The indirect vertex-buffer slot exceeds the device limit.");
                    }
                }
            }
        }

        internal static void ValidateExecution(
            RHIIndirectCommandLayout layout,
            RHIBuffer argumentsBuffer,
            uint argumentsOffset,
            RHIBuffer? countBuffer,
            uint countOffset,
            uint maximumCommandCount,
            string operation)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(argumentsBuffer);
            layout.ThrowIfDisposed();
            if (maximumCommandCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCommandCount));
            }

            ValidateIndirectBuffer(argumentsBuffer, argumentsOffset, operation, nameof(argumentsBuffer));
            ulong requiredEnd = checked(
                (ulong)argumentsOffset + (ulong)maximumCommandCount * layout.RecordStride);
            if (requiredEnd > (ulong)argumentsBuffer.Descriptor.ByteSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(argumentsOffset),
                    "The indirect record range exceeds the arguments buffer.");
            }

            if (countBuffer != null)
            {
                ValidateIndirectBuffer(countBuffer, countOffset, operation, nameof(countBuffer));
                if (checked((ulong)countOffset + sizeof(uint)) >
                    (ulong)countBuffer.Descriptor.ByteSize)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(countOffset),
                        "The indirect count uint exceeds the count buffer.");
                }
            }
        }

        private static void ValidateIndirectBuffer(
            RHIBuffer buffer,
            uint offset,
            string operation,
            string parameterName)
        {
            if (buffer.IsDisposed)
            {
                throw new ObjectDisposedException(buffer.GetType().FullName);
            }
            if ((buffer.Descriptor.UsageFlag & ERHIBufferUsage.IndirectBuffer) == 0)
            {
                throw new ArgumentException(
                    $"{operation} requires IndirectBuffer usage.",
                    parameterName);
            }
            if ((offset & 3u) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Indirect offsets must be four-byte aligned.");
            }
        }

        private static bool IsAction(ERHIIndirectTokenType token) =>
            token is ERHIIndirectTokenType.Draw or
                ERHIIndirectTokenType.DrawIndexed or
                ERHIIndirectTokenType.Dispatch or
                ERHIIndirectTokenType.DispatchMesh;

        private static bool ActionRequiresIndex(ERHIIndirectTokenType token) =>
            token == ERHIIndirectTokenType.DrawIndexed;

        private static void ValidateActionDomain(
            ERHIIndirectDomain domain,
            ERHIIndirectTokenType action)
        {
            bool valid = domain == ERHIIndirectDomain.Compute
                ? action == ERHIIndirectTokenType.Dispatch
                : action is ERHIIndirectTokenType.Draw or
                    ERHIIndirectTokenType.DrawIndexed or
                    ERHIIndirectTokenType.DispatchMesh;
            if (!valid)
            {
                throw new ArgumentException(
                    $"{action} is not a valid terminal action for the {domain} indirect domain.");
            }
        }

        private static uint GetTokenSize(ERHIIndirectTokenType token) => token switch
        {
            ERHIIndirectTokenType.VertexBuffer => 16,
            ERHIIndirectTokenType.IndexBuffer => 16,
            ERHIIndirectTokenType.Draw => 16,
            ERHIIndirectTokenType.DrawIndexed => 20,
            ERHIIndirectTokenType.Dispatch => 12,
            ERHIIndirectTokenType.DispatchMesh => 12,
            _ => throw new ArgumentOutOfRangeException(nameof(token))
        };

        private static RHICapability GetTokenCapability(
            RHIIndirectTokenCapabilities capabilities,
            ERHIIndirectTokenType token) => token switch
        {
            ERHIIndirectTokenType.VertexBuffer => capabilities.VertexBuffer,
            ERHIIndirectTokenType.IndexBuffer => capabilities.IndexBuffer,
            ERHIIndirectTokenType.Draw => capabilities.Draw,
            ERHIIndirectTokenType.DrawIndexed => capabilities.DrawIndexed,
            ERHIIndirectTokenType.Dispatch => capabilities.Dispatch,
            ERHIIndirectTokenType.DispatchMesh => capabilities.DispatchMesh,
            _ => throw new ArgumentOutOfRangeException(nameof(token))
        };
    }
}
