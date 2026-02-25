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

        public bool IsBindless => Count > 1;

        public MetalBindInfo(in uint slot, in uint index, in uint count, in ERHIBindType type, in ERHIShaderStage stage)
        {
            Slot = slot;
            Index = index;
            Count = count;
            Type = type;
            Stage = stage;
        }
    }

    internal sealed class MetalArgumentTableLayout : RHIArgumentTableLayout
    {
        public uint Index => m_Index;
        public MetalBindInfo[] BindInfos => m_BindInfos;
        public int TotalElementCount => m_TotalElementCount;

        private readonly uint m_Index;
        private readonly MetalBindInfo[] m_BindInfos;
        private readonly int[] m_ElementOffsets;
        private readonly int m_TotalElementCount;

        public MetalArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new MetalBindInfo[descriptor.Elements.Length];
            m_ElementOffsets = new int[descriptor.Elements.Length];

            Span<RHIArgumentTableLayoutElement> elements = descriptor.Elements.Span;
            int offset = 0;
            for (int i = 0; i < elements.Length; ++i)
            {
                ref RHIArgumentTableLayoutElement element = ref elements[i];
                m_BindInfos[i] = new MetalBindInfo(element.Slot, descriptor.Index, element.Count, element.Type, element.Stage);
                m_ElementOffsets[i] = offset;
                offset += (int)Math.Max(1u, element.Count);
            }
            m_TotalElementCount = offset;
        }

        public int GetElementOffset(int bindIndex)
        {
            return m_ElementOffsets[bindIndex];
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalArgumentTable : RHIArgumentTable
    {
        public MetalArgumentTableLayout ArgumentTableLayout => m_Layout;
        public RHIArgumentTableElement[] Elements => m_Elements;

        private readonly MetalArgumentTableLayout m_Layout;
        private readonly RHIArgumentTableElement[] m_Elements;

        public MetalArgumentTable(in RHIArgumentTableDescriptor descriptor)
        {
            m_Layout = descriptor.Layout as MetalArgumentTableLayout ?? throw new ArgumentException("Invalid resource table layout type.", nameof(descriptor));

            // Allocate expanded element array to hold all elements including bindless arrays
            m_Elements = new RHIArgumentTableElement[m_Layout.TotalElementCount];

            // Copy initial elements from descriptor (one per bind slot)
            Span<RHIArgumentTableElement> srcElements = descriptor.Elements.Span;
            for (int i = 0; i < m_Layout.BindInfos.Length && i < srcElements.Length; ++i)
            {
                int elementOffset = m_Layout.GetElementOffset(i);
                m_Elements[elementOffset] = srcElements[i];
            }
        }

        public RHIArgumentTableElement GetElement(int bindIndex, int arrayIndex)
        {
            int offset = m_Layout.GetElementOffset(bindIndex) + arrayIndex;
            return m_Elements[offset];
        }

        public int GetBindCount()
        {
            return m_Layout.BindInfos.Length;
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot)
        {
            for (int i = 0; i < m_Layout.BindInfos.Length; ++i)
            {
                ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[i];
                if (bindInfo.Slot == (uint)slot && bindInfo.Type == bindType)
                {
                    int elementOffset = m_Layout.GetElementOffset(i);
                    m_Elements[elementOffset] = element;
                    return;
                }
            }
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot, in int arrayIndex)
        {
            for (int i = 0; i < m_Layout.BindInfos.Length; ++i)
            {
                ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[i];
                if (bindInfo.Slot == (uint)slot && bindInfo.Type == bindType)
                {
#if DEBUG
                    if (arrayIndex < 0 || arrayIndex >= (int)bindInfo.Count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(arrayIndex), $"arrayIndex {arrayIndex} out of range [0, {bindInfo.Count}) for slot {slot}");
                    }
#endif
                    int elementOffset = m_Layout.GetElementOffset(i) + arrayIndex;
                    m_Elements[elementOffset] = element;
                    return;
                }
            }
        }

        protected override void Release()
        {
        }
    }
}
