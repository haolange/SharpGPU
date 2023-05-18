using System;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindInfo
    {
        public uint Slot;
        public uint Index;
        public EBindType Type;
        public EFunctionStage Visible;
        public RHIBindlessDescriptor? Bindless;

        internal bool IsBindless => Bindless != null;
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
                bindInfo.Slot = element.Slot;
                bindInfo.Type = element.Type;
                bindInfo.Visible = element.Visible;
                bindInfo.Bindless = element.Bindless;
            }
        }

        protected override void Release()
        {
            m_Index = 0;
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
