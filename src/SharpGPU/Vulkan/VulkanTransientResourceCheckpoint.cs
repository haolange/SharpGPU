namespace SharpGPU
{
    internal readonly struct VulkanTransientResourceCheckpoint
    {
        internal static VulkanTransientResourceCheckpoint Empty =>
            default;

        internal int AllocationCount { get; }
        internal int ImageViewCount { get; }
        internal int FramebufferCount { get; }
        internal int RenderPassCount { get; }
        internal int DescriptorSetLeaseCount { get; }
        internal int RasterPipelineCount { get; }

        internal VulkanTransientResourceCheckpoint(
            int allocationCount,
            int imageViewCount,
            int framebufferCount,
            int renderPassCount,
            int descriptorSetLeaseCount,
            int rasterPipelineCount)
        {
            AllocationCount = allocationCount;
            ImageViewCount = imageViewCount;
            FramebufferCount = framebufferCount;
            RenderPassCount = renderPassCount;
            DescriptorSetLeaseCount = descriptorSetLeaseCount;
            RasterPipelineCount = rasterPipelineCount;
        }
    }
}
