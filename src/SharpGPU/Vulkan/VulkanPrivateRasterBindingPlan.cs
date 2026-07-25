using System;
using System.Numerics;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal readonly struct VulkanPrivateRasterBindingPlan
    {
        internal const uint InputAttachmentBindingBase = 0;
        internal const uint RasterOrderedBindingBase = 8;

        internal uint DescriptorSet { get; }
        internal byte LocalInputMask { get; }
        internal byte LocalInputBindingMask { get; }
        internal byte RasterOrderedMask { get; }
        internal uint InputAttachmentCount =>
            checked((uint)BitOperations.PopCount((uint)LocalInputBindingMask));
        internal uint StorageImageCount =>
            checked((uint)BitOperations.PopCount((uint)RasterOrderedMask));
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            new VulkanDescriptorPoolRequirements(
                samplers: 0,
                sampledImages: 0,
                storageImages: StorageImageCount,
                uniformBuffers: 0,
                storageBuffers: 0,
                accelerationStructures: 0,
                inputAttachments: InputAttachmentCount);

        internal bool HasPrivateBindings =>
            LocalInputBindingMask != 0 ||
            RasterOrderedMask != 0;

        private VulkanPrivateRasterBindingPlan(
            uint descriptorSet,
            byte localInputMask,
            byte localInputBindingMask,
            byte rasterOrderedMask)
        {
            DescriptorSet = descriptorSet;
            LocalInputMask = localInputMask;
            LocalInputBindingMask = localInputBindingMask;
            RasterOrderedMask = rasterOrderedMask;
        }

        internal bool UsesInputAttachmentBinding(int inputIndex)
        {
            if ((uint)inputIndex >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return (LocalInputBindingMask & (1 << inputIndex)) != 0;
        }

        internal uint GetInputAttachmentBinding(int inputIndex)
        {
            if (!UsesInputAttachmentBinding(inputIndex))
            {
                throw new InvalidOperationException(
                    $"Input index {inputIndex} is not a private Vulkan " +
                    "input-attachment binding.");
            }
            return checked(
                InputAttachmentBindingBase +
                checked((uint)inputIndex));
        }

        internal uint GetRasterOrderedBinding(
            int logicalAttachment)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            byte bit =
                checked((byte)(1 << logicalAttachment));
            if ((RasterOrderedMask & bit) == 0)
            {
                throw new InvalidOperationException(
                    $"Logical attachment {logicalAttachment} is not " +
                    "raster-ordered in this pipeline.");
            }
            return checked(
                RasterOrderedBindingBase +
                checked((uint)logicalAttachment));
        }

        internal static uint GetPrivateAttachmentDescriptorSet(
            ReadOnlySpan<uint> ordinaryDescriptorSets)
        {
            bool hasOrdinarySet = false;
            uint highestOrdinarySet = 0;
            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet = ordinaryDescriptorSets[index];
                if (!hasOrdinarySet ||
                    descriptorSet > highestOrdinarySet)
                {
                    highestOrdinarySet = descriptorSet;
                    hasOrdinarySet = true;
                }
            }

            if (hasOrdinarySet &&
                highestOrdinarySet == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordinaryDescriptorSets),
                    highestOrdinarySet,
                    "The highest Vulkan descriptor-set index leaves no " +
                    "representable slot for the private attachment set.");
            }
            return hasOrdinarySet
                ? highestOrdinarySet + 1
                : 0;
        }

        internal static VulkanPrivateRasterBindingPlan Compile(
            ReadOnlySpan<uint> ordinaryDescriptorSets,
            uint maximumBoundDescriptorSets,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumBoundDescriptorSets == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBoundDescriptorSets),
                    maximumBoundDescriptorSets,
                    "Vulkan must expose at least one bound descriptor set.");
            }

            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet =
                    ordinaryDescriptorSets[index];
                if (descriptorSet >= maximumBoundDescriptorSets)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ordinaryDescriptorSets),
                        descriptorSet,
                        $"Ordinary Vulkan descriptor set {descriptorSet} " +
                        $"exceeds maxBoundDescriptorSets " +
                        $"{maximumBoundDescriptorSets}.");
                }
                for (int previous = 0;
                     previous < index;
                     ++previous)
                {
                    if (ordinaryDescriptorSets[previous] ==
                        descriptorSet)
                    {
                        throw new ArgumentException(
                            $"Ordinary Vulkan descriptor set " +
                            $"{descriptorSet} is duplicated.",
                            nameof(ordinaryDescriptorSets));
                    }
                }
            }
            uint privateDescriptorSet =
                GetPrivateAttachmentDescriptorSet(
                    ordinaryDescriptorSets);

            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            byte localInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~rasterOrderedMask));
            byte localInputBindingMask = 0;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0 ||
                    (rasterOrderedMask &
                     (1 << logicalAttachment)) != 0)
                {
                    continue;
                }
                localInputBindingMask |=
                    checked((byte)(1 << inputIndex));
            }

            bool hasPrivateBindings =
                localInputBindingMask != 0 ||
                rasterOrderedMask != 0;
            if (hasPrivateBindings &&
                privateDescriptorSet >= maximumBoundDescriptorSets)
            {
                throw new NotSupportedException(
                    $"Vulkan ordinary descriptor sets consume slots through " +
                    $"{privateDescriptorSet - 1}; no slot remains for the private " +
                    $"attachment set within maxBoundDescriptorSets " +
                    $"{maximumBoundDescriptorSets}.");
            }

            return new VulkanPrivateRasterBindingPlan(
                privateDescriptorSet,
                localInputMask,
                localInputBindingMask,
                rasterOrderedMask);
        }

        internal static void ValidatePipelineLayoutIdentity(
            bool isDisposed,
            object? actualDevice,
            object expectedDevice)
        {
            ArgumentNullException.ThrowIfNull(expectedDevice);
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Vulkan pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }
    }

    internal unsafe sealed class VulkanPrivateRasterDescriptorLayout :
        IDisposable
    {
        internal VulkanPrivateRasterBindingPlan Plan { get; }
        internal VkDescriptorSetLayout NativeLayout { get; private set; }
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            Plan.PoolRequirements;

        private readonly VkDevice m_NativeDevice;
        private bool m_Disposed;

        internal VulkanPrivateRasterDescriptorLayout(
            VulkanDevice device,
            in VulkanPrivateRasterBindingPlan plan)
        {
            ArgumentNullException.ThrowIfNull(device);
            if (!plan.HasPrivateBindings)
            {
                throw new ArgumentException(
                    "A Vulkan private descriptor layout requires at least " +
                    "one input-attachment or raster-ordered binding.",
                    nameof(plan));
            }
            ValidateLimits(device.DescriptorLimits, in plan);

            m_NativeDevice = device.NativeDevice;
            Plan = plan;
            VkDescriptorSetLayoutBinding* bindings =
                stackalloc VkDescriptorSetLayoutBinding[
                    RHIAttachmentIndexArray.MaxAttachments * 2];
            int bindingCount = 0;
            for (int inputIndex = 0;
                 inputIndex < RHIAttachmentIndexArray.MaxAttachments;
                 ++inputIndex)
            {
                if (!plan.UsesInputAttachmentBinding(inputIndex))
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetInputAttachmentBinding(inputIndex),
                        descriptorType =
                            VkDescriptorType.InputAttachment,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }
            for (int logicalAttachment = 0;
                 logicalAttachment <
                    RHIAttachmentIndexArray.MaxAttachments;
                 ++logicalAttachment)
            {
                byte bit =
                    checked((byte)(1 << logicalAttachment));
                if ((plan.RasterOrderedMask & bit) == 0)
                {
                    continue;
                }
                bindings[bindingCount++] =
                    new VkDescriptorSetLayoutBinding
                    {
                        binding =
                            plan.GetRasterOrderedBinding(
                                logicalAttachment),
                        descriptorType =
                            VkDescriptorType.StorageImage,
                        descriptorCount = 1,
                        stageFlags = VkShaderStageFlags.Fragment,
                    };
            }

            VkDescriptorSetLayoutCreateInfo createInfo =
                new VkDescriptorSetLayoutCreateInfo
                {
                    sType =
                        VkStructureType.DescriptorSetLayoutCreateInfo,
                    bindingCount = checked((uint)bindingCount),
                    pBindings = bindings,
                };
            VkDescriptorSetLayout nativeLayout = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateDescriptorSetLayout(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &nativeLayout));
            NativeLayout = nativeLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (NativeLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(
                    m_NativeDevice,
                    NativeLayout,
                    null);
                NativeLayout = default;
            }
            m_Disposed = true;
        }

        private static void ValidateLimits(
            in VulkanDescriptorLimits limits,
            in VulkanPrivateRasterBindingPlan plan)
        {
            if (plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerSet ||
                plan.InputAttachmentCount >
                    limits.MaximumInputAttachmentsPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster input-attachment count " +
                    $"{plan.InputAttachmentCount} exceeds the device " +
                    "descriptor limits.");
            }
            if (plan.StorageImageCount >
                    limits.MaximumStorageImagesPerSet ||
                plan.StorageImageCount >
                    limits.MaximumStorageImagesPerStage)
            {
                throw new NotSupportedException(
                    $"Vulkan private raster storage-image count " +
                    $"{plan.StorageImageCount} exceeds the device " +
                    "descriptor limits.");
            }
        }
    }

    internal sealed class VulkanDescriptorSetLeaseTransaction :
        IDisposable
    {
        private readonly Action<VulkanDescriptorSetLease> m_Rollback;
        private VulkanDescriptorSetLease m_Lease;
        private bool m_Committed;
        private bool m_Disposed;

        internal VulkanDescriptorSetLeaseTransaction(
            in VulkanDescriptorSetLease lease,
            Action<VulkanDescriptorSetLease> rollback)
        {
            ArgumentNullException.ThrowIfNull(rollback);
            if (lease.Pool.Handle == 0 || lease.Set.Handle == 0)
            {
                throw new ArgumentException(
                    "A private Vulkan descriptor transaction requires a " +
                    "complete native lease.",
                    nameof(lease));
            }
            m_Lease = lease;
            m_Rollback = rollback;
        }

        internal VulkanDescriptorSetLease Lease
        {
            get
            {
                ObjectDisposedException.ThrowIf(m_Disposed, this);
                return m_Lease;
            }
        }

        internal void Commit(
            Action<VulkanDescriptorSetLease> register)
        {
            ObjectDisposedException.ThrowIf(m_Disposed, this);
            ArgumentNullException.ThrowIfNull(register);
            if (m_Committed)
            {
                throw new InvalidOperationException(
                    "The Vulkan descriptor lease is already committed.");
            }
            register(m_Lease);
            m_Committed = true;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            try
            {
                if (!m_Committed)
                {
                    m_Rollback(m_Lease);
                }
            }
            finally
            {
                m_Lease = default;
                m_Disposed = true;
            }
        }
    }
}
