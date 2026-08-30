using System;
using System.Threading;
using Vortice.Vulkan;
using System.Collections.Generic;

namespace SharpGPU
{
    internal unsafe sealed class VulkanBindingTableLayout :
        RHIBindingTableLayout
    {
        public VkDescriptorSetLayout NativeDescriptorSetLayout =>
            m_NativeDescriptorSetLayout;
        internal VulkanDevice Device => m_VulkanDevice;
        internal VulkanBindingTablePlan Plan { get; }

        private readonly VulkanDevice m_VulkanDevice;
        private VkDescriptorSetLayout m_NativeDescriptorSetLayout;

        public VulkanBindingTableLayout(
            VulkanDevice device,
            in RHIBindingTableLayoutDescriptor descriptor)
            : base(descriptor)
        {
            m_VulkanDevice = device;
            Plan = new VulkanBindingTablePlan(
                descriptor,
                device.DescriptorFeatures,
                device.DescriptorLimits);

            int bindingCount = Plan.BindInfos.Length;
            VkDescriptorSetLayoutBinding* bindings =
                stackalloc VkDescriptorSetLayoutBinding[bindingCount];
            for (int index = 0; index < bindingCount; ++index)
            {
                ref readonly VulkanBindInfo binding = ref Plan.BindInfos[index];
                bindings[index] = new VkDescriptorSetLayoutBinding
                {
                    binding = binding.PhysicalBinding,
                    descriptorType = binding.NativeDescriptorType,
                    descriptorCount = binding.Count,
                    stageFlags = binding.NativeStages,
                    pImmutableSamplers = null,
                };
            }

            VkDescriptorSetLayoutCreateInfo createInfo =
                new VkDescriptorSetLayoutCreateInfo
                {
                    sType = VkStructureType.DescriptorSetLayoutCreateInfo,
                    flags = 0,
                    bindingCount = checked((uint)bindingCount),
                    pBindings = bindingCount == 0 ? null : bindings,
                };
            fixed (VkDescriptorSetLayout* layoutPointer =
                &m_NativeDescriptorSetLayout)
            {
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateDescriptorSetLayout(
                        device.NativeDevice,
                        &createInfo,
                        null,
                        layoutPointer));
            }
        }

        internal bool StructurallyEquals(VulkanBindingTableLayout? other)
        {
            return other is not null && Plan.StructurallyEquals(other.Plan);
        }

        protected override void Release()
        {
            if (m_NativeDescriptorSetLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(
                    m_VulkanDevice.NativeDevice,
                    m_NativeDescriptorSetLayout,
                    null);
                m_NativeDescriptorSetLayout = default;
            }
        }
    }

    internal unsafe sealed class VulkanBindingTable : RHIBindingTable
    {
        public VkDescriptorSet NativeDescriptorSet
        {
            get
            {
                ThrowIfDisposed();
                return m_Lease.Set;
            }
        }

        internal VulkanDevice Device => m_VulkanDevice;
        internal VulkanBindingTableLayout Layout => m_Layout;
        private readonly VulkanDevice m_VulkanDevice;
        private readonly VulkanBindingTableLayout m_Layout;
        private readonly bool[] m_BoundStates;
        private VulkanDescriptorSetLease m_Lease;
        private int m_MissingRequiredDescriptorCount;

        public VulkanBindingTable(
            VulkanDevice device,
            in RHIBindingTableDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Layout = descriptor.Layout as VulkanBindingTableLayout
                ?? throw new ArgumentException(
                    "Vulkan binding table requires a Vulkan layout.",
                    nameof(descriptor));
            if (m_Layout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(descriptor),
                    $"Vulkan argument-table layout {m_Layout.Plan.Index} is disposed.");
            }
            if (!ReferenceEquals(m_Layout.Device, device))
            {
                throw new ArgumentException(
                    "Vulkan argument-table layout belongs to a different device.",
                    nameof(descriptor));
            }
            if (descriptor.Elements.Length > m_Layout.Plan.BindInfos.Length)
            {
                throw new ArgumentException(
                    $"Vulkan binding table {m_Layout.Plan.Index} received "
                    + $"{descriptor.Elements.Length} initial elements for "
                    + $"{m_Layout.Plan.BindInfos.Length} bindings.",
                    nameof(descriptor));
            }

            m_BoundStates = new bool[m_Layout.Plan.DescriptorCount];
            for (int bindingIndex = 0;
                bindingIndex < m_Layout.Plan.BindInfos.Length;
                ++bindingIndex)
            {
                ref readonly VulkanBindInfo binding =
                    ref m_Layout.Plan.BindInfos[bindingIndex];
                if (binding.Requirement
                    == ERHIBindingRequirement.Required)
                {
                    m_MissingRequiredDescriptorCount = checked(
                        m_MissingRequiredDescriptorCount
                        + (int)binding.Count);
                }
            }

            m_Lease = device.DescriptorPoolAllocator.Allocate(m_Layout);
            try
            {
                InitializeOptionalDescriptors();
                Span<RHIBindingTableElement> initialElements =
                    descriptor.Elements.Span;
                for (int bindingIndex = 0;
                    bindingIndex < initialElements.Length;
                    ++bindingIndex)
                {
                    SetBinding(
                        bindingIndex,
                        arrayIndex: 0,
                        initialElements[bindingIndex]);
                }
            }
            catch
            {
                device.DescriptorPoolAllocator.Free(m_Lease);
                m_Lease = default;
                throw;
            }
        }

        public override void SetBindElement(
            in RHIBindingTableElement element,
            in ERHIBindType bindType,
            in int slot)
        {
            ThrowIfDisposedAndLayout();
            int bindingIndex = m_Layout.Plan.ResolveBinding(slot, bindType);
            SetBinding(bindingIndex, arrayIndex: 0, element);
        }

        public override void SetBindElement(
            in RHIBindingTableElement element,
            in ERHIBindType bindType,
            in int slot,
            in int arrayIndex)
        {
            ThrowIfDisposedAndLayout();
            int bindingIndex = m_Layout.Plan.ResolveBinding(slot, bindType);
            ref readonly VulkanBindInfo binding =
                ref m_Layout.Plan.BindInfos[bindingIndex];
            if (binding.Count <= 1)
            {
                throw new ArgumentException(
                    $"Vulkan argument-table binding ({bindType}, slot {slot}) "
                    + "is not an array.",
                    nameof(arrayIndex));
            }
            SetBinding(bindingIndex, arrayIndex, element);
        }

        internal void EnsureReadyForBinding()
        {
            ThrowIfDisposedAndLayout();
            if (m_MissingRequiredDescriptorCount == 0)
            {
                return;
            }

            for (int bindingIndex = 0;
                bindingIndex < m_Layout.Plan.BindInfos.Length;
                ++bindingIndex)
            {
                ref readonly VulkanBindInfo binding =
                    ref m_Layout.Plan.BindInfos[bindingIndex];
                if (binding.Requirement
                    != ERHIBindingRequirement.Required)
                {
                    continue;
                }
                for (int arrayIndex = 0;
                    (uint)arrayIndex < binding.Count;
                    ++arrayIndex)
                {
                    if (!m_BoundStates[binding.StateOffset + arrayIndex])
                    {
                        throw new InvalidOperationException(
                            $"Vulkan binding table {binding.TableIndex} cannot "
                            + $"be bound: required {binding.Type} slot "
                            + $"{binding.Slot}[{arrayIndex}] is unset.");
                    }
                }
            }

            throw new InvalidOperationException(
                "Vulkan required descriptor accounting is inconsistent.");
        }

        protected override void Release()
        {
            if (m_Lease.Set.Handle != 0)
            {
                m_VulkanDevice.DescriptorPoolAllocator.Free(m_Lease);
                m_Lease = default;
            }
        }

        private void InitializeOptionalDescriptors()
        {
            for (int bindingIndex = 0;
                bindingIndex < m_Layout.Plan.BindInfos.Length;
                ++bindingIndex)
            {
                ref readonly VulkanBindInfo binding =
                    ref m_Layout.Plan.BindInfos[bindingIndex];
                if (binding.Requirement
                    != ERHIBindingRequirement.Optional)
                {
                    continue;
                }
                for (int arrayIndex = 0;
                    (uint)arrayIndex < binding.Count;
                    ++arrayIndex)
                {
                    WriteDescriptor(binding, arrayIndex, default, isBound: false);
                }
            }
        }

        private void SetBinding(
            in int bindingIndex,
            in int arrayIndex,
            in RHIBindingTableElement element)
        {
            ref readonly VulkanBindInfo binding =
                ref m_Layout.Plan.BindInfos[bindingIndex];
            if (arrayIndex < 0 || (uint)arrayIndex >= binding.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    $"Vulkan binding ({binding.Type}, slot {binding.Slot}) "
                    + $"array index must be in [0, {binding.Count}).");
            }

            bool isBound = ValidateElement(element, binding, arrayIndex);
            if (isBound
                || binding.Requirement
                    == ERHIBindingRequirement.Optional)
            {
                WriteDescriptor(binding, arrayIndex, element, isBound);
            }

            int stateIndex = binding.StateOffset + arrayIndex;
            bool wasBound = m_BoundStates[stateIndex];
            m_BoundStates[stateIndex] = isBound;
            if (binding.Requirement
                    == ERHIBindingRequirement.Required
                && wasBound != isBound)
            {
                m_MissingRequiredDescriptorCount += isBound ? -1 : 1;
            }
        }

        private bool ValidateElement(
            in RHIBindingTableElement element,
            in VulkanBindInfo binding,
            in int arrayIndex)
        {
            int populatedCount =
                (element.Sampler is null ? 0 : 1)
                + (element.BufferView is null ? 0 : 1)
                + (element.TextureView is null ? 0 : 1)
                + (element.AccelStruct is null ? 0 : 1);
            if (populatedCount == 0)
            {
                return false;
            }
            if (populatedCount != 1)
            {
                throw new ArgumentException(
                    $"Vulkan binding table {binding.TableIndex} binding "
                    + $"({binding.Type}, slot {binding.Slot})[{arrayIndex}] "
                    + "must contain exactly one resource field.",
                    nameof(element));
            }

            switch (binding.Type)
            {
                case ERHIBindType.Sampler:
                    if (element.Sampler is not VulkanSampler sampler)
                    {
                        throw WrongResourceType(
                            binding,
                            arrayIndex,
                            nameof(VulkanSampler));
                    }
                    ValidateResource(
                        sampler,
                        sampler.Device,
                        binding,
                        arrayIndex);
                    break;
                case ERHIBindType.Buffer:
                case ERHIBindType.StorageBuffer:
                case ERHIBindType.UniformBuffer:
                    if (element.BufferView is not VulkanBufferView bufferView)
                    {
                        throw WrongResourceType(
                            binding,
                            arrayIndex,
                            nameof(VulkanBufferView));
                    }
                    ValidateResource(
                        bufferView,
                        bufferView.Device,
                        binding,
                        arrayIndex);
                    if (bufferView.VulkanBuffer.IsDisposed)
                    {
                        throw new ObjectDisposedException(
                            nameof(element),
                            "Vulkan buffer-view binding references a disposed buffer.");
                    }
                    ERHIBufferViewType expectedBufferType = binding.Type switch
                    {
                        ERHIBindType.Buffer =>
                            ERHIBufferViewType.ShaderResource,
                        ERHIBindType.StorageBuffer =>
                            ERHIBufferViewType.UnorderedAccess,
                        ERHIBindType.UniformBuffer =>
                            ERHIBufferViewType.UniformBuffer,
                        _ => throw new InvalidOperationException(),
                    };
                    if (bufferView.Descriptor.ViewType != expectedBufferType)
                    {
                        throw new ArgumentException(
                            $"Vulkan binding {binding.Type} requires a "
                            + $"{expectedBufferType} buffer view, but received "
                            + $"{bufferView.Descriptor.ViewType}.",
                            nameof(element));
                    }
                    break;
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
                    if (element.TextureView is not VulkanTextureView textureView)
                    {
                        throw WrongResourceType(
                            binding,
                            arrayIndex,
                            nameof(VulkanTextureView));
                    }
                    ValidateResource(
                        textureView,
                        textureView.Device,
                        binding,
                        arrayIndex);
                    if (textureView.VulkanTexture.IsDisposed)
                    {
                        throw new ObjectDisposedException(
                            nameof(element),
                            "Vulkan texture-view binding references a disposed texture.");
                    }
                    ERHITextureViewType expectedTextureType =
                        IsStorageTexture(binding.Type)
                            ? ERHITextureViewType.UnorderedAccess
                            : ERHITextureViewType.ShaderResource;
                    ERHITextureDimension expectedDimension =
                        GetExpectedTextureDimension(binding.Type);
                    if (textureView.Descriptor.ViewType != expectedTextureType
                        || textureView.VulkanTexture.Descriptor.Dimension
                            != expectedDimension)
                    {
                        throw new ArgumentException(
                            $"Vulkan binding {binding.Type} requires a "
                            + $"{expectedTextureType} {expectedDimension} "
                            + "texture view.",
                            nameof(element));
                    }
                    break;
                case ERHIBindType.AccelStruct:
                    if (element.AccelStruct
                        is not VulkanTopLevelAccelStruct accelerationStructure)
                    {
                        throw WrongResourceType(
                            binding,
                            arrayIndex,
                            nameof(VulkanTopLevelAccelStruct));
                    }
                    ValidateResource(
                        accelerationStructure,
                        accelerationStructure.Device,
                        binding,
                        arrayIndex);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(binding),
                        binding.Type,
                        "Vulkan argument-table bind type is unsupported.");
            }

            return true;
        }

        private void ValidateResource(
            SharpGPU.Core.Disposal resource,
            VulkanDevice resourceDevice,
            in VulkanBindInfo binding,
            in int arrayIndex)
        {
            if (resource.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(resource),
                    $"Vulkan binding ({binding.Type}, slot "
                    + $"{binding.Slot})[{arrayIndex}] references a disposed resource.");
            }
            if (!ReferenceEquals(resourceDevice, m_VulkanDevice))
            {
                throw new ArgumentException(
                    $"Vulkan binding ({binding.Type}, slot "
                    + $"{binding.Slot})[{arrayIndex}] references a resource "
                    + "from a different device.",
                    nameof(resource));
            }
        }

        private void WriteDescriptor(
            in VulkanBindInfo binding,
            in int arrayIndex,
            in RHIBindingTableElement element,
            bool isBound)
        {
            VkWriteDescriptorSet write = new VkWriteDescriptorSet
            {
                sType = VkStructureType.WriteDescriptorSet,
                dstSet = m_Lease.Set,
                dstBinding = binding.PhysicalBinding,
                dstArrayElement = checked((uint)arrayIndex),
                descriptorCount = 1,
                descriptorType = binding.NativeDescriptorType,
            };

            switch (binding.NativeDescriptorType)
            {
                case VkDescriptorType.Sampler:
                {
                    VkDescriptorImageInfo info = isBound
                        ? ((VulkanSampler)element.Sampler)
                            .GetDescriptorImageInfo()
                        : default;
                    write.pImageInfo = &info;
                    VulkanNative.vkUpdateDescriptorSets(
                        m_VulkanDevice.NativeDevice,
                        1,
                        &write,
                        0,
                        null);
                    return;
                }
                case VkDescriptorType.SampledImage:
                case VkDescriptorType.StorageImage:
                {
                    VkImageLayout imageLayout =
                        binding.NativeDescriptorType == VkDescriptorType.StorageImage
                            ? VkImageLayout.General
                            : VkImageLayout.ShaderReadOnlyOptimal;
                    VkDescriptorImageInfo info = isBound
                        ? ((VulkanTextureView)element.TextureView)
                            .GetDescriptorImageInfo(imageLayout)
                        : default;
                    write.pImageInfo = &info;
                    VulkanNative.vkUpdateDescriptorSets(
                        m_VulkanDevice.NativeDevice,
                        1,
                        &write,
                        0,
                        null);
                    return;
                }
                case VkDescriptorType.UniformBuffer:
                case VkDescriptorType.StorageBuffer:
                {
                    VkDescriptorBufferInfo info = isBound
                        ? ((VulkanBufferView)element.BufferView)
                            .GetDescriptorBufferInfo()
                        : default;
                    write.pBufferInfo = &info;
                    VulkanNative.vkUpdateDescriptorSets(
                        m_VulkanDevice.NativeDevice,
                        1,
                        &write,
                        0,
                        null);
                    return;
                }
                case VkDescriptorType.AccelerationStructureKHR:
                {
                    VkAccelerationStructureKHR handle = isBound
                        ? ((VulkanTopLevelAccelStruct)element.AccelStruct)
                            .NativeAccelerationStructure
                        : default;
                    VkWriteDescriptorSetAccelerationStructureKHR accelerationInfo =
                        new VkWriteDescriptorSetAccelerationStructureKHR
                        {
                            sType = VkStructureType
                                .WriteDescriptorSetAccelerationStructureKHR,
                            accelerationStructureCount = 1,
                            pAccelerationStructures = &handle,
                        };
                    write.pNext = &accelerationInfo;
                    VulkanNative.vkUpdateDescriptorSets(
                        m_VulkanDevice.NativeDevice,
                        1,
                        &write,
                        0,
                        null);
                    return;
                }
                default:
                    throw new NotSupportedException(
                        $"Vulkan descriptor type {binding.NativeDescriptorType} "
                        + "is not supported by binding tables.");
            }
        }

        private void ThrowIfDisposedAndLayout()
        {
            ThrowIfDisposed();
            if (m_Layout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    $"VulkanBindingTableLayout[{m_Layout.Plan.Index}]");
            }
        }

        private static ArgumentException WrongResourceType(
            in VulkanBindInfo binding,
            in int arrayIndex,
            string expectedType)
        {
            return new ArgumentException(
                $"Vulkan binding ({binding.Type}, slot "
                + $"{binding.Slot})[{arrayIndex}] requires {expectedType} "
                + "from the Vulkan backend.");
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

        private static ERHITextureDimension GetExpectedTextureDimension(
            in ERHIBindType bindType)
        {
            return bindType switch
            {
                ERHIBindType.Texture2D
                    or ERHIBindType.StorageTexture2D =>
                    ERHITextureDimension.Texture2D,
                ERHIBindType.Texture2DMS
                    or ERHIBindType.StorageTexture2DMS =>
                    ERHITextureDimension.Texture2DMS,
                ERHIBindType.Texture2DArray
                    or ERHIBindType.StorageTexture2DArray =>
                    ERHITextureDimension.Texture2DArray,
                ERHIBindType.Texture2DArrayMS
                    or ERHIBindType.StorageTexture2DArrayMS =>
                    ERHITextureDimension.Texture2DArrayMS,
                ERHIBindType.TextureCube
                    or ERHIBindType.StorageTextureCube =>
                    ERHITextureDimension.TextureCube,
                ERHIBindType.TextureCubeArray
                    or ERHIBindType.StorageTextureCubeArray =>
                    ERHITextureDimension.TextureCubeArray,
                ERHIBindType.Texture3D
                    or ERHIBindType.StorageTexture3D =>
                    ERHITextureDimension.Texture3D,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(bindType),
                    bindType,
                    "Vulkan texture bind type is unsupported."),
            };
        }
    }
}


namespace SharpGPU
{
    internal readonly struct VulkanDescriptorFeatures
    {
        public bool DescriptorIndexing { get; }
        public bool NullDescriptor { get; }
        public bool AccelerationStructure { get; }
        public bool SampledImageArrayNonUniformIndexing { get; }
        public bool StorageImageArrayNonUniformIndexing { get; }
        public bool UniformBufferArrayNonUniformIndexing { get; }
        public bool StorageBufferArrayNonUniformIndexing { get; }

        public VulkanDescriptorFeatures(
            bool descriptorIndexing,
            bool nullDescriptor,
            bool accelerationStructure,
            bool sampledImageArrayNonUniformIndexing,
            bool storageImageArrayNonUniformIndexing,
            bool uniformBufferArrayNonUniformIndexing,
            bool storageBufferArrayNonUniformIndexing)
        {
            DescriptorIndexing = descriptorIndexing;
            NullDescriptor = nullDescriptor;
            AccelerationStructure = accelerationStructure;
            SampledImageArrayNonUniformIndexing = sampledImageArrayNonUniformIndexing;
            StorageImageArrayNonUniformIndexing = storageImageArrayNonUniformIndexing;
            UniformBufferArrayNonUniformIndexing = uniformBufferArrayNonUniformIndexing;
            StorageBufferArrayNonUniformIndexing = storageBufferArrayNonUniformIndexing;
        }

        public bool SupportsArray(in VkDescriptorType descriptorType)
        {
            return descriptorType switch
            {
                VkDescriptorType.Sampler or VkDescriptorType.SampledImage =>
                    SampledImageArrayNonUniformIndexing,
                VkDescriptorType.StorageImage =>
                    StorageImageArrayNonUniformIndexing,
                VkDescriptorType.UniformBuffer =>
                    UniformBufferArrayNonUniformIndexing,
                VkDescriptorType.StorageBuffer =>
                    StorageBufferArrayNonUniformIndexing,
                VkDescriptorType.AccelerationStructureKHR =>
                    AccelerationStructure,
                _ => false,
            };
        }
    }

    internal readonly struct VulkanDescriptorLimits
    {
        public uint MaximumBoundSets { get; }
        public uint MaximumSamplersPerSet { get; }
        public uint MaximumSampledImagesPerSet { get; }
        public uint MaximumStorageImagesPerSet { get; }
        public uint MaximumUniformBuffersPerSet { get; }
        public uint MaximumStorageBuffersPerSet { get; }
        public uint MaximumSamplersPerStage { get; }
        public uint MaximumSampledImagesPerStage { get; }
        public uint MaximumStorageImagesPerStage { get; }
        public uint MaximumUniformBuffersPerStage { get; }
        public uint MaximumStorageBuffersPerStage { get; }
        public uint MaximumInputAttachmentsPerSet { get; }
        public uint MaximumInputAttachmentsPerStage { get; }

        public VulkanDescriptorLimits(
            uint maximumBoundSets,
            uint maximumSamplersPerSet,
            uint maximumSampledImagesPerSet,
            uint maximumStorageImagesPerSet,
            uint maximumUniformBuffersPerSet,
            uint maximumStorageBuffersPerSet,
            uint maximumSamplersPerStage,
            uint maximumSampledImagesPerStage,
            uint maximumStorageImagesPerStage,
            uint maximumUniformBuffersPerStage,
            uint maximumStorageBuffersPerStage,
            uint maximumInputAttachmentsPerSet = uint.MaxValue,
            uint maximumInputAttachmentsPerStage = uint.MaxValue)
        {
            MaximumBoundSets = maximumBoundSets;
            MaximumSamplersPerSet = maximumSamplersPerSet;
            MaximumSampledImagesPerSet = maximumSampledImagesPerSet;
            MaximumStorageImagesPerSet = maximumStorageImagesPerSet;
            MaximumUniformBuffersPerSet = maximumUniformBuffersPerSet;
            MaximumStorageBuffersPerSet = maximumStorageBuffersPerSet;
            MaximumSamplersPerStage = maximumSamplersPerStage;
            MaximumSampledImagesPerStage = maximumSampledImagesPerStage;
            MaximumStorageImagesPerStage = maximumStorageImagesPerStage;
            MaximumUniformBuffersPerStage = maximumUniformBuffersPerStage;
            MaximumStorageBuffersPerStage = maximumStorageBuffersPerStage;
            MaximumInputAttachmentsPerSet =
                maximumInputAttachmentsPerSet;
            MaximumInputAttachmentsPerStage =
                maximumInputAttachmentsPerStage;
        }
    }

    internal readonly struct VulkanBindingKey : IEquatable<VulkanBindingKey>
    {
        public uint Slot { get; }
        public ERHIBindType Type { get; }

        public VulkanBindingKey(in uint slot, in ERHIBindType type)
        {
            Slot = slot;
            Type = type;
        }

        public bool Equals(VulkanBindingKey other)
        {
            return Slot == other.Slot && Type == other.Type;
        }

        public override bool Equals(object? obj)
        {
            return obj is VulkanBindingKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Slot, Type);
        }
    }

    internal readonly struct VulkanBindInfo
    {
        public uint Slot { get; }
        public uint TableIndex { get; }
        public uint Count { get; }
        public uint PhysicalBinding { get; }
        public int StateOffset { get; }
        public ERHIBindType Type { get; }
        public ERHIShaderStageMask Stages { get; }
        public ERHIBindingRequirement Requirement { get; }
        public VkDescriptorType NativeDescriptorType { get; }
        public VkShaderStageFlags NativeStages { get; }

        public VulkanBindInfo(
            in uint slot,
            in uint tableIndex,
            in uint count,
            in uint physicalBinding,
            in int stateOffset,
            in ERHIBindType type,
            in ERHIShaderStageMask stages,
            in ERHIBindingRequirement requirement,
            in VkDescriptorType nativeDescriptorType,
            in VkShaderStageFlags nativeStages)
        {
            Slot = slot;
            TableIndex = tableIndex;
            Count = count;
            PhysicalBinding = physicalBinding;
            StateOffset = stateOffset;
            Type = type;
            Stages = stages;
            Requirement = requirement;
            NativeDescriptorType = nativeDescriptorType;
            NativeStages = nativeStages;
        }
    }

    internal readonly struct VulkanDescriptorPoolRequirements :
        IEquatable<VulkanDescriptorPoolRequirements>
    {
        public uint Samplers { get; }
        public uint SampledImages { get; }
        public uint StorageImages { get; }
        public uint UniformBuffers { get; }
        public uint StorageBuffers { get; }
        public uint AccelerationStructures { get; }
        public uint InputAttachments { get; }

        public VulkanDescriptorPoolRequirements(
            uint samplers,
            uint sampledImages,
            uint storageImages,
            uint uniformBuffers,
            uint storageBuffers,
            uint accelerationStructures,
            uint inputAttachments = 0)
        {
            Samplers = samplers;
            SampledImages = sampledImages;
            StorageImages = storageImages;
            UniformBuffers = uniformBuffers;
            StorageBuffers = storageBuffers;
            AccelerationStructures = accelerationStructures;
            InputAttachments = inputAttachments;
        }

        public bool Equals(VulkanDescriptorPoolRequirements other)
        {
            return Samplers == other.Samplers
                && SampledImages == other.SampledImages
                && StorageImages == other.StorageImages
                && UniformBuffers == other.UniformBuffers
                && StorageBuffers == other.StorageBuffers
                && AccelerationStructures == other.AccelerationStructures
                && InputAttachments == other.InputAttachments;
        }

        public override bool Equals(object? obj)
        {
            return obj is VulkanDescriptorPoolRequirements other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Samplers,
                SampledImages,
                StorageImages,
                UniformBuffers,
                StorageBuffers,
                AccelerationStructures,
                InputAttachments);
        }
    }

    internal sealed class VulkanBindingTablePlan
    {
        private static readonly ERHIShaderStageMask[] s_Stages =
        {
            ERHIShaderStageMask.Vertex,
            ERHIShaderStageMask.Fragment,
            ERHIShaderStageMask.Compute,
            ERHIShaderStageMask.Task,
            ERHIShaderStageMask.Mesh,
            ERHIShaderStageMask.RayTracing,
            ERHIShaderStageMask.MachineLearning,
        };

        public uint Index { get; }
        public int DescriptorCount { get; }
        public VulkanBindInfo[] BindInfos { get; }
        public VulkanDescriptorPoolRequirements PoolRequirements { get; }

        private readonly Dictionary<VulkanBindingKey, int> m_BindingMap;

        public VulkanBindingTablePlan(
            in RHIBindingTableLayoutDescriptor descriptor,
            in VulkanDescriptorFeatures features,
            in VulkanDescriptorLimits limits)
        {
            if (limits.MaximumBoundSets == 0 || descriptor.Index >= limits.MaximumBoundSets)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor.Index),
                    descriptor.Index,
                    $"Vulkan argument-table index must be in [0, {limits.MaximumBoundSets}).");
            }

            Index = descriptor.Index;
            BindInfos = new VulkanBindInfo[descriptor.Elements.Length];
            m_BindingMap = new Dictionary<VulkanBindingKey, int>(descriptor.Elements.Length);

            uint samplers = 0;
            uint sampledImages = 0;
            uint storageImages = 0;
            uint uniformBuffers = 0;
            uint storageBuffers = 0;
            uint accelerationStructures = 0;
            int stateOffset = 0;

            Span<RHIBindingTableLayoutElement> elements = descriptor.Elements.Span;
            for (int elementIndex = 0; elementIndex < elements.Length; ++elementIndex)
            {
                ref readonly RHIBindingTableLayoutElement element = ref elements[elementIndex];
                if (element.Count == 0 || element.Count > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(element.Count),
                        element.Count,
                        $"Vulkan argument-table element {elementIndex} count must be in [1, {int.MaxValue}].");
                }
                if (!Enum.IsDefined(element.Requirement))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(element.Requirement),
                        element.Requirement,
                        "Vulkan argument-table binding requirement is undefined.");
                }

                VkDescriptorType nativeType =
                    VulkanUtility.ConvertToVkDescriptorType(element.Type);
                VkShaderStageFlags nativeStages =
                    VulkanUtility.ConvertToVkShaderStages(element.Stages);
                if (nativeType == VkDescriptorType.AccelerationStructureKHR
                    && !features.AccelerationStructure)
                {
                    throw new NotSupportedException(
                        $"Vulkan argument-table element {elementIndex} requires acceleration structures, but the device did not enable them.");
                }
                if (element.Count > 1
                    && (!features.DescriptorIndexing || !features.SupportsArray(nativeType)))
                {
                    throw new NotSupportedException(
                        $"Vulkan argument-table element {elementIndex} ({element.Type}, count {element.Count}) requires native descriptor indexing for that descriptor class.");
                }
                if (element.Requirement == ERHIBindingRequirement.Optional
                    && !features.NullDescriptor)
                {
                    throw new NotSupportedException(
                        $"Vulkan argument-table element {elementIndex} is optional, but robustness2 nullDescriptor was not enabled.");
                }

                VulkanBindingKey key = new VulkanBindingKey(element.Slot, element.Type);
                if (!m_BindingMap.TryAdd(key, elementIndex))
                {
                    throw new ArgumentException(
                        $"Vulkan binding table {Index} contains duplicate logical binding ({element.Type}, slot {element.Slot}).",
                        nameof(descriptor));
                }

                BindInfos[elementIndex] = new VulkanBindInfo(
                    element.Slot,
                    descriptor.Index,
                    element.Count,
                    checked((uint)elementIndex),
                    stateOffset,
                    element.Type,
                    element.Stages,
                    element.Requirement,
                    nativeType,
                    nativeStages);
                stateOffset = checked(stateOffset + (int)element.Count);

                switch (nativeType)
                {
                    case VkDescriptorType.Sampler:
                        samplers = checked(samplers + element.Count);
                        break;
                    case VkDescriptorType.SampledImage:
                        sampledImages = checked(sampledImages + element.Count);
                        break;
                    case VkDescriptorType.StorageImage:
                        storageImages = checked(storageImages + element.Count);
                        break;
                    case VkDescriptorType.UniformBuffer:
                        uniformBuffers = checked(uniformBuffers + element.Count);
                        break;
                    case VkDescriptorType.StorageBuffer:
                        storageBuffers = checked(storageBuffers + element.Count);
                        break;
                    case VkDescriptorType.AccelerationStructureKHR:
                        accelerationStructures = checked(accelerationStructures + element.Count);
                        break;
                }
            }

            DescriptorCount = stateOffset;
            ValidateLimit("sampler", samplers, limits.MaximumSamplersPerSet);
            ValidateLimit("sampled-image", sampledImages, limits.MaximumSampledImagesPerSet);
            ValidateLimit("storage-image", storageImages, limits.MaximumStorageImagesPerSet);
            ValidateLimit("uniform-buffer", uniformBuffers, limits.MaximumUniformBuffersPerSet);
            ValidateLimit("storage-buffer", storageBuffers, limits.MaximumStorageBuffersPerSet);
            ValidatePerStageLimits(limits);

            PoolRequirements = new VulkanDescriptorPoolRequirements(
                samplers,
                sampledImages,
                storageImages,
                uniformBuffers,
                storageBuffers,
                accelerationStructures);
        }

        public int ResolveBinding(in int slot, in ERHIBindType type)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    slot,
                    "Vulkan logical binding slot must not be negative.");
            }
            if (!m_BindingMap.TryGetValue(
                    new VulkanBindingKey((uint)slot, type),
                    out int bindingIndex))
            {
                throw new ArgumentException(
                    $"Vulkan binding table {Index} does not declare logical binding ({type}, slot {slot}).",
                    nameof(slot));
            }
            return bindingIndex;
        }

        public bool StructurallyEquals(VulkanBindingTablePlan? other)
        {
            if (other is null || Index != other.Index || BindInfos.Length != other.BindInfos.Length)
            {
                return false;
            }

            for (int index = 0; index < BindInfos.Length; ++index)
            {
                ref readonly VulkanBindInfo left = ref BindInfos[index];
                ref readonly VulkanBindInfo right = ref other.BindInfos[index];
                if (left.Slot != right.Slot
                    || left.Count != right.Count
                    || left.PhysicalBinding != right.PhysicalBinding
                    || left.Type != right.Type
                    || left.Stages != right.Stages
                    || left.Requirement != right.Requirement)
                {
                    return false;
                }
            }
            return true;
        }

        private void ValidatePerStageLimits(in VulkanDescriptorLimits limits)
        {
            for (int stageIndex = 0; stageIndex < s_Stages.Length; ++stageIndex)
            {
                ERHIShaderStageMask stage = s_Stages[stageIndex];
                uint samplers = 0;
                uint sampledImages = 0;
                uint storageImages = 0;
                uint uniformBuffers = 0;
                uint storageBuffers = 0;
                for (int bindingIndex = 0; bindingIndex < BindInfos.Length; ++bindingIndex)
                {
                    ref readonly VulkanBindInfo binding = ref BindInfos[bindingIndex];
                    if ((binding.Stages & stage) == 0)
                    {
                        continue;
                    }

                    switch (binding.NativeDescriptorType)
                    {
                        case VkDescriptorType.Sampler:
                            samplers = checked(samplers + binding.Count);
                            break;
                        case VkDescriptorType.SampledImage:
                            sampledImages = checked(sampledImages + binding.Count);
                            break;
                        case VkDescriptorType.StorageImage:
                            storageImages = checked(storageImages + binding.Count);
                            break;
                        case VkDescriptorType.UniformBuffer:
                            uniformBuffers = checked(uniformBuffers + binding.Count);
                            break;
                        case VkDescriptorType.StorageBuffer:
                            storageBuffers = checked(storageBuffers + binding.Count);
                            break;
                    }
                }

                ValidateLimit($"{stage} sampler", samplers, limits.MaximumSamplersPerStage);
                ValidateLimit($"{stage} sampled-image", sampledImages, limits.MaximumSampledImagesPerStage);
                ValidateLimit($"{stage} storage-image", storageImages, limits.MaximumStorageImagesPerStage);
                ValidateLimit($"{stage} uniform-buffer", uniformBuffers, limits.MaximumUniformBuffersPerStage);
                ValidateLimit($"{stage} storage-buffer", storageBuffers, limits.MaximumStorageBuffersPerStage);
            }
        }

        private void ValidateLimit(string kind, uint count, uint limit)
        {
            if (count > limit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    count,
                    $"Vulkan binding table {Index} requires {count} {kind} descriptors, exceeding native limit {limit}.");
            }
        }
    }
    #region DescriptorPoolAllocator
internal readonly struct VulkanDescriptorSetLease
    {
        public VkDescriptorPool Pool { get; }
        public VkDescriptorSet Set { get; }

        public VulkanDescriptorSetLease(
            in VkDescriptorPool pool,
            in VkDescriptorSet set)
        {
            Pool = pool;
            Set = set;
        }
    }

    internal sealed class VulkanDescriptorPoolPage
    {
        public VkDescriptorPool Pool { get; }
        public VulkanDescriptorPoolRequirements Requirements { get; }
        public int AllocatedSetCount { get; set; }

        public VulkanDescriptorPoolPage(
            in VkDescriptorPool pool,
            in VulkanDescriptorPoolRequirements requirements)
        {
            Pool = pool;
            Requirements = requirements;
        }
    }

    internal unsafe sealed class VulkanDescriptorPoolAllocator : IDisposable
    {
        private const int SetsPerPage = 32;

        private readonly object m_Gate = new object();
        private readonly VulkanDevice m_Device;
        private readonly List<VulkanDescriptorPoolPage> m_Pages =
            new List<VulkanDescriptorPoolPage>();
        private bool m_Disposed;

        public VulkanDescriptorPoolAllocator(VulkanDevice device)
        {
            m_Device = device;
        }

        internal int AllocatedSetCount
        {
            get
            {
                lock (m_Gate)
                {
                    int total = 0;
                    for (int pageIndex = 0;
                         pageIndex < m_Pages.Count;
                         ++pageIndex)
                    {
                        total = checked(
                            total + m_Pages[pageIndex].AllocatedSetCount);
                    }

                    return total;
                }
            }
        }

        public VulkanDescriptorSetLease Allocate(VulkanBindingTableLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            return Allocate(
                layout.Plan.PoolRequirements,
                layout.NativeDescriptorSetLayout);
        }

        internal VulkanDescriptorSetLease Allocate(
            in VulkanDescriptorPoolRequirements requirements,
            in VkDescriptorSetLayout nativeLayout)
        {
            if (nativeLayout.Handle == 0)
            {
                throw new ArgumentException(
                    "Vulkan descriptor allocation requires a live native layout.",
                    nameof(nativeLayout));
            }
            lock (m_Gate)
            {
                ThrowIfDisposed();
                for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
                {
                    VulkanDescriptorPoolPage page = m_Pages[pageIndex];
                    if (!page.Requirements.Equals(requirements)
                        || page.AllocatedSetCount >= SetsPerPage)
                    {
                        continue;
                    }

                    VkResult result = TryAllocate(
                        page.Pool,
                        nativeLayout,
                        out VkDescriptorSet set);
                    if (result == VkResult.Success)
                    {
                        page.AllocatedSetCount++;
                        return new VulkanDescriptorSetLease(page.Pool, set);
                    }
                    if (result != VkResult.ErrorOutOfPoolMemory
                        && result != VkResult.ErrorFragmentedPool)
                    {
                        VulkanUtility.CheckErrors(result);
                    }
                }

                VulkanDescriptorPoolPage newPage = CreatePage(requirements);
                try
                {
                    VkResult result = TryAllocate(
                        newPage.Pool,
                        nativeLayout,
                        out VkDescriptorSet set);
                    VulkanUtility.CheckErrors(result);
                    newPage.AllocatedSetCount = 1;
                    m_Pages.Add(newPage);
                    return new VulkanDescriptorSetLease(newPage.Pool, set);
                }
                catch
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        newPage.Pool,
                        null);
                    throw;
                }
            }
        }

        public void Free(in VulkanDescriptorSetLease lease)
        {
            if (lease.Pool.Handle == 0 || lease.Set.Handle == 0)
            {
                return;
            }

            lock (m_Gate)
            {
                ThrowIfDisposed();
                int pageIndex = FindPage(lease.Pool);
                VulkanDescriptorPoolPage page = m_Pages[pageIndex];
                VkDescriptorSet set = lease.Set;
                VulkanUtility.CheckErrors(
                    VulkanNative.vkFreeDescriptorSets(
                        m_Device.NativeDevice,
                        lease.Pool,
                        1,
                        &set));
                page.AllocatedSetCount--;
                if (page.AllocatedSetCount < 0)
                {
                    throw new InvalidOperationException(
                        "Vulkan descriptor-pool accounting underflowed.");
                }

                if (page.AllocatedSetCount == 0
                    && HasOtherEmptyPage(pageIndex, page.Requirements))
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        page.Pool,
                        null);
                    m_Pages.RemoveAt(pageIndex);
                }
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                {
                    return;
                }

                for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        m_Pages[pageIndex].Pool,
                        null);
                }
                m_Pages.Clear();
                m_Disposed = true;
            }
        }

        private VulkanDescriptorPoolPage CreatePage(
            in VulkanDescriptorPoolRequirements requirements)
        {
            VkDescriptorPoolSize[] sizes = BuildPoolSizes(requirements);
            VkDescriptorPool pool = default;
            fixed (VkDescriptorPoolSize* sizesPointer = sizes)
            {
                VkDescriptorPoolCreateInfo createInfo =
                    new VkDescriptorPoolCreateInfo
                    {
                        sType = VkStructureType.DescriptorPoolCreateInfo,
                        flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet,
                        maxSets = SetsPerPage,
                        poolSizeCount = checked((uint)sizes.Length),
                        pPoolSizes = sizes.Length == 0 ? null : sizesPointer,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateDescriptorPool(
                        m_Device.NativeDevice,
                        &createInfo,
                        null,
                        &pool));
            }

            return new VulkanDescriptorPoolPage(pool, requirements);
        }

        private static VkDescriptorPoolSize[] BuildPoolSizes(
            in VulkanDescriptorPoolRequirements requirements)
        {
            List<VkDescriptorPoolSize> sizes =
                new List<VkDescriptorPoolSize>(7);
            AddSize(sizes, VkDescriptorType.Sampler, requirements.Samplers);
            AddSize(
                sizes,
                VkDescriptorType.SampledImage,
                requirements.SampledImages);
            AddSize(
                sizes,
                VkDescriptorType.StorageImage,
                requirements.StorageImages);
            AddSize(
                sizes,
                VkDescriptorType.UniformBuffer,
                requirements.UniformBuffers);
            AddSize(
                sizes,
                VkDescriptorType.StorageBuffer,
                requirements.StorageBuffers);
            AddSize(
                sizes,
                VkDescriptorType.AccelerationStructureKHR,
                requirements.AccelerationStructures);
            AddSize(
                sizes,
                VkDescriptorType.InputAttachment,
                requirements.InputAttachments);
            return sizes.ToArray();
        }

        private static void AddSize(
            List<VkDescriptorPoolSize> sizes,
            in VkDescriptorType type,
            in uint descriptorsPerSet)
        {
            if (descriptorsPerSet == 0)
            {
                return;
            }

            sizes.Add(new VkDescriptorPoolSize
            {
                type = type,
                descriptorCount = checked(
                    descriptorsPerSet * (uint)SetsPerPage),
            });
        }

        private VkResult TryAllocate(
            in VkDescriptorPool pool,
            in VkDescriptorSetLayout layout,
            out VkDescriptorSet set)
        {
            VkDescriptorSetLayout nativeLayout = layout;
            VkDescriptorSet nativeSet = default;
            VkDescriptorSetAllocateInfo allocateInfo =
                new VkDescriptorSetAllocateInfo
                {
                    sType = VkStructureType.DescriptorSetAllocateInfo,
                    descriptorPool = pool,
                    descriptorSetCount = 1,
                    pSetLayouts = &nativeLayout,
                };
            VkResult result = VulkanNative.vkAllocateDescriptorSets(
                m_Device.NativeDevice,
                &allocateInfo,
                &nativeSet);
            set = nativeSet;
            return result;
        }

        private int FindPage(in VkDescriptorPool pool)
        {
            for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
            {
                if (m_Pages[pageIndex].Pool.Handle == pool.Handle)
                {
                    return pageIndex;
                }
            }

            throw new InvalidOperationException(
                "Vulkan descriptor-set lease references an unknown pool page.");
        }

        private bool HasOtherEmptyPage(
            in int pageIndex,
            in VulkanDescriptorPoolRequirements requirements)
        {
            for (int index = 0; index < m_Pages.Count; ++index)
            {
                if (index != pageIndex
                    && m_Pages[index].AllocatedSetCount == 0
                    && m_Pages[index].Requirements.Equals(requirements))
                {
                    return true;
                }
            }
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanDescriptorPoolAllocator));
            }
        }
    }
    #endregion
}
