using System;
using System.Collections.Generic;
using SharpMetal.Metal;

namespace SharpGPU
{
    internal readonly struct MetalBindInfo
    {
        public readonly uint Slot;
        public readonly uint Index;
        public readonly uint Count;
        public readonly ERHIBindType Type;
        public readonly ERHIShaderStageMask Stages;
        public readonly ERHIArgumentBindingRequirement Requirement;

        public bool HasDescriptorArray => Count > 1;

        public MetalBindInfo(
            in uint slot,
            in uint index,
            in uint count,
            in ERHIBindType type,
            in ERHIShaderStageMask stages)
            : this(slot, index, count, type, stages, ERHIArgumentBindingRequirement.Required)
        {
        }

        public MetalBindInfo(
            in uint slot,
            in uint index,
            in uint count,
            in ERHIBindType type,
            in ERHIShaderStageMask stages,
            in ERHIArgumentBindingRequirement requirement)
        {
            Slot = slot;
            Index = index;
            Count = count;
            Type = type;
            Stages = stages;
            Requirement = requirement;
        }
    }

    internal readonly struct MetalArgumentBindingSnapshot
    {
        public readonly bool IsBound;
        public readonly ulong BufferAddress;
        public readonly MTLResourceID ResourceId;
        public readonly MTLAllocation ResidencyAllocation;

        private MetalArgumentBindingSnapshot(
            in ulong bufferAddress,
            in MTLResourceID resourceId,
            in MTLAllocation residencyAllocation)
        {
            IsBound = true;
            BufferAddress = bufferAddress;
            ResourceId = resourceId;
            ResidencyAllocation = residencyAllocation;
        }

        public static MetalArgumentBindingSnapshot Capture(
            in RHIArgumentTableElement element,
            in MetalBindInfo bindInfo)
        {
            switch (bindInfo.Type)
            {
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is MetalBufferView bufferView)
                    {
                        MTLBuffer nativeBuffer = bufferView.Buffer.NativeBuffer;
                        ulong offset = (ulong)Math.Max(0, bufferView.Descriptor.Offset);
                        return new MetalArgumentBindingSnapshot(
                            checked(nativeBuffer.GpuAddress + offset),
                            default,
                            new MTLAllocation(nativeBuffer.NativePtr));
                    }

                    return default;

                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    if (element.TextureView is MetalTextureView textureView)
                    {
                        MTLTexture nativeTexture = textureView.ParentTexture;
                        return new MetalArgumentBindingSnapshot(
                            0,
                            textureView.ResourceID,
                            new MTLAllocation(nativeTexture.NativePtr));
                    }

                    return default;

                case ERHIBindType.Sampler:
                    if (element.Sampler is MetalSampler sampler)
                    {
                        MTLSamplerState nativeSampler = sampler.NativeSampler;
                        return new MetalArgumentBindingSnapshot(
                            0,
                            nativeSampler.GpuResourceID,
                            default);
                    }

                    return default;

                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct is MetalTopLevelAccelStruct topLevel)
                    {
                        MTLAccelerationStructure nativeAccelerationStructure =
                            topLevel.NativeAccelerationStructure;
                        return new MetalArgumentBindingSnapshot(
                            0,
                            nativeAccelerationStructure.GpuResourceID,
                            new MTLAllocation(nativeAccelerationStructure.NativePtr));
                    }

                    return default;

                default:
                    throw new InvalidOperationException(
                        $"Metal argument table {bindInfo.Index} contains unsupported binding type {bindInfo.Type}.");
            }
        }
    }

    internal sealed class MetalArgumentTableLayout : RHIArgumentTableLayout
    {
        public uint Index => m_Index;
        public ReadOnlySpan<MetalBindInfo> BindInfos => m_BindInfos;
        public int TotalElementCount => m_TotalElementCount;
        public ulong ReferenceBufferElementCount => m_ReferenceBufferElementCount;
        public bool RequiresReferenceBuffer => m_RequiresReferenceBuffer;

        internal MetalDevice? Device { get; }
        private readonly uint m_Index;
        private readonly MetalBindInfo[] m_BindInfos;
        private readonly int[] m_ElementOffsets;
        private readonly int m_TotalElementCount;
        private readonly ulong m_ReferenceBufferElementCount;
        private readonly bool m_RequiresReferenceBuffer;

        public MetalArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
            : base(descriptor)
        {
            m_Index = descriptor.Index;
            m_BindInfos = new MetalBindInfo[descriptor.Elements.Length];
            m_ElementOffsets = new int[descriptor.Elements.Length];

            Span<RHIArgumentTableLayoutElement> elements = descriptor.Elements.Span;
            HashSet<(uint Slot, ERHIBindType Type)> keys = new();
            int offset = 0;
            bool requiresReferenceBuffer = false;
            ulong referenceBufferElementCount = 0;
            for (int index = 0; index < elements.Length; ++index)
            {
                ref RHIArgumentTableLayoutElement element = ref elements[index];
                MetalArgumentTableValidation.ValidateLayoutElement(element, descriptor.Index, index);
                if (!keys.Add((element.Slot, element.Type)))
                {
                    throw new ArgumentException(
                        $"Metal argument table {descriptor.Index} contains duplicate binding slot={element.Slot}, type={element.Type}.",
                        nameof(descriptor));
                }

                int count = checked((int)element.Count);
                m_BindInfos[index] = new MetalBindInfo(
                    element.Slot,
                    descriptor.Index,
                    element.Count,
                    element.Type,
                    element.Stages,
                    element.Requirement);
                m_ElementOffsets[index] = offset;
                offset = checked(offset + count);
                requiresReferenceBuffer |= element.Count > 1;
                referenceBufferElementCount = Math.Max(
                    referenceBufferElementCount,
                    checked((ulong)element.Slot + element.Count));
            }

            ValidateDirectBindingRanges();
            m_TotalElementCount = offset;
            m_ReferenceBufferElementCount = referenceBufferElementCount;
            m_RequiresReferenceBuffer = requiresReferenceBuffer;
        }

        internal MetalArgumentTableLayout(
            MetalDevice device,
            in RHIArgumentTableLayoutDescriptor descriptor)
            : this(descriptor)
        {
            Device = device;
        }

        public int GetElementOffset(int bindIndex)
        {
            if ((uint)bindIndex >= (uint)m_ElementOffsets.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bindIndex),
                    bindIndex,
                    $"Metal argument table {m_Index} binding index must be in [0, {m_ElementOffsets.Length}).");
            }

            return m_ElementOffsets[bindIndex];
        }

        public int FindBindIndex(in int slot, in ERHIBindType bindType)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "Metal binding slot must not be negative.");
            }

            MetalArgumentTableValidation.ValidateBindType(bindType, nameof(bindType));
            for (int index = 0; index < m_BindInfos.Length; ++index)
            {
                ref readonly MetalBindInfo bindInfo = ref m_BindInfos[index];
                if (bindInfo.Slot == (uint)slot && bindInfo.Type == bindType)
                {
                    return index;
                }
            }

            throw new KeyNotFoundException(
                $"Metal argument table {m_Index} does not contain binding slot={slot}, type={bindType}.");
        }
        public void ValidateReferenceBufferRanges()
        {
            for (int leftIndex = 0; leftIndex < m_BindInfos.Length; ++leftIndex)
            {
                ref readonly MetalBindInfo left = ref m_BindInfos[leftIndex];
                ulong leftEnd = checked((ulong)left.Slot + left.Count);
                for (int rightIndex = leftIndex + 1; rightIndex < m_BindInfos.Length; ++rightIndex)
                {
                    ref readonly MetalBindInfo right = ref m_BindInfos[rightIndex];
                    ulong rightEnd = checked((ulong)right.Slot + right.Count);
                    if ((ulong)left.Slot < rightEnd && (ulong)right.Slot < leftEnd)
                    {
                        throw new InvalidOperationException(
                            $"Metal reference buffer {m_Index} has overlapping unified entry ranges "
                            + $"[{left.Slot}, {leftEnd}) for {left.Type} and "
                            + $"[{right.Slot}, {rightEnd}) for {right.Type}.");
                    }
                }
            }
        }


        public bool StructurallyEquals(MetalArgumentTableLayout? other)
        {
            if (other is null || m_Index != other.m_Index || m_BindInfos.Length != other.m_BindInfos.Length)
            {
                return false;
            }

            for (int index = 0; index < m_BindInfos.Length; ++index)
            {
                ref readonly MetalBindInfo left = ref m_BindInfos[index];
                ref readonly MetalBindInfo right = ref other.m_BindInfos[index];
                if (left.Slot != right.Slot
                    || left.Index != right.Index
                    || left.Count != right.Count
                    || left.Type != right.Type
                    || left.Stages != right.Stages
                    || left.Requirement != right.Requirement)
                {
                    return false;
                }
            }

            return true;
        }

        protected override void Release()
        {
        }

        private void ValidateDirectBindingRanges()
        {
            for (int leftIndex = 0; leftIndex < m_BindInfos.Length; ++leftIndex)
            {
                ref readonly MetalBindInfo left = ref m_BindInfos[leftIndex];
                MetalArgumentTableValidation.BindingNamespace leftNamespace =
                    MetalArgumentTableValidation.GetDirectBindingNamespace(left.Type);
                ulong leftEnd = checked((ulong)left.Slot + left.Count);

                for (int rightIndex = leftIndex + 1; rightIndex < m_BindInfos.Length; ++rightIndex)
                {
                    ref readonly MetalBindInfo right = ref m_BindInfos[rightIndex];
                    if (leftNamespace != MetalArgumentTableValidation.GetDirectBindingNamespace(right.Type))
                    {
                        continue;
                    }

                    ulong rightEnd = checked((ulong)right.Slot + right.Count);
                    if ((ulong)left.Slot < rightEnd && (ulong)right.Slot < leftEnd)
                    {
                        throw new ArgumentException(
                            $"Metal argument table {m_Index} has overlapping {leftNamespace} binding ranges "
                            + $"[{left.Slot}, {leftEnd}) for {left.Type} and "
                            + $"[{right.Slot}, {rightEnd}) for {right.Type}.");
                    }
                }
            }
        }
    }

    internal sealed class MetalArgumentTable : RHIArgumentTable
    {
        public MetalArgumentTableLayout ArgumentTableLayout => m_Layout;

        internal MetalDevice? Device { get; }
        private readonly MetalArgumentTableLayout m_Layout;
        private readonly MetalArgumentBindingSnapshot[] m_Bindings;

        public MetalArgumentTable(in RHIArgumentTableDescriptor descriptor)
            : this(null, descriptor, validateDevice: false)
        {
        }

        internal MetalArgumentTable(
            MetalDevice device,
            in RHIArgumentTableDescriptor descriptor)
            : this(device, descriptor, validateDevice: true)
        {
        }

        private MetalArgumentTable(
            MetalDevice? device,
            in RHIArgumentTableDescriptor descriptor,
            bool validateDevice)
        {
            Device = device;
            m_Layout = descriptor.Layout as MetalArgumentTableLayout
                ?? throw new ArgumentException(
                    $"Metal argument tables require a {nameof(MetalArgumentTableLayout)}.",
                    nameof(descriptor));
            if (m_Layout.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(descriptor), $"Metal argument table layout {m_Layout.Index} is disposed.");
            }
            if (validateDevice && !ReferenceEquals(m_Layout.Device, device))
            {
                throw new ArgumentException(
                    $"Metal argument table layout {m_Layout.Index} belongs to a different Metal device.",
                    nameof(descriptor));
            }
            if (descriptor.Elements.Length > m_Layout.BindInfos.Length)
            {
                throw new ArgumentException(
                    $"Metal argument table {m_Layout.Index} received {descriptor.Elements.Length} initial elements for {m_Layout.BindInfos.Length} bindings.",
                    nameof(descriptor));
            }

            m_Bindings = new MetalArgumentBindingSnapshot[m_Layout.TotalElementCount];

            Span<RHIArgumentTableElement> sourceElements = descriptor.Elements.Span;
            for (int index = 0; index < sourceElements.Length; ++index)
            {
                ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[index];
                MetalArgumentTableValidation.ValidateElement(
                    sourceElements[index],
                    bindInfo,
                    arrayIndex: 0,
                    Device,
                    nameof(descriptor));
                int elementOffset = m_Layout.GetElementOffset(index);
                m_Bindings[elementOffset] =
                    MetalArgumentBindingSnapshot.Capture(sourceElements[index], bindInfo);
            }
        }

        public MetalArgumentBindingSnapshot GetBindingSnapshot(int bindIndex, int arrayIndex)
        {
            ThrowIfDisposed();
            int elementOffset = m_Layout.GetElementOffset(bindIndex);
            ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[bindIndex];
            if (arrayIndex < 0 || (uint)arrayIndex >= bindInfo.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    $"Metal argument table {m_Layout.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type} array index must be in [0, {bindInfo.Count}).");
            }

            return m_Bindings[checked(elementOffset + arrayIndex)];
        }

        public int GetBindCount()
        {
            ThrowIfDisposed();
            return m_Layout.BindInfos.Length;
        }

        public override void SetBindElement(
            in RHIArgumentTableElement element,
            in ERHIBindType bindType,
            in int slot)
        {
            ThrowIfDisposed();
            int bindIndex = m_Layout.FindBindIndex(slot, bindType);
            ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[bindIndex];
            MetalArgumentTableValidation.ValidateElement(
                element,
                bindInfo,
                arrayIndex: 0,
                Device,
                nameof(element));
            m_Bindings[m_Layout.GetElementOffset(bindIndex)] =
                MetalArgumentBindingSnapshot.Capture(element, bindInfo);
        }

        public override void SetBindElement(
            in RHIArgumentTableElement element,
            in ERHIBindType bindType,
            in int slot,
            in int arrayIndex)
        {
            ThrowIfDisposed();
            int bindIndex = m_Layout.FindBindIndex(slot, bindType);
            ref readonly MetalBindInfo bindInfo = ref m_Layout.BindInfos[bindIndex];
            if (bindInfo.Count <= 1)
            {
                throw new InvalidOperationException(
                    $"Metal argument table {m_Layout.Index} binding slot={slot}, type={bindType} is not an array binding.");
            }

            if (arrayIndex < 0 || (uint)arrayIndex >= bindInfo.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    $"Metal argument table {m_Layout.Index} binding slot={slot}, type={bindType} array index must be in [0, {bindInfo.Count}).");
            }

            MetalArgumentTableValidation.ValidateElement(
                element,
                bindInfo,
                arrayIndex,
                Device,
                nameof(element));
            int elementOffset = checked(m_Layout.GetElementOffset(bindIndex) + arrayIndex);
            m_Bindings[elementOffset] =
                MetalArgumentBindingSnapshot.Capture(element, bindInfo);
        }

        internal void ValidateRequiredBindings()
        {
            ThrowIfDisposed();
            ReadOnlySpan<MetalBindInfo> binds = m_Layout.BindInfos;
            for (int bindIndex = 0; bindIndex < binds.Length; ++bindIndex)
            {
                ref readonly MetalBindInfo bind = ref binds[bindIndex];
                if (bind.Requirement != ERHIArgumentBindingRequirement.Required)
                {
                    continue;
                }

                for (int arrayIndex = 0; (uint)arrayIndex < bind.Count; ++arrayIndex)
                {
                    if (!GetBindingSnapshot(bindIndex, arrayIndex).IsBound)
                    {
                        throw new InvalidOperationException(
                            $"Metal argument table {m_Layout.Index} required binding slot={bind.Slot}, "
                            + $"type={bind.Type}, arrayIndex={arrayIndex} is not bound.");
                    }
                }
            }
        }

        private new void ThrowIfDisposed()
        {
            base.ThrowIfDisposed();

            if (m_Layout.IsDisposed)
            {
                throw new ObjectDisposedException($"MetalArgumentTableLayout[{m_Layout.Index}]");
            }
        }

        protected override void Release()
        {
        }
    }

    internal static class MetalArgumentTableValidation
    {
        internal enum BindingNamespace : byte
        {
            Buffer,
            Texture,
            Sampler
        }

        public static void ValidateLayoutElement(
            in RHIArgumentTableLayoutElement element,
            in uint tableIndex,
            in int elementIndex)
        {
            if (element.Count == 0 || element.Count > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(element),
                    element.Count,
                    $"Metal argument table {tableIndex} element {elementIndex} count must be in [1, {int.MaxValue}].");
            }

            _ = checked(element.Slot + element.Count - 1);
            ValidateBindType(element.Type, nameof(element));
            ValidateShaderStages(element.Stages, tableIndex, elementIndex);
            if (!Enum.IsDefined(element.Requirement))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(element.Requirement),
                    element.Requirement,
                    "Metal argument-table binding requirement is undefined.");
            }
        }

        public static void ValidateBindType(in ERHIBindType bindType, string parameterName)
        {
            switch (bindType)
            {
                case ERHIBindType.Sampler:
                case ERHIBindType.Buffer:
                case ERHIBindType.AccelStruct:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        bindType,
                        "Metal argument-table bind type is undefined or unsupported.");
            }
        }

        internal static BindingNamespace GetDirectBindingNamespace(in ERHIBindType bindType)
        {
            return bindType switch
            {
                ERHIBindType.Buffer
                    or ERHIBindType.StorageBuffer
                    or ERHIBindType.UniformBuffer
                    or ERHIBindType.AccelStruct => BindingNamespace.Buffer,
                ERHIBindType.Texture2D
                    or ERHIBindType.Texture2DMS
                    or ERHIBindType.Texture2DArray
                    or ERHIBindType.Texture2DArrayMS
                    or ERHIBindType.TextureCube
                    or ERHIBindType.TextureCubeArray
                    or ERHIBindType.Texture3D
                    or ERHIBindType.StorageTexture2D
                    or ERHIBindType.StorageTexture2DMS
                    or ERHIBindType.StorageTexture2DArray
                    or ERHIBindType.StorageTexture2DArrayMS
                    or ERHIBindType.StorageTextureCube
                    or ERHIBindType.StorageTextureCubeArray
                    or ERHIBindType.StorageTexture3D => BindingNamespace.Texture,
                ERHIBindType.Sampler => BindingNamespace.Sampler,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(bindType),
                    bindType,
                    "Metal argument-table bind type is undefined or unsupported.")
            };
        }

        public static void ValidateElement(
            in RHIArgumentTableElement element,
            in MetalBindInfo bindInfo,
            in int arrayIndex,
            MetalDevice? expectedDevice,
            string parameterName)
        {
            bool hasSampler = element.Sampler is not null;
            bool hasBuffer = element.BufferView is not null;
            bool hasTexture = element.TextureView is not null;
            bool hasAccelerationStructure = element.AccelStruct is not null;
            int populatedFieldCount =
                (hasSampler ? 1 : 0)
                + (hasBuffer ? 1 : 0)
                + (hasTexture ? 1 : 0)
                + (hasAccelerationStructure ? 1 : 0);
            if (populatedFieldCount == 0)
            {
                return;
            }

            if (populatedFieldCount != 1)
            {
                throw new ArgumentException(
                    $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type}, arrayIndex={arrayIndex} must contain exactly one resource field.",
                    parameterName);
            }

            switch (bindInfo.Type)
            {
                case ERHIBindType.Sampler:
                    if (element.Sampler is not MetalSampler sampler)
                    {
                        throw WrongResourceType(bindInfo, arrayIndex, nameof(MetalSampler), parameterName);
                    }

                    ValidateNotDisposed(sampler, bindInfo, arrayIndex, parameterName);
                    ValidateDevice(expectedDevice, sampler.Device, bindInfo, arrayIndex, parameterName);
                    return;
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is not MetalBufferView bufferView)
                    {
                        throw WrongResourceType(bindInfo, arrayIndex, nameof(MetalBufferView), parameterName);
                    }

                    ValidateNotDisposed(bufferView, bindInfo, arrayIndex, parameterName);
                    ValidateNotDisposed(bufferView.Buffer, bindInfo, arrayIndex, parameterName);
                    ValidateDevice(expectedDevice, bufferView.Buffer.MetalDevice, bindInfo, arrayIndex, parameterName);
                    ERHIBufferViewType expectedBufferViewType = bindInfo.Type switch
                    {
                        ERHIBindType.Buffer => ERHIBufferViewType.ShaderResource,
                        ERHIBindType.StorageBuffer => ERHIBufferViewType.UnorderedAccess,
                        ERHIBindType.UniformBuffer => ERHIBufferViewType.UniformBuffer,
                        _ => throw new InvalidOperationException($"Unexpected Metal buffer binding type {bindInfo.Type}.")
                    };
                    if (bufferView.Descriptor.ViewType != expectedBufferViewType)
                    {
                        throw new ArgumentException(
                            $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type}, arrayIndex={arrayIndex} "
                            + $"requires a {expectedBufferViewType} buffer view, but received {bufferView.Descriptor.ViewType}.",
                            parameterName);
                    }

                    return;
                case ERHIBindType.Texture2D:
                case ERHIBindType.Texture2DMS:
                case ERHIBindType.Texture2DArray:
                case ERHIBindType.Texture2DArrayMS:
                case ERHIBindType.TextureCube:
                case ERHIBindType.TextureCubeArray:
                case ERHIBindType.Texture3D:
                case ERHIBindType.StorageTexture2D:
                case ERHIBindType.StorageTexture2DMS:
                case ERHIBindType.StorageTexture2DArray:
                case ERHIBindType.StorageTexture2DArrayMS:
                case ERHIBindType.StorageTextureCube:
                case ERHIBindType.StorageTextureCubeArray:
                case ERHIBindType.StorageTexture3D:
                    if (element.TextureView is not MetalTextureView textureView)
                    {
                        throw WrongResourceType(bindInfo, arrayIndex, nameof(MetalTextureView), parameterName);
                    }
                    ValidateNotDisposed(textureView, bindInfo, arrayIndex, parameterName);
                    ValidateNotDisposed(textureView.Texture, bindInfo, arrayIndex, parameterName);
                    ValidateDevice(expectedDevice, textureView.Texture.MetalDevice, bindInfo, arrayIndex, parameterName);
                    ERHITextureViewType expectedTextureViewType = IsStorageTexture(bindInfo.Type)
                        ? ERHITextureViewType.UnorderedAccess
                        : ERHITextureViewType.ShaderResource;
                    ERHITextureDimension expectedDimension = GetExpectedTextureDimension(bindInfo.Type);
                    ERHITextureViewType actualTextureViewType = textureView.Descriptor.ViewType;
                    ERHITextureDimension actualDimension = textureView.Texture.Descriptor.Dimension;
                    if (actualTextureViewType != expectedTextureViewType || actualDimension != expectedDimension)
                    {
                        throw new ArgumentException(
                            $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type}, arrayIndex={arrayIndex} "
                            + $"requires a {expectedTextureViewType} {expectedDimension} texture view, but received "
                            + $"{actualTextureViewType} {actualDimension}.",
                            parameterName);
                    }


                    return;
                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct is not MetalTopLevelAccelStruct accelerationStructure)
                    {
                        throw WrongResourceType(bindInfo, arrayIndex, nameof(MetalTopLevelAccelStruct), parameterName);
                    }
                    ValidateNotDisposed(accelerationStructure, bindInfo, arrayIndex, parameterName);
                    ValidateDevice(expectedDevice, accelerationStructure.Device, bindInfo, arrayIndex, parameterName);

                    return;
                default:
                    throw new ArgumentOutOfRangeException(
                        parameterName,
                        bindInfo.Type,
                        "Metal argument-table bind type is undefined or unsupported.");
            }
        }

        private static void ValidateShaderStages(
            in ERHIShaderStageMask stages,
            in uint tableIndex,
            in int elementIndex)
        {
            if (stages == ERHIShaderStageMask.None || (stages & ~ERHIShaderStageMask.All) != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stages),
                    stages,
                    $"Metal argument table {tableIndex} element {elementIndex} shader-stage visibility must be a non-empty known mask.");
            }
        }

        private static ArgumentException WrongResourceType(
            in MetalBindInfo bindInfo,
            in int arrayIndex,
            string expectedType,
            string parameterName)
        {
            return new ArgumentException(
                $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type}, arrayIndex={arrayIndex} requires a {expectedType} from the Metal backend.",
                parameterName);
        }

        private static bool IsStorageTexture(in ERHIBindType bindType)
        {
            return bindType is
                ERHIBindType.StorageTexture2D
                or ERHIBindType.StorageTexture2DMS
                or ERHIBindType.StorageTexture2DArray
                or ERHIBindType.StorageTexture2DArrayMS
                or ERHIBindType.StorageTextureCube
                or ERHIBindType.StorageTextureCubeArray
                or ERHIBindType.StorageTexture3D;
        }

        private static ERHITextureDimension GetExpectedTextureDimension(in ERHIBindType bindType)
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
                _ => throw new ArgumentOutOfRangeException(
                    nameof(bindType),
                    bindType,
                    "Metal texture bind type is undefined or unsupported.")
            };
        }

        private static void ValidateNotDisposed(
            SharpGPU.Core.Disposal resource,
            in MetalBindInfo bindInfo,
            in int arrayIndex,
            string parameterName)
        {
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(
                    parameterName,
                    $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, type={bindInfo.Type}, arrayIndex={arrayIndex} references a disposed resource.");
            }
        }

        private static void ValidateDevice(
            MetalDevice? expectedDevice,
            MetalDevice actualDevice,
            in MetalBindInfo bindInfo,
            in int arrayIndex,
            string parameterName)
        {
            if (expectedDevice is null || ReferenceEquals(expectedDevice, actualDevice))
            {
                return;
            }

            throw new ArgumentException(
                $"Metal argument table {bindInfo.Index} binding slot={bindInfo.Slot}, "
                + $"type={bindInfo.Type}, arrayIndex={arrayIndex} references a resource "
                + "from a different Metal device.",
                parameterName);
        }
    }
}
