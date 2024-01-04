using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBindTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIPipelineStage Visible;
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
