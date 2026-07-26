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
        internal bool SupportsAttachmentFeedbackLoopLayout =>
            m_AttachmentFeedbackLoopLayoutSupported;
        internal bool SupportsFragmentShaderPixelInterlock => m_FragmentShaderPixelInterlockSupported;
        internal bool SupportsFragmentStoresAndAtomics =>
            m_FragmentStoresAndAtomicsSupported;
        internal bool SupportsUnifiedImageLayouts => m_UnifiedImageLayoutsSupported;
        internal EVulkanFeatureProvenance AttachmentFeedbackLoopLayoutProvenance => m_AttachmentFeedbackLoopLayoutProvenance;
        internal EVulkanFeatureProvenance FragmentShaderPixelInterlockProvenance => m_FragmentShaderPixelInterlockProvenance;
        internal EVulkanFeatureProvenance UnifiedImageLayoutsProvenance => m_UnifiedImageLayoutsProvenance;
        internal EVulkanFeatureProvenance DynamicRenderingLocalReadProvenance => m_DynamicRenderingLocalReadProvenance;
        internal EVulkanFeatureProvenance RenderPass2Provenance => m_RenderPass2Provenance;
        internal VulkanRasterCapabilities RasterCapabilities =>
            new VulkanRasterCapabilities(
                m_DynamicRenderingSupported,
                m_DynamicRenderingLocalReadSupported,
                m_DynamicRenderingLocalReadDepthStencilAttachments,
                m_DynamicRenderingLocalReadMultisampledAttachments,
                m_RenderPass2Supported,
                m_AttachmentFeedbackLoopLayoutSupported,
                m_FragmentShaderPixelInterlockSupported,
                m_FragmentStoresAndAtomicsSupported,
                m_UnifiedImageLayoutsSupported);
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
        private bool m_RaytracingInlineSupported;
        private bool m_MeshShadingSupported;
        private bool m_VariableRateShadingSupported;
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
        private bool m_AttachmentFeedbackLoopLayoutSupported;
        private bool m_FragmentShaderPixelInterlockSupported;
        private bool m_FragmentStoresAndAtomicsSupported;
        private bool m_UnifiedImageLayoutsSupported;
        private EVulkanFeatureProvenance m_AttachmentFeedbackLoopLayoutProvenance;
        private EVulkanFeatureProvenance m_FragmentShaderPixelInterlockProvenance;
        private EVulkanFeatureProvenance m_UnifiedImageLayoutsProvenance;
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
        private bool m_MemoryBudgetSupported;
        private bool m_SwapchainSupported;
        private bool m_SwapchainMaintenanceSupported;

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

            m_Limit = new RHIDeviceLimit(
                uniformBufferAlignment: (int)limits.minUniformBufferOffsetAlignment,
                uploadBufferAlignment: 256,
                uploadBufferTextureAlignment: 256,
                uploadBufferTextureRowAlignment: 256,
                maxMSAACount: 8,
                maxBoundTexture: (int)limits.maxDescriptorSetSampledImages,
                minWavefrontSize: (int)limits.minStorageBufferOffsetAlignment > 0 ? 32 : 32,
                maxWavefrontSize: 64,
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
            bool hasMeshShader = availableExtNames.Contains("VK_EXT_mesh_shader");
            bool hasFragmentShadingRate = availableExtNames.Contains("VK_KHR_fragment_shading_rate");
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
            bool hasAttachmentFeedbackLoopLayoutExtension =
                availableExtNames.Contains("VK_EXT_attachment_feedback_loop_layout");
            bool hasFragmentShaderInterlockExtension =
                availableExtNames.Contains("VK_EXT_fragment_shader_interlock");
            bool hasUnifiedImageLayoutsExtension =
                availableExtNames.Contains("VK_KHR_unified_image_layouts");
            bool hasMemoryBudgetExtension =
                availableExtNames.Contains(VulkanMemoryBudgetUtility.ExtensionName);
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
                hasDynamicRenderingLocalReadExtension,
                hasAttachmentFeedbackLoopLayoutExtension,
                hasFragmentShaderInterlockExtension,
                hasUnifiedImageLayoutsExtension);
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

            VkPhysicalDeviceAttachmentFeedbackLoopLayoutFeaturesEXT attachmentFeedbackLoopLayoutFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceAttachmentFeedbackLoopLayoutFeaturesEXT,
            };
            VkPhysicalDeviceFragmentShaderInterlockFeaturesEXT fragmentShaderInterlockFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceFragmentShaderInterlockFeaturesEXT,
            };
            VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR unifiedImageLayoutsFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceUnifiedImageLayoutsFeaturesKHR,
            };
            VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR swapchainMaintenanceFeaturesQuery = new()
            {
                sType = VkStructureType.PhysicalDeviceSwapchainMaintenance1FeaturesKHR,
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

            if (hasAttachmentFeedbackLoopLayoutExtension)
            {
                attachmentFeedbackLoopLayoutFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &attachmentFeedbackLoopLayoutFeaturesQuery;
            }
            if (hasFragmentShaderInterlockExtension)
            {
                fragmentShaderInterlockFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &fragmentShaderInterlockFeaturesQuery;
            }
            if (hasUnifiedImageLayoutsExtension)
            {
                unifiedImageLayoutsFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &unifiedImageLayoutsFeaturesQuery;
            }
            if (swapchainMaintenanceExtension != null)
            {
                swapchainMaintenanceFeaturesQuery.pNext = featureQueryChain;
                featureQueryChain = &swapchainMaintenanceFeaturesQuery;
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
            bool attachmentFeedbackLoopLayoutFeatureSupported =
                hasAttachmentFeedbackLoopLayoutExtension &&
                attachmentFeedbackLoopLayoutFeaturesQuery.attachmentFeedbackLoopLayout;
            bool fragmentShaderPixelInterlockFeatureSupported =
                hasFragmentShaderInterlockExtension &&
                fragmentShaderInterlockFeaturesQuery
                    .fragmentShaderPixelInterlock;
            bool fragmentStoresAndAtomicsSupported =
                supportedCoreFeatures.fragmentStoresAndAtomics;
            bool unifiedImageLayoutsFeatureSupported =
                hasUnifiedImageLayoutsExtension &&
                unifiedImageLayoutsFeaturesQuery.unifiedImageLayouts;
            bool swapchainMaintenanceFeatureSupported =
                hasSwapchainExtension &&
                swapchainMaintenanceExtension != null &&
                swapchainMaintenanceFeaturesQuery.swapchainMaintenance1;
            bool accelFeatureSupported = hasAccelStruct && accelFeaturesQuery.accelerationStructure;
            bool rtPipelineFeatureSupported = hasRTPipeline && rtPipelineFeaturesQuery.rayTracingPipeline;
            bool rtQueryFeatureSupported = hasRTQuery && rtQueryFeaturesQuery.rayQuery;
            bool meshShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.meshShader;
            bool taskShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.taskShader;
            bool fragmentShadingRateFeatureSupported = hasFragmentShadingRate && vrsFeaturesQuery.pipelineFragmentShadingRate;
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
            if (attachmentFeedbackLoopLayoutFeatureSupported)
            {
                deviceExtensions.Add("VK_EXT_attachment_feedback_loop_layout");
            }
            if (fragmentShaderPixelInterlockFeatureSupported)
            {
                deviceExtensions.Add("VK_EXT_fragment_shader_interlock");
            }
            if (unifiedImageLayoutsFeatureSupported)
            {
                deviceExtensions.Add("VK_KHR_unified_image_layouts");
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

            if (hasMemoryBudgetExtension)
            {
                deviceExtensions.Add(VulkanMemoryBudgetUtility.ExtensionName);
            }
            if (swapchainMaintenanceFeatureSupported)
            {
                deviceExtensions.Add(swapchainMaintenanceExtension!);
            }

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

            VkPhysicalDeviceAttachmentFeedbackLoopLayoutFeaturesEXT attachmentFeedbackLoopLayoutFeatures = new()
            {
                sType = VkStructureType.PhysicalDeviceAttachmentFeedbackLoopLayoutFeaturesEXT,
                attachmentFeedbackLoopLayout = attachmentFeedbackLoopLayoutFeatureSupported,
            };
            VkPhysicalDeviceFragmentShaderInterlockFeaturesEXT fragmentShaderInterlockFeatures = new()
            {
                sType = VkStructureType.PhysicalDeviceFragmentShaderInterlockFeaturesEXT,
                fragmentShaderPixelInterlock = fragmentShaderPixelInterlockFeatureSupported,
            };
            VkPhysicalDeviceUnifiedImageLayoutsFeaturesKHR unifiedImageLayoutsFeatures = new()
            {
                sType = VkStructureType.PhysicalDeviceUnifiedImageLayoutsFeaturesKHR,
                unifiedImageLayouts = unifiedImageLayoutsFeatureSupported,
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
            if (attachmentFeedbackLoopLayoutFeatureSupported)
            {
                attachmentFeedbackLoopLayoutFeatures.pNext = pNextChain;
                pNextChain = &attachmentFeedbackLoopLayoutFeatures;
            }
            if (fragmentShaderPixelInterlockFeatureSupported)
            {
                fragmentShaderInterlockFeatures.pNext = pNextChain;
                pNextChain = &fragmentShaderInterlockFeatures;
            }
            if (unifiedImageLayoutsFeatureSupported)
            {
                unifiedImageLayoutsFeatures.pNext = pNextChain;
                pNextChain = &unifiedImageLayoutsFeatures;
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
                vrsFeatures.pipelineFragmentShadingRate = true;
                vrsFeatures.pNext = pNextChain;
                pNextChain = &vrsFeatures;
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
            m_MeshShadingSupported = meshSupported;
            m_VariableRateShadingSupported = vrsSupported;
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
            m_RenderPass2Provenance =
                featureChainPlan.RenderPass2Provenance;
            m_AttachmentFeedbackLoopLayoutSupported =
                attachmentFeedbackLoopLayoutFeatureSupported;
            m_FragmentShaderPixelInterlockSupported =
                fragmentShaderPixelInterlockFeatureSupported;
            m_FragmentStoresAndAtomicsSupported =
                fragmentStoresAndAtomicsSupported;
            m_UnifiedImageLayoutsSupported =
                unifiedImageLayoutsFeatureSupported;
            m_AttachmentFeedbackLoopLayoutProvenance =
                featureChainPlan.AttachmentFeedbackLoopLayoutQueryProvenance;
            m_FragmentShaderPixelInterlockProvenance =
                featureChainPlan.FragmentShaderPixelInterlockQueryProvenance;
            m_UnifiedImageLayoutsProvenance =
                featureChainPlan.UnifiedImageLayoutsQueryProvenance;
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
            m_SparseBindingSupported =
                sparseBindingSupported &&
                (m_SparseResidencyImage2DSupported ||
                 m_SparseResidencyImage3DSupported);
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

        private void UpdateDeviceFeatures()
        {
            // Re-query features to read actual device capabilities
            VkPhysicalDeviceProperties properties;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &properties);
            VkPhysicalDeviceFeatures features;
            VulkanNative.vkGetPhysicalDeviceFeatures(m_PhysicalDevice, &features);
            VkPhysicalDeviceLimits limits = properties.limits;

            bool hasAtomicInt64 = m_ShaderAtomicInt64Supported;
            // Barycentric support is fail-closed until its extension and feature are
            // negotiated in the device-create chain.
            bool hasBarycentrics = false;
            bool hasDynamicRenderingLocalReadRoute =
                VulkanRasterCapabilityUtility
                    .HasFramebufferLocalReadLoweringRoute(
                        m_DynamicRenderingSupported,
                        m_DynamicRenderingLocalReadSupported,
                        m_RenderPass2Supported);
            bool hasSampledFeedbackLoweringRoute =
                VulkanRasterCapabilityUtility
                    .HasSampledFeedbackLoweringRoute(
                        m_DynamicRenderingSupported,
                        m_RenderPass2Supported);            static RHICapability Probe(
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
            RHICapabilityLimits computeLimits = new RHICapabilityLimits(
                new RHICapabilityLimit(ERHICapabilityLimitKind.MinimumWavefrontSize, 32),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumWavefrontSize, 64),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumComputeThreads, limits.maxComputeWorkGroupInvocations),
                new RHICapabilityLimit(ERHICapabilityLimitKind.MaximumGroupSharedMemoryBytes, limits.maxComputeSharedMemorySize));

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
                    rasterOrderedAccess: Probe(
                        m_DynamicRenderingSupported &&
                            features.fragmentStoresAndAtomics &&
                            m_FragmentShaderPixelInterlockSupported &&
                            m_AttachmentFeedbackLoopLayoutSupported &&
                            m_UnifiedImageLayoutsSupported,
                        "dynamicRendering + VkPhysicalDeviceFeatures.fragmentStoresAndAtomics + VK_EXT_fragment_shader_interlock/fragmentShaderPixelInterlock + VK_EXT_attachment_feedback_loop_layout/attachmentFeedbackLoopLayout + VK_KHR_unified_image_layouts/unifiedImageLayouts",
                        !m_DynamicRenderingSupported
                            ? "Dynamic rendering is unavailable."
                            : !features.fragmentStoresAndAtomics
                            ? "VkPhysicalDeviceFeatures.fragmentStoresAndAtomics is false."
                            : !m_FragmentShaderPixelInterlockSupported
                                ? "VK_EXT_fragment_shader_interlock or fragmentShaderPixelInterlock is unavailable."
                                : !m_AttachmentFeedbackLoopLayoutSupported
                                    ? "VK_EXT_attachment_feedback_loop_layout or attachmentFeedbackLoopLayout is unavailable."
                                    : "VK_KHR_unified_image_layouts or unifiedImageLayouts is unavailable.",
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
                    sampledFeedback: Probe(
                        m_AttachmentFeedbackLoopLayoutSupported &&
                            hasSampledFeedbackLoweringRoute,
                        m_AttachmentFeedbackLoopLayoutSupported
                            ? m_DynamicRenderingSupported
                                ? "VK_EXT_attachment_feedback_loop_layout extension + attachmentFeedbackLoopLayout feature + dynamic rendering lowering; color-attachment SampledFeedback ABI only"
                                : "VK_EXT_attachment_feedback_loop_layout extension + attachmentFeedbackLoopLayout feature + RenderPass2 lowering; color-attachment SampledFeedback ABI only"
                            : "VK_EXT_attachment_feedback_loop_layout attachmentFeedbackLoopLayout feature query",
                        !m_AttachmentFeedbackLoopLayoutSupported
                            ? m_AttachmentFeedbackLoopLayoutProvenance ==
                                EVulkanFeatureProvenance.Unavailable
                                ? "VK_EXT_attachment_feedback_loop_layout is not exposed by this device."
                                : "VK_EXT_attachment_feedback_loop_layout is exposed, but attachmentFeedbackLoopLayout is false."
                            : "attachmentFeedbackLoopLayout is available, but neither dynamic rendering nor RenderPass2 can lower SampledFeedback.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery,
                        capabilityLimits: rasterLimits),
                    drawIndirect: Probe(
                        features.drawIndirectFirstInstance,
                        "VkPhysicalDeviceFeatures.drawIndirectFirstInstance",
                        "Indirect drawing is unavailable."),
                    multiDrawIndirect: Probe(
                        features.multiDrawIndirect,
                        "VkPhysicalDeviceFeatures.multiDrawIndirect",
                        "Multi-draw indirect is unavailable."),
                    variableRateShading: Probe(
                        m_VariableRateShadingSupported,
                        "VK_KHR_fragment_shading_rate feature query",
                        "Fragment shading rate is unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    hiddenSurfaceRemoval: Probe(
                        false,
                        "SharpGPU Vulkan raster lowering",
                        "Hidden-surface removal is renderer policy and is not a Vulkan HAL capability."),
                    barycentricCoordinates: Probe(
                        hasBarycentrics,
                        "VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR.fragmentShaderBarycentric",
                        "Fragment shader barycentric coordinates are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    programmableSamplePositions: Probe(
                        false,
                        "Vulkan programmable sample-location feature query",
                        "Programmable sample locations are not enabled by this Vulkan device."),
                    nativeRenderPass: Probe(
                        m_RenderPass2Supported,
                        m_UseRenderPass2KhrCommands ? "VK_KHR_create_renderpass2 extension" : "Vulkan 1.2 RenderPass2 core",
                        "Vulkan RenderPass2 is unavailable.",
                        strategy: m_UseRenderPass2KhrCommands ? ERHICapabilityStrategy.NativeExtension : ERHICapabilityStrategy.CoreApi,
                        capabilityLimits: rasterLimits)),
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
                            : ERHICapabilityProbeKind.ApiVersion)),
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
                    sparseBinding: Probe(
                        m_SparseBindingSupported,
                        "enabled VkPhysicalDeviceFeatures sparseBinding/sparseResidencyImage* + sparse-capable created queue",
                        "Vulkan sparse image residency or a sparse-capable created queue is unavailable.",
                        strategy: ERHICapabilityStrategy.CoreApi),
                    residency: Probe(
                        false,
                        "SharpGPU Vulkan residency lowering",
                        "Explicit residency requests are not implemented."),
                    budgetQuery: VulkanMemoryBudgetUtility.CreateCapability(
                        m_MemoryBudgetSupported,
                        m_MemoryProperties.memoryHeapCount)),
                storage: new RHIStorageCapabilities(
                    nativeGpuFileIo: RHICapability.Unavailable(
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
                        m_SwapchainSupported,
                        "VK_KHR_swapchain device-extension enumeration",
                        "The Vulkan device does not expose VK_KHR_swapchain.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    acquireSignal: Probe(
                        m_SwapchainSupported,
                        "VK_KHR_swapchain vkAcquireNextImageKHR semaphore/fence contract",
                        "Vulkan acquisition synchronization requires VK_KHR_swapchain.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    presentWait: Probe(
                        m_SwapchainSupported,
                        "VK_KHR_swapchain VkPresentInfoKHR wait-semaphore contract",
                        "Vulkan presentation waits require VK_KHR_swapchain.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    presentCompletion: Probe(
                        m_SwapchainSupported &&
                            m_SwapchainMaintenanceSupported,
                        "VkPhysicalDeviceSwapchainMaintenance1FeaturesKHR.swapchainMaintenance1 + VkSwapchainPresentFenceInfoKHR",
                        "Vulkan present completion fences require VK_KHR_swapchain plus VK_KHR/EXT_swapchain_maintenance1.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery),
                    maintenance: Probe(
                        m_SwapchainSupported,
                        m_SwapchainMaintenanceSupported
                            ? "VkSwapchainPresentFenceInfoKHR caller-drained present completion"
                            : "VK_KHR_swapchain plus explicit caller vkQueueWaitIdle lifecycle drain",
                        "Vulkan presentation maintenance requires VK_KHR_swapchain.",
                        tier: ERHICapabilityTier.Tier1,
                        strategy:
                            m_SwapchainMaintenanceSupported
                                ? ERHICapabilityStrategy.NativeExtension
                                : ERHICapabilityStrategy.CoreApi,
                        probeKind:
                            m_SwapchainMaintenanceSupported
                                ? ERHICapabilityProbeKind.NativeExtensionQuery
                                : ERHICapabilityProbeKind.BackendContract),
                    maintenanceStrategy:
                        !m_SwapchainSupported
                            ? ERHIPresentationMaintenanceStrategy.Unavailable
                            : m_SwapchainMaintenanceSupported
                                ? ERHIPresentationMaintenanceStrategy.PresentFence
                                : ERHIPresentationMaintenanceStrategy.QueueIdle,
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
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery)),
                mesh: new RHIMeshCapabilities(
                    shader: Probe(
                        m_MeshShadingSupported,
                        "VK_EXT_mesh_shader feature and extension set plus SharpGPU factory",
                        "Mesh shaders are unavailable.",
                        strategy: ERHICapabilityStrategy.NativeExtension,
                        probeKind: ERHICapabilityProbeKind.NativeExtensionQuery)),
                machineLearning: new RHIMachineLearningCapabilities(
                    execution: RHICapability.Unavailable(
                        "Vulkan ML requires a supported native tensor/data-graph contract.",
                        ERHICapabilityProbeKind.NativeExtensionQuery,
                        "Vulkan ML extension set")),
                workGraph: new RHIWorkGraphCapabilities(
                    execution: RHICapability.Unavailable(
                        "Vulkan Work Graph execution is not exposed by SharpGPU.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan factory surface")),
                indirectCommandBuffer: new RHIIndirectCommandBufferCapabilities(
                    execution: RHICapability.Unavailable(
                        "Vulkan device-generated / NV indirect commands are not exposed by SharpGPU.",
                        ERHICapabilityProbeKind.BackendContract,
                        "SharpGPU Vulkan ICB factory surface")),
                compute: new RHIComputeCapabilities(
                    ERHIWaveOperationStrategy.Basic,
                    waveOperations: RHICapability.Available(
                        ERHICapabilityTier.Tier1,
                        ERHICapabilityStrategy.CoreApi,
                        ERHICapabilityProbeKind.ApiVersion,
                        "Vulkan subgroup operations",
                        computeLimits)));
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

        public override RHIStorageQueue CreateStorageQueue()
        {
            ThrowIfDeviceUnavailable();
            return new VulkanStorageQueue(this);
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
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
            return new VulkanTexture(this, descriptor);
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
            Capabilities.Memory.SparseBinding.Require(
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
            Capabilities.Memory.SparseBinding.Require("Vulkan sparse textures");
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
            return new VulkanRasterPipeline(this, descriptor);
        }

        public override RHIPipelineCache CreatePipelineCache()
        {
            Capabilities.PipelineCache.NativeCache.Require("Vulkan pipeline cache");
            return new VulkanPipelineCache(this);
        }

        public override RHIComputeIndirectCommandBuffer CreateComputeIndirectCommandBuffer(
            in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            ThrowIfDisposed();
            Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan compute indirect command buffer");
            throw new NotSupportedException(
                "Vulkan compute IndirectCommandBuffer is unavailable: "
                + Capabilities.IndirectCommandBuffer.Execution.UnavailableReason);
        }

        public override RHIRayTracingIndirectCommandBuffer CreateRayTracingIndirectCommandBuffer(
            in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            ThrowIfDisposed();
            Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan ray-tracing indirect command buffer");
            throw new NotSupportedException(
                "Vulkan ray-tracing IndirectCommandBuffer is unavailable: "
                + Capabilities.IndirectCommandBuffer.Execution.UnavailableReason);
        }

        public override RHIRasterIndirectCommandBuffer CreateRasterIndirectCommandBuffer(
            in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            ThrowIfDisposed();
            Capabilities.IndirectCommandBuffer.Execution.Require(
                "Vulkan raster indirect command buffer");
            throw new NotSupportedException(
                "Vulkan raster IndirectCommandBuffer is unavailable: "
                + Capabilities.IndirectCommandBuffer.Execution.UnavailableReason);
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
}

