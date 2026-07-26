using System;
using SharpGPU.Core;

namespace SharpGPU
{
    public enum ERHIBindingRequirement : byte
    {
        Required,
        Optional
    }

    public struct RHIBindingTableLayoutElement
    {
        public uint Slot;
        public uint Count;
        public ERHIBindType Type;
        public ERHIShaderStageMask Stages;
        public ERHIBindingRequirement Requirement;
    }

    public struct RHIBindingTableLayoutDescriptor
    {
        public uint Index;
        public Memory<RHIBindingTableLayoutElement> Elements;
    }

    public abstract class RHIBindingTableLayout : Disposal
    {
        internal uint CanonicalIndex => m_CanonicalIndex;
        internal ReadOnlySpan<RHIBindingTableLayoutElement> CanonicalElements =>
            m_CanonicalElements;

        private readonly uint m_CanonicalIndex;
        private readonly RHIBindingTableLayoutElement[] m_CanonicalElements;

        protected RHIBindingTableLayout(
            in RHIBindingTableLayoutDescriptor descriptor)
        {
            m_CanonicalIndex = descriptor.Index;
            m_CanonicalElements = descriptor.Elements.Span.ToArray();
        }
    }

    public struct RHIBindingTableElement
    {
        public RHISampler Sampler;
        public RHIBufferView BufferView;
        public RHITextureView TextureView;
        public RHITopLevelAccelStruct AccelStruct;
    }

    public struct RHIBindingTableDescriptor
    {
        public RHIBindingTableLayout Layout;
        public Memory<RHIBindingTableElement> Elements;
    }

    public abstract class RHIBindingTable : Disposal
    {
        public abstract void SetBindElement(in RHIBindingTableElement element, in ERHIBindType bindType, in int slot);
        public abstract void SetBindElement(in RHIBindingTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex);
    }
}
