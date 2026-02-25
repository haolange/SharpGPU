using System;
using Infinity.Core;

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
        public abstract void SetRayGenerationProgram(string exportName, RHIArgumentTable[]? resourceTables = null);
        public abstract int AddMissProgram(string exportName, RHIArgumentTable[]? resourceTables = null);
        public abstract int AddHitGroupProgram(string exportName, RHIArgumentTable[]? resourceTables = null);
        public abstract void SetMissProgram(in int index, string exportName, RHIArgumentTable[]? resourceTables = null);
        public abstract void SetHitGroupProgram(in int index, string exportName, RHIArgumentTable[]? resourceTables = null);
        public abstract void ClearMissPrograms();
        public abstract void ClearHitGroupPrograms();
        public abstract void Generate(RHIRaytracingPipeline pipeline);
        public abstract void Update(RHIRaytracingPipeline pipeline);
    }
}
