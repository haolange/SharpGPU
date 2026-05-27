using System;
using SharpGPU.Core;

namespace Infinity.Graphics
{
    public enum ERHIShaderPayloadKind : byte
    {
        Dxil = 0,
        SpirV = 1,
        MslSource = 2,
        MetalLibrary = 3,
        Pending = 255
    }

    public struct RHIFunctionDescriptor
    {
        public uint ByteSize;
        public IntPtr ByteCode;
        public string EntryName;
        public ERHIFunctionType Type;
        public ERHIShaderPayloadKind PayloadKind;
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
        public RHIFunctionDescriptor Descriptor
        {
            get
            {
                return m_Descriptor;
            }
        }

        protected RHIFunctionDescriptor m_Descriptor;
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

        protected RHIFunctionLibraryDescriptor m_Descriptor;
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
