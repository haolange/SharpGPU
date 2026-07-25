using System;
using Vortice.Vulkan;

namespace SharpGPU
{
    internal interface IVulkanRasterNativePipeline
    {
        VkPipeline NativePipeline { get; }
        VkPipelineLayout EffectiveNativePipelineLayout { get; }
        VulkanPrivateRasterBindingPlan PrivateBindingPlan { get; }
        VulkanPrivateRasterDescriptorLayout? PrivateDescriptorLayout
        {
            get;
        }
        bool HasNativePipeline { get; }
    }

    internal unsafe sealed class VulkanRasterNativeVariant :
        IVulkanRasterNativePipeline,
        IDisposable
    {
        public VkPipeline NativePipeline => m_NativePipeline;
        public VkPipelineLayout EffectiveNativePipelineLayout =>
            m_EffectiveNativePipelineLayout;
        public VulkanPrivateRasterBindingPlan PrivateBindingPlan =>
            m_PrivateBindingPlan;
        public VulkanPrivateRasterDescriptorLayout?
            PrivateDescriptorLayout => m_PrivateDescriptorLayout;
        public bool HasNativePipeline =>
            m_NativePipeline.Handle != 0;

        private readonly VkDevice m_NativeDevice;
        private VkPipeline m_NativePipeline;
        private VkPipelineLayout m_EffectiveNativePipelineLayout;
        private readonly bool m_OwnsEffectiveNativePipelineLayout;
        private readonly VulkanPrivateRasterBindingPlan
            m_PrivateBindingPlan;
        private VulkanPrivateRasterDescriptorLayout?
            m_PrivateDescriptorLayout;
        private bool m_Disposed;

        internal VulkanRasterNativeVariant(
            VkDevice nativeDevice,
            VkPipeline nativePipeline,
            VkPipelineLayout effectiveNativePipelineLayout,
            bool ownsEffectiveNativePipelineLayout,
            in VulkanPrivateRasterBindingPlan privateBindingPlan,
            VulkanPrivateRasterDescriptorLayout?
                privateDescriptorLayout)
        {
            if (nativeDevice.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan device is required.",
                    nameof(nativeDevice));
            }
            if (nativePipeline.Handle == 0)
            {
                throw new ArgumentException(
                    "A native Vulkan raster pipeline is required.",
                    nameof(nativePipeline));
            }

            m_NativeDevice = nativeDevice;
            m_NativePipeline = nativePipeline;
            m_EffectiveNativePipelineLayout =
                effectiveNativePipelineLayout;
            m_OwnsEffectiveNativePipelineLayout =
                ownsEffectiveNativePipelineLayout;
            m_PrivateBindingPlan = privateBindingPlan;
            m_PrivateDescriptorLayout = privateDescriptorLayout;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }
            if (m_NativePipeline.Handle != 0)
            {
                VulkanNative.vkDestroyPipeline(
                    m_NativeDevice,
                    m_NativePipeline,
                    null);
                m_NativePipeline = default;
            }
            if (m_OwnsEffectiveNativePipelineLayout &&
                m_EffectiveNativePipelineLayout.Handle != 0)
            {
                VulkanNative.vkDestroyPipelineLayout(
                    m_NativeDevice,
                    m_EffectiveNativePipelineLayout,
                    null);
                m_EffectiveNativePipelineLayout = default;
            }
            m_PrivateDescriptorLayout?.Dispose();
            m_PrivateDescriptorLayout = null;
            m_Disposed = true;
        }
    }
}
