using System;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal sealed class VulkanRenderPass2Plan
    {
        internal VkRenderPass NativeRenderPass { get; }
        internal int[] ColorResolveAttachmentIndices { get; }
        internal int DepthStencilAttachmentIndex { get; }
        internal int DepthStencilResolveAttachmentIndex { get; }
        internal int AttachmentCount { get; }
        internal bool UsesKhrEntryPoints { get; }

        internal VulkanRenderPass2Plan(
            VkRenderPass nativeRenderPass,
            int[] colorResolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex,
            int attachmentCount,
            bool usesKhrEntryPoints)
        {
            NativeRenderPass = nativeRenderPass;
            ColorResolveAttachmentIndices =
                colorResolveAttachmentIndices;
            DepthStencilAttachmentIndex =
                depthStencilAttachmentIndex;
            DepthStencilResolveAttachmentIndex =
                depthStencilResolveAttachmentIndex;
            AttachmentCount = attachmentCount;
            UsesKhrEntryPoints = usesKhrEntryPoints;
        }
    }

    internal static unsafe class VulkanRenderPass2Lowering
    {
        private const uint AttachmentUnused = uint.MaxValue;

        internal static VulkanRenderPass2Plan Create(
            VkDevice device,
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            bool useKhrEntryPoints)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(lowering);
            VulkanRenderPass2Description description =
                VulkanRenderPass2Description.Compile(plan, lowering);

            int[] resolveAttachmentIndices =
                new int[plan.ColorAttachmentCount];
            Array.Fill(resolveAttachmentIndices, -1);
            int attachmentCount = plan.ColorAttachmentCount;
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                if (color.ResolveTarget != null)
                {
                    resolveAttachmentIndices[colorIndex] =
                        attachmentCount++;
                }
            }

            int depthStencilAttachmentIndex = -1;
            int depthStencilResolveAttachmentIndex = -1;
            if (plan.HasDepthStencilAttachment)
            {
                depthStencilAttachmentIndex = attachmentCount++;
                if (plan.GetDepthStencilAttachment().ResolveTarget != null)
                {
                    depthStencilResolveAttachmentIndex =
                        attachmentCount++;
                }
            }

            VkAttachmentDescription2* attachments =
                Allocate<VkAttachmentDescription2>(attachmentCount);
            VkSubpassDescription2* subpasses =
                Allocate<VkSubpassDescription2>(plan.SubPassCount);
            VkSubpassDependency2* dependencies =
                Allocate<VkSubpassDependency2>(
                    checked(plan.SubPassCount * plan.SubPassCount));
            VkAttachmentReference2* inputReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* colorReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* resolveReferences =
                Allocate<VkAttachmentReference2>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            uint* preserveReferences =
                Allocate<uint>(
                    plan.SubPassCount *
                    RHIAttachmentIndexArray.MaxAttachments);
            VkAttachmentReference2* depthReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkAttachmentReference2* depthResolveReferences =
                Allocate<VkAttachmentReference2>(plan.SubPassCount);
            VkSubpassDescriptionDepthStencilResolve*
                depthResolveDescriptions =
                    Allocate<VkSubpassDescriptionDepthStencilResolve>(
                        plan.SubPassCount);

            try
            {
                PopulateAttachmentDescriptions(
                    plan,
                    lowering,
                    attachments,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                VulkanRenderPass2DescriptionLowering.PopulateSubpasses(
                    plan,
                    description,
                    subpasses,
                    inputReferences,
                    colorReferences,
                    resolveReferences,
                    preserveReferences,
                    depthReferences,
                    depthResolveReferences,
                    depthResolveDescriptions,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex);
                int dependencyCount = PopulateDependencies(
                    plan,
                    description,
                    dependencies);

                VkRenderPassCreateInfo2 createInfo = new()
                {
                    sType = VkStructureType.RenderPassCreateInfo2,
                    attachmentCount = checked((uint)attachmentCount),
                    pAttachments = attachments,
                    subpassCount = checked((uint)plan.SubPassCount),
                    pSubpasses = subpasses,
                    dependencyCount =
                        checked((uint)dependencyCount),
                    pDependencies = dependencies,
                };
                VkRenderPass renderPass;
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateRenderPass2(
                        device,
                        &createInfo,
                        null,
                        &renderPass,
                        useKhrEntryPoints));
                return new VulkanRenderPass2Plan(
                    renderPass,
                    resolveAttachmentIndices,
                    depthStencilAttachmentIndex,
                    depthStencilResolveAttachmentIndex,
                    attachmentCount,
                    useKhrEntryPoints);
            }
            finally
            {
                NativeMemory.Free(attachments);
                NativeMemory.Free(subpasses);
                NativeMemory.Free(dependencies);
                NativeMemory.Free(inputReferences);
                NativeMemory.Free(colorReferences);
                NativeMemory.Free(resolveReferences);
                NativeMemory.Free(preserveReferences);
                NativeMemory.Free(depthReferences);
                NativeMemory.Free(depthResolveReferences);
                NativeMemory.Free(depthResolveDescriptions);
            }
        }

        private static void PopulateAttachmentDescriptions(
            RasterPassPlan plan,
            VulkanRasterPassLowering lowering,
            VkAttachmentDescription2* attachments,
            int[] resolveAttachmentIndices,
            int depthStencilAttachmentIndex,
            int depthStencilResolveAttachmentIndex)
        {
            for (int colorIndex = 0;
                 colorIndex < plan.ColorAttachmentCount;
                 ++colorIndex)
            {
                ref readonly RHIColorAttachmentDescriptor color =
                    ref plan.GetColorAttachment(colorIndex);
                attachments[colorIndex] = new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            color.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            color.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            color.LoadAction),
                    storeOp =
                        color.StoreAction ==
                            ERHIStoreAction.StoreAndResolve ||
                        color.StoreAction == ERHIStoreAction.Store
                            ? VkAttachmentStoreOp.Store
                            : VkAttachmentStoreOp.DontCare,
                    stencilLoadOp = VkAttachmentLoadOp.DontCare,
                    stencilStoreOp = VkAttachmentStoreOp.DontCare,
                    initialLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.ColorAttachmentOptimal,
                };

                int resolveIndex =
                    resolveAttachmentIndices[colorIndex];
                if (resolveIndex >= 0)
                {
                    attachments[resolveIndex] =
                        new VkAttachmentDescription2
                        {
                            sType =
                                VkStructureType.AttachmentDescription2,
                            format =
                                VulkanUtility.ConvertToVkFormat(
                                    color.ResolveTarget!.Descriptor.Format),
                            samples = VkSampleCountFlags.Count1,
                            loadOp = VkAttachmentLoadOp.DontCare,
                            storeOp = VkAttachmentStoreOp.Store,
                            stencilLoadOp =
                                VkAttachmentLoadOp.DontCare,
                            stencilStoreOp =
                                VkAttachmentStoreOp.DontCare,
                            initialLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                            finalLayout =
                                VkImageLayout.ColorAttachmentOptimal,
                        };
                }
            }

            if (depthStencilAttachmentIndex < 0)
            {
                return;
            }
            RHIDepthStencilAttachmentDescriptor depthStencil =
                plan.GetDepthStencilAttachment();
            attachments[depthStencilAttachmentIndex] =
                new VkAttachmentDescription2
                {
                    sType = VkStructureType.AttachmentDescription2,
                    format =
                        VulkanUtility.ConvertToVkFormat(
                            depthStencil.RenderTarget.Descriptor.Format),
                    samples =
                        VulkanUtility.ConvertToVkSampleCount(
                            depthStencil.RenderTarget.Descriptor.SampleCount),
                    loadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.DepthLoadOp),
                    storeOp =
                        ConvertSourceStoreOp(
                            depthStencil.DepthStoreOp),
                    stencilLoadOp =
                        VulkanUtility.ConvertToVkLoadOp(
                            depthStencil.StencilLoadOp),
                    stencilStoreOp =
                        ConvertSourceStoreOp(
                            depthStencil.StencilStoreOp),
                    initialLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                    finalLayout =
                        VkImageLayout.DepthStencilAttachmentOptimal,
                };

            if (depthStencilResolveAttachmentIndex >= 0)
            {
                attachments[depthStencilResolveAttachmentIndex] =
                    new VkAttachmentDescription2
                    {
                        sType =
                            VkStructureType.AttachmentDescription2,
                        format =
                            VulkanUtility.ConvertToVkFormat(
                                depthStencil.ResolveTarget!.Descriptor
                                    .Format),
                        samples = VkSampleCountFlags.Count1,
                        loadOp = VkAttachmentLoadOp.DontCare,
                        storeOp = VkAttachmentStoreOp.Store,
                        stencilLoadOp = VkAttachmentLoadOp.DontCare,
                        stencilStoreOp = VkAttachmentStoreOp.Store,
                        initialLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                        finalLayout =
                            VkImageLayout.DepthStencilAttachmentOptimal,
                    };
            }
        }

        private static int PopulateDependencies(
            RasterPassPlan plan,
            VulkanRenderPass2Description description,
            VkSubpassDependency2* dependencies)
        {
            int dependencyCount = 0;
            ReadOnlySpan<VulkanRenderPass2SubPassDescription> subPasses =
                description.SubPasses.Span;
            for (int sourceIndex = 0;
                 sourceIndex < subPasses.Length;
                 ++sourceIndex)
            {
                VulkanRenderPass2SubPassDescription source =
                    subPasses[sourceIndex];
                for (int destinationIndex = sourceIndex;
                     destinationIndex < subPasses.Length;
                     ++destinationIndex)
                {
                    VulkanRenderPass2SubPassDescription destination =
                        subPasses[destinationIndex];
                    byte destinationColorAccessMask =
                        checked((byte)(
                            destination.InputMask |
                            destination.OutputMask |
                            destination.SampledFeedbackMask));
                    byte colorHazardMask =
                        checked((byte)(
                            source.OutputMask &
                            destinationColorAccessMask));
                    bool selfDependency =
                        sourceIndex == destinationIndex;
                    bool ordinaryReadWrite =
                        selfDependency &&
                        source.OrdinaryReadWriteMask != 0;
                    bool sampledFeedback =
                        (colorHazardMask &
                         destination.SampledFeedbackMask) != 0;
                    bool colorHazard =
                        selfDependency
                            ? ordinaryReadWrite ||
                              sampledFeedback
                            : colorHazardMask != 0;
                    bool depthStencilHazard =
                        !selfDependency &&
                        plan.HasDepthStencilAttachment &&
                        WritesDepthOrStencil(
                            source.DepthStencilFlags);
                    if (!colorHazard && !depthStencilHazard)
                    {
                        continue;
                    }

                    VkPipelineStageFlags sourceStages = 0;
                    VkPipelineStageFlags destinationStages = 0;
                    VkAccessFlags sourceAccess = 0;
                    VkAccessFlags destinationAccess = 0;
                    if (colorHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .ColorAttachmentOutput;
                        sourceAccess |=
                            VkAccessFlags.ColorAttachmentWrite;
                        if ((colorHazardMask &
                             (destination.InputMask |
                              destination.SampledFeedbackMask)) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags.FragmentShader;
                            destinationAccess |=
                                VkAccessFlags.InputAttachmentRead |
                                VkAccessFlags.ShaderRead;
                        }
                        if ((colorHazardMask &
                             destination.OutputMask) != 0)
                        {
                            destinationStages |=
                                VkPipelineStageFlags
                                    .ColorAttachmentOutput;
                            destinationAccess |=
                                VkAccessFlags.ColorAttachmentRead |
                                VkAccessFlags.ColorAttachmentWrite;
                        }
                    }
                    if (depthStencilHazard)
                    {
                        sourceStages |=
                            VkPipelineStageFlags
                                .LateFragmentTests;
                        destinationStages |=
                            VkPipelineStageFlags
                                .EarlyFragmentTests;
                        sourceAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                        destinationAccess |=
                            VkAccessFlags
                                .DepthStencilAttachmentRead |
                            VkAccessFlags
                                .DepthStencilAttachmentWrite;
                    }

                    VkDependencyFlags flags =
                        VkDependencyFlags.ByRegion;
                    if (sampledFeedback)
                    {
                        flags |=
                            VkDependencyFlags.FeedbackLoopEXT;
                    }
                    dependencies[dependencyCount++] =
                        new VkSubpassDependency2
                        {
                            sType =
                                VkStructureType.SubpassDependency2,
                            srcSubpass =
                                checked((uint)sourceIndex),
                            dstSubpass =
                                checked((uint)destinationIndex),
                            srcStageMask = sourceStages,
                            dstStageMask = destinationStages,
                            srcAccessMask = sourceAccess,
                            dstAccessMask = destinationAccess,
                            dependencyFlags = flags,
                        };
                }
            }
            return dependencyCount;
        }

        private static bool WritesDepthOrStencil(
            ERHISubPassFlags flags) =>
            flags != ERHISubPassFlags.ReadOnlyDepthStencil;
        private static VkAttachmentReference2
            CreateAttachmentReference(
                uint attachment,
                VkImageLayout layout,
                VkImageAspectFlags aspects) =>
            new()
            {
                sType = VkStructureType.AttachmentReference2,
                attachment = attachment,
                layout = layout,
                aspectMask = aspects,
            };

        private static VkAttachmentStoreOp ConvertSourceStoreOp(
            ERHIStoreAction action) =>
            action == ERHIStoreAction.Store ||
            action == ERHIStoreAction.StoreAndResolve
                ? VkAttachmentStoreOp.Store
                : VkAttachmentStoreOp.DontCare;

        private static VkResolveModeFlags ConvertResolveMode(
            EResolveMode mode) => mode switch
        {
            EResolveMode.None => VkResolveModeFlags.None,
            EResolveMode.Sample0 => VkResolveModeFlags.SampleZero,
            EResolveMode.Min => VkResolveModeFlags.Min,
            EResolveMode.Max => VkResolveModeFlags.Max,
            _ => VkResolveModeFlags.Average,
        };

        private static T* Allocate<T>(int count)
            where T : unmanaged =>
            count == 0
                ? null
                : (T*)NativeMemory.AllocZeroed(
                    checked((nuint)count),
                    (nuint)sizeof(T));
    }

    internal static class VulkanRasterAspectExtensions
    {
        internal static VkImageAspectFlags ToVkImageAspectFlags(
            this ERHITextureAspectMask aspects)
        {
            VkImageAspectFlags result = 0;
            if ((aspects & ERHITextureAspectMask.Color) != 0)
            {
                result |= VkImageAspectFlags.Color;
            }
            if ((aspects & ERHITextureAspectMask.Depth) != 0)
            {
                result |= VkImageAspectFlags.Depth;
            }
            if ((aspects & ERHITextureAspectMask.Stencil) != 0)
            {
                result |= VkImageAspectFlags.Stencil;
            }
            return result;
        }
    }
}
