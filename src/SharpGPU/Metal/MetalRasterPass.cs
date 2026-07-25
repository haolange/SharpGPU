using System.Collections.Generic;
using System;

namespace SharpGPU
{
    internal readonly struct MetalRasterCapabilities
    {
        internal bool ColorOutputMapping { get; }
        internal bool FramebufferLocalRead { get; }
        internal bool RasterOrderGroups { get; }
        internal bool PassMappingSelector { get; }
        internal bool MapEntrySelector { get; }
        internal bool EncoderMappingSelector { get; }

        internal MetalRasterCapabilities(
            bool colorOutputMapping,
            bool framebufferLocalRead,
            bool rasterOrderGroups,
            bool passMappingSelector = true,
            bool mapEntrySelector = true,
            bool encoderMappingSelector = true)
        {
            ColorOutputMapping = colorOutputMapping;
            FramebufferLocalRead = framebufferLocalRead;
            RasterOrderGroups = rasterOrderGroups;
            PassMappingSelector = passMappingSelector;
            MapEntrySelector = mapEntrySelector;
            EncoderMappingSelector = encoderMappingSelector;
        }

        internal static MetalRasterCapabilities FromSelectorProbes(
            bool passMappingSelector,
            bool mapEntrySelector,
            bool encoderMappingSelector,
            bool rasterOrderGroups)
        {
            bool completeMappingMechanism =
                passMappingSelector &&
                mapEntrySelector &&
                encoderMappingSelector;
            return new MetalRasterCapabilities(
                colorOutputMapping: completeMappingMechanism,
                framebufferLocalRead: completeMappingMechanism,
                rasterOrderGroups,
                passMappingSelector,
                mapEntrySelector,
                encoderMappingSelector);
        }
    }

    internal readonly struct MetalRasterSubPassLowering
    {
        internal byte LocalInputMask { get; }
        internal byte OrdinaryOutputMask { get; }
        internal byte RasterOrderedMask { get; }
        internal byte PreserveMask { get; }
        internal ERHISubPassFlags DepthStencilFlags { get; }
        internal bool HasLocalInputs => LocalInputMask != 0;
        internal bool HasNonIdentityOutputMapping { get; }
        internal bool RequiresColorAttachmentMap =>
            HasLocalInputs || HasNonIdentityOutputMapping;
        internal int LocalInputSlotCount =>
            m_LocalInputPhysicalAttachments.Length;
        internal int OutputLocationCount =>
            m_OutputPhysicalAttachments.Length;

        private readonly int[] m_LocalInputPhysicalAttachments;
        private readonly int[] m_OutputPhysicalAttachments;
        private readonly ulong[] m_PhysicalAttachmentsByMappingIndex;

        internal MetalRasterSubPassLowering(
            in RasterSubPassPlan plan,
            byte passRasterOrderedMask)
        {
            RHIAttachmentInterfaceSignature attachmentInterface =
                plan.AttachmentInterface;
            byte phaseRasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            LocalInputMask = checked((byte)(
                attachmentInterface.ColorInputMask &
                ~phaseRasterOrderedMask));
            OrdinaryOutputMask = checked((byte)(
                attachmentInterface.ColorOutputMask &
                ~phaseRasterOrderedMask));
            RasterOrderedMask = phaseRasterOrderedMask;
            PreserveMask = plan.PreserveMask;
            DepthStencilFlags = attachmentInterface.DepthStencilFlags;

            m_LocalInputPhysicalAttachments =
                new int[attachmentInterface.ColorInputSlotCount];
            Array.Fill(
                m_LocalInputPhysicalAttachments,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            m_OutputPhysicalAttachments =
                new int[attachmentInterface.ColorOutputLocationCount];
            Array.Fill(
                m_OutputPhysicalAttachments,
                RHIAttachmentInterfaceSignature
                    .UnboundLogicalAttachment);
            m_PhysicalAttachmentsByMappingIndex =
                new ulong[RHIAttachmentIndexArray.MaxAttachments];
            Array.Fill(
                m_PhysicalAttachmentsByMappingIndex,
                ulong.MaxValue);

            for (int inputIndex = 0;
                 inputIndex < m_LocalInputPhysicalAttachments.Length;
                 ++inputIndex)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorInputLogicalAttachment(
                        inputIndex);
                if (logicalAttachment < 0 ||
                    IsMaskSet(
                        phaseRasterOrderedMask,
                        logicalAttachment))
                {
                    continue;
                }

                m_LocalInputPhysicalAttachments[inputIndex] =
                    logicalAttachment;
                AddMapping(
                    m_PhysicalAttachmentsByMappingIndex,
                    inputIndex,
                    logicalAttachment,
                    "framebuffer-local input");
            }

            bool hasNonIdentityOutputMapping = false;
            for (int outputLocation = 0;
                 outputLocation < m_OutputPhysicalAttachments.Length;
                 ++outputLocation)
            {
                int logicalAttachment =
                    attachmentInterface.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (logicalAttachment < 0)
                {
                    continue;
                }

                if (IsMaskSet(
                        phaseRasterOrderedMask,
                        logicalAttachment))
                {
                    hasNonIdentityOutputMapping = true;
                    continue;
                }

                if (IsMaskSet(
                        passRasterOrderedMask,
                        logicalAttachment))
                {
                    throw new NotSupportedException(
                        $"Metal output location {outputLocation} targets " +
                        $"logical attachment {logicalAttachment}, which is " +
                        "raster-ordered in another phase. A Metal ROG " +
                        "texture must never also be an ordinary color " +
                        "attachment in the same render pass.");
                }

                m_OutputPhysicalAttachments[outputLocation] =
                    logicalAttachment;
                AddMapping(
                    m_PhysicalAttachmentsByMappingIndex,
                    outputLocation,
                    logicalAttachment,
                    "color output");
                hasNonIdentityOutputMapping |=
                    outputLocation != logicalAttachment;
            }
            HasNonIdentityOutputMapping =
                hasNonIdentityOutputMapping;
        }

        internal int GetLocalInputPhysicalAttachment(
            int inputIndex)
        {
            if ((uint)inputIndex >=
                (uint)m_LocalInputPhysicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputIndex));
            }
            return m_LocalInputPhysicalAttachments[inputIndex];
        }

        internal ulong GetPhysicalOutputAttachmentIndex(
            int outputLocation)
        {
            if ((uint)outputLocation >=
                (uint)m_OutputPhysicalAttachments.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outputLocation));
            }
            int physicalAttachment =
                m_OutputPhysicalAttachments[outputLocation];
            return physicalAttachment < 0
                ? ulong.MaxValue
                : checked((ulong)physicalAttachment);
        }

        internal ulong GetPhysicalAttachmentForMappingIndex(
            int mappingIndex)
        {
            if ((uint)mappingIndex >=
                (uint)m_PhysicalAttachmentsByMappingIndex.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mappingIndex));
            }
            return m_PhysicalAttachmentsByMappingIndex[mappingIndex];
        }

        private static void AddMapping(
            ulong[] mappings,
            int mappingIndex,
            int physicalAttachment,
            string purpose)
        {
            ulong physical = checked((ulong)physicalAttachment);
            ulong existing = mappings[mappingIndex];
            if (existing != ulong.MaxValue &&
                existing != physical)
            {
                throw new NotSupportedException(
                    $"Metal cannot independently map {purpose} index " +
                    $"{mappingIndex} to physical attachment {physical} " +
                    $"because the same native mapping index already targets " +
                    $"{existing}.");
            }
            mappings[mappingIndex] = physical;
        }

        private static bool IsMaskSet(
            byte mask,
            int logicalAttachment) =>
            (mask & (1 << logicalAttachment)) != 0;
    }

    internal sealed class MetalRasterPassLowering
    {
        internal bool RequiresColorAttachmentMapping { get; }
        internal bool RequiresFramebufferLocalRead { get; }
        internal bool RequiresRasterOrderGroups { get; }
        internal byte OrdinaryAttachmentMask { get; }
        internal byte RasterOrderedAttachmentMask { get; }
        internal ReadOnlyMemory<MetalRasterSubPassLowering> SubPasses =>
            m_SubPasses;

        private readonly MetalRasterSubPassLowering[] m_SubPasses;

        private MetalRasterPassLowering(
            bool requiresColorAttachmentMapping,
            bool requiresFramebufferLocalRead,
            bool requiresRasterOrderGroups,
            byte ordinaryAttachmentMask,
            byte rasterOrderedAttachmentMask,
            MetalRasterSubPassLowering[] subPasses)
        {
            RequiresColorAttachmentMapping =
                requiresColorAttachmentMapping;
            RequiresFramebufferLocalRead =
                requiresFramebufferLocalRead;
            RequiresRasterOrderGroups = requiresRasterOrderGroups;
            OrdinaryAttachmentMask = ordinaryAttachmentMask;
            RasterOrderedAttachmentMask =
                rasterOrderedAttachmentMask;
            m_SubPasses = subPasses;
        }

        internal static MetalRasterPassLowering Compile(
            RasterPassPlan plan,
            in MetalRasterCapabilities capabilities)
        {
            ArgumentNullException.ThrowIfNull(plan);

            byte passRasterOrderedMask = 0;
            for (int i = 0; i < plan.SubPassCount; ++i)
            {
                passRasterOrderedMask |=
                    plan.GetSubPass(i)
                        .AttachmentInterface
                        .RasterOrderedReadWriteMask;
            }

            bool requiresOutputMapping = false;
            bool requiresFramebufferLocalRead = false;
            byte ordinaryAttachmentMask = 0;
            MetalRasterSubPassLowering[] subPasses =
                new MetalRasterSubPassLowering[plan.SubPassCount];
            for (int i = 0; i < subPasses.Length; ++i)
            {
                ref readonly RasterSubPassPlan subPass =
                    ref plan.GetSubPass(i);
                ValidateMetalAccess(in subPass, i);
                MetalRasterSubPassLowering lowering =
                    new MetalRasterSubPassLowering(
                        subPass,
                        passRasterOrderedMask);
                subPasses[i] = lowering;
                requiresOutputMapping |=
                    lowering.HasNonIdentityOutputMapping;
                requiresFramebufferLocalRead |=
                    lowering.HasLocalInputs;
                ordinaryAttachmentMask |= checked((byte)(
                    lowering.LocalInputMask |
                    lowering.OrdinaryOutputMask |
                    lowering.PreserveMask));
            }

            ordinaryAttachmentMask = checked((byte)(
                ordinaryAttachmentMask &
                ~passRasterOrderedMask));
            bool requiresMapping =
                requiresOutputMapping ||
                requiresFramebufferLocalRead;
            if (requiresOutputMapping &&
                !capabilities.ColorOutputMapping)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires logical-to-physical " +
                    "color-output mapping, but the exact native mapping " +
                    "mechanism was not independently probed.");
            }
            if (requiresFramebufferLocalRead &&
                !capabilities.FramebufferLocalRead)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires framebuffer-local color " +
                    "reads, but the exact MSL [[color(n)]] attachment-read " +
                    "mechanism was not independently probed.");
            }
            if (passRasterOrderedMask != 0 &&
                !capabilities.RasterOrderGroups)
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires raster order groups, but " +
                    "MTLDevice.rasterOrderGroupsSupported is false.");
            }
            if (requiresMapping &&
                (!capabilities.PassMappingSelector ||
                 !capabilities.MapEntrySelector ||
                 !capabilities.EncoderMappingSelector))
            {
                throw new NotSupportedException(
                    "The Metal raster pass requires attachment mapping, but " +
                    "one or more required native selectors are unavailable.");
            }

            ValidateMemorylessAttachments(plan);
            return new MetalRasterPassLowering(
                requiresMapping,
                requiresFramebufferLocalRead,
                passRasterOrderedMask != 0,
                ordinaryAttachmentMask,
                passRasterOrderedMask,
                subPasses);
        }

        private static void ValidateMetalAccess(
            in RasterSubPassPlan subPass,
            int subPassIndex)
        {
            if (subPass.AttachmentInterface.SampledFeedbackMask != 0)
            {
                throw new NotSupportedException(
                    $"Metal subpass {subPassIndex} cannot prove sampled " +
                    "attachment feedback. SampledFeedback remains an " +
                    "ordinary shader resource and has no private Metal " +
                    "attachment binding until an exact native strategy is " +
                    "qualified.");
            }
        }

        private static void ValidateMemorylessAttachments(
            RasterPassPlan plan)
        {
            for (int i = 0; i < plan.ColorAttachmentCount; ++i)
            {
                ref readonly RHIColorAttachmentDescriptor attachment =
                    ref plan.GetColorAttachment(i);
                if (attachment.RenderTarget.Descriptor.StorageMode !=
                    ERHIStorageMode.Memoryless)
                {
                    continue;
                }

                if (attachment.LoadAction == ERHILoadAction.Load)
                {
                    throw new NotSupportedException(
                        $"Memoryless color attachment {i} cannot use Load.");
                }
                if (attachment.StoreAction != ERHIStoreAction.DontCare &&
                    attachment.StoreAction != ERHIStoreAction.Resolve)
                {
                    throw new NotSupportedException(
                        $"Memoryless color attachment {i} must use DontCare " +
                        "or Resolve store action.");
                }
            }

            if (!plan.HasDepthStencilAttachment)
            {
                return;
            }

            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            if (depthStencil.RenderTarget.Descriptor.StorageMode !=
                ERHIStorageMode.Memoryless)
            {
                return;
            }
            if (depthStencil.DepthLoadOp == ERHILoadAction.Load ||
                depthStencil.StencilLoadOp == ERHILoadAction.Load)
            {
                throw new NotSupportedException(
                    "A memoryless depth/stencil attachment cannot use Load.");
            }
            if (!IsMemorylessStoreAction(depthStencil.DepthStoreOp) ||
                !IsMemorylessStoreAction(depthStencil.StencilStoreOp))
            {
                throw new NotSupportedException(
                    "A memoryless depth/stencil attachment must use DontCare " +
                    "or Resolve independently for each selected aspect.");
            }
        }

        private static bool IsMemorylessStoreAction(
            ERHIStoreAction storeAction) =>
            storeAction == ERHIStoreAction.DontCare ||
            storeAction == ERHIStoreAction.Resolve;
    }
}

namespace SharpGPU
{
    internal readonly struct MetalPrivateRasterBindingPlan
    {
        internal uint TextureBase { get; }
        internal uint MaximumTextureBindings { get; }
        internal uint RequiredTextureBindingCount { get; }
        internal byte RasterOrderedMask { get; }

        internal bool HasRasterOrderedBindings =>
            RasterOrderedMask != 0;

        private MetalPrivateRasterBindingPlan(
            uint textureBase,
            uint maximumTextureBindings,
            uint requiredTextureBindingCount,
            byte rasterOrderedMask)
        {
            TextureBase = textureBase;
            MaximumTextureBindings = maximumTextureBindings;
            RequiredTextureBindingCount = requiredTextureBindingCount;
            RasterOrderedMask = rasterOrderedMask;
        }

        internal uint GetRasterOrderedTextureIndex(
            int logicalAttachment)
        {
            if ((uint)logicalAttachment >=
                RHIAttachmentIndexArray.MaxAttachments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalAttachment));
            }
            byte bit =
                checked((byte)(1 << logicalAttachment));
            if ((RasterOrderedMask & bit) == 0)
            {
                throw new InvalidOperationException(
                    $"Logical attachment {logicalAttachment} is not " +
                    "raster-ordered in this pipeline.");
            }
            uint index = checked(
                TextureBase +
                checked((uint)logicalAttachment));
            if (index >= MaximumTextureBindings)
            {
                throw new InvalidOperationException(
                    $"Metal private texture index {index} exceeds the " +
                    $"qualified fragment texture limit " +
                    $"{MaximumTextureBindings}.");
            }
            return index;
        }

        internal static MetalPrivateRasterBindingPlan Compile(
            MetalDevice device,
            MetalPipelineLayout pipelineLayout,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(pipelineLayout);
            ValidatePipelineLayoutIdentity(
                pipelineLayout.IsDisposed,
                pipelineLayout.Device,
                device);
            if (!device.Capabilities.Binding.DescriptorIndexing.Limits
                    .TryGetValue(
                        ERHICapabilityLimitKind.MaximumBoundTextures,
                        out ulong maximumTextureBindings) ||
                maximumTextureBindings == 0 ||
                maximumTextureBindings > uint.MaxValue)
            {
                throw new NotSupportedException(
                    "The Metal device did not publish a usable " +
                    "MaximumBoundTextures capability limit.");
            }

            return Compile(
                pipelineLayout.ArgumentTableLayouts,
                checked((uint)maximumTextureBindings),
                in attachmentInterface);
        }

        internal static MetalPrivateRasterBindingPlan Compile(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            uint maximumTextureBindings,
            in RHIAttachmentInterfaceSignature attachmentInterface)
        {
            if (maximumTextureBindings == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumTextureBindings),
                    maximumTextureBindings,
                    "Metal must expose at least one fragment texture binding.");
            }

            uint textureBase = GetOrdinaryTextureRangeEnd(layouts);
            byte rasterOrderedMask =
                attachmentInterface.RasterOrderedReadWriteMask;
            uint requiredTextureBindingCount = textureBase;
            if (rasterOrderedMask != 0)
            {
                int highestLogicalAttachment =
                    HighestSetBit(rasterOrderedMask);
                uint highestPrivateIndex = checked(
                    textureBase +
                    checked((uint)highestLogicalAttachment));
                if (highestPrivateIndex >= maximumTextureBindings)
                {
                    throw new NotSupportedException(
                        $"Metal ordinary texture bindings consume indices " +
                        $"through {Math.Max(0, (long)textureBase - 1)}; " +
                        $"raster-ordered logical attachment " +
                        $"{highestLogicalAttachment} would require private " +
                        $"index {highestPrivateIndex}, exceeding the " +
                        $"qualified limit {maximumTextureBindings}.");
                }
                requiredTextureBindingCount =
                    checked(highestPrivateIndex + 1);
            }

            return new MetalPrivateRasterBindingPlan(
                textureBase,
                maximumTextureBindings,
                requiredTextureBindingCount,
                rasterOrderedMask);
        }

        internal static uint GetOrdinaryTextureRangeEnd(
            ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            uint nextIndex = 0;
            for (int layoutIndex = 0;
                 layoutIndex < layouts.Length;
                 ++layoutIndex)
            {
                MetalArgumentTableLayout layout =
                    layouts[layoutIndex]
                    ?? throw new ArgumentException(
                        $"Metal pipeline layout slot {layoutIndex} is null.",
                        nameof(layouts));
                if (layout.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        $"MetalArgumentTableLayout[{layout.Index}]");
                }

                ReadOnlySpan<MetalBindInfo> bindings =
                    layout.BindInfos;
                for (int bindingIndex = 0;
                     bindingIndex < bindings.Length;
                     ++bindingIndex)
                {
                    ref readonly MetalBindInfo binding =
                        ref bindings[bindingIndex];
                    if (MetalArgumentTableValidation
                            .GetDirectBindingNamespace(
                                binding.Type) !=
                        MetalArgumentTableValidation
                            .BindingNamespace.Texture)
                    {
                        continue;
                    }

                    nextIndex = Math.Max(
                        nextIndex,
                        checked(binding.Slot + binding.Count));
                }
            }
            return nextIndex;
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
                    nameof(MetalPipelineLayout));
            }
            if (!ReferenceEquals(actualDevice, expectedDevice))
            {
                throw new ArgumentException(
                    "Metal pipeline layout belongs to a different device.",
                    nameof(actualDevice));
            }
        }

        private static int HighestSetBit(byte mask)
        {
            for (int bit = 7; bit >= 0; --bit)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    return bit;
                }
            }
            throw new ArgumentOutOfRangeException(
                nameof(mask),
                mask,
                "A non-empty mask is required.");
        }
    }
}

namespace SharpGPU
{
    internal readonly struct MetalVertexBufferBinding
    {
        internal uint LogicalIndex { get; }
        internal ulong PhysicalIndex { get; }
        internal uint Stride { get; }

        internal MetalVertexBufferBinding(in uint logicalIndex, in ulong physicalIndex, in uint stride)
        {
            LogicalIndex = logicalIndex;
            PhysicalIndex = physicalIndex;
            Stride = stride;
        }
    }

    internal sealed class MetalRasterBufferBindingPlan
    {
        private readonly Dictionary<uint, MetalVertexBufferBinding> m_VertexBindings;

        internal IReadOnlyDictionary<uint, MetalVertexBufferBinding> VertexBindings => m_VertexBindings;

        internal MetalRasterBufferBindingPlan(
            Dictionary<uint, MetalVertexBufferBinding> vertexBindings)
        {
            m_VertexBindings = vertexBindings;
        }

        internal MetalVertexBufferBinding GetVertexBinding(in uint logicalIndex)
        {
            if (m_VertexBindings.TryGetValue(logicalIndex, out MetalVertexBufferBinding binding))
            {
                return binding;
            }

            throw new InvalidOperationException(
                $"Metal raster pipeline has no vertex-buffer layout for logical slot {logicalIndex}.");
        }
    }

    internal static class MetalBufferBindingPlanner
    {
        internal const int MaxBufferBindCount = 31;
        internal const int MaxBufferIndex = MaxBufferBindCount - 1;

        internal static void ValidatePipelineBufferBudget(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }
        }

        internal static MetalRasterBufferBindingPlan CompileRaster(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            ReadOnlySpan<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            Dictionary<uint, MetalVertexBufferBinding> bindings = new(vertexLayouts.Length);

            int nextPhysicalIndex = MaxBufferIndex;
            for (int layoutIndex = 0; layoutIndex < vertexLayouts.Length; ++layoutIndex)
            {
                ref readonly RHIVertexLayoutDescriptor layout = ref vertexLayouts[layoutIndex];
                if (bindings.ContainsKey(layout.Index))
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline contains duplicate vertex-buffer logical slot {layout.Index}.");
                }

                while (nextPhysicalIndex >= 0 && occupied[nextPhysicalIndex])
                {
                    --nextPhysicalIndex;
                }

                if (nextPhysicalIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline requires {vertexLayouts.Length} vertex buffers, but the shared "
                        + $"Metal 4 buffer namespace has no free slot in [0, {MaxBufferIndex}] after shader resources.");
                }

                occupied[nextPhysicalIndex] = true;
                bindings.Add(
                    layout.Index,
                    new MetalVertexBufferBinding(
                        layout.Index,
                        checked((ulong)nextPhysicalIndex),
                        layout.Stride));
                --nextPhysicalIndex;
            }

            return new MetalRasterBufferBindingPlan(bindings);
        }

        internal static ulong GetDirectArgumentTableBufferBindCount(
            MetalArgumentTableLayout layout,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            ReserveDirectBufferBindings(occupied, layout);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReferenceRootBufferBindCount(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildReferenceBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReservedBufferOnlyBindCount(in MetalBindingPipelineType pipelineType)
        {
            return pipelineType switch
            {
                MetalBindingPipelineType.Raster or MetalBindingPipelineType.Raytracing =>
                    MaxBufferBindCount,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pipelineType),
                    pipelineType,
                    "Only Metal raster and ray tracing pipelines use reserved-only argument tables.")
            };
        }

        private static bool[] BuildPipelineBufferOccupancy(
            ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            if (MetalBindingHelpers.RequiresReferenceBuffers(layouts))
            {
                return BuildReferenceBufferOccupancy(layouts);
            }

            bool[] occupied = new bool[MaxBufferBindCount];
            if (layouts.Length == 1)
            {
                ReserveDirectBufferBindings(occupied, layouts[0]);
            }

            return occupied;
        }

        private static bool[] BuildReferenceBufferOccupancy(
            ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            for (int index = 0; index < layouts.Length; ++index)
            {
                MetalArgumentTableLayout layout = layouts[index];
                Reserve(
                    occupied,
                    layout.Index,
                    $"argument-table reference buffer {layout.Index}");
            }

            return occupied;
        }

        private static void ReserveDirectBufferBindings(
            bool[] occupied,
            MetalArgumentTableLayout layout)
        {
            ReadOnlySpan<MetalBindInfo> binds = layout.BindInfos;
            for (int index = 0; index < binds.Length; ++index)
            {
                ref readonly MetalBindInfo bind = ref binds[index];
                if (MetalArgumentTableValidation.GetDirectBindingNamespace(bind.Type)
                    != MetalArgumentTableValidation.BindingNamespace.Buffer)
                {
                    continue;
                }

                ulong end = checked((ulong)bind.Slot + bind.Count);
                if (end > MaxBufferBindCount)
                {
                    throw new InvalidOperationException(
                        $"Metal argument table {layout.Index} binding slot={bind.Slot}, type={bind.Type}, count={bind.Count} "
                        + $"exceeds the Metal 4 buffer index range [0, {MaxBufferIndex}].");
                }

                for (ulong physicalIndex = bind.Slot; physicalIndex < end; ++physicalIndex)
                {
                    Reserve(
                        occupied,
                        physicalIndex,
                        $"argument table {layout.Index} {bind.Type} binding");
                }
            }
        }

        private static void Reserve(bool[] occupied, in ulong physicalIndex, string owner)
        {
            if (physicalIndex > MaxBufferIndex)
            {
                throw new InvalidOperationException(
                    $"Metal {owner} uses buffer index {physicalIndex}, outside the Metal 4 range [0, {MaxBufferIndex}].");
            }

            int index = checked((int)physicalIndex);
            if (occupied[index])
            {
                throw new InvalidOperationException(
                    $"Metal {owner} collides at shared Metal 4 buffer index {physicalIndex}.");
            }

            occupied[index] = true;
        }

        private static ulong GetRequiredBindCount(bool[] occupied)
        {
            for (int index = occupied.Length - 1; index >= 0; --index)
            {
                if (occupied[index])
                {
                    return checked((ulong)index + 1UL);
                }
            }

            return 0;
        }
    }
}
