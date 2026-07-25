using System;
using System.Threading;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal unsafe sealed class VulkanArgumentTableLayout :
        RHIArgumentTableLayout
    {
        public VkDescriptorSetLayout NativeDescriptorSetLayout =>
            m_NativeDescriptorSetLayout;
        internal VulkanDevice Device => m_VulkanDevice;
        internal VulkanArgumentTablePlan Plan { get; }

        private readonly VulkanDevice m_VulkanDevice;
        private VkDescriptorSetLayout m_NativeDescriptorSetLayout;

        public VulkanArgumentTableLayout(
            VulkanDevice device,
            in RHIArgumentTableLayoutDescriptor descriptor)
            : base(descriptor)
        {
            m_VulkanDevice = device;
            Plan = new VulkanArgumentTablePlan(
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

        internal bool StructurallyEquals(VulkanArgumentTableLayout? other)
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

    internal unsafe sealed class VulkanArgumentTable : RHIArgumentTable
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
        internal VulkanArgumentTableLayout Layout => m_Layout;
        internal ulong DescriptorRevision =>
            checked((ulong)Volatile.Read(
                ref m_DescriptorRevision));

        private readonly VulkanDevice m_VulkanDevice;
        private readonly VulkanArgumentTableLayout m_Layout;
        private readonly bool[] m_BoundStates;
        private readonly VulkanSampledImageDescriptorFact[]
            m_SampledImageFacts;
        private VulkanDescriptorSetLease m_Lease;
        private int m_MissingRequiredDescriptorCount;
        private long m_DescriptorRevision;

        public VulkanArgumentTable(
            VulkanDevice device,
            in RHIArgumentTableDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Layout = descriptor.Layout as VulkanArgumentTableLayout
                ?? throw new ArgumentException(
                    "Vulkan argument table requires a Vulkan layout.",
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
                    $"Vulkan argument table {m_Layout.Plan.Index} received "
                    + $"{descriptor.Elements.Length} initial elements for "
                    + $"{m_Layout.Plan.BindInfos.Length} bindings.",
                    nameof(descriptor));
            }

            m_BoundStates = new bool[m_Layout.Plan.DescriptorCount];
            m_SampledImageFacts =
                new VulkanSampledImageDescriptorFact[
                    m_Layout.Plan.DescriptorCount];
            for (int bindingIndex = 0;
                bindingIndex < m_Layout.Plan.BindInfos.Length;
                ++bindingIndex)
            {
                ref readonly VulkanBindInfo binding =
                    ref m_Layout.Plan.BindInfos[bindingIndex];
                if (binding.Requirement
                    == ERHIArgumentBindingRequirement.Required)
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
                Span<RHIArgumentTableElement> initialElements =
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
            in RHIArgumentTableElement element,
            in ERHIBindType bindType,
            in int slot)
        {
            ThrowIfDisposedAndLayout();
            int bindingIndex = m_Layout.Plan.ResolveBinding(slot, bindType);
            SetBinding(bindingIndex, arrayIndex: 0, element);
        }

        public override void SetBindElement(
            in RHIArgumentTableElement element,
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
                    != ERHIArgumentBindingRequirement.Required)
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
                            $"Vulkan argument table {binding.TableIndex} cannot "
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
                    != ERHIArgumentBindingRequirement.Optional)
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
            in RHIArgumentTableElement element)
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
                    == ERHIArgumentBindingRequirement.Optional)
            {
                WriteDescriptor(binding, arrayIndex, element, isBound);
            }

            int stateIndex = binding.StateOffset + arrayIndex;
            bool wasBound = m_BoundStates[stateIndex];
            m_BoundStates[stateIndex] = isBound;
            if (binding.Requirement
                    == ERHIArgumentBindingRequirement.Required
                && wasBound != isBound)
            {
                m_MissingRequiredDescriptorCount += isBound ? -1 : 1;
            }
            UpdateSampledImageFact(
                in binding,
                arrayIndex,
                in element,
                isBound);
            checked
            {
                Interlocked.Increment(
                    ref m_DescriptorRevision);
            }
        }

        private bool ValidateElement(
            in RHIArgumentTableElement element,
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
                    $"Vulkan argument table {binding.TableIndex} binding "
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
            in RHIArgumentTableElement element,
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
                        + "is not supported by argument tables.");
            }
        }

        internal VulkanDescriptorSetLease CloneForSampledFeedback(
            ReadOnlySpan<VulkanSampledFeedbackAttachmentFact>
                attachments,
            out byte matchedAttachmentMask,
            out ulong descriptorRevision)
        {
            ThrowIfDisposedAndLayout();
            EnsureReadyForBinding();
            if (attachments.Length == 0)
            {
                throw new ArgumentException(
                    "A sampled-feedback clone requires at least one " +
                    "attachment fact.",
                    nameof(attachments));
            }

            descriptorRevision = DescriptorRevision;
            VulkanDescriptorSetLease clone =
                m_VulkanDevice.DescriptorPoolAllocator.Allocate(
                    m_Layout);
            try
            {
                CopyAllDescriptors(clone.Set);
                matchedAttachmentMask = 0;
                for (int bindingIndex = 0;
                     bindingIndex <
                        m_Layout.Plan.BindInfos.Length;
                     ++bindingIndex)
                {
                    ref readonly VulkanBindInfo binding =
                        ref m_Layout.Plan.BindInfos[
                            bindingIndex];
                    if (binding.NativeDescriptorType !=
                        VkDescriptorType.SampledImage)
                    {
                        continue;
                    }
                    for (int arrayIndex = 0;
                         (uint)arrayIndex < binding.Count;
                         ++arrayIndex)
                    {
                        VulkanSampledImageDescriptorFact fact =
                            m_SampledImageFacts[
                                binding.StateOffset +
                                arrayIndex];
                        if (!fact.IsBound)
                        {
                            continue;
                        }

                        bool rewrite = false;
                        for (int attachmentIndex = 0;
                             attachmentIndex <
                                attachments.Length;
                             ++attachmentIndex)
                        {
                            ref readonly
                                VulkanSampledFeedbackAttachmentFact
                                attachment =
                                    ref attachments[
                                        attachmentIndex];
                            RHITextureSubresourceRange factRange =
                                fact.Range;
                            RHITextureSubresourceRange attachmentRange =
                                attachment.Range;
                            EVulkanSampledFeedbackRangeRelation relation =
                                VulkanSampledFeedbackRangeUtility.Classify(
                                    fact.Image,
                                    in factRange,
                                    attachment.Image,
                                    in attachmentRange);
                            if (relation is
                                EVulkanSampledFeedbackRangeRelation
                                    .DifferentImage or
                                EVulkanSampledFeedbackRangeRelation
                                    .Disjoint)
                            {
                                continue;
                            }
                            if (relation ==
                                EVulkanSampledFeedbackRangeRelation
                                    .PartialOverlap)
                            {
                                throw new ArgumentException(
                                    "A sampled-image descriptor partially " +
                                    "overlaps a SampledFeedback " +
                                    "attachment. Vulkan requires an exact " +
                                    "aspect/mip/layer view match.");
                            }
                            rewrite = true;
                            matchedAttachmentMask |=
                                attachment.AttachmentMask;
                        }
                        if (rewrite)
                        {
                            RewriteSampledImageDescriptor(
                                clone.Set,
                                in binding,
                                arrayIndex,
                                in fact);
                        }
                    }
                }

                if (DescriptorRevision != descriptorRevision)
                {
                    throw new InvalidOperationException(
                        "The Vulkan argument table changed while its " +
                        "sampled-feedback descriptor clone was being " +
                        "created. Rebind after external synchronization.");
                }
                return clone;
            }
            catch
            {
                m_VulkanDevice.DescriptorPoolAllocator.Free(
                    in clone);
                throw;
            }
        }

        private void CopyAllDescriptors(
            VkDescriptorSet destinationSet)
        {
            VulkanBindInfo[] bindings =
                m_Layout.Plan.BindInfos;
            VkCopyDescriptorSet* copies =
                stackalloc VkCopyDescriptorSet[
                    Math.Max(1, bindings.Length)];
            for (int bindingIndex = 0;
                 bindingIndex < bindings.Length;
                 ++bindingIndex)
            {
                ref readonly VulkanBindInfo binding =
                    ref bindings[bindingIndex];
                copies[bindingIndex] =
                    new VkCopyDescriptorSet
                    {
                        sType =
                            VkStructureType.CopyDescriptorSet,
                        srcSet = m_Lease.Set,
                        srcBinding =
                            binding.PhysicalBinding,
                        srcArrayElement = 0,
                        dstSet = destinationSet,
                        dstBinding =
                            binding.PhysicalBinding,
                        dstArrayElement = 0,
                        descriptorCount = binding.Count,
                    };
            }
            VulkanNative.vkUpdateDescriptorSets(
                m_VulkanDevice.NativeDevice,
                0,
                null,
                checked((uint)bindings.Length),
                bindings.Length == 0 ? null : copies);
        }

        private void RewriteSampledImageDescriptor(
            VkDescriptorSet destinationSet,
            in VulkanBindInfo binding,
            int arrayIndex,
            in VulkanSampledImageDescriptorFact fact)
        {
            VkDescriptorImageInfo imageInfo =
                new VkDescriptorImageInfo
                {
                    imageView = fact.ImageView,
                    imageLayout =
                        VkImageLayout
                            .AttachmentFeedbackLoopOptimalEXT,
                };
            VkWriteDescriptorSet write =
                new VkWriteDescriptorSet
                {
                    sType =
                        VkStructureType.WriteDescriptorSet,
                    dstSet = destinationSet,
                    dstBinding = binding.PhysicalBinding,
                    dstArrayElement =
                        checked((uint)arrayIndex),
                    descriptorCount = 1,
                    descriptorType =
                        VkDescriptorType.SampledImage,
                    pImageInfo = &imageInfo,
                };
            VulkanNative.vkUpdateDescriptorSets(
                m_VulkanDevice.NativeDevice,
                1,
                &write,
                0,
                null);
        }

        private void UpdateSampledImageFact(
            in VulkanBindInfo binding,
            int arrayIndex,
            in RHIArgumentTableElement element,
            bool isBound)
        {
            if (binding.NativeDescriptorType !=
                VkDescriptorType.SampledImage)
            {
                return;
            }
            int stateIndex =
                binding.StateOffset + arrayIndex;
            if (!isBound)
            {
                m_SampledImageFacts[stateIndex] =
                    default;
                return;
            }

            VulkanTextureView view =
                (VulkanTextureView)element.TextureView;
            VulkanTexture texture = view.VulkanTexture;
            RHITextureViewDescriptor descriptor =
                view.Descriptor;
            RHITextureSubresourceRange range =
                VulkanTextureSubresourceRangeUtility.Normalize(
                    texture.Descriptor,
                    new RHITextureSubresourceRange
                    {
                        AspectMask =
                            ERHITextureAspectMask.Color,
                        BaseMipLevel =
                            descriptor.BaseMipLevel,
                        MipLevelCount =
                            descriptor.MipCount,
                        BaseArrayLayer =
                            descriptor.BaseArraySlice,
                        ArrayLayerCount =
                            descriptor.ArrayCount == 0
                                ? 1u
                                : descriptor.ArrayCount,
                    });
            m_SampledImageFacts[stateIndex] =
                new VulkanSampledImageDescriptorFact(
                    texture.NativeImage,
                    view.NativeImageView,
                    in range);
        }


        private void ThrowIfDisposedAndLayout()
        {
            ThrowIfDisposed();
            if (m_Layout.IsDisposed)
            {
                throw new ObjectDisposedException(
                    $"VulkanArgumentTableLayout[{m_Layout.Plan.Index}]");
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
