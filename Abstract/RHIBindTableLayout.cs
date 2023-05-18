using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindlessDescriptor
    {
        public uint Count;

        public RHIBindlessDescriptor(in uint count)
        {
            Count = count;
        }
    }

    public struct RHIBindTableLayoutElement
    {
        public uint Slot;
        public EBindType Type;
        public EFunctionStage Visible;
        public RHIBindlessDescriptor? Bindless;
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
