using System;

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
