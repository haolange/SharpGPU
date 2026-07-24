using System;
using System.Collections.Generic;
using SharpGPU.Core;

namespace SharpGPU
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal enum Dx12DescriptorHeapClass : byte
    {
        CbvSrvUav,
        Sampler,
    }

    internal enum Dx12DescriptorClass : byte
    {
        Sampler,
        ShaderResource,
        UnorderedAccess,
        ConstantBuffer,
        AccelerationStructure,
    }

    internal interface IDx12DescriptorView
    {
        Dx12Device Device { get; }
        Dx12DescriptorClass DescriptorClass { get; }
        Vortice.Direct3D12.CpuDescriptorHandle NativeCpuDescriptorHandle { get; }
        Vortice.Direct3D12.GpuDescriptorHandle NativeGpuDescriptorHandle { get; }
    }

    internal readonly struct Dx12BindingKey : IEquatable<Dx12BindingKey>
    {
        public uint Slot { get; }
        public ERHIBindType Type { get; }

        public Dx12BindingKey(in uint slot, in ERHIBindType type)
        {
            Slot = slot;
            Type = type;
        }

        public bool Equals(Dx12BindingKey other)
        {
            return Slot == other.Slot && Type == other.Type;
        }

        public override bool Equals(object? obj)
        {
            return obj is Dx12BindingKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Slot, Type);
        }
    }

    internal readonly struct Dx12ArgumentTableGroupKey : IEquatable<Dx12ArgumentTableGroupKey>
    {
        public Dx12DescriptorHeapClass HeapClass { get; }
        public Vortice.Direct3D12.ShaderVisibility Visibility { get; }

        public Dx12ArgumentTableGroupKey(in Dx12DescriptorHeapClass heapClass, in Vortice.Direct3D12.ShaderVisibility visibility)
        {
            HeapClass = heapClass;
            Visibility = visibility;
        }

        public bool Equals(Dx12ArgumentTableGroupKey other)
        {
            return HeapClass == other.HeapClass && Visibility == other.Visibility;
        }

        public override bool Equals(object? obj)
        {
            return obj is Dx12ArgumentTableGroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(HeapClass, Visibility);
        }
    }

    internal readonly struct Dx12BindInfo
    {
        public uint Slot { get; }
        public uint Index { get; }
        public uint Count { get; }
        public ERHIBindType Type { get; }
        public ERHIShaderStage Stage { get; }
        public int GroupIndex { get; }
        public int DescriptorOffset { get; }
        public int StateOffset { get; }
        public Vortice.Direct3D12.DescriptorRangeType NativeRangeType { get; }
        public Vortice.Direct3D12.ShaderVisibility NativeVisibility { get; }

        public Dx12BindInfo(
            in uint slot,
            in uint index,
            in uint count,
            in ERHIBindType type,
            in ERHIShaderStage stage,
            in int groupIndex,
            in int descriptorOffset,
            in int stateOffset,
            in Vortice.Direct3D12.DescriptorRangeType nativeRangeType,
            in Vortice.Direct3D12.ShaderVisibility nativeVisibility)
        {
            Slot = slot;
            Index = index;
            Count = count;
            Type = type;
            Stage = stage;
            GroupIndex = groupIndex;
            DescriptorOffset = descriptorOffset;
            StateOffset = stateOffset;
            NativeRangeType = nativeRangeType;
            NativeVisibility = nativeVisibility;
        }
    }

    internal sealed class Dx12ArgumentTableGroupPlan
    {
        public Dx12DescriptorHeapClass HeapClass { get; }
        public Vortice.Direct3D12.ShaderVisibility Visibility { get; }
        public int DescriptorCount { get; }
        public int[] BindingIndices { get; }
        public bool RequiresOwnedRange => DescriptorCount > 1 || BindingIndices.Length > 1;

        public Dx12ArgumentTableGroupPlan(
            in Dx12DescriptorHeapClass heapClass,
            in Vortice.Direct3D12.ShaderVisibility visibility,
            in int descriptorCount,
            int[] bindingIndices)
        {
            HeapClass = heapClass;
            Visibility = visibility;
            DescriptorCount = descriptorCount;
            BindingIndices = bindingIndices;
        }
    }

    internal sealed class Dx12ArgumentTableGroupBuilder
    {
        public Dx12DescriptorHeapClass HeapClass { get; }
        public Vortice.Direct3D12.ShaderVisibility Visibility { get; }
        public int DescriptorCount { get; set; }
        public List<int> BindingIndices { get; } = new List<int>();

        public Dx12ArgumentTableGroupBuilder(
            in Dx12DescriptorHeapClass heapClass,
            in Vortice.Direct3D12.ShaderVisibility visibility)
        {
            HeapClass = heapClass;
            Visibility = visibility;
        }
    }

    internal sealed class Dx12ArgumentTableLayout : RHIArgumentTableLayout
    {
        public uint Index { get; }
        public int DescriptorCount { get; }
        public Dx12BindInfo[] BindInfos { get; }
        public Dx12ArgumentTableGroupPlan[] Groups { get; }

        private readonly Dictionary<Dx12BindingKey, int> m_BindingMap;

        public Dx12ArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            Index = descriptor.Index;
            BindInfos = new Dx12BindInfo[descriptor.Elements.Length];
            m_BindingMap = new Dictionary<Dx12BindingKey, int>(descriptor.Elements.Length);

            List<Dx12ArgumentTableGroupBuilder> groupBuilders = new List<Dx12ArgumentTableGroupBuilder>();
            Dictionary<Dx12ArgumentTableGroupKey, int> groupMap = new Dictionary<Dx12ArgumentTableGroupKey, int>();
            Span<RHIArgumentTableLayoutElement> elements = descriptor.Elements.Span;
            int stateOffset = 0;

            for (int i = 0; i < elements.Length; ++i)
            {
                ref RHIArgumentTableLayoutElement element = ref elements[i];
                ValidateElement(element, i);

                Dx12BindingKey bindingKey = new Dx12BindingKey(element.Slot, element.Type);
                if (!m_BindingMap.TryAdd(bindingKey, i))
                {
                    throw new ArgumentException(
                        $"DX12 argument table space {Index} contains duplicate binding ({element.Type}, slot {element.Slot}); SetBindElement cannot address it unambiguously.",
                        nameof(descriptor));
                }

                Vortice.Direct3D12.DescriptorRangeType rangeType = Dx12Utility.ConvertToDx12BindType(element.Type);
                Vortice.Direct3D12.ShaderVisibility visibility = Dx12Utility.ConvertToDx12ShaderType(element.Stage);
                ValidateNativeRangeCollision(element, rangeType, visibility, i);

                Dx12DescriptorHeapClass heapClass = element.Type == ERHIBindType.Sampler
                    ? Dx12DescriptorHeapClass.Sampler
                    : Dx12DescriptorHeapClass.CbvSrvUav;
                Dx12ArgumentTableGroupKey groupKey = new Dx12ArgumentTableGroupKey(heapClass, visibility);
                if (!groupMap.TryGetValue(groupKey, out int groupIndex))
                {
                    groupIndex = groupBuilders.Count;
                    groupMap.Add(groupKey, groupIndex);
                    groupBuilders.Add(new Dx12ArgumentTableGroupBuilder(heapClass, visibility));
                }

                Dx12ArgumentTableGroupBuilder groupBuilder = groupBuilders[groupIndex];
                int descriptorOffset = groupBuilder.DescriptorCount;
                groupBuilder.DescriptorCount = checked(groupBuilder.DescriptorCount + (int)element.Count);
                groupBuilder.BindingIndices.Add(i);

                BindInfos[i] = new Dx12BindInfo(
                    element.Slot,
                    Index,
                    element.Count,
                    element.Type,
                    element.Stage,
                    groupIndex,
                    descriptorOffset,
                    stateOffset,
                    rangeType,
                    visibility);
                stateOffset = checked(stateOffset + (int)element.Count);
            }

            DescriptorCount = stateOffset;
            Groups = new Dx12ArgumentTableGroupPlan[groupBuilders.Count];
            for (int i = 0; i < groupBuilders.Count; ++i)
            {
                Dx12ArgumentTableGroupBuilder builder = groupBuilders[i];
                Groups[i] = new Dx12ArgumentTableGroupPlan(
                    builder.HeapClass,
                    builder.Visibility,
                    builder.DescriptorCount,
                    builder.BindingIndices.ToArray());
            }
        }

        public bool TryGetBindingIndex(in int slot, in ERHIBindType type, out int bindingIndex)
        {
            if (slot < 0)
            {
                bindingIndex = -1;
                return false;
            }

            return m_BindingMap.TryGetValue(new Dx12BindingKey((uint)slot, type), out bindingIndex);
        }

        public bool IsStructurallyCompatibleWith(Dx12ArgumentTableLayout other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (other == null || Index != other.Index || BindInfos.Length != other.BindInfos.Length)
            {
                return false;
            }

            for (int i = 0; i < BindInfos.Length; ++i)
            {
                ref readonly Dx12BindInfo left = ref BindInfos[i];
                ref readonly Dx12BindInfo right = ref other.BindInfos[i];
                if (left.Slot != right.Slot
                    || left.Count != right.Count
                    || left.Type != right.Type
                    || left.Stage != right.Stage)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidateElement(in RHIArgumentTableLayoutElement element, in int elementIndex)
        {
            if (element.Count == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(element.Count),
                    $"DX12 argument table binding {elementIndex} ({element.Type}, slot {element.Slot}) must declare Count greater than zero.");
            }
            if (element.Count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(element.Count),
                    $"DX12 argument table binding {elementIndex} Count {element.Count} exceeds the supported descriptor range.");
            }

            ulong rangeEnd = (ulong)element.Slot + element.Count;
            if (rangeEnd > (ulong)uint.MaxValue + 1UL)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(element.Slot),
                    $"DX12 argument table binding {elementIndex} register range [{element.Slot}, {rangeEnd}) overflows the native register space.");
            }

            _ = Dx12Utility.ConvertToDx12BindType(element.Type);
            _ = Dx12Utility.ConvertToDx12ShaderType(element.Stage);
        }

        private void ValidateNativeRangeCollision(
            in RHIArgumentTableLayoutElement element,
            in Vortice.Direct3D12.DescriptorRangeType rangeType,
            in Vortice.Direct3D12.ShaderVisibility visibility,
            in int elementIndex)
        {
            ulong rangeStart = element.Slot;
            ulong rangeEnd = rangeStart + element.Count;

            for (int i = 0; i < elementIndex; ++i)
            {
                ref readonly Dx12BindInfo existing = ref BindInfos[i];
                if (existing.NativeRangeType != rangeType
                    || !ShaderVisibilityOverlaps(existing.NativeVisibility, visibility))
                {
                    continue;
                }

                ulong existingStart = existing.Slot;
                ulong existingEnd = existingStart + existing.Count;
                if (rangeStart < existingEnd && existingStart < rangeEnd)
                {
                    throw new ArgumentException(
                        $"DX12 argument table space {Index} has overlapping {rangeType} register ranges "
                        + $"[{existingStart}, {existingEnd}) and [{rangeStart}, {rangeEnd}) with overlapping shader visibility.",
                        nameof(element));
                }
            }
        }

        private static bool ShaderVisibilityOverlaps(
            in Vortice.Direct3D12.ShaderVisibility left,
            in Vortice.Direct3D12.ShaderVisibility right)
        {
            return left == Vortice.Direct3D12.ShaderVisibility.All
                || right == Vortice.Direct3D12.ShaderVisibility.All
                || left == right;
        }
    }

    internal sealed class Dx12NullDescriptorCache : Disposal
    {
        private readonly object m_Gate = new object();
        private readonly Dx12Device m_Device;
        private readonly Dx12DescriptorPair?[] m_Descriptors = new Dx12DescriptorPair?[(int)ERHIBindType.Pending];

        public Dx12NullDescriptorCache(Dx12Device device)
        {
            m_Device = device;
        }

        public Dx12DescriptorPair Get(in ERHIBindType bindType)
        {
            int descriptorIndex = (int)bindType;
            if (descriptorIndex < 0 || descriptorIndex >= m_Descriptors.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(bindType), bindType, "DX12 cannot create a null descriptor for this bind type.");
            }

            lock (m_Gate)
            {
                if (m_Descriptors[descriptorIndex].HasValue)
                {
                    return m_Descriptors[descriptorIndex]!.Value;
                }

                Dx12DescriptorPair descriptor = Create(bindType);
                m_Descriptors[descriptorIndex] = descriptor;
                return descriptor;
            }
        }

        protected override void Release()
        {
            lock (m_Gate)
            {
                for (int i = 0; i < m_Descriptors.Length; ++i)
                {
                    if (!m_Descriptors[i].HasValue)
                    {
                        continue;
                    }

                    Dx12DescriptorPair descriptor = m_Descriptors[i]!.Value;
                    m_Device.FreeDescriptorPair(descriptor);
                    m_Descriptors[i] = null;
                }
            }
        }

        private Dx12DescriptorPair Create(in ERHIBindType bindType)
        {
            bool isSampler = bindType == ERHIBindType.Sampler;
            Dx12DescriptorPair descriptors = isSampler
                ? m_Device.AllocateSamplerDescriptorPair()
                : m_Device.AllocateCbvSrvUavDescriptorPair();

            try
            {
                if (isSampler)
                {
                    Vortice.Direct3D12.SamplerDescription sampler = Vortice.Direct3D12.SamplerDescription.PointClamp;
                    m_Device.NativeDevice.CreateSampler(ref sampler, descriptors.Staging.CpuHandle);
                    m_Device.CopyDescriptorToShaderVisible(descriptors);
                    return descriptors;
                }

                if (bindType == ERHIBindType.UniformBuffer)
                {
                    m_Device.NativeDevice.CreateConstantBufferView(null, descriptors.Staging.CpuHandle);
                    m_Device.CopyDescriptorToShaderVisible(descriptors);
                    return descriptors;
                }

                if (Dx12Utility.ConvertToDx12BindType(bindType) == Vortice.Direct3D12.DescriptorRangeType.ShaderResourceView)
                {
                    Vortice.Direct3D12.ShaderResourceViewDescription srv = CreateNullSrvDescription(bindType);
                    m_Device.NativeDevice.CreateShaderResourceView(null, srv, descriptors.Staging.CpuHandle);
                    m_Device.CopyDescriptorToShaderVisible(descriptors);
                    return descriptors;
                }

                Vortice.Direct3D12.UnorderedAccessViewDescription uav = CreateNullUavDescription(bindType);
                m_Device.NativeDevice.CreateUnorderedAccessView(null, null, uav, descriptors.Staging.CpuHandle);
                m_Device.CopyDescriptorToShaderVisible(descriptors);
                return descriptors;
            }
            catch
            {
                m_Device.FreeDescriptorPair(descriptors);
                throw;
            }
        }

        private static Vortice.Direct3D12.ShaderResourceViewDescription CreateNullSrvDescription(in ERHIBindType bindType)
        {
            Vortice.Direct3D12.ShaderResourceViewDescription description = new Vortice.Direct3D12.ShaderResourceViewDescription
            {
                Format = Vortice.DXGI.Format.Unknown,
                Shader4ComponentMapping = 5768,
            };

            if (bindType == ERHIBindType.Buffer)
            {
                description.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Buffer;
                description.Buffer.NumElements = 1;
                description.Buffer.StructureByteStride = sizeof(uint);
                return description;
            }
            if (bindType == ERHIBindType.AccelStruct)
            {
                description.ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.RaytracingAccelerationStructure;
                description.RaytracingAccelerationStructure.Location = 0;
                return description;
            }

            ERHITextureDimension dimension = GetTextureDimension(bindType);
            description.Format = Vortice.DXGI.Format.R32_Float;
            description.ViewDimension = Dx12Utility.ConvertToDx12TextureSRVDimension(dimension);
            RHITextureViewDescriptor nullView = new RHITextureViewDescriptor
            {
                MipCount = 1,
                ArrayCount = 1,
            };
            Dx12Utility.FillTexture2DSRV(ref description.Texture2D, nullView, dimension);
            Dx12Utility.FillTexture2DArraySRV(ref description.Texture2DArray, nullView, dimension);
            Dx12Utility.FillTextureCubeSRV(ref description.TextureCube, nullView, dimension);
            Dx12Utility.FillTextureCubeArraySRV(ref description.TextureCubeArray, nullView, dimension);
            Dx12Utility.FillTexture3DSRV(ref description.Texture3D, nullView, dimension);
            if (dimension == ERHITextureDimension.Texture2DArrayMS)
            {
                description.Texture2DMSArray.FirstArraySlice = 0;
                description.Texture2DMSArray.ArraySize = 1;
            }
            return description;
        }

        private static Vortice.Direct3D12.UnorderedAccessViewDescription CreateNullUavDescription(in ERHIBindType bindType)
        {
            Vortice.Direct3D12.UnorderedAccessViewDescription description = new Vortice.Direct3D12.UnorderedAccessViewDescription
            {
                Format = Vortice.DXGI.Format.Unknown,
            };

            if (bindType == ERHIBindType.StorageBuffer)
            {
                description.ViewDimension = Vortice.Direct3D12.UnorderedAccessViewDimension.Buffer;
                description.Buffer.NumElements = 1;
                description.Buffer.StructureByteStride = sizeof(uint);
                return description;
            }

            ERHITextureDimension dimension = GetTextureDimension(bindType);
            description.Format = Vortice.DXGI.Format.R32_Float;
            description.ViewDimension = Dx12Utility.ConvertToDx12TextureUAVDimension(dimension);
            RHITextureViewDescriptor nullView = new RHITextureViewDescriptor
            {
                MipCount = 1,
                ArrayCount = 1,
            };
            Dx12Utility.FillTexture2DUAV(ref description.Texture2D, nullView, dimension);
            Dx12Utility.FillTexture2DArrayUAV(ref description.Texture2DArray, nullView, dimension);
            Dx12Utility.FillTexture3DUAV(ref description.Texture3D, nullView, dimension);
            if (dimension == ERHITextureDimension.Texture2DArrayMS)
            {
                description.Texture2DMSArray.FirstArraySlice = 0;
                description.Texture2DMSArray.ArraySize = 1;
            }
            else if (dimension is ERHITextureDimension.TextureCube or ERHITextureDimension.TextureCubeArray)
            {
                description.Texture2DArray.FirstArraySlice = 0;
                description.Texture2DArray.ArraySize = 6;
                description.Texture2DArray.MipSlice = 0;
                description.Texture2DArray.PlaneSlice = 0;
            }
            return description;
        }

        internal static ERHITextureDimension GetTextureDimension(in ERHIBindType bindType)
        {
            return bindType switch
            {
                ERHIBindType.Texture2D or ERHIBindType.StorageTexture2D => ERHITextureDimension.Texture2D,
                ERHIBindType.Texture2DMS or ERHIBindType.StorageTexture2DMS => ERHITextureDimension.Texture2DMS,
                ERHIBindType.Texture2DArray or ERHIBindType.StorageTexture2DArray => ERHITextureDimension.Texture2DArray,
                ERHIBindType.Texture2DArrayMS or ERHIBindType.StorageTexture2DArrayMS => ERHITextureDimension.Texture2DArrayMS,
                ERHIBindType.TextureCube or ERHIBindType.StorageTextureCube => ERHITextureDimension.TextureCube,
                ERHIBindType.TextureCubeArray or ERHIBindType.StorageTextureCubeArray => ERHITextureDimension.TextureCubeArray,
                ERHIBindType.Texture3D or ERHIBindType.StorageTexture3D => ERHITextureDimension.Texture3D,
                _ => throw new ArgumentOutOfRangeException(nameof(bindType), bindType, "DX12 bind type does not describe a texture."),
            };
        }
    }

    internal struct Dx12ArgumentTableGroupStorage
    {
        public int HeapIndex;
        public int DescriptorCount;
        public bool IsSampler;
        public bool OwnsRange;
        public Vortice.Direct3D12.GpuDescriptorHandle GpuHandle;
    }

    internal readonly struct Dx12DescriptorSource
    {
        public Vortice.Direct3D12.CpuDescriptorHandle CpuHandle { get; }
        public Vortice.Direct3D12.GpuDescriptorHandle GpuHandle { get; }

        public Dx12DescriptorSource(
            in Vortice.Direct3D12.CpuDescriptorHandle cpuHandle,
            in Vortice.Direct3D12.GpuDescriptorHandle gpuHandle)
        {
            CpuHandle = cpuHandle;
            GpuHandle = gpuHandle;
        }
    }

    internal sealed class Dx12ArgumentTable : RHIArgumentTable
    {
        public Dx12ArgumentTableLayout ArgumentTableLayout { get; }
        public int GroupCount => m_GroupStorages.Length;
        internal Dx12Device Device => m_Device;

        private readonly bool[] m_BoundStates;
        private readonly Dx12Device m_Device;
        private readonly Dx12ArgumentTableGroupStorage[] m_GroupStorages;
        private int m_MissingRequiredDescriptorCount;

        public Dx12ArgumentTable(Dx12Device device, in RHIArgumentTableDescriptor descriptor)
        {
            m_Device = device;
            ArgumentTableLayout = descriptor.Layout as Dx12ArgumentTableLayout
                ?? throw new ArgumentException("DX12 argument table requires a Dx12ArgumentTableLayout from the same backend.", nameof(descriptor));
            if (descriptor.Elements.Length > ArgumentTableLayout.BindInfos.Length)
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} received {descriptor.Elements.Length} initial elements for {ArgumentTableLayout.BindInfos.Length} bindings.",
                    nameof(descriptor));
            }

            m_BoundStates = new bool[ArgumentTableLayout.DescriptorCount];
            m_GroupStorages = new Dx12ArgumentTableGroupStorage[ArgumentTableLayout.Groups.Length];

            try
            {
                InitializeGroups();
                InitializeRequiredDescriptorCount();

                Span<RHIArgumentTableElement> initialElements = descriptor.Elements.Span;
                for (int i = 0; i < initialElements.Length; ++i)
                {
                    ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[i];
                    SetBinding(i, 0, initialElements[i], bindInfo.Type);
                }
            }
            catch
            {
                ReleaseOwnedRanges();
                throw;
            }
        }

        public Vortice.Direct3D12.GpuDescriptorHandle GetGroupGpuHandle(in int groupIndex)
        {
            ThrowIfDisposed();
            if ((uint)groupIndex >= (uint)m_GroupStorages.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex));
            }
            return m_GroupStorages[groupIndex].GpuHandle;
        }

        public void EnsureReadyForBinding()
        {
            ThrowIfDisposed();
            if (m_MissingRequiredDescriptorCount == 0)
            {
                return;
            }

            for (int bindingIndex = 0; bindingIndex < ArgumentTableLayout.BindInfos.Length; ++bindingIndex)
            {
                ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[bindingIndex];
                if (!IsRequired(bindInfo.Type))
                {
                    continue;
                }

                for (int arrayIndex = 0; arrayIndex < (int)bindInfo.Count; ++arrayIndex)
                {
                    if (!m_BoundStates[bindInfo.StateOffset + arrayIndex])
                    {
                        throw new InvalidOperationException(
                            $"DX12 argument table space {ArgumentTableLayout.Index} cannot be bound: required {bindInfo.Type} slot {bindInfo.Slot}[{arrayIndex}] is unset.");
                    }
                }
            }

            throw new InvalidOperationException(
                $"DX12 argument table space {ArgumentTableLayout.Index} has {m_MissingRequiredDescriptorCount} unset required descriptors.");
        }

        public override void SetBindElement(in RHIArgumentTableElement element, in ERHIBindType bindType, in int slot)
        {
            ThrowIfDisposed();
            int bindingIndex = GetBindingIndex(slot, bindType);
            SetBinding(bindingIndex, 0, element, bindType);
        }

        public override void SetBindElement(
            in RHIArgumentTableElement element,
            in ERHIBindType bindType,
            in int slot,
            in int arrayIndex)
        {
            ThrowIfDisposed();
            int bindingIndex = GetBindingIndex(slot, bindType);
            ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[bindingIndex];
            if (bindInfo.Count <= 1)
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindType}, slot {slot}) is not an array.",
                    nameof(arrayIndex));
            }
            SetBinding(bindingIndex, arrayIndex, element, bindType);
        }

        protected override void Release()
        {
            ReleaseOwnedRanges();
        }

        private void InitializeGroups()
        {
            for (int groupIndex = 0; groupIndex < ArgumentTableLayout.Groups.Length; ++groupIndex)
            {
                Dx12ArgumentTableGroupPlan groupPlan = ArgumentTableLayout.Groups[groupIndex];
                ref Dx12ArgumentTableGroupStorage storage = ref m_GroupStorages[groupIndex];
                storage.HeapIndex = -1;
                storage.DescriptorCount = groupPlan.DescriptorCount;
                storage.IsSampler = groupPlan.HeapClass == Dx12DescriptorHeapClass.Sampler;
                storage.OwnsRange = groupPlan.RequiresOwnedRange;

                if (storage.OwnsRange)
                {
                    Dx12DescriptorInfo allocation = storage.IsSampler
                        ? m_Device.AllocateSamplerDescriptor(storage.DescriptorCount)
                        : m_Device.AllocateCbvSrvUavDescriptor(storage.DescriptorCount);
                    storage.HeapIndex = allocation.Index;
                    storage.GpuHandle = allocation.GpuHandle;
                    InitializeOwnedRange(groupPlan, storage);
                    continue;
                }

                int bindingIndex = groupPlan.BindingIndices[0];
                ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[bindingIndex];
                storage.GpuHandle = m_Device.NullDescriptors.Get(bindInfo.Type).ShaderVisible.GpuHandle;
            }
        }

        private void InitializeOwnedRange(
            Dx12ArgumentTableGroupPlan groupPlan,
            in Dx12ArgumentTableGroupStorage storage)
        {
            for (int i = 0; i < groupPlan.BindingIndices.Length; ++i)
            {
                ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[groupPlan.BindingIndices[i]];
                Dx12DescriptorPair nullDescriptor = m_Device.NullDescriptors.Get(bindInfo.Type);
                for (int arrayIndex = 0; arrayIndex < (int)bindInfo.Count; ++arrayIndex)
                {
                    CopyDescriptor(storage, bindInfo.DescriptorOffset + arrayIndex, nullDescriptor.Staging.CpuHandle);
                }
            }
        }

        private void InitializeRequiredDescriptorCount()
        {
            int missingCount = 0;
            for (int i = 0; i < ArgumentTableLayout.BindInfos.Length; ++i)
            {
                ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[i];
                if (IsRequired(bindInfo.Type))
                {
                    missingCount = checked(missingCount + (int)bindInfo.Count);
                }
            }
            m_MissingRequiredDescriptorCount = missingCount;
        }

        private int GetBindingIndex(in int slot, in ERHIBindType bindType)
        {
            if (!ArgumentTableLayout.TryGetBindingIndex(slot, bindType, out int bindingIndex))
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} does not declare binding ({bindType}, slot {slot}).",
                    nameof(slot));
            }
            return bindingIndex;
        }

        private void SetBinding(
            in int bindingIndex,
            in int arrayIndex,
            in RHIArgumentTableElement element,
            in ERHIBindType bindType)
        {
            ref readonly Dx12BindInfo bindInfo = ref ArgumentTableLayout.BindInfos[bindingIndex];
            if (bindInfo.Type != bindType)
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding slot {bindInfo.Slot} expects {bindInfo.Type}, not {bindType}.",
                    nameof(bindType));
            }
            if (arrayIndex < 0 || arrayIndex >= (int)bindInfo.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindType}, slot {bindInfo.Slot}) accepts array indices [0, {bindInfo.Count}).");
            }

            bool hasSource = TryGetDescriptorSource(element, bindInfo, out Dx12DescriptorSource source);
            ref Dx12ArgumentTableGroupStorage storage = ref m_GroupStorages[bindInfo.GroupIndex];
            Dx12DescriptorPair nullDescriptor = m_Device.NullDescriptors.Get(bindInfo.Type);

            if (storage.OwnsRange)
            {
                CopyDescriptor(
                    storage,
                    bindInfo.DescriptorOffset + arrayIndex,
                    hasSource ? source.CpuHandle : nullDescriptor.Staging.CpuHandle);
            }
            else
            {
                storage.GpuHandle = hasSource ? source.GpuHandle : nullDescriptor.ShaderVisible.GpuHandle;
            }

            int stateIndex = bindInfo.StateOffset + arrayIndex;
            bool wasBound = m_BoundStates[stateIndex];
            m_BoundStates[stateIndex] = hasSource;
            if (IsRequired(bindInfo.Type) && wasBound != hasSource)
            {
                m_MissingRequiredDescriptorCount += hasSource ? -1 : 1;
            }
        }

        private bool TryGetDescriptorSource(
            in RHIArgumentTableElement element,
            in Dx12BindInfo bindInfo,
            out Dx12DescriptorSource source)
        {
            int fieldCount = 0;
            if (element.Sampler != null) ++fieldCount;
            if (element.BufferView != null) ++fieldCount;
            if (element.TextureView != null) ++fieldCount;
            if (element.AccelStruct != null) ++fieldCount;

            if (fieldCount == 0)
            {
                source = default;
                return false;
            }
            if (fieldCount != 1)
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) requires exactly one resource field.");
            }

            switch (bindInfo.Type)
            {
                case ERHIBindType.Sampler:
                {
                    Dx12Sampler sampler = element.Sampler as Dx12Sampler
                        ?? throw CreateResourceTypeException(bindInfo, nameof(RHIArgumentTableElement.Sampler));
                    source = ValidateDescriptorSource(sampler, Dx12DescriptorClass.Sampler, bindInfo);
                    return true;
                }
                case ERHIBindType.AccelStruct:
                {
                    Dx12TopLevelAccelStruct accelStruct = element.AccelStruct as Dx12TopLevelAccelStruct
                        ?? throw CreateResourceTypeException(bindInfo, nameof(RHIArgumentTableElement.AccelStruct));
                    source = ValidateDescriptorSource(accelStruct, Dx12DescriptorClass.AccelerationStructure, bindInfo);
                    return true;
                }
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                {
                    Dx12BufferView bufferView = element.BufferView as Dx12BufferView
                        ?? throw CreateResourceTypeException(bindInfo, nameof(RHIArgumentTableElement.BufferView));
                    ERHIBufferViewType expectedViewType = bindInfo.Type switch
                    {
                        ERHIBindType.Buffer => ERHIBufferViewType.ShaderResource,
                        ERHIBindType.StorageBuffer => ERHIBufferViewType.UnorderedAccess,
                        ERHIBindType.UniformBuffer => ERHIBufferViewType.UniformBuffer,
                        _ => throw new InvalidOperationException(),
                    };
                    if (bufferView.ViewType != expectedViewType)
                    {
                        throw new ArgumentException(
                            $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) "
                            + $"requires a {expectedViewType} buffer view, but received {bufferView.ViewType}.");
                    }
                    Dx12DescriptorClass expectedDescriptorClass = expectedViewType switch
                    {
                        ERHIBufferViewType.ShaderResource => Dx12DescriptorClass.ShaderResource,
                        ERHIBufferViewType.UnorderedAccess => Dx12DescriptorClass.UnorderedAccess,
                        ERHIBufferViewType.UniformBuffer => Dx12DescriptorClass.ConstantBuffer,
                        _ => throw new InvalidOperationException(),
                    };
                    source = ValidateDescriptorSource(bufferView, expectedDescriptorClass, bindInfo);
                    return true;
                }
                default:
                {
                    Dx12TextureView textureView = element.TextureView as Dx12TextureView
                        ?? throw CreateResourceTypeException(bindInfo, nameof(RHIArgumentTableElement.TextureView));
                    ERHITextureViewType expectedViewType = bindInfo.NativeRangeType == Vortice.Direct3D12.DescriptorRangeType.UnorderedAccessView
                        ? ERHITextureViewType.UnorderedAccess
                        : ERHITextureViewType.ShaderResource;
                    ERHITextureDimension expectedDimension = Dx12NullDescriptorCache.GetTextureDimension(bindInfo.Type);
                    if (textureView.ViewType != expectedViewType || textureView.Dimension != expectedDimension)
                    {
                        throw new ArgumentException(
                            $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) "
                            + $"requires a {expectedViewType} {expectedDimension} texture view, but received {textureView.ViewType} {textureView.Dimension}.");
                    }
                    Dx12DescriptorClass expectedDescriptorClass = expectedViewType == ERHITextureViewType.UnorderedAccess
                        ? Dx12DescriptorClass.UnorderedAccess
                        : Dx12DescriptorClass.ShaderResource;
                    source = ValidateDescriptorSource(textureView, expectedDescriptorClass, bindInfo);
                    return true;
                }
            }
        }

        private Dx12DescriptorSource ValidateDescriptorSource(
            IDx12DescriptorView descriptor,
            in Dx12DescriptorClass expectedClass,
            in Dx12BindInfo bindInfo)
        {
            if (!ReferenceEquals(descriptor.Device, m_Device))
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) received a descriptor from a different DX12 device.");
            }
            if (descriptor.DescriptorClass != expectedClass)
            {
                throw new ArgumentException(
                    $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) requires descriptor class {expectedClass}, but received {descriptor.DescriptorClass}.");
            }

            return new Dx12DescriptorSource(
                descriptor.NativeCpuDescriptorHandle,
                descriptor.NativeGpuDescriptorHandle);
        }
        private ArgumentException CreateResourceTypeException(in Dx12BindInfo bindInfo, string expectedField)
        {
            return new ArgumentException(
                $"DX12 argument table space {ArgumentTableLayout.Index} binding ({bindInfo.Type}, slot {bindInfo.Slot}) "
                + $"requires a DX12 {expectedField} from the same device/backend.");
        }

        private void CopyDescriptor(
            in Dx12ArgumentTableGroupStorage storage,
            in int descriptorOffset,
            in Vortice.Direct3D12.CpuDescriptorHandle sourceHandle)
        {
            Dx12DescriptorHeap destinationHeap = storage.IsSampler
                ? m_Device.DescriptorHeapSampler
                : m_Device.DescriptorHeapCbvSrvUav;
            Vortice.Direct3D12.CpuDescriptorHandle destinationHandle = destinationHeap.NativeCpuStartHandle.Offset(
                storage.HeapIndex + descriptorOffset,
                destinationHeap.DescriptorSize);
            m_Device.NativeDevice.CopyDescriptorsSimple(1, destinationHandle, sourceHandle, destinationHeap.NativeType);
        }

        private void ReleaseOwnedRanges()
        {
            for (int i = 0; i < m_GroupStorages.Length; ++i)
            {
                ref Dx12ArgumentTableGroupStorage storage = ref m_GroupStorages[i];
                if (!storage.OwnsRange || storage.HeapIndex < 0)
                {
                    continue;
                }

                int heapIndex = storage.HeapIndex;
                int descriptorCount = storage.DescriptorCount;
                storage.HeapIndex = -1;
                storage.OwnsRange = false;
                if (storage.IsSampler)
                {
                    m_Device.FreeSamplerDescriptor(heapIndex, descriptorCount);
                }
                else
                {
                    m_Device.FreeCbvSrvUavDescriptor(heapIndex, descriptorCount);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(Dx12ArgumentTable));
            }
        }

        private static bool IsRequired(in ERHIBindType bindType)
        {
            return bindType == ERHIBindType.Sampler || bindType == ERHIBindType.AccelStruct;
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
