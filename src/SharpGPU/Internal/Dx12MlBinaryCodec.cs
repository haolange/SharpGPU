using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SharpGPU
{
    /// <summary>
    /// DirectMLProgramV1 (.dmlbin) container: magic "DMLB", version 1, content hash, program IR payload.
    /// </summary>
    internal static class Dx12MlBinaryCodec
    {
        internal const uint Magic = 0x424C4D44; // "DMLB" little-endian
        internal const byte Version = 1;
        internal const int HeaderSize = 13;

        internal static RHIMLBinary Pack(in RHIMLProgramIR program)
        {
            RHIMLBinaryReflection reflection = BuildReflection(program);
            byte[] payload = SerializeProgram(program);
            byte[] container = WriteContainer(payload);
            ulong contentHash = RHIMLBinaryHash.ComputeContentHash(payload);
            return new RHIMLBinary(
                ERHIMLBinaryFormat.DirectMLProgramV1,
                container,
                reflection,
                contentHash);
        }

        internal static RHIMLProgramIR DeserializeProgram(ReadOnlyMemory<byte> container)
        {
            ReadOnlyMemory<byte> programPayload = ValidateContainer(container, ERHIMLBinaryFormat.DirectMLProgramV1);
            return ReadProgram(programPayload.Span);
        }

        internal static RHIMLBinaryReflection ReadReflection(ReadOnlyMemory<byte> container)
        {
            RHIMLProgramIR program = DeserializeProgram(container);
            return BuildReflection(program);
        }

        internal static byte[] SerializeProgram(in RHIMLProgramIR program)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteProgram(writer, program);
            return stream.ToArray();
        }

        private static byte[] WriteContainer(ReadOnlySpan<byte> programPayload)
        {
            byte[] container = new byte[HeaderSize + programPayload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(0, 4), Magic);
            container[4] = Version;
            ulong hash = RHIMLBinaryHash.ComputeContentHash(programPayload);
            BinaryPrimitives.WriteUInt64LittleEndian(container.AsSpan(5, 8), hash);
            programPayload.CopyTo(container.AsSpan(HeaderSize));
            return container;
        }

        internal static ReadOnlyMemory<byte> ValidateContainer(ReadOnlyMemory<byte> container, ERHIMLBinaryFormat expectedFormat)
        {
            if (expectedFormat != ERHIMLBinaryFormat.DirectMLProgramV1)
            {
                throw new InvalidOperationException($"DX12 ML binary codec cannot decode format '{expectedFormat}'.");
            }

            ReadOnlySpan<byte> bytes = container.Span;
            if (bytes.Length < HeaderSize)
            {
                throw new InvalidOperationException("DirectMLProgramV1 container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
            if (magic != Magic)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 magic mismatch. expected=0x{Magic:X8}, actual=0x{magic:X8}.");
            }

            byte version = bytes[4];
            if (version != Version)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 version mismatch. expected={Version}, actual={version}.");
            }

            ulong storedHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(5, 8));
            ReadOnlySpan<byte> programPayload = bytes.Slice(HeaderSize);
            ulong computedHash = RHIMLBinaryHash.ComputeContentHash(programPayload);
            if (storedHash != computedHash)
            {
                throw new InvalidOperationException(
                    $"DirectMLProgramV1 content hash mismatch. expected=0x{storedHash:X16}, actual=0x{computedHash:X16}.");
            }

            return container.Slice(HeaderSize);
        }

        private static RHIMLBinaryReflection BuildReflection(in RHIMLProgramIR program)
        {
            int bindingCount = program.Inputs.Length + program.Outputs.Length;
            RHIMLTensorBindingInfo[] bindings = new RHIMLTensorBindingInfo[bindingCount];
            int writeIndex = 0;
            for (uint index = 0; index < program.Inputs.Length; ++index)
            {
                bindings[writeIndex++] = new RHIMLTensorBindingInfo
                {
                    Name = $"input{index}",
                    Index = index,
                    Kind = ERHIMLTensorBindingKind.Input,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(in program.Inputs[index]),
                };
            }

            for (uint index = 0; index < program.Outputs.Length; ++index)
            {
                bindings[writeIndex++] = new RHIMLTensorBindingInfo
                {
                    Name = $"output{index}",
                    Index = index,
                    Kind = ERHIMLTensorBindingKind.Output,
                    Descriptor = RHIMLHelpers.CloneLayoutDescriptor(in program.Outputs[index]),
                };
            }

            return new RHIMLBinaryReflection
            {
                EntryName = "main",
                Bindings = bindings,
                IntermediateHeapSizeHint = 0,
            };
        }

        private static void WriteProgram(BinaryWriter writer, in RHIMLProgramIR program)
        {
            WriteString(writer, program.Name ?? string.Empty);
            WriteTensorDescriptors(writer, program.Inputs);
            WriteTensorDescriptors(writer, program.Outputs);
            writer.Write(program.Ops.Length);
            for (int i = 0; i < program.Ops.Length; ++i)
            {
                WriteOp(writer, in program.Ops[i]);
            }
        }

        private static RHIMLProgramIR ReadProgram(ReadOnlySpan<byte> payload)
        {
            int offset = 0;
            string name = ReadString(payload, ref offset);
            RHIMLTensorDescriptor[] inputs = ReadTensorDescriptors(payload, ref offset);
            RHIMLTensorDescriptor[] outputs = ReadTensorDescriptors(payload, ref offset);
            int opCount = ReadInt32(payload, ref offset);
            RHIMLOpDescriptor[] ops = new RHIMLOpDescriptor[opCount];
            for (int i = 0; i < opCount; ++i)
            {
                ops[i] = ReadOp(payload, ref offset);
            }

            if (offset != payload.Length)
            {
                throw new InvalidOperationException($"DirectMLProgramV1 payload has trailing bytes. consumed={offset}, total={payload.Length}.");
            }

            return RHIMLProgramIR.Create(name, inputs, outputs, ops);
        }

        private static void WriteOp(BinaryWriter writer, in RHIMLOpDescriptor op)
        {
            writer.Write((ushort)op.Kind);
            WriteString(writer, op.Name ?? string.Empty);
            writer.Write(op.Inputs.Length);
            for (int i = 0; i < op.Inputs.Length; ++i)
            {
                RHIMLOpTensorRef input = op.Inputs[i];
                writer.Write(input.InputIndex);
                writer.Write(input.OpIndex);
                writer.Write(input.IsOpOutput);
            }

            WriteTensorDescriptor(writer, in op.Output);
            writer.Write(op.Alpha);
            writer.Write(op.Beta);
            writer.Write((byte)op.TransformA);
            writer.Write((byte)op.TransformB);
            writer.Write((byte)op.FusedActivation);
            writer.Write(op.Epsilon);
            if (op.Axes is { Length: > 0 } axes)
            {
                writer.Write(axes.Length);
                for (int i = 0; i < axes.Length; ++i)
                {
                    writer.Write(axes[i]);
                }
            }
            else
            {
                writer.Write(0);
            }
        }

        private static RHIMLOpDescriptor ReadOp(ReadOnlySpan<byte> payload, ref int offset)
        {
            ERHIMLOpKind kind = (ERHIMLOpKind)ReadUInt16(payload, ref offset);
            string name = ReadString(payload, ref offset);
            int inputCount = ReadInt32(payload, ref offset);
            RHIMLOpTensorRef[] inputs = new RHIMLOpTensorRef[inputCount];
            for (int i = 0; i < inputCount; ++i)
            {
                inputs[i] = new RHIMLOpTensorRef
                {
                    InputIndex = ReadInt32(payload, ref offset),
                    OpIndex = ReadInt32(payload, ref offset),
                    IsOpOutput = ReadBoolean(payload, ref offset),
                };
            }

            RHIMLTensorDescriptor output = ReadTensorDescriptor(payload, ref offset);
            RHIMLOpDescriptor op = RHIMLOpDescriptor.Create(kind, inputs, in output, name);
            op.Alpha = ReadSingle(payload, ref offset);
            op.Beta = ReadSingle(payload, ref offset);
            op.TransformA = (ERHIMLMatrixTransform)ReadByte(payload, ref offset);
            op.TransformB = (ERHIMLMatrixTransform)ReadByte(payload, ref offset);
            op.FusedActivation = (ERHIMLFusedActivation)ReadByte(payload, ref offset);
            op.Epsilon = ReadSingle(payload, ref offset);
            int axisCount = ReadInt32(payload, ref offset);
            if (axisCount > 0)
            {
                int[] axes = new int[axisCount];
                for (int i = 0; i < axisCount; ++i)
                {
                    axes[i] = ReadInt32(payload, ref offset);
                }

                op.Axes = axes;
            }

            return op;
        }

        private static void WriteTensorDescriptors(BinaryWriter writer, RHIMLTensorDescriptor[] descriptors)
        {
            writer.Write(descriptors.Length);
            for (int i = 0; i < descriptors.Length; ++i)
            {
                WriteTensorDescriptor(writer, in descriptors[i]);
            }
        }

        private static RHIMLTensorDescriptor[] ReadTensorDescriptors(ReadOnlySpan<byte> payload, ref int offset)
        {
            int count = ReadInt32(payload, ref offset);
            RHIMLTensorDescriptor[] descriptors = new RHIMLTensorDescriptor[count];
            for (int i = 0; i < count; ++i)
            {
                descriptors[i] = ReadTensorDescriptor(payload, ref offset);
            }

            return descriptors;
        }

        private static void WriteTensorDescriptor(BinaryWriter writer, in RHIMLTensorDescriptor descriptor)
        {
            writer.Write((byte)descriptor.DataType);
            writer.Write((uint)descriptor.UsageFlag);
            writer.Write((byte)descriptor.StorageMode);
            uint[] dimensions = descriptor.Dimensions.ToArray();
            writer.Write(dimensions.Length);
            for (int i = 0; i < dimensions.Length; ++i)
            {
                writer.Write(dimensions[i]);
            }

            if (descriptor.Strides is Memory<uint> explicitStrides && explicitStrides.Length > 0)
            {
                uint[] strides = explicitStrides.ToArray();
                writer.Write((byte)1);
                writer.Write(strides.Length);
                for (int i = 0; i < strides.Length; ++i)
                {
                    writer.Write(strides[i]);
                }
            }
            else
            {
                writer.Write((byte)0);
            }
        }

        private static RHIMLTensorDescriptor ReadTensorDescriptor(ReadOnlySpan<byte> payload, ref int offset)
        {
            ERHIMLDataType dataType = (ERHIMLDataType)ReadByte(payload, ref offset);
            ERHITensorUsage usage = (ERHITensorUsage)ReadUInt32(payload, ref offset);
            ERHIStorageMode storageMode = (ERHIStorageMode)ReadByte(payload, ref offset);
            int dimensionCount = ReadInt32(payload, ref offset);
            uint[] dimensions = new uint[dimensionCount];
            for (int i = 0; i < dimensionCount; ++i)
            {
                dimensions[i] = ReadUInt32(payload, ref offset);
            }

            Memory<uint>? strides = null;
            byte hasStrides = ReadByte(payload, ref offset);
            if (hasStrides != 0)
            {
                int strideCount = ReadInt32(payload, ref offset);
                uint[] strideValues = new uint[strideCount];
                for (int i = 0; i < strideCount; ++i)
                {
                    strideValues[i] = ReadUInt32(payload, ref offset);
                }

                strides = strideValues;
            }

            return new RHIMLTensorDescriptor
            {
                DataType = dataType,
                UsageFlag = usage,
                StorageMode = storageMode,
                Dimensions = dimensions,
                Strides = strides,
                BackingBuffer = null,
                BackingBufferOffset = 0,
            };
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
        {
            int length = ReadInt32(payload, ref offset);
            if (length < 0 || offset + length > payload.Length)
            {
                throw new InvalidOperationException("DirectMLProgramV1 string length is out of range.");
            }

            string value = Encoding.UTF8.GetString(payload.Slice(offset, length));
            offset += length;
            return value;
        }

        private static byte ReadByte(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 1);
            byte value = payload[offset];
            ++offset;
            return value;
        }

        private static bool ReadBoolean(ReadOnlySpan<byte> payload, ref int offset)
        {
            return ReadByte(payload, ref offset) != 0;
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, 2));
            offset += 2;
            return value;
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
        {
            return checked((int)ReadUInt32(payload, ref offset));
        }

        private static float ReadSingle(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 4);
            float value = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset, 4));
            offset += 4;
            return value;
        }

        private static void EnsureRemaining(ReadOnlySpan<byte> payload, int offset, int required)
        {
            if (offset + required > payload.Length)
            {
                throw new InvalidOperationException("DirectMLProgramV1 payload is truncated.");
            }
        }
    }
}
