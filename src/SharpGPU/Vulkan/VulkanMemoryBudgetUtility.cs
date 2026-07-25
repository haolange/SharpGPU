using System;
using Vortice.Vulkan;

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
