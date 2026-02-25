using System;
using Infinity.Mathmatics;
using Evergine.Bindings.Vulkan;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanPipelineLayout : RHIPipelineLayout
    {
        public VkPipelineLayout NativePipelineLayout => m_NativePipelineLayout;
        public uint PushConstantSize => m_PushConstantSize;

        private VulkanDevice m_VulkanDevice;
        private VkPipelineLayout m_NativePipelineLayout;
        private uint m_PushConstantSize;

        public VulkanPipelineLayout(VulkanDevice device, in RHIPipelineLayoutDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_PushConstantSize = descriptor.PushConstantSize;

            int layoutCount = descriptor.ArgumentTableLayouts != null ? descriptor.ArgumentTableLayouts.Length : 0;
            VkDescriptorSetLayout* setLayouts = stackalloc VkDescriptorSetLayout[Math.Max(layoutCount, 1)];

            for (int i = 0; i < layoutCount; ++i)
            {
                VulkanArgumentTableLayout vkLayout = descriptor.ArgumentTableLayouts[i] as VulkanArgumentTableLayout;
                setLayouts[i] = vkLayout.NativeDescriptorSetLayout;
            }

            VkPushConstantRange pushConstantRange = new VkPushConstantRange()
            {
                stageFlags = VkShaderStageFlags.VK_SHADER_STAGE_ALL,
                offset = 0,
                size = descriptor.PushConstantSize,
            };

            VkPipelineLayoutCreateInfo layoutInfo = new VkPipelineLayoutCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO,
                setLayoutCount = (uint)layoutCount,
                pSetLayouts = layoutCount > 0 ? setLayouts : null,
                pushConstantRangeCount = descriptor.PushConstantSize > 0 ? 1u : 0u,
                pPushConstantRanges = descriptor.PushConstantSize > 0 ? &pushConstantRange : null,
            };

            fixed (VkPipelineLayout* layoutPtr = &m_NativePipelineLayout)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreatePipelineLayout(device.NativeDevice, &layoutInfo, null, layoutPtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyPipelineLayout(m_VulkanDevice.NativeDevice, m_NativePipelineLayout, null);
        }
    }

    internal unsafe class VulkanComputePipeline : RHIComputePipeline
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VulkanPipelineLayout VulkanPipelineLayout => m_VulkanPipelineLayout;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;

        public VulkanComputePipeline(VulkanDevice device, in RHIComputePipelineDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout;

            VulkanFunction computeFunction = descriptor.ComputeFunction as VulkanFunction;
            VkPipelineShaderStageCreateInfo stageInfo = computeFunction.GetShaderStageCreateInfo();

            VkComputePipelineCreateInfo pipelineInfo = new VkComputePipelineCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO,
                stage = stageInfo,
                layout = m_VulkanPipelineLayout.NativePipelineLayout,
            };

            fixed (VkPipeline* pipelinePtr = &m_NativePipeline)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateComputePipelines(device.NativeDevice, default, 1, &pipelineInfo, null, pipelinePtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyPipeline(m_VulkanDevice.NativeDevice, m_NativePipeline, null);
        }
    }

    internal unsafe class VulkanRasterPipeline : RHIRasterPipeline
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VulkanPipelineLayout VulkanPipelineLayout => m_VulkanPipelineLayout;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;

        public VulkanRasterPipeline(VulkanDevice device, in RHIRasterPipelineDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout;

            // Shader stages
            int stageCount = 0;
            VkPipelineShaderStageCreateInfo* shaderStages = stackalloc VkPipelineShaderStageCreateInfo[5];

            if (descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                VulkanFunction vertexFunction = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexFunction as VulkanFunction;
                shaderStages[stageCount++] = vertexFunction.GetShaderStageCreateInfo();
            }

            if (descriptor.PrimitiveAssembler.MeshletAssembler.HasValue)
            {
                if (descriptor.PrimitiveAssembler.MeshletAssembler.Value.TaskFunction != null)
                {
                    VulkanFunction taskFunction = descriptor.PrimitiveAssembler.MeshletAssembler.Value.TaskFunction as VulkanFunction;
                    shaderStages[stageCount++] = taskFunction.GetShaderStageCreateInfo();
                }
                VulkanFunction meshFunction = descriptor.PrimitiveAssembler.MeshletAssembler.Value.MeshFunction as VulkanFunction;
                shaderStages[stageCount++] = meshFunction.GetShaderStageCreateInfo();
            }

            if (descriptor.FragmentFunction != null)
            {
                VulkanFunction fragmentFunction = descriptor.FragmentFunction as VulkanFunction;
                shaderStages[stageCount++] = fragmentFunction.GetShaderStageCreateInfo();
            }

            // Vertex input state
            int vertexBindingCount = 0;
            int vertexAttributeCount = 0;
            VkVertexInputBindingDescription* vertexBindings = stackalloc VkVertexInputBindingDescription[32];
            VkVertexInputAttributeDescription* vertexAttributes = stackalloc VkVertexInputAttributeDescription[32];

            if (descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                var vertexAssembler = descriptor.PrimitiveAssembler.VertexAssembler.Value;
                for (int i = 0; i < vertexAssembler.VertexLayouts.Length; ++i)
                {
                    ref RHIVertexLayoutDescriptor layout = ref vertexAssembler.VertexLayouts.Span[i];

                    vertexBindings[vertexBindingCount] = new VkVertexInputBindingDescription()
                    {
                        binding = layout.Index,
                        stride = layout.Stride,
                        inputRate = layout.StepMode == ERHIVertexStepMode.PerVertex ? VkVertexInputRate.VK_VERTEX_INPUT_RATE_VERTEX : VkVertexInputRate.VK_VERTEX_INPUT_RATE_INSTANCE,
                    };
                    vertexBindingCount++;

                    for (int j = 0; j < layout.VertexElements.Length; ++j)
                    {
                        ref RHIVertexElementDescriptor element = ref layout.VertexElements.Span[j];

                        vertexAttributes[vertexAttributeCount] = new VkVertexInputAttributeDescription()
                        {
                            binding = layout.Index,
                            location = (uint)vertexAttributeCount,
                            format = VulkanUtility.ConvertToVkVertexFormat(element.Format),
                            offset = element.Offset,
                        };
                        vertexAttributeCount++;
                    }
                }
            }

            VkPipelineVertexInputStateCreateInfo vertexInputInfo = new VkPipelineVertexInputStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO,
                vertexBindingDescriptionCount = (uint)vertexBindingCount,
                pVertexBindingDescriptions = vertexBindingCount > 0 ? vertexBindings : null,
                vertexAttributeDescriptionCount = (uint)vertexAttributeCount,
                pVertexAttributeDescriptions = vertexAttributeCount > 0 ? vertexAttributes : null,
            };

            // Input assembly
            VkPipelineInputAssemblyStateCreateInfo inputAssembly = new VkPipelineInputAssemblyStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO,
                topology = VulkanUtility.ConvertToVkPrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology),
                primitiveRestartEnable = false,
            };

            // Viewport state (dynamic)
            VkPipelineViewportStateCreateInfo viewportState = new VkPipelineViewportStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO,
                viewportCount = 1,
                scissorCount = 1,
            };

            // Rasterization state
            VkPipelineRasterizationStateCreateInfo rasterizer = new VkPipelineRasterizationStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO,
                depthClampEnable = descriptor.RenderState.RasterizerState.DepthClipEnable,
                rasterizerDiscardEnable = false,
                polygonMode = VulkanUtility.ConvertToVkPolygonMode(descriptor.RenderState.RasterizerState.FillMode),
                cullMode = VulkanUtility.ConvertToVkCullMode(descriptor.RenderState.RasterizerState.CullMode),
                frontFace = VulkanUtility.ConvertToVkFrontFace(descriptor.RenderState.RasterizerState.FrontCounterClockwise),
                depthBiasEnable = descriptor.RenderState.RasterizerState.DepthBias != 0,
                depthBiasConstantFactor = descriptor.RenderState.RasterizerState.DepthBias,
                depthBiasClamp = descriptor.RenderState.RasterizerState.DepthBiasClamp,
                depthBiasSlopeFactor = descriptor.RenderState.RasterizerState.SlopeScaledDepthBias,
                lineWidth = 1.0f,
            };

            // Multisample state
            VkPipelineMultisampleStateCreateInfo multisampling = new VkPipelineMultisampleStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO,
                rasterizationSamples = VulkanUtility.ConvertToVkSampleCount(descriptor.SampleCount),
                sampleShadingEnable = false,
                minSampleShading = 1.0f,
                alphaToCoverageEnable = descriptor.RenderState.BlendState.AlphaToCoverage,
            };

            if (descriptor.RenderState.SampleMask.HasValue)
            {
                uint sampleMask = descriptor.RenderState.SampleMask.Value;
                multisampling.pSampleMask = &sampleMask;
            }

            // Depth stencil state
            VkPipelineDepthStencilStateCreateInfo depthStencil = new VkPipelineDepthStencilStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO,
                depthTestEnable = descriptor.RenderState.DepthStencilState.DepthEnable,
                depthWriteEnable = descriptor.RenderState.DepthStencilState.DepthWriteMask,
                depthCompareOp = VulkanUtility.ConvertToVkCompareOp(descriptor.RenderState.DepthStencilState.ComparisonMode),
                depthBoundsTestEnable = false,
                stencilTestEnable = descriptor.RenderState.DepthStencilState.StencilEnable,
                front = new VkStencilOpState()
                {
                    failOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.FrontFace.StencilFailOp),
                    passOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.FrontFace.StencilPassOp),
                    depthFailOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.FrontFace.StencilDepthFailOp),
                    compareOp = VulkanUtility.ConvertToVkCompareOp(descriptor.RenderState.DepthStencilState.FrontFace.ComparisonMode),
                    compareMask = descriptor.RenderState.DepthStencilState.StencilReadMask,
                    writeMask = descriptor.RenderState.DepthStencilState.StencilWriteMask,
                    reference = 0,
                },
                back = new VkStencilOpState()
                {
                    failOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.BackFace.StencilFailOp),
                    passOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.BackFace.StencilPassOp),
                    depthFailOp = VulkanUtility.ConvertToVkStencilOp(descriptor.RenderState.DepthStencilState.BackFace.StencilDepthFailOp),
                    compareOp = VulkanUtility.ConvertToVkCompareOp(descriptor.RenderState.DepthStencilState.BackFace.ComparisonMode),
                    compareMask = descriptor.RenderState.DepthStencilState.StencilReadMask,
                    writeMask = descriptor.RenderState.DepthStencilState.StencilWriteMask,
                    reference = 0,
                },
            };

            // Color blend attachments
            int colorFormatCount = descriptor.ColorFormats != null ? descriptor.ColorFormats.Length : 0;
            VkPipelineColorBlendAttachmentState* colorBlendAttachments = stackalloc VkPipelineColorBlendAttachmentState[Math.Max(colorFormatCount, 1)];

            RHIBlendDescriptor* blendDescs = stackalloc RHIBlendDescriptor[8];
            blendDescs[0] = descriptor.RenderState.BlendState.BlendDescriptor0;
            blendDescs[1] = descriptor.RenderState.BlendState.BlendDescriptor1;
            blendDescs[2] = descriptor.RenderState.BlendState.BlendDescriptor2;
            blendDescs[3] = descriptor.RenderState.BlendState.BlendDescriptor3;
            blendDescs[4] = descriptor.RenderState.BlendState.BlendDescriptor4;
            blendDescs[5] = descriptor.RenderState.BlendState.BlendDescriptor5;
            blendDescs[6] = descriptor.RenderState.BlendState.BlendDescriptor6;
            blendDescs[7] = descriptor.RenderState.BlendState.BlendDescriptor7;

            for (int i = 0; i < colorFormatCount; ++i)
            {
                int blendIndex = descriptor.RenderState.BlendState.IndependentBlend ? i : 0;
                ref RHIBlendDescriptor blend = ref blendDescs[blendIndex];

                colorBlendAttachments[i] = new VkPipelineColorBlendAttachmentState()
                {
                    blendEnable = blend.BlendEnable,
                    srcColorBlendFactor = VulkanUtility.ConvertToVkBlendFactor(blend.SrcBlendColor),
                    dstColorBlendFactor = VulkanUtility.ConvertToVkBlendFactor(blend.DstBlendColor),
                    colorBlendOp = VulkanUtility.ConvertToVkBlendOp(blend.BlendOpColor),
                    srcAlphaBlendFactor = VulkanUtility.ConvertToVkBlendFactor(blend.SrcBlendAlpha),
                    dstAlphaBlendFactor = VulkanUtility.ConvertToVkBlendFactor(blend.DstBlendAlpha),
                    alphaBlendOp = VulkanUtility.ConvertToVkBlendOp(blend.BlendOpAlpha),
                    colorWriteMask = VulkanUtility.ConvertToVkColorWriteMask(blend.ColorWriteChannel),
                };
            }

            VkPipelineColorBlendStateCreateInfo colorBlending = new VkPipelineColorBlendStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO,
                logicOpEnable = false,
                attachmentCount = (uint)colorFormatCount,
                pAttachments = colorFormatCount > 0 ? colorBlendAttachments : null,
            };

            // Dynamic state
            VkDynamicState* dynamicStates = stackalloc VkDynamicState[4];
            dynamicStates[0] = VkDynamicState.VK_DYNAMIC_STATE_VIEWPORT;
            dynamicStates[1] = VkDynamicState.VK_DYNAMIC_STATE_SCISSOR;
            dynamicStates[2] = VkDynamicState.VK_DYNAMIC_STATE_STENCIL_REFERENCE;
            dynamicStates[3] = VkDynamicState.VK_DYNAMIC_STATE_BLEND_CONSTANTS;

            VkPipelineDynamicStateCreateInfo dynamicState = new VkPipelineDynamicStateCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO,
                dynamicStateCount = 4,
                pDynamicStates = dynamicStates,
            };

            // Dynamic rendering format info (Vulkan 1.3)
            VkFormat* colorFormats = stackalloc VkFormat[Math.Max(colorFormatCount, 1)];
            for (int i = 0; i < colorFormatCount; ++i)
            {
                colorFormats[i] = VulkanUtility.ConvertToVkFormat(descriptor.ColorFormats[i]);
            }

            VkPipelineRenderingCreateInfo renderingInfo = new VkPipelineRenderingCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_RENDERING_CREATE_INFO,
                colorAttachmentCount = (uint)colorFormatCount,
                pColorAttachmentFormats = colorFormatCount > 0 ? colorFormats : null,
                depthAttachmentFormat = descriptor.DepthFormat != ERHIPixelFormat.Unknown ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.VK_FORMAT_UNDEFINED,
                stencilAttachmentFormat = (descriptor.DepthFormat == ERHIPixelFormat.D24_UNorm_S8_UInt || descriptor.DepthFormat == ERHIPixelFormat.D32_Float_S8_UInt) ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.VK_FORMAT_UNDEFINED,
            };

            VkGraphicsPipelineCreateInfo pipelineInfo = new VkGraphicsPipelineCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO,
                pNext = &renderingInfo,
                stageCount = (uint)stageCount,
                pStages = shaderStages,
                pVertexInputState = &vertexInputInfo,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &rasterizer,
                pMultisampleState = &multisampling,
                pDepthStencilState = &depthStencil,
                pColorBlendState = &colorBlending,
                pDynamicState = &dynamicState,
                layout = m_VulkanPipelineLayout.NativePipelineLayout,
                renderPass = default,
                subpass = 0,
            };

            fixed (VkPipeline* pipelinePtr = &m_NativePipeline)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateGraphicsPipelines(device.NativeDevice, default, 1, &pipelineInfo, null, pipelinePtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyPipeline(m_VulkanDevice.NativeDevice, m_NativePipeline, null);
        }
    }

    internal unsafe class VulkanRaytracingPipeline : RHIRaytracingPipeline
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VulkanPipelineLayout VulkanPipelineLayout => m_VulkanPipelineLayout;
        public uint ShaderGroupCount => m_ShaderGroupCount;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;
        private uint m_ShaderGroupCount;

        public VulkanRaytracingPipeline(VulkanDevice device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout;

            // Collect shader stages and groups
            bool hasRayGen = descriptor.RayGenFunction != null;
            int missCount = descriptor.MissFunction != null ? descriptor.MissFunction.Length : 0;
            int hitGroupCount = descriptor.HitGroups != null ? descriptor.HitGroups.Length : 0;

            int maxStages = (hasRayGen ? 1 : 0) + missCount + hitGroupCount * 3;
            int maxGroups = (hasRayGen ? 1 : 0) + missCount + hitGroupCount;

            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[Math.Max(maxStages, 1)];
            VkRayTracingShaderGroupCreateInfoKHR* groups = stackalloc VkRayTracingShaderGroupCreateInfoKHR[Math.Max(maxGroups, 1)];

            int stageIdx = 0;
            int groupIdx = 0;
            uint unusedShader = unchecked((uint)(-1)); // VK_SHADER_UNUSED_KHR

            // Ray generation
            if (hasRayGen)
            {
                VulkanFunction rayGenFunc = descriptor.RayGenFunction as VulkanFunction;
                stages[stageIdx] = rayGenFunc.GetShaderStageCreateInfo();
                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_RAY_TRACING_SHADER_GROUP_CREATE_INFO_KHR,
                    type = VkRayTracingShaderGroupTypeKHR.VK_RAY_TRACING_SHADER_GROUP_TYPE_GENERAL_KHR,
                    generalShader = (uint)stageIdx,
                    closestHitShader = unusedShader,
                    anyHitShader = unusedShader,
                    intersectionShader = unusedShader,
                };
                stageIdx++;
                groupIdx++;
            }

            // Miss shaders
            for (int i = 0; i < missCount; ++i)
            {
                VulkanFunction missFunc = descriptor.MissFunction.Span[i] as VulkanFunction;
                stages[stageIdx] = missFunc.GetShaderStageCreateInfo();
                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_RAY_TRACING_SHADER_GROUP_CREATE_INFO_KHR,
                    type = VkRayTracingShaderGroupTypeKHR.VK_RAY_TRACING_SHADER_GROUP_TYPE_GENERAL_KHR,
                    generalShader = (uint)stageIdx,
                    closestHitShader = unusedShader,
                    anyHitShader = unusedShader,
                    intersectionShader = unusedShader,
                };
                stageIdx++;
                groupIdx++;
            }

            // Hit groups
            for (int i = 0; i < hitGroupCount; ++i)
            {
                ref RHIRayHitGroupDescriptor hitGroup = ref descriptor.HitGroups.Span[i];
                bool hasIntersection = hitGroup.IntersectionFunction != null;
                var groupType = hasIntersection
                    ? VkRayTracingShaderGroupTypeKHR.VK_RAY_TRACING_SHADER_GROUP_TYPE_PROCEDURAL_HIT_GROUP_KHR
                    : VkRayTracingShaderGroupTypeKHR.VK_RAY_TRACING_SHADER_GROUP_TYPE_TRIANGLES_HIT_GROUP_KHR;

                uint closestHitIdx = unusedShader;
                uint anyHitIdx = unusedShader;
                uint intersectionIdx = unusedShader;

                VulkanFunction closestHitFunc = hitGroup.ClosestHitFunction as VulkanFunction;
                stages[stageIdx] = closestHitFunc.GetShaderStageCreateInfo();
                closestHitIdx = (uint)stageIdx;
                stageIdx++;

                if (hitGroup.AnyHitFunction != null)
                {
                    VulkanFunction anyHitFunc = hitGroup.AnyHitFunction as VulkanFunction;
                    stages[stageIdx] = anyHitFunc.GetShaderStageCreateInfo();
                    anyHitIdx = (uint)stageIdx;
                    stageIdx++;
                }

                if (hasIntersection)
                {
                    VulkanFunction intersectionFunc = hitGroup.IntersectionFunction as VulkanFunction;
                    stages[stageIdx] = intersectionFunc.GetShaderStageCreateInfo();
                    intersectionIdx = (uint)stageIdx;
                    stageIdx++;
                }

                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_RAY_TRACING_SHADER_GROUP_CREATE_INFO_KHR,
                    type = groupType,
                    generalShader = unusedShader,
                    closestHitShader = closestHitIdx,
                    anyHitShader = anyHitIdx,
                    intersectionShader = intersectionIdx,
                };
                groupIdx++;
            }

            m_ShaderGroupCount = (uint)groupIdx;

            VkRayTracingPipelineCreateInfoKHR pipelineInfo = new VkRayTracingPipelineCreateInfoKHR()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_RAY_TRACING_PIPELINE_CREATE_INFO_KHR,
                stageCount = (uint)stageIdx,
                pStages = stages,
                groupCount = (uint)groupIdx,
                pGroups = groups,
                maxPipelineRayRecursionDepth = descriptor.MaxTraceRecursionDepth,
                layout = m_VulkanPipelineLayout.NativePipelineLayout,
            };

            fixed (VkPipeline* pipelinePtr = &m_NativePipeline)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateRayTracingPipelinesKHR(device.NativeDevice, default, default, 1, &pipelineInfo, null, pipelinePtr));
            }
        }

        protected override void Release()
        {
            if (m_NativePipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(m_VulkanDevice.NativeDevice, m_NativePipeline, null);
            }
        }
    }

    internal unsafe class VulkanPipelineLibrary : RHIPipelineLibrary
    {
        private VulkanDevice m_VulkanDevice;
        private VkPipelineCache m_NativePipelineCache;

        public VulkanPipelineLibrary(VulkanDevice device, in RHIPipelineLibraryDescriptor descriptor) : base(descriptor)
        {
            m_VulkanDevice = device;

            VkPipelineCacheCreateInfo cacheInfo = new VkPipelineCacheCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_PIPELINE_CACHE_CREATE_INFO,
            };

            fixed (VkPipelineCache* cachePtr = &m_NativePipelineCache)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreatePipelineCache(device.NativeDevice, &cacheInfo, null, cachePtr));
            }
        }

        public override void StoreComputePipeline(string name, RHIComputePipeline computePipeline)
        {
            // Pipeline cache handles this implicitly in Vulkan
        }

        public override void StoreRaytracingPipeline(string name, RHIRaytracingPipeline raytracingPipeline)
        {
        }

        public override void StoreRasterPipeline(string name, RHIRasterPipeline rasterPipeline)
        {
        }

        public override RHIComputePipeline LoadComputePipeline(RHIComputePipelineDescriptor computePipelineDescriptor)
        {
            return new VulkanComputePipeline(m_VulkanDevice, computePipelineDescriptor);
        }

        public override RHIRaytracingPipeline LoadRaytracingPipeline(RHIRaytracingPipelineDescriptor raytracingPipelineDescriptor)
        {
            return new VulkanRaytracingPipeline(m_VulkanDevice, raytracingPipelineDescriptor);
        }

        public override RHIRasterPipeline LoadRasterPipeline(RHIRasterPipelineDescriptor rasterPipelineDescriptor)
        {
            return new VulkanRasterPipeline(m_VulkanDevice, rasterPipelineDescriptor);
        }

        public override RHIPipelineLibraryResult Serialize()
        {
            nuint dataSize = 0;
            VulkanNative.vkGetPipelineCacheData(m_VulkanDevice.NativeDevice, m_NativePipelineCache, &dataSize, null);

            IntPtr data = Marshal.AllocHGlobal((int)dataSize);
            VulkanNative.vkGetPipelineCacheData(m_VulkanDevice.NativeDevice, m_NativePipelineCache, &dataSize, data.ToPointer());

            return new RHIPipelineLibraryResult()
            {
                ByteSize = (uint)dataSize,
                ByteCode = data,
            };
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyPipelineCache(m_VulkanDevice.NativeDevice, m_NativePipelineCache, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
