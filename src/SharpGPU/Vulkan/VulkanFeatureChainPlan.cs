using System;

namespace SharpGPU
{
    internal enum EVulkanDescriptorIndexingPath : byte
    {
        Unavailable = 0,
        Vulkan12Core = 1,
        ExtDescriptorIndexing = 2,
    }

    internal enum EVulkanFeatureProvenance : byte
    {
        Unavailable = 0,
        Vulkan12Core = 1,
        Vulkan13Core = 2,
        Vulkan14Core = 3,
        KhrExtension = 4,
        ExtExtension = 5,
    }

    internal readonly struct VulkanFeatureChainPlan
    {
        public uint EffectiveApiVersion { get; }
        public bool UseVulkan12Features { get; }
        public bool UseVulkan13Features { get; }
        public bool UseVulkan14Features { get; }
        public bool EnableDynamicRenderingLocalReadExtension { get; }
        public bool UseDynamicRenderingLocalReadExtensionFeatureStruct =>
            DynamicRenderingLocalReadQueryProvenance ==
                EVulkanFeatureProvenance.KhrExtension;
        public EVulkanDescriptorIndexingPath DescriptorIndexingPath { get; }
        public bool UseDynamicRenderingExtension { get; }
        public bool UseSynchronization2Extension { get; }
        public bool SupportsRenderPass2 { get; }
        public bool UseCreateRenderPass2Extension { get; }
        public EVulkanFeatureProvenance DynamicRenderingProvenance { get; }
        public EVulkanFeatureProvenance RenderPass2Provenance { get; }
        public EVulkanFeatureProvenance DynamicRenderingLocalReadQueryProvenance { get; }
        public EVulkanFeatureProvenance AttachmentFeedbackLoopLayoutQueryProvenance { get; }
        public EVulkanFeatureProvenance FragmentShaderPixelInterlockQueryProvenance { get; }
        public EVulkanFeatureProvenance UnifiedImageLayoutsQueryProvenance { get; }

        private VulkanFeatureChainPlan(
            uint effectiveApiVersion,
            bool useVulkan12Features,
            bool useVulkan13Features,
            bool useVulkan14Features,
            bool enableDynamicRenderingLocalReadExtension,
            EVulkanDescriptorIndexingPath descriptorIndexingPath,
            bool useDynamicRenderingExtension,
            bool useSynchronization2Extension,
            bool supportsRenderPass2,
            bool useCreateRenderPass2Extension,
            EVulkanFeatureProvenance dynamicRenderingProvenance,
            EVulkanFeatureProvenance renderPass2Provenance,
            EVulkanFeatureProvenance dynamicRenderingLocalReadQueryProvenance,
            EVulkanFeatureProvenance attachmentFeedbackLoopLayoutQueryProvenance,
            EVulkanFeatureProvenance fragmentShaderPixelInterlockQueryProvenance,
            EVulkanFeatureProvenance unifiedImageLayoutsQueryProvenance)
        {
            EffectiveApiVersion = effectiveApiVersion;
            UseVulkan12Features = useVulkan12Features;
            UseVulkan13Features = useVulkan13Features;
            UseVulkan14Features = useVulkan14Features;
            EnableDynamicRenderingLocalReadExtension =
                enableDynamicRenderingLocalReadExtension;
            DescriptorIndexingPath = descriptorIndexingPath;
            UseDynamicRenderingExtension = useDynamicRenderingExtension;
            UseSynchronization2Extension = useSynchronization2Extension;
            SupportsRenderPass2 = supportsRenderPass2;
            UseCreateRenderPass2Extension = useCreateRenderPass2Extension;
            DynamicRenderingProvenance = dynamicRenderingProvenance;
            RenderPass2Provenance = renderPass2Provenance;
            DynamicRenderingLocalReadQueryProvenance =
                dynamicRenderingLocalReadQueryProvenance;
            AttachmentFeedbackLoopLayoutQueryProvenance =
                attachmentFeedbackLoopLayoutQueryProvenance;
            FragmentShaderPixelInterlockQueryProvenance =
                fragmentShaderPixelInterlockQueryProvenance;
            UnifiedImageLayoutsQueryProvenance =
                unifiedImageLayoutsQueryProvenance;
        }

        public static VulkanFeatureChainPlan Create(
            uint instanceApiVersion,
            uint physicalDeviceApiVersion,
            bool hasDescriptorIndexingExtension,
            bool hasDynamicRenderingExtension,
            bool hasCreateRenderPass2Extension,
            bool hasSynchronization2Extension,
            bool hasDynamicRenderingLocalReadExtension = false,
            bool hasAttachmentFeedbackLoopLayoutExtension = false,
            bool hasFragmentShaderInterlockExtension = false,
            bool hasUnifiedImageLayoutsExtension = false)
        {
            ValidateInstanceApiVersion(instanceApiVersion);
            ValidatePhysicalDeviceApiVersion(physicalDeviceApiVersion);

            uint effectiveApiVersion = Math.Min(
                instanceApiVersion,
                physicalDeviceApiVersion);
            bool useVulkan12Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 2, 0);
            bool useVulkan13Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 3, 0);
            bool useVulkan14Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 4, 0);

            EVulkanDescriptorIndexingPath descriptorIndexingPath =
                useVulkan12Features
                    ? EVulkanDescriptorIndexingPath.Vulkan12Core
                    : hasDescriptorIndexingExtension
                        ? EVulkanDescriptorIndexingPath.ExtDescriptorIndexing
                        : EVulkanDescriptorIndexingPath.Unavailable;

            bool useCreateRenderPass2Extension =
                !useVulkan12Features && hasCreateRenderPass2Extension;
            bool useDynamicRenderingExtension =
                !useVulkan13Features && hasDynamicRenderingExtension;

            return new VulkanFeatureChainPlan(
                effectiveApiVersion,
                useVulkan12Features,
                useVulkan13Features,
                useVulkan14Features,
                enableDynamicRenderingLocalReadExtension:
                    hasDynamicRenderingLocalReadExtension,
                descriptorIndexingPath,
                useDynamicRenderingExtension,
                useSynchronization2Extension:
                    !useVulkan13Features && hasSynchronization2Extension,
                supportsRenderPass2:
                    useVulkan12Features ||
                    useCreateRenderPass2Extension,
                useCreateRenderPass2Extension,
                dynamicRenderingProvenance:
                    useVulkan13Features
                        ? EVulkanFeatureProvenance.Vulkan13Core
                        : useDynamicRenderingExtension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable,
                renderPass2Provenance:
                    useVulkan12Features
                        ? EVulkanFeatureProvenance.Vulkan12Core
                        : useCreateRenderPass2Extension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable,
                dynamicRenderingLocalReadQueryProvenance:
                    useVulkan14Features
                        ? EVulkanFeatureProvenance.Vulkan14Core
                        : hasDynamicRenderingLocalReadExtension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable,
                attachmentFeedbackLoopLayoutQueryProvenance:
                    hasAttachmentFeedbackLoopLayoutExtension
                        ? EVulkanFeatureProvenance.ExtExtension
                        : EVulkanFeatureProvenance.Unavailable,
                fragmentShaderPixelInterlockQueryProvenance:
                    hasFragmentShaderInterlockExtension
                        ? EVulkanFeatureProvenance.ExtExtension
                        : EVulkanFeatureProvenance.Unavailable,
                unifiedImageLayoutsQueryProvenance:
                    hasUnifiedImageLayoutsExtension
                        ? EVulkanFeatureProvenance.KhrExtension
                        : EVulkanFeatureProvenance.Unavailable);
        }

        private static void ValidateInstanceApiVersion(uint version)
        {
            uint minimum = VulkanUtility.Version(1, 0, 0);
            if (version < minimum || GetMajor(version) != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    "The negotiated Vulkan instance API version must be Vulkan 1.x.");
            }
        }

        private static void ValidatePhysicalDeviceApiVersion(uint version)
        {
            if (version < VulkanUtility.Version(1, 0, 0) ||
                GetMajor(version) != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    "The Vulkan physical-device API version must be Vulkan 1.x.");
            }
        }

        private static uint GetMajor(uint version)
        {
            return (version >> 22) & 0x7Fu;
        }
    }
}
