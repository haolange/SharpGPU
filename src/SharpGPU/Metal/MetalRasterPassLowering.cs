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
