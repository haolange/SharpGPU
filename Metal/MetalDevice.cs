using System;
using SharpMetal.Metal;
using System.Diagnostics;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class MetalDevice : RHIDevice
    {
        public MTLDevice NativeDevice
        {
            get
            {
                return m_NativeDevice;
            }
        }
        public MetalInstance MtlInstance
        {
            get
            {
                return m_MtlInstance;
            }
        }

        private MTLDevice m_NativeDevice;
        private MetalInstance m_MtlInstance;

        public MetalDevice(MetalInstance instance, in IntPtr devicePtr)
        {
            m_MtlInstance = instance;
            CreateDevice(devicePtr);
        }

        public override RHIFence CreateFence()
        {
            throw new NotImplementedException();
        }

        public override RHISemaphore CreateSemaphore()
        {
            throw new NotImplementedException();
        }

        public override RHIQuery CreateQuery(in RHIQueryDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIHeap CreateHeap(in RHIHeapDescription descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIBuffer CreateBuffer(in RHIBufferDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHITexture CreateTexture(in RHITextureDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHISampler CreateSampler(in RHISamplerDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIStorageQueue CreateStorageQueue()
        {
            throw new NotImplementedException();
        }

        public override RHICommandQueue CreateCommandQueue(in ERHIPipelineType pipeline)
        {
            throw new NotImplementedException();
        }

        public override RHITopLevelAccelStruct CreateAccelerationStructure(in RHITopLevelAccelStructDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIBottomLevelAccelStruct CreateAccelerationStructure(in RHIBottomLevelAccelStructDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIFunction CreateFunction(in RHIFunctionDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHISwapChain CreateSwapChain(in RHISwapChainDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIBindTableLayout CreateBindTableLayout(in RHIBindTableLayoutDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIBindTable CreateBindTable(in RHIBindTableDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIPipelineLayout CreatePipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIComputePipelineState CreateComputePipelineState(in RHIComputePipelineStateDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIRaytracingPipelineState CreateRaytracingPipelineState(in RHIRaytracingPipelineStateDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIRasterPipelineState CreateRasterPipelineState(in RHIRasterPipelineStateDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        private void CreateDevice(in IntPtr devicePtr)
        {
#if DEBUG
            Debug.Assert(devicePtr.ToPointer() != null, "System device is null");
#endif
            m_NativeDevice = new MTLDevice(devicePtr);
        }

        protected override void Release()
        {
            ObjectiveCRuntime.Release(m_NativeDevice);
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
