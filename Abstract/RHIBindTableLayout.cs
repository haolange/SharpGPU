using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public EBindType Type;
        public EFunctionStage Visible;
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
