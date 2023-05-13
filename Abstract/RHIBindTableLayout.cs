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
        public uint BindSlot;
        public EBindType BindType;
        public EFunctionStage Visibility;
        public RHIBindlessDescriptor? BindlessDescriptor;
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
