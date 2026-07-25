using System;

namespace SharpGPU
{
    internal sealed unsafe class
        VulkanDynamicRenderingAttachmentMappingPlan
    {
        internal const uint AttachmentUnused = uint.MaxValue;

        internal int ColorAttachmentCount =>
            m_OutputLocationsByPhysicalAttachment.Length;
        internal bool HasLocalReadOrRemapping { get; }

        private readonly uint[]
            m_OutputLocationsByPhysicalAttachment;
        private readonly uint[]
            m_InputIndicesByPhysicalAttachment;

        private VulkanDynamicRenderingAttachmentMappingPlan(
            uint[] outputLocationsByPhysicalAttachment,
            uint[] inputIndicesByPhysicalAttachment,
            bool hasLocalReadOrRemapping)
        {
            m_OutputLocationsByPhysicalAttachment =
                outputLocationsByPhysicalAttachment;
            m_InputIndicesByPhysicalAttachment =
                inputIndicesByPhysicalAttachment;
            HasLocalReadOrRemapping = hasLocalReadOrRemapping;
        }

        internal static
            VulkanDynamicRenderingAttachmentMappingPlan Compile(
                in RHIAttachmentInterfaceSignature signature)
        {
            int colorCount = signature.ColorAttachmentCount;
            uint[] outputs = new uint[colorCount];
            uint[] inputs = new uint[colorCount];
            Array.Fill(outputs, AttachmentUnused);
            Array.Fill(inputs, AttachmentUnused);

            byte rasterOrderedMask =
                signature.RasterOrderedReadWriteMask;
            bool requiresMapping = false;
            for (int outputLocation = 0;
                 outputLocation <
                    signature.ColorOutputLocationCount;
                 ++outputLocation)
            {
                int logicalAttachment =
                    signature.GetColorOutputLogicalAttachment(
                        outputLocation);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    outputs[logicalAttachment] =
                        checked((uint)outputLocation);
                }
                requiresMapping |=
                    logicalAttachment != outputLocation;
            }

            for (int inputIndex = 0;
                 inputIndex <
                    signature.ColorInputSlotCount;
                 ++inputIndex)
            {
                int logicalAttachment =
                    signature.GetColorInputLogicalAttachment(
                        inputIndex);
                if (IsRasterOrdered(
                        logicalAttachment,
                        rasterOrderedMask))
                {
                    logicalAttachment =
                        RHIAttachmentInterfaceSignature
                            .UnboundLogicalAttachment;
                }
                if (logicalAttachment >= 0)
                {
                    inputs[logicalAttachment] =
                        checked((uint)inputIndex);
                    requiresMapping = true;
                }
            }

            for (int physicalAttachment = 0;
                 physicalAttachment < colorCount;
                 ++physicalAttachment)
            {
                requiresMapping |=
                    outputs[physicalAttachment] !=
                        checked((uint)physicalAttachment) ||
                    inputs[physicalAttachment] != AttachmentUnused;
            }

            return new(
                outputs,
                inputs,
                requiresMapping);
        }

        internal void Populate(
            uint* outputLocations,
            uint* inputIndices)
        {
            if (ColorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < ColorAttachmentCount;
                 ++physicalAttachment)
            {
                outputLocations[physicalAttachment] =
                    m_OutputLocationsByPhysicalAttachment[
                        physicalAttachment];
                inputIndices[physicalAttachment] =
                    m_InputIndicesByPhysicalAttachment[
                        physicalAttachment];
            }
        }

        internal static void Populate(
            in VulkanRasterSubPassLowering subPass,
            int colorAttachmentCount,
            uint* outputLocations,
            uint* inputIndices)
        {
            if (colorAttachmentCount != 0 &&
                (outputLocations == null || inputIndices == null))
            {
                throw new ArgumentNullException(
                    outputLocations == null
                        ? nameof(outputLocations)
                        : nameof(inputIndices));
            }
            for (int physicalAttachment = 0;
                 physicalAttachment < colorAttachmentCount;
                 ++physicalAttachment)
            {
                int outputLocation =
                    subPass.GetOutputLocationForPhysicalAttachment(
                        physicalAttachment);
                int inputIndex =
                    subPass.GetInputIndexForPhysicalAttachment(
                        physicalAttachment);
                outputLocations[physicalAttachment] =
                    outputLocation < 0
                        ? AttachmentUnused
                        : checked((uint)outputLocation);
                inputIndices[physicalAttachment] =
                    inputIndex < 0
                        ? AttachmentUnused
                        : checked((uint)inputIndex);
            }
        }

        private static bool IsRasterOrdered(
            int logicalAttachment,
            byte rasterOrderedMask) =>
            logicalAttachment >= 0 &&
            (rasterOrderedMask &
             (1 << logicalAttachment)) != 0;
    }
}
