using System;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    internal sealed class MetalComputeIndirectCommandBuffer : RHIComputeIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalComputeIndirectCommandBuffer(MetalDevice device, in RHIComputeIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.ConcurrentDispatch;
            icbDesc.MaxKernelBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }

    internal sealed class MetalRayTracingIndirectCommandBuffer : RHIRayTracingIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalRayTracingIndirectCommandBuffer(MetalDevice device, in RHIRayTracingIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.ConcurrentDispatch;
            icbDesc.MaxKernelBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }

    internal sealed class MetalRasterIndirectCommandBuffer : RHIRasterIndirectCommandBuffer
    {
        internal MTLIndirectCommandBuffer NativeIndirectCommandBuffer => m_NativeICB;
        internal uint MaxCommandCount => m_MaxCommandCount;

        private MTLIndirectCommandBuffer m_NativeICB;
        private readonly uint m_MaxCommandCount;

        internal MetalRasterIndirectCommandBuffer(MetalDevice device, in RHIRasterIndirectCommandBufferDescription descriptor)
        {
            m_MaxCommandCount = descriptor.MaxCommandCount;

            MTLIndirectCommandBufferDescriptor icbDesc = MTLIndirectCommandBufferDescriptor.New();
            icbDesc.CommandTypes = MTLIndirectCommandType.Draw | MTLIndirectCommandType.DrawIndexed;
            icbDesc.MaxVertexBufferBindCount = 8;
            icbDesc.MaxFragmentBufferBindCount = 8;
            icbDesc.InheritBuffers = false;
            icbDesc.InheritPipelineState = false;

            m_NativeICB = device.NativeDevice.NewIndirectCommandBuffer(icbDesc, m_MaxCommandCount, MTLResourceOptions.ResourceStorageModeShared);
            ObjectiveCRuntime.Release(icbDesc);
        }

        protected override void Release()
        {
            if (m_NativeICB.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeICB);
                m_NativeICB = default;
            }
        }
    }
}
