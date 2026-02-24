using System;
using System.Linq;
using Infinity.Collections;
using Evergine.Bindings.Vulkan;
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
                case VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU:
                case VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU:
                case VkPhysicalDeviceType.VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU:
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

                if (m_GraphicsQueueFamilyIndex == -1 && (flags & VkQueueFlags.VK_QUEUE_GRAPHICS_BIT) != 0)
                {
                    m_GraphicsQueueFamilyIndex = i;
                }
                else if (m_ComputeQueueFamilyIndex == -1 && (flags & VkQueueFlags.VK_QUEUE_COMPUTE_BIT) != 0 && (flags & VkQueueFlags.VK_QUEUE_GRAPHICS_BIT) == 0)
                {
                    m_ComputeQueueFamilyIndex = i;
                }
                else if (m_TransferQueueFamilyIndex == -1 && (flags & VkQueueFlags.VK_QUEUE_TRANSFER_BIT) != 0 && (flags & VkQueueFlags.VK_QUEUE_GRAPHICS_BIT) == 0 && (flags & VkQueueFlags.VK_QUEUE_COMPUTE_BIT) == 0)
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
                    sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO,
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

            if (availableExtNames.Contains("VK_KHR_dynamic_rendering"))
                deviceExtensions.Add("VK_KHR_dynamic_rendering");

            // Raytracing extensions
            bool hasAccelStruct = availableExtNames.Contains("VK_KHR_acceleration_structure");
            bool hasRTPipeline = availableExtNames.Contains("VK_KHR_ray_tracing_pipeline");
            bool hasDeferredOps = availableExtNames.Contains("VK_KHR_deferred_host_operations");
            bool hasRTQuery = availableExtNames.Contains("VK_KHR_ray_query");
            bool rtSupported = hasAccelStruct && hasRTPipeline && hasDeferredOps;
            if (rtSupported)
            {
                deviceExtensions.Add("VK_KHR_acceleration_structure");
                deviceExtensions.Add("VK_KHR_ray_tracing_pipeline");
                deviceExtensions.Add("VK_KHR_deferred_host_operations");
                if (hasRTQuery)
                    deviceExtensions.Add("VK_KHR_ray_query");
            }

            // Mesh shader extension
            bool hasMeshShader = availableExtNames.Contains("VK_EXT_mesh_shader");
            if (hasMeshShader)
                deviceExtensions.Add("VK_EXT_mesh_shader");

            // Variable rate shading
            bool hasFragmentShadingRate = availableExtNames.Contains("VK_KHR_fragment_shading_rate");
            if (hasFragmentShadingRate)
                deviceExtensions.Add("VK_KHR_fragment_shading_rate");

            // Sparse binding is a core feature, no extension needed

            IntPtr* extensionPtrs = stackalloc IntPtr[deviceExtensions.Count];
            for (int i = 0; i < deviceExtensions.Count; ++i)
            {
                extensionPtrs[i] = Marshal.StringToHGlobalAnsi(deviceExtensions[i]);
            }

            // Enable features
            VkPhysicalDeviceFeatures enabledFeatures = default;
            enabledFeatures.samplerAnisotropy = true;
            enabledFeatures.fillModeNonSolid = true;
            enabledFeatures.multiDrawIndirect = true;
            enabledFeatures.drawIndirectFirstInstance = true;
            enabledFeatures.fragmentStoresAndAtomics = true;
            enabledFeatures.shaderStorageImageExtendedFormats = true;
            enabledFeatures.pipelineStatisticsQuery = true;
            enabledFeatures.occlusionQueryPrecise = true;
            enabledFeatures.sparseBinding = true;
            enabledFeatures.sparseResidencyImage2D = true;

            // Build pNext chain for extended features
            void* pNextChain = null;

            // Vulkan 1.3 features
            VkPhysicalDeviceVulkan13Features vulkan13Features = default;
            vulkan13Features.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_3_FEATURES;
            vulkan13Features.dynamicRendering = true;
            vulkan13Features.synchronization2 = true;

            VkPhysicalDeviceVulkan12Features vulkan12Features = default;
            vulkan12Features.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES;
            vulkan12Features.pNext = &vulkan13Features;
            vulkan12Features.descriptorIndexing = true;
            vulkan12Features.timelineSemaphore = true;
            vulkan12Features.bufferDeviceAddress = true;
            pNextChain = &vulkan12Features;

            // Raytracing features
            VkPhysicalDeviceAccelerationStructureFeaturesKHR accelFeatures = default;
            VkPhysicalDeviceRayTracingPipelineFeaturesKHR rtPipelineFeatures = default;
            if (rtSupported)
            {
                accelFeatures.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ACCELERATION_STRUCTURE_FEATURES_KHR;
                accelFeatures.accelerationStructure = true;
                accelFeatures.pNext = pNextChain;

                rtPipelineFeatures.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_RAY_TRACING_PIPELINE_FEATURES_KHR;
                rtPipelineFeatures.rayTracingPipeline = true;
                rtPipelineFeatures.pNext = &accelFeatures;
                pNextChain = &rtPipelineFeatures;
            }

            // Mesh shader features
            VkPhysicalDeviceMeshShaderFeaturesEXT meshFeatures = default;
            if (hasMeshShader)
            {
                meshFeatures.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_MESH_SHADER_FEATURES_EXT;
                meshFeatures.meshShader = true;
                meshFeatures.taskShader = true;
                meshFeatures.pNext = pNextChain;
                pNextChain = &meshFeatures;
            }

            // Fragment shading rate features
            VkPhysicalDeviceFragmentShadingRateFeaturesKHR vrsFeatures = default;
            if (hasFragmentShadingRate)
            {
                vrsFeatures.sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_SHADING_RATE_FEATURES_KHR;
                vrsFeatures.pipelineFragmentShadingRate = true;
                vrsFeatures.pNext = pNextChain;
                pNextChain = &vrsFeatures;
            }

            VkDeviceCreateInfo deviceCreateInfo = new VkDeviceCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO,
                pNext = pNextChain,
                queueCreateInfoCount = (uint)familyCount,
                pQueueCreateInfos = queueCreateInfos,
                enabledExtensionCount = (uint)deviceExtensions.Count,
                ppEnabledExtensionNames = (byte**)extensionPtrs,
                pEnabledFeatures = &enabledFeatures,
            };

            fixed (VkDevice* devicePtr = &m_NativeDevice)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateDevice(m_PhysicalDevice, &deviceCreateInfo, null, devicePtr));
            }

            // Store detected capability flags for feature reporting
            m_RaytracingSupported = rtSupported;
            m_RaytracingInlineSupported = rtSupported && hasRTQuery;
            m_MeshShadingSupported = hasMeshShader;
            m_VariableRateShadingSupported = hasFragmentShadingRate;

            // Create command queues
            CreateCommandQueues(computeQueueCount, transferQueueCount, graphicsQueueCount);
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
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_VULKAN_1_2_FEATURES,
            };
            VkPhysicalDeviceFeatures2 features2 = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
                pNext = &vk12Features,
            };
            VulkanNative.vkGetPhysicalDeviceFeatures2(m_PhysicalDevice, &features2);
            hasAtomicInt64 = vk12Features.shaderBufferInt64Atomics || vk12Features.shaderSharedInt64Atomics;

            // Check for barycentric coordinates
            bool hasBarycentrics = false;
            VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR barycentricFeatures = new VkPhysicalDeviceFragmentShaderBarycentricFeaturesKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FRAGMENT_SHADER_BARYCENTRIC_FEATURES_KHR,
            };
            VkPhysicalDeviceFeatures2 features2Bary = new VkPhysicalDeviceFeatures2()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2,
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
            poolSizes[0] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLER, descriptorCount = 2048 };
            poolSizes[1] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, descriptorCount = 16384 };
            poolSizes[2] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_IMAGE, descriptorCount = 4096 };
            poolSizes[3] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, descriptorCount = 8192 };
            poolSizes[4] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, descriptorCount = 8192 };
            poolSizes[5] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_ACCELERATION_STRUCTURE_KHR, descriptorCount = 512 };
            poolSizes[6] = new VkDescriptorPoolSize() { type = VkDescriptorType.VK_DESCRIPTOR_TYPE_COMBINED_IMAGE_SAMPLER, descriptorCount = 4096 };

            VkDescriptorPoolCreateInfo poolInfo = new VkDescriptorPoolCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO,
                flags = VkDescriptorPoolCreateFlags.VK_DESCRIPTOR_POOL_CREATE_FREE_DESCRIPTOR_SET_BIT,
                maxSets = 8192,
                poolSizeCount = 7,
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

        public override RHITensor CreateTensor(in RHIMLTensorDescriptor descriptor)
        {
            return new VulkanTensor(this, descriptor);
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
