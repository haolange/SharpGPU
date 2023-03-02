using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindGroupLayoutElement
    {
        public uint Count;
        public uint BindSlot;
        public EBindType BindType;
        public EFunctionStage FunctionStage;
    }
    
    public struct RHIBindGroupLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIBindGroupLayoutElement> Elements;
    }

    public abstract class RHIBindGroupLayout : Disposal
    {

    }
}
