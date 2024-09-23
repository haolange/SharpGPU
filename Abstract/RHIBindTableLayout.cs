using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIResourceTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIPipelineStage Visible;
    }
    
    public struct RHIResourceTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIResourceTableLayoutElement> Elements;
    }

    public abstract class RHIResourceTableLayout : Disposal
    {

    }
}
