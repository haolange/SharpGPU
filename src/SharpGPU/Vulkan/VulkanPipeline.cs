using System;
using System.Numerics;
using Vortice.Vulkan;
using SharpGPU.Mathematics;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace SharpGPU
{
    internal unsafe class VulkanPipelineLayout : RHIPipelineLayout
    {
        public VkPipelineLayout NativePipelineLayout
        {
            get
            {
                ThrowIfDisposed();
                return m_NativePipelineLayout;
            }
        }
        internal VulkanDevice Device => m_VulkanDevice;
        public uint PushConstantSize => m_PushConstantSize;
        internal IReadOnlyDictionary<uint, VkDescriptorSetLayout>
            NativeTableLayouts => m_NativeTableLayouts;

        private readonly VulkanDevice m_VulkanDevice;
        private readonly Dictionary<uint, VulkanBindingTablePlan> m_TablePlans;
        private readonly Dictionary<uint, VkDescriptorSetLayout>
            m_NativeTableLayouts;
        private VkPipelineLayout m_NativePipelineLayout;
        private readonly uint m_PushConstantSize;

        public VulkanPipelineLayout(
            VulkanDevice device,
            in RHIPipelineLayoutDescriptor descriptor)
        {
            m_VulkanDevice = device;
            m_PushConstantSize = descriptor.PushConstantSize;

            RHIBindingTableLayout[] sourceLayouts =
                descriptor.BindingTableLayouts
                ?? Array.Empty<RHIBindingTableLayout>();
            int layoutCount = sourceLayouts.Length;
            m_TablePlans =
                new Dictionary<uint, VulkanBindingTablePlan>(layoutCount);
            m_NativeTableLayouts =
                new Dictionary<uint, VkDescriptorSetLayout>(layoutCount);
            int maxSetIndex = -1;
            for (int index = 0; index < layoutCount; ++index)
            {
                VulkanBindingTableLayout layout =
                    sourceLayouts[index] as VulkanBindingTableLayout
                    ?? throw new ArgumentException(
                        $"Vulkan pipeline binding table {index} must be a "
                        + $"{nameof(VulkanBindingTableLayout)}.",
                        nameof(descriptor));
                if (layout.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        nameof(descriptor),
                        $"Vulkan pipeline binding table "
                        + $"{layout.Plan.Index} is disposed.");
                }
                if (!ReferenceEquals(layout.Device, device))
                {
                    throw new ArgumentException(
                        $"Vulkan pipeline binding table "
                        + $"{layout.Plan.Index} belongs to a different "
                        + "Vulkan device.",
                        nameof(descriptor));
                }
                if (!m_TablePlans.TryAdd(layout.Plan.Index, layout.Plan))
                {
                    throw new ArgumentException(
                        $"Vulkan pipeline contains duplicate binding table "
                        + $"index {layout.Plan.Index}.",
                        nameof(descriptor));
                }
                m_NativeTableLayouts.Add(
                    layout.Plan.Index,
                    layout.NativeDescriptorSetLayout);
                maxSetIndex = Math.Max(
                    maxSetIndex,
                    checked((int)layout.Plan.Index));
            }

            int setLayoutCount = maxSetIndex < 0 ? 0 : maxSetIndex + 1;
            if ((uint)setLayoutCount > device.DescriptorLimits.MaximumBoundSets)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptor),
                    $"Vulkan pipeline declares {setLayoutCount} "
                    + "descriptor-set slots, exceeding the device limit "
                    + $"{device.DescriptorLimits.MaximumBoundSets}.");
            }

            InitializePipelineCacheIdentity(descriptor);
            VkDescriptorSetLayout* setLayouts =
                stackalloc VkDescriptorSetLayout[Math.Max(setLayoutCount, 1)];
            VkDescriptorSetLayout emptySetLayout = default;
            try
            {
                if (setLayoutCount > 0)
                {
                    VkDescriptorSetLayoutCreateInfo emptyLayoutInfo =
                        new VkDescriptorSetLayoutCreateInfo
                        {
                            sType = VkStructureType
                                .DescriptorSetLayoutCreateInfo,
                            bindingCount = 0,
                            pBindings = null,
                        };
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreateDescriptorSetLayout(
                            device.NativeDevice,
                            &emptyLayoutInfo,
                            null,
                            &emptySetLayout));

                    for (int index = 0;
                        index < setLayoutCount;
                        ++index)
                    {
                        setLayouts[index] = emptySetLayout;
                    }
                    for (int index = 0; index < layoutCount; ++index)
                    {
                        VulkanBindingTableLayout layout =
                            (VulkanBindingTableLayout)sourceLayouts[index];
                        setLayouts[(int)layout.Plan.Index] =
                            layout.NativeDescriptorSetLayout;
                    }
                }

                VkPushConstantRange pushConstantRange =
                    new VkPushConstantRange
                    {
                        stageFlags = VkShaderStageFlags.All,
                        offset = 0,
                        size = descriptor.PushConstantSize,
                    };
                VkPipelineLayoutCreateInfo layoutInfo =
                    new VkPipelineLayoutCreateInfo
                    {
                        sType = VkStructureType.PipelineLayoutCreateInfo,
                        setLayoutCount = checked((uint)setLayoutCount),
                        pSetLayouts = setLayoutCount > 0
                            ? setLayouts
                            : null,
                        pushConstantRangeCount =
                            descriptor.PushConstantSize > 0 ? 1u : 0u,
                        pPushConstantRanges =
                            descriptor.PushConstantSize > 0
                                ? &pushConstantRange
                                : null,
                    };
                fixed (VkPipelineLayout* layoutPointer =
                    &m_NativePipelineLayout)
                {
                    VulkanUtility.CheckErrors(
                        VulkanNative.vkCreatePipelineLayout(
                            device.NativeDevice,
                            &layoutInfo,
                            null,
                            layoutPointer));
                }
            }
            finally
            {
                if (emptySetLayout.Handle != 0)
                {
                    VulkanNative.vkDestroyDescriptorSetLayout(
                        device.NativeDevice,
                        emptySetLayout,
                        null);
                }
            }
        }

        internal VulkanBindingTable ResolveReadyTable(
            RHIBindingTable bindingTable,
            in uint tableIndex)
        {
            ThrowIfDisposed();
            VulkanBindingTable table =
                bindingTable as VulkanBindingTable
                ?? throw new ArgumentException(
                    "Vulkan encoder requires a VulkanBindingTable from the "
                    + "same backend.",
                    nameof(bindingTable));
            if (!ReferenceEquals(m_VulkanDevice, table.Device))
            {
                throw new ArgumentException(
                    "Vulkan encoder cannot bind an binding table allocated "
                    + "from a different Vulkan device.",
                    nameof(bindingTable));
            }
            if (table.Layout.Plan.Index != tableIndex)
            {
                throw new ArgumentException(
                    $"Vulkan binding table reports index "
                    + $"{table.Layout.Plan.Index}, but SetBindingTable "
                    + $"requested {tableIndex}.",
                    nameof(tableIndex));
            }
            if (!m_TablePlans.TryGetValue(
                    tableIndex,
                    out VulkanBindingTablePlan? expectedPlan))
            {
                throw new ArgumentException(
                    $"Vulkan pipeline layout does not declare binding table "
                    + $"index {tableIndex}.",
                    nameof(tableIndex));
            }
            if (!expectedPlan.StructurallyEquals(table.Layout.Plan))
            {
                throw new ArgumentException(
                    $"Vulkan binding table {tableIndex} is structurally "
                    + "incompatible with the pipeline layout.",
                    nameof(bindingTable));
            }

            table.EnsureReadyForBinding();
            return table;
        }

        protected override void Release()
        {
            if (m_NativePipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    m_VulkanDevice.NativeDevice,
                    m_NativePipelineLayout,
                    null);
                m_NativePipelineLayout = default;
            }
        }
    }
    internal unsafe class VulkanComputePipeline : RHIComputePipeline
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VulkanPipelineLayout VulkanPipelineLayout => m_VulkanPipelineLayout;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;

        public VulkanComputePipeline(
            VulkanDevice device,
            in RHIComputePipelineDescriptor descriptor)
            : this(device, descriptor, null)
        {
        }

        internal VulkanComputePipeline(
            VulkanDevice device,
            in RHIComputePipelineDescriptor descriptor,
            VulkanPipelineCache? pipelineCache)
        {
            m_VulkanDevice = device;
            m_Descriptor = descriptor;
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout
                ?? throw new ArgumentException(
                    "Vulkan compute pipeline requires a VulkanPipelineLayout.",
                    nameof(descriptor));

            VulkanFunction computeFunction = descriptor.ComputeFunction as VulkanFunction
                ?? throw new ArgumentException(
                    "Vulkan compute pipeline requires a VulkanFunction.", nameof(descriptor));
            VkPipelineShaderStageCreateInfo stageInfo = computeFunction.GetShaderStageCreateInfo();

            VkComputePipelineCreateInfo pipelineInfo = new VkComputePipelineCreateInfo()
            {
                sType = VkStructureType.ComputePipelineCreateInfo,
                stage = stageInfo,
                layout = m_VulkanPipelineLayout.NativePipelineLayout,
            };

            fixed (VkPipeline* pipelinePtr = &m_NativePipeline)
            {
                VkResult result = VulkanNative.vkCreateComputePipelines(
                    device.NativeDevice,
                    pipelineCache?.NativePipelineCache ?? default,
                    1,
                    &pipelineInfo,
                    null,
                    pipelinePtr);
                if (result != VkResult.Success)
                {
                    if (m_NativePipeline.Handle != 0)
                    {
                        VulkanNative.vkDestroyPipeline(
                            device.NativeDevice,
                            m_NativePipeline,
                            null);
                        m_NativePipeline = default;
                    }
                    if (pipelineCache != null)
                    {
                        pipelineCache.ThrowPipelineCreationFailure(result, "Compute");
                    }
                }
                VulkanUtility.CheckErrors(result);
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyPipeline(m_VulkanDevice.NativeDevice, m_NativePipeline, null);
        }
    }

    internal unsafe class VulkanRasterPipeline : RHIRasterPipeline, IVulkanRasterNativePipeline
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VulkanPipelineLayout VulkanPipelineLayout => m_VulkanPipelineLayout;
        internal VkPipelineLayout EffectiveNativePipelineLayout =>
            m_EffectiveNativePipelineLayout;
        internal VulkanPrivateRasterBindingPlan PrivateBindingPlan =>
            m_PrivateBindingPlan;
        internal VulkanPrivateRasterDescriptorLayout?
            PrivateDescriptorLayout => m_PrivateDescriptorLayout;
        internal bool HasNativePipeline =>
            m_NativePipeline.Handle != 0;
        VkPipeline IVulkanRasterNativePipeline.NativePipeline =>
            m_NativePipeline;
        VkPipelineLayout IVulkanRasterNativePipeline
            .EffectiveNativePipelineLayout =>
                m_EffectiveNativePipelineLayout;
        VulkanPrivateRasterBindingPlan IVulkanRasterNativePipeline
            .PrivateBindingPlan => m_PrivateBindingPlan;
        VulkanPrivateRasterDescriptorLayout?
            IVulkanRasterNativePipeline.PrivateDescriptorLayout =>
                m_PrivateDescriptorLayout;
        bool IVulkanRasterNativePipeline.HasNativePipeline =>
            HasNativePipeline;

        private VulkanDevice m_VulkanDevice;
        private VkPipeline m_NativePipeline;
        private VulkanPipelineLayout m_VulkanPipelineLayout;
        private VkPipelineLayout m_EffectiveNativePipelineLayout;
        private bool m_OwnsEffectiveNativePipelineLayout;
        private VulkanPrivateRasterBindingPlan m_PrivateBindingPlan;
        private VulkanPrivateRasterDescriptorLayout? m_PrivateDescriptorLayout;
        private VulkanRasterShaderModuleSet m_ShaderModules;
        private bool m_OwnsShaderModules;

        public VulkanRasterPipeline(
            VulkanDevice device,
            in RHIRasterPipelineDescriptor descriptor)
            : this(device, descriptor, null)
        {
        }

        internal VulkanRasterPipeline(
            VulkanDevice device,
            in RHIRasterPipelineDescriptor descriptor,
            VulkanPipelineCache? pipelineCache,
            VkRenderPass compatibleRenderPass = default,
            uint compatibleSubPass = 0,
            VulkanRasterShaderModuleSet? shaderModules = null)
        {
            m_VulkanDevice = device;
            m_Descriptor =
                RHIRasterPipelineContract.SnapshotAndValidate(
                    in descriptor);
            RHIRasterPipelineContract.ValidateAttachmentSupport(
                device,
                in m_Descriptor);
            m_ShaderModules =
                shaderModules ??
                VulkanRasterShaderModuleSet.Create(
                    device,
                    in m_Descriptor);
            m_OwnsShaderModules = shaderModules == null;
            try
            {
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout
                ?? throw new ArgumentException(
                    "Vulkan raster pipelines require a Vulkan pipeline layout.",
                    nameof(descriptor));
            m_PrivateBindingPlan =
                VulkanPrivatePipelineLayoutBuilder.CompilePlan(
                    device,
                    m_VulkanPipelineLayout,
                    in m_Descriptor.AttachmentInterface);
            if (m_PrivateBindingPlan.HasPrivateBindings)
            {
                m_PrivateDescriptorLayout =
                    new VulkanPrivateRasterDescriptorLayout(
                        device,
                        in m_PrivateBindingPlan);
                m_EffectiveNativePipelineLayout =
                    VulkanPrivatePipelineLayoutBuilder.Create(
                        m_VulkanPipelineLayout,
                        m_PrivateDescriptorLayout);
                m_OwnsEffectiveNativePipelineLayout = true;
            }
            else
            {
                m_EffectiveNativePipelineLayout =
                    m_VulkanPipelineLayout.NativePipelineLayout;
            }

            VulkanDynamicRenderingAttachmentMappingPlan
                dynamicMappingPlan =
                    VulkanDynamicRenderingAttachmentMappingPlan
                        .Compile(
                            in m_Descriptor.AttachmentInterface);
            if (compatibleRenderPass.Handle == 0 &&
                (!device.SupportsDynamicRendering ||
                 (dynamicMappingPlan.HasLocalReadOrRemapping &&
                  !device.SupportsDynamicRenderingLocalRead)))
            {
                // Vulkan 1.1/1.2 RenderPass2-only devices freeze the
                // private state and shader modules here. The native
                // compatible variant is created only after a concrete
                // RenderPass/subpass is known.
                return;
            }

            // Pipeline-owned shader modules keep lazy native-compatible
            // variants independent from disposed public RHIFunctions.
            int stageCount = m_ShaderModules.StageCount;
            VkPipelineShaderStageCreateInfo* shaderStages =
                stackalloc VkPipelineShaderStageCreateInfo[stageCount];
            m_ShaderModules.Populate(shaderStages);

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
            int colorFormatCount =
                descriptor.ColorFormats?.Length ?? 0;
            int colorBlendAttachmentCount =
                compatibleRenderPass.Handle == 0
                    ? colorFormatCount
                    : descriptor.AttachmentInterface
                        .ColorOutputLocationCount;
            VkPipelineColorBlendAttachmentState* colorBlendAttachments =
                stackalloc VkPipelineColorBlendAttachmentState[
                    Math.Max(colorBlendAttachmentCount, 1)];

            RHIBlendDescriptor* blendDescs = stackalloc RHIBlendDescriptor[8];
            blendDescs[0] = descriptor.RenderState.BlendState.BlendDescriptor0;
            blendDescs[1] = descriptor.RenderState.BlendState.BlendDescriptor1;
            blendDescs[2] = descriptor.RenderState.BlendState.BlendDescriptor2;
            blendDescs[3] = descriptor.RenderState.BlendState.BlendDescriptor3;
            blendDescs[4] = descriptor.RenderState.BlendState.BlendDescriptor4;
            blendDescs[5] = descriptor.RenderState.BlendState.BlendDescriptor5;
            blendDescs[6] = descriptor.RenderState.BlendState.BlendDescriptor6;
            blendDescs[7] = descriptor.RenderState.BlendState.BlendDescriptor7;

            for (int i = 0; i < colorBlendAttachmentCount; ++i)
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
                flags = descriptor.AttachmentInterface
                    .FramebufferReadWriteMask != 0
                        ? VkPipelineColorBlendStateCreateFlags
                            .RasterizationOrderAttachmentAccessEXT
                        : 0,
                logicOpEnable = false,
                attachmentCount =
                    checked((uint)colorBlendAttachmentCount),
                pAttachments =
                    colorBlendAttachmentCount > 0
                        ? colorBlendAttachments
                        : null,
            };

            // Dynamic state
            bool fragmentShadingRateDynamic =
                m_VulkanDevice.Capabilities.Raster.VariableRateShadingPerDraw.Tier !=
                    ERHICapabilityTier.Unavailable;
            int dynamicStateCount = fragmentShadingRateDynamic ? 5 : 4;
            VkDynamicState* dynamicStates = stackalloc VkDynamicState[5];
            dynamicStates[0] = VkDynamicState.Viewport;
            dynamicStates[1] = VkDynamicState.Scissor;
            dynamicStates[2] = VkDynamicState.StencilReference;
            dynamicStates[3] = VkDynamicState.BlendConstants;
            if (fragmentShadingRateDynamic)
            {
                dynamicStates[4] = VkDynamicState.FragmentShadingRateKHR;
            }

            VkPipelineDynamicStateCreateInfo dynamicState = new VkPipelineDynamicStateCreateInfo()
            {
                sType = VkStructureType.PipelineDynamicStateCreateInfo,
                dynamicStateCount = checked((uint)dynamicStateCount),
                pDynamicStates = dynamicStates,
            };

            // Dynamic rendering format info (Vulkan 1.3)
            VkFormat* colorFormats = stackalloc VkFormat[Math.Max(colorFormatCount, 1)];
            ERHIPixelFormat[] descriptorColorFormats = descriptor.ColorFormats
                ?? throw new InvalidOperationException("Vulkan raster pipeline descriptor is missing color formats.");
            for (int i = 0; i < colorFormatCount; ++i)
            {
                colorFormats[i] = VulkanUtility.ConvertToVkFormat(descriptorColorFormats[i]);
            }

            uint* outputLocations =
                stackalloc uint[Math.Max(colorFormatCount, 1)];
            uint* inputIndices =
                stackalloc uint[Math.Max(colorFormatCount, 1)];
            dynamicMappingPlan.Populate(
                outputLocations,
                inputIndices);
            VkRenderingInputAttachmentIndexInfo inputIndexInfo = new()
            {
                sType = VkStructureType
                    .RenderingInputAttachmentIndexInfo,
                colorAttachmentCount =
                    checked((uint)colorFormatCount),
                pColorAttachmentInputIndices =
                    colorFormatCount == 0 ? null : inputIndices,
            };
            VkRenderingAttachmentLocationInfo locationInfo = new()
            {
                sType = VkStructureType
                    .RenderingAttachmentLocationInfo,
                pNext = &inputIndexInfo,
                colorAttachmentCount =
                    checked((uint)colorFormatCount),
                pColorAttachmentLocations =
                    colorFormatCount == 0 ? null : outputLocations,
            };
            VkPipelineRenderingCreateInfo renderingInfo = new VkPipelineRenderingCreateInfo()
            {
                sType = VkStructureType.PipelineRenderingCreateInfo,
                pNext =
                    dynamicMappingPlan.HasLocalReadOrRemapping
                        ? &locationInfo
                        : null,
                colorAttachmentCount = (uint)colorFormatCount,
                pColorAttachmentFormats = colorFormatCount > 0 ? colorFormats : null,
                depthAttachmentFormat = descriptor.DepthFormat != ERHIPixelFormat.Unknown ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.Undefined,
                stencilAttachmentFormat = (descriptor.DepthFormat == ERHIPixelFormat.D24_UNorm_S8_UInt || descriptor.DepthFormat == ERHIPixelFormat.D32_Float_S8_UInt) ? VulkanUtility.ConvertToVkFormat(descriptor.DepthFormat) : VkFormat.Undefined,
            };

            VkGraphicsPipelineCreateInfo pipelineInfo = new VkGraphicsPipelineCreateInfo()
            {
                sType = VkStructureType.GraphicsPipelineCreateInfo,
                pNext =
                    compatibleRenderPass.Handle == 0
                        ? &renderingInfo
                        : null,
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
                layout = m_EffectiveNativePipelineLayout,
                renderPass = compatibleRenderPass,
                subpass = compatibleSubPass,
                flags = 0,
            };

            fixed (VkPipeline* pipelinePtr = &m_NativePipeline)
            {
                VkResult result = VulkanNative.vkCreateGraphicsPipelines(
                    device.NativeDevice,
                    pipelineCache?.NativePipelineCache ?? default,
                    1,
                    &pipelineInfo,
                    null,
                    pipelinePtr);
                if (result != VkResult.Success)
                {
                    if (m_NativePipeline.Handle != 0)
                    {
                        VulkanNative.vkDestroyPipeline(
                            device.NativeDevice,
                            m_NativePipeline,
                            null);
                        m_NativePipeline = default;
                    }
                    if (pipelineCache != null)
                    {
                        pipelineCache.ThrowPipelineCreationFailure(result, "Graphics");
                    }
                }
                VulkanUtility.CheckErrors(result);
            }
            }
            catch
            {
                ReleaseNativeOwnership();
                throw;
            }
        }

        internal VulkanRasterNativeVariant CreateCompatibleVariant(
            VkRenderPass renderPass,
            uint subPass)
        {
            ThrowIfDisposed();
            if (renderPass.Handle == 0)
            {
                throw new ArgumentException(
                    "A non-null compatible RenderPass is required.",
                    nameof(renderPass));
            }
            RHIRasterPipelineDescriptor descriptor = m_Descriptor;
            using VulkanRasterPipeline temporary =
                new(
                    m_VulkanDevice,
                    in descriptor,
                    pipelineCache: null,
                    renderPass,
                    subPass,
                    m_ShaderModules);
            return temporary.DetachNativeVariant();
        }

        private VulkanRasterNativeVariant DetachNativeVariant()
        {
            VulkanRasterNativeVariant result =
                new(
                    m_VulkanDevice.NativeDevice,
                    m_NativePipeline,
                    m_EffectiveNativePipelineLayout,
                    m_OwnsEffectiveNativePipelineLayout,
                    in m_PrivateBindingPlan,
                    m_PrivateDescriptorLayout);
            m_NativePipeline = default;
            m_EffectiveNativePipelineLayout = default;
            m_OwnsEffectiveNativePipelineLayout = false;
            m_PrivateDescriptorLayout = null;

            // The detached transient owns native facts only. It must not
            // retain the public descriptor, layout, function wrappers, or
            // the source pipeline's shader-module owner.
            m_Descriptor = default;
            return result;
        }

        protected override void Release()
        {
            ReleaseNativeOwnership();
        }

        private void ReleaseNativeOwnership()
        {
            if (m_NativePipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(
                    m_VulkanDevice.NativeDevice,
                    m_NativePipeline,
                    null);
                m_NativePipeline = default;
            }
            if (m_OwnsEffectiveNativePipelineLayout &&
                m_EffectiveNativePipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    m_VulkanDevice.NativeDevice,
                    m_EffectiveNativePipelineLayout,
                    null);
                m_EffectiveNativePipelineLayout = default;
            }
            m_PrivateDescriptorLayout?.Dispose();
            m_PrivateDescriptorLayout = null;
            if (m_OwnsShaderModules &&
                m_ShaderModules != null)
            {
                m_ShaderModules.Dispose();
            }
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
            m_VulkanPipelineLayout = descriptor.PipelineLayout as VulkanPipelineLayout
                ?? throw new ArgumentException(
                    "Vulkan ray-tracing pipeline requires a VulkanPipelineLayout.",
                    nameof(descriptor));

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

}


namespace SharpGPU
{
    internal readonly struct VulkanRasterPipelineVariantKey :
        IEquatable<VulkanRasterPipelineVariantKey>
    {
        internal ulong RenderPassHandle { get; }
        internal uint SubPass { get; }

        internal VulkanRasterPipelineVariantKey(
            VkRenderPass renderPass,
            uint subPass)
        {
            RenderPassHandle = renderPass.Handle;
            SubPass = subPass;
        }

        public bool Equals(VulkanRasterPipelineVariantKey other) =>
            RenderPassHandle == other.RenderPassHandle &&
            SubPass == other.SubPass;

        public override bool Equals(object? obj) =>
            obj is VulkanRasterPipelineVariantKey other &&
            Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(RenderPassHandle, SubPass);
    }

    #region PrivateRasterBinding
    internal readonly struct VulkanPrivateRasterBindingPlan
    {
        internal const uint InputAttachmentBindingBase = 0;

        internal uint DescriptorSet { get; }
        internal byte LocalInputMask { get; }
        internal byte LocalInputBindingMask { get; }
        internal uint InputAttachmentCount =>
            checked((uint)BitOperations.PopCount((uint)LocalInputBindingMask));
        internal VulkanDescriptorPoolRequirements PoolRequirements =>
            new VulkanDescriptorPoolRequirements(
                samplers: 0,
                sampledImages: 0,
                storageImages: 0,
                uniformBuffers: 0,
                storageBuffers: 0,
                accelerationStructures: 0,
                inputAttachments: InputAttachmentCount);

        internal bool HasPrivateBindings =>
            LocalInputBindingMask != 0;

        private VulkanPrivateRasterBindingPlan(
            uint descriptorSet,
            byte localInputMask,
            byte localInputBindingMask)
        {
            DescriptorSet = descriptorSet;
            LocalInputMask = localInputMask;
            LocalInputBindingMask = localInputBindingMask;
        }

        internal bool UsesInputAttachmentBinding(int inputIndex)
        {
            if ((uint)inputIndex >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return (LocalInputBindingMask & (1 << inputIndex)) != 0;
        }

        internal uint GetInputAttachmentBinding(int inputIndex)
        {
            if (!UsesInputAttachmentBinding(inputIndex))
            {
                throw new InvalidOperationException(
                    $"Input index {inputIndex} is not a private Vulkan " +
                    "input-attachment binding.");
            }
            return checked(
                InputAttachmentBindingBase +
                checked((uint)inputIndex));
        }

        internal static uint GetPrivateAttachmentDescriptorSet(
            ReadOnlySpan<uint> ordinaryDescriptorSets)
        {
            bool hasOrdinarySet = false;
            uint highestOrdinarySet = 0;
            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet = ordinaryDescriptorSets[index];
                if (!hasOrdinarySet ||
                    descriptorSet > highestOrdinarySet)
                {
                    highestOrdinarySet = descriptorSet;
                    hasOrdinarySet = true;
                }
            }

            if (hasOrdinarySet &&
                highestOrdinarySet == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ordinaryDescriptorSets),
                    highestOrdinarySet,
                    "The highest Vulkan descriptor-set index leaves no " +
                    "representable slot for the private attachment set.");
            }
            return hasOrdinarySet
                ? highestOrdinarySet + 1
                : 0;
        }

        internal static VulkanPrivateRasterBindingPlan Compile(
            ReadOnlySpan<uint> ordinaryDescriptorSets,
            uint maximumBoundDescriptorSets,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumBoundDescriptorSets == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBoundDescriptorSets),
                    maximumBoundDescriptorSets,
                    "Vulkan must expose at least one bound descriptor set.");
            }

            for (int index = 0;
                 index < ordinaryDescriptorSets.Length;
                 ++index)
            {
                uint descriptorSet =
                    ordinaryDescriptorSets[index];
                if (descriptorSet >= maximumBoundDescriptorSets)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ordinaryDescriptorSets),
                        descriptorSet,
                        $"Ordinary Vulkan descriptor set {descriptorSet} " +
                        $"exceeds maxBoundDescriptorSets " +
                        $"{maximumBoundDescriptorSets}.");
                }
                for (int previous = 0;
                     previous < index;
                     ++previous)
                {
                    if (ordinaryDescriptorSets[previous] ==
                        descriptorSet)
                    {
                        throw new ArgumentException(
                            $"Ordinary Vulkan descriptor set " +
                            $"{descriptorSet} is duplicated.",
                            nameof(ordinaryDescriptorSets));
                    }
                }
            }
            uint privateDescriptorSet =
                GetPrivateAttachmentDescriptorSet(
                    ordinaryDescriptorSets);

            byte localInputMask =
                attachmentInterface.ColorInputMask;
            byte localInputBindingMask = 0;
            for (int inputIndex = 0;
                 inputIndex < attachmentInterface.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0)
                {
                    continue;
                }
                localInputBindingMask |=
                    checked((byte)(1 << inputIndex));
            }

            bool hasPrivateBindings =
                localInputBindingMask != 0;
            if (hasPrivateBindings &&
                privateDescriptorSet >= maximumBoundDescriptorSets)
            {
                throw new NotSupportedException(
                    $"Vulkan ordinary descriptor sets consume slots through " +
                    $"{privateDescriptorSet - 1}; no slot remains for the private " +
                    $"attachment set within maxBoundDescriptorSets " +
                    $"{maximumBoundDescriptorSets}.");
            }

            return new VulkanPrivateRasterBindingPlan(
                privateDescriptorSet,
                localInputMask,
                localInputBindingMask);
        }

        internal static void ValidatePipelineLayoutIdentity(
            bool isDisposed,
            object? actualDevice,
            object expectedDevice)
        {
            ArgumentNullException.ThrowIfNull(expectedDevice);
            if (isDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Vulkan pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }
    }
    #endregion

    internal static unsafe class VulkanPrivatePipelineLayoutBuilder
    {
        internal static VulkanPrivateRasterBindingPlan CompilePlan(
            VulkanDevice device,
            VulkanPipelineLayout pipelineLayout,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            uint[] ordinarySets =
                new uint[pipelineLayout.NativeTableLayouts.Count];
            int setIndex = 0;
            foreach (uint descriptorSet in
                     pipelineLayout.NativeTableLayouts.Keys)
            {
                ordinarySets[setIndex++] = descriptorSet;
            }
            return VulkanPrivateRasterBindingPlan.Compile(
                ordinarySets,
                device.DescriptorLimits.MaximumBoundSets,
                in attachmentInterface);
        }

        internal static VkPipelineLayout Create(
            VulkanPipelineLayout pipelineLayout,
            VulkanPrivateRasterDescriptorLayout privateLayout)
        {
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            ArgumentNullException.ThrowIfNull(privateLayout);
            VulkanPrivateRasterBindingPlan plan = privateLayout.Plan;
            int setLayoutCount =
                checked((int)plan.DescriptorSet + 1);
            VkDescriptorSetLayout* setLayouts =
                stackalloc VkDescriptorSetLayout[setLayoutCount];
            VkDescriptorSetLayout emptyLayout = default;
            VkPipelineLayout nativeLayout = default;
            VulkanDevice device = pipelineLayout.Device;
            try
            {
                VkDescriptorSetLayoutCreateInfo emptyInfo =
                    new VkDescriptorSetLayoutCreateInfo
                    {
                        sType = VkStructureType
                            .DescriptorSetLayoutCreateInfo,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateDescriptorSetLayout(
                        device.NativeDevice,
                        &emptyInfo,
                        null,
                        &emptyLayout));
                for (int index = 0;
                     index < setLayoutCount;
                     ++index)
                {
                    setLayouts[index] = emptyLayout;
                }
                foreach (KeyValuePair<uint, VkDescriptorSetLayout> pair
                         in pipelineLayout.NativeTableLayouts)
                {
                    setLayouts[checked((int)pair.Key)] = pair.Value;
                }
                setLayouts[checked((int)plan.DescriptorSet)] =
                    privateLayout.NativeLayout;

                VkPushConstantRange pushConstantRange =
                    new VkPushConstantRange
                    {
                        stageFlags = VkShaderStageFlags.All,
                        size = pipelineLayout.PushConstantSize,
                    };
                VkPipelineLayoutCreateInfo createInfo =
                    new VkPipelineLayoutCreateInfo
                    {
                        sType =
                            VkStructureType.PipelineLayoutCreateInfo,
                        setLayoutCount =
                            checked((uint)setLayoutCount),
                        pSetLayouts = setLayouts,
                        pushConstantRangeCount =
                            pipelineLayout.PushConstantSize == 0
                                ? 0u
                                : 1u,
                        pPushConstantRanges =
                            pipelineLayout.PushConstantSize == 0
                                ? null
                                : &pushConstantRange,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreatePipelineLayout(
                        device.NativeDevice,
                        &createInfo,
                        null,
                        &nativeLayout));
                return nativeLayout;
            }
            finally
            {
                if (emptyLayout.Handle != 0)
                {
                    VulkanNative.vkDestroyDescriptorSetLayout(
                        device.NativeDevice,
                        emptyLayout,
                        null);
                }
            }
        }
    }
    #region PipelineCache
internal sealed unsafe class VulkanPipelineCache : RHIPipelineCache
    {
        private readonly object m_Gate = new object();
        private readonly VulkanDevice m_VulkanDevice;
        private VkPipelineCache m_NativePipelineCache;

        internal VkPipelineCache NativePipelineCache
        {
            get
            {
                ThrowIfDisposed();
                return m_NativePipelineCache;
            }
        }

        internal VulkanPipelineCache(VulkanDevice device)
            : base(device)
        {
            m_VulkanDevice = device;
            VkResult result = TryCreateNativeCache(
                ReadOnlySpan<byte>.Empty,
                out m_NativePipelineCache);
            if (result != VkResult.Success)
            {
                throw CreateNativeException(
                    ERHIErrorCode.InitializationFailed,
                    result,
                    $"vkCreatePipelineCache failed during initialization with {result}.");
            }
        }

        public override RHIComputePipeline CreateComputePipeline(
            in RHIComputePipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            _ = BuildComputePipelineCacheKey(descriptor);
            lock (m_Gate)
            {
                return new VulkanComputePipeline(
                    m_VulkanDevice,
                    descriptor,
                    this);
            }
        }

        public override RHIRasterPipeline CreateRasterPipeline(
            in RHIRasterPipelineDescriptor descriptor)
        {
            ThrowIfDisposed();
            ValidateLayoutDevice(descriptor.PipelineLayout);
            _ = BuildRasterPipelineCacheKey(descriptor);
            lock (m_Gate)
            {
                return new VulkanRasterPipeline(
                    m_VulkanDevice,
                    descriptor,
                    this);
            }
        }

        internal void ThrowPipelineCreationFailure(
            in VkResult result,
            string pipelineKind)
        {
            ThrowIfDisposed();
            throw CreateNativeException(
                ToErrorCode(result, ERHIErrorCode.NativeFailure),
                result,
                $"vkCreate{pipelineKind}Pipelines failed while using the "
                + $"active VkPipelineCache with {result}.");
        }

        protected override bool TryReplaceNativePayload(
            ReadOnlySpan<byte> nativePayload,
            out string reason)
        {
            ThrowIfDisposed();
            VkResult result = TryCreateNativeCache(
                nativePayload,
                out VkPipelineCache replacement);
            if (result != VkResult.Success)
            {
                if (result == VkResult.ErrorDeviceLost
                    || result == VkResult.ErrorOutOfHostMemory
                    || result == VkResult.ErrorOutOfDeviceMemory)
                {
                    throw CreateNativeException(
                        ToErrorCode(result, ERHIErrorCode.InitializationFailed),
                        result,
                        $"vkCreatePipelineCache failed during import with {result}.");
                }

                reason =
                    $"vkCreatePipelineCache rejected the native payload with {result}.";
                return false;
            }

            lock (m_Gate)
            {
                VkPipelineCache previous = m_NativePipelineCache;
                m_NativePipelineCache = replacement;
                if (previous.Handle != 0)
                {
                    VulkanNative.vkDestroyPipelineCache(
                        m_VulkanDevice.NativeDevice,
                        previous,
                        null);
                }
            }

            reason = string.Empty;
            return true;
        }

        protected override byte[] ExportNativePayload()
        {
            ThrowIfDisposed();
            lock (m_Gate)
            {
                nuint byteCount = 0;
                VkResult sizeResult = VulkanNative.vkGetPipelineCacheData(
                    m_VulkanDevice.NativeDevice,
                    m_NativePipelineCache,
                    &byteCount,
                    null);
                if (sizeResult != VkResult.Success)
                {
                    throw CreateNativeException(
                        ToErrorCode(sizeResult, ERHIErrorCode.NativeFailure),
                        sizeResult,
                        $"vkGetPipelineCacheData size query failed with {sizeResult}.");
                }
                if (byteCount > int.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"Vulkan pipeline cache is {byteCount} bytes, exceeding the managed blob limit.");
                }

                for (int attempt = 0; attempt < 3; ++attempt)
                {
                    byte[] payload = new byte[checked((int)byteCount)];
                    if (payload.Length == 0)
                    {
                        return payload;
                    }

                    fixed (byte* payloadPointer = payload)
                    {
                        nuint writtenByteCount = byteCount;
                        VkResult result = VulkanNative.vkGetPipelineCacheData(
                            m_VulkanDevice.NativeDevice,
                            m_NativePipelineCache,
                            &writtenByteCount,
                            payloadPointer);
                        if (result == VkResult.Success)
                        {
                            if (writtenByteCount == byteCount)
                            {
                                return payload;
                            }

                            Array.Resize(
                                ref payload,
                                checked((int)writtenByteCount));
                            return payload;
                        }
                        if (result != VkResult.Incomplete)
                        {
                            throw CreateNativeException(
                                ToErrorCode(result, ERHIErrorCode.NativeFailure),
                                result,
                                $"vkGetPipelineCacheData export failed with {result}.");
                        }
                        byteCount = writtenByteCount;
                    }
                }

                throw CreateNativeException(
                    ERHIErrorCode.NativeFailure,
                    VkResult.Incomplete,
                    "vkGetPipelineCacheData remained incomplete after three bounded retries.");
            }
        }

        protected override void Release()
        {
            lock (m_Gate)
            {
                if (m_NativePipelineCache.Handle != 0)
                {
                    VulkanNative.vkDestroyPipelineCache(
                        m_VulkanDevice.NativeDevice,
                        m_NativePipelineCache,
                        null);
                    m_NativePipelineCache = default;
                }
            }
        }

        private VkResult TryCreateNativeCache(
            ReadOnlySpan<byte> nativePayload,
            out VkPipelineCache nativeCache)
        {
            nativeCache = default;
            fixed (byte* payloadPointer = nativePayload)
            {
                VkPipelineCacheCreateInfo createInfo =
                    new VkPipelineCacheCreateInfo
                    {
                        sType = VkStructureType.PipelineCacheCreateInfo,
                        initialDataSize = (nuint)nativePayload.Length,
                        pInitialData = nativePayload.IsEmpty
                            ? null
                            : payloadPointer,
                    };
                VkPipelineCache createdCache = default;
                VkResult result = VulkanNative.vkCreatePipelineCache(
                    m_VulkanDevice.NativeDevice,
                    &createInfo,
                    null,
                    &createdCache);
                nativeCache = createdCache;
                return result;
            }
        }

        private static ERHIErrorCode ToErrorCode(
            in VkResult result,
            in ERHIErrorCode fallback)
        {
            return result switch
            {
                VkResult.ErrorDeviceLost => ERHIErrorCode.DeviceLost,
                VkResult.ErrorOutOfHostMemory or
                VkResult.ErrorOutOfDeviceMemory => ERHIErrorCode.OutOfMemory,
                _ => fallback,
            };
        }

        private static RHIException CreateNativeException(
            in ERHIErrorCode errorCode,
            in VkResult result,
            string message,
            Exception? innerException = null)
        {
            ERHIDeviceState deviceState = result == VkResult.ErrorDeviceLost
                ? ERHIDeviceState.Lost
                : ERHIDeviceState.Operational;
            return new RHIException(
                errorCode,
                ERHIBackend.Vulkan,
                (long)(int)result,
                message,
                deviceState,
                innerException);
        }

        private void ValidateLayoutDevice(RHIPipelineLayout? pipelineLayout)
        {
            VulkanPipelineLayout vulkanLayout =
                pipelineLayout as VulkanPipelineLayout
                ?? throw new ArgumentException(
                    "Vulkan pipeline cache requires a VulkanPipelineLayout.",
                    nameof(pipelineLayout));
            if (vulkanLayout.IsDisposed)
            {
                throw new ObjectDisposedException(vulkanLayout.GetType().FullName);
            }
            if (!ReferenceEquals(vulkanLayout.Device, m_VulkanDevice))
            {
                throw new ArgumentException(
                    "Vulkan pipeline cache cannot create a pipeline from a different device's layout.",
                    nameof(pipelineLayout));
            }
        }
    }
    #endregion

#region MachineLearning

    internal sealed class VulkanMLProgram : RHIMLProgram
    {
        internal VulkanMLProgram(string name)
        {
            m_Name = name;
            throw VulkanMachineLearning.CreateNotSupported("Vulkan machine-learning programs");
        }

        protected override void Release()
        {
        }
    }

    internal sealed class VulkanMLPipeline : RHIMLPipeline
    {
        internal VulkanDevice Device { get; }

        internal VulkanMLPipeline(VulkanDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            m_Descriptor = descriptor;
            throw VulkanMachineLearning.CreateNotSupported("Vulkan machine-learning pipelines");
        }

        protected override void Release()
        {
        }
    }

    internal sealed class VulkanMLBindingTable : RHIMLBindingTable
    {
        internal VulkanMLBindingTable(VulkanDevice device, in RHIMLBindingTableDescriptor descriptor)
        {
            _ = device;
            m_Pipeline = descriptor.Pipeline;
            throw VulkanMachineLearning.CreateNotSupported("Vulkan machine-learning binding tables");
        }

        protected override void Release()
        {
        }
    }

    internal static class VulkanMachineLearning
    {
        internal static NotSupportedException CreateNotSupported(string operation) =>
            new NotSupportedException(
                operation
                + " are unavailable because SharpGPU has no official native ML inference API for the Vulkan backend.");
    }
#endregion
}
