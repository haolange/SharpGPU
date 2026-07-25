using System;
using System.Collections.Generic;
using Vortice.Vulkan;

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
        public ERHIArgumentBindingRequirement Requirement { get; }
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
            in ERHIArgumentBindingRequirement requirement,
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

    internal sealed class VulkanArgumentTablePlan
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

        public VulkanArgumentTablePlan(
            in RHIArgumentTableLayoutDescriptor descriptor,
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

            Span<RHIArgumentTableLayoutElement> elements = descriptor.Elements.Span;
            for (int elementIndex = 0; elementIndex < elements.Length; ++elementIndex)
            {
                ref readonly RHIArgumentTableLayoutElement element = ref elements[elementIndex];
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
                if (element.Requirement == ERHIArgumentBindingRequirement.Optional
                    && !features.NullDescriptor)
                {
                    throw new NotSupportedException(
                        $"Vulkan argument-table element {elementIndex} is optional, but robustness2 nullDescriptor was not enabled.");
                }

                VulkanBindingKey key = new VulkanBindingKey(element.Slot, element.Type);
                if (!m_BindingMap.TryAdd(key, elementIndex))
                {
                    throw new ArgumentException(
                        $"Vulkan argument table {Index} contains duplicate logical binding ({element.Type}, slot {element.Slot}).",
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
                    $"Vulkan argument table {Index} does not declare logical binding ({type}, slot {slot}).",
                    nameof(slot));
            }
            return bindingIndex;
        }

        public bool StructurallyEquals(VulkanArgumentTablePlan? other)
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
                    $"Vulkan argument table {Index} requires {count} {kind} descriptors, exceeding native limit {limit}.");
            }
        }
    }
}
