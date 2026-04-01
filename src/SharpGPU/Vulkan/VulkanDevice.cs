using System;
using System.Linq;
using Infinity.Collections;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanDevice : RHIDevice
    {
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
        public VkDescriptorPool NativeDescriptorPool
        {
            get
            {
                return m_DescriptorPool;
            }
        }

        internal int GraphicsQueueFamilyIndex => m_GraphicsQueueFamilyIndex;
        internal int ComputeQueueFamilyIndex => m_ComputeQueueFamilyIndex;
        internal int TransferQueueFamilyIndex => m_TransferQueueFamilyIndex;
        internal bool UseSynchronization2 => m_UseSynchronization2;
        internal bool UseSynchronization2KhrCommand => m_UseSynchronization2KhrCommand;

        private VulkanInstance m_VulkanInstance;
        private VkDevice m_NativeDevice;
        private VkPhysicalDevice m_PhysicalDevice;
        private VkPhysicalDeviceMemoryProperties m_MemoryProperties;
        private VkDescriptorPool m_DescriptorPool;

        private int m_GraphicsQueueFamilyIndex = -1;
        private int m_ComputeQueueFamilyIndex = -1;
        private int m_TransferQueueFamilyIndex = -1;

        private bool m_RaytracingSupported;
        private bool m_RaytracingInlineSupported;
        private bool m_MeshShadingSupported;
        private bool m_VariableRateShadingSupported;
        private bool m_UseSynchronization2;
        private bool m_UseSynchronization2KhrCommand;

        public VulkanDevice(VulkanInstance instance, VkPhysicalDevice physicalDevice, in int computeQueueCount, in int transferQueueCount, in int graphicsQueueCount)
        {
            m_VulkanInstance = instance;
            m_PhysicalDevice = physicalDevice;

            QueryDeviceProperties();
            CreateDevice(computeQueueCount, transferQueueCount, graphicsQueueCount);
            UpdateDeviceFeatures();
            CreateDescriptorPool();
        }

        private void QueryDeviceProperties()
        {
            VkPhysicalDeviceProperties properties;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &properties);

            m_Name = VulkanUtility.GetString(properties.deviceName);
            m_VendorId.IntValue = properties.vendorID;
            m_DeviceId.IntValue = properties.deviceID;

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

            m_Feature = new RHIDeviceFeature(
                isFlipProjection: true,
                isHDRPresentSupported: false,
                isUnifiedMemorySupported: false,
                isRootConstantSupport: true,
                isIndirectRootConstantSupport: false,
                isPixelShaderUAVSupported: features.fragmentStoresAndAtomics,
                isRasterizerOrderedSupported: false,
                isAnisotropyTextureSupported: features.samplerAnisotropy,
                isDepthbufferFetchSupported: false,
                isFramebufferFetchSupported: false,
                isTimestampQueriesSupported: limits.timestampComputeAndGraphics,
                isOcclusionQueriesSupported: features.occlusionQueryPrecise,
                isPipelineStatsQueriesSupported: features.pipelineStatisticsQuery,
                isAtomicUInt64Supported: false,
                isWorkgraphSupported: false,
                isMeshShadingSupported: false,
                isDrawIndirectSupported: features.drawIndirectFirstInstance,
                isDrawMultiIndirectSupported: features.multiDrawIndirect,
                isRaytracingSupported: false,
                isRaytracingInlineSupported: false,
                isVariableRateShadingSupported: false,
                isHiddenSurfaceRemovalSupported: false,
                isBarycentricCoordSupported: false,
                isProgrammableSamplePositionSupported: false,
                isMLSupported: false,
                matrixMajorons: ERHIMatrixMajorons.RowMajor,
                depthValueRange: ERHIDepthValueRange.ZeroToOne,
                multiviewStrategy: ERHIMultiviewStrategy.Unsupported,
                waveOperationStrategy: ERHIWaveOperationStrategy.Basic
            );
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
            int idx = 0;
            foreach (int familyIndex in uniqueFamilies)
            {
                uint count = familyQueueCounts[familyIndex];
                float* priorities = stackalloc float[(int)count];
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
            }

            // Enumerate available device extensions
            List<string> deviceExtensions = new List<string>();
            deviceExtensions.Add("VK_KHR_swapchain");

            uint extensionCount = 0;
            VulkanNative.vkEnumerateDeviceExtensionProperties(m_PhysicalDevice, null, &extensionCount, null);
            VkExtensionProperties* availableExtensions = stackalloc VkExtensionProperties[(int)extensionCount];
            VulkanNative.vkEnumerateDeviceExtensionProperties(m_PhysicalDevice, null, &extensionCount, availableExtensions);

            HashSet<string> availableExtNames = new HashSet<string>();
            for (int i = 0; i < extensionCount; ++i)
            {
                availableExtNames.Add(VulkanUtility.GetString(availableExtensions[i].extensionName));
            }

            bool hasDynamicRendering = availableExtNames.Contains("VK_KHR_dynamic_rendering");
            bool hasAccelStruct = availableExtNames.Contains("VK_KHR_acceleration_structure");
            bool hasRTPipeline = availableExtNames.Contains("VK_KHR_ray_tracing_pipeline");
            bool hasDeferredOps = availableExtNames.Contains("VK_KHR_deferred_host_operations");
            bool hasRTQuery = availableExtNames.Contains("VK_KHR_ray_query");
            bool hasMeshShader = availableExtNames.Contains("VK_EXT_mesh_shader");
            bool hasFragmentShadingRate = availableExtNames.Contains("VK_KHR_fragment_shading_rate");
            bool hasSynchronization2Extension = availableExtNames.Contains("VK_KHR_synchronization2");
            VkPhysicalDeviceFeatures supportedCoreFeatures = default;
            VulkanNative.vkGetPhysicalDeviceFeatures(m_PhysicalDevice, &supportedCoreFeatures);
            VkPhysicalDeviceProperties physicalDeviceProperties = default;
            VulkanNative.vkGetPhysicalDeviceProperties(m_PhysicalDevice, &physicalDeviceProperties);
            bool hasVulkan13Core = physicalDeviceProperties.apiVersion.Value >= VulkanUtility.Version(1, 3, 0);

            VkPhysicalDeviceRayQueryFeaturesKHR rtQueryFeaturesQuery = new VkPhysicalDeviceRayQueryFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceRayQueryFeaturesKHR,
                pNext = null,
            };
            VkPhysicalDeviceRayTracingPipelineFeaturesKHR rtPipelineFeaturesQuery = new VkPhysicalDeviceRayTracingPipelineFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceRayTracingPipelineFeaturesKHR,
                pNext = hasRTQuery ? &rtQueryFeaturesQuery : null,
            };
            VkPhysicalDeviceAccelerationStructureFeaturesKHR accelFeaturesQuery = new VkPhysicalDeviceAccelerationStructureFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceAccelerationStructureFeaturesKHR,
                pNext = hasRTPipeline ? &rtPipelineFeaturesQuery : (hasRTQuery ? &rtQueryFeaturesQuery : null),
            };
            VkPhysicalDeviceMeshShaderFeaturesEXT meshFeaturesQuery = new VkPhysicalDeviceMeshShaderFeaturesEXT()
            {
                sType = VkStructureType.PhysicalDeviceMeshShaderFeaturesEXT,
                pNext = hasAccelStruct ? &accelFeaturesQuery : (hasRTPipeline ? &rtPipelineFeaturesQuery : (hasRTQuery ? &rtQueryFeaturesQuery : null)),
            };
            VkPhysicalDeviceFragmentShadingRateFeaturesKHR vrsFeaturesQuery = new VkPhysicalDeviceFragmentShadingRateFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceFragmentShadingRateFeaturesKHR,
                pNext = hasMeshShader ? &meshFeaturesQuery : (hasAccelStruct ? &accelFeaturesQuery : (hasRTPipeline ? &rtPipelineFeaturesQuery : (hasRTQuery ? &rtQueryFeaturesQuery : null))),
            };
            VkPhysicalDeviceVulkan13Features vulkan13FeaturesQuery = new VkPhysicalDeviceVulkan13Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan13Features,
                pNext = hasFragmentShadingRate ? &vrsFeaturesQuery : (hasMeshShader ? &meshFeaturesQuery : (hasAccelStruct ? &accelFeaturesQuery : (hasRTPipeline ? &rtPipelineFeaturesQuery : (hasRTQuery ? &rtQueryFeaturesQuery : null)))),
            };
            VkPhysicalDeviceVulkan12Features vulkan12FeaturesQuery = new VkPhysicalDeviceVulkan12Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan12Features,
                pNext = &vulkan13FeaturesQuery,
            };
            VkPhysicalDeviceFeatures2 features2Query = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.PhysicalDeviceFeatures2,
                pNext = &vulkan12FeaturesQuery,
            };
            VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &features2Query);

            bool synchronization2FeatureSupported;
            if (hasVulkan13Core)
            {
                synchronization2FeatureSupported = vulkan13FeaturesQuery.synchronization2;
            }
            else if (hasSynchronization2Extension)
            {
                VkPhysicalDeviceSynchronization2Features synchronization2FeaturesQuery = new VkPhysicalDeviceSynchronization2Features()
                {
                    sType = VkStructureType.PhysicalDeviceSynchronization2Features,
                };
                VkPhysicalDeviceFeatures2 synchronization2Features2Query = new VkPhysicalDeviceFeatures2()
                {
                    sType = VkStructureType.PhysicalDeviceFeatures2,
                    pNext = &synchronization2FeaturesQuery,
                };
                VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &synchronization2Features2Query);
                synchronization2FeatureSupported = synchronization2FeaturesQuery.synchronization2;
            }
            else
            {
                synchronization2FeatureSupported = false;
            }

            bool accelFeatureSupported = hasAccelStruct && accelFeaturesQuery.accelerationStructure;
            bool rtPipelineFeatureSupported = hasRTPipeline && rtPipelineFeaturesQuery.rayTracingPipeline;
            bool rtQueryFeatureSupported = hasRTQuery && rtQueryFeaturesQuery.rayQuery;
            bool meshShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.meshShader;
            bool taskShaderFeatureSupported = hasMeshShader && meshFeaturesQuery.taskShader;
            bool fragmentShadingRateFeatureSupported = hasFragmentShadingRate && vrsFeaturesQuery.pipelineFragmentShadingRate;
            bool useSynchronization2 = synchronization2FeatureSupported;
            bool useSynchronization2Extension = useSynchronization2 && !hasVulkan13Core && hasSynchronization2Extension;

            bool rtSupported = hasAccelStruct && hasRTPipeline && hasDeferredOps && accelFeatureSupported && rtPipelineFeatureSupported;
            bool rtInlineSupported = rtSupported && rtQueryFeatureSupported;
            bool meshSupported = hasMeshShader && meshShaderFeatureSupported;
            bool vrsSupported = hasFragmentShadingRate && fragmentShadingRateFeatureSupported;

            if (hasDynamicRendering)
            {
                deviceExtensions.Add("VK_KHR_dynamic_rendering");
            }

            if (useSynchronization2Extension)
            {
                deviceExtensions.Add("VK_KHR_synchronization2");
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

            void* pNextChain = null;

            VkPhysicalDeviceVulkan13Features vulkan13Features = new VkPhysicalDeviceVulkan13Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan13Features,
                dynamicRendering = vulkan13FeaturesQuery.dynamicRendering,
                synchronization2 = hasVulkan13Core && useSynchronization2,
            };

            VkPhysicalDeviceVulkan12Features vulkan12Features = new VkPhysicalDeviceVulkan12Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan12Features,
                pNext = &vulkan13Features,
                descriptorIndexing = vulkan12FeaturesQuery.descriptorIndexing,
                shaderSampledImageArrayNonUniformIndexing = vulkan12FeaturesQuery.shaderSampledImageArrayNonUniformIndexing,
                shaderStorageBufferArrayNonUniformIndexing = vulkan12FeaturesQuery.shaderStorageBufferArrayNonUniformIndexing,
                shaderStorageImageArrayNonUniformIndexing = vulkan12FeaturesQuery.shaderStorageImageArrayNonUniformIndexing,
                descriptorBindingSampledImageUpdateAfterBind = vulkan12FeaturesQuery.descriptorBindingSampledImageUpdateAfterBind,
                descriptorBindingStorageImageUpdateAfterBind = vulkan12FeaturesQuery.descriptorBindingStorageImageUpdateAfterBind,
                descriptorBindingStorageBufferUpdateAfterBind = vulkan12FeaturesQuery.descriptorBindingStorageBufferUpdateAfterBind,
                descriptorBindingUniformBufferUpdateAfterBind = vulkan12FeaturesQuery.descriptorBindingUniformBufferUpdateAfterBind,
                descriptorBindingPartiallyBound = vulkan12FeaturesQuery.descriptorBindingPartiallyBound,
                runtimeDescriptorArray = vulkan12FeaturesQuery.runtimeDescriptorArray,
                timelineSemaphore = vulkan12FeaturesQuery.timelineSemaphore,
                bufferDeviceAddress = vulkan12FeaturesQuery.bufferDeviceAddress,
            };
            pNextChain = &vulkan12Features;

            VkPhysicalDeviceSynchronization2Features synchronization2Features = default;
            if (useSynchronization2Extension)
            {
                synchronization2Features.sType = VkStructureType.PhysicalDeviceSynchronization2Features;
                synchronization2Features.synchronization2 = true;
                synchronization2Features.pNext = pNextChain;
                pNextChain = &synchronization2Features;
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

            // Check for 64-bit atomics via VkPhysicalDeviceVulkan12Features
            bool hasAtomicInt64 = false;
            VkPhysicalDeviceVulkan12Features vk12Features = new VkPhysicalDeviceVulkan12Features()
            {
                sType = VkStructureType.PhysicalDeviceVulkan12Features,
            };
            VkPhysicalDeviceFeatures2 features2 = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.PhysicalDeviceFeatures2,
                pNext = &vk12Features,
            };
            VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &features2);
            hasAtomicInt64 = vk12Features.shaderBufferInt64Atomics || vk12Features.shaderSharedInt64Atomics;

            // Check for barycentric coordinates
            bool hasBarycentrics = false;
            VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR barycentricFeatures = new VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR()
            {
                sType = VkStructureType.PhysicalDeviceFragmentShaderBarycentricFeaturesKHR,
            };
            VkPhysicalDeviceFeatures2 features2Bary = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.PhysicalDeviceFeatures2,
                pNext = &barycentricFeatures,
            };
            VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &features2Bary);
            hasBarycentrics = barycentricFeatures.fragmentShaderBarycentric;

            m_Feature = new RHIDeviceFeature(
                isFlipProjection: true,
                isHDRPresentSupported: false,
                isUnifiedMemorySupported: false,
                isRootConstantSupport: true,
                isIndirectRootConstantSupport: false,
                isPixelShaderUAVSupported: features.fragmentStoresAndAtomics,
                isRasterizerOrderedSupported: false,
                isAnisotropyTextureSupported: features.samplerAnisotropy,
                isDepthbufferFetchSupported: false,
                isFramebufferFetchSupported: false,
                isTimestampQueriesSupported: limits.timestampComputeAndGraphics,
                isOcclusionQueriesSupported: features.occlusionQueryPrecise,
                isPipelineStatsQueriesSupported: features.pipelineStatisticsQuery,
                isAtomicUInt64Supported: hasAtomicInt64,
                isWorkgraphSupported: false,
                isMeshShadingSupported: m_MeshShadingSupported,
                isDrawIndirectSupported: features.drawIndirectFirstInstance,
                isDrawMultiIndirectSupported: features.multiDrawIndirect,
                isRaytracingSupported: m_RaytracingSupported,
                isRaytracingInlineSupported: m_RaytracingInlineSupported,
                isVariableRateShadingSupported: m_VariableRateShadingSupported,
                isHiddenSurfaceRemovalSupported: false,
                isBarycentricCoordSupported: hasBarycentrics,
                isProgrammableSamplePositionSupported: false,
                isMLSupported: false,
                matrixMajorons: ERHIMatrixMajorons.RowMajor,
                depthValueRange: ERHIDepthValueRange.ZeroToOne,
                multiviewStrategy: ERHIMultiviewStrategy.Unsupported,
                waveOperationStrategy: ERHIWaveOperationStrategy.Basic
            );
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

        private void CreateDescriptorPool()
        {
            VkDescriptorPoolSize* poolSizes = stackalloc VkDescriptorPoolSize[7];
            poolSizes[0] = new VkDescriptorPoolSize() { type = VkDescriptorType.Sampler, descriptorCount = 2048 };
            poolSizes[1] = new VkDescriptorPoolSize() { type = VkDescriptorType.SampledImage, descriptorCount = 16384 };
            poolSizes[2] = new VkDescriptorPoolSize() { type = VkDescriptorType.StorageImage, descriptorCount = 4096 };
            poolSizes[3] = new VkDescriptorPoolSize() { type = VkDescriptorType.UniformBuffer, descriptorCount = 8192 };
            poolSizes[4] = new VkDescriptorPoolSize() { type = VkDescriptorType.StorageBuffer, descriptorCount = 8192 };
            int poolSizeCount = 5;
            if (m_RaytracingSupported)
            {
                poolSizes[poolSizeCount++] = new VkDescriptorPoolSize() { type = VkDescriptorType.AccelerationStructureKHR, descriptorCount = 512 };
            }
            poolSizes[poolSizeCount++] = new VkDescriptorPoolSize() { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 4096 };

            VkDescriptorPoolCreateInfo poolInfo = new VkDescriptorPoolCreateInfo()
            {
                sType = VkStructureType.DescriptorPoolCreateInfo,
                flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet | VkDescriptorPoolCreateFlags.UpdateAfterBind,
                maxSets = 8192,
                poolSizeCount = (uint)poolSizeCount,
                pPoolSizes = poolSizes,
            };

            fixed (VkDescriptorPool* poolPtr = &m_DescriptorPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateDescriptorPool(m_NativeDevice, &poolInfo, null, poolPtr));
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
            return new VulkanSwapChain(this, descriptor);
        }

        public override RHIFence CreateFence()
        {
            return new VulkanFence(this);
        }

        public override RHISemaphore CreateSemaphore()
        {
            return new VulkanSemaphore(this);
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            return new VulkanStorageQueue(this);
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            return new VulkanQuery(this, descriptor);
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            return new VulkanHeap(this, descriptor);
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            return new VulkanBuffer(this, descriptor);
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            return new VulkanTexture(this, descriptor);
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

        public override RHIArgumentTableLayout CreateArgumentTableLayout(in RHIArgumentTableLayoutDescriptor descriptor)
        {
            return new VulkanArgumentTableLayout(this, descriptor);
        }

        public override RHIArgumentTable CreateArgumentTable(in RHIArgumentTableDescriptor descriptor)
        {
            return new VulkanArgumentTable(this, descriptor);
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
            return new VulkanRaytracingPipeline(this, descriptor);
        }

        public override RHIRasterPipeline CreateRasterPipeline(in RHIRasterPipelineDescriptor descriptor)
        {
            return new VulkanRasterPipeline(this, descriptor);
        }

        public override RHIPipelineLibrary CreatePipelineLibrary(in RHIPipelineLibraryDescriptor descriptor)
        {
            return new VulkanPipelineLibrary(this, descriptor);
        }

        public override RHIComputeIndirectCommandBuffer CreateComputeIndirectCommandBuffer(in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            return new VulkanComputeIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRayTracingIndirectCommandBuffer CreateRayTracingIndirectCommandBuffer(in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            return new VulkanRayTracingIndirectCommandBuffer(this, descriptor);
        }

        public override RHIRasterIndirectCommandBuffer CreateRasterIndirectCommandBuffer(in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            return new VulkanRasterIndirectCommandBuffer(this, descriptor);
        }

        public override RHIMLPipeline CreateMLPipeline(in RHIMLPipelineDescriptor descriptor)
        {
            return new VulkanMLPipeline(this, descriptor);
        }

        public override RHIMLBindingSet CreateMLBindingSet(in RHIMLBindingSetDescriptor descriptor)
        {
            return new VulkanMLBindingSet(descriptor);
        }

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            return new VulkanTensor(this, descriptor);
        }

        public override RHIWorkGraphPipeline CreateWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            return new VulkanWorkGraphPipeline(descriptor);
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

            VulkanNative.vkDestroyDescriptorPool(m_NativeDevice, m_DescriptorPool, null);
            VulkanNative.vkDestroyDevice(m_NativeDevice, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
