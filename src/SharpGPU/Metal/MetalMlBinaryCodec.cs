using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    /// <summary>
    /// MetalPackageV1 (.mtlmlbin): magic "MTLM", version 1, reflection sidecar, embedded .mtlpackage zip payload.
    /// </summary>
    internal static class MetalMlBinaryCodec
    {
        internal const uint Magic = 0x4D4C544D; // "MTLM" little-endian
        internal const byte Version = 1;
        internal const int HeaderSize = 13;

        internal static RHIMLBinary PackFromMtlPackageDirectory(string mtlPackageDirectory, in RHIMLBinaryReflection reflection)
        {
            if (string.IsNullOrWhiteSpace(mtlPackageDirectory) || !Directory.Exists(mtlPackageDirectory))
            {
                throw new DirectoryNotFoundException($"Metal ML package directory not found: {mtlPackageDirectory}");
            }

            byte[] packageZip = ZipDirectoryAsPackageEntry(mtlPackageDirectory);
            using MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(0UL); // placeholder hash
                WriteReflectionBlock(writer, in reflection);
                writer.Write((ulong)packageZip.Length);
                writer.Write(packageZip);
            }

            byte[] container = stream.ToArray();
            ulong contentHash = RHIMLBinaryHash.ComputeContentHash(container.AsSpan(HeaderSize));
            BinaryPrimitives.WriteUInt64LittleEndian(container.AsSpan(5, 8), contentHash);
            RHIMLBinaryReflection storedReflection = reflection;
            if (string.IsNullOrWhiteSpace(storedReflection.EntryName))
            {
                storedReflection.EntryName = MetalMLProgram.DefaultEntryName;
            }

            return new RHIMLBinary(
                ERHIMLBinaryFormat.MetalPackageV1,
                container,
                storedReflection,
                contentHash);
        }

        private static byte[] ZipDirectoryAsPackageEntry(string mtlPackageDirectory)
        {
            string fullPath = Path.GetFullPath(mtlPackageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Loader extracts to a fixed "package.mtlpackage" directory name.
            const string packageName = "package.mtlpackage";

            using MemoryStream archiveStream = new MemoryStream();
            using (ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (string filePath in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(fullPath, filePath).Replace('\\', '/');
                    if (IsAppleDoubleOrJunkPath(relative))
                    {
                        continue;
                    }

                    ZipArchiveEntry entry = archive.CreateEntry(packageName + "/" + relative, CompressionLevel.Optimal);
                    using Stream entryStream = entry.Open();
                    using FileStream fileStream = File.OpenRead(filePath);
                    fileStream.CopyTo(entryStream);
                }
            }

            return archiveStream.ToArray();
        }

        internal static RHIMLBinaryReflection ReadReflection(ReadOnlyMemory<byte> container)
        {
            ValidateContainerHeader(container.Span, out int reflectionOffset);
            return ReadReflectionBlock(container.Span.Slice(reflectionOffset));
        }

        internal static MetalMLProgram LoadProgram(MetalDevice device, RHIMLBinary binary, string pipelineName)
        {
            if (binary.Format != ERHIMLBinaryFormat.MetalPackageV1)
            {
                throw new InvalidOperationException($"Metal ML binary loader requires {nameof(ERHIMLBinaryFormat.MetalPackageV1)}.");
            }

            ValidateContainerHeader(binary.Payload.Span, out int reflectionOffset);
            RHIMLBinaryReflection reflection = ReadReflectionBlock(binary.Payload.Span.Slice(reflectionOffset));
            int packageOffset = reflectionOffset + MeasureReflectionBlock(reflection);
            ReadOnlySpan<byte> container = binary.Payload.Span;
            if (packageOffset + 8 > container.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 container is missing package length.");
            }

            ulong packageLength = BinaryPrimitives.ReadUInt64LittleEndian(container.Slice(packageOffset, 8));
            int packageBytesOffset = packageOffset + 8;
            if (packageBytesOffset + (long)packageLength > container.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 embedded package bytes are truncated.");
            }

            ReadOnlySpan<byte> packageBytes = container.Slice(packageBytesOffset, checked((int)packageLength));
            string tempRoot = Path.Combine(Path.GetTempPath(), "sharpgpu-mtlmlbin-" + Guid.NewGuid().ToString("N"));
            string packagePath = Path.Combine(tempRoot, "package.mtlpackage");
            Directory.CreateDirectory(tempRoot);
            try
            {
                ExtractPackageArchive(packageBytes, packagePath);
                NSURL packageUrl = NSURL.FileURLWithPath(new NSString(packagePath));
                NSError error = default;
                MTLLibrary library = device.NativeDevice.NewLibrary(packageUrl, ref error);
                if (library.NativePtr == IntPtr.Zero)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to load MTLLibrary from MetalPackageV1 payload: {errorText}");
                }

                // Keep extracted package alive for the MetalMLProgram lifetime: MTLLibrary loaded from
                // URL may still depend on on-disk package contents during pipeline compilation.
                return new MetalMLProgram(
                    pipelineName,
                    library,
                    reflection.EntryName ?? MetalMLProgram.DefaultEntryName,
                    reflection.Bindings ?? Array.Empty<RHIMLTensorBindingInfo>(),
                    tempRoot);
            }
            catch
            {
                TryDeleteDirectory(tempRoot);
                throw;
            }
        }

        private static void ExtractPackageArchive(ReadOnlySpan<byte> packageBytes, string packagePath)
        {
            using MemoryStream archiveStream = new MemoryStream(packageBytes.ToArray(), writable: false);
            using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            string extractRoot = Path.GetDirectoryName(packagePath)
                ?? throw new InvalidOperationException("MetalPackageV1 extract root is unavailable.");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.Combine(extractRoot, entry.FullName);
                string? destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            if (!Directory.Exists(packagePath))
            {
                throw new InvalidOperationException(
                    "MetalPackageV1 archive must contain a .mtlpackage directory entry.");
            }
        }

        private static void ValidateContainerHeader(ReadOnlySpan<byte> container, out int reflectionOffset)
        {
            if (container.Length < HeaderSize)
            {
                throw new InvalidOperationException("MetalPackageV1 container is too small.");
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(container.Slice(0, 4));
            if (magic != Magic)
            {
                throw new InvalidOperationException($"MetalPackageV1 magic mismatch. expected=0x{Magic:X8}, actual=0x{magic:X8}.");
            }

            byte version = container[4];
            if (version != Version)
            {
                throw new InvalidOperationException($"MetalPackageV1 version mismatch. expected={Version}, actual={version}.");
            }

            ReadOnlySpan<byte> tail = container.Slice(HeaderSize);
            ulong storedHash = BinaryPrimitives.ReadUInt64LittleEndian(container.Slice(5, 8));
            ulong computedHash = RHIMLBinaryHash.ComputeContentHash(tail);
            if (storedHash != computedHash)
            {
                throw new InvalidOperationException(
                    $"MetalPackageV1 content hash mismatch. expected=0x{storedHash:X16}, actual=0x{computedHash:X16}.");
            }

            reflectionOffset = HeaderSize;
        }

        private static RHIMLBinaryReflection ReadReflectionBlock(ReadOnlySpan<byte> payload)
        {
            int offset = 0;
            string entryName = ReadString(payload, ref offset);
            int bindingCount = ReadInt32(payload, ref offset);
            RHIMLTensorBindingInfo[] bindings = new RHIMLTensorBindingInfo[bindingCount];
            for (int i = 0; i < bindingCount; ++i)
            {
                bindings[i] = ReadBinding(payload, ref offset);
            }

            ulong intermediateHeapSizeHint = ReadUInt64(payload, ref offset);
            return new RHIMLBinaryReflection
            {
                EntryName = entryName,
                Bindings = bindings,
                IntermediateHeapSizeHint = intermediateHeapSizeHint,
            };
        }

        private static int MeasureReflectionBlock(in RHIMLBinaryReflection reflection)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            WriteReflectionBlock(writer, in reflection);
            return checked((int)stream.Length);
        }

        private static void WriteReflectionBlock(BinaryWriter writer, in RHIMLBinaryReflection reflection)
        {
            WriteString(writer, reflection.EntryName ?? MetalMLProgram.DefaultEntryName);
            writer.Write(reflection.Bindings?.Length ?? 0);
            if (reflection.Bindings is { Length: > 0 } bindings)
            {
                for (int i = 0; i < bindings.Length; ++i)
                {
                    WriteBinding(writer, in bindings[i]);
                }
            }

            writer.Write(reflection.IntermediateHeapSizeHint);
        }

        private static void WriteBinding(BinaryWriter writer, in RHIMLTensorBindingInfo binding)
        {
            WriteString(writer, binding.Name ?? string.Empty);
            writer.Write(binding.Index);
            writer.Write((byte)binding.Kind);
            WriteTensorDescriptor(writer, in binding.Descriptor);
        }

        private static RHIMLTensorBindingInfo ReadBinding(ReadOnlySpan<byte> payload, ref int offset)
        {
            return new RHIMLTensorBindingInfo
            {
                Name = ReadString(payload, ref offset),
                Index = ReadUInt32(payload, ref offset),
                Kind = (ERHIMLTensorBindingKind)ReadByte(payload, ref offset),
                Descriptor = ReadTensorDescriptor(payload, ref offset),
            };
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
            if (ReadByte(payload, ref offset) != 0)
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
                throw new InvalidOperationException("MetalPackageV1 string length is out of range.");
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

        private static ulong ReadUInt64(ReadOnlySpan<byte> payload, ref int offset)
        {
            EnsureRemaining(payload, offset, 8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(offset, 8));
            offset += 8;
            return value;
        }

        private static void EnsureRemaining(ReadOnlySpan<byte> payload, int offset, int required)
        {
            if (offset + required > payload.Length)
            {
                throw new InvalidOperationException("MetalPackageV1 payload is truncated.");
            }
        }

        private static bool IsAppleDoubleOrJunkPath(string relativePath)
        {
            string[] parts = relativePath.Split('/', '\\');
            for (int i = 0; i < parts.Length; ++i)
            {
                string part = parts[i];
                if (part.StartsWith("._", StringComparison.Ordinal)
                    || string.Equals(part, ".DS_Store", StringComparison.Ordinal)
                    || string.Equals(part, "__MACOSX", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }
    }
}
