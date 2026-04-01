using System;
using Infinity.Mathmatics;
using Vortice.Vulkan;
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
            int setLayoutCount = 0;
            int maxSetIndex = -1;
            for (int i = 0; i < layoutCount; ++i)
            {
                VulkanArgumentTableLayout vkLayout = descriptor.ArgumentTableLayouts[i] as VulkanArgumentTableLayout;
                maxSetIndex = Math.Max(maxSetIndex, (int)vkLayout.Descriptor.Index);
            }
            if (maxSetIndex >= 0)
            {
                setLayoutCount = maxSetIndex + 1;
            }

            VkDescriptorSetLayout* setLayouts = stackalloc VkDescriptorSetLayout[Math.Max(setLayoutCount, 1)];
            VkDescriptorSetLayout emptySetLayout = default;

            if (setLayoutCount > 0)
            {
                VkDescriptorSetLayoutCreateInfo emptyLayoutInfo = new VkDescriptorSetLayoutCreateInfo()
                {
                    sType = VkStructureType.DescriptorSetLayoutCreateInfo,
                    bindingCount = 0,
                    pBindings = null,
                };

                VulkanUtility.CheckErrors(VulkanNative.vkCreateDescriptorSetLayout(device.NativeDevice, &emptyLayoutInfo, null, &emptySetLayout));

                for (int i = 0; i < setLayoutCount; ++i)
                {
                    setLayouts[i] = emptySetLayout;
                }

                for (int i = 0; i < layoutCount; ++i)
                {
                    VulkanArgumentTableLayout vkLayout = descriptor.ArgumentTableLayouts[i] as VulkanArgumentTableLayout;
                    setLayouts[(int)vkLayout.Descriptor.Index] = vkLayout.NativeDescriptorSetLayout;
                }
            }

            VkPushConstantRange pushConstantRange = new VkPushConstantRange()
            {
                stageFlags = VkShaderStageFlags.All,
                offset = 0,
                size = descriptor.PushConstantSize,
            };

            VkPipelineLayoutCreateInfo layoutInfo = new VkPipelineLayoutCreateInfo()
            {
                sType = VkStructureType.PipelineLayoutCreateInfo,
                setLayoutCount = (uint)setLayoutCount,
                pSetLayouts = setLayoutCount > 0 ? setLayouts : null,
                pushConstantRangeCount = descriptor.PushConstantSize > 0 ? 1u : 0u,
                pPushConstantRanges = descriptor.PushConstantSize > 0 ? &pushConstantRange : null,
            };

            fixed (VkPipelineLayout* layoutPtr = &m_NativePipelineLayout)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreatePipelineLayout(device.NativeDevice, &layoutInfo, null, layoutPtr));
            }

            if (emptySetLayout.Handle != 0)
            {
                VulkanNative.vkDestroyDescriptorSetLayout(device.NativeDevice, emptySetLayout, null);
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
                sType = VkStructureType.ComputePipelineCreateInfo,
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
                        inputRate = layout.StepMode == ERHIVertexStepMode.PerVertex ? VkVertexInputRate.Vertex : VkVertexInputRate.Instance,
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
                sType = VkStructureType.PipelineVertexInputStateCreateInfo,
                vertexBindingDescriptionCount = (uint)vertexBindingCount,
                pVertexBindingDescriptions = vertexBindingCount > 0 ? vertexBindings : null,
                vertexAttributeDescriptionCount = (uint)vertexAttributeCount,
                pVertexAttributeDescriptions = vertexAttributeCount > 0 ? vertexAttributes : null,
            };

            // Input assembly
            VkPipelineInputAssemblyStateCreateInfo inputAssembly = new VkPipelineInputAssemblyStateCreateInfo()
            {
                sType = VkStructureType.PipelineInputAssemblyStateCreateInfo,
                topology = VulkanUtility.ConvertToVkPrimitiveTopology(descriptor.PrimitiveAssembler.PrimitiveTopology),
                primitiveRestartEnable = false,
            };

            // Viewport state (dynamic)
            VkPipelineViewportStateCreateInfo viewportState = new VkPipelineViewportStateCreateInfo()
            {
                sType = VkStructureType.PipelineViewportStateCreateInfo,
                viewportCount = 1,
                scissorCount = 1,
            };

            // Rasterization state
            VkPipelineRasterizationStateCreateInfo rasterizer = new VkPipelineRasterizationStateCreateInfo()
            {
                sType = VkStructureType.PipelineRasterizationStateCreateInfo,
                depthClampEnable = !descriptor.RenderState.RasterizerState.DepthClipEnable,
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
                sType = VkStructureType.PipelineMultisampleStateCreateInfo,
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
                sType = VkStructureType.PipelineDepthStencilStateCreateInfo,
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
                sType = VkStructureType.PipelineColorBlendStateCreateInfo,
                logicOpEnable = false,
                attachmentCount = (uint)colorFormatCount,
                pAttachments = colorFormatCount > 0 ? colorBlendAttachments : null,
            };

            // Dynamic state
            VkDynamicState* dynamicStates = stackalloc VkDynamicState[4];
            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            dynamicStates[2] = VkDynamicState.StencilReference;
            dynamicStates[3] = VkDynamicState.BlendConstants;

            VkPipelineDynamicStateCreateInfo dynamicState = new VkPipelineDynamicStateCreateInfo()
            {
                sType = VkStructureType.PipelineDynamicStateCreateInfo,
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
                sType = VkStructureType.PipelineRenderingCreateInfo,
                colorAttachmentCount = (uint)colorFormatCount,
                pColorAttachmentFormats = colorFormatCount > 0 ? colorFormats : null,
                depthAttachmentFormat = descriptor.DepthFormat != ERHIPixelFormat.Unknown ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.Undefined,
                stencilAttachmentFormat = (descriptor.DepthFormat == ERHIPixelFormat.D24_UNorm_S8_UInt || descriptor.DepthFormat == ERHIPixelFormat.D32_Float_S8_UInt) ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.Undefined,
            };

            VkGraphicsPipelineCreateInfo pipelineInfo = new VkGraphicsPipelineCreateInfo()
            {
                sType = VkStructureType.GraphicsPipelineCreateInfo,
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
        public int RayGenerationGroupBase => m_RayGenerationGroupBase;
        public int RayGenerationGroupCount => m_RayGenerationGroupCount;
        public int MissGroupBase => m_MissGroupBase;
        public int MissGroupCount => m_MissGroupCount;
        public int HitGroupBase => m_HitGroupBase;
        public int HitGroupCount => m_HitGroupCount;
        public int CallableGroupBase => m_CallableGroupBase;
        public int CallableGroupCount => m_CallableGroupCount;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;
        private uint m_ShaderGroupCount;
        private int m_RayGenerationGroupBase;
        private int m_RayGenerationGroupCount;
        private int m_MissGroupBase;
        private int m_MissGroupCount;
        private int m_HitGroupBase;
        private int m_HitGroupCount;
        private int m_CallableGroupBase;
        private int m_CallableGroupCount;

        public VulkanRaytracingPipeline(VulkanDevice device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout;

            VulkanFunctionLibrary functionLibrary = descriptor.FunctionLibrary as VulkanFunctionLibrary
                ?? throw new InvalidOperationException("Vulkan raytracing pipeline requires a VulkanFunctionLibrary.");

            // Collect shader stages and groups
            bool hasRayGen = !string.IsNullOrEmpty(descriptor.RayGeneration.General.EntryName);
            if (!hasRayGen)
            {
                throw new InvalidOperationException("Vulkan ray tracing pipeline requires a ray generation entry.");
            }

            int missCount = descriptor.RayMissGroups.Length;
            int hitGroupCount = descriptor.RayHitGroups.Length;
            int callableCount = descriptor.RayCallableGroups.Length;

            int maxStages = 1 + missCount + hitGroupCount * 3 + callableCount;
            int maxGroups = 1 + missCount + hitGroupCount + callableCount;

            VkPipelineShaderStageCreateInfo* stages = stackalloc VkPipelineShaderStageCreateInfo[Math.Max(maxStages, 1)];
            VkRayTracingShaderGroupCreateInfoKHR* groups = stackalloc VkRayTracingShaderGroupCreateInfoKHR[Math.Max(maxGroups, 1)];

            int stageIdx = 0;
            int groupIdx = 0;
            uint unusedShader = unchecked((uint)(-1)); // VK_SHADER_UNUSED_KHR

            // Ray generation
            stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
            {
                sType = VkStructureType.PipelineShaderStageCreateInfo,
                stage = VkShaderStageFlags.RaygenKHR,
                module = functionLibrary.NativeShaderModule,
                pName = descriptor.RayGeneration.General.EntryName.ToPointer(),
            };
            groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
            {
                sType = VkStructureType.RayTracingShaderGroupCreateInfoKHR,
                type = VkRayTracingShaderGroupTypeKHR.General,
                generalShader = (uint)stageIdx,
                closestHitShader = unusedShader,
                anyHitShader = unusedShader,
                intersectionShader = unusedShader,
            };
            stageIdx++;
            groupIdx++;

            // Miss shaders
            for (int i = 0; i < missCount; ++i)
            {
                ref RHIRayGeneralGroupDescriptor missGroup = ref descriptor.RayMissGroups.Span[i];
                stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
                {
                    sType = VkStructureType.PipelineShaderStageCreateInfo,
                    stage = VkShaderStageFlags.MissKHR,
                    module = functionLibrary.NativeShaderModule,
                    pName = missGroup.General.EntryName.ToPointer(),
                };
                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.RayTracingShaderGroupCreateInfoKHR,
                    type = VkRayTracingShaderGroupTypeKHR.General,
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
                ref RHIRayHitGroupDescriptor hitGroup = ref descriptor.RayHitGroups.Span[i];
                bool hasIntersection = hitGroup.Intersect.HasValue;
                var groupType = hasIntersection
                    ? VkRayTracingShaderGroupTypeKHR.ProceduralHitGroup
                    : VkRayTracingShaderGroupTypeKHR.TrianglesHitGroup;

                uint closestHitIdx = unusedShader;
                uint anyHitIdx = unusedShader;
                uint intersectionIdx = unusedShader;

                if (hitGroup.ClosestHit.HasValue)
                {
                    stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
                    {
                        sType = VkStructureType.PipelineShaderStageCreateInfo,
                        stage = VkShaderStageFlags.ClosestHitKHR,
                        module = functionLibrary.NativeShaderModule,
                        pName = hitGroup.ClosestHit.Value.EntryName.ToPointer(),
                    };
                    closestHitIdx = (uint)stageIdx;
                    stageIdx++;
                }

                if (hitGroup.AnyHit.HasValue)
                {
                    stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
                    {
                        sType = VkStructureType.PipelineShaderStageCreateInfo,
                        stage = VkShaderStageFlags.AnyHitKHR,
                        module = functionLibrary.NativeShaderModule,
                        pName = hitGroup.AnyHit.Value.EntryName.ToPointer(),
                    };
                    anyHitIdx = (uint)stageIdx;
                    stageIdx++;
                }

                if (hitGroup.Intersect.HasValue)
                {
                    stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
                    {
                        sType = VkStructureType.PipelineShaderStageCreateInfo,
                        stage = VkShaderStageFlags.IntersectionKHR,
                        module = functionLibrary.NativeShaderModule,
                        pName = hitGroup.Intersect.Value.EntryName.ToPointer(),
                    };
                    intersectionIdx = (uint)stageIdx;
                    stageIdx++;
                }

                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.RayTracingShaderGroupCreateInfoKHR,
                    type = groupType,
                    generalShader = unusedShader,
                    closestHitShader = closestHitIdx,
                    anyHitShader = anyHitIdx,
                    intersectionShader = intersectionIdx,
                };
                groupIdx++;
            }

            // Callable shaders
            for (int i = 0; i < callableCount; ++i)
            {
                ref RHIRayGeneralGroupDescriptor callableGroup = ref descriptor.RayCallableGroups.Span[i];
                stages[stageIdx] = new VkPipelineShaderStageCreateInfo()
                {
                    sType = VkStructureType.PipelineShaderStageCreateInfo,
                    stage = VkShaderStageFlags.CallableKHR,
                    module = functionLibrary.NativeShaderModule,
                    pName = callableGroup.General.EntryName.ToPointer(),
                };
                groups[groupIdx] = new VkRayTracingShaderGroupCreateInfoKHR()
                {
                    sType = VkStructureType.RayTracingShaderGroupCreateInfoKHR,
                    type = VkRayTracingShaderGroupTypeKHR.General,
                    generalShader = (uint)stageIdx,
                    closestHitShader = unusedShader,
                    anyHitShader = unusedShader,
                    intersectionShader = unusedShader,
                };
                stageIdx++;
                groupIdx++;
            }

            m_ShaderGroupCount = (uint)groupIdx;
            m_RayGenerationGroupBase = 0;
            m_RayGenerationGroupCount = 1;
            m_MissGroupBase = m_RayGenerationGroupBase + m_RayGenerationGroupCount;
            m_MissGroupCount = missCount;
            m_HitGroupBase = m_MissGroupBase + m_MissGroupCount;
            m_HitGroupCount = hitGroupCount;
            m_CallableGroupBase = m_HitGroupBase + m_HitGroupCount;
            m_CallableGroupCount = callableCount;

            VkRayTracingPipelineCreateInfoKHR pipelineInfo = new VkRayTracingPipelineCreateInfoKHR()
            {
                sType = VkStructureType.RayTracingPipelineCreateInfoKHR,
                stageCount = (uint)stageIdx,
                pStages = stages,
                groupCount = (uint)groupIdx,
                pGroups = groups,
                maxPipelineRayRecursionDepth = descriptor.MaxRecursionDepth,
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
                sType = VkStructureType.PipelineCacheCreateInfo,
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
    internal sealed class VulkanWorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal VulkanWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
        }
    }

    internal unsafe class VulkanMLPipeline : RHIMLPipeline
    {
        internal string Name => m_Name;

        private readonly string m_Name;
        private readonly VulkanDevice m_VulkanDevice;
        private readonly VulkanMLProgram? m_Program;

        public VulkanMLPipeline(VulkanDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_VulkanDevice = device;
            m_Name = descriptor.Name;
            m_Program = descriptor.Program as VulkanMLProgram;
            m_BindingInfos = m_Program?.BindingInfos ?? Array.Empty<RHIMLTensorBindingInfo>();

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            m_TemporaryResourceSize = 0;
            m_PersistentResourceSize = 0;
        }

        protected override void Release()
        {
        }
    }
}


