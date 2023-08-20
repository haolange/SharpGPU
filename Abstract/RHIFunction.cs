using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIFunctionDescriptor
    {
        public uint ByteSize;
        public IntPtr ByteCode;
        public string EntryName;
        public EFunctionType Type;
    }

    public struct RHIRayFunctionDescriptor
    {
        public string EntryName;
    }

    public struct RHIFunctionLibraryDescriptor
    {
        public uint ByteSize;
        public IntPtr ByteCode;
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
        public abstract void SetRayGenerationProgram(string exportName, RHIBindTable[]? bindTables = null);
        public abstract int AddMissProgram(string exportName, RHIBindTable[]? bindTables = null);
        public abstract int AddHitGroupProgram(string exportName, RHIBindTable[]? bindTables = null);
        public abstract void SetMissProgram(in int index, string exportName, RHIBindTable[]? bindTables = null);
        public abstract void SetHitGroupProgram(in int index, string exportName, RHIBindTable[]? bindTables = null);
        public abstract void ClearMissPrograms();
        public abstract void ClearHitGroupPrograms();
        public abstract void Generate(RHIRaytracingPipelineState pipelineState);
        public abstract void Update(RHIRaytracingPipelineState pipelineState);
    }
}
