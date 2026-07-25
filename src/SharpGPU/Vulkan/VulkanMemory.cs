using System;
using Vortice.Vulkan;
using SharpGPU.Mathematics;

namespace SharpGPU
{
    internal static class VulkanMemoryUtility
    {
        internal static VkBufferCreateInfo BuildBufferCreateInfo(in RHIBufferDescriptor descriptor)
        {
            if (descriptor.ByteSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "Buffer byte size must be greater than zero.");
            }
            ValidateStorageMode(descriptor.StorageMode);
            if (descriptor.StorageMode == ERHIStorageMode.Memoryless)
            {
                throw new NotSupportedException(
                    "Vulkan memoryless allocations are image-only.");
            }

            return new VkBufferCreateInfo
            {
                sType = VkStructureType.BufferCreateInfo,
                size = checked((ulong)descriptor.ByteSize),
                usage = VulkanUtility.ConvertToVkBufferUsage(descriptor.UsageFlag),
                sharingMode = VkSharingMode.Exclusive,
            };
        }

        internal static VkImageCreateInfo BuildImageCreateInfo(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            bool sparse)
        {
            ValidateStorageMode(descriptor.StorageMode);
            if (descriptor.MipCount == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture mip count must be non-zero.");
            }
            if (descriptor.Extent.x == 0 || descriptor.Extent.y == 0 || descriptor.Extent.z == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture extent must be non-zero on every axis.");
            }
            if (!Enum.IsDefined(descriptor.Dimension) || descriptor.Dimension == ERHITextureDimension.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture dimension is unknown.");
            }
            if (!Enum.IsDefined(descriptor.SampleCount) || descriptor.SampleCount == ERHISampleCount.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture sample count is unknown.");
            }
            if (!Enum.IsDefined(descriptor.Format) ||
                descriptor.Format is ERHIPixelFormat.Unknown or ERHIPixelFormat.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Texture format is unknown.");
            }

            VkImageCreateFlags flags = VulkanUtility.ConvertToVkImageCreateFlags(descriptor.Dimension);
            if (sparse)
            {
                flags |= VkImageCreateFlags.SparseBinding | VkImageCreateFlags.SparseResidency;
            }

            return new VkImageCreateInfo
            {
                sType = VkStructureType.ImageCreateInfo,
                flags = flags,
                imageType = VulkanUtility.ConvertToVkImageType(descriptor.Dimension),
                format = VulkanUtility.ConvertToVkFormat(descriptor.Format),
                extent = new VkExtent3D
                {
                    width = descriptor.Extent.x,
                    height = descriptor.Extent.y,
                    depth = descriptor.Dimension == ERHITextureDimension.Texture3D
                        ? descriptor.Extent.z
                        : 1,
                },
                mipLevels = descriptor.MipCount,
                arrayLayers = VulkanUtility.GetArrayLayers(descriptor.Dimension, descriptor.Extent.z),
                samples = VulkanUtility.ConvertToVkSampleCount(descriptor.SampleCount),
                tiling = VkImageTiling.Optimal,
                usage = VulkanUtility.ConvertToVkImageUsage(
                    descriptor.UsageFlag,
                    device.SupportsAttachmentFeedbackLoopLayout),
                sharingMode = VkSharingMode.Exclusive,
                initialLayout = VkImageLayout.Undefined,
            };
        }

        internal static ulong FilterCompatibleMemoryTypes(
            VulkanDevice device,
            uint nativeMemoryTypeBits,
            ERHIStorageMode storageMode)
        {
            VkMemoryPropertyFlags requiredProperties =
                VulkanUtility.ConvertToVkMemoryProperty(storageMode);
            ulong result = 0;
            VkPhysicalDeviceMemoryProperties properties = device.MemoryProperties;
            for (uint index = 0; index < properties.memoryTypeCount; ++index)
            {
                uint bit = 1U << checked((int)index);
                if ((nativeMemoryTypeBits & bit) != 0 &&
                    (properties.GetMemoryType(index).propertyFlags & requiredProperties) == requiredProperties)
                {
                    result |= 1UL << checked((int)index);
                }
            }

            if (result == 0)
            {
                throw new NotSupportedException(
                    $"No Vulkan memory type satisfies {storageMode} for the queried resource.");
            }

            return result;
        }

        private static void ValidateStorageMode(ERHIStorageMode storageMode)
        {
            if (!Enum.IsDefined(storageMode) || storageMode == ERHIStorageMode.Pending)
            {
                throw new ArgumentOutOfRangeException(nameof(storageMode), storageMode, "Unknown storage mode.");
            }
        }
    }
}


namespace SharpGPU
{
    internal static unsafe class VulkanSparseMemoryUtility
    {
        internal static void ValidateDescriptor(
            VulkanDevice device,
            in RHITextureDescriptor descriptor)
        {
            _ = VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: true);
            if (descriptor.StorageMode != ERHIStorageMode.GPULocal)
            {
                throw new NotSupportedException(
                    "Vulkan sparse textures require GPU-local storage.");
            }
            if (descriptor.SampleCount != ERHISampleCount.None)
            {
                throw new NotSupportedException(
                    "Vulkan sparse textures currently require one sample per texel.");
            }
            if (descriptor.MipCount > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    "A uint texture extent cannot contain more than 32 sparse mip levels.");
            }
            if (RHIBarrierUtility.InferAspectMask(descriptor.Format) !=
                ERHITextureAspectMask.Color)
            {
                throw new NotSupportedException(
                    "Vulkan sparse texture mapping currently exposes exact color-aspect tiling only.");
            }
            if (descriptor.Dimension is
                ERHITextureDimension.Texture2DMS or
                ERHITextureDimension.Texture2DArrayMS or
                ERHITextureDimension.TextureCube or
                ERHITextureDimension.TextureCubeArray)
            {
                throw new NotSupportedException(
                    "Vulkan sparse texture mapping supports 2D, 2D-array, and 3D color textures.");
            }
            bool dimensionSupported =
                descriptor.Dimension == ERHITextureDimension.Texture3D
                    ? device.SparseResidencyImage3DSupported
                    : device.SparseResidencyImage2DSupported;
            if (!dimensionSupported)
            {
                throw new NotSupportedException(
                    $"Vulkan sparse residency is unavailable for {descriptor.Dimension}.");
            }
        }

        internal static VkImage CreateSparseImage(
            VulkanDevice device,
            in RHITextureDescriptor descriptor)
        {
            ValidateDescriptor(device, descriptor);
            VkImageCreateInfo createInfo =
                VulkanMemoryUtility.BuildImageCreateInfo(device, descriptor, sparse: true);
            VkImage image = default;
            VkResult result = VulkanNative.vkCreateImage(
                device.NativeDevice,
                &createInfo,
                null,
                &image);
            if (result is VkResult.ErrorFormatNotSupported or
                VkResult.ErrorFeatureNotPresent)
            {
                throw new NotSupportedException(
                    $"The Vulkan device cannot create a sparse {descriptor.Dimension} {descriptor.Format} image.");
            }

            VulkanUtility.CheckErrors(result);
            return image;
        }

        internal static RHISparseTextureMemoryRequirements QueryRequirements(
            VulkanDevice device,
            in RHITextureDescriptor descriptor,
            VkImage image)
        {
            ValidateDescriptor(device, descriptor);
            VkMemoryRequirements memoryRequirements;
            VulkanNative.vkGetImageMemoryRequirements(
                device.NativeDevice,
                image,
                &memoryRequirements);
            uint sparseRequirementCount = 0;
            VulkanNative.vkGetImageSparseMemoryRequirements(
                device.NativeDevice,
                image,
                &sparseRequirementCount,
                null);
            if (sparseRequirementCount != 1)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image must expose exactly one color-aspect memory requirement.");
            }

            VkSparseImageMemoryRequirements nativeRequirements;
            uint requestedCount = 1;
            VulkanNative.vkGetImageSparseMemoryRequirements(
                device.NativeDevice,
                image,
                &requestedCount,
                &nativeRequirements);
            if (requestedCount != 1 ||
                nativeRequirements.formatProperties.aspectMask !=
                    VkImageAspectFlags.Color)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image exposes an unsupported aspect or metadata requirement.");
            }

            VkExtent3D granularity =
                nativeRequirements.formatProperties.imageGranularity;
            if (granularity.width == 0 ||
                granularity.height == 0 ||
                granularity.depth == 0)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image reported an invalid image granularity.");
            }
            if (memoryRequirements.alignment == 0 ||
                (memoryRequirements.alignment &
                 (memoryRequirements.alignment - 1)) != 0)
            {
                throw new NotSupportedException(
                    "The Vulkan sparse image reported a non-power-of-two tile alignment.");
            }

            uint arrayLayerCount = VulkanUtility.GetArrayLayers(
                descriptor.Dimension,
                descriptor.Extent.z);
            uint standardMipCount = Math.Min(
                descriptor.MipCount,
                nativeRequirements.imageMipTailFirstLod);
            RHISparseTextureSubresourceTiling[] subresources =
                new RHISparseTextureSubresourceTiling[
                    checked((int)(standardMipCount * arrayLayerCount))];
            int destination = 0;
            for (uint layer = 0; layer < arrayLayerCount; ++layer)
            {
                for (uint mip = 0; mip < standardMipCount; ++mip)
                {
                    uint mipWidth = MipExtent(descriptor.Extent.x, mip);
                    uint mipHeight = MipExtent(descriptor.Extent.y, mip);
                    uint mipDepth = descriptor.Dimension ==
                        ERHITextureDimension.Texture3D
                            ? MipExtent(descriptor.Extent.z, mip)
                            : 1;
                    subresources[destination++] =
                        new RHISparseTextureSubresourceTiling(
                            ERHITextureAspectMask.Color,
                            mip,
                            layer,
                            new uint3(
                                DivideRoundUp(mipWidth, granularity.width),
                                DivideRoundUp(mipHeight, granularity.height),
                                DivideRoundUp(mipDepth, granularity.depth)));
                }
            }

            RHISparseTextureMipTail[] mipTails;
            if (nativeRequirements.imageMipTailSize == 0 ||
                nativeRequirements.imageMipTailFirstLod >= descriptor.MipCount)
            {
                mipTails = Array.Empty<RHISparseTextureMipTail>();
            }
            else if ((nativeRequirements.formatProperties.flags &
                      VkSparseImageFormatFlags.SingleMiptail) != 0)
            {
                mipTails = new[]
                {
                    new RHISparseTextureMipTail(
                        0,
                        ERHITextureAspectMask.Color,
                        nativeRequirements.imageMipTailFirstLod,
                        0,
                        arrayLayerCount,
                        nativeRequirements.imageMipTailOffset,
                        nativeRequirements.imageMipTailSize),
                };
            }
            else
            {
                mipTails = new RHISparseTextureMipTail[
                    checked((int)arrayLayerCount)];
                for (uint layer = 0; layer < arrayLayerCount; ++layer)
                {
                    mipTails[checked((int)layer)] =
                        new RHISparseTextureMipTail(
                            layer,
                            ERHITextureAspectMask.Color,
                            nativeRequirements.imageMipTailFirstLod,
                            layer,
                            1,
                            checked(
                                nativeRequirements.imageMipTailOffset +
                                layer * nativeRequirements.imageMipTailStride),
                            nativeRequirements.imageMipTailSize);
                }
            }

            ulong compatibilityMask =
                VulkanMemoryUtility.FilterCompatibleMemoryTypes(
                    device,
                    memoryRequirements.memoryTypeBits,
                    descriptor.StorageMode);
            RHIResourceMemoryRequirements heapCompatibility =
                new RHIResourceMemoryRequirements(
                    device,
                    memoryRequirements.alignment,
                    memoryRequirements.alignment,
                    descriptor.StorageMode,
                    compatibilityMask,
                    ERHIMemoryResourceKind.Texture);
            return new RHISparseTextureMemoryRequirements(
                device,
                descriptor,
                memoryRequirements.size,
                memoryRequirements.alignment,
                new uint3(
                    granularity.width,
                    granularity.height,
                    granularity.depth),
                heapCompatibility,
                subresources,
                mipTails);
        }

        private static uint MipExtent(uint value, uint mipLevel)
        {
            return Math.Max(1u, value >> checked((int)mipLevel));
        }

        private static uint DivideRoundUp(uint value, uint divisor)
        {
            return checked((uint)(((ulong)value + divisor - 1) / divisor));
        }
    }
}


namespace SharpGPU
{
    internal static unsafe class VulkanMemoryBudgetUtility
    {
        internal const string ExtensionName = "VK_EXT_memory_budget";

        internal static RHICapability CreateCapability(
            bool extensionEnabled,
            uint memoryHeapCount)
        {
            if (memoryHeapCount == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(memoryHeapCount),
                    "A Vulkan physical device must expose at least one memory heap.");
            }

            return RHICapability.FromProbe(
                extensionEnabled,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                $"{ExtensionName} + vkGetPhysicalDeviceMemoryProperties2",
                $"{ExtensionName} is not advertised by the Vulkan physical device.",
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.MemoryHeapCount,
                        memoryHeapCount)));
        }

        internal static RHIMemoryBudget Query(
            VulkanDevice device,
            ERHIStorageMode storageMode)
        {
            ArgumentNullException.ThrowIfNull(device);
            VkMemoryPropertyFlags requiredProperties =
                GetRequiredMemoryProperties(storageMode);

            VkPhysicalDeviceMemoryBudgetPropertiesEXT budgetProperties =
                new VkPhysicalDeviceMemoryBudgetPropertiesEXT
                {
                    sType = VkStructureType.PhysicalDeviceMemoryBudgetPropertiesEXT,
                };
            VkPhysicalDeviceMemoryProperties2 memoryProperties =
                new VkPhysicalDeviceMemoryProperties2
                {
                    sType = VkStructureType.PhysicalDeviceMemoryProperties2,
                    pNext = &budgetProperties,
                };

            if (device.EffectiveApiVersion >=
                VulkanUtility.Version(1, 1, 0))
            {
                VulkanNative.vkGetPhysicalDeviceMemoryProperties2(
                    device.NativePhysicalDevice,
                    &memoryProperties);
            }
            else
            {
                VulkanNative.vkGetPhysicalDeviceMemoryProperties2KHR(
                    device.NativePhysicalDevice,
                    &memoryProperties);
            }

            uint heapCount = memoryProperties.memoryProperties.memoryHeapCount;
            if (heapCount == 0)
            {
                throw NativeFactFailure(
                    "vkGetPhysicalDeviceMemoryProperties2 returned zero memory heaps.");
            }

            Span<bool> selectedHeaps =
                stackalloc bool[checked((int)heapCount)];
            uint selectedHeapCount = 0;
            for (uint memoryTypeIndex = 0;
                memoryTypeIndex <
                    memoryProperties.memoryProperties.memoryTypeCount;
                ++memoryTypeIndex)
            {
                VkMemoryType memoryType =
                    memoryProperties.memoryProperties.memoryTypes[
                        checked((int)memoryTypeIndex)];
                if ((memoryType.propertyFlags & requiredProperties) !=
                    requiredProperties)
                {
                    continue;
                }

                if (memoryType.heapIndex >= heapCount)
                {
                    throw NativeFactFailure(
                        $"Vulkan memory type {memoryTypeIndex} references out-of-range heap {memoryType.heapIndex}.");
                }

                if (!selectedHeaps[checked((int)memoryType.heapIndex)])
                {
                    selectedHeaps[checked((int)memoryType.heapIndex)] = true;
                    ++selectedHeapCount;
                }
            }

            if (selectedHeapCount == 0)
            {
                throw new NotSupportedException(
                    $"No Vulkan memory heap satisfies storage mode {storageMode}.");
            }

            ulong budgetBytes = 0;
            ulong usageBytes = 0;
            for (uint heapIndex = 0; heapIndex < heapCount; ++heapIndex)
            {
                if (!selectedHeaps[checked((int)heapIndex)])
                {
                    continue;
                }

                ulong heapBudget =
                    budgetProperties.heapBudget[checked((int)heapIndex)];
                if (heapBudget == 0)
                {
                    throw NativeFactFailure(
                        $"{ExtensionName} returned a zero budget for active heap {heapIndex}.");
                }

                budgetBytes = checked(budgetBytes + heapBudget);
                usageBytes = checked(
                    usageBytes +
                    budgetProperties.heapUsage[checked((int)heapIndex)]);
            }

            return new RHIMemoryBudget(
                storageMode,
                budgetBytes,
                usageBytes);
        }

        private static VkMemoryPropertyFlags GetRequiredMemoryProperties(
            ERHIStorageMode storageMode)
        {
            if (!Enum.IsDefined(storageMode) ||
                storageMode == ERHIStorageMode.Pending)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storageMode),
                    storageMode,
                    "Unknown storage mode.");
            }

            return VulkanUtility.ConvertToVkMemoryProperty(
                storageMode);
        }

        private static RHIException NativeFactFailure(string message) =>
            new RHIException(
                ERHIErrorCode.NativeFailure,
                ERHIBackend.Vulkan,
                nativeCode: 0,
                message,
                ERHIDeviceState.Operational);
    }
}


namespace SharpGPU
{
    internal unsafe class VulkanHeap : RHIHeap
    {
        public VkDeviceMemory NativeMemory
        {
            get
            {
                ThrowIfDisposed();
                return m_NativeMemory;
            }
        }
        internal uint MemoryTypeIndex { get; }

        private VulkanDevice m_VulkanDevice;
        private VkDeviceMemory m_NativeMemory;
        private readonly object m_MapSync = new object();
        private void* m_MappedBaseAddress;
        private int m_MapReferenceCount;

        public VulkanHeap(VulkanDevice device, in RHIHeapDescription descriptor)
            : base(device, descriptor, SelectCompatibilityBit(device, descriptor))
        {
            m_VulkanDevice = device;

            uint memTypeIndex = 0;
            ulong compatibilityBit = CompatibilityBit;
            while ((compatibilityBit >>= 1) != 0)
            {
                ++memTypeIndex;
            }
            MemoryTypeIndex = memTypeIndex;

            VkMemoryAllocateInfo allocInfo = new VkMemoryAllocateInfo()
            {
                sType = VkStructureType.MemoryAllocateInfo,
                allocationSize = descriptor.Size,
                memoryTypeIndex = memTypeIndex,
            };
            VkMemoryAllocateFlagsInfo allocateFlags = default;
            if (descriptor.Compatibility.NativeAllocationFlags != 0)
            {
                allocateFlags.sType = VkStructureType.MemoryAllocateFlagsInfo;
                allocateFlags.flags =
                    (VkMemoryAllocateFlags)descriptor.Compatibility.NativeAllocationFlags;
                allocInfo.pNext = &allocateFlags;
            }

            fixed (VkDeviceMemory* memPtr = &m_NativeMemory)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateMemory(device.NativeDevice, &allocInfo, null, memPtr));
            }
        }

        internal void* MapShared(bool invalidate)
        {
            ThrowIfDisposed();
            lock (m_MapSync)
            {
                if (m_MapReferenceCount == 0)
                {
                    void* mappedAddress;
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkMapMemory(
                            m_VulkanDevice.NativeDevice,
                            m_NativeMemory,
                            0,
                            ulong.MaxValue,
                            0,
                            &mappedAddress));
                    m_MappedBaseAddress = mappedAddress;
                }

                if (invalidate)
                {
                    VkMappedMemoryRange range =
                        new VkMappedMemoryRange
                        {
                            sType =
                                VkStructureType.MappedMemoryRange,
                            memory = m_NativeMemory,
                            offset = 0,
                            size = ulong.MaxValue,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkInvalidateMappedMemoryRanges(
                            m_VulkanDevice.NativeDevice,
                            1,
                            &range));
                }

                m_MapReferenceCount = checked(m_MapReferenceCount + 1);
                return m_MappedBaseAddress;
            }
        }

        internal void FlushAndUnmapShared(bool flush)
        {
            ThrowIfDisposed();
            lock (m_MapSync)
            {
                if (m_MapReferenceCount <= 0 ||
                    m_MappedBaseAddress == null)
                {
                    throw new InvalidOperationException(
                        "The Vulkan heap has no active shared mapping.");
                }

                if (flush)
                {
                    VkMappedMemoryRange range =
                        new VkMappedMemoryRange
                        {
                            sType =
                                VkStructureType.MappedMemoryRange,
                            memory = m_NativeMemory,
                            offset = 0,
                            size = ulong.MaxValue,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkFlushMappedMemoryRanges(
                            m_VulkanDevice.NativeDevice,
                            1,
                            &range));
                }

                --m_MapReferenceCount;
                if (m_MapReferenceCount == 0)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }
            }
        }

        internal void ReleaseSharedMappingAfterResourceDispose()
        {
            lock (m_MapSync)
            {
                if (m_MapReferenceCount <= 0)
                {
                    return;
                }

                --m_MapReferenceCount;
                if (m_MapReferenceCount == 0 &&
                    m_MappedBaseAddress != null &&
                    !IsDisposed)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }
            }
        }

        private static ulong SelectCompatibilityBit(
            VulkanDevice device,
            in RHIHeapDescription descriptor)
        {
            uint compatibleTypes = checked((uint)descriptor.Compatibility.CompatibilityMask);
            VkMemoryPropertyFlags properties =
                VulkanUtility.ConvertToVkMemoryProperty(descriptor.StorageMode);
            uint memoryTypeIndex = VulkanUtility.FindMemoryType(
                device.MemoryProperties,
                compatibleTypes,
                properties);
            return 1UL << checked((int)memoryTypeIndex);
        }

        protected override void Release()
        {
            lock (m_MapSync)
            {
                if (m_MappedBaseAddress != null)
                {
                    VulkanNative.vkUnmapMemory(
                        m_VulkanDevice.NativeDevice,
                        m_NativeMemory);
                    m_MappedBaseAddress = null;
                }

                m_MapReferenceCount = 0;
            }

            VulkanNative.vkFreeMemory(m_VulkanDevice.NativeDevice, m_NativeMemory, null);
            m_NativeMemory = default;
        }
    }
}

