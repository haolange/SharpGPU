using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal readonly struct VulkanImageSubresourceKey :
        IEquatable<VulkanImageSubresourceKey>
    {
        internal readonly ulong ImageHandle;
        internal readonly VkImageAspectFlags Aspect;
        internal readonly uint MipLevel;
        internal readonly uint ArrayLayer;

        internal VulkanImageSubresourceKey(
            VkImage image,
            VkImageAspectFlags aspect,
            uint mipLevel,
            uint arrayLayer)
        {
            ImageHandle = image.Handle;
            Aspect = aspect;
            MipLevel = mipLevel;
            ArrayLayer = arrayLayer;
        }

        public bool Equals(VulkanImageSubresourceKey other) =>
            ImageHandle == other.ImageHandle &&
            Aspect == other.Aspect &&
            MipLevel == other.MipLevel &&
            ArrayLayer == other.ArrayLayer;

        public override bool Equals(object? value) =>
            value is VulkanImageSubresourceKey other &&
            Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                ImageHandle,
                (uint)Aspect,
                MipLevel,
                ArrayLayer);
    }

    internal readonly struct VulkanImageLayoutOverlayMutation
    {
        internal readonly VulkanImageSubresourceKey Key;
        internal readonly bool HadPreviousValue;
        internal readonly VkImageLayout PreviousValue;

        internal VulkanImageLayoutOverlayMutation(
            in VulkanImageSubresourceKey key,
            bool hadPreviousValue,
            VkImageLayout previousValue)
        {
            Key = key;
            HadPreviousValue = hadPreviousValue;
            PreviousValue = previousValue;
        }
    }

    internal sealed class VulkanCommandBufferImageLayoutOverlay
    {
        private readonly Dictionary<
            VulkanImageSubresourceKey,
            VkImageLayout> m_Layouts = new(32);
        private readonly List<
            VulkanImageLayoutOverlayMutation> m_Mutations = new(64);

        internal int CaptureCheckpoint() => m_Mutations.Count;

        internal void Clear()
        {
            m_Layouts.Clear();
            m_Mutations.Clear();
        }

        internal void Rollback(int checkpoint)
        {
            if ((uint)checkpoint > (uint)m_Mutations.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            for (int index = m_Mutations.Count - 1;
                 index >= checkpoint;
                 --index)
            {
                VulkanImageLayoutOverlayMutation mutation =
                    m_Mutations[index];
                if (mutation.HadPreviousValue)
                {
                    m_Layouts[mutation.Key] =
                        mutation.PreviousValue;
                }
                else
                {
                    m_Layouts.Remove(mutation.Key);
                }
            }
            if (checkpoint != m_Mutations.Count)
            {
                m_Mutations.RemoveRange(
                    checkpoint,
                    m_Mutations.Count - checkpoint);
            }
        }

        internal void ValidateDeclaredLayout(
            VkImage image,
            in RHITextureSubresourceRange range,
            VkImageLayout declaredLayout)
        {
            VisitRange(
                image,
                in range,
                (in VulkanImageSubresourceKey key) =>
                {
                    if (m_Layouts.TryGetValue(
                            key,
                            out VkImageLayout knownLayout) &&
                        knownLayout != declaredLayout)
                    {
                        throw new InvalidOperationException(
                            "The texture barrier LayoutBefore does " +
                            "not match the layout previously recorded " +
                            "for the same exact subresource in this " +
                            "command buffer.");
                    }
                });
        }

        internal void RequireKnownLayout(
            VkImage image,
            in RHITextureSubresourceRange range,
            VkImageLayout requiredLayout,
            string operation)
        {
            VisitRange(
                image,
                in range,
                (in VulkanImageSubresourceKey key) =>
                {
                    if (!m_Layouts.TryGetValue(
                            key,
                            out VkImageLayout knownLayout))
                    {
                        throw new InvalidOperationException(
                            $"{operation} requires the caller to " +
                            "record an explicit texture barrier for " +
                            "every attachment subresource before " +
                            "BeginRasterPass.");
                    }
                    if (knownLayout != requiredLayout)
                    {
                        throw new InvalidOperationException(
                            $"{operation} requires layout " +
                            $"{requiredLayout}, but this command " +
                            "buffer records {knownLayout} for an " +
                            "attachment subresource.");
                    }
                });
        }

        internal void SetLayout(
            VkImage image,
            in RHITextureSubresourceRange range,
            VkImageLayout layout)
        {
            VisitRange(
                image,
                in range,
                (in VulkanImageSubresourceKey key) =>
                {
                    bool hadPreviousValue =
                        m_Layouts.TryGetValue(
                            key,
                            out VkImageLayout previousValue);
                    m_Mutations.Add(
                        new VulkanImageLayoutOverlayMutation(
                            in key,
                            hadPreviousValue,
                            previousValue));
                    m_Layouts[key] = layout;
                });
        }

        private delegate void SubresourceVisitor(
            in VulkanImageSubresourceKey key);

        private static void VisitRange(
            VkImage image,
            in RHITextureSubresourceRange range,
            SubresourceVisitor visitor)
        {
            ValidateRange(in range);
            VkImageAspectFlags aspects =
                range.AspectMask.ToVkImageAspectFlags();
            VisitAspect(
                image,
                aspects,
                VkImageAspectFlags.Color,
                in range,
                visitor);
            VisitAspect(
                image,
                aspects,
                VkImageAspectFlags.Depth,
                in range,
                visitor);
            VisitAspect(
                image,
                aspects,
                VkImageAspectFlags.Stencil,
                in range,
                visitor);
        }

        private static void VisitAspect(
            VkImage image,
            VkImageAspectFlags selectedAspects,
            VkImageAspectFlags aspect,
            in RHITextureSubresourceRange range,
            SubresourceVisitor visitor)
        {
            if ((selectedAspects & aspect) == 0)
            {
                return;
            }
            uint mipEnd = checked(
                range.BaseMipLevel + range.MipLevelCount);
            uint layerEnd = checked(
                range.BaseArrayLayer + range.ArrayLayerCount);
            for (uint mip = range.BaseMipLevel;
                 mip < mipEnd;
                 ++mip)
            {
                for (uint layer = range.BaseArrayLayer;
                     layer < layerEnd;
                     ++layer)
                {
                    VulkanImageSubresourceKey key =
                        new(image, aspect, mip, layer);
                    visitor(in key);
                }
            }
        }

        private static void ValidateRange(
            in RHITextureSubresourceRange range)
        {
            if (range.AspectMask == ERHITextureAspectMask.None)
            {
                throw new ArgumentException(
                    "A texture subresource range must select at " +
                    "least one aspect.");
            }
            if (range.MipLevelCount == 0)
            {
                throw new ArgumentException(
                    "A texture subresource range must select at " +
                    "least one mip level.");
            }
            if (range.ArrayLayerCount == 0)
            {
                throw new ArgumentException(
                    "A texture subresource range must select at " +
                    "least one array layer.");
            }
        }
    }
}
