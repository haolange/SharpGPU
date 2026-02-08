using System;

namespace Infinity.Graphics
{
    internal readonly struct MetalBindInfo
    {
        public readonly uint Slot;
        public readonly uint Index;
        public readonly uint Count;
        public readonly ERHIBindType Type;
        public readonly ERHIShaderStage Stage;

        public MetalBindInfo(in uint slot, in uint index, in uint count, in ERHIBindType type, in ERHIShaderStage stage)
        {
            Slot = slot;
            Index = index;
            Count = count;
            Type = type;
            Stage = stage;
        }
    }

    internal sealed class MetalResourceTableLayout : RHIResourceTableLayout
    {
        public uint Index => m_Index;
        public MetalBindInfo[] BindInfos => m_BindInfos;

        private readonly uint m_Index;
        private readonly MetalBindInfo[] m_BindInfos;

        public MetalResourceTableLayout(in RHIResourceTableLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new MetalBindInfo[descriptor.Elements.Length];
            Span<RHIResourceTableLayoutElement> elements = descriptor.Elements.Span;
            for (int i = 0; i < elements.Length; ++i)
            {
                ref RHIResourceTableLayoutElement element = ref elements[i];
                m_BindInfos[i] = new MetalBindInfo(element.Slot, descriptor.Index, element.Count, element.Type, element.Stage);
            }
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalResourceTable : RHIResourceTable
    {
        public MetalResourceTableLayout ResourceTableLayout => m_Layout;
        public RHIResourceTableElement[] Elements => m_Elements;

        private readonly MetalResourceTableLayout m_Layout;
        private readonly RHIResourceTableElement[] m_Elements;

        public MetalResourceTable(in RHIResourceTableDescriptor descriptor)
        {
            m_Layout = descriptor.Layout as MetalResourceTableLayout ?? throw new ArgumentException("Invalid resource table layout type.", nameof(descriptor));
            m_Elements = new RHIResourceTableElement[descriptor.Elements.Length];
            descriptor.Elements.Span.CopyTo(m_Elements);
        }

        public override void SetBindElement(in RHIResourceTableElement element, in ERHIBindType bindType, in int slot)
        {
            for (int i = 0; i < m_Layout.BindInfos.Length && i < m_Elements.Length; ++i)
            {
                ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[i];
                if (bindInfo.Slot == (uint)slot && bindInfo.Type == bindType)
                {
                    m_Elements[i] = element;
                    return;
                }
            }

            if ((uint)slot < (uint)m_Elements.Length)
            {
                m_Elements[slot] = element;
            }
        }

        protected override void Release()
        {
        }
    }
}
