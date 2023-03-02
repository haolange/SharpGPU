using System;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal struct Dx12BindInfo
    {
        public uint Count;
        public uint Index;
        public uint BindSlot;
        public EBindType BindType;
        public EFunctionStage FunctionStage;

        internal bool IsBindless => BindType == EBindType.Bindless;
    }

    internal unsafe class Dx12BindGroupLayout : RHIBindGroupLayout
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

        public Dx12BindGroupLayout(in RHIBindGroupLayoutDescriptor descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new Dx12BindInfo[descriptor.Elements.Length];

            Span<RHIBindGroupLayoutElement> elements = descriptor.Elements.Span;
            for (int i = 0; i < descriptor.Elements.Length; ++i)
            {
                ref RHIBindGroupLayoutElement element = ref elements[i];
                ref Dx12BindInfo bindInfo = ref m_BindInfos[i];
                bindInfo.Count = element.Count;
                bindInfo.Index = descriptor.Index;
                bindInfo.BindSlot = element.BindSlot;
                bindInfo.BindType = element.BindType;
                bindInfo.FunctionStage = element.FunctionStage;
            }
        }

        protected override void Release()
        {
            m_Index = 0;
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
