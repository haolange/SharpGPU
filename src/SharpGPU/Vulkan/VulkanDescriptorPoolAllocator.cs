using System;
using System.Collections.Generic;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal readonly struct VulkanDescriptorSetLease
    {
        public VkDescriptorPool Pool { get; }
        public VkDescriptorSet Set { get; }

        public VulkanDescriptorSetLease(
            in VkDescriptorPool pool,
            in VkDescriptorSet set)
        {
            Pool = pool;
            Set = set;
        }
    }

    internal sealed class VulkanDescriptorPoolPage
    {
        public VkDescriptorPool Pool { get; }
        public VulkanDescriptorPoolRequirements Requirements { get; }
        public int AllocatedSetCount { get; set; }

        public VulkanDescriptorPoolPage(
            in VkDescriptorPool pool,
            in VulkanDescriptorPoolRequirements requirements)
        {
            Pool = pool;
            Requirements = requirements;
        }
    }

    internal unsafe sealed class VulkanDescriptorPoolAllocator : IDisposable
    {
        private const int SetsPerPage = 32;

        private readonly object m_Gate = new object();
        private readonly VulkanDevice m_Device;
        private readonly List<VulkanDescriptorPoolPage> m_Pages =
            new List<VulkanDescriptorPoolPage>();
        private bool m_Disposed;

        public VulkanDescriptorPoolAllocator(VulkanDevice device)
        {
            m_Device = device;
        }

        internal int AllocatedSetCount
        {
            get
            {
                lock (m_Gate)
                {
                    int total = 0;
                    for (int pageIndex = 0;
                         pageIndex < m_Pages.Count;
                         ++pageIndex)
                    {
                        total = checked(
                            total + m_Pages[pageIndex].AllocatedSetCount);
                    }

                    return total;
                }
            }
        }

        public VulkanDescriptorSetLease Allocate(VulkanBindingTableLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);
            return Allocate(
                layout.Plan.PoolRequirements,
                layout.NativeDescriptorSetLayout);
        }

        internal VulkanDescriptorSetLease Allocate(
            in VulkanDescriptorPoolRequirements requirements,
            in VkDescriptorSetLayout nativeLayout)
        {
            if (nativeLayout.Handle == 0)
            {
                throw new ArgumentException(
                    "Vulkan descriptor allocation requires a live native layout.",
                    nameof(nativeLayout));
            }
            lock (m_Gate)
            {
                ThrowIfDisposed();
                for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
                {
                    VulkanDescriptorPoolPage page = m_Pages[pageIndex];
                    if (!page.Requirements.Equals(requirements)
                        || page.AllocatedSetCount >= SetsPerPage)
                    {
                        continue;
                    }

                    VkResult result = TryAllocate(
                        page.Pool,
                        nativeLayout,
                        out VkDescriptorSet set);
                    if (result == VkResult.Success)
                    {
                        page.AllocatedSetCount++;
                        return new VulkanDescriptorSetLease(page.Pool, set);
                    }
                    if (result != VkResult.ErrorOutOfPoolMemory
                        && result != VkResult.ErrorFragmentedPool)
                    {
                        VulkanUtility.CheckErrors(result);
                    }
                }

                VulkanDescriptorPoolPage newPage = CreatePage(requirements);
                try
                {
                    VkResult result = TryAllocate(
                        newPage.Pool,
                        nativeLayout,
                        out VkDescriptorSet set);
                    VulkanUtility.CheckErrors(result);
                    newPage.AllocatedSetCount = 1;
                    m_Pages.Add(newPage);
                    return new VulkanDescriptorSetLease(newPage.Pool, set);
                }
                catch
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        newPage.Pool,
                        null);
                    throw;
                }
            }
        }

        public void Free(in VulkanDescriptorSetLease lease)
        {
            if (lease.Pool.Handle == 0 || lease.Set.Handle == 0)
            {
                return;
            }

            lock (m_Gate)
            {
                ThrowIfDisposed();
                int pageIndex = FindPage(lease.Pool);
                VulkanDescriptorPoolPage page = m_Pages[pageIndex];
                VkDescriptorSet set = lease.Set;
                VulkanUtility.CheckErrors(
                    VulkanNative.vkFreeDescriptorSets(
                        m_Device.NativeDevice,
                        lease.Pool,
                        1,
                        &set));
                page.AllocatedSetCount--;
                if (page.AllocatedSetCount < 0)
                {
                    throw new InvalidOperationException(
                        "Vulkan descriptor-pool accounting underflowed.");
                }

                if (page.AllocatedSetCount == 0
                    && HasOtherEmptyPage(pageIndex, page.Requirements))
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        page.Pool,
                        null);
                    m_Pages.RemoveAt(pageIndex);
                }
            }
        }

        public void Dispose()
        {
            lock (m_Gate)
            {
                if (m_Disposed)
                {
                    return;
                }

                for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
                {
                    VulkanNative.vkDestroyDescriptorPool(
                        m_Device.NativeDevice,
                        m_Pages[pageIndex].Pool,
                        null);
                }
                m_Pages.Clear();
                m_Disposed = true;
            }
        }

        private VulkanDescriptorPoolPage CreatePage(
            in VulkanDescriptorPoolRequirements requirements)
        {
            VkDescriptorPoolSize[] sizes = BuildPoolSizes(requirements);
            VkDescriptorPool pool = default;
            fixed (VkDescriptorPoolSize* sizesPointer = sizes)
            {
                VkDescriptorPoolCreateInfo createInfo =
                    new VkDescriptorPoolCreateInfo
                    {
                        sType = VkStructureType.DescriptorPoolCreateInfo,
                        flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet,
                        maxSets = SetsPerPage,
                        poolSizeCount = checked((uint)sizes.Length),
                        pPoolSizes = sizes.Length == 0 ? null : sizesPointer,
                    };
                VulkanUtility.CheckErrors(
                    VulkanNative.vkCreateDescriptorPool(
                        m_Device.NativeDevice,
                        &createInfo,
                        null,
                        &pool));
            }

            return new VulkanDescriptorPoolPage(pool, requirements);
        }

        private static VkDescriptorPoolSize[] BuildPoolSizes(
            in VulkanDescriptorPoolRequirements requirements)
        {
            List<VkDescriptorPoolSize> sizes =
                new List<VkDescriptorPoolSize>(7);
            AddSize(sizes, VkDescriptorType.Sampler, requirements.Samplers);
            AddSize(
                sizes,
                VkDescriptorType.SampledImage,
                requirements.SampledImages);
            AddSize(
                sizes,
                VkDescriptorType.StorageImage,
                requirements.StorageImages);
            AddSize(
                sizes,
                VkDescriptorType.UniformBuffer,
                requirements.UniformBuffers);
            AddSize(
                sizes,
                VkDescriptorType.StorageBuffer,
                requirements.StorageBuffers);
            AddSize(
                sizes,
                VkDescriptorType.AccelerationStructureKHR,
                requirements.AccelerationStructures);
            AddSize(
                sizes,
                VkDescriptorType.InputAttachment,
                requirements.InputAttachments);
            return sizes.ToArray();
        }

        private static void AddSize(
            List<VkDescriptorPoolSize> sizes,
            in VkDescriptorType type,
            in uint descriptorsPerSet)
        {
            if (descriptorsPerSet == 0)
            {
                return;
            }

            sizes.Add(new VkDescriptorPoolSize
            {
                type = type,
                descriptorCount = checked(
                    descriptorsPerSet * (uint)SetsPerPage),
            });
        }

        private VkResult TryAllocate(
            in VkDescriptorPool pool,
            in VkDescriptorSetLayout layout,
            out VkDescriptorSet set)
        {
            VkDescriptorSetLayout nativeLayout = layout;
            VkDescriptorSet nativeSet = default;
            VkDescriptorSetAllocateInfo allocateInfo =
                new VkDescriptorSetAllocateInfo
                {
                    sType = VkStructureType.DescriptorSetAllocateInfo,
                    descriptorPool = pool,
                    descriptorSetCount = 1,
                    pSetLayouts = &nativeLayout,
                };
            VkResult result = VulkanNative.vkAllocateDescriptorSets(
                m_Device.NativeDevice,
                &allocateInfo,
                &nativeSet);
            set = nativeSet;
            return result;
        }

        private int FindPage(in VkDescriptorPool pool)
        {
            for (int pageIndex = 0; pageIndex < m_Pages.Count; ++pageIndex)
            {
                if (m_Pages[pageIndex].Pool.Handle == pool.Handle)
                {
                    return pageIndex;
                }
            }

            throw new InvalidOperationException(
                "Vulkan descriptor-set lease references an unknown pool page.");
        }

        private bool HasOtherEmptyPage(
            in int pageIndex,
            in VulkanDescriptorPoolRequirements requirements)
        {
            for (int index = 0; index < m_Pages.Count; ++index)
            {
                if (index != pageIndex
                    && m_Pages[index].AllocatedSetCount == 0
                    && m_Pages[index].Requirements.Equals(requirements))
                {
                    return true;
                }
            }
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (m_Disposed)
            {
                throw new ObjectDisposedException(
                    nameof(VulkanDescriptorPoolAllocator));
            }
        }
    }
}
