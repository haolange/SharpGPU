using System;
using System.Security.Cryptography;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum ERHIShaderPayloadKind : byte
    {
        Dxil = 0,
        SpirV = 1,
        MslSource = 2,
        MetalLibrary = 3,
        Pending = 255
    }

    public enum ERHIFunctionSourceKind : byte
    {
        DirectBytecode = 0,
        LibraryView = 1,
    }

    [Flags]
    public enum ERHIFunctionLibraryReusablePipelineClass : ulong
    {
        None = 0,
        Raytracing = 1UL << 0,
        WorkGraph = 1UL << 1,
        Raster = 1UL << 2,
        Compute = 1UL << 3,
    }

    public struct RHIFunctionDescriptor
    {
        public uint ByteSize;
        public IntPtr ByteCode;
        public string EntryName;
        public ERHIFunctionType Type;
        public ERHIShaderPayloadKind PayloadKind;
    }

    public struct RHIFunctionViewDescriptor
    {
        public string EntryName;
        public ERHIFunctionType Type;
    }

    public struct RHIRayFunctionDescriptor
    {
        public string EntryName;
    }

    public struct RHIFunctionLibraryDescriptor
    {
        public uint ByteSize;
        public IntPtr ByteCode;
        public ERHIShaderPayloadKind PayloadKind;
    }

    public enum ERHIRayShaderTableSection : byte
    {
        RayGeneration = 0,
        Miss = 1,
        Hit = 2,
        Callable = 3,
    }

    public readonly struct RHIRayRecordDescriptor
    {
        public readonly int GroupIndex;
        public readonly ReadOnlyMemory<byte> LocalData;

        public RHIRayRecordDescriptor(in int groupIndex, in ReadOnlyMemory<byte> localData)
        {
            GroupIndex = groupIndex;
            LocalData = localData;
        }
    }

    public abstract class RHIFunction : Disposal
    {
        public const int ContentDigestByteCount = 32;

        public RHIFunctionDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        public ERHIFunctionSourceKind SourceKind
        {
            get
            {
                return m_SourceKind;
            }
        }

        public ReadOnlyMemory<byte> ContentDigest
        {
            get
            {
                return m_ContentDigest;
            }
        }

        protected RHIFunctionDescriptor m_Descriptor;
        protected ERHIFunctionSourceKind m_SourceKind = ERHIFunctionSourceKind.DirectBytecode;
        protected byte[] m_ContentDigest = Array.Empty<byte>();
        protected RHIFunctionLibrary? m_OwningLibrary;

        protected void BindDirectBytecodeSource(in RHIFunctionDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_SourceKind = ERHIFunctionSourceKind.DirectBytecode;
            m_OwningLibrary = null;
            m_ContentDigest = RHIFunctionContentDigest.Compute(
                descriptor.ByteCode,
                descriptor.ByteSize);
        }

        protected void BindLibraryViewSource(
            RHIFunctionLibrary library,
            in RHIFunctionViewDescriptor view,
            ERHIShaderPayloadKind payloadKind,
            ReadOnlyMemory<byte> libraryContentDigest)
        {
            ArgumentNullException.ThrowIfNull(library);
            if (library.IsDisposed)
            {
                throw new ObjectDisposedException(library.GetType().FullName);
            }
            if (string.IsNullOrWhiteSpace(view.EntryName))
            {
                throw new ArgumentException(
                    "A function library view requires an entry name.",
                    nameof(view));
            }
            if (view.Type == ERHIFunctionType.Pending || !Enum.IsDefined(view.Type))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(view),
                    view.Type,
                    "A function library view requires a concrete shader stage.");
            }
            if (payloadKind == ERHIShaderPayloadKind.Pending || !Enum.IsDefined(payloadKind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadKind),
                    payloadKind,
                    "A function library view requires a concrete payload kind.");
            }
            if (libraryContentDigest.Length != ContentDigestByteCount)
            {
                throw new ArgumentException(
                    $"Library content digest must be {ContentDigestByteCount} bytes.",
                    nameof(libraryContentDigest));
            }

            m_Descriptor = new RHIFunctionDescriptor
            {
                ByteSize = 0,
                ByteCode = IntPtr.Zero,
                EntryName = view.EntryName,
                Type = view.Type,
                PayloadKind = payloadKind,
            };
            m_SourceKind = ERHIFunctionSourceKind.LibraryView;
            m_OwningLibrary = library;
            m_ContentDigest = libraryContentDigest.ToArray();
        }

        internal void ThrowIfSourceUnavailable()
        {
            ThrowIfDisposed();
            if (m_SourceKind == ERHIFunctionSourceKind.LibraryView)
            {
                if (m_OwningLibrary == null || m_OwningLibrary.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        m_OwningLibrary?.GetType().FullName ?? GetType().FullName,
                        "A function library view cannot be used after its library is disposed.");
                }
            }
        }

        internal static bool IsRasterOrComputeStage(ERHIFunctionType type)
        {
            return type is
                ERHIFunctionType.Vertex or
                ERHIFunctionType.Fragment or
                ERHIFunctionType.Compute or
                ERHIFunctionType.Task or
                ERHIFunctionType.Mesh;
        }
    }

    public abstract class RHIFunctionLibrary : Disposal
    {
        public RHIFunctionLibraryDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        public ReadOnlyMemory<byte> ContentDigest
        {
            get
            {
                ThrowIfDisposed();
                return m_ContentDigest;
            }
        }

        protected RHIFunctionLibraryDescriptor m_Descriptor;
        protected byte[] m_ContentDigest = Array.Empty<byte>();

        protected void BindLibraryPayload(in RHIFunctionLibraryDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_ContentDigest = RHIFunctionContentDigest.Compute(
                descriptor.ByteCode,
                descriptor.ByteSize);
        }

        public abstract RHIFunction CreateFunction(in RHIFunctionViewDescriptor descriptor);
    }

    internal static unsafe class RHIFunctionContentDigest
    {
        public static byte[] Compute(IntPtr byteCode, uint byteSize)
        {
            if (byteCode == IntPtr.Zero || byteSize == 0)
            {
                throw new ArgumentException("Function payload is empty.");
            }
            if (byteSize > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(byteSize),
                    "Function payload is larger than the supported digest input.");
            }

            return Compute(new ReadOnlySpan<byte>(byteCode.ToPointer(), checked((int)byteSize)));
        }

        public static byte[] Compute(ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty)
            {
                throw new ArgumentException("Function payload is empty.", nameof(payload));
            }

            return SHA256.HashData(payload);
        }
    }

    public abstract class RHIFunctionTable : Disposal
    {
        public abstract void SetRayGenerationRecord(in RHIRayRecordDescriptor record);
        public abstract int AddMissRecord(in RHIRayRecordDescriptor record);
        public abstract int AddHitGroupRecord(in RHIRayRecordDescriptor record);
        public abstract int AddCallableRecord(in RHIRayRecordDescriptor record);
        public abstract void SetMissRecord(in int index, in RHIRayRecordDescriptor record);
        public abstract void SetHitGroupRecord(in int index, in RHIRayRecordDescriptor record);
        public abstract void SetCallableRecord(in int index, in RHIRayRecordDescriptor record);
        public abstract void ClearMissRecords();
        public abstract void ClearHitGroupRecords();
        public abstract void ClearCallableRecords();
        public abstract void UpdateRecord(in ERHIRayShaderTableSection section, in int index, in RHIRayRecordDescriptor record);
        public abstract void Generate(RHIRaytracingPipeline pipeline);
        public abstract void Update();
    }
}
