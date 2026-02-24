using System;
using System.IO;
using System.Runtime.InteropServices;
using Evergine.Bindings.Vulkan;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8618
    internal unsafe class VulkanStorageQueue : RHIStorageQueue
    {
        private VulkanDevice m_VulkanDevice;
        private VkCommandPool m_CommandPool;
        private VkCommandBuffer m_CommandBuffer;
        private VkQueue m_TransferQueue;

        public VulkanStorageQueue(VulkanDevice device)
        {
            m_VulkanDevice = device;

            uint queueFamilyIndex = (uint)device.TransferQueueFamilyIndex;
            if (queueFamilyIndex < 0) queueFamilyIndex = (uint)device.GraphicsQueueFamilyIndex;

            fixed (VkQueue* queuePtr = &m_TransferQueue)
            {
                VulkanNative.vkGetDeviceQueue(device.NativeDevice, queueFamilyIndex, 0, queuePtr);
            }

            VkCommandPoolCreateInfo poolInfo = new VkCommandPoolCreateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO,
                flags = VkCommandPoolCreateFlags.VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = queueFamilyIndex,
            };

            fixed (VkCommandPool* poolPtr = &m_CommandPool)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateCommandPool(device.NativeDevice, &poolInfo, null, poolPtr));
            }

            VkCommandBufferAllocateInfo allocInfo = new VkCommandBufferAllocateInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO,
                commandPool = m_CommandPool,
                level = VkCommandBufferLevel.VK_COMMAND_BUFFER_LEVEL_PRIMARY,
                commandBufferCount = 1,
            };

            fixed (VkCommandBuffer* cmdBufPtr = &m_CommandBuffer)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkAllocateCommandBuffers(device.NativeDevice, &allocInfo, cmdBufPtr));
            }
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            IntPtr handle = IntPtr.Zero;

            if (File.Exists(absPath))
            {
                FileStream fs = File.OpenRead(absPath);
                handle = GCHandle.ToIntPtr(GCHandle.Alloc(fs));
            }

            return new RHIStorageFileHandle { NativeHandle = handle };
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            if (fileHandle.NativeHandle != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(fileHandle.NativeHandle);
                if (gcHandle.Target is FileStream fs)
                {
                    fs.Close();
                    fs.Dispose();
                }
                gcHandle.Free();
            }
        }

        public override ulong QueryFileSize(in RHIStorageFileHandle fileHandle)
        {
            if (fileHandle.NativeHandle != IntPtr.Zero)
            {
                GCHandle gcHandle = GCHandle.FromIntPtr(fileHandle.NativeHandle);
                if (gcHandle.Target is FileStream fs)
                {
                    return (ulong)fs.Length;
                }
            }
            return 0;
        }

        public override void RequestBuffer(in RHIStorageBufferRequest request)
        {
            if (request.FileHandle.NativeHandle == IntPtr.Zero) return;

            GCHandle gcHandle = GCHandle.FromIntPtr(request.FileHandle.NativeHandle);
            if (gcHandle.Target is FileStream fs)
            {
                VulkanBuffer vkDstBuffer = request.DestinationBuffer as VulkanBuffer;

                // Create staging buffer
                RHIBufferDescriptor stagingDesc = new RHIBufferDescriptor()
                {
                    ByteSize = (int)request.FileSize,
                    UsageFlag = ERHIBufferUsage.CopySrc,
                    StorageMode = ERHIStorageMode.HostUpload,
                };

                VulkanBuffer stagingBuffer = new VulkanBuffer(m_VulkanDevice, stagingDesc);

                // Map and copy data
                void* data;
                VulkanNative.vkMapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory, 0, (ulong)request.FileSize, 0, &data);
                byte[] fileData = new byte[request.FileSize];
                fs.Seek((long)request.FileOffset, SeekOrigin.Begin);
                fs.Read(fileData, 0, (int)request.FileSize);
                Marshal.Copy(fileData, 0, new IntPtr(data), (int)request.FileSize);
                VulkanNative.vkUnmapMemory(m_VulkanDevice.NativeDevice, stagingBuffer.NativeMemory);

                // Record copy command
                VkCommandBufferBeginInfo beginInfo = new VkCommandBufferBeginInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO,
                    flags = VkCommandBufferUsageFlags.VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT,
                };
                VulkanNative.vkBeginCommandBuffer(m_CommandBuffer, &beginInfo);

                VkBufferCopy copyRegion = new VkBufferCopy()
                {
                    srcOffset = 0,
                    dstOffset = request.DestinationOffset,
                    size = request.FileSize,
                };
                VulkanNative.vkCmdCopyBuffer(m_CommandBuffer, stagingBuffer.NativeBuffer, vkDstBuffer.NativeBuffer, 1, &copyRegion);
                VulkanNative.vkEndCommandBuffer(m_CommandBuffer);

                // Submit
                VkCommandBuffer cmdBuf = m_CommandBuffer;
                VkSubmitInfo submitInfo = new VkSubmitInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO,
                    commandBufferCount = 1,
                    pCommandBuffers = &cmdBuf,
                };
                VulkanNative.vkQueueSubmit(m_TransferQueue, 1, &submitInfo, default);
                VulkanNative.vkQueueWaitIdle(m_TransferQueue);

                stagingBuffer.Dispose();
            }
        }

        public override void RequestTexture(in RHIStorageTextureRequest request)
        {
            // Texture loading from file - similar pattern to buffer but with image copy
        }

        public override void Submit(RHIFence signalFence)
        {
            if (signalFence != null)
            {
                VulkanFence vkFence = signalFence as VulkanFence;
                vkFence.Reset();

                VkSubmitInfo submitInfo = new VkSubmitInfo()
                {
                    sType = VkStructureType.VK_STRUCTURE_TYPE_SUBMIT_INFO,
                    commandBufferCount = 0,
                };
                VulkanNative.vkQueueSubmit(m_TransferQueue, 1, &submitInfo, vkFence.NativeFence);
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyCommandPool(m_VulkanDevice.NativeDevice, m_CommandPool, null);
        }
    }
#pragma warning restore CS8600, CS8602, CS8618
}
