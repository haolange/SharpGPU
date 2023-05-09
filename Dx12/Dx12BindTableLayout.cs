using System;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindInfo
    {
        public uint Index;
        public uint BindSlot;
        public EBindType BindType;
        public EFunctionStage Visibility;
        public RHIBindlessDescriptor? BindlessDescriptor;

        internal bool IsBindless => BindlessDescriptor != null;
    }

    internal unsafe class Dx12BindTableLayout : RHIBindTableLayout
    {
        public uint Index
        {
            get
            {
                return m_Index;
            }
        }
        public Dx12BindInfo[] BindInfos
        {
            get
            {
                return m_BindInfos;
            }
        }

        private uint m_Index;
        private Dx12BindInfo[] m_BindInfos;

        public Dx12BindTableLayout(in RHIBindTableLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new Dx12BindInfo[descriptor.Elements.Length];

            Span<RHIBindTableLayoutElement> elements = descriptor.Elements.Span;
            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref RHIBindTableLayoutElement element = ref elements[i];
                ref Dx12BindInfo bindInfo = ref m_BindInfos[i];
                bindInfo.Index = descriptor.Index;
                bindInfo.BindSlot = element.BindSlot;
                bindInfo.BindType = element.BindType;
                bindInfo.Visibility = element.Visibility;
                bindInfo.BindlessDescriptor = element.BindlessDescriptor;
            }
        }

        protected override void Release()
        {
            m_Index = 0;
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
