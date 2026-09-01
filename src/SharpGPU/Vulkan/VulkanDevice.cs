using System;
using System.Linq;
using Vortice.Vulkan;
using SharpGPU.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;

namespace SharpGPU
{
    internal unsafe class VulkanDevice : RHIDevice
    {
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;
        public VulkanInstance VulkanInstance
        {
            get
            {
                return m_VulkanInstance;
            }
        }
        public VkDevice NativeDevice
        {
            get
            {
                return m_NativeDevice;
            }
        }
        public VkPhysicalDevice NativePhysicalDevice
        {
            get
            {
                return m_PhysicalDevice;
            }
        }
        public VkPhysicalDeviceMemoryProperties MemoryProperties
        {
            get
            {
                return m_MemoryProperties;
            }
        }

        internal int GraphicsQueueFamilyIndex => m_GraphicsQueueFamilyIndex;
        internal int ComputeQueueFamilyIndex => m_ComputeQueueFamilyIndex;
        internal int TransferQueueFamilyIndex => m_TransferQueueFamilyIndex;
        internal bool UseSynchronization2 => m_UseSynchronization2;
        internal bool UseSynchronization2KhrCommand => m_UseSynchronization2KhrCommand;
        internal VulkanDescriptorFeatures DescriptorFeatures => m_DescriptorFeatures;
        internal VulkanDescriptorLimits DescriptorLimits => m_DescriptorLimits;
        internal VulkanDescriptorPoolAllocator DescriptorPoolAllocator => m_DescriptorPoolAllocator;
        internal uint EffectiveApiVersion => m_EffectiveApiVersion;
        internal bool SparseResidencyImage2DSupported => m_SparseResidencyImage2DSupported;
        internal bool SupportsDynamicRendering => m_DynamicRenderingSupported;
        internal bool SupportsDynamicRenderingLocalRead =>
            m_DynamicRenderingLocalReadSupported;
        internal bool SupportsDynamicRenderingLocalReadDepthStencil =>
            m_DynamicRenderingLocalReadDepthStencilAttachments;
        internal bool SupportsDynamicRenderingLocalReadMultisampled =>
            m_DynamicRenderingLocalReadMultisampledAttachments;
        internal bool SupportsRasterizationOrderAttachmentAccess =>
            m_RasterizationOrderAttachmentAccessSupported;
        internal EVulkanFeatureProvenance DynamicRenderingLocalReadProvenance => m_DynamicRenderingLocalReadProvenance;
        internal EVulkanFeatureProvenance RenderPass2Provenance => m_RenderPass2Provenance;
        internal VulkanRasterCapabilities RasterCapabilities =>
            new VulkanRasterCapabilities(
                m_DynamicRenderingSupported,
                m_DynamicRenderingLocalReadSupported,
                m_DynamicRenderingLocalReadDepthStencilAttachments,
                m_DynamicRenderingLocalReadMultisampledAttachments,
                m_RenderPass2Supported,
                m_RasterizationOrderAttachmentAccessSupported);
        internal bool SupportsRenderPass2 => m_RenderPass2Supported;
        internal bool SupportsSeparateDepthStencilLayouts =>
            m_SeparateDepthStencilLayoutsSupported;
        internal VkResolveModeFlags SupportedDepthResolveModes =>
            m_SupportedDepthResolveModes;
        internal VkResolveModeFlags SupportedStencilResolveModes =>
            m_SupportedStencilResolveModes;
        internal bool IndependentResolveNone =>
            m_IndependentResolveNone;
        internal bool IndependentResolve =>
            m_IndependentResolve;        internal bool UseDynamicRenderingKhrCommands => m_UseDynamicRenderingKhrCommands;
        internal bool UseDynamicRenderingLocalReadKhrCommands =>
            m_DynamicRenderingLocalReadProvenance ==
                EVulkanFeatureProvenance.KhrExtension;
        internal bool UseRenderPass2KhrCommands => m_UseRenderPass2KhrCommands;
        internal bool SupportsSwapchainMaintenance => m_SwapchainMaintenanceSupported;
        internal bool SparseResidencyImage3DSupported => m_SparseResidencyImage3DSupported;
        internal bool SupportsSparseQueueFamily(uint queueFamilyIndex) =>
            m_SparseBindingSupported &&
            m_SparseQueueFamilies.Contains(queueFamilyIndex);
        internal RHIAdapterIdentity AdapterIdentity => m_AdapterIdentity;
        internal bool ExternalFence64Supported => m_ExternalFence64Supported;
        internal bool ExternalMemoryWin32Supported => m_ExternalMemoryWin32Supported;

        private VulkanInstance m_VulkanInstance;
        private VkDevice m_NativeDevice;
        private VkPhysicalDevice m_PhysicalDevice;
        private VkPhysicalDeviceMemoryProperties m_MemoryProperties;
        private VulkanDescriptorFeatures m_DescriptorFeatures;
        private VulkanDescriptorLimits m_DescriptorLimits;
        private VulkanDescriptorPoolAllocator m_DescriptorPoolAllocator;
        private readonly HashSet<uint> m_SparseQueueFamilies = new HashSet<uint>();

        private int m_GraphicsQueueFamilyIndex = -1;
        private int m_ComputeQueueFamilyIndex = -1;
        private int m_TransferQueueFamilyIndex = -1;

        private bool m_RaytracingSupported;
        private bool m_ExternalFence64Supported;
        private bool m_ExternalMemoryWin32Supported;
        private RHIAdapterIdentity m_AdapterIdentity;
        private bool m_RaytracingInlineSupported;
        private bool m_OpacityMicromapExtensionListed;
        private bool m_NvInvocationReorderExtensionListed;
        private bool m_ExtInvocationReorderExtensionListed;
        private bool m_NvMotionBlurExtensionListed;
        private bool m_MeshShadingSupported;
        private bool m_TaskShadingSupported;
        private RHICapabilityLimits m_MeshShaderLimits;
        private bool m_FragmentShadingRateExtensionPresent;
        private bool m_VariableRateShadingPerDrawSupported;
        private bool m_VariableRateShadingPerPrimitiveSupported;
        private bool m_VariableRateShadingAttachmentHardwareSupported;
        private bool m_FragmentShadingRateNonTrivialCombinerOps;
        private bool m_FragmentShadingRateEnumerationComplete;
        private ulong m_SupportedShadingRateMask;
        private uint m_MinFragmentShadingRateAttachmentTexelWidth;
        private uint m_MinFragmentShadingRateAttachmentTexelHeight;
        private uint m_MaxFragmentShadingRateAttachmentTexelWidth;
        private uint m_MaxFragmentShadingRateAttachmentTexelHeight;
        private bool m_UseSynchronization2;
        private bool m_UseSynchronization2KhrCommand;
        private bool m_ShaderAtomicInt64Supported;
        private bool m_DescriptorIndexingUsesExtension;
        private uint m_EffectiveApiVersion;
        private bool m_SparseBindingSupported;
        private bool m_DynamicRenderingSupported;
        private bool m_UseDynamicRenderingKhrCommands;
        private bool m_DynamicRenderingLocalReadSupported;
        private bool
            m_DynamicRenderingLocalReadDepthStencilAttachments;
        private bool
            m_DynamicRenderingLocalReadMultisampledAttachments;
        private bool
            m_DynamicRenderingLocalReadExtensionEnabled;
        private bool m_RasterizationOrderAttachmentAccessSupported;
        private EVulkanFeatureProvenance m_DynamicRenderingLocalReadProvenance;
        private EVulkanFeatureProvenance m_RenderPass2Provenance;
        private bool m_RenderPass2Supported;
        private bool m_UseRenderPass2KhrCommands;
        private bool m_SeparateDepthStencilLayoutsSupported;
        private VkResolveModeFlags m_SupportedDepthResolveModes;
        private VkResolveModeFlags m_SupportedStencilResolveModes;
        private bool m_IndependentResolveNone;
        private bool m_IndependentResolve;        private bool m_SparseResidencyImage2DSupported;
        private bool m_SparseResidencyImage3DSupported;
        private bool m_SparseResidencyMsaaSupported;
        private bool m_SparseResidencyAliasedSupported;
        private bool m_SparseTileGeometrySupported;
        private bool m_SparseMipTailSupported;
        private bool m_CalibratedTimestampsSupported;
        private string m_CalibratedTimestampsUnavailableReason =
            "VK_KHR_calibrated_timestamps is not supported by this physical device.";
        private string m_CalibratedTimestampsExtensionName =
            VulkanCalibratedTimestampNative.KhrExtensionName;
        private PFN_vkGetPhysicalDeviceCalibrateableTimeDomainsKHR? m_GetCalibrateableTimeDomains;
        private PFN_vkGetCalibratedTimestampsKHR? m_GetCalibratedTimestamps;
        private VkTimeDomainKHR m_CpuTimeDomain = VkTimeDomainKHR.Device;
        private ERHITimeDomain m_CpuRhiTimeDomain = ERHITimeDomain.Device;
        private ulong m_GpuTimestampFrequency;
        private ulong m_CalibratedTimestampMaxDeviation;
        private string m_CalibratedTimestampsGetFunctionName =
            "vkGetCalibratedTimestampsKHR";
        private bool m_MemoryBudgetSupported;
        private bool m_SwapchainSupported;
        private bool m_SwapchainMaintenanceSupported;
        private VulkanWaveProbe m_WaveProbe =
            VulkanWaveProbe.Unavailable(
                "Vulkan wave / cooperative-matrix probe has not run.",
                "SharpGPU Vulkan factory surface");
        private RHICooperativeMatrixConfig[] m_CooperativeMatrixConfigs =
            Array.Empty<RHICooperativeMatrixConfig>();

        public VulkanDevice(VulkanInstance instance, VkPhysicalDevice physicalDevice, in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_VulkanInstance = instance;
            m_PhysicalDevice = physicalDevice;

            QueryDeviceProperties();
            CreateDevice(computeQueueCount, transferQueueCount, graphicsQueueCount);
            UpdateDeviceFeatures();
            m_DescriptorPoolAllocator = new VulkanDescriptorPoolAllocator(this);
        }

        private void QueryDeviceProperties()
        {
            VkPhysicalDeviceProperties properties;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &properties);

            m_Name = VulkanUtility.GetString(properties.deviceName);
            m_VendorId.IntValue = properties.vendorID;
            m_DeviceId.IntValue = properties.deviceID;
            m_DriverVersion = String.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Vulkan-0x{0:X8}-API-0x{1:X8}",
                properties.driverVersion,
                properties.apiVersion);

            switch (properties.deviceType)
            {
                case VkPhysicalDeviceType.DiscreteGpu:
                case VkPhysicalDeviceType.IntegratedGpu:
                case VkPhysicalDeviceType.VirtualGpu:
                    m_Type = ERHIDeviceType.Hardware;
                    break;
                default:
                    m_Type = ERHIDeviceType.Software;
                    break;
            }

            VkPhysicalDeviceMemoryProperties memProperties;
            VulkanNative.vkGetPhysicalDeviceMemoryProperties(m_PhysicalDevice, &memProperties);
            m_MemoryProperties = memProperties;

            // Query features
            VkPhysicalDeviceFeatures features;
            VulkanNative.vkGetPhysicalDeviceFeatures(m_PhysicalDevice, &features);

            VkPhysicalDeviceLimits limits = properties.limits;
            m_WaveProbe = VulkanWaveAndCooperativeMatrixNative.QuerySubgroupSizes(
                m_PhysicalDevice);

            m_Limit = new RHIDeviceLimit(
                uniformBufferAlignment: (int)limits.minUniformBufferOffsetAlignment,
                uploadBufferAlignment: 256,
                uploadBufferTextureAlignment: 256,
                uploadBufferTextureRowAlignment: 256,
                maxMSAACount: 8,
                maxBoundTexture: (int)limits.maxDescriptorSetSampledImages,
                minWavefrontSize: m_WaveProbe.MinWavefrontSize,
                maxWavefrontSize: m_WaveProbe.MaxWavefrontSize,
                maxComputeThreads: (int)limits.maxComputeWorkGroupInvocations,
                maxGroupShareMemorySize: (int)limits.maxComputeSharedMemorySize,
                maxVertexInputBindings: (int)limits.maxVertexInputBindings,
                maxColorAttachments: (int)limits.maxColorAttachments,
                maxTexture2DSize: (int)limits.maxImageDimension2D,
                maxTextureCubeSize: (int)limits.maxImageDimensionCube
            );
            m_DescriptorLimits = new VulkanDescriptorLimits(
                limits.maxBoundDescriptorSets,
                limits.maxDescriptorSetSamplers,
                limits.maxDescriptorSetSampledImages,
                limits.maxDescriptorSetStorageImages,
                limits.maxDescriptorSetUniformBuffers,
                limits.maxDescriptorSetStorageBuffers,
                limits.maxPerStageDescriptorSamplers,
                limits.maxPerStageDescriptorSampledImages,
                limits.maxPerStageDescriptorStorageImages,
                limits.maxPerStageDescriptorUniformBuffers,
                limits.maxPerStageDescriptorStorageBuffers,
                limits.maxDescriptorSetInputAttachments,
                limits.maxPerStageDescriptorInputAttachments);

        }

        private void CreateDevice(in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            // Find queue families
            uint queueFamilyCount = 0;
            VulkanNative.vkGetPhysicalDeviceQueueFamilyProperties(m_PhysicalDevice, &queueFamilyCount, null);
            VkQueueFamilyProperties* queueFamilies = stackalloc VkQueueFamilyProperties[(int)queueFamilyCount];
            VulkanNative.vkGetPhysicalDeviceQueueFamilyProperties(m_PhysicalDevice, &queueFamilyCount, queueFamilies);

            for (int i = 0; i < queueFamilyCount; ++i)
            {
                VkQueueFlags flags = queueFamilies[i].queueFlags;

                if ((flags & VkQueueFlags.SparseBinding) != 0)
                {
                    m_SparseQueueFamilies.Add((uint)i);
                }

                if (m_GraphicsQueueFamilyIndex == -1 && (flags & VkQueueFlags.Graphics) != 0)
                {
                    m_GraphicsQueueFamilyIndex = i;
                }
                else if (m_ComputeQueueFamilyIndex == -1 && (flags & VkQueueFlags.Compute) != 0 && (flags & VkQueueFlags.Graphics) == 0)
                {
                    m_ComputeQueueFamilyIndex = i;
                }
                else if (m_TransferQueueFamilyIndex == -1 && (flags & VkQueueFlags.Transfer) != 0 && (flags & VkQueueFlags.Graphics) == 0 && (flags & VkQueueFlags.Compute) == 0)
                {
                    m_TransferQueueFamilyIndex = i;
                }
            }

            // Fallbacks
            if (m_ComputeQueueFamilyIndex == -1)
                m_ComputeQueueFamilyIndex = m_GraphicsQueueFamilyIndex;
            if (m_TransferQueueFamilyIndex == -1)
                m_TransferQueueFamilyIndex = m_GraphicsQueueFamilyIndex;

            // Build unique queue create infos
            HashSet<int> uniqueFamilies = new HashSet<int>();
            Dictionary<int, uint> familyQueueCounts = new Dictionary<int, uint>();

            if (graphicsQueueCount > 0 && m_GraphicsQueueFamilyIndex >= 0)
            {
                uniqueFamilies.Add(m_GraphicsQueueFamilyIndex);
                familyQueueCounts[m_GraphicsQueueFamilyIndex] = (uint)Math.Min(graphicsQueueCount, (int)queueFamilies[m_GraphicsQueueFamilyIndex].queueCount);
            }
            if (computeQueueCount > 0 && m_ComputeQueueFamilyIndex >= 0)
            {
                uniqueFamilies.Add(m_ComputeQueueFamilyIndex);
                if (!familyQueueCounts.ContainsKey(m_ComputeQueueFamilyIndex))
                    familyQueueCounts[m_ComputeQueueFamilyIndex] = 0;
                familyQueueCounts[m_ComputeQueueFamilyIndex] = Math.Max(familyQueueCounts[m_ComputeQueueFamilyIndex], (uint)Math.Min(computeQueueCount, (int)queueFamilies[m_ComputeQueueFamilyIndex].queueCount));
            }
            if (transferQueueCount > 0 && m_TransferQueueFamilyIndex >= 0)
            {
                uniqueFamilies.Add(m_TransferQueueFamilyIndex);
                if (!familyQueueCounts.ContainsKey(m_TransferQueueFamilyIndex))
                    familyQueueCounts[m_TransferQueueFamilyIndex] = 0;
                familyQueueCounts[m_TransferQueueFamilyIndex] = Math.Max(familyQueueCounts[m_TransferQueueFamilyIndex], (uint)Math.Min(transferQueueCount, (int)queueFamilies[m_TransferQueueFamilyIndex].queueCount));
            }

            int familyCount = uniqueFamilies.Count;
            if (familyCount == 0)
            {
                // Ensure at least one queue family
                uniqueFamilies.Add(0);
                familyQueueCounts[0] = 1;
                familyCount = 1;
                m_GraphicsQueueFamilyIndex = 0;
                m_ComputeQueueFamilyIndex = 0;
                m_TransferQueueFamilyIndex = 0;
            }

            int actualGraphicsQueueCount = 0;
            int actualComputeQueueCount = 0;
            int actualTransferQueueCount = 0;
            if (m_GraphicsQueueFamilyIndex >= 0 && familyQueueCounts.TryGetValue(m_GraphicsQueueFamilyIndex, out uint graphicsCreatedCount))
            {
                actualGraphicsQueueCount = (int)Math.Min((uint)Math.Max(graphicsQueueCount, 0), graphicsCreatedCount);
            }
            if (m_ComputeQueueFamilyIndex >= 0 && familyQueueCounts.TryGetValue(m_ComputeQueueFamilyIndex, out uint computeCreatedCount))
            {
                actualComputeQueueCount = (int)Math.Min((uint)Math.Max(computeQueueCount, 0), computeCreatedCount);
            }
            if (m_TransferQueueFamilyIndex >= 0 && familyQueueCounts.TryGetValue(m_TransferQueueFamilyIndex, out uint transferCreatedCount))
            {
                actualTransferQueueCount = (int)Math.Min((uint)Math.Max(transferQueueCount, 0), transferCreatedCount);
            }

            VkDeviceQueueCreateInfo* queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyCount];
            int totalRequestedQueues = 0;
            foreach (int familyIndex in uniqueFamilies)
            {
                totalRequestedQueues += (int)familyQueueCounts[familyIndex];
            }
            float* queuePriorities = stackalloc float[Math.Max(totalRequestedQueues, 1)];
            int idx = 0;
            int priorityOffset = 0;
            foreach (int familyIndex in uniqueFamilies)
            {
                uint count = familyQueueCounts[familyIndex];
                float* priorities = queuePriorities + priorityOffset;
                for (int p = 0; p < count; ++p)
                    priorities[p] = 1.0f;

                queueCreateInfos[idx] = new VkDeviceQueueCreateInfo()
                {
                    sType = VkStructureType.DeviceQueueCreateInfo,
                    queueFamilyIndex = (uint)familyIndex,
                    queueCount = count,
                    pQueuePriorities = priorities,
                };
                idx++;
                priorityOffset += (int)count;
            }

            // Enumerate available device extensions
            List<string> deviceExtensions = new List<string>();
            uint extensionCount = 0;
            VulkanUtility.CheckErrors(
                VulkanNative.vkEnumerateDeviceExtensionProperties(
                    m_PhysicalDevice,
                    null,
                    &extensionCount,
                    null));
            VkExtensionProperties* availableExtensions = stackalloc VkExtensionProperties[(int)extensionCount];
            VulkanUtility.CheckErrors(
                VulkanNative.vkEnumerateDeviceExtensionProperties(
                    m_PhysicalDevice,
                    null,
                    &extensionCount,
                    availableExtensions));

            HashSet<string> availableExtNames = new HashSet<string>();
            for (int i = 0; i < extensionCount; ++i)
            {
                availableExtNames.Add(VulkanUtility.GetString(availableExtensions[i].extensionName));
            }

            bool hasSwapchainExtension =
                availableExtNames.Contains("VK_KHR_swapchain");
            if (VulkanInstance.RequiresSwapchainDeviceExtension(
                    m_VulkanInstance.SurfaceKind))
            {
                if (!hasSwapchainExtension)
                {
                    throw new NotSupportedException(
                        "The selected Vulkan device does not expose "
                        + "VK_KHR_swapchain required by the requested "
                        + $"surface kind {m_VulkanInstance.SurfaceKind}.");
                }

                deviceExtensions.Add("VK_KHR_swapchain");
            }

            bool hasDescriptorIndexingExtension = availableExtNames.Contains("VK_EXT_descriptor_indexing");
            bool hasDynamicRenderingExtension = availableExtNames.Contains("VK_KHR_dynamic_rendering");
            bool hasAccelStruct = availableExtNames.Contains("VK_KHR_acceleration_structure");
            bool hasRTPipeline = availableExtNames.Contains("VK_KHR_ray_tracing_pipeline");
            bool hasDeferredOps = availableExtNames.Contains("VK_KHR_deferred_host_operations");
            bool hasRTQuery = availableExtNames.Contains("VK_KHR_ray_query");
            bool hasOpacityMicromap = availableExtNames.Contains("VK_EXT_opacity_micromap");
            bool hasNvInvocationReorder = availableExtNames.Contains(
                "VK_NV_ray_tracing_invocation_reorder");
            bool hasExtInvocationReorder = availableExtNames.Contains(
                "VK_EXT_ray_tracing_invocation_reorder");
            bool hasNvMotionBlur = availableExtNames.Contains("VK_NV_ray_tracing_motion_blur");
            bool hasMeshShader = availableExtNames.Contains("VK_EXT_mesh_shader");
            bool hasFragmentShadingRate = availableExtNames.Contains("VK_KHR_fragment_shading_rate");
            bool hasCalibratedTimestampsKhr = availableExtNames.Contains(
                VulkanCalibratedTimestampNative.KhrExtensionName);
            bool hasCalibratedTimestampsExt = availableExtNames.Contains(
                VulkanCalibratedTimestampNative.ExtExtensionName);
            bool hasCalibratedTimestamps =
                hasCalibratedTimestampsKhr || hasCalibratedTimestampsExt;
            bool hasSynchronization2Extension = availableExtNames.Contains("VK_KHR_synchronization2");
            bool hasCreateRenderPass2Extension =
                availableExtNames.Contains("VK_KHR_create_renderpass2");
            bool hasDepthStencilResolveExtension =
                availableExtNames.Contains(
                    "VK_KHR_depth_stencil_resolve");
            bool hasSeparateDepthStencilLayoutsExtension =
                availableExtNames.Contains(
                    "VK_KHR_separate_depth_stencil_layouts");
            bool hasDynamicRenderingLocalReadExtension =
                availableExtNames.Contains("VK_KHR_dynamic_rendering_local_read");
            bool hasRasterizationOrderAttachmentAccessExtension =
                availableExtNames.Contains(
                    "VK_EXT_rasterization_order_attachment_access");
            bool hasMemoryBudgetExtension =
                availableExtNames.Contains(VulkanMemoryBudgetUtility.ExtensionName);
            bool hasSubgroupSizeControlExtension = availableExtNames.Contains(
                VulkanWaveAndCooperativeMatrixNative.SubgroupSizeControlExtensionName);
            bool hasCooperativeMatrixExtension = availableExtNames.Contains(
                VulkanWaveAndCooperativeMatrixNative.CooperativeMatrixExtensionName);
            bool hasExternalSemaphore = availableExtNames.Contains("VK_KHR_external_semaphore");
            bool hasExternalSemaphoreWin32 = availableExtNames.Contains("VK_KHR_external_semaphore_win32");
            bool enableExternalSemaphoreWin32 =
                OperatingSystem.IsWindows() &&
                hasExternalSemaphore &&
                hasExternalSemaphoreWin32;
            bool enableExternalMemoryWin32 = false;
            string? swapchainMaintenanceExtension =
                availableExtNames.Contains("VK_KHR_swapchain_maintenance1")
                    ? "VK_KHR_swapchain_maintenance1"
                    : availableExtNames.Contains("VK_EXT_swapchain_maintenance1")
                        ? "VK_EXT_swapchain_maintenance1"
                        : null;
            VkPhysicalDeviceFeatures supportedCoreFeatures = default;
            VulkanNative.vkGetPhysicalDeviceFeatures(m_PhysicalDevice, &supportedCoreFeatures);
            VkPhysicalDeviceProperties physicalDeviceProperties = default;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &physicalDeviceProperties);
            VulkanFeatureChainPlan featureChainPlan = VulkanFeatureChainPlan.Create(
                m_VulkanInstance.ApiVersion,
                physicalDeviceProperties.apiVersion.Value,
                hasDescriptorIndexingExtension,
                hasDynamicRenderingExtension,
                hasCreateRenderPass2Extension,
                hasSynchronization2Extension,
                hasDynamicRenderingLocalReadExtension);
            m_EffectiveApiVersion = featureChainPlan.EffectiveApiVersion;
            VkPhysicalDeviceDepthStencilResolveProperties
                depthStencilResolveProperties = new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceDepthStencilResolveProperties,
                };
            VkPhysicalDeviceVulkan14Properties vulkan14Properties =
                new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceVulkan14Properties,
                };
            void* rasterPropertiesChain = null;
            bool hasDepthStencilResolveMechanism =
                featureChainPlan.UseVulkan12Features ||
                hasDepthStencilResolveExtension;
            // Queried fields include maxFragmentSize plus
            // min/maxFragmentShadingRateAttachmentTexelSize. Attachment
            // limits are mapped below; they stay unpublished while Attachment
            // is Unavailable because FromProbe(false, ...) discards limits.
            VkPhysicalDeviceFragmentShadingRatePropertiesKHR
                fragmentShadingRateProperties = new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceFragmentShadingRatePropertiesKHR,
                };
            if (hasFragmentShadingRate)
            {
                fragmentShadingRateProperties.pNext =
                    rasterPropertiesChain;
                rasterPropertiesChain =
                    &fragmentShadingRateProperties;
            }
            if (hasDepthStencilResolveMechanism)
            {
                depthStencilResolveProperties.pNext =
                    rasterPropertiesChain;
                rasterPropertiesChain =
                    &depthStencilResolveProperties;
            }            if (featureChainPlan.UseVulkan14Features)
            {
                vulkan14Properties.pNext =
                    rasterPropertiesChain;
                rasterPropertiesChain = &vulkan14Properties;
            }
            VkPhysicalDeviceProperties2 rasterProperties = new()
            {
                sType =
                    VkStructureType.PhysicalDeviceProperties2,
                pNext = rasterPropertiesChain,
            };
            VulkanNative.vkGetPhysicalDeviceProperties2(
                m_PhysicalDevice,
                &rasterProperties);
            bool localReadUsesKhrExtension =
                featureChainPlan
                    .EnableDynamicRenderingLocalReadExtension;
            bool localReadDepthStencilAttachments =
                localReadUsesKhrExtension ||
                !featureChainPlan.UseVulkan14Features ||
                vulkan14Properties
                    .dynamicRenderingLocalReadDepthStencilAttachments;
            bool localReadMultisampledAttachments =
                localReadUsesKhrExtension ||
                !featureChainPlan.UseVulkan14Features ||
                vulkan14Properties
                    .dynamicRenderingLocalReadMultisampledAttachments;
            bool hasCreatedSparseQueue =
                familyQueueCounts.Keys.Any(
                    family => m_SparseQueueFamilies.Contains((uint)family));
            bool sparseBindingSupported =
                supportedCoreFeatures.sparseBinding && hasCreatedSparseQueue;

            VkPhysicalDeviceRayQueryFeaturesKHR rtQueryFeaturesQuery = new VkPhysicalDeviceRayQueryFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceRayQueryFeaturesKHR,
            };
            VkPhysicalDeviceRayTracingPipelineFeaturesKHR rtPipelineFeaturesQuery = new VkPhysicalDeviceRayTracingPipelineFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceRayTracingPipelineFeaturesKHR,
            };
            VkPhysicalDeviceAccelerationStructureFeaturesKHR accelFeaturesQuery = new VkPhysicalDeviceAccelerationStructureFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceAccelerationStructureFeaturesKHR,
            };
            VkPhysicalDeviceMeshShaderFeaturesEXT meshFeaturesQuery = new VkPhysicalDeviceMeshShaderFeaturesEXT()
            {
                sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT,
            };
            VkPhysicalDeviceFragmentShadingRateFeaturesKHR vrsFeaturesQuery = new VkPhysicalDeviceFragmentShadingRateFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceFragmentShadingRateFeaturesKHR,
            };
            VkPhysicalDeviceVulkan14Features vulkan14FeaturesQuery =
                new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceVulkan14Features,
                };
            VkPhysicalDeviceVulkan13Features vulkan13FeaturesQuery = new VkPhysicalDeviceVulkan13Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan13Features,
            };
            VkPhysicalDeviceVulkan12Features vulkan12FeaturesQuery = new VkPhysicalDeviceVulkan12Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan12Features,
            };
            VkPhysicalDeviceDescriptorIndexingFeatures descriptorIndexingFeaturesQuery = new VkPhysicalDeviceDescriptorIndexingFeatures()
            {
                sType = VkStructureType.PhysicalDeviceDescriptorIndexingFeatures,
            };
            VkPhysicalDeviceDynamicRenderingFeatures dynamicRenderingFeaturesQuery = new VkPhysicalDeviceDynamicRenderingFeatures()
            {
                sType = VkStructureType.PhysicalDeviceDynamicRenderingFeatures,
            };
            VkPhysicalDeviceSynchronization2Features synchronization2FeaturesQuery = new VkPhysicalDeviceSynchronization2Features()
            {
                sType = VkStructureType.PhysicalDeviceSynchronization2Features,
            };
            VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures
                separateDepthStencilLayoutsFeaturesQuery = new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceSeparateDepthStencilLayoutsFeatures,
                };
            VkPhysicalDeviceDynamicRenderingLocalReadFeatures dynamicRenderingLocalReadFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceDynamicRenderingLocalReadFeatures,
            };
            VkPhysicalDeviceRasterizationOrderAttachmentAccessFeaturesEXT
                rasterizationOrderAttachmentAccessFeaturesQuery = new()
                {
                    sType = VkStructureType
                        .PhysicalDeviceRasterizationOrderAttachmentAccessFeaturesEXT,
                };
            VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR swapchainMaintenanceFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceSwapchainMaintenance1FeaturesKHR,
            };
            VkPhysicalDeviceSubgroupSizeControlFeatures subgroupSizeControlFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceSubgroupSizeControlFeatures,
            };
            VkPhysicalDeviceCooperativeMatrixFeaturesKHR cooperativeMatrixFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceCooperativeMatrixFeaturesKHR,
            };

            void* featureQueryChain = null;
            if (hasRTQuery)
            {
                rtQueryFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &rtQueryFeaturesQuery;
            }
            if (hasRTPipeline)
            {
                rtPipelineFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &rtPipelineFeaturesQuery;
            }
            if (hasAccelStruct)
            {
                accelFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &accelFeaturesQuery;
            }
            if (hasMeshShader)
            {
                meshFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &meshFeaturesQuery;
            }
            if (hasFragmentShadingRate)
            {
                vrsFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &vrsFeaturesQuery;
            }
            if (featureChainPlan.UseVulkan14Features)
            {
                vulkan14FeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &vulkan14FeaturesQuery;
            }
            if (featureChainPlan.UseVulkan13Features)
            {
                vulkan13FeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &vulkan13FeaturesQuery;
            }
            if (featureChainPlan.UseVulkan12Features)
            {
                vulkan12FeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &vulkan12FeaturesQuery;
            }
            if (!featureChainPlan.UseVulkan12Features &&
                hasSeparateDepthStencilLayoutsExtension)
            {
                separateDepthStencilLayoutsFeaturesQuery.pNext =
                    featureQueryChain;
                featureQueryChain =
                    &separateDepthStencilLayoutsFeaturesQuery;
            }
            if (featureChainPlan.DescriptorIndexingPath == EVulkanDescriptorIndexingPath.ExtDescriptorIndexing)
            {
                descriptorIndexingFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &descriptorIndexingFeaturesQuery;
            }
            if (featureChainPlan.UseDynamicRenderingExtension)
            {
                dynamicRenderingFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &dynamicRenderingFeaturesQuery;
            }
            if (featureChainPlan.UseSynchronization2Extension)
            {
                synchronization2FeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &synchronization2FeaturesQuery;
            }
            if (featureChainPlan
                .UseDynamicRenderingLocalReadExtensionFeatureStruct)
            {
                dynamicRenderingLocalReadFeaturesQuery.pNext =
                    featureQueryChain;
                featureQueryChain =
                    &dynamicRenderingLocalReadFeaturesQuery;
            }
            if (hasRasterizationOrderAttachmentAccessExtension)
            {
                rasterizationOrderAttachmentAccessFeaturesQuery.pNext =
                    featureQueryChain;
                featureQueryChain =
                    &rasterizationOrderAttachmentAccessFeaturesQuery;
            }
            if (swapchainMaintenanceExtension != null)
            {
                swapchainMaintenanceFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &swapchainMaintenanceFeaturesQuery;
            }
            if (!featureChainPlan.UseVulkan13Features &&
                hasSubgroupSizeControlExtension)
            {
                subgroupSizeControlFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &subgroupSizeControlFeaturesQuery;
            }
            if (hasCooperativeMatrixExtension)
            {
                cooperativeMatrixFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &cooperativeMatrixFeaturesQuery;
            }
            VkPhysicalDeviceFeatures2 features2Query = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.PhysicalDeviceFeatures2,
                pNext = featureQueryChain,
            };
            VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &features2Query);

            bool descriptorIndexingCore =
                featureChainPlan.DescriptorIndexingPath == EVulkanDescriptorIndexingPath.Vulkan12Core;
            bool descriptorIndexingExtension =
                featureChainPlan.DescriptorIndexingPath == EVulkanDescriptorIndexingPath.ExtDescriptorIndexing;
            bool sampledImageArrayNonUniformIndexing = descriptorIndexingCore
                ? vulkan12FeaturesQuery.shaderSampledImageArrayNonUniformIndexing
                : descriptorIndexingExtension
                    && descriptorIndexingFeaturesQuery.shaderSampledImageArrayNonUniformIndexing;
            bool storageImageArrayNonUniformIndexing = descriptorIndexingCore
                ? vulkan12FeaturesQuery.shaderStorageImageArrayNonUniformIndexing
                : descriptorIndexingExtension
                    && descriptorIndexingFeaturesQuery.shaderStorageImageArrayNonUniformIndexing;
            bool uniformBufferArrayNonUniformIndexing = descriptorIndexingCore
                ? vulkan12FeaturesQuery.shaderUniformBufferArrayNonUniformIndexing
                : descriptorIndexingExtension
                    && descriptorIndexingFeaturesQuery.shaderUniformBufferArrayNonUniformIndexing;
            bool storageBufferArrayNonUniformIndexing = descriptorIndexingCore
                ? vulkan12FeaturesQuery.shaderStorageBufferArrayNonUniformIndexing
                : descriptorIndexingExtension
                    && descriptorIndexingFeaturesQuery.shaderStorageBufferArrayNonUniformIndexing;
            bool anyRequiredDescriptorIndexingFeature =
                sampledImageArrayNonUniformIndexing
                || storageImageArrayNonUniformIndexing
                || uniformBufferArrayNonUniformIndexing
                || storageBufferArrayNonUniformIndexing;
            bool descriptorIndexingFeatureSupported = descriptorIndexingCore
                ? vulkan12FeaturesQuery.descriptorIndexing
                    && anyRequiredDescriptorIndexingFeature
                : descriptorIndexingExtension
                    && anyRequiredDescriptorIndexingFeature;
            bool separateDepthStencilLayoutsSupported =
                featureChainPlan.UseVulkan12Features
                    ? vulkan12FeaturesQuery
                        .separateDepthStencilLayouts
                    : hasSeparateDepthStencilLayoutsExtension &&
                      separateDepthStencilLayoutsFeaturesQuery
                        .separateDepthStencilLayouts;
            bool dynamicRenderingFeatureSupported = featureChainPlan.UseVulkan13Features
                ? vulkan13FeaturesQuery.dynamicRendering
                : featureChainPlan.UseDynamicRenderingExtension
                    && dynamicRenderingFeaturesQuery.dynamicRendering;
            bool synchronization2FeatureSupported = featureChainPlan.UseVulkan13Features
                ? vulkan13FeaturesQuery.synchronization2
                : featureChainPlan.UseSynchronization2Extension
                    && synchronization2FeaturesQuery.synchronization2;
            bool dynamicRenderingLocalReadFeatureSupported =
                featureChainPlan.DynamicRenderingLocalReadQueryProvenance
                    switch
                    {
                        EVulkanFeatureProvenance.Vulkan14Core =>
                            vulkan14FeaturesQuery
                                .dynamicRenderingLocalRead,
                        EVulkanFeatureProvenance.KhrExtension =>
                            dynamicRenderingLocalReadFeaturesQuery
                                .dynamicRenderingLocalRead,
                        _ => false,
                    };
            bool rasterizationOrderAttachmentAccessFeatureSupported =
                hasRasterizationOrderAttachmentAccessExtension &&
                rasterizationOrderAttachmentAccessFeaturesQuery
                    .rasterizationOrderColorAttachmentAccess;
            bool swapchainMaintenanceFeatureSupported =
                hasSwapchainExtension &&
                swapchainMaintenanceExtension != null &&
                swapchainMaintenanceFeaturesQuery.swapchainMaintenance1;
            bool accelFeatureSupported = hasAccelStruct && accelFeaturesQuery.accelerationStructure;
            bool rtPipelineFeatureSupported = hasRTPipeline && rtPipelineFeaturesQuery.rayTracingPipeline;
            bool rtQueryFeatureSupported = hasRTQuery && rtQueryFeaturesQuery.rayQuery;
            bool meshShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.meshShader;
            bool taskShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.taskShader;
            bool fragmentShadingRatePerDrawSupported =
                hasFragmentShadingRate && vrsFeaturesQuery.pipelineFragmentShadingRate;
            bool fragmentShadingRatePerPrimitiveSupported =
                hasFragmentShadingRate && vrsFeaturesQuery.primitiveFragmentShadingRate;
            bool fragmentShadingRateAttachmentSupported =
                hasFragmentShadingRate && vrsFeaturesQuery.attachmentFragmentShadingRate;
            bool fragmentShadingRateFeatureSupported =
                fragmentShadingRatePerDrawSupported ||
                fragmentShadingRatePerPrimitiveSupported ||
                fragmentShadingRateAttachmentSupported;
            bool fragmentShadingRateNonTrivialCombinerOps =
                hasFragmentShadingRate &&
                fragmentShadingRateProperties.fragmentShadingRateNonTrivialCombinerOps;
            bool useDescriptorIndexingExtension =
                descriptorIndexingExtension && descriptorIndexingFeatureSupported;
            bool useDynamicRenderingExtension =
                featureChainPlan.UseDynamicRenderingExtension && dynamicRenderingFeatureSupported;
            bool useSynchronization2 = synchronization2FeatureSupported;
            bool useSynchronization2Extension =
                featureChainPlan.UseSynchronization2Extension && useSynchronization2;

            bool rtSupported = featureChainPlan.UseVulkan12Features
                && hasAccelStruct
                && hasRTPipeline
                && hasDeferredOps
                && vulkan12FeaturesQuery.bufferDeviceAddress
                && accelFeatureSupported
                && rtPipelineFeatureSupported;
            bool rtInlineSupported = rtSupported && rtQueryFeatureSupported;
            bool meshSupported = hasMeshShader && meshShaderFeatureSupported;
            bool vrsSupported = hasFragmentShadingRate && fragmentShadingRateFeatureSupported;
            bool subgroupSizeControlUsesVulkan13Core =
                featureChainPlan.UseVulkan13Features &&
                vulkan13FeaturesQuery.subgroupSizeControl;
            bool subgroupSizeControlUsesExtension =
                !featureChainPlan.UseVulkan13Features &&
                hasSubgroupSizeControlExtension &&
                subgroupSizeControlFeaturesQuery.subgroupSizeControl;
            bool subgroupSizeControlSupported =
                subgroupSizeControlUsesVulkan13Core ||
                subgroupSizeControlUsesExtension;
            bool cooperativeMatrixSupported =
                hasCooperativeMatrixExtension &&
                cooperativeMatrixFeaturesQuery.cooperativeMatrix;

            if (useDescriptorIndexingExtension)
            {
                deviceExtensions.Add("VK_EXT_descriptor_indexing");
            }
            if (useDynamicRenderingExtension)
            {
                deviceExtensions.Add("VK_KHR_dynamic_rendering");
            }
            if (useSynchronization2Extension)
            {
                deviceExtensions.Add("VK_KHR_synchronization2");
            }
            if (!featureChainPlan.UseVulkan12Features &&
                hasDepthStencilResolveExtension)
            {
                deviceExtensions.Add(
                    "VK_KHR_depth_stencil_resolve");
            }
            if (!featureChainPlan.UseVulkan12Features &&
                separateDepthStencilLayoutsSupported)
            {
                deviceExtensions.Add(
                    "VK_KHR_separate_depth_stencil_layouts");
            }
            if (featureChainPlan.UseCreateRenderPass2Extension)
            {
                deviceExtensions.Add("VK_KHR_create_renderpass2");
            }
            if (dynamicRenderingLocalReadFeatureSupported &&
                featureChainPlan
                    .EnableDynamicRenderingLocalReadExtension)
            {
                deviceExtensions.Add(
                    "VK_KHR_dynamic_rendering_local_read");
            }
            if (rasterizationOrderAttachmentAccessFeatureSupported)
            {
                deviceExtensions.Add(
                    "VK_EXT_rasterization_order_attachment_access");
            }

            if (rtSupported)
            {
                deviceExtensions.Add("VK_KHR_acceleration_structure");
                deviceExtensions.Add("VK_KHR_ray_tracing_pipeline");
                deviceExtensions.Add("VK_KHR_deferred_host_operations");
                if (rtInlineSupported)
                {
                    deviceExtensions.Add("VK_KHR_ray_query");
                }
            }

            if (meshSupported)
            {
                deviceExtensions.Add("VK_EXT_mesh_shader");
            }

            if (vrsSupported)
            {
                deviceExtensions.Add("VK_KHR_fragment_shading_rate");
            }

            if (hasCalibratedTimestamps)
            {
                deviceExtensions.Add(
                    hasCalibratedTimestampsKhr
                        ? VulkanCalibratedTimestampNative.KhrExtensionName
                        : VulkanCalibratedTimestampNative.ExtExtensionName);
            }

            if (hasMemoryBudgetExtension)
            {
                deviceExtensions.Add(VulkanMemoryBudgetUtility.ExtensionName);
            }
            if (swapchainMaintenanceFeatureSupported)
            {
                deviceExtensions.Add(swapchainMaintenanceExtension!);
            }
            if (subgroupSizeControlUsesExtension)
            {
                deviceExtensions.Add(
                    VulkanWaveAndCooperativeMatrixNative.SubgroupSizeControlExtensionName);
            }
            if (cooperativeMatrixSupported)
            {
                deviceExtensions.Add(
                    VulkanWaveAndCooperativeMatrixNative.CooperativeMatrixExtensionName);
            }
            if (enableExternalSemaphoreWin32)
            {
                deviceExtensions.Add("VK_KHR_external_semaphore");
                deviceExtensions.Add("VK_KHR_external_semaphore_win32");
            }
            if (enableExternalMemoryWin32)
            {
                deviceExtensions.Add("VK_KHR_external_memory");
                deviceExtensions.Add("VK_KHR_external_memory_win32");
            }

            m_ExternalFence64Supported =
                enableExternalSemaphoreWin32 &&
                vulkan12FeaturesQuery.timelineSemaphore;
            m_ExternalMemoryWin32Supported = enableExternalMemoryWin32;
            m_AdapterIdentity = QueryAdapterIdentity(m_PhysicalDevice);

            IntPtr* extensionPtrs = stackalloc IntPtr[deviceExtensions.Count];
            for (int i = 0; i < deviceExtensions.Count; ++i)
            {
                extensionPtrs[i] = Marshal.StringToHGlobalAnsi(deviceExtensions[i]);
            }

            VkPhysicalDeviceFeatures enabledFeatures = default;
            enabledFeatures.samplerAnisotropy = supportedCoreFeatures.samplerAnisotropy;
            enabledFeatures.fillModeNonSolid = supportedCoreFeatures.fillModeNonSolid;
            enabledFeatures.multiDrawIndirect = supportedCoreFeatures.multiDrawIndirect;
            enabledFeatures.drawIndirectFirstInstance = supportedCoreFeatures.drawIndirectFirstInstance;
            enabledFeatures.fragmentStoresAndAtomics = supportedCoreFeatures.fragmentStoresAndAtomics;
            enabledFeatures.shaderStorageImageExtendedFormats = supportedCoreFeatures.shaderStorageImageExtendedFormats;
            enabledFeatures.pipelineStatisticsQuery = supportedCoreFeatures.pipelineStatisticsQuery;
            enabledFeatures.occlusionQueryPrecise = supportedCoreFeatures.occlusionQueryPrecise;
            enabledFeatures.sparseBinding = sparseBindingSupported;
            enabledFeatures.sparseResidencyImage2D =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidencyImage2D;
            enabledFeatures.sparseResidencyImage3D =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidencyImage3D;
            enabledFeatures.sparseResidency2Samples =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidency2Samples;
            enabledFeatures.sparseResidency4Samples =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidency4Samples;
            enabledFeatures.sparseResidency8Samples =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidency8Samples;
            enabledFeatures.sparseResidency16Samples =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidency16Samples;
            enabledFeatures.sparseResidencyAliased =
                sparseBindingSupported &&
                supportedCoreFeatures.sparseResidencyAliased;

            VkPhysicalDeviceVulkan14Features vulkan14Features =
                new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceVulkan14Features,
                    dynamicRenderingLocalRead =
                        dynamicRenderingLocalReadFeatureSupported,
                };
            VkPhysicalDeviceVulkan13Features vulkan13Features = new VkPhysicalDeviceVulkan13Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan13Features,
                dynamicRendering = dynamicRenderingFeatureSupported,
                synchronization2 = featureChainPlan.UseVulkan13Features && useSynchronization2,
                subgroupSizeControl = subgroupSizeControlUsesVulkan13Core,
                computeFullSubgroups = subgroupSizeControlUsesVulkan13Core &&
                    vulkan13FeaturesQuery.computeFullSubgroups,
            };
            VkPhysicalDeviceVulkan12Features vulkan12Features = new VkPhysicalDeviceVulkan12Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan12Features,
                descriptorIndexing = descriptorIndexingFeatureSupported,
                shaderUniformBufferArrayNonUniformIndexing = uniformBufferArrayNonUniformIndexing,
                shaderSampledImageArrayNonUniformIndexing = sampledImageArrayNonUniformIndexing,
                shaderStorageBufferArrayNonUniformIndexing = storageBufferArrayNonUniformIndexing,
                shaderStorageImageArrayNonUniformIndexing = storageImageArrayNonUniformIndexing,
                timelineSemaphore = vulkan12FeaturesQuery.timelineSemaphore,
                bufferDeviceAddress =
                    vulkan12FeaturesQuery.bufferDeviceAddress,
                separateDepthStencilLayouts =
                    separateDepthStencilLayoutsSupported,
            };
            VkPhysicalDeviceDescriptorIndexingFeatures descriptorIndexingFeatures = new VkPhysicalDeviceDescriptorIndexingFeatures()
            {
                sType = VkStructureType.PhysicalDeviceDescriptorIndexingFeatures,
                shaderUniformBufferArrayNonUniformIndexing = uniformBufferArrayNonUniformIndexing,
                shaderSampledImageArrayNonUniformIndexing = sampledImageArrayNonUniformIndexing,
                shaderStorageBufferArrayNonUniformIndexing = storageBufferArrayNonUniformIndexing,
                shaderStorageImageArrayNonUniformIndexing = storageImageArrayNonUniformIndexing,
            };
            VkPhysicalDeviceDynamicRenderingFeatures dynamicRenderingFeatures = new VkPhysicalDeviceDynamicRenderingFeatures()
            {
                sType = VkStructureType.PhysicalDeviceDynamicRenderingFeatures,
                dynamicRendering = dynamicRenderingFeatureSupported,
            };
            VkPhysicalDeviceSynchronization2Features synchronization2Features = new VkPhysicalDeviceSynchronization2Features()
            {
                sType = VkStructureType.PhysicalDeviceSynchronization2Features,
                synchronization2 = useSynchronization2,
            };
            VkPhysicalDeviceSeparateDepthStencilLayoutsFeatures
                separateDepthStencilLayoutsFeatures = new()
                {
                    sType =
                        VkStructureType
                            .PhysicalDeviceSeparateDepthStencilLayoutsFeatures,
                    separateDepthStencilLayouts =
                        separateDepthStencilLayoutsSupported,
                };
            VkPhysicalDeviceDynamicRenderingLocalReadFeatures dynamicRenderingLocalReadFeatures = new()
            {
                sType = VkStructureType.PhysicalDeviceDynamicRenderingLocalReadFeatures,
                dynamicRenderingLocalRead = dynamicRenderingLocalReadFeatureSupported,
            };
            VkPhysicalDeviceRasterizationOrderAttachmentAccessFeaturesEXT
                rasterizationOrderAttachmentAccessFeatures = new()
                {
                    sType = VkStructureType
                        .PhysicalDeviceRasterizationOrderAttachmentAccessFeaturesEXT,
                    rasterizationOrderColorAttachmentAccess =
                        rasterizationOrderAttachmentAccessFeatureSupported,
                };
            VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR swapchainMaintenanceFeatures = new()
            {
                sType = VkStructureType.PhysicalDeviceSwapchainMaintenance1FeaturesKHR,
                swapchainMaintenance1 = swapchainMaintenanceFeatureSupported,
            };
            void* pNextChain = null;
            if (featureChainPlan.UseVulkan14Features)
            {
                vulkan14Features.pNext = pNextChain;
                pNextChain = &vulkan14Features;
            }
            if (featureChainPlan.UseVulkan13Features)
            {
                vulkan13Features.pNext = pNextChain;
                pNextChain = &vulkan13Features;
            }
            if (featureChainPlan.UseVulkan12Features)
            {
                vulkan12Features.pNext = pNextChain;
                pNextChain = &vulkan12Features;
            }
            if (!featureChainPlan.UseVulkan12Features &&
                separateDepthStencilLayoutsSupported)
            {
                separateDepthStencilLayoutsFeatures.pNext =
                    pNextChain;
                pNextChain =
                    &separateDepthStencilLayoutsFeatures;
            }
            if (useDescriptorIndexingExtension)
            {
                descriptorIndexingFeatures.pNext = pNextChain;
                pNextChain = &descriptorIndexingFeatures;
            }
            if (useDynamicRenderingExtension)
            {
                dynamicRenderingFeatures.pNext = pNextChain;
                pNextChain = &dynamicRenderingFeatures;
            }
            if (useSynchronization2Extension)
            {
                synchronization2Features.pNext = pNextChain;
                pNextChain = &synchronization2Features;
            }
            if (dynamicRenderingLocalReadFeatureSupported &&
                featureChainPlan
                    .UseDynamicRenderingLocalReadExtensionFeatureStruct)
            {
                dynamicRenderingLocalReadFeatures.pNext = pNextChain;
                pNextChain = &dynamicRenderingLocalReadFeatures;
            }
            if (rasterizationOrderAttachmentAccessFeatureSupported)
            {
                rasterizationOrderAttachmentAccessFeatures.pNext =
                    pNextChain;
                pNextChain =
                    &rasterizationOrderAttachmentAccessFeatures;
            }
            if (swapchainMaintenanceFeatureSupported)
            {
                swapchainMaintenanceFeatures.pNext = pNextChain;
                pNextChain = &swapchainMaintenanceFeatures;
            }
            VkPhysicalDeviceAccelerationStructureFeaturesKHR accelFeatures = default;
            VkPhysicalDeviceRayTracingPipelineFeaturesKHR rtPipelineFeatures = default;
            VkPhysicalDeviceRayQueryFeaturesKHR rtQueryFeatures = default;
            if (rtSupported)
            {
                accelFeatures.sType = VkStructureType.PhysicalDeviceAccelerationStructureFeaturesKHR;
                accelFeatures.accelerationStructure = true;
                accelFeatures.pNext = pNextChain;

                rtPipelineFeatures.sType = VkStructureType.PhysicalDeviceRayTracingPipelineFeaturesKHR;
                rtPipelineFeatures.rayTracingPipeline = true;
                rtPipelineFeatures.pNext = &accelFeatures;

                if (rtInlineSupported)
                {
                    rtQueryFeatures.sType = VkStructureType.PhysicalDeviceRayQueryFeaturesKHR;
                    rtQueryFeatures.rayQuery = true;
                    rtQueryFeatures.pNext = &rtPipelineFeatures;
                    pNextChain = &rtQueryFeatures;
                }
                else
                {
                    pNextChain = &rtPipelineFeatures;
                }
            }

            VkPhysicalDeviceMeshShaderFeaturesEXT meshFeatures = default;
            if (meshSupported)
            {
                meshFeatures.sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT;
                meshFeatures.meshShader = true;
                meshFeatures.taskShader = taskShaderFeatureSupported;
                meshFeatures.pNext = pNextChain;
                pNextChain = &meshFeatures;
            }

            VkPhysicalDeviceFragmentShadingRateFeaturesKHR vrsFeatures = default;
            if (vrsSupported)
            {
                vrsFeatures.sType = VkStructureType.PhysicalDeviceFragmentShadingRateFeaturesKHR;
                vrsFeatures.pipelineFragmentShadingRate = fragmentShadingRatePerDrawSupported;
                vrsFeatures.primitiveFragmentShadingRate = fragmentShadingRatePerPrimitiveSupported;
                vrsFeatures.attachmentFragmentShadingRate = fragmentShadingRateAttachmentSupported;
                vrsFeatures.pNext = pNextChain;
                pNextChain = &vrsFeatures;
            }

            VkPhysicalDeviceSubgroupSizeControlFeatures subgroupSizeControlFeatures = default;
            if (subgroupSizeControlUsesExtension)
            {
                subgroupSizeControlFeatures.sType =
                    VkStructureType.PhysicalDeviceSubgroupSizeControlFeatures;
                subgroupSizeControlFeatures.subgroupSizeControl = true;
                subgroupSizeControlFeatures.computeFullSubgroups =
                    subgroupSizeControlFeaturesQuery.computeFullSubgroups;
                subgroupSizeControlFeatures.pNext = pNextChain;
                pNextChain = &subgroupSizeControlFeatures;
            }

            VkPhysicalDeviceCooperativeMatrixFeaturesKHR cooperativeMatrixFeatures = default;
            if (cooperativeMatrixSupported)
            {
                cooperativeMatrixFeatures.sType =
                    VkStructureType.PhysicalDeviceCooperativeMatrixFeaturesKHR;
                cooperativeMatrixFeatures.cooperativeMatrix = true;
                cooperativeMatrixFeatures.pNext = pNextChain;
                pNextChain = &cooperativeMatrixFeatures;
            }

            VkDeviceCreateInfo deviceCreateInfo = new VkDeviceCreateInfo()
            {
                sType = VkStructureType.DeviceCreateInfo,
                pNext = pNextChain,
                queueCreateInfoCount = (uint)familyCount,
                pQueueCreateInfos = queueCreateInfos,
                enabledExtensionCount = (uint)deviceExtensions.Count,
                ppEnabledExtensionNames = (byte**)extensionPtrs,
                pEnabledFeatures = &enabledFeatures,
            };

            try
            {
                fixed (VkDevice* devicePtr = &m_NativeDevice)
                {
                    VulkanUtility.CheckErrors(VulkanNative.vkCreateDevice(m_PhysicalDevice, &deviceCreateInfo, null, devicePtr));
                }
            }
            finally
            {
                for (int i = 0; i < deviceExtensions.Count; ++i)
                {
                    if (extensionPtrs[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(extensionPtrs[i]);
                    }
                }
            }

            m_RaytracingSupported = rtSupported;
            m_RaytracingInlineSupported = rtInlineSupported;
            m_OpacityMicromapExtensionListed = hasOpacityMicromap;
            m_NvInvocationReorderExtensionListed = hasNvInvocationReorder;
            m_ExtInvocationReorderExtensionListed = hasExtInvocationReorder;
            m_NvMotionBlurExtensionListed = hasNvMotionBlur;
            m_MeshShadingSupported = meshSupported;
            m_TaskShadingSupported = taskShaderFeatureSupported;
            m_MeshShaderLimits = QueryMeshShaderLimits(hasMeshShader);
            string cooperativeMatrixUnavailableReason = !hasCooperativeMatrixExtension
                ? "VK_KHR_cooperative_matrix is not listed."
                : cooperativeMatrixSupported
                    ? string.Empty
                    : "VK_KHR_cooperative_matrix is listed but VkPhysicalDeviceCooperativeMatrixFeaturesKHR.cooperativeMatrix is false.";
            m_WaveProbe = VulkanWaveAndCooperativeMatrixNative.Refine(
                m_WaveProbe,
                m_PhysicalDevice,
                m_VulkanInstance,
                subgroupSizeControlSupported,
                subgroupSizeControlUsesVulkan13Core,
                cooperativeMatrixSupported,
                cooperativeMatrixUnavailableReason);

            m_CooperativeMatrixConfigs = m_WaveProbe.Configs;
            m_Limit = new RHIDeviceLimit(
                m_Limit.UniformBufferAlignment,
                m_Limit.UploadBufferAlignment,
                m_Limit.UploadBufferTextureAlignment,
                m_Limit.UploadBufferTextureRowAlignment,
                m_Limit.MaxMSAACount,
                m_Limit.MaxBoundTexture,
                m_WaveProbe.MinWavefrontSize,
                m_WaveProbe.MaxWavefrontSize,
                m_Limit.MaxComputeThreads,
                m_Limit.MaxGroupShareMemorySize,
                m_Limit.MaxVertexInputBindings,
                m_Limit.MaxColorAttachments,
                m_Limit.MaxTexture2DSize,
                m_Limit.MaxTextureCubeSize);
            m_FragmentShadingRateExtensionPresent = hasFragmentShadingRate;
            m_VariableRateShadingPerDrawSupported = fragmentShadingRatePerDrawSupported;
            m_VariableRateShadingPerPrimitiveSupported = fragmentShadingRatePerPrimitiveSupported;
            m_VariableRateShadingAttachmentHardwareSupported = fragmentShadingRateAttachmentSupported;
            m_FragmentShadingRateNonTrivialCombinerOps = fragmentShadingRateNonTrivialCombinerOps;
            // maxFragmentSize is informational only; the supported set comes from
            // vkGetPhysicalDeviceFragmentShadingRatesKHR.
            VulkanFragmentShadingRateMaskQuery rateEnumeration =
                QuerySupportedFragmentShadingRateMask(hasFragmentShadingRate);
            m_FragmentShadingRateEnumerationComplete = rateEnumeration.IsComplete;
            m_SupportedShadingRateMask = rateEnumeration.SupportedMask;
            m_MinFragmentShadingRateAttachmentTexelWidth =
                hasFragmentShadingRate
                    ? fragmentShadingRateProperties
                        .minFragmentShadingRateAttachmentTexelSize.width
                    : 0;
            m_MinFragmentShadingRateAttachmentTexelHeight =
                hasFragmentShadingRate
                    ? fragmentShadingRateProperties
                        .minFragmentShadingRateAttachmentTexelSize.height
                    : 0;
            m_MaxFragmentShadingRateAttachmentTexelWidth =
                hasFragmentShadingRate
                    ? fragmentShadingRateProperties
                        .maxFragmentShadingRateAttachmentTexelSize.width
                    : 0;
            m_MaxFragmentShadingRateAttachmentTexelHeight =
                hasFragmentShadingRate
                    ? fragmentShadingRateProperties
                        .maxFragmentShadingRateAttachmentTexelSize.height
                    : 0;
            m_UseSynchronization2 = useSynchronization2;
            m_UseSynchronization2KhrCommand = useSynchronization2Extension;
            m_DynamicRenderingSupported = dynamicRenderingFeatureSupported;
            m_UseDynamicRenderingKhrCommands =
                useDynamicRenderingExtension;
            m_DynamicRenderingLocalReadSupported =
                dynamicRenderingLocalReadFeatureSupported;
            m_DynamicRenderingLocalReadDepthStencilAttachments =
                dynamicRenderingLocalReadFeatureSupported &&
                localReadDepthStencilAttachments;
            m_DynamicRenderingLocalReadMultisampledAttachments =
                dynamicRenderingLocalReadFeatureSupported &&
                localReadMultisampledAttachments;
            m_DynamicRenderingLocalReadExtensionEnabled =
                dynamicRenderingLocalReadFeatureSupported &&
                featureChainPlan
                    .EnableDynamicRenderingLocalReadExtension;
            m_DynamicRenderingLocalReadProvenance =
                featureChainPlan.DynamicRenderingLocalReadQueryProvenance;
            m_RasterizationOrderAttachmentAccessSupported =
                rasterizationOrderAttachmentAccessFeatureSupported;
            m_RenderPass2Provenance =
                featureChainPlan.RenderPass2Provenance;
            m_SeparateDepthStencilLayoutsSupported =
                separateDepthStencilLayoutsSupported;
            m_SupportedDepthResolveModes =
                hasDepthStencilResolveMechanism
                    ? depthStencilResolveProperties
                        .supportedDepthResolveModes
                    : VkResolveModeFlags.None;
            m_SupportedStencilResolveModes =
                hasDepthStencilResolveMechanism
                    ? depthStencilResolveProperties
                        .supportedStencilResolveModes
                    : VkResolveModeFlags.None;
            m_IndependentResolveNone =
                hasDepthStencilResolveMechanism &&
                depthStencilResolveProperties.independentResolveNone;
            m_IndependentResolve =
                hasDepthStencilResolveMechanism &&
                depthStencilResolveProperties.independentResolve;            m_RenderPass2Supported = featureChainPlan.SupportsRenderPass2;
            m_UseRenderPass2KhrCommands =
                featureChainPlan.UseCreateRenderPass2Extension;
            m_ShaderAtomicInt64Supported = featureChainPlan.UseVulkan12Features
                && (vulkan12FeaturesQuery.shaderBufferInt64Atomics
                    || vulkan12FeaturesQuery.shaderSharedInt64Atomics);
            m_MemoryBudgetSupported = hasMemoryBudgetExtension;
            m_SwapchainSupported = hasSwapchainExtension;
            m_SwapchainMaintenanceSupported =
                swapchainMaintenanceFeatureSupported;
            m_DescriptorIndexingUsesExtension = descriptorIndexingExtension;
            m_SparseResidencyImage2DSupported =
                enabledFeatures.sparseResidencyImage2D;
            m_SparseResidencyImage3DSupported =
                enabledFeatures.sparseResidencyImage3D;
            m_SparseResidencyMsaaSupported =
                sparseBindingSupported &&
                (enabledFeatures.sparseResidency2Samples ||
                 enabledFeatures.sparseResidency4Samples ||
                 enabledFeatures.sparseResidency8Samples ||
                 enabledFeatures.sparseResidency16Samples);
            m_SparseResidencyAliasedSupported =
                enabledFeatures.sparseResidencyAliased;
            m_SparseBindingSupported =
                sparseBindingSupported &&
                (m_SparseResidencyImage2DSupported ||
                 m_SparseResidencyImage3DSupported);
            ProbeVulkanSparseProperties();
            ProbeVulkanCalibratedTimestamps(hasCalibratedTimestamps);
            m_DescriptorFeatures = new VulkanDescriptorFeatures(
                descriptorIndexingFeatureSupported,
                nullDescriptor: false,
                accelerationStructure: rtSupported,
                sampledImageArrayNonUniformIndexing,
                storageImageArrayNonUniformIndexing,
                uniformBufferArrayNonUniformIndexing,
                storageBufferArrayNonUniformIndexing);

            // Create command queues
            CreateCommandQueues(actualComputeQueueCount, actualTransferQueueCount, actualGraphicsQueueCount);
        }

        private void ProbeVulkanSparseProperties()
        {
            VkPhysicalDeviceProperties properties;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &properties);
            VkPhysicalDeviceSparseProperties sparse = properties.sparseProperties;
            m_SparseTileGeometrySupported =
                m_SparseBindingSupported &&
                (sparse.residencyStandard2DBlockShape ||
                 sparse.residencyStandard3DBlockShape);
            m_SparseMipTailSupported =
                m_SparseBindingSupported &&
                !sparse.residencyAlignedMipSize;
            double timestampPeriod = properties.limits.timestampPeriod;
            m_GpuTimestampFrequency = timestampPeriod > 0.0
                ? (ulong)System.Math.Round(1_000_000_000.0 / timestampPeriod)
                : 0;
        }

        private void ProbeVulkanCalibratedTimestamps(bool extensionPresent)
        {
            if (!extensionPresent)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    "VK_KHR_calibrated_timestamps is not supported by this physical device.";
                return;
            }

            if (!VulkanCalibratedTimestampNative.TryLoad(
                    m_VulkanInstance,
                    out m_GetCalibrateableTimeDomains,
                    out m_GetCalibratedTimestamps,
                    out m_CalibratedTimestampsExtensionName,
                    out m_CalibratedTimestampsGetFunctionName))
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    "vkGetPhysicalDeviceCalibrateableTimeDomainsKHR/EXT and vkGetCalibratedTimestampsKHR/EXT are unavailable.";
                return;
            }

            uint domainCount = 0;
            VkResult countResult = m_GetCalibrateableTimeDomains!(
                m_PhysicalDevice,
                &domainCount,
                null);
            if (countResult != VkResult.Success || domainCount == 0)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    $"vkGetPhysicalDeviceCalibrateableTimeDomainsKHR failed: {countResult}.";
                return;
            }

            VkTimeDomainKHR* domains = stackalloc VkTimeDomainKHR[(int)domainCount];
            VkResult fetchResult = m_GetCalibrateableTimeDomains(
                m_PhysicalDevice,
                &domainCount,
                domains);
            if (fetchResult != VkResult.Success)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    $"vkGetPhysicalDeviceCalibrateableTimeDomainsKHR failed: {fetchResult}.";
                return;
            }

            bool hasDevice = false;
            bool hasQpc = false;
            bool hasMonotonic = false;
            for (uint i = 0; i < domainCount; ++i)
            {
                hasDevice |= domains[i] == VkTimeDomainKHR.Device;
                hasQpc |= domains[i] == VkTimeDomainKHR.QueryPerformanceCounter;
                hasMonotonic |= domains[i] == VkTimeDomainKHR.ClockMonotonic;
            }

            if (!hasDevice)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    "VK_TIME_DOMAIN_DEVICE_KHR is not calibrateable on this device.";
                return;
            }

            if (OperatingSystem.IsWindows() && hasQpc)
            {
                m_CpuTimeDomain = VkTimeDomainKHR.QueryPerformanceCounter;
                m_CpuRhiTimeDomain = ERHITimeDomain.QueryPerformanceCounter;
            }
            else if (hasMonotonic)
            {
                m_CpuTimeDomain = VkTimeDomainKHR.ClockMonotonic;
                m_CpuRhiTimeDomain = ERHITimeDomain.ClockMonotonic;
            }
            else
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    "No CPU time domain (QPC or CLOCK_MONOTONIC) is calibrateable with DEVICE.";
                return;
            }

            if (m_GpuTimestampFrequency == 0)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    "VkPhysicalDeviceLimits.timestampPeriod is zero; GPU timestamp frequency is unknown.";
                return;
            }

            VkCalibratedTimestampInfoKHR* infos =
                stackalloc VkCalibratedTimestampInfoKHR[2];
            infos[0].sType =
                VulkanCalibratedTimestampNative.CalibratedTimestampInfoStructureType;
            infos[0].pNext = null;
            infos[0].timeDomain = VkTimeDomainKHR.Device;
            infos[1].sType =
                VulkanCalibratedTimestampNative.CalibratedTimestampInfoStructureType;
            infos[1].pNext = null;
            infos[1].timeDomain = m_CpuTimeDomain;
            ulong* timestamps = stackalloc ulong[2];
            ulong maxDeviation = 0;
            VkResult calibrated = m_GetCalibratedTimestamps!(
                m_NativeDevice,
                2,
                infos,
                timestamps,
                &maxDeviation);
            if (calibrated != VkResult.Success)
            {
                m_CalibratedTimestampsSupported = false;
                m_CalibratedTimestampsUnavailableReason =
                    $"{m_CalibratedTimestampsGetFunctionName} probe failed: {calibrated}.";
                return;
            }

            m_CalibratedTimestampMaxDeviation = maxDeviation;
            m_CalibratedTimestampsSupported = true;
        }

        private VulkanFragmentShadingRateMaskQuery QuerySupportedFragmentShadingRateMask(
            bool hasFragmentShadingRate)
        {
            return VulkanFragmentShadingRateEnumeration.QuerySupportedMask(
                hasFragmentShadingRate,
                new PhysicalDeviceFragmentShadingRateQuery(m_PhysicalDevice));
        }

        private readonly unsafe struct PhysicalDeviceFragmentShadingRateQuery :
            IVulkanFragmentShadingRateQuery
        {
            private readonly VkPhysicalDevice m_PhysicalDevice;

            public PhysicalDeviceFragmentShadingRateQuery(
                VkPhysicalDevice physicalDevice)
            {
                m_PhysicalDevice = physicalDevice;
            }

            public VkResult QueryCount(out uint count)
            {
                uint queriedCount = 0;
                VkResult result =
                    VulkanNative.vkGetPhysicalDeviceFragmentShadingRatesKHR(
                        m_PhysicalDevice,
                        &queriedCount,
                        null);
                count = queriedCount;
                return result;
            }

            public VkResult QueryFetch(
                Span<VkPhysicalDeviceFragmentShadingRateKHR> destination,
                out uint writtenCount)
            {
                writtenCount = (uint)destination.Length;
                fixed (VkPhysicalDeviceFragmentShadingRateKHR* ratesPtr = destination)
                {
                    uint returnedCount = writtenCount;
                    VkResult result =
                        VulkanNative.vkGetPhysicalDeviceFragmentShadingRatesKHR(
                            m_PhysicalDevice,
                            &returnedCount,
                            ratesPtr);
                    writtenCount = returnedCount;
                    return result;
                }
            }
        }

        private unsafe RHICapabilityLimits QueryMeshShaderLimits(bool hasMeshShaderExtension)
        {
            if (!hasMeshShaderExtension)
            {
                return RHICapabilityLimits.Empty;
            }

            VkPhysicalDeviceMeshShaderPropertiesEXT meshProperties = new()
            {
                sType = VkStructureType.PhysicalDeviceMeshShaderPropertiesEXT,
            };
            VkPhysicalDeviceProperties2 properties2 = new()
            {
                sType = VkStructureType.PhysicalDeviceProperties2,
                pNext = &meshProperties,
            };
            VulkanNative.vkGetPhysicalDeviceProperties2(m_PhysicalDevice, &properties2);
            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxOutputVertices,
                    meshProperties.maxMeshOutputVertices),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxOutputPrimitives,
                    meshProperties.maxMeshOutputPrimitives),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxPayloadBytes,
                    meshProperties.maxTaskPayloadSize),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxWorkGroupSizeX,
                    meshProperties.maxMeshWorkGroupSize[0]),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxWorkGroupSizeY,
                    meshProperties.maxMeshWorkGroupSize[1]),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MeshMaxWorkGroupSizeZ,
                    meshProperties.maxMeshWorkGroupSize[2]));
        }

        private void UpdateDeviceFeatures()
        {
            // Re-query features to read actual device capabilities
            VkPhysicalDeviceProperties properties;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &properties);
            VkPhysicalDeviceFeatures features;
            VulkanNative.vkGetPhysicalDeviceFeatures(m_PhysicalDevice, &features);
            VkPhysicalDeviceLimits limits = properties.limits;

            bool hasAtomicInt64 = m_ShaderAtomicInt64Supported;
            bool hasDynamicRenderingLocalReadRoute =
                VulkanRasterCapabilityUtility
                    .HasFramebufferLocalReadLoweringRoute(
                        m_DynamicRenderingSupported,
                        m_DynamicRenderingLocalReadSupported,
                        m_RenderPass2Supported);
            static RHICapability Probe(
                bool available,
                string source,
                string unavailableReason,
                ERHICapabilityTier tier = ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy strategy = ERHICapabilityStrategy.NativeSpecialized,
                ERHICapabilityProbeKind probeKind = ERHICapabilityProbeKind.NativeFeatureQuery,
                RHICapabilityLimits capabilityLimits = default)
            {
                return RHICapability.FromProbe(
                    available,
                    tier,
                    strategy,
                    probeKind,
                    source,
                    unavailableReason,
                    capabilityLimits);
            }

            bool hasUnifiedMemory = false;
            VkMemoryPropertyFlags unifiedFlags =
                VkMemoryPropertyFlags.DeviceLocal
                | VkMemoryPropertyFlags.HostVisible;
            for (uint memoryTypeIndex = 0; memoryTypeIndex < m_MemoryProperties.memoryTypeCount; memoryTypeIndex++)
            {
                if ((m_MemoryProperties.GetMemoryType(memoryTypeIndex).propertyFlags & unifiedFlags) == unifiedFlags)
                {
                    hasUnifiedMemory = true;
                    break;
                }
            }

            RHICapabilityLimits rasterLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumSampleCount, 8),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumVertexInputBindings, limits.maxVertexInputBindings),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumColorAttachments, limits.maxColorAttachments),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTexture2DSize, limits.maxImageDimension2D),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumTextureCubeSize, limits.maxImageDimensionCube));
            RHICapabilityLimits bindingLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.UniformBufferAlignment, limits.minUniformBufferOffsetAlignment),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumBoundTextures, limits.maxDescriptorSetSampledImages),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumBindingTables, limits.maxBoundDescriptorSets),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumSamplerDescriptorsPerTable, limits.maxDescriptorSetSamplers),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumSampledImageDescriptorsPerTable, limits.maxDescriptorSetSampledImages),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumStorageImageDescriptorsPerTable, limits.maxDescriptorSetStorageImages),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumUniformBufferDescriptorsPerTable, limits.maxDescriptorSetUniformBuffers),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumStorageBufferDescriptorsPerTable, limits.maxDescriptorSetStorageBuffers));
            RHICapabilityLimits computeLimits =
                VulkanWaveAndCooperativeMatrixNative.CreateWaveLimits(
                    m_WaveProbe,
                    limits.maxComputeWorkGroupInvocations,
                    limits.maxComputeSharedMemorySize);

            RHICapability layoutIndirectUnavailable = RHICapability.Unavailable(
                "Vulkan layout-driven indirect execution requires VK_EXT_device_generated_commands lowering.",
                ERHICapabilityProbeKind.BackendContract,
                "SharpGPU Vulkan VK_EXT_device_generated_commands lowering");
            VulkanVariableRateShadingCapabilities variableRateShadingCapabilities =
                VulkanVariableRateShadingCapabilityFactory.Create(
                    new VulkanVariableRateShadingProbeState(
                        m_FragmentShadingRateExtensionPresent,
                        m_VariableRateShadingPerDrawSupported,
                        m_VariableRateShadingPerPrimitiveSupported,
                        m_VariableRateShadingAttachmentHardwareSupported,
                        m_FragmentShadingRateNonTrivialCombinerOps,
                        m_FragmentShadingRateEnumerationComplete,
                        m_SupportedShadingRateMask,
                        m_MinFragmentShadingRateAttachmentTexelWidth,
                        m_MinFragmentShadingRateAttachmentTexelHeight,
                        m_MaxFragmentShadingRateAttachmentTexelWidth,
                        m_MaxFragmentShadingRateAttachmentTexelHeight));
            m_Capabilities = new RHIDeviceCapabilities(
                raster: new RHIRasterCapabilities(
                    ERHIProjectionStrategy.FlipY,
                    ERHIMatrixMajorOrder.RowMajor,
                    ERHIDepthValueRange.ZeroToOne,
                    ERHIMultiviewStrategy.Unsupported,
                    pixelShaderStorageWrites: Probe(
                        features.fragmentStoresAndAtomics,
                        "VkPhysicalDeviceFeatures.fragmentStoresAndAtomics",
                        "Fragment-stage storage writes are unavailable.",
                        capabilityLimits: rasterLimits),
                    framebufferReadWrite: Probe(
                        hasDynamicRenderingLocalReadRoute &&
                            m_RasterizationOrderAttachmentAccessSupported,
                        "framebuffer local-read lowering + " +
                        "VK_EXT_rasterization_order_attachment_access/" +
                        "rasterizationOrderColorAttachmentAccess",
                        !hasDynamicRenderingLocalReadRoute
                            ? "No framebuffer-local input attachment route is available."
                            : "VK_EXT_rasterization_order_attachment_access " +
                              "or rasterizationOrderColorAttachmentAccess is unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    anisotropicSampling: Probe(
                        features.samplerAnisotropy,
                        "VkPhysicalDeviceFeatures.samplerAnisotropy",
                        "Sampler anisotropy is unavailable."),
                    depthAttachmentRead: Probe(
                        false,
                        "SharpGPU Vulkan attachment-read lowering",
                        "Depth attachment reads are not exposed by the current Vulkan lowering."),
                    framebufferLocalRead: Probe(
                        hasDynamicRenderingLocalReadRoute,
                        m_DynamicRenderingSupported &&
                            m_DynamicRenderingLocalReadSupported
                            ? m_DynamicRenderingLocalReadProvenance ==
                                EVulkanFeatureProvenance.Vulkan14Core
                                ? m_DynamicRenderingLocalReadExtensionEnabled
                                    ? "VkPhysicalDeviceVulkan14Features.dynamicRenderingLocalRead core query + enabled VK_KHR_dynamic_rendering_local_read + dynamic rendering lowering"
                                    : "VkPhysicalDeviceVulkan14Features.dynamicRenderingLocalRead core query + dynamic rendering lowering"
                                : "VkPhysicalDeviceDynamicRenderingLocalReadFeatures.dynamicRenderingLocalRead KHR query + dynamic rendering lowering"
                            : m_RenderPass2Provenance ==
                                EVulkanFeatureProvenance.KhrExtension
                                    ? "VK_KHR_create_renderpass2 extension input-attachment lowering"
                                    : "Vulkan 1.2 core RenderPass2 input-attachment lowering",
                        !m_DynamicRenderingLocalReadSupported &&
                            !m_RenderPass2Supported
                            ? "Neither VK_KHR_dynamic_rendering_local_read nor RenderPass2 input attachments are available."
                            : m_DynamicRenderingLocalReadSupported &&
                                !m_DynamicRenderingSupported &&
                                !m_RenderPass2Supported
                                ? "Dynamic-rendering local-read is exposed, but dynamic rendering is unavailable and RenderPass2 input attachments are unavailable."
                                : "No exact framebuffer-local-read lowering route is available.",
                        strategy:
                            m_DynamicRenderingSupported &&
                                m_DynamicRenderingLocalReadSupported
                                ? m_DynamicRenderingLocalReadProvenance ==
                                    EVulkanFeatureProvenance.KhrExtension
                                    ? ERHICapabilityStrategy.NativeExtension
                                    : ERHICapabilityStrategy.CoreApi
                                : m_RenderPass2Provenance ==
                                    EVulkanFeatureProvenance.KhrExtension
                                    ? ERHICapabilityStrategy.NativeExtension
                                    : ERHICapabilityStrategy.CoreApi,
                        probeKind:
                            m_DynamicRenderingSupported &&
                                m_DynamicRenderingLocalReadSupported
                                ? m_DynamicRenderingLocalReadProvenance ==
                                    EVulkanFeatureProvenance.KhrExtension
                                    ? ERHICapabilityProbeKind.NativeExtensionQuery
                                    : ERHICapabilityProbeKind.ApiVersion
                                : m_RenderPass2Provenance ==
                                    EVulkanFeatureProvenance.KhrExtension
                                    ? ERHICapabilityProbeKind.NativeExtensionQuery
                                    : ERHICapabilityProbeKind.ApiVersion),
                    drawIndirect: Probe(
                        features.drawIndirectFirstInstance,
                        "VkPhysicalDeviceFeatures.drawIndirectFirstInstance",
                        "Indirect drawing is unavailable."),
                    multiDrawIndirect: Probe(
                        features.multiDrawIndirect,
                        "VkPhysicalDeviceFeatures.multiDrawIndirect",
                        "Multi-draw indirect is unavailable."),
                    variableRateShadingPerDraw:
                        variableRateShadingCapabilities.PerDraw,
                    variableRateShadingPerPrimitive:
                        variableRateShadingCapabilities.PerPrimitive,
                    // Tile-size limits are mapped so attachment lowering can publish them later.
                    // FromProbe(false, ...) discards limits while Attachment is Unavailable.
                    // Hardware support and SharpGPU lowering are independent: a device
                    // without attachmentFragmentShadingRate is a feature miss, not a
                    // lowering gap.
                    variableRateShadingAttachment:
                        variableRateShadingCapabilities.Attachment,
                    // Combiners availability follows executable second sources:
                    // primitive feature+lowering, or attachment lowering when bound.
                    variableRateShadingCombiners:
                        variableRateShadingCapabilities.Combiners,
                    hiddenSurfaceRemoval: Probe(
                        false,
                        "SharpGPU Vulkan raster lowering",
                        "Hidden-surface removal is renderer policy and is not a Vulkan HAL capability."),
                    barycentricCoordinates:
                        VulkanBarycentricCapabilityFactory.CreatePublicCapability(),
                    programmableSamplePositions: Probe(
                        false,
                        "Vulkan programmable sample-location feature query",
                        "Programmable sample locations are not enabled by this Vulkan device."),
                    nativeRenderPass: Probe(
                        m_RenderPass2Supported,
                        m_UseRenderPass2KhrCommands ? "VK_KHR_create_renderpass2 extension" : "Vulkan 1.2 RenderPass2 core",
                        "Vulkan RenderPass2 is unavailable.",
                        strategy: m_UseRenderPass2KhrCommands ? ERHICapabilityStrategy.NativeExtension : ERHICapabilityStrategy.CoreApi,
                        capabilityLimits: rasterLimits),
                    samplerFeedback: RHICapability.Unavailable(
                        "Sampler feedback has no Vulkan equivalent; it is a DX12-only optional facet and is not framebuffer-local read or an attachment feedback loop.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan sampler-feedback contract")),
                binding: new RHIBindingCapabilities(
                    rootConstants: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.BackendContract,
                        "Vulkan push constants",
                        bindingLimits),
                    indirectRootConstants: Probe(
                        false,
                        "SharpGPU Vulkan indirect-command lowering",
                        "Indirect push-constant mutation is not exposed."),
                    atomicUInt64: Probe(
                        hasAtomicInt64,
                        "VkPhysicalDeviceVulkan12Features shader 64-bit atomics",
                        "64-bit shader atomics are unavailable."),
                    descriptorIndexing: Probe(
                        m_DescriptorFeatures.DescriptorIndexing,
                        m_DescriptorIndexingUsesExtension
                            ? "VK_EXT_descriptor_indexing VkPhysicalDeviceDescriptorIndexingFeatures query"
                            : "VkPhysicalDeviceVulkan12Features.descriptorIndexing query",
                        "Descriptor indexing is unavailable.",
                        strategy: m_DescriptorIndexingUsesExtension
                            ? ERHICapabilityStrategy.NativeExtension
                            : ERHICapabilityStrategy.CoreApi,
                        probeKind: m_DescriptorIndexingUsesExtension
                            ? ERHICapabilityProbeKind.NativeExtensionQuery
                            : ERHICapabilityProbeKind.NativeFeatureQuery,
                        capabilityLimits: bindingLimits),
                    partiallyBoundDescriptors: Probe(
                        false,
                        "SharpGPU Vulkan argument-table contract",
                        "Partially-bound descriptors are not exposed without a native null-descriptor contract."),
                    updateAfterBindDescriptors: Probe(
                        false,
                        "SharpGPU SetBindElement external-synchronization contract",
                        "Update-after-bind is deliberately not enabled for mutable public binding tables."),
                    nullDescriptors: Probe(
                        false,
                        "VK_KHR/EXT_robustness2 nullDescriptor feature",
                        "The Vulkan device does not currently enable a native null-descriptor feature.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery)),
                synchronization: new RHISynchronizationCapabilities(
                    timestampQueries: Probe(
                        limits.timestampComputeAndGraphics,
                        "VkPhysicalDeviceLimits.timestampComputeAndGraphics",
                        "Timestamp queries are unavailable."),
                    occlusionQueries: Probe(
                        features.occlusionQueryPrecise,
                        "VkPhysicalDeviceFeatures.occlusionQueryPrecise",
                        "Precise occlusion queries are unavailable."),
                    pipelineStatisticsQueries: Probe(
                        features.pipelineStatisticsQuery,
                        "VkPhysicalDeviceFeatures.pipelineStatisticsQuery",
                        "Pipeline statistics queries are unavailable."),
                    enhancedBarriers: Probe(
                        m_UseSynchronization2,
                        "Vulkan 1.3 synchronization2 or VK_KHR_synchronization2",
                        "Synchronization2 is unavailable.",
                        strategy: m_UseSynchronization2KhrCommand
                            ? ERHICapabilityStrategy.NativeExtension
                            : ERHICapabilityStrategy.CoreApi,
                        probeKind: m_UseSynchronization2KhrCommand
                            ? ERHICapabilityProbeKind.NativeExtensionQuery
                            : ERHICapabilityProbeKind.ApiVersion),
                    calibratedTimestamps: Probe(
                        m_CalibratedTimestampsSupported,
                        m_CalibratedTimestampsExtensionName +
                            " + " +
                            m_CalibratedTimestampsGetFunctionName,
                        m_CalibratedTimestampsUnavailableReason,
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery,
                        capabilityLimits: m_CalibratedTimestampsSupported
                            ? new RHICapabilityLimits(
                                new RHICapabilityLimit(
                                    ERHICapabilityLimitKind.GpuTimestampFrequency,
                                    m_GpuTimestampFrequency),
                                new RHICapabilityLimit(
                                    ERHICapabilityLimitKind.CalibratedTimestampMaxDeviation,
                                    m_CalibratedTimestampMaxDeviation))
                            : default),
                    externalFence64: Probe(
                        m_ExternalFence64Supported,
                        "VK_KHR_external_semaphore + VK_KHR_external_semaphore_win32 + D3D12 fence handle type",
                        OperatingSystem.IsWindows()
                            ? "Vulkan win32 external semaphore or D3D12 fence handle type is unavailable."
                            : "ExternalFence64 is Win32 NT shared fence only.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery)),
                memory: new RHIMemoryCapabilities(
                    unifiedMemory: Probe(
                        hasUnifiedMemory,
                        "VkPhysicalDeviceMemoryProperties DEVICE_LOCAL|HOST_VISIBLE memory type",
                        "No device-local host-visible memory type is available."),
                    placedResources: Probe(
                        true,
                        "vkGet*MemoryRequirements + vkAllocateMemory + vkBind*Memory",
                        "Vulkan placed resources are unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    gpuVirtualAddress: RHICapability.Unavailable(
                        "Vulkan buffer device-address allocation is not established for every SharpGPU buffer path.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan buffer-address contract"),
                    sparseBuffer: RHICapability.Unavailable(
                        "Vulkan sparse-buffer mapping is not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan sparse-buffer contract"),
                    sparseTexture2D: Probe(
                        m_SparseResidencyImage2DSupported &&
                            m_SparseBindingSupported,
                        "enabled VkPhysicalDeviceFeatures.sparseResidencyImage2D + sparse-capable created queue",
                        "Vulkan sparse 2D image residency or a sparse-capable created queue is unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    sparseTexture3D: Probe(
                        m_SparseResidencyImage3DSupported &&
                            m_SparseBindingSupported,
                        "enabled VkPhysicalDeviceFeatures.sparseResidencyImage3D + sparse-capable created queue",
                        "Vulkan sparse 3D image residency or a sparse-capable created queue is unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    sparseMsaa: RHICapability.Unavailable(
                        "Vulkan sparse texture factory rejects MSAA; sparse MSAA residency is not implemented.",
                        ERHICapabilityProbeKind.BackendContract,
                        "VulkanSparseMemoryUtility.ValidateDescriptor"),
                    sparseMipTail: Probe(
                        m_SparseMipTailSupported,
                        "VkPhysicalDeviceSparseProperties.residencyAlignedMipSize",
                        "Vulkan sparse mip-tail binding is unavailable because sparse image residency is off or mip tails are aligned-only."),
                    sparseTileGeometry: Probe(
                        m_SparseTileGeometrySupported,
                        "VkPhysicalDeviceSparseProperties.residencyStandard2D/3DBlockShape",
                        "Vulkan standard sparse block shapes are not reported for 2D or 3D images."),
                    residency: Probe(
                        false,
                        "SharpGPU Vulkan residency lowering",
                        "Explicit residency requests are not implemented."),
                    budgetQuery: VulkanMemoryBudgetUtility.CreateCapability(
                        m_MemoryBudgetSupported,
                        m_MemoryProperties.memoryHeapCount),
                    sparseAliasing: RHICapability.Unavailable(
                        "Vulkan sparse image creation does not set SparseAliased and SharpGPU has no sparse-aliasing path.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan sparse-aliasing contract"),
                    externalImport: RHICapability.Unavailable(
                        "SharpGPU does not implement Vulkan Win32 NT buffer/texture import. Extensions may exist, but the HAL path is not shipped.",
                        ERHICapabilityProbeKind.BackendContract,
                        "ADR-0066 Vulkan external memory"),
                    externalExport: RHICapability.Unavailable(
                        "SharpGPU does not implement Vulkan Win32 NT buffer/texture export. Extensions may exist, but the HAL path is not shipped.",
                        ERHICapabilityProbeKind.BackendContract,
                        "ADR-0066 Vulkan external memory")),
                storage: new RHIStorageCapabilities(
                    nativeGpuFileIo: RHICapability.Unavailable(
                        "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Vulkan storage contract"),
                    gpuDecompression: RHICapability.Unavailable(
                        "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Vulkan storage contract"),
                    requestCancellation: RHICapability.Unavailable(
                        "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Vulkan storage contract"),
                    ioPriority: RHICapability.Unavailable(
                        "Vulkan has no SharpGPU-supported official native GPU file-I/O queue.",
                        ERHICapabilityProbeKind.BackendContract,
                        "Vulkan storage contract")),
                pipelineCache: new RHIPipelineCacheCapabilities(
                    nativeCache: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "VkPipelineCache")),
                presentation: new RHIPresentationCapabilities(
                    swapChain: Probe(
                        m_SwapchainSupported &&
                            m_SwapchainMaintenanceSupported,
                        "VK_KHR_swapchain plus VK_KHR/EXT_swapchain_maintenance1 present-completion fences",
                        !m_SwapchainSupported
                            ? "The Vulkan device does not expose VK_KHR_swapchain."
                            : "Vulkan presentation requires VK_KHR/EXT_swapchain_maintenance1.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    hdr: Probe(
                        false,
                        "Vulkan surface format/color-space probe",
                        "HDR presentation is surface-specific and has not been proven for this device.")),
                rayTracing: new RHIRayTracingCapabilities(
                    pipeline: Probe(
                        m_RaytracingSupported,
                        "Vulkan ray-tracing pipeline feature and extension set",
                        "Ray-tracing pipelines are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    inline: Probe(
                        m_RaytracingInlineSupported,
                        "VkPhysicalDeviceRayQueryFeaturesKHR.rayQuery",
                        "Inline ray queries are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    opacityMicromap: CreateUnusedVulkanRayTracingFacet(
                        m_OpacityMicromapExtensionListed,
                        "VK_EXT_opacity_micromap",
                        "SharpGPU has no micromap object or command path."),
                    shaderExecutionReordering: CreateUnusedVulkanShaderExecutionReorderingFacet(),
                    motion: CreateUnusedVulkanRayTracingFacet(
                        m_NvMotionBlurExtensionListed,
                        "VK_NV_ray_tracing_motion_blur",
                        "SharpGPU has no motion-instance or motion-triangle build path.")),
                mesh: new RHIMeshCapabilities(
                    meshShader: VulkanMeshCapabilityFactory.CreatePublicMeshShaderCapability(
                        m_MeshShadingSupported,
                        m_MeshShaderLimits),
                    taskShader: VulkanMeshCapabilityFactory.CreatePublicTaskShaderCapability(
                        m_TaskShadingSupported)),
                machineLearning: new RHIMachineLearningCapabilities(
                    execution: RHICapability.Unavailable(
                        "Vulkan ML requires a supported native tensor/data-graph contract.",
                        ERHICapabilityProbeKind.NativeExtensionQuery,
                        "Vulkan ML extension set")),
                workGraph: RHIWorkGraphCapabilities.CreateUnavailable(
                    "Vulkan Work Graph execution is not exposed by SharpGPU.",
                    "SharpGPU Vulkan factory surface"),
                indirectCommandBuffer: new RHIIndirectCommandBufferCapabilities(layoutIndirectUnavailable, new RHIIndirectTokenCapabilities(layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable, layoutIndirectUnavailable)),
                compute: new RHIComputeCapabilities(
                    m_WaveProbe.WaveOperationsAvailable
                        ? m_WaveProbe.Strategy
                        : ERHIWaveOperationStrategy.None,
                    waveOperations: RHICapability.FromProbe(
                        m_WaveProbe.WaveOperationsAvailable,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.NativeFeatureQuery,
                        m_WaveProbe.WaveOperationsProbeSource,
                        m_WaveProbe.WaveOperationsUnavailableReason,
                        computeLimits),
                    variableSubgroupSize: RHICapability.FromProbe(
                        m_WaveProbe.VariableSubgroupSizeAvailable,
                        ERHICapabilityTier.Tier1,
                        m_WaveProbe.VariableSubgroupSizeEnabled &&
                            !string.IsNullOrEmpty(m_WaveProbe.VariableSubgroupSizeProbeSource) &&
                            m_WaveProbe.VariableSubgroupSizeProbeSource.Contains(
                                "Vulkan13Properties",
                                StringComparison.Ordinal)
                            ? ERHICapabilityStrategy.CoreApi
                            : ERHICapabilityStrategy.NativeExtension,
                        m_WaveProbe.VariableSubgroupSizeEnabled &&
                            m_WaveProbe.VariableSubgroupSizeProbeSource.Contains(
                                "Vulkan13Properties",
                                StringComparison.Ordinal)
                            ? ERHICapabilityProbeKind.ApiVersion
                            : ERHICapabilityProbeKind.NativeExtensionQuery,
                        m_WaveProbe.VariableSubgroupSizeProbeSource,
                        m_WaveProbe.VariableSubgroupSizeUnavailableReason,
                        VulkanWaveAndCooperativeMatrixNative.CreateVariableSubgroupSizeLimits(
                            m_WaveProbe)),
                    cooperativeMatrix: RHICapability.FromProbe(
                        m_WaveProbe.CooperativeMatrixEnabled,
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.NativeExtension,
                        ERHICapabilityProbeKind.NativeExtensionQuery,
                        m_WaveProbe.CooperativeMatrixProbeSource,
                        m_WaveProbe.CooperativeMatrixUnavailableReason,
                        VulkanWaveAndCooperativeMatrixNative.CreateCooperativeMatrixLimits(
                            m_WaveProbe))),
                functionLibrary: CreateVulkanFunctionLibraryCapabilities(
                    m_RaytracingSupported),
                multiGpu: RHIMultiGpuCapabilities.CreateUnavailable(
                    "Vulkan explicit multi-adapter"));
        }

        private static RHIFunctionLibraryCapabilities CreateVulkanFunctionLibraryCapabilities(
            bool raytracingAvailable)
        {
            ulong reusable =
                (ulong)ERHIFunctionLibraryReusablePipelineClass.Raster |
                (ulong)ERHIFunctionLibraryReusablePipelineClass.Compute |
                (raytracingAvailable
                    ? (ulong)ERHIFunctionLibraryReusablePipelineClass.Raytracing
                    : 0UL);
            return RHIFunctionLibraryCapabilities.CreateNative(
                "shared VkShaderModule + pName views",
                reusable,
                RHIFunctionLibraryCapabilities.PayloadKindBit(ERHIShaderPayloadKind.SpirV),
                rasterComputeUnavailableReason: null);
        }

        private static RHIAdapterIdentity QueryAdapterIdentity(VkPhysicalDevice physicalDevice)
        {
            VkPhysicalDeviceIDProperties idProperties = new()
            {
                sType = VkStructureType.PhysicalDeviceIdProperties,
            };
            VkPhysicalDeviceProperties2 properties2 = new()
            {
                sType = VkStructureType.PhysicalDeviceProperties2,
                pNext = &idProperties,
            };
            VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);

            byte[] uuidBytes = new byte[16];
            for (int i = 0; i < 16; ++i)
            {
                uuidBytes[i] = idProperties.deviceUUID[i];
            }
            Guid deviceUuid = new Guid(uuidBytes);

            long luid = 0;
            if (idProperties.deviceLUIDValid)
            {
                byte[] luidBytes = new byte[8];
                for (int i = 0; i < 8; ++i)
                {
                    luidBytes[i] = idProperties.deviceLUID[i];
                }

                luid = BitConverter.ToInt64(luidBytes, 0);
            }

            return new RHIAdapterIdentity(luid, deviceUuid);
        }

        private static RHICapability CreateUnusedVulkanRayTracingFacet(
            bool listed,
            string extensionName,
            string missingPath)
        {
            string reason = listed
                ? $"{extensionName} is listed but not enabled; {missingPath}"
                : $"{extensionName} is not listed; {missingPath}";
            return RHICapability.Unavailable(
                reason,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                extensionName);
        }

        private RHICapability CreateUnusedVulkanShaderExecutionReorderingFacet()
        {
            const string ExtensionNames =
                "VK_NV_ray_tracing_invocation_reorder / VK_EXT_ray_tracing_invocation_reorder";
            bool listed =
                m_NvInvocationReorderExtensionListed ||
                m_ExtInvocationReorderExtensionListed;
            string reason = listed
                ? $"{ExtensionNames} are listed but not enabled; SharpGPU has no HitObject or invocation-reorder command path. VK_EXT_shader_replicated_composites is not Shader Execution Reordering."
                : $"{ExtensionNames} are not listed; SharpGPU has no HitObject or invocation-reorder command path. VK_EXT_shader_replicated_composites is not Shader Execution Reordering.";
            return RHICapability.Unavailable(
                reason,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                ExtensionNames);
        }

        private void CreateCommandQueues(in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_ComputeQueueCount = computeQueueCount;
            m_TransferQueueCount = transferQueueCount;
            m_GraphicsQueueCount = graphicsQueueCount;
            m_CommandQueueMap = new Dictionary<ERHIPipelineType, TArray<RHICommandQueue>>(3);

            if (graphicsQueueCount > 0 && m_GraphicsQueueFamilyIndex >= 0)
            {
                TArray<RHICommandQueue> graphicsQueueArray = new TArray<RHICommandQueue>(graphicsQueueCount);
                for (int i = 0; i < graphicsQueueCount; ++i)
                {
                    graphicsQueueArray.Add(new VulkanCommandQueue(this, ERHIPipelineType.Graphics, (uint)m_GraphicsQueueFamilyIndex, (uint)i));
                }
                m_CommandQueueMap.Add(ERHIPipelineType.Graphics, graphicsQueueArray);
            }

            if (computeQueueCount > 0 && m_ComputeQueueFamilyIndex >= 0)
            {
                TArray<RHICommandQueue> computeQueueArray = new TArray<RHICommandQueue>(computeQueueCount);
                for (int i = 0; i < computeQueueCount; ++i)
                {
                    computeQueueArray.Add(new VulkanCommandQueue(this, ERHIPipelineType.Compute, (uint)m_ComputeQueueFamilyIndex, (uint)i));
                }
                m_CommandQueueMap.Add(ERHIPipelineType.Compute, computeQueueArray);
            }

            if (transferQueueCount > 0 && m_TransferQueueFamilyIndex >= 0)
            {
                TArray<RHICommandQueue> transferQueueArray = new TArray<RHICommandQueue>(transferQueueCount);
                for (int i = 0; i < transferQueueCount; ++i)
                {
                    transferQueueArray.Add(new VulkanCommandQueue(this, ERHIPipelineType.Transfer, (uint)m_TransferQueueFamilyIndex, (uint)i));
                }
                m_CommandQueueMap.Add(ERHIPipelineType.Transfer, transferQueueArray);
            }
        }

        public override RHICommandQueue? GetCommandQueue(in ERHIPipelineType pipeline, in int index)
        {
            if (m_CommandQueueMap != null && m_CommandQueueMap.TryGetValue(pipeline, out var cmdQueue))
            {
                if (index < cmdQueue.length)
                {
                    return cmdQueue[index];
                }
            }
            return null;
        }

        public override RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            ThrowIfDeviceUnavailable();
            m_Capabilities.Presentation.SwapChain.Require(
                "Vulkan swapchain creation");
            return new VulkanSwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            ThrowIfDeviceUnavailable();
            return new VulkanFence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            ThrowIfDeviceUnavailable();
            return new VulkanSemaphore(this);
        }

        public override RHIExternalFence64 CreateExternalFence64(
            in RHIExternalFence64CreateDescriptor descriptor)
        {
            ThrowIfDeviceUnavailable();
            Capabilities.Synchronization.ExternalFence64.Require(
                "Synchronization.ExternalFence64");
            return VulkanExternalFence64.Create(this, descriptor);
        }

        public override RHIExternalFence64 ImportExternalFence64(
            in RHIExternalFence64ImportDescriptor descriptor)
        {
            ThrowIfDeviceUnavailable();
            Capabilities.Synchronization.ExternalFence64.Require(
                "Synchronization.ExternalFence64");
            return VulkanExternalFence64.Import(this, descriptor);
        }

        public override RHIExternalFence64Export ExportExternalFence64(
            RHIExternalFence64 fence)
        {
            ThrowIfDeviceUnavailable();
            ArgumentNullException.ThrowIfNull(fence);
            Capabilities.Synchronization.ExternalFence64.Require(
                "Synchronization.ExternalFence64");
            if (fence is not VulkanExternalFence64 vulkanFence ||
                !ReferenceEquals(vulkanFence.OwnerDevice, this))
            {
                throw new ArgumentException(
                    "Vulkan ExternalFence64 export requires a fence created by this device.",
                    nameof(fence));
            }

            return vulkanFence.Export();
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            ThrowIfDeviceUnavailable();
            VulkanStorageQueueFactoryPolicy.RequireNativeGpuFileIo(
                Capabilities.Storage.NativeGpuFileIo);
            throw new InvalidOperationException(
                "Vulkan storage queue factory must not return a handle after NativeGpuFileIo Require.");
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            VulkanQueryFactoryPolicy.RequireSupportedQueryType(
                descriptor.Type,
                Capabilities.Synchronization);
            return new VulkanQuery(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require("Vulkan heap creation");
            return new VulkanHeap(this, descriptor);
        }

        public override RHIResourceMemoryRequirements GetBufferMemoryRequirements(
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Vulkan buffer memory requirements");
            VkBufferCreateInfo createInfo = VulkanMemoryUtility.BuildBufferCreateInfo(descriptor);
            VkBuffer buffer = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateBuffer(m_NativeDevice, &createInfo, null, &buffer));
            try
            {
                VkMemoryRequirements nativeRequirements;
                VulkanNative.vkGetBufferMemoryRequirements(
                    m_NativeDevice,
                    buffer,
                    &nativeRequirements);
                ulong compatibilityMask = VulkanMemoryUtility.FilterCompatibleMemoryTypes(
                    this,
                    nativeRequirements.memoryTypeBits,
                    descriptor.StorageMode);
                ulong allocationFlags =
                    (createInfo.usage & VkBufferUsageFlags.ShaderDeviceAddress) != 0
                        ? (ulong)VkMemoryAllocateFlags.DeviceAddress
                        : 0;
                return new RHIResourceMemoryRequirements(
                    this,
                    nativeRequirements.size,
                    nativeRequirements.alignment,
                    descriptor.StorageMode,
                    compatibilityMask,
                    ERHIMemoryResourceKind.Buffer,
                    allocationFlags);
            }
            finally
            {
                VulkanNative.vkDestroyBuffer(m_NativeDevice, buffer, null);
            }
        }

        public override RHIResourceMemoryRequirements GetTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Vulkan texture memory requirements");
            VkImageCreateInfo createInfo =
                VulkanMemoryUtility.BuildImageCreateInfo(this, descriptor, sparse: false);
            VkImage image = default;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateImage(m_NativeDevice, &createInfo, null, &image));
            try
            {
                VkMemoryRequirements nativeRequirements;
                VulkanNative.vkGetImageMemoryRequirements(
                    m_NativeDevice,
                    image,
                    &nativeRequirements);
                ulong compatibilityMask = VulkanMemoryUtility.FilterCompatibleMemoryTypes(
                    this,
                    nativeRequirements.memoryTypeBits,
                    descriptor.StorageMode);
                return new RHIResourceMemoryRequirements(
                    this,
                    nativeRequirements.size,
                    nativeRequirements.alignment,
                    descriptor.StorageMode,
                    compatibilityMask,
                    ERHIMemoryResourceKind.Texture);
            }
            finally
            {
                VulkanNative.vkDestroyImage(m_NativeDevice, image, null);
            }
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new VulkanBuffer(this, descriptor);
        }

        public override RHIBuffer CreatePlacedBuffer(
            RHIHeap heap,
            ulong heapOffset,
            in RHIBufferDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Vulkan placed buffer creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not VulkanHeap vulkanHeap || !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException("Placed buffer heap was created by a different backend or device.", nameof(heap));
            }

            RHIResourceMemoryRequirements requirements = GetBufferMemoryRequirements(descriptor);
            RHIHeapPlacement placement = heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new VulkanBuffer(this, descriptor, vulkanHeap, heapOffset, placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            RejectUnpairedSamplerFeedbackTexture(in descriptor);
            return new VulkanTexture(this, descriptor);
        }

        public override RHICapability QueryRasterAttachmentSupport(
            in RHIRasterAttachmentSupportQuery query)
        {
            ThrowIfDisposed();
            const string ProbeSource =
                "vkGetPhysicalDeviceFormatProperties + " +
                "vkGetPhysicalDeviceImageFormatProperties";

            if (query.IsInput && query.IsOutput &&
                Capabilities.Raster.FramebufferReadWrite.Tier ==
                    ERHICapabilityTier.Unavailable)
            {
                return Capabilities.Raster.FramebufferReadWrite;
            }
            if (query.IsInput &&
                Capabilities.Raster.FramebufferLocalRead.Tier ==
                    ERHICapabilityTier.Unavailable)
            {
                return Capabilities.Raster.FramebufferLocalRead;
            }

            VkFormat format =
                VulkanUtility.ConvertToVkFormat(query.Format);
            VkFormatProperties formatProperties = default;
            VulkanNative.vkGetPhysicalDeviceFormatProperties(
                m_PhysicalDevice,
                format,
                &formatProperties);
            VkFormatFeatureFlags requiredFeatures =
                VkFormatFeatureFlags.ColorAttachment;
            if (query.IsOutput && query.Blend.BlendEnable)
            {
                requiredFeatures |=
                    VkFormatFeatureFlags.ColorAttachmentBlend;
            }
            if ((formatProperties.optimalTilingFeatures &
                 requiredFeatures) != requiredFeatures)
            {
                return RHICapability.Unavailable(
                    $"Vulkan format {format} lacks required optimal-tiling " +
                    $"features {requiredFeatures}.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }

            VkImageUsageFlags usage =
                VkImageUsageFlags.ColorAttachment;
            if (query.IsInput)
            {
                usage |= VkImageUsageFlags.InputAttachment;
            }
            VkImageFormatProperties imageProperties = default;
            VkResult result =
                VulkanNative.vkGetPhysicalDeviceImageFormatProperties(
                    m_PhysicalDevice,
                    format,
                    VkImageType.Image2D,
                    VkImageTiling.Optimal,
                    usage,
                    0,
                    &imageProperties);
            if (result != VkResult.Success)
            {
                return RHICapability.Unavailable(
                    $"Vulkan rejected {format} for image usage {usage}: " +
                    $"{result}.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }
            VkSampleCountFlags sampleCount =
                VulkanUtility.ConvertToVkSampleCount(query.SampleCount);
            if ((imageProperties.sampleCounts & sampleCount) == 0)
            {
                return RHICapability.Unavailable(
                    $"Vulkan format {format} with usage {usage} does not " +
                    $"support {query.SampleCount}.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }

            return RHICapability.Available(
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeSpecialized,
                ERHICapabilityProbeKind.NativeFeatureQuery,
                ProbeSource);
        }

        public override RHICapability QueryFormatSupport(
            in RHIFormatSupportQuery query)
        {
            ThrowIfDisposed();
            const string ProbeSource =
                "vkGetPhysicalDeviceFormatProperties2 + " +
                "vkGetPhysicalDeviceImageFormatProperties2";

            if ((query.Dimension is
                    ERHITextureDimension.Texture2DMS or
                    ERHITextureDimension.Texture2DArrayMS) &&
                query.SampleCount == ERHISampleCount.None)
            {
                return RHICapability.Unavailable(
                    $"Vulkan {query.Dimension} queries require a concrete MSAA sample count.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            if ((query.Usage & ERHITextureUsage.ResolveTarget) != 0 &&
                query.SampleCount != ERHISampleCount.None)
            {
                return RHICapability.Unavailable(
                    "Vulkan resolve destination queries require a single-sample count.",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            VkFormat format = VulkanUtility.ConvertToVkFormat(query.Format);
            VkFormatProperties2 formatProperties = new()
            {
                sType = VkStructureType.FormatProperties2,
            };
            VulkanNative.vkGetPhysicalDeviceFormatProperties2(
                m_PhysicalDevice,
                format,
                &formatProperties);

            VkFormatFeatureFlags tilingFeatures =
                query.Tiling == ERHITextureTiling.Linear
                    ? formatProperties.formatProperties.linearTilingFeatures
                    : formatProperties.formatProperties.optimalTilingFeatures;
            ERHIFormatSupportOperation mask =
                MapVulkanFormatFeatures(
                    tilingFeatures,
                    formatProperties.formatProperties.bufferFeatures);

            VkImageUsageFlags usage =
                VulkanUtility.ConvertToVkImageUsage(query.Usage);
            if (usage == 0)
            {
                return RHICapability.Unavailable(
                    "Vulkan cannot map the requested usage to VkImageUsageFlags",
                    ERHICapabilityProbeKind.BackendContract,
                    ProbeSource);
            }

            VkPhysicalDeviceImageFormatInfo2 imageInfo = new()
            {
                sType = VkStructureType.PhysicalDeviceImageFormatInfo2,
                format = format,
                type = VulkanUtility.ConvertToVkImageType(query.Dimension),
                tiling = query.Tiling == ERHITextureTiling.Linear
                    ? VkImageTiling.Linear
                    : VkImageTiling.Optimal,
                usage = usage,
                flags = VulkanUtility.ConvertToVkImageCreateFlags(query.Dimension),
            };
            VkImageFormatProperties2 imageProperties = new()
            {
                sType = VkStructureType.ImageFormatProperties2,
            };
            VkResult result =
                VulkanNative.vkGetPhysicalDeviceImageFormatProperties2(
                    m_PhysicalDevice,
                    &imageInfo,
                    &imageProperties);
            if (result != VkResult.Success)
            {
                return RHICapability.Unavailable(
                    $"Vulkan rejected {format} for image usage {usage}, " +
                    $"tiling {imageInfo.tiling}: {result}.",
                    ERHICapabilityProbeKind.NativeFeatureQuery,
                    ProbeSource);
            }

            if (query.SampleCount != ERHISampleCount.None)
            {
                VkSampleCountFlags sampleCount =
                    VulkanUtility.ConvertToVkSampleCount(query.SampleCount);
                if ((imageProperties.imageFormatProperties.sampleCounts &
                     sampleCount) == 0)
                {
                    return RHICapability.Unavailable(
                        $"Vulkan format {format} does not support {query.SampleCount} for the queried image combination.",
                        ERHICapabilityProbeKind.NativeFeatureQuery,
                        ProbeSource);
                }
            }

            return RHICapability.Available(
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.CoreApi,
                ERHICapabilityProbeKind.NativeFeatureQuery,
                ProbeSource,
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.SupportedFormatOperationMask,
                        (ulong)mask)));
        }

        private static ERHIFormatSupportOperation MapVulkanFormatFeatures(
            VkFormatFeatureFlags tilingFeatures,
            VkFormatFeatureFlags bufferFeatures)
        {
            ERHIFormatSupportOperation mask = ERHIFormatSupportOperation.None;
            if ((tilingFeatures & VkFormatFeatureFlags.SampledImage) != 0)
            {
                mask |= ERHIFormatSupportOperation.Sample;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.StorageImage) != 0)
            {
                mask |= ERHIFormatSupportOperation.StorageLoad |
                    ERHIFormatSupportOperation.StorageStore;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.StorageImageAtomic) != 0)
            {
                mask |= ERHIFormatSupportOperation.Atomic;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.ColorAttachment) != 0)
            {
                mask |= ERHIFormatSupportOperation.ColorAttachment;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.DepthStencilAttachment) != 0)
            {
                mask |= ERHIFormatSupportOperation.DepthStencilAttachment;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.ColorAttachmentBlend) != 0)
            {
                mask |= ERHIFormatSupportOperation.Blend;
            }
            if ((tilingFeatures & VkFormatFeatureFlags.SampledImageFilterLinear) != 0)
            {
                mask |= ERHIFormatSupportOperation.LinearFilter;
            }
            if ((bufferFeatures & VkFormatFeatureFlags.VertexBuffer) != 0)
            {
                mask |= ERHIFormatSupportOperation.VertexBuffer;
            }

            return mask;
        }

        public override int QueryCooperativeMatrixConfigs(
            Span<RHICooperativeMatrixConfig> destination)
        {
            ThrowIfDisposed();
            return CopyCooperativeMatrixConfigs(
                m_CooperativeMatrixConfigs,
                destination);
        }

        public override RHIClockCalibration QueryClockCalibration(
            ERHIPipelineType queue,
            int queueIndex = 0)
        {
            ThrowIfDisposed();
            Capabilities.Synchronization.CalibratedTimestamps.Require(
                "Synchronization.CalibratedTimestamps");
            _ = RequireCommandQueue(queue, queueIndex, "QueryClockCalibration");
            return QueryVulkanClockCalibration(queue, queueIndex);
        }

        private RHIClockCalibration QueryVulkanClockCalibration(
            ERHIPipelineType queue,
            int queueIndex)
        {
            if (m_GetCalibratedTimestamps == null ||
                m_GpuTimestampFrequency == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan calibrated timestamps were reported available without loaded entry points.");
            }

            VkCalibratedTimestampInfoKHR* infos =
                stackalloc VkCalibratedTimestampInfoKHR[2];
            infos[0].sType =
                VulkanCalibratedTimestampNative.CalibratedTimestampInfoStructureType;
            infos[0].pNext = null;
            infos[0].timeDomain = VkTimeDomainKHR.Device;
            infos[1].sType =
                VulkanCalibratedTimestampNative.CalibratedTimestampInfoStructureType;
            infos[1].pNext = null;
            infos[1].timeDomain = m_CpuTimeDomain;
            ulong* timestamps = stackalloc ulong[2];
            ulong maxDeviation = 0;
            VkResult result = m_GetCalibratedTimestamps(
                m_NativeDevice,
                2,
                infos,
                timestamps,
                &maxDeviation);
            if (result != VkResult.Success)
            {
                throw new InvalidOperationException(
                    $"{m_CalibratedTimestampsGetFunctionName} failed: {result}.");
            }

            return new RHIClockCalibration(
                timestamps[0],
                timestamps[1],
                m_GpuTimestampFrequency,
                m_CpuRhiTimeDomain,
                queue,
                queueIndex,
                maxDeviation);
        }

        public override RHIRasterAttachmentShaderAbi
            QueryRasterAttachmentShaderAbi(
                in RHIRasterAttachmentShaderAbiDescriptor descriptor)
        {
            ThrowIfDisposed();
            VulkanPipelineLayout pipelineLayout =
                descriptor.PipelineLayout as VulkanPipelineLayout ??
                throw new ArgumentException(
                    "Vulkan attachment shader ABI requires a Vulkan pipeline layout.",
                    nameof(descriptor));
            VulkanPrivateRasterBindingPlan plan =
                VulkanPrivatePipelineLayoutBuilder.CompilePlan(
                    this,
                    pipelineLayout,
                    in descriptor.AttachmentInterface);
            RHIAttachmentInterfaceSignature signature =
                descriptor.AttachmentInterface;
            List<RHIRasterAttachmentShaderBinding> bindings = new();
            for (int logicalAttachment = 0;
                 logicalAttachment < signature.ColorAttachmentCount;
                 ++logicalAttachment)
            {
                int inputSlot =
                    RHIRasterAttachmentShaderAbiFactory.FindInputSlot(
                        in signature,
                        logicalAttachment);
                int outputLocation =
                    RHIRasterAttachmentShaderAbiFactory.FindOutputLocation(
                        in signature,
                        logicalAttachment);
                if (inputSlot < 0 && outputLocation < 0)
                {
                    continue;
                }
                RHIRawShaderBindingLocation input = default;
                RHIRawShaderBindingLocation output = default;
                if (inputSlot >= 0)
                {
                    uint binding = plan.GetInputAttachmentBinding(inputSlot);
                    input = new RHIRawShaderBindingLocation(
                        ERHIRawShaderBindingKind.InputAttachment,
                        binding,
                        plan.DescriptorSet);
                }
                if (outputLocation >= 0)
                {
                    output = new RHIRawShaderBindingLocation(
                        ERHIRawShaderBindingKind.ColorAttachment,
                        checked((uint)outputLocation));
                }
                bindings.Add(new RHIRasterAttachmentShaderBinding(
                    logicalAttachment,
                    inputSlot,
                    outputLocation,
                    input,
                    output));
            }
            return RHIRasterAttachmentShaderAbiFactory.Create(
                BackendType,
                in descriptor,
                bindings.ToArray());
        }

        public override RHITexture CreatePlacedTexture(
            RHIHeap heap,
            ulong heapOffset,
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.PlacedResources.Require(
                "Vulkan placed texture creation");
            ArgumentNullException.ThrowIfNull(heap);
            if (heap is not VulkanHeap vulkanHeap || !ReferenceEquals(heap.OwnerDevice, this))
            {
                throw new ArgumentException("Placed texture heap was created by a different backend or device.", nameof(heap));
            }

            RHIResourceMemoryRequirements requirements = GetTextureMemoryRequirements(descriptor);
            RHIHeapPlacement placement = heap.ReservePlacement(heapOffset, requirements);
            try
            {
                return new VulkanTexture(this, descriptor, vulkanHeap, heapOffset, placement);
            }
            catch
            {
                placement.Dispose();
                throw;
            }
        }

        public override RHISparseTextureMemoryRequirements GetSparseTextureMemoryRequirements(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.RequireSparseTexture(
                descriptor,
                "Vulkan sparse texture requirements");
            VkImage image = VulkanSparseMemoryUtility.CreateSparseImage(
                this,
                descriptor);
            try
            {
                return VulkanSparseMemoryUtility.QueryRequirements(
                    this,
                    descriptor,
                    image);
            }
            finally
            {
                VulkanNative.vkDestroyImage(m_NativeDevice, image, null);
            }
        }

        public override RHITexture CreateSparseTexture(
            in RHITextureDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.Memory.RequireSparseTexture(
                descriptor,
                "Vulkan sparse textures");
            return new VulkanTexture(this, descriptor, createSparse: true);
        }

        public override RHIMemoryBudget QueryMemoryBudget(
            ERHIStorageMode storageMode)
        {
            ThrowIfDisposed();
            Capabilities.Memory.BudgetQuery.Require(
                "Vulkan memory budget query");
            return VulkanMemoryBudgetUtility.Query(
                this,
                storageMode);
        }

        public override RHISampler CreateSampler(in RHISamplerDescriptor descriptor)
        {
            return new VulkanSampler(this, descriptor);
        }

        public override RHITopLevelAccelStruct CreateTopAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            return new VulkanTopLevelAccelStruct(this, descriptor);
        }

        public override RHIBottomLevelAccelStruct CreateBottomAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            return new VulkanBottomLevelAccelStruct(this, descriptor);
        }

        public override RHIBindingTableLayout CreateBindingTableLayout(in RHIBindingTableLayoutDescriptor descriptor)
        {
            return new VulkanBindingTableLayout(this, descriptor);
        }

        public override RHIBindingTable CreateBindingTable(in RHIBindingTableDescriptor descriptor)
        {
            return new VulkanBindingTable(this, descriptor);
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            return new VulkanPipelineLayout(this, descriptor);
        }

        public override RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor)
        {
            return new VulkanFunction(this, descriptor);
        }

        public override RHIFunctionLibrary CreateFunctionLibrary(in RHIFunctionLibraryDescriptor descriptor)
        {
            Capabilities.FunctionLibrary.NativeLibrary.Require("FunctionLibrary.NativeLibrary");
            return new VulkanFunctionLibrary(this, descriptor);
        }

        public override RHIFunctionTable CreateFunctionTable()
        {
            return new VulkanFunctionTable(this);
        }

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            return new VulkanComputePipeline(this, descriptor);
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.RayTracing.Pipeline.Require("Vulkan ray-tracing pipelines");
            return new VulkanRaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            VulkanMeshCapabilityFactory.RequireMeshRasterPipeline(
                in descriptor,
                Capabilities.Mesh.MeshShader,
                Capabilities.Mesh.TaskShader);
            return new VulkanRasterPipeline(this, descriptor);
        }

        public override RHIPipelineCache CreatePipelineCache()
        {
            Capabilities.PipelineCache.NativeCache.Require("Vulkan pipeline cache");
            return new VulkanPipelineCache(this);
        }
        public override RHIIndirectCommandLayout CreateIndirectCommandLayout(in RHIIndirectCommandLayoutDescriptor descriptor)
        {
            ThrowIfDisposed();
            return new VulkanIndirectCommandLayout(this, descriptor);
        }


        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Vulkan machine-learning pipelines");
            return new VulkanMLPipeline(this, descriptor);
        }

        public override RHIMLBindingTable CreateMLBindingTable(in RHIMLBindingTableDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Vulkan machine-learning binding tables");
            return new VulkanMLBindingTable(this, descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.MachineLearning.Execution.Require(
                "Vulkan machine-learning tensors");
            return new VulkanTensor(this, descriptor);
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            Capabilities.WorkGraph.Execution.Require(
                "Vulkan work-graph pipelines");
            throw new NotSupportedException(
                "Vulkan work-graph pipelines are unavailable: "
                + Capabilities.WorkGraph.Execution.UnavailableReason);
        }

        public int GetQueueFamilyIndex(in ERHIPipelineType pipeline)
        {
            switch (pipeline)
            {
                case ERHIPipelineType.Graphics:
                    return m_GraphicsQueueFamilyIndex;
                case ERHIPipelineType.Compute:
                    return m_ComputeQueueFamilyIndex;
                case ERHIPipelineType.Transfer:
                    return m_TransferQueueFamilyIndex;
                default:
                    return m_GraphicsQueueFamilyIndex;
            }
        }

        protected override void Release()
        {
            if (m_CommandQueueMap != null)
            {
                foreach (var kvp in m_CommandQueueMap)
                {
                    for (int i = 0; i < kvp.Value.length; ++i)
                    {
                        kvp.Value[i].Dispose();
                    }
                }
            }

            m_DescriptorPoolAllocator.Dispose();
            VulkanNative.vkDestroyDevice(m_NativeDevice, null);
        }
    }
}


namespace SharpGPU
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VulkanDebugUtilsMessengerCreateInfo
    {
        internal uint StructureType;
        internal void* Next;
        internal uint Flags;
        internal uint MessageSeverity;
        internal uint MessageType;
        internal IntPtr UserCallback;
        internal void* UserData;
    }

    /// <summary>
    /// Owns the debug-utils messenger created while a Vulkan instance is still
    /// being constructed. Keeping the creation-info callback in the instance
    /// pNext chain is required to observe validation failures from logical
    /// device creation as well as from later command recording and execution.
    /// </summary>
    internal unsafe sealed class VulkanValidationDiagnostics : IDisposable
    {
        private const uint DebugUtilsMessengerCreateInfoStructureType =
            1000128004;
        private const uint ErrorSeverity = 0x00001000;
        private const uint GeneralMessageType = 0x00000001;
        private const uint ValidationMessageType = 0x00000002;
        private const uint PerformanceMessageType = 0x00000004;

        private readonly ConcurrentQueue<string> m_Errors = new();
        private readonly DebugUtilsMessengerCallback m_Callback;
        private DestroyDebugUtilsMessenger? m_Destroy;
        private VkInstance m_Instance;
        private ulong m_Messenger;
        private bool m_Disposed;

        internal VulkanValidationDiagnostics()
        {
            m_Callback = Collect;
        }

        internal VulkanDebugUtilsMessengerCreateInfo
            CreateInstanceCreateInfo()
        {
            ThrowIfDisposed();
            return new VulkanDebugUtilsMessengerCreateInfo
            {
                StructureType =
                    DebugUtilsMessengerCreateInfoStructureType,
                MessageSeverity = ErrorSeverity,
                MessageType =
                    GeneralMessageType |
                    ValidationMessageType |
                    PerformanceMessageType,
                UserCallback =
                    Marshal.GetFunctionPointerForDelegate(m_Callback),
            };
        }

        internal void Attach(
            VkInstance instance,
            IntPtr createProcedure,
            IntPtr destroyProcedure)
        {
            ThrowIfDisposed();
            if (instance.Handle == 0)
            {
                throw new ArgumentException(
                    "A non-null Vulkan instance is required.",
                    nameof(instance));
            }
            if (createProcedure == IntPtr.Zero ||
                destroyProcedure == IntPtr.Zero)
            {
                throw new NotSupportedException(
                    "VK_EXT_debug_utils does not expose its required " +
                    "messenger entry points.");
            }
            if (m_Messenger != 0)
            {
                throw new InvalidOperationException(
                    "The Vulkan validation messenger is already attached.");
            }

            CreateDebugUtilsMessenger create =
                Marshal.GetDelegateForFunctionPointer<
                    CreateDebugUtilsMessenger>(createProcedure);
            m_Destroy = Marshal.GetDelegateForFunctionPointer<
                DestroyDebugUtilsMessenger>(destroyProcedure);
            VulkanDebugUtilsMessengerCreateInfo createInfo =
                CreateInstanceCreateInfo();
            ulong messenger = 0;
            VkResult result = create(
                instance,
                &createInfo,
                null,
                &messenger);
            if (result != VkResult.Success)
            {
                m_Destroy = null;
                throw new InvalidOperationException(
                    "vkCreateDebugUtilsMessengerEXT failed with " +
                    $"VkResult {result}.");
            }

            m_Instance = instance;
            m_Messenger = messenger;
        }

        /// <summary>
        /// Reads the collected validation result after command, device, and
        /// instance teardown as well as while the messenger is live. Disposal
        /// only releases the native messenger; it must not discard evidence.
        /// </summary>
        internal void ThrowIfErrors(string observedStages)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(observedStages);
            string[] errors = m_Errors.ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    "Vulkan validation reported Error messages during " +
                    observedStages + ":" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors));
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            if (m_Messenger != 0)
            {
                m_Destroy!(m_Instance, m_Messenger, null);
                m_Messenger = 0;
            }
            m_Destroy = null;
            m_Instance = default;
            m_Disposed = true;
        }

        private uint Collect(
            uint severity,
            uint messageTypes,
            IntPtr callbackData,
            IntPtr userData)
        {
            _ = userData;
            try
            {
                VulkanDebugUtilsMessengerCallbackData data =
                    Marshal.PtrToStructure<
                        VulkanDebugUtilsMessengerCallbackData>(
                            callbackData);
                string identifier =
                    Marshal.PtrToStringUTF8(data.MessageIdName) ??
                    "unknown";
                string message =
                    Marshal.PtrToStringUTF8(data.Message) ??
                    "Vulkan validation returned no message text.";
                m_Errors.Enqueue(
                    $"[severity=0x{severity:X}, types=0x{messageTypes:X}, " +
                    $"id={identifier}/{data.MessageIdNumber}] {message}");
            }
            catch (Exception exception)
            {
                m_Errors.Enqueue(
                    "Failed to decode a Vulkan validation Error callback: " +
                    exception.Message);
            }

            return 0;
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(m_Disposed, this);

        [StructLayout(LayoutKind.Sequential)]
        private struct VulkanDebugUtilsMessengerCallbackData
        {
            public uint StructureType;
            public IntPtr Next;
            public uint Flags;
            public IntPtr MessageIdName;
            public int MessageIdNumber;
            public IntPtr Message;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate VkResult CreateDebugUtilsMessenger(
            VkInstance instance,
            VulkanDebugUtilsMessengerCreateInfo* createInfo,
            void* allocator,
            ulong* messenger);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyDebugUtilsMessenger(
            VkInstance instance,
            ulong messenger,
            void* allocator);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint DebugUtilsMessengerCallback(
            uint severity,
            uint messageTypes,
            IntPtr callbackData,
            IntPtr userData);
    }

    internal interface IVulkanFragmentShadingRateQuery
    {
        VkResult QueryCount(out uint count);
        VkResult QueryFetch(
            Span<VkPhysicalDeviceFragmentShadingRateKHR> destination,
            out uint writtenCount);
    }

    internal readonly struct VulkanFragmentShadingRateMaskQuery
    {
        public bool IsComplete { get; }
        public ulong SupportedMask { get; }

        private VulkanFragmentShadingRateMaskQuery(
            bool isComplete,
            ulong supportedMask)
        {
            IsComplete = isComplete;
            SupportedMask = supportedMask;
        }

        public static VulkanFragmentShadingRateMaskQuery Complete(ulong supportedMask)
        {
            return new VulkanFragmentShadingRateMaskQuery(true, supportedMask);
        }

        public static VulkanFragmentShadingRateMaskQuery CompleteEmpty()
        {
            return new VulkanFragmentShadingRateMaskQuery(true, 0);
        }

        public static VulkanFragmentShadingRateMaskQuery Unreliable()
        {
            return new VulkanFragmentShadingRateMaskQuery(false, 0);
        }
    }

    internal static class VulkanFragmentShadingRateEnumeration
    {
        internal const int MaxFetchAttempts = 2;
        internal const int MaxEnumeratedRateCount = 256;
        internal const string UnstableCompleteRateListReason =
            "vkGetPhysicalDeviceFragmentShadingRatesKHR did not return a stable complete rate list.";
        internal const string MissingRequired1x1RateReason =
            "vkGetPhysicalDeviceFragmentShadingRatesKHR stable list did not include required 1x1 rate.";
        internal const string FragmentShadingRatesQuerySource =
            "vkGetPhysicalDeviceFragmentShadingRatesKHR";
        internal const ulong Required1x1ShadingRateBit =
            1UL << (byte)ERHIShadingRate.Rate1x1;

        internal static bool TryGetSafeCount(uint count, out int safeCount)
        {
            if (count > (uint)int.MaxValue ||
                count > (uint)MaxEnumeratedRateCount)
            {
                safeCount = 0;
                return false;
            }

            safeCount = (int)count;
            return true;
        }

        internal static bool IsStableCompleteResult(
            VkResult result,
            uint returnedCount,
            in int capacity)
        {
            return result == VkResult.Success &&
                returnedCount <= (uint)capacity;
        }

        internal static bool IsIncompleteOrTruncated(
            VkResult result,
            uint returnedCount,
            in int capacity)
        {
            return result == VkResult.Incomplete ||
                returnedCount > (uint)capacity;
        }

        // Callers must pass a stable complete enumeration. Unknown sizes are
        // skipped; Pending is never written into the published mask.
        internal static ulong BuildSupportedShadingRateMask(
            ReadOnlySpan<VkExtent2D> fragmentSizes)
        {
            ulong mask = 0;
            for (int i = 0; i < fragmentSizes.Length; ++i)
            {
                if (VulkanUtility.TryMapVkFragmentSizeToShadingRate(
                    fragmentSizes[i],
                    out ERHIShadingRate shadingRate) &&
                    shadingRate != ERHIShadingRate.Pending)
                {
                    mask |= 1UL << (byte)shadingRate;
                }
            }

            return mask;
        }

        internal static bool ContainsRequired1x1Rate(ulong supportedShadingRateMask)
        {
            return (supportedShadingRateMask & Required1x1ShadingRateBit) != 0;
        }

        internal static VulkanFragmentShadingRateMaskQuery QuerySupportedMask<TQuery>(
            bool hasFragmentShadingRate,
            TQuery query)
            where TQuery : IVulkanFragmentShadingRateQuery
        {
            if (!hasFragmentShadingRate)
            {
                // Meaningful only when VariableRateShadingPerDraw is Available.
                // No VK_KHR_fragment_shading_rate query means no supported-rate evidence.
                return VulkanFragmentShadingRateMaskQuery.CompleteEmpty();
            }

            if (!TryQuerySafeCount(query, out uint count, out int safeCount))
            {
                return VulkanFragmentShadingRateMaskQuery.Unreliable();
            }

            if (count == 0)
            {
                // Zero is a legal complete list: no enumerated fragment sizes.
                return VulkanFragmentShadingRateMaskQuery.CompleteEmpty();
            }

            for (int attempt = 0; attempt < MaxFetchAttempts; ++attempt)
            {
                int capacity = safeCount;
                VkPhysicalDeviceFragmentShadingRateKHR[] rates =
                    new VkPhysicalDeviceFragmentShadingRateKHR[capacity];
                for (int i = 0; i < capacity; ++i)
                {
                    rates[i] = new VkPhysicalDeviceFragmentShadingRateKHR
                    {
                        sType =
                            VkStructureType
                                .PhysicalDeviceFragmentShadingRateKHR,
                    };
                }

                VkResult result = query.QueryFetch(rates, out uint returnedCount);
                if (IsStableCompleteResult(result, returnedCount, capacity))
                {
                    VkExtent2D[] fragmentSizes = new VkExtent2D[returnedCount];
                    for (uint i = 0; i < returnedCount; ++i)
                    {
                        fragmentSizes[i] = rates[i].fragmentSize;
                    }

                    return VulkanFragmentShadingRateMaskQuery.Complete(
                        BuildSupportedShadingRateMask(fragmentSizes));
                }

                if (!IsIncompleteOrTruncated(result, returnedCount, capacity) ||
                    attempt + 1 >= MaxFetchAttempts)
                {
                    return VulkanFragmentShadingRateMaskQuery.Unreliable();
                }

                // VK_INCOMPLETE overwrites the count with the number of
                // structures actually written, not the required capacity.
                // Re-run the count-only query before any retry allocation.
                if (!TryQuerySafeCount(query, out count, out safeCount) ||
                    count == 0)
                {
                    return VulkanFragmentShadingRateMaskQuery.Unreliable();
                }
            }

            return VulkanFragmentShadingRateMaskQuery.Unreliable();
        }

        private static bool TryQuerySafeCount<TQuery>(
            TQuery query,
            out uint count,
            out int safeCount)
            where TQuery : IVulkanFragmentShadingRateQuery
        {
            safeCount = 0;
            VkResult countResult = query.QueryCount(out uint queriedCount);
            if (countResult != VkResult.Success)
            {
                // Capability probe must not fail device create. Incomplete or
                // any other native result is not a stable complete rate list.
                count = 0;
                return false;
            }

            if (queriedCount == 0)
            {
                count = 0;
                return true;
            }

            if (!TryGetSafeCount(queriedCount, out safeCount))
            {
                count = 0;
                return false;
            }

            count = queriedCount;
            return true;
        }
    }

    internal readonly struct VulkanVariableRateShadingProbeState
    {
        public bool ExtensionPresent { get; }
        public bool PerDrawSupported { get; }
        public bool PerPrimitiveSupported { get; }
        public bool AttachmentHardwareSupported { get; }
        public bool NonTrivialCombinerOps { get; }
        public bool RateEnumerationComplete { get; }
        public ulong SupportedShadingRateMask { get; }
        public uint MinAttachmentTileWidth { get; }
        public uint MinAttachmentTileHeight { get; }
        public uint MaxAttachmentTileWidth { get; }
        public uint MaxAttachmentTileHeight { get; }

        public VulkanVariableRateShadingProbeState(
            in bool extensionPresent,
            in bool perDrawSupported,
            in bool perPrimitiveSupported,
            in bool attachmentHardwareSupported,
            in bool nonTrivialCombinerOps,
            in bool rateEnumerationComplete,
            in ulong supportedShadingRateMask,
            in uint minAttachmentTileWidth,
            in uint minAttachmentTileHeight,
            in uint maxAttachmentTileWidth,
            in uint maxAttachmentTileHeight)
        {
            ExtensionPresent = extensionPresent;
            PerDrawSupported = perDrawSupported;
            PerPrimitiveSupported = perPrimitiveSupported;
            AttachmentHardwareSupported = attachmentHardwareSupported;
            NonTrivialCombinerOps = nonTrivialCombinerOps;
            RateEnumerationComplete = rateEnumerationComplete;
            SupportedShadingRateMask = supportedShadingRateMask;
            MinAttachmentTileWidth = minAttachmentTileWidth;
            MinAttachmentTileHeight = minAttachmentTileHeight;
            MaxAttachmentTileWidth = maxAttachmentTileWidth;
            MaxAttachmentTileHeight = maxAttachmentTileHeight;
        }
    }

    internal readonly struct VulkanVariableRateShadingCapabilities
    {
        public RHICapability PerDraw { get; }
        public RHICapability PerPrimitive { get; }
        public RHICapability Attachment { get; }
        public RHICapability Combiners { get; }

        public VulkanVariableRateShadingCapabilities(
            in RHICapability perDraw,
            in RHICapability perPrimitive,
            in RHICapability attachment,
            in RHICapability combiners)
        {
            PerDraw = perDraw;
            PerPrimitive = perPrimitive;
            Attachment = attachment;
            Combiners = combiners;
        }
    }

    internal static class VulkanVariableRateShadingCapabilityFactory
    {
        internal const string ExtensionPresenceQuerySource =
            "VK_KHR_fragment_shading_rate extension presence query";
        internal const string ExtensionUnsupportedReason =
            "VK_KHR_fragment_shading_rate is not supported by this physical device.";
        internal const string PipelineFragmentShadingRateSource =
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.pipelineFragmentShadingRate";
        internal const string PrimitiveFragmentShadingRateSource =
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.primitiveFragmentShadingRate";
        internal const string AttachmentFragmentShadingRateSource =
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.attachmentFragmentShadingRate";
        internal const string RasterLoweringSource =
            "SharpGPU Vulkan raster lowering";
        internal const string CombinableFeatureFieldsSource =
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.primitiveFragmentShadingRate+attachmentFragmentShadingRate";
        internal const string CombinersBothFeaturesUnsupportedReason =
            "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.primitiveFragmentShadingRate and attachmentFragmentShadingRate are both false; this physical device has no combinable second shading-rate source.";
        internal const string CombinersAttachmentLoweringUnboundReason =
            "This physical device supports attachmentFragmentShadingRate, but SharpGPU Vulkan raster lowering has not bound a fragment shading rate attachment, so there is no combinable second source.";

        public static VulkanVariableRateShadingCapabilities Create(
            in VulkanVariableRateShadingProbeState state)
        {
            if (!state.ExtensionPresent)
            {
                RHICapability extensionMissing = RHICapability.FromProbe(
                    false,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    ExtensionPresenceQuerySource,
                    ExtensionUnsupportedReason);
                return new VulkanVariableRateShadingCapabilities(
                    extensionMissing,
                    extensionMissing,
                    extensionMissing,
                    extensionMissing);
            }

            RHICapability perDraw = CreatePerDraw(in state);
            RHICapability perPrimitive = RHICapability.FromProbe(
                state.PerPrimitiveSupported,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                PrimitiveFragmentShadingRateSource,
                "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.primitiveFragmentShadingRate is false.");
            RHICapability attachment = RHICapability.FromProbe(
                false,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                state.AttachmentHardwareSupported
                    ? ERHICapabilityProbeKind.BackendContract
                    : ERHICapabilityProbeKind.NativeExtensionQuery,
                state.AttachmentHardwareSupported
                    ? RasterLoweringSource
                    : AttachmentFragmentShadingRateSource,
                state.AttachmentHardwareSupported
                    ? "SharpGPU Vulkan raster lowering has not bound a fragment shading rate attachment."
                    : "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.attachmentFragmentShadingRate is false.",
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMin,
                        state.MinAttachmentTileWidth),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMin,
                        state.MinAttachmentTileHeight),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileWidthMax,
                        state.MaxAttachmentTileWidth),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.ShadingRateAttachmentTileHeightMax,
                        state.MaxAttachmentTileHeight)));
            return new VulkanVariableRateShadingCapabilities(
                perDraw,
                perPrimitive,
                attachment,
                CreateCombiners(in state));
        }

        private static RHICapability CreatePerDraw(
            in VulkanVariableRateShadingProbeState state)
        {
            if (!state.PerDrawSupported)
            {
                return RHICapability.FromProbe(
                    false,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    PipelineFragmentShadingRateSource,
                    "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.pipelineFragmentShadingRate is false.");
            }

            if (!state.RateEnumerationComplete)
            {
                return RHICapability.FromProbe(
                    false,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    VulkanFragmentShadingRateEnumeration.FragmentShadingRatesQuerySource,
                    VulkanFragmentShadingRateEnumeration.UnstableCompleteRateListReason);
            }

            if (!VulkanFragmentShadingRateEnumeration.ContainsRequired1x1Rate(
                state.SupportedShadingRateMask))
            {
                return RHICapability.FromProbe(
                    false,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    VulkanFragmentShadingRateEnumeration.FragmentShadingRatesQuerySource,
                    VulkanFragmentShadingRateEnumeration.MissingRequired1x1RateReason);
            }

            return RHICapability.FromProbe(
                true,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                PipelineFragmentShadingRateSource,
                "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.pipelineFragmentShadingRate is false.",
                new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.SupportedShadingRateMask,
                        state.SupportedShadingRateMask)));
        }

        private static RHICapability CreateCombiners(
            in VulkanVariableRateShadingProbeState state)
        {
            RHICapabilityLimits combinerLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.SupportedShadingRateCombinerMask,
                    (1UL << (byte)ERHIShadingRateCombiner.Passthrough) |
                    (1UL << (byte)ERHIShadingRateCombiner.Override) |
                    (state.NonTrivialCombinerOps
                        ? (1UL << (byte)ERHIShadingRateCombiner.Min) |
                          (1UL << (byte)ERHIShadingRateCombiner.Max)
                        : 0UL)));

            // Primitive fragment shading rate is already lowered by SetShadingRate.
            // No additional SharpGPU primitive-lowering constraint exists today.
            if (state.PerPrimitiveSupported)
            {
                return RHICapability.FromProbe(
                    true,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    PrimitiveFragmentShadingRateSource,
                    "VkPhysicalDeviceFragmentShadingRateFeaturesKHR.primitiveFragmentShadingRate is false.",
                    combinerLimits);
            }

            if (!state.AttachmentHardwareSupported)
            {
                return RHICapability.FromProbe(
                    false,
                    ERHICapabilityTier.Tier1,
                    ERHICapabilityStrategy.NativeExtension,
                    ERHICapabilityProbeKind.NativeExtensionQuery,
                    CombinableFeatureFieldsSource,
                    CombinersBothFeaturesUnsupportedReason);
            }

            return RHICapability.FromProbe(
                false,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.BackendContract,
                RasterLoweringSource,
                CombinersAttachmentLoweringUnboundReason);
        }
    }

    internal static class VulkanVariableRateShadingCommandPolicy
    {
        internal const string PerDrawCapabilityName = "Vulkan variable-rate shading";
        internal const string CombinersCapabilityName =
            "Vulkan variable-rate shading combiners";

        internal static void ValidateSetShadingRate(
            in RHICapability perDraw,
            in RHICapability combiners,
            ERHIShadingRate shadingRate,
            ERHIShadingRateCombiner combiner)
        {
            perDraw.Require(PerDrawCapabilityName);
            if (!perDraw.Limits.TryGetValue(
                    ERHICapabilityLimitKind.SupportedShadingRateMask,
                    out ulong supportedShadingRateMask) ||
                (supportedShadingRateMask & (1UL << (byte)shadingRate)) == 0)
            {
                throw new NotSupportedException(
                    $"Shading rate {shadingRate} is not supported by this Vulkan device.");
            }

            if (combiner != ERHIShadingRateCombiner.Passthrough)
            {
                combiners.Require(CombinersCapabilityName);
                if (!combiners.Limits.TryGetValue(
                        ERHICapabilityLimitKind.SupportedShadingRateCombinerMask,
                        out ulong supportedCombinerMask) ||
                    (supportedCombinerMask & (1UL << (byte)combiner)) == 0)
                {
                    throw new NotSupportedException(
                        $"Shading rate combiner {combiner} is not supported by this Vulkan device.");
                }
            }
        }

        internal static void ResolveCombinerOps(
            in RHICapability perPrimitive,
            in RHICapability attachment,
            ERHIShadingRateCombiner combiner,
            out VkFragmentShadingRateCombinerOpKHR pipelineWithPrimitive,
            out VkFragmentShadingRateCombinerOpKHR resultWithAttachment)
        {
            pipelineWithPrimitive =
                perPrimitive.Tier != ERHICapabilityTier.Unavailable
                    ? VulkanUtility.ConvertToVkShadingRateCombiner(combiner)
                    : VkFragmentShadingRateCombinerOpKHR.Keep;
            resultWithAttachment =
                attachment.Tier != ERHICapabilityTier.Unavailable
                    ? VulkanUtility.ConvertToVkShadingRateCombiner(combiner)
                    : VkFragmentShadingRateCombinerOpKHR.Keep;
        }
    }

    internal static class VulkanMeshCapabilityFactory
    {
        internal const string UnavailableReason =
            "VK_EXT_mesh_shader or meshShader feature is unavailable.";
        internal const string TaskUnavailableReason =
            "VK_EXT_mesh_shader or taskShader feature is unavailable.";
        internal const string ProbeSource =
            "VK_EXT_mesh_shader + VkPhysicalDeviceMeshShaderFeaturesEXT.meshShader plus SharpGPU factory";
        internal const string TaskProbeSource =
            "VK_EXT_mesh_shader + VkPhysicalDeviceMeshShaderFeaturesEXT.taskShader plus SharpGPU factory";
        internal const string CapabilityName = "Vulkan mesh shaders";
        internal const string TaskCapabilityName = "Vulkan task shaders";

        internal static RHICapability CreatePublicMeshShaderCapability(
            bool available,
            RHICapabilityLimits limits = default)
        {
            return RHICapability.FromProbe(
                available,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                ProbeSource,
                UnavailableReason,
                limits);
        }

        internal static RHICapability CreatePublicTaskShaderCapability(bool available)
        {
            return RHICapability.FromProbe(
                available,
                ERHICapabilityTier.Tier1,
                ERHICapabilityStrategy.NativeExtension,
                ERHICapabilityProbeKind.NativeExtensionQuery,
                TaskProbeSource,
                TaskUnavailableReason);
        }

        internal static bool RequestsMeshPath(in RHIRasterPipelineDescriptor descriptor)
        {
            return RHIRasterPipelineContract.RequestsMeshPath(in descriptor);
        }

        internal static void RequireMeshRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor,
            RHICapability meshShader,
            RHICapability taskShader)
        {
            if (!RequestsMeshPath(in descriptor))
            {
                return;
            }

            meshShader.Require(CapabilityName);
            if (descriptor.PrimitiveAssembler.MeshletAssembler is { TaskFunction: not null })
            {
                taskShader.Require(TaskCapabilityName);
            }
        }
    }

    internal static class VulkanMeshCommandPolicy
    {
        internal static void RequireDispatch(RHICapability meshShader)
        {
            meshShader.Require(VulkanMeshCapabilityFactory.CapabilityName);
        }
    }

    internal static class VulkanStorageQueueFactoryPolicy
    {
        internal const string CapabilityName = "Vulkan storage queue creation";

        internal static void RequireNativeGpuFileIo(RHICapability nativeGpuFileIo)
        {
            nativeGpuFileIo.Require(CapabilityName);
        }
    }

    internal static class VulkanQueryFactoryPolicy
    {
        internal static void RequireSupportedQueryType(
            ERHIQueryType queryType,
            RHISynchronizationCapabilities synchronization)
        {
            ArgumentNullException.ThrowIfNull(synchronization);
            switch (queryType)
            {
                case ERHIQueryType.Occlusion:
                    synchronization.OcclusionQueries.Require("Vulkan occlusion queries");
                    break;
                case ERHIQueryType.Statistics:
                    synchronization.PipelineStatisticsQueries.Require(
                        "Vulkan pipeline statistics queries");
                    break;
                case ERHIQueryType.Timestamp:
                case ERHIQueryType.TimestampTransfer:
                    synchronization.TimestampQueries.Require("Vulkan timestamp queries");
                    break;
                case ERHIQueryType.Pending:
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(queryType),
                        queryType,
                        "Vulkan CreateQuery does not accept Pending or unknown query types.");
            }
        }
    }

    internal static class VulkanBarycentricCapabilityFactory
    {
        internal const string UnavailableReason =
            "Fragment shader barycentric coordinates have not been negotiated in the SharpGPU Vulkan device-create chain.";
        internal const string ProbeSource =
            "SharpGPU Vulkan device-create feature negotiation";

        internal static RHICapability CreatePublicCapability()
        {
            return RHICapability.Unavailable(
                UnavailableReason,
                ERHICapabilityProbeKind.BackendContract,
                ProbeSource);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal unsafe delegate VkResult PFN_vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR(
        VkPhysicalDevice physicalDevice,
        uint* propertyCount,
        VkCooperativeMatrixPropertiesKHR* properties);

    internal readonly struct VulkanWaveProbe
    {
        public int MinWavefrontSize { get; init; }
        public int MaxWavefrontSize { get; init; }
        public ERHIWaveOperationStrategy Strategy { get; init; }
        public ERHIStageMask SupportedStages { get; init; }
        public bool WaveOperationsAvailable { get; init; }
        public string WaveOperationsProbeSource { get; init; }
        public string WaveOperationsUnavailableReason { get; init; }
        public bool VariableSubgroupSizeEnabled { get; init; }
        public bool VariableSubgroupSizeAvailable { get; init; }
        public ERHIStageMask RequiredSubgroupSizeStages { get; init; }
        public string VariableSubgroupSizeProbeSource { get; init; }
        public string VariableSubgroupSizeUnavailableReason { get; init; }
        public bool CooperativeMatrixEnabled { get; init; }
        public string CooperativeMatrixProbeSource { get; init; }
        public string CooperativeMatrixUnavailableReason { get; init; }
        public ERHIStageMask CooperativeMatrixStages { get; init; }
        public RHICooperativeMatrixConfig[] Configs { get; init; }

        public static VulkanWaveProbe Unavailable(string reason, string source)
        {
            return new VulkanWaveProbe
            {
                WaveOperationsProbeSource = source,
                WaveOperationsUnavailableReason = reason,
                VariableSubgroupSizeProbeSource = source,
                VariableSubgroupSizeUnavailableReason = reason,
                CooperativeMatrixProbeSource = source,
                CooperativeMatrixUnavailableReason = reason,
                Configs = Array.Empty<RHICooperativeMatrixConfig>(),
            };
        }
    }

    internal static unsafe class VulkanWaveAndCooperativeMatrixNative
    {
        internal const string CooperativeMatrixExtensionName = "VK_KHR_cooperative_matrix";
        internal const string SubgroupSizeControlExtensionName = "VK_EXT_subgroup_size_control";
        internal const string SubgroupPropertiesSource =
            "VkPhysicalDeviceSubgroupProperties.subgroupSize";
        internal const string Vulkan11PropertiesSource =
            "VkPhysicalDeviceVulkan11Properties.subgroupSize";

        internal static VulkanWaveProbe QuerySubgroupSizes(VkPhysicalDevice physicalDevice)
        {
            VkPhysicalDeviceSubgroupProperties subgroup = new()
            {
                sType = VkStructureType.PhysicalDeviceSubgroupProperties,
            };
            VkPhysicalDeviceVulkan11Properties vulkan11 = new()
            {
                sType = VkStructureType.PhysicalDeviceVulkan11Properties,
                pNext = &subgroup,
            };
            VkPhysicalDeviceProperties2 properties2 = new()
            {
                sType = VkStructureType.PhysicalDeviceProperties2,
                pNext = &vulkan11,
            };
            VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);

            uint subgroupSize = subgroup.subgroupSize != 0
                ? subgroup.subgroupSize
                : vulkan11.subgroupSize;
            VkShaderStageFlags stages = subgroup.supportedStages != 0
                ? subgroup.supportedStages
                : vulkan11.subgroupSupportedStages;
            VkSubgroupFeatureFlags operations = subgroup.supportedOperations != 0
                ? subgroup.supportedOperations
                : vulkan11.subgroupSupportedOperations;
            string source = subgroup.subgroupSize != 0
                ? SubgroupPropertiesSource
                : Vulkan11PropertiesSource;
            bool available = subgroupSize > 0 && operations != 0;
            return new VulkanWaveProbe
            {
                MinWavefrontSize = available ? (int)subgroupSize : 0,
                MaxWavefrontSize = available ? (int)subgroupSize : 0,
                Strategy = available
                    ? MapWaveOperations(operations)
                    : ERHIWaveOperationStrategy.None,
                SupportedStages = MapShaderStages(stages),
                WaveOperationsAvailable = available,
                WaveOperationsProbeSource = source,
                WaveOperationsUnavailableReason = available
                    ? string.Empty
                    : "VkPhysicalDeviceSubgroupProperties.subgroupSize is 0 or supportedOperations is empty.",
                VariableSubgroupSizeProbeSource = SubgroupSizeControlExtensionName,
                VariableSubgroupSizeUnavailableReason =
                    "VK_EXT_subgroup_size_control is not enabled and Vulkan 1.3 subgroupSizeControl is not enabled.",
                CooperativeMatrixProbeSource = CooperativeMatrixExtensionName,
                CooperativeMatrixUnavailableReason =
                    "VK_KHR_cooperative_matrix is not listed.",
                Configs = Array.Empty<RHICooperativeMatrixConfig>(),
            };
        }

        internal static VulkanWaveProbe Refine(
            VulkanWaveProbe baseline,
            VkPhysicalDevice physicalDevice,
            VulkanInstance instance,
            bool sizeControlEnabled,
            bool sizeControlUsesVulkan13Core,
            bool cooperativeMatrixEnabled,
            string? cooperativeMatrixUnavailableReason = null)
        {
            int minSize = baseline.MinWavefrontSize;
            int maxSize = baseline.MaxWavefrontSize;
            ERHIStageMask requiredStages = ERHIStageMask.None;
            string waveSource = baseline.WaveOperationsProbeSource;
            string variableSource = baseline.VariableSubgroupSizeProbeSource;
            string variableReason = baseline.VariableSubgroupSizeUnavailableReason;
            bool variableAvailable = false;

            if (sizeControlEnabled)
            {
                uint minSubgroupSize;
                uint maxSubgroupSize;
                VkShaderStageFlags requiredStageFlags;
                if (sizeControlUsesVulkan13Core)
                {
                    VkPhysicalDeviceVulkan13Properties vulkan13 = new()
                    {
                        sType = VkStructureType.PhysicalDeviceVulkan13Properties,
                    };
                    VkPhysicalDeviceProperties2 properties2 = new()
                    {
                        sType = VkStructureType.PhysicalDeviceProperties2,
                        pNext = &vulkan13,
                    };
                    VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);
                    minSubgroupSize = vulkan13.minSubgroupSize;
                    maxSubgroupSize = vulkan13.maxSubgroupSize;
                    requiredStageFlags = vulkan13.requiredSubgroupSizeStages;
                    variableSource =
                        "VkPhysicalDeviceVulkan13Properties.minSubgroupSize / maxSubgroupSize / requiredSubgroupSizeStages";
                }
                else
                {
                    VkPhysicalDeviceSubgroupSizeControlProperties sizeControl = new()
                    {
                        sType = VkStructureType.PhysicalDeviceSubgroupSizeControlProperties,
                    };
                    VkPhysicalDeviceProperties2 properties2 = new()
                    {
                        sType = VkStructureType.PhysicalDeviceProperties2,
                        pNext = &sizeControl,
                    };
                    VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);
                    minSubgroupSize = sizeControl.minSubgroupSize;
                    maxSubgroupSize = sizeControl.maxSubgroupSize;
                    requiredStageFlags = sizeControl.requiredSubgroupSizeStages;
                    variableSource =
                        "VkPhysicalDeviceSubgroupSizeControlProperties.minSubgroupSize / maxSubgroupSize / requiredSubgroupSizeStages";
                }

                requiredStages = MapShaderStages(requiredStageFlags);
                if (minSubgroupSize > 0 && maxSubgroupSize >= minSubgroupSize)
                {
                    minSize = (int)minSubgroupSize;
                    maxSize = (int)maxSubgroupSize;
                    waveSource =
                        $"{baseline.WaveOperationsProbeSource}; {variableSource}";
                }

                variableAvailable =
                    minSubgroupSize != maxSubgroupSize || requiredStageFlags != 0;
                variableReason = variableAvailable
                    ? string.Empty
                    : "Subgroup size control is enabled but minSubgroupSize equals maxSubgroupSize and requiredSubgroupSizeStages is 0.";
            }

            RHICooperativeMatrixConfig[] configs = Array.Empty<RHICooperativeMatrixConfig>();
            ERHIStageMask coopStages = ERHIStageMask.None;
            string coopSource = CooperativeMatrixExtensionName;
            string coopReason = cooperativeMatrixUnavailableReason
                ?? baseline.CooperativeMatrixUnavailableReason;
            bool coopEnabled = cooperativeMatrixEnabled;
            if (cooperativeMatrixEnabled)
            {
                VkPhysicalDeviceCooperativeMatrixPropertiesKHR coopProperties = new()
                {
                    sType = VkStructureType.PhysicalDeviceCooperativeMatrixPropertiesKHR,
                };
                VkPhysicalDeviceProperties2 properties2 = new()
                {
                    sType = VkStructureType.PhysicalDeviceProperties2,
                    pNext = &coopProperties,
                };
                VulkanNative.vkGetPhysicalDeviceProperties2(physicalDevice, &properties2);
                coopStages = MapShaderStages(coopProperties.cooperativeMatrixSupportedStages);
                coopSource =
                    "VK_KHR_cooperative_matrix + vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR";
                if (!TryEnumerateCooperativeMatrixConfigs(
                        physicalDevice,
                        instance,
                        out configs,
                        out string enumerateReason))
                {
                    coopEnabled = false;
                    coopReason = enumerateReason;
                    configs = Array.Empty<RHICooperativeMatrixConfig>();
                }
                else
                {
                    coopReason = string.Empty;
                }
            }
            else if (string.IsNullOrWhiteSpace(coopReason))
            {
                coopReason = "VK_KHR_cooperative_matrix is not enabled.";
            }

            return new VulkanWaveProbe
            {
                MinWavefrontSize = minSize,
                MaxWavefrontSize = maxSize,
                Strategy = baseline.Strategy,
                SupportedStages = baseline.SupportedStages,
                WaveOperationsAvailable = baseline.WaveOperationsAvailable,
                WaveOperationsProbeSource = waveSource,
                WaveOperationsUnavailableReason = baseline.WaveOperationsUnavailableReason,
                VariableSubgroupSizeEnabled = sizeControlEnabled,
                VariableSubgroupSizeAvailable = variableAvailable,
                RequiredSubgroupSizeStages = requiredStages,
                VariableSubgroupSizeProbeSource = variableSource,
                VariableSubgroupSizeUnavailableReason = variableReason,
                CooperativeMatrixEnabled = coopEnabled,
                CooperativeMatrixProbeSource = coopSource,
                CooperativeMatrixUnavailableReason = coopReason,
                CooperativeMatrixStages = coopStages,
                Configs = configs,
            };
        }

        internal static RHICapabilityLimits CreateWaveLimits(
            VulkanWaveProbe probe,
            ulong maxComputeThreads,
            ulong maxGroupSharedMemoryBytes)
        {
            if (!probe.WaveOperationsAvailable)
            {
                return new RHICapabilityLimits(
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.MaximumComputeThreads,
                        maxComputeThreads),
                    new RHICapabilityLimit(
                        ERHICapabilityLimitKind.MaximumGroupSharedMemoryBytes,
                        maxGroupSharedMemoryBytes));
            }

            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MinimumWavefrontSize,
                    (ulong)probe.MinWavefrontSize),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumWavefrontSize,
                    (ulong)probe.MaxWavefrontSize),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.WaveStageMask,
                    (ulong)probe.SupportedStages),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumComputeThreads,
                    maxComputeThreads),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumGroupSharedMemoryBytes,
                    maxGroupSharedMemoryBytes));
        }

        internal static RHICapabilityLimits CreateVariableSubgroupSizeLimits(
            VulkanWaveProbe probe)
        {
            if (!probe.VariableSubgroupSizeAvailable)
            {
                return RHICapabilityLimits.Empty;
            }

            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MinimumWavefrontSize,
                    (ulong)probe.MinWavefrontSize),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.MaximumWavefrontSize,
                    (ulong)probe.MaxWavefrontSize),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.WaveStageMask,
                    (ulong)probe.RequiredSubgroupSizeStages));
        }

        internal static RHICapabilityLimits CreateCooperativeMatrixLimits(
            VulkanWaveProbe probe)
        {
            if (!probe.CooperativeMatrixEnabled)
            {
                return RHICapabilityLimits.Empty;
            }

            return new RHICapabilityLimits(
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.CooperativeMatrixConfigCount,
                    (ulong)probe.Configs.Length),
                new RHICapabilityLimit(
                    ERHICapabilityLimitKind.WaveStageMask,
                    (ulong)probe.CooperativeMatrixStages));
        }

        private static bool TryEnumerateCooperativeMatrixConfigs(
            VkPhysicalDevice physicalDevice,
            VulkanInstance instance,
            out RHICooperativeMatrixConfig[] configs,
            out string reason)
        {
            configs = Array.Empty<RHICooperativeMatrixConfig>();
            IntPtr function = instance.TryGetInstanceProcedure(
                "vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR");
            if (function == IntPtr.Zero)
            {
                reason =
                    "vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR is not exported by this instance.";
                return false;
            }

            PFN_vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR getProperties =
                Marshal.GetDelegateForFunctionPointer<
                    PFN_vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR>(function);
            uint count = 0;
            VkResult countResult = getProperties(physicalDevice, &count, null);
            if (countResult != VkResult.Success)
            {
                reason =
                    $"vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR count query failed: {countResult}.";
                return false;
            }

            if (count == 0)
            {
                reason = string.Empty;
                return true;
            }

            VkCooperativeMatrixPropertiesKHR* native =
                stackalloc VkCooperativeMatrixPropertiesKHR[(int)count];
            for (int i = 0; i < count; ++i)
            {
                native[i].sType = VkStructureType.CooperativeMatrixPropertiesKHR;
            }

            VkResult listResult = getProperties(physicalDevice, &count, native);
            if (listResult != VkResult.Success)
            {
                reason =
                    $"vkGetPhysicalDeviceCooperativeMatrixPropertiesKHR enumeration failed: {listResult}.";
                return false;
            }

            RHICooperativeMatrixConfig[] mapped = new RHICooperativeMatrixConfig[count];
            for (int i = 0; i < count; ++i)
            {
                mapped[i] = new RHICooperativeMatrixConfig(
                    native[i].MSize,
                    native[i].NSize,
                    native[i].KSize,
                    MapComponentType(native[i].AType),
                    MapComponentType(native[i].BType),
                    MapComponentType(native[i].CType),
                    MapComponentType(native[i].ResultType),
                    MapScope(native[i].scope),
                    native[i].saturatingAccumulation);
            }

            configs = mapped;
            reason = string.Empty;
            return true;
        }

        private static ERHIWaveOperationStrategy MapWaveOperations(
            VkSubgroupFeatureFlags operations)
        {
            ERHIWaveOperationStrategy strategy = ERHIWaveOperationStrategy.None;
            if ((operations & VkSubgroupFeatureFlags.Basic) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Basic;
            }
            if ((operations & VkSubgroupFeatureFlags.Vote) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Vote;
            }
            if ((operations & VkSubgroupFeatureFlags.Arithmetic) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Arithmetic;
            }
            if ((operations & VkSubgroupFeatureFlags.Ballot) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Ballot;
            }
            if ((operations & VkSubgroupFeatureFlags.Shuffle) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Shuffle;
            }
            if ((operations & VkSubgroupFeatureFlags.ShuffleRelative) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.ShuffleRelative;
            }
            if ((operations & VkSubgroupFeatureFlags.Clustered) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Clustered;
            }
            if ((operations & VkSubgroupFeatureFlags.Quad) != 0)
            {
                strategy |= ERHIWaveOperationStrategy.Quad;
            }

            return strategy == ERHIWaveOperationStrategy.None
                ? ERHIWaveOperationStrategy.Basic
                : strategy;
        }

        internal static ERHIStageMask MapShaderStages(VkShaderStageFlags stages)
        {
            ERHIStageMask mask = ERHIStageMask.None;
            if ((stages & VkShaderStageFlags.Vertex) != 0)
            {
                mask |= ERHIStageMask.Vertex;
            }
            if ((stages & VkShaderStageFlags.Fragment) != 0)
            {
                mask |= ERHIStageMask.Fragment;
            }
            if ((stages & VkShaderStageFlags.Compute) != 0)
            {
                mask |= ERHIStageMask.Compute;
            }
            if ((stages & VkShaderStageFlags.TaskEXT) != 0)
            {
                mask |= ERHIStageMask.Task;
            }
            if ((stages & VkShaderStageFlags.MeshEXT) != 0)
            {
                mask |= ERHIStageMask.Mesh;
            }
            if ((stages & (
                    VkShaderStageFlags.RaygenKHR |
                    VkShaderStageFlags.MissKHR |
                    VkShaderStageFlags.ClosestHitKHR |
                    VkShaderStageFlags.AnyHitKHR |
                    VkShaderStageFlags.IntersectionKHR |
                    VkShaderStageFlags.CallableKHR)) != 0)
            {
                mask |= ERHIStageMask.RayTracing;
            }

            return mask;
        }

        private static ERHICooperativeMatrixElementType MapComponentType(
            VkComponentTypeKHR type)
        {
            return type switch
            {
                VkComponentTypeKHR.Float16 => ERHICooperativeMatrixElementType.Float16,
                VkComponentTypeKHR.Float32 => ERHICooperativeMatrixElementType.Float32,
                VkComponentTypeKHR.Float64 => ERHICooperativeMatrixElementType.Float64,
                VkComponentTypeKHR.Sint8 => ERHICooperativeMatrixElementType.SInt8,
                VkComponentTypeKHR.Sint16 => ERHICooperativeMatrixElementType.SInt16,
                VkComponentTypeKHR.Sint32 => ERHICooperativeMatrixElementType.SInt32,
                VkComponentTypeKHR.Sint64 => ERHICooperativeMatrixElementType.SInt64,
                VkComponentTypeKHR.Uint8 => ERHICooperativeMatrixElementType.UInt8,
                VkComponentTypeKHR.Uint16 => ERHICooperativeMatrixElementType.UInt16,
                VkComponentTypeKHR.Uint32 => ERHICooperativeMatrixElementType.UInt32,
                VkComponentTypeKHR.Uint64 => ERHICooperativeMatrixElementType.UInt64,
                _ => MapExtendedComponentType(type),
            };
        }

        private static ERHICooperativeMatrixElementType MapExtendedComponentType(
            VkComponentTypeKHR type)
        {
            string name = type.ToString();
            if (name.Contains("BFloat16", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Bfloat16", StringComparison.OrdinalIgnoreCase))
            {
                return ERHICooperativeMatrixElementType.BFloat16;
            }

            if (name.Contains("E4M3", StringComparison.OrdinalIgnoreCase))
            {
                return ERHICooperativeMatrixElementType.Float8E4M3;
            }

            if (name.Contains("E5M2", StringComparison.OrdinalIgnoreCase))
            {
                return ERHICooperativeMatrixElementType.Float8E5M2;
            }

            return ERHICooperativeMatrixElementType.Unknown;
        }

        private static ERHICooperativeMatrixScope MapScope(VkScopeKHR scope)
        {
            return scope switch
            {
                VkScopeKHR.Subgroup => ERHICooperativeMatrixScope.Subgroup,
                VkScopeKHR.Workgroup => ERHICooperativeMatrixScope.Workgroup,
                VkScopeKHR.Device => ERHICooperativeMatrixScope.Device,
                VkScopeKHR.QueueFamily => ERHICooperativeMatrixScope.QueueFamily,
                _ => ERHICooperativeMatrixScope.Unknown,
            };
        }
    }
}

