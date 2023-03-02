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

    public abstract class RHIFunctionTable : Disposal
    {
        public abstract void SetRayGenerationProgram(string exportName, RHIBindGroup[]? bindGroups = null);
        public abstract int AddMissProgram(string exportName, RHIBindGroup[]? bindGroups = null);
        public abstract int AddHitGroupProgram(string exportName, RHIBindGroup[]? bindGroups = null);
        public abstract void SetMissProgram(in int index, RHIBindGroup[]? bindGroups = null);
        public abstract void SetHitGroupProgram(in int index, RHIBindGroup[]? bindGroups = null);
        public abstract void ClearMissPrograms();
        public abstract void ClearHitGroupPrograms();
        public abstract void Generate(RHIRaytracingPipeline rayTracingPipeline);
        public abstract void Update(RHIRaytracingPipeline rayTracingPipeline);
    }
}
