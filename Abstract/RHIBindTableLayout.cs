using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindTableLayoutElement
    {
        public uint Count;
        public uint BindSlot;
        public EBindType BindType;
        public EFunctionStage FunctionStage;
    }
    
    public struct RHIBindTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIBindTableLayoutElement> Elements;
    }

    public abstract class RHIBindTableLayout : Disposal
    {

    }
}
