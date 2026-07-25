using System;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum ERHIArgumentBindingRequirement : byte
    {
        Required,
        Optional
    }

    public struct RHIArgumentTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIShaderStageMask Stages;
        public ERHIArgumentBindingRequirement Requirement;
    }

    public struct RHIArgumentTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIArgumentTableLayoutElement> Elements;
    }

    public abstract class RHIArgumentTableLayout : Disposal
    {
        internal uint CanonicalIndex => m_CanonicalIndex;
        internal ReadOnlySpan<RHIArgumentTableLayoutElement> CanonicalElements =>
            m_CanonicalElements;

        private readonly uint m_CanonicalIndex;
        private readonly RHIArgumentTableLayoutElement[] m_CanonicalElements;

        protected RHIArgumentTableLayout(
            in RHIArgumentTableLayoutDescriptor descriptor)
        {
            m_CanonicalIndex = descriptor.Index;
            m_CanonicalElements = descriptor.Elements.Span.ToArray();
        }
    }

    public struct RHIArgumentTableElement
    {
        //public int Slot;
        //public ERHIBindType BindType;
        public RHISampler Sampler;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHITopLevelAccelStruct AccelStruct;
    }

    public struct RHIArgumentTableDescriptor
    {
        public RHIArgumentTableLayout Layout;
        public Memory<RHIArgumentTableElement> Elements;
    }

    public abstract class RHIArgumentTable : Disposal
    {
        public abstract void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot);
        public abstract void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex);
    }
}
