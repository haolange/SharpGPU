using System;
using System.Linq;
using Vortice.Vulkan;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal static unsafe class VulkanNative
    {
        static VulkanNative()
        {
            Vulkan.vkInitialize();
        }

        private static readonly ConcurrentDictionary<nint, VkInstanceApi> s_InstanceApis = new();
        private static readonly ConcurrentDictionary<nint, VkDeviceApi> s_DeviceApis = new();
        private static readonly ConcurrentDictionary<nint, nint> s_PhysicalToInstance = new();
        private static readonly ConcurrentDictionary<nint, nint> s_QueueToDevice = new();
        private static readonly ConcurrentDictionary<nint, nint> s_CommandBufferToDevice = new();
        private static readonly ConcurrentDictionary<nint, PFN_vkCreateWaylandSurfaceKHR> s_CreateWaylandSurface = new();
        private static nint s_VulkanLoader;
        private static PFN_vkGetInstanceProcAddr? s_GetInstanceProcAddr;

        private static VkInstanceApi GetInstanceApi(VkInstance instance) => s_InstanceApis.GetOrAdd(instance.Handle, _ => Vulkan.GetApi(instance));
        private static VkInstanceApi GetPrimaryInstanceApi() { foreach (VkInstanceApi api in s_InstanceApis.Values) return api; throw new InvalidOperationException("No Vulkan instance API registered."); }
        private static VkInstanceApi GetInstanceApi(VkPhysicalDevice physicalDevice)
        {
            if (s_PhysicalToInstance.TryGetValue(physicalDevice.Handle, out nint instanceHandle) && s_InstanceApis.TryGetValue(instanceHandle, out VkInstanceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkPhysicalDevice is not registered to any VkInstance.");
        }
        private static VkDeviceApi GetDeviceApi(VkDevice device)
        {
            if (s_DeviceApis.TryGetValue(device.Handle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkDevice API not registered.");
        }
        private static VkDeviceApi GetPrimaryDeviceApi() { foreach (VkDeviceApi api in s_DeviceApis.Values) return api; throw new InvalidOperationException("No Vulkan device API registered."); }
        private static VkDeviceApi GetDeviceApi(VkQueue queue)
        {
            if (s_QueueToDevice.TryGetValue(queue.Handle, out nint deviceHandle) && s_DeviceApis.TryGetValue(deviceHandle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkQueue is not registered to any VkDevice.");
        }
        private static VkDeviceApi GetDeviceApi(VkCommandBuffer commandBuffer)
        {
            if (s_CommandBufferToDevice.TryGetValue(commandBuffer.Handle, out nint deviceHandle) && s_DeviceApis.TryGetValue(deviceHandle, out VkDeviceApi? api) && api != null) return api;
            throw new InvalidOperationException("VkCommandBuffer is not registered to any VkDevice.");
        }
        private static void RegisterInstance(VkInstance instance) => GetInstanceApi(instance);
        private static void UnregisterInstance(VkInstance instance)
        {
            s_InstanceApis.TryRemove(instance.Handle, out _);
            foreach (nint physicalHandle in s_PhysicalToInstance.Where(kv => kv.Value == instance.Handle).Select(kv => kv.Key).ToArray()) s_PhysicalToInstance.TryRemove(physicalHandle, out _);
        }
        private static void RegisterPhysicalDevice(VkInstance instance, VkPhysicalDevice physicalDevice) => s_PhysicalToInstance[physicalDevice.Handle] = instance.Handle;
        private static void RegisterDevice(VkPhysicalDevice physicalDevice, VkDevice device)
        {
            if (!s_PhysicalToInstance.TryGetValue(physicalDevice.Handle, out nint instanceHandle)) throw new InvalidOperationException("VkPhysicalDevice is not registered to any VkInstance.");
            VkInstance instance = new VkInstance(instanceHandle);
            s_DeviceApis[device.Handle] = Vulkan.GetApi(instance, device);
        }
        private static void UnregisterDevice(VkDevice device)
        {
            s_DeviceApis.TryRemove(device.Handle, out _);
            foreach (nint queueHandle in s_QueueToDevice.Where(kv => kv.Value == device.Handle).Select(kv => kv.Key).ToArray()) s_QueueToDevice.TryRemove(queueHandle, out _);
            foreach (nint commandBufferHandle in s_CommandBufferToDevice.Where(kv => kv.Value == device.Handle).Select(kv => kv.Key).ToArray()) s_CommandBufferToDevice.TryRemove(commandBufferHandle, out _);
        }
        private static void RegisterQueue(VkDevice device, VkQueue queue) => s_QueueToDevice[queue.Handle] = device.Handle;
        private static void RegisterCommandBuffers(VkDevice device, uint count, VkCommandBuffer* commandBuffers)
        {
            if (commandBuffers == null) return;
            for (uint i = 0; i < count; i++) s_CommandBufferToDevice[commandBuffers[i].Handle] = device.Handle;
        }

        public static VkResult vkAcquireNextImageKHR(VkDevice device, VkSwapchainKHR swapchain, ulong timeout, VkSemaphore semaphore, VkFence fence, uint* imageIndex)
        {
            VkResult result = GetDeviceApi(device).vkAcquireNextImageKHR(swapchain, timeout, semaphore, fence, imageIndex);
            return result;
        }

        public static VkResult vkAllocateCommandBuffers(VkDevice device, VkCommandBufferAllocateInfo* allocateInfo, VkCommandBuffer* commandBuffers)
        {
            VkResult result = GetDeviceApi(device).vkAllocateCommandBuffers(allocateInfo, commandBuffers);
            if (result == VkResult.Success && allocateInfo != null) RegisterCommandBuffers(device, allocateInfo->commandBufferCount, commandBuffers);
            return result;
        }

        public static VkResult vkAllocateDescriptorSets(VkDevice device, VkDescriptorSetAllocateInfo* allocateInfo, VkDescriptorSet* descriptorSets)
        {
            VkResult result = GetDeviceApi(device).vkAllocateDescriptorSets(allocateInfo, descriptorSets);
            return result;
        }

        public static VkResult vkAllocateMemory(VkDevice device, VkMemoryAllocateInfo* allocateInfo, VkAllocationCallbacks* allocator, VkDeviceMemory* memory)
        {
            VkResult result = GetDeviceApi(device).vkAllocateMemory(allocateInfo, allocator, memory);
            return result;
        }

        public static VkResult vkBeginCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferBeginInfo* beginInfo)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkBeginCommandBuffer(commandBuffer, beginInfo);
            return result;
        }

        public static VkResult vkBindBufferMemory(VkDevice device, VkBuffer buffer, VkDeviceMemory memory, ulong memoryOffset)
        {
            VkResult result = GetDeviceApi(device).vkBindBufferMemory(buffer, memory, memoryOffset);
            return result;
        }

        public static VkResult vkBindImageMemory(VkDevice device, VkImage image, VkDeviceMemory memory, ulong memoryOffset)
        {
            VkResult result = GetDeviceApi(device).vkBindImageMemory(image, memory, memoryOffset);
            return result;
        }

        public static void vkCmdBeginQuery(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint query, VkQueryControlFlags flags)
        {
            GetDeviceApi(commandBuffer).vkCmdBeginQuery(commandBuffer, queryPool, query, flags);
        }

        public static void vkCmdBeginRendering(
            VkCommandBuffer commandBuffer,
            VkRenderingInfo* renderingInfo,
            bool useKhrCommand = false)
        {
            if (useKhrCommand)
            {
                GetDeviceApi(commandBuffer).vkCmdBeginRenderingKHR(
                    commandBuffer,
                    renderingInfo);
            }
            else
            {
                GetDeviceApi(commandBuffer).vkCmdBeginRendering(
                    commandBuffer,
                    renderingInfo);
            }
        }

        public static void vkCmdBeginRenderPass2(
            VkCommandBuffer commandBuffer,
            VkRenderPassBeginInfo* renderPassBegin,
            VkSubpassBeginInfo* subpassBegin,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdBeginRenderPass2KHR(
                    commandBuffer,
                    renderPassBegin,
                    subpassBegin);
            }
            else
            {
                api.vkCmdBeginRenderPass2(
                    commandBuffer,
                    renderPassBegin,
                    subpassBegin);
            }
        }

        public static void vkCmdBindDescriptorSets(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipelineLayout layout, uint firstSet, uint descriptorSetCount, VkDescriptorSet* descriptorSets, uint dynamicOffsetCount, uint* dynamicOffsets)
        {
            GetDeviceApi(commandBuffer).vkCmdBindDescriptorSets(commandBuffer, pipelineBindPoint, layout, firstSet, descriptorSetCount, descriptorSets, dynamicOffsetCount, dynamicOffsets);
        }

        public static void vkCmdBindIndexBuffer(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, VkIndexType indexType)
        {
            GetDeviceApi(commandBuffer).vkCmdBindIndexBuffer(commandBuffer, buffer, offset, indexType);
        }

        public static void vkCmdBindPipeline(VkCommandBuffer commandBuffer, VkPipelineBindPoint pipelineBindPoint, VkPipeline pipeline)
        {
            GetDeviceApi(commandBuffer).vkCmdBindPipeline(commandBuffer, pipelineBindPoint, pipeline);
        }

        public static void vkCmdBindVertexBuffers(VkCommandBuffer commandBuffer, uint firstBinding, uint bindingCount, VkBuffer* buffers, ulong* offsets)
        {
            GetDeviceApi(commandBuffer).vkCmdBindVertexBuffers(commandBuffer, firstBinding, bindingCount, buffers, offsets);
        }

        public static void vkCmdBuildAccelerationStructuresKHR(VkCommandBuffer commandBuffer, uint infoCount, VkAccelerationStructureBuildGeometryInfoKHR* infos, VkAccelerationStructureBuildRangeInfoKHR** buildRangeInfos)
        {
            GetDeviceApi(commandBuffer).vkCmdBuildAccelerationStructuresKHR(commandBuffer, infoCount, infos, buildRangeInfos);
        }

        public static void vkCmdCopyBuffer(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkBuffer dstBuffer, uint regionCount, VkBufferCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, regionCount, regions);
        }

        public static void vkCmdCopyBufferToImage(VkCommandBuffer commandBuffer, VkBuffer srcBuffer, VkImage dstImage, VkImageLayout dstImageLayout, uint regionCount, VkBufferImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyBufferToImage(commandBuffer, srcBuffer, dstImage, dstImageLayout, regionCount, regions);
        }

        public static void vkCmdCopyImage(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkImage dstImage, VkImageLayout dstImageLayout, uint regionCount, VkImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyImage(commandBuffer, srcImage, srcImageLayout, dstImage, dstImageLayout, regionCount, regions);
        }

        public static void vkCmdCopyImageToBuffer(VkCommandBuffer commandBuffer, VkImage srcImage, VkImageLayout srcImageLayout, VkBuffer dstBuffer, uint regionCount, VkBufferImageCopy* regions)
        {
            GetDeviceApi(commandBuffer).vkCmdCopyImageToBuffer(commandBuffer, srcImage, srcImageLayout, dstBuffer, regionCount, regions);
        }

        public static void vkCmdDispatch(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            GetDeviceApi(commandBuffer).vkCmdDispatch(commandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public static void vkCmdDispatchIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset)
        {
            GetDeviceApi(commandBuffer).vkCmdDispatchIndirect(commandBuffer, buffer, offset);
        }

        public static void vkCmdDraw(VkCommandBuffer commandBuffer, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            GetDeviceApi(commandBuffer).vkCmdDraw(commandBuffer, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public static void vkCmdDrawIndexed(VkCommandBuffer commandBuffer, uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndexed(commandBuffer, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        public static void vkCmdDrawIndexedIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndexedIndirect(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdDrawIndirect(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawIndirect(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdDrawMeshTasksEXT(VkCommandBuffer commandBuffer, uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawMeshTasksEXT(commandBuffer, groupCountX, groupCountY, groupCountZ);
        }

        public static void vkCmdDrawMeshTasksIndirectEXT(VkCommandBuffer commandBuffer, VkBuffer buffer, ulong offset, uint drawCount, uint stride)
        {
            GetDeviceApi(commandBuffer).vkCmdDrawMeshTasksIndirectEXT(commandBuffer, buffer, offset, drawCount, stride);
        }

        public static void vkCmdEndQuery(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint query)
        {
            GetDeviceApi(commandBuffer).vkCmdEndQuery(commandBuffer, queryPool, query);
        }

        public static void vkCmdEndRendering(
            VkCommandBuffer commandBuffer,
            bool useKhrCommand = false)
        {
            if (useKhrCommand)
            {
                GetDeviceApi(commandBuffer).vkCmdEndRenderingKHR(
                    commandBuffer);
            }
            else
            {
                GetDeviceApi(commandBuffer).vkCmdEndRendering(
                    commandBuffer);
            }
        }

        public static void vkCmdEndRenderPass2(
            VkCommandBuffer commandBuffer,
            VkSubpassEndInfo* subpassEnd,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdEndRenderPass2KHR(commandBuffer, subpassEnd);
            }
            else
            {
                api.vkCmdEndRenderPass2(commandBuffer, subpassEnd);
            }
        }

        public static void vkCmdNextSubpass2(
            VkCommandBuffer commandBuffer,
            VkSubpassBeginInfo* subpassBegin,
            VkSubpassEndInfo* subpassEnd,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdNextSubpass2KHR(
                    commandBuffer,
                    subpassBegin,
                    subpassEnd);
            }
            else
            {
                api.vkCmdNextSubpass2(
                    commandBuffer,
                    subpassBegin,
                    subpassEnd);
            }
        }

        public static void vkCmdPipelineBarrier(VkCommandBuffer commandBuffer, VkPipelineStageFlags srcStageMask, VkPipelineStageFlags dstStageMask, VkDependencyFlags dependencyFlags, uint memoryBarrierCount, VkMemoryBarrier* memoryBarriers, uint bufferMemoryBarrierCount, VkBufferMemoryBarrier* bufferMemoryBarriers, uint imageMemoryBarrierCount, VkImageMemoryBarrier* imageMemoryBarriers)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier(commandBuffer, srcStageMask, dstStageMask, dependencyFlags, memoryBarrierCount, memoryBarriers, bufferMemoryBarrierCount, bufferMemoryBarriers, imageMemoryBarrierCount, imageMemoryBarriers);
        }

        public static void vkCmdPipelineBarrier2(VkCommandBuffer commandBuffer, VkDependencyInfo* dependencyInfo)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier2(commandBuffer, dependencyInfo);
        }

        public static void vkCmdPipelineBarrier2KHR(VkCommandBuffer commandBuffer, VkDependencyInfo* dependencyInfo)
        {
            GetDeviceApi(commandBuffer).vkCmdPipelineBarrier2KHR(commandBuffer, dependencyInfo);
        }

        public static void vkCmdPushConstants(VkCommandBuffer commandBuffer, VkPipelineLayout layout, VkShaderStageFlags stageFlags, uint offset, uint size, void* values)
        {
            GetDeviceApi(commandBuffer).vkCmdPushConstants(commandBuffer, layout, stageFlags, offset, size, values);
        }

        public static void vkCmdResetQueryPool(VkCommandBuffer commandBuffer, VkQueryPool queryPool, uint firstQuery, uint queryCount)
        {
            GetDeviceApi(commandBuffer).vkCmdResetQueryPool(commandBuffer, queryPool, firstQuery, queryCount);
        }

        public static void vkCmdSetBlendConstants(VkCommandBuffer commandBuffer, float* blendConstants)
        {
            GetDeviceApi(commandBuffer).vkCmdSetBlendConstants(commandBuffer, blendConstants);
        }

        public static void vkCmdSetRenderingAttachmentLocations(
            VkCommandBuffer commandBuffer,
            VkRenderingAttachmentLocationInfo* locationInfo,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdSetRenderingAttachmentLocationsKHR(
                    commandBuffer,
                    locationInfo);
            }
            else
            {
                api.vkCmdSetRenderingAttachmentLocations(
                    commandBuffer,
                    locationInfo);
            }
        }

        public static void vkCmdSetRenderingInputAttachmentIndices(
            VkCommandBuffer commandBuffer,
            VkRenderingInputAttachmentIndexInfo* inputIndexInfo,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(commandBuffer);
            if (useKhrEntryPoints)
            {
                api.vkCmdSetRenderingInputAttachmentIndicesKHR(
                    commandBuffer,
                    inputIndexInfo);
            }
            else
            {
                api.vkCmdSetRenderingInputAttachmentIndices(
                    commandBuffer,
                    inputIndexInfo);
            }
        }

        public static void vkCmdSetFragmentShadingRateKHR(VkCommandBuffer commandBuffer, VkExtent2D* fragmentSize, VkFragmentShadingRateCombinerOpKHR* combinerOps)
        {
            GetDeviceApi(commandBuffer).vkCmdSetFragmentShadingRateKHR(commandBuffer, fragmentSize, combinerOps);
        }

        public static void vkCmdSetScissor(VkCommandBuffer commandBuffer, uint firstScissor, uint scissorCount, VkRect2D* scissors)
        {
            GetDeviceApi(commandBuffer).vkCmdSetScissor(commandBuffer, firstScissor, scissorCount, scissors);
        }

        public static void vkCmdSetStencilReference(VkCommandBuffer commandBuffer, VkStencilFaceFlags faceMask, uint reference)
        {
            GetDeviceApi(commandBuffer).vkCmdSetStencilReference(commandBuffer, faceMask, reference);
        }

        public static void vkCmdSetViewport(VkCommandBuffer commandBuffer, uint firstViewport, uint viewportCount, VkViewport* viewports)
        {
            GetDeviceApi(commandBuffer).vkCmdSetViewport(commandBuffer, firstViewport, viewportCount, viewports);
        }

        public static void vkCmdTraceRaysIndirectKHR(VkCommandBuffer commandBuffer, VkStridedDeviceAddressRegionKHR* raygenShaderBindingTable, VkStridedDeviceAddressRegionKHR* missShaderBindingTable, VkStridedDeviceAddressRegionKHR* hitShaderBindingTable, VkStridedDeviceAddressRegionKHR* callableShaderBindingTable, ulong indirectDeviceAddress)
        {
            GetDeviceApi(commandBuffer).vkCmdTraceRaysIndirectKHR(commandBuffer, raygenShaderBindingTable, missShaderBindingTable, hitShaderBindingTable, callableShaderBindingTable, indirectDeviceAddress);
        }

        public static void vkCmdTraceRaysKHR(VkCommandBuffer commandBuffer, VkStridedDeviceAddressRegionKHR* raygenShaderBindingTable, VkStridedDeviceAddressRegionKHR* missShaderBindingTable, VkStridedDeviceAddressRegionKHR* hitShaderBindingTable, VkStridedDeviceAddressRegionKHR* callableShaderBindingTable, uint width, uint height, uint depth)
        {
            GetDeviceApi(commandBuffer).vkCmdTraceRaysKHR(commandBuffer, raygenShaderBindingTable, missShaderBindingTable, hitShaderBindingTable, callableShaderBindingTable, width, height, depth);
        }

        public static void vkCmdWriteTimestamp(VkCommandBuffer commandBuffer, VkPipelineStageFlags pipelineStage, VkQueryPool queryPool, uint query)
        {
            GetDeviceApi(commandBuffer).vkCmdWriteTimestamp(commandBuffer, pipelineStage, queryPool, query);
        }

        public static VkResult vkCreateAccelerationStructureKHR(VkDevice device, VkAccelerationStructureCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkAccelerationStructureKHR* accelerationStructure)
        {
            VkResult result = GetDeviceApi(device).vkCreateAccelerationStructureKHR(createInfo, allocator, accelerationStructure);
            return result;
        }

        public static VkResult vkCreateBuffer(VkDevice device, VkBufferCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkBuffer* buffer)
        {
            VkResult result = GetDeviceApi(device).vkCreateBuffer(createInfo, allocator, buffer);
            return result;
        }

        public static VkResult vkCreateCommandPool(VkDevice device, VkCommandPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkCommandPool* commandPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateCommandPool(createInfo, allocator, commandPool);
            return result;
        }

        public static VkResult vkCreateComputePipelines(VkDevice device, VkPipelineCache pipelineCache, uint createInfoCount, VkComputePipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateComputePipelines(pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateDescriptorPool(VkDevice device, VkDescriptorPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDescriptorPool* descriptorPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateDescriptorPool(createInfo, allocator, descriptorPool);
            return result;
        }

        public static VkResult vkCreateDescriptorSetLayout(VkDevice device, VkDescriptorSetLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDescriptorSetLayout* setLayout)
        {
            VkResult result = GetDeviceApi(device).vkCreateDescriptorSetLayout(createInfo, allocator, setLayout);
            return result;
        }

        public static VkResult vkCreateDevice(VkPhysicalDevice physicalDevice, VkDeviceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkDevice* device)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkCreateDevice(physicalDevice, createInfo, allocator, device);
            if (result == VkResult.Success && device != null) RegisterDevice(physicalDevice, *device);
            return result;
        }

        public static VkResult vkCreateFramebuffer(
            VkDevice device,
            VkFramebufferCreateInfo* createInfo,
            VkAllocationCallbacks* allocator,
            VkFramebuffer* framebuffer)
        {
            return GetDeviceApi(device).vkCreateFramebuffer(
                createInfo,
                allocator,
                framebuffer);
        }

        public static VkResult vkCreateRenderPass2(
            VkDevice device,
            VkRenderPassCreateInfo2* createInfo,
            VkAllocationCallbacks* allocator,
            VkRenderPass* renderPass,
            bool useKhrEntryPoints)
        {
            VkDeviceApi api = GetDeviceApi(device);
            return useKhrEntryPoints
                ? api.vkCreateRenderPass2KHR(
                    createInfo,
                    allocator,
                    renderPass)
                : api.vkCreateRenderPass2(
                    createInfo,
                    allocator,
                    renderPass);
        }

        public static VkResult vkCreateFence(VkDevice device, VkFenceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkFence* fence)
        {
            VkResult result = GetDeviceApi(device).vkCreateFence(createInfo, allocator, fence);
            return result;
        }

        public static VkResult vkCreateGraphicsPipelines(VkDevice device, VkPipelineCache pipelineCache, uint createInfoCount, VkGraphicsPipelineCreateInfo* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateGraphicsPipelines(pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateImage(VkDevice device, VkImageCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkImage* image)
        {
            VkResult result = GetDeviceApi(device).vkCreateImage(createInfo, allocator, image);
            return result;
        }

        public static VkResult vkCreateImageView(VkDevice device, VkImageViewCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkImageView* view)
        {
            VkResult result = GetDeviceApi(device).vkCreateImageView(createInfo, allocator, view);
            return result;
        }

        public static VkResult vkCreateInstance(VkInstanceCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkInstance* instance)
        {
            VkResult result = Vulkan.vkCreateInstance(createInfo, allocator, instance);
            if (result == VkResult.Success && instance != null) RegisterInstance(*instance);
            return result;
        }

        public static VkResult vkCreatePipelineCache(VkDevice device, VkPipelineCacheCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkPipelineCache* pipelineCache)
        {
            VkResult result = GetDeviceApi(device).vkCreatePipelineCache(createInfo, allocator, pipelineCache);
            return result;
        }

        public static VkResult vkCreatePipelineLayout(VkDevice device, VkPipelineLayoutCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkPipelineLayout* pipelineLayout)
        {
            VkResult result = GetDeviceApi(device).vkCreatePipelineLayout(createInfo, allocator, pipelineLayout);
            return result;
        }

        public static VkResult vkCreateQueryPool(VkDevice device, VkQueryPoolCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkQueryPool* queryPool)
        {
            VkResult result = GetDeviceApi(device).vkCreateQueryPool(createInfo, allocator, queryPool);
            return result;
        }

        public static VkResult vkCreateRayTracingPipelinesKHR(VkDevice device, VkDeferredOperationKHR deferredOperation, VkPipelineCache pipelineCache, uint createInfoCount, VkRayTracingPipelineCreateInfoKHR* createInfos, VkAllocationCallbacks* allocator, VkPipeline* pipelines)
        {
            VkResult result = GetDeviceApi(device).vkCreateRayTracingPipelinesKHR(deferredOperation, pipelineCache, createInfoCount, createInfos, allocator, pipelines);
            return result;
        }

        public static VkResult vkCreateSampler(VkDevice device, VkSamplerCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkSampler* sampler)
        {
            VkResult result = GetDeviceApi(device).vkCreateSampler(createInfo, allocator, sampler);
            return result;
        }

        public static VkResult vkCreateSemaphore(VkDevice device, VkSemaphoreCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkSemaphore* semaphore)
        {
            VkResult result = GetDeviceApi(device).vkCreateSemaphore(createInfo, allocator, semaphore);
            return result;
        }

        public static VkResult vkCreateShaderModule(VkDevice device, VkShaderModuleCreateInfo* createInfo, VkAllocationCallbacks* allocator, VkShaderModule* shaderModule)
        {
            VkResult result = GetDeviceApi(device).vkCreateShaderModule(createInfo, allocator, shaderModule);
            return result;
        }

        public static VkResult vkCreateSwapchainKHR(VkDevice device, VkSwapchainCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSwapchainKHR* swapchain)
        {
            VkResult result = GetDeviceApi(device).vkCreateSwapchainKHR(createInfo, allocator, swapchain);
            return result;
        }

        public static VkResult vkCreateAndroidSurfaceKHR(VkInstance instance, VkAndroidSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateAndroidSurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateMetalSurfaceEXT(VkInstance instance, VkMetalSurfaceCreateInfoEXT* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateMetalSurfaceEXT(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateWin32SurfaceKHR(VkInstance instance, VkWin32SurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateWin32SurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateXlibSurfaceKHR(VkInstance instance, VkXlibSurfaceCreateInfoKHR* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            VkResult result = GetInstanceApi(instance).vkCreateXlibSurfaceKHR(createInfo, allocator, surface);
            return result;
        }

        public static VkResult vkCreateWaylandSurfaceKHR(VkInstance instance, void* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface)
        {
            PFN_vkCreateWaylandSurfaceKHR create = s_CreateWaylandSurface.GetOrAdd(instance.Handle, _ =>
            {
                PFN_vkGetInstanceProcAddr getProc = GetInstanceProcAddr();
                nint name = Marshal.StringToCoTaskMemUTF8("vkCreateWaylandSurfaceKHR");
                try
                {
                    nint address = getProc(instance, (byte*)name);
                    if (address == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("vkCreateWaylandSurfaceKHR is unavailable on the active Vulkan instance.");
                    }
                    return Marshal.GetDelegateForFunctionPointer<PFN_vkCreateWaylandSurfaceKHR>(address);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            });
            return create(instance, createInfo, allocator, surface);
        }

        private static PFN_vkGetInstanceProcAddr GetInstanceProcAddr()
        {
            if (s_GetInstanceProcAddr != null)
            {
                return s_GetInstanceProcAddr;
            }

            if (!NativeLibrary.TryLoad("libvulkan.so.1", out s_VulkanLoader) &&
                !NativeLibrary.TryLoad("libvulkan.so", out s_VulkanLoader))
            {
                throw new InvalidOperationException("Unable to load the Vulkan loader for Wayland surface creation.");
            }
            nint address = NativeLibrary.GetExport(s_VulkanLoader, "vkGetInstanceProcAddr");
            s_GetInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<PFN_vkGetInstanceProcAddr>(address);
            return s_GetInstanceProcAddr;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate nint PFN_vkGetInstanceProcAddr(VkInstance instance, byte* name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate VkResult PFN_vkCreateWaylandSurfaceKHR(VkInstance instance, void* createInfo, VkAllocationCallbacks* allocator, VkSurfaceKHR* surface);

        public static void vkDestroyAccelerationStructureKHR(VkDevice device, VkAccelerationStructureKHR accelerationStructure, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyAccelerationStructureKHR(accelerationStructure, allocator);
        }

        public static void vkDestroyBuffer(VkDevice device, VkBuffer buffer, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyBuffer(buffer, allocator);
        }

        public static void vkDestroyCommandPool(VkDevice device, VkCommandPool commandPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyCommandPool(commandPool, allocator);
        }

        public static void vkDestroyDescriptorPool(VkDevice device, VkDescriptorPool descriptorPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyDescriptorPool(descriptorPool, allocator);
        }

        public static void vkDestroyDescriptorSetLayout(VkDevice device, VkDescriptorSetLayout descriptorSetLayout, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyDescriptorSetLayout(descriptorSetLayout, allocator);
        }

        public static void vkDestroyFramebuffer(VkDevice device, VkFramebuffer framebuffer, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyFramebuffer(framebuffer, allocator);
        }

        public static void vkDestroyDevice(VkDevice device, VkAllocationCallbacks* allocator)
        {
            VkDevice destroyedDevice = device;
            GetDeviceApi(device).vkDestroyDevice(allocator);
            UnregisterDevice(destroyedDevice);
        }

        public static void vkDestroyFence(VkDevice device, VkFence fence, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyFence(fence, allocator);
        }

        public static void vkDestroyImage(VkDevice device, VkImage image, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyImage(image, allocator);
        }

        public static void vkDestroyImageView(VkDevice device, VkImageView imageView, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyImageView(imageView, allocator);
        }

        public static void vkDestroyInstance(VkInstance instance, VkAllocationCallbacks* allocator)
        {
            VkInstance destroyedInstance = instance;
            GetInstanceApi(instance).vkDestroyInstance(allocator);
            UnregisterInstance(destroyedInstance);
        }

        public static void vkDestroyPipeline(VkDevice device, VkPipeline pipeline, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipeline(pipeline, allocator);
        }

        public static void vkDestroyPipelineCache(VkDevice device, VkPipelineCache pipelineCache, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipelineCache(pipelineCache, allocator);
        }

        public static void vkDestroyPipelineLayout(VkDevice device, VkPipelineLayout pipelineLayout, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyPipelineLayout(pipelineLayout, allocator);
        }

        public static void vkDestroyRenderPass(VkDevice device, VkRenderPass renderPass, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyRenderPass(renderPass, allocator);
        }

        public static void vkDestroyQueryPool(VkDevice device, VkQueryPool queryPool, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyQueryPool(queryPool, allocator);
        }

        public static void vkDestroySampler(VkDevice device, VkSampler sampler, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySampler(sampler, allocator);
        }

        public static void vkDestroySemaphore(VkDevice device, VkSemaphore semaphore, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySemaphore(semaphore, allocator);
        }

        public static void vkDestroyShaderModule(VkDevice device, VkShaderModule shaderModule, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroyShaderModule(shaderModule, allocator);
        }

        public static void vkDestroySurfaceKHR(VkInstance instance, VkSurfaceKHR surface, VkAllocationCallbacks* allocator)
        {
            GetInstanceApi(instance).vkDestroySurfaceKHR(surface, allocator);
        }

        public static void vkDestroySwapchainKHR(VkDevice device, VkSwapchainKHR swapchain, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkDestroySwapchainKHR(swapchain, allocator);
        }

        public static VkResult vkDeviceWaitIdle(VkDevice device)
        {
            VkResult result = GetDeviceApi(device).vkDeviceWaitIdle();
            return result;
        }

        public static VkResult vkEndCommandBuffer(VkCommandBuffer commandBuffer)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkEndCommandBuffer(commandBuffer);
            return result;
        }

        public static VkResult vkEnumerateDeviceExtensionProperties(VkPhysicalDevice physicalDevice, byte* layerName, uint* propertyCount, VkExtensionProperties* properties)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkEnumerateDeviceExtensionProperties(physicalDevice, layerName, propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumerateInstanceExtensionProperties(byte* layerName, uint* propertyCount, VkExtensionProperties* properties)
        {
            VkResult result = Vulkan.vkEnumerateInstanceExtensionProperties(layerName, propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumerateInstanceLayerProperties(uint* propertyCount, VkLayerProperties* properties)
        {
            VkResult result = Vulkan.vkEnumerateInstanceLayerProperties(propertyCount, properties);
            return result;
        }

        public static VkResult vkEnumeratePhysicalDevices(VkInstance instance, uint* physicalDeviceCount, VkPhysicalDevice* physicalDevices)
        {
            VkResult result = GetInstanceApi(instance).vkEnumeratePhysicalDevices(physicalDeviceCount, physicalDevices);
            if (result == VkResult.Success && physicalDevices != null)
            {
                uint count = physicalDeviceCount != null ? *physicalDeviceCount : 0u;
                for (uint idx = 0; idx < count; idx++) RegisterPhysicalDevice(instance, physicalDevices[idx]);
            }
            return result;
        }

        public static VkResult vkFlushMappedMemoryRanges(VkDevice device, uint memoryRangeCount, VkMappedMemoryRange* memoryRanges)
        {
            VkResult result = GetDeviceApi(device).vkFlushMappedMemoryRanges(memoryRangeCount, memoryRanges);
            return result;
        }

        public static VkResult vkInvalidateMappedMemoryRanges(VkDevice device, uint memoryRangeCount, VkMappedMemoryRange* memoryRanges)
        {
            VkResult result = GetDeviceApi(device).vkInvalidateMappedMemoryRanges(memoryRangeCount, memoryRanges);
            return result;
        }

        public static VkResult vkFreeDescriptorSets(VkDevice device, VkDescriptorPool descriptorPool, uint descriptorSetCount, VkDescriptorSet* descriptorSets)
        {
            VkResult result = GetDeviceApi(device).vkFreeDescriptorSets(descriptorPool, descriptorSetCount, descriptorSets);
            return result;
        }

        public static void vkFreeMemory(VkDevice device, VkDeviceMemory memory, VkAllocationCallbacks* allocator)
        {
            GetDeviceApi(device).vkFreeMemory(memory, allocator);
        }

        public static void vkGetAccelerationStructureBuildSizesKHR(VkDevice device, VkAccelerationStructureBuildTypeKHR buildType, VkAccelerationStructureBuildGeometryInfoKHR* buildInfo, uint* maxPrimitiveCounts, VkAccelerationStructureBuildSizesInfoKHR* sizeInfo)
        {
            GetDeviceApi(device).vkGetAccelerationStructureBuildSizesKHR(buildType, buildInfo, maxPrimitiveCounts, sizeInfo);
        }

        public static ulong vkGetAccelerationStructureDeviceAddressKHR(VkDevice device, VkAccelerationStructureDeviceAddressInfoKHR* info)
        {
            ulong result = GetDeviceApi(device).vkGetAccelerationStructureDeviceAddressKHR(info);
            return result;
        }

        public static ulong vkGetBufferDeviceAddress(VkDevice device, VkBufferDeviceAddressInfo* info)
        {
            ulong result = GetDeviceApi(device).vkGetBufferDeviceAddress(info);
            return result;
        }

        public static void vkGetBufferMemoryRequirements(VkDevice device, VkBuffer buffer, VkMemoryRequirements* memoryRequirements)
        {
            GetDeviceApi(device).vkGetBufferMemoryRequirements(buffer, memoryRequirements);
        }

        public static void vkGetDeviceQueue(VkDevice device, uint queueFamilyIndex, uint queueIndex, VkQueue* queue)
        {
            GetDeviceApi(device).vkGetDeviceQueue(queueFamilyIndex, queueIndex, queue);
            if (queue != null) RegisterQueue(device, *queue);
        }

        public static VkResult vkGetFenceStatus(VkDevice device, VkFence fence)
        {
            VkResult result = GetDeviceApi(device).vkGetFenceStatus(fence);
            return result;
        }

        public static void vkGetImageMemoryRequirements(VkDevice device, VkImage image, VkMemoryRequirements* memoryRequirements)
        {
            GetDeviceApi(device).vkGetImageMemoryRequirements(image, memoryRequirements);
        }

        public static void vkGetImageSparseMemoryRequirements(VkDevice device, VkImage image, uint* sparseMemoryRequirementCount, VkSparseImageMemoryRequirements* sparseMemoryRequirements)
        {
            GetDeviceApi(device).vkGetImageSparseMemoryRequirements(image, sparseMemoryRequirementCount, sparseMemoryRequirements);
        }

        public static void vkGetPhysicalDeviceFeatures(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures* features)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceFeatures(physicalDevice, features);
        }

        public static void vkGetPhysicalDeviceFeatures2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceFeatures2* features)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceFeatures2(physicalDevice, features);
        }

        public static void vkGetPhysicalDeviceFormatProperties(
            VkPhysicalDevice physicalDevice,
            VkFormat format,
            VkFormatProperties* formatProperties)
        {
            GetInstanceApi(physicalDevice)
                .vkGetPhysicalDeviceFormatProperties(
                    physicalDevice,
                    format,
                    formatProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties2* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties2(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceMemoryProperties2KHR(VkPhysicalDevice physicalDevice, VkPhysicalDeviceMemoryProperties2* memoryProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceMemoryProperties2KHR(physicalDevice, memoryProperties);
        }

        public static void vkGetPhysicalDeviceProperties(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties* properties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceProperties(physicalDevice, properties);
        }

        public static void vkGetPhysicalDeviceProperties2(VkPhysicalDevice physicalDevice, VkPhysicalDeviceProperties2* properties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceProperties2(physicalDevice, properties);
        }

        public static void vkGetPhysicalDeviceQueueFamilyProperties(VkPhysicalDevice physicalDevice, uint* queueFamilyPropertyCount, VkQueueFamilyProperties* queueFamilyProperties)
        {
            GetInstanceApi(physicalDevice).vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, queueFamilyPropertyCount, queueFamilyProperties);
        }

        public static VkResult vkGetPhysicalDeviceSurfaceCapabilitiesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, VkSurfaceCapabilitiesKHR* surfaceCapabilities)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, surface, surfaceCapabilities);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfaceFormatsKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* surfaceFormatCount, VkSurfaceFormatKHR* surfaceFormats)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, surface, surfaceFormatCount, surfaceFormats);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfacePresentModesKHR(VkPhysicalDevice physicalDevice, VkSurfaceKHR surface, uint* presentModeCount, VkPresentModeKHR* presentModes)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, surface, presentModeCount, presentModes);
            return result;
        }

        public static VkResult vkGetPhysicalDeviceSurfaceSupportKHR(VkPhysicalDevice physicalDevice, uint queueFamilyIndex, VkSurfaceKHR surface, VkBool32* supported)
        {
            VkResult result = GetInstanceApi(physicalDevice).vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, queueFamilyIndex, surface, supported);
            return result;
        }

        public static VkResult vkGetPipelineCacheData(VkDevice device, VkPipelineCache pipelineCache, nuint* dataSize, void* data)
        {
            VkResult result = GetDeviceApi(device).vkGetPipelineCacheData(pipelineCache, dataSize, data);
            return result;
        }

        public static VkResult vkGetQueryPoolResults(VkDevice device, VkQueryPool queryPool, uint firstQuery, uint queryCount, nuint dataSize, void* data, ulong stride, VkQueryResultFlags flags)
        {
            VkResult result = GetDeviceApi(device).vkGetQueryPoolResults(queryPool, firstQuery, queryCount, dataSize, data, stride, flags);
            return result;
        }

        public static VkResult vkGetRayTracingShaderGroupHandlesKHR(VkDevice device, VkPipeline pipeline, uint firstGroup, uint groupCount, nuint dataSize, void* data)
        {
            VkResult result = GetDeviceApi(device).vkGetRayTracingShaderGroupHandlesKHR(pipeline, firstGroup, groupCount, dataSize, data);
            return result;
        }

        public static VkResult vkGetSwapchainImagesKHR(VkDevice device, VkSwapchainKHR swapchain, uint* swapchainImageCount, VkImage* swapchainImages)
        {
            VkResult result = GetDeviceApi(device).vkGetSwapchainImagesKHR(swapchain, swapchainImageCount, swapchainImages);
            return result;
        }

        public static VkResult vkMapMemory(VkDevice device, VkDeviceMemory memory, ulong offset, ulong size, VkMemoryMapFlags flags, void** data)
        {
            VkResult result = GetDeviceApi(device).vkMapMemory(memory, offset, size, flags, data);
            return result;
        }

        public static VkResult vkQueueBindSparse(VkQueue queue, uint bindInfoCount, VkBindSparseInfo* bindInfo, VkFence fence)
        {
            VkResult result = GetDeviceApi(queue).vkQueueBindSparse(queue, bindInfoCount, bindInfo, fence);
            return result;
        }

        public static VkResult vkQueuePresentKHR(VkQueue queue, VkPresentInfoKHR* presentInfo)
        {
            VkResult result = GetDeviceApi(queue).vkQueuePresentKHR(queue, presentInfo);
            return result;
        }

        public static VkResult vkQueueSubmit(VkQueue queue, uint submitCount, VkSubmitInfo* submits, VkFence fence)
        {
            VkResult result = GetDeviceApi(queue).vkQueueSubmit(queue, submitCount, submits, fence);
            return result;
        }

        public static VkResult vkQueueWaitIdle(VkQueue queue)
        {
            VkResult result = GetDeviceApi(queue).vkQueueWaitIdle(queue);
            return result;
        }

        public static VkResult vkResetCommandBuffer(VkCommandBuffer commandBuffer, VkCommandBufferResetFlags flags)
        {
            VkResult result = GetDeviceApi(commandBuffer).vkResetCommandBuffer(commandBuffer, flags);
            return result;
        }

        public static VkResult vkResetFences(VkDevice device, uint fenceCount, VkFence* fences)
        {
            VkResult result = GetDeviceApi(device).vkResetFences(fenceCount, fences);
            return result;
        }

        public static void vkUnmapMemory(VkDevice device, VkDeviceMemory memory)
        {
            GetDeviceApi(device).vkUnmapMemory(memory);
        }

        public static void vkUpdateDescriptorSets(VkDevice device, uint descriptorWriteCount, VkWriteDescriptorSet* descriptorWrites, uint descriptorCopyCount, VkCopyDescriptorSet* descriptorCopies)
        {
            GetDeviceApi(device).vkUpdateDescriptorSets(descriptorWriteCount, descriptorWrites, descriptorCopyCount, descriptorCopies);
        }

        public static VkResult vkWaitForFences(VkDevice device, uint fenceCount, VkFence* fences, VkBool32 waitAll, ulong timeout)
        {
            VkResult result = GetDeviceApi(device).vkWaitForFences(fenceCount, fences, waitAll, timeout);
            return result;
        }

    }
}
