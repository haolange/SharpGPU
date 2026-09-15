using System;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal readonly struct Dx12SamplerInternKey : IEquatable<Dx12SamplerInternKey>
    {
        public float LodMin { get; }
        public float LodMax { get; }
        public float MipLODBias { get; }
        public uint Anisotropy { get; }
        public ERHIFilterMode MinFilter { get; }
        public ERHIFilterMode MagFilter { get; }
        public ERHIFilterMode MipFilter { get; }
        public ERHIAddressMode AddressModeU { get; }
        public ERHIAddressMode AddressModeV { get; }
        public ERHIAddressMode AddressModeW { get; }
        public ERHIComparisonMode ComparisonMode { get; }

        private Dx12SamplerInternKey(in RHISamplerDescriptor descriptor)
        {
            LodMin = descriptor.LodMin;
            LodMax = descriptor.LodMax;
            MipLODBias = descriptor.MipLODBias;
            Anisotropy = descriptor.Anisotropy;
            MinFilter = descriptor.MinFilter;
            MagFilter = descriptor.MagFilter;
            MipFilter = descriptor.MipFilter;
            AddressModeU = descriptor.AddressModeU;
            AddressModeV = descriptor.AddressModeV;
            AddressModeW = descriptor.AddressModeW;
            ComparisonMode = descriptor.ComparisonMode;
        }

        public static Dx12SamplerInternKey From(in RHISamplerDescriptor descriptor)
        {
            return new Dx12SamplerInternKey(descriptor);
        }

        public bool Equals(Dx12SamplerInternKey other)
        {
            return LodMin.Equals(other.LodMin)
                && LodMax.Equals(other.LodMax)
                && MipLODBias.Equals(other.MipLODBias)
                && Anisotropy == other.Anisotropy
                && MinFilter == other.MinFilter
                && MagFilter == other.MagFilter
                && MipFilter == other.MipFilter
                && AddressModeU == other.AddressModeU
                && AddressModeV == other.AddressModeV
                && AddressModeW == other.AddressModeW
                && ComparisonMode == other.ComparisonMode;
        }

        public override bool Equals(object? obj)
        {
            return obj is Dx12SamplerInternKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(LodMin);
            hash.Add(LodMax);
            hash.Add(MipLODBias);
            hash.Add(Anisotropy);
            hash.Add(MinFilter);
            hash.Add(MagFilter);
            hash.Add(MipFilter);
            hash.Add(AddressModeU);
            hash.Add(AddressModeV);
            hash.Add(AddressModeW);
            hash.Add(ComparisonMode);
            return hash.ToHashCode();
        }
    }

    internal sealed class Dx12SamplerInternSlot
    {
        public Dx12CpuDescriptorAllocation Allocation { get; }
        public int RefCount { get; private set; }

        public Dx12SamplerInternSlot(in Dx12CpuDescriptorAllocation allocation)
        {
            Allocation = allocation;
            RefCount = 1;
        }

        public void AddRef()
        {
            RefCount = checked(RefCount + 1);
        }

        public bool ReleaseRef()
        {
            if (RefCount <= 0)
            {
                throw new InvalidOperationException("DX12 sampler intern slot underflowed.");
            }

            RefCount--;
            return RefCount == 0;
        }
    }

    internal unsafe class Dx12Sampler : RHISampler, IDx12DescriptorView
    {
        public Dx12Device Device => m_Dx12Device;
        public Dx12DescriptorClass DescriptorClass => Dx12DescriptorClass.Sampler;
        public Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle => m_Allocation.Descriptor.CpuHandle;

        private bool m_HasDescriptors;
        private readonly RHISamplerDescriptor m_Descriptor;
        private Dx12Device m_Dx12Device;
        private Dx12CpuDescriptorAllocation m_Allocation;

        public Dx12Sampler(
            Dx12Device device,
            in RHISamplerDescriptor descriptor,
            in Dx12CpuDescriptorAllocation allocation)
        {
            m_Dx12Device = device;
            m_Descriptor = descriptor;
            m_Allocation = allocation;
            m_HasDescriptors = true;
        }

        internal static Vortice.Direct3D12.SamplerDescription CreateNativeDescription(in RHISamplerDescriptor descriptor)
        {
            return new Vortice.Direct3D12.SamplerDescription
            {
                MinLOD = descriptor.LodMin,
                MaxLOD = descriptor.LodMax,
                MipLODBias = descriptor.MipLODBias,
                MaxAnisotropy = descriptor.Anisotropy,
                Filter = Dx12Utility.ConvertToDx12Filter(descriptor),
                AddressU = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeU),
                AddressV = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeV),
                AddressW = Dx12Utility.ConvertToDx12AddressMode(descriptor.AddressModeW),
                ComparisonFunction = Dx12Utility.ConvertToDx12ComparisonMode(descriptor.ComparisonMode),
            };
        }

        protected override void Release()
        {
            if (m_HasDescriptors)
            {
                m_Dx12Device.ReleaseSamplerIntern(m_Descriptor);
                m_HasDescriptors = false;
            }
        }
    }
#pragma warning restore CA1416
}
