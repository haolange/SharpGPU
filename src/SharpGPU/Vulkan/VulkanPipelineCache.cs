using System;
using Vortice.Vulkan;

namespace SharpGPU
{
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
}
