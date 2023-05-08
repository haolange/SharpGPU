using System;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Infinity.Graphics
{
    internal class VkDevice : RHIDevice
    {
        public override bool IsMRTSupported => true;
        public override bool IsShadowMapSupported => true;
        public override bool IsRaytracingSupported => true;
        public override bool IsComputeShaderSupported => true;
        public override bool IsFlipProjectionRequired => false;
        public override EClipDepth ClipDepth => EClipDepth.ZeroToOne;
        public override EMatrixMajorness MatrixMajorness => EMatrixMajorness.RowMajor;
        public override EMultiviewStrategy MultiviewStrategy => EMultiviewStrategy.Unsupported;

        public VkInstance VkInstance
        {
            get
            {
                return m_VkInstance;
            }
        }

        private VkInstance m_VkInstance;

        public VkDevice(VkInstance instance, in IntPtr adapterPtr)
        {
            m_VkInstance = instance;
            CreateDevice(adapterPtr);
        }

        public override RHIDeviceProperty GetDeviceProperty()
        {
            throw new NotImplementedException();
        }

        public override RHIQueue CreateQueue(in EQueueType type)
        {
            throw new NotImplementedException();
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

        public override RHIComputePipeline CreateComputePipeline(in RHIComputePipelineDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIRaytracingPipeline CreateRaytracingPipeline(in RHIRaytracingPipelineDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIMeshletPipeline CreateMeshletPipeline(in RHIMeshletPipelineDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIGraphicsPipeline CreateGraphicsPipeline(in RHIGraphicsPipelineDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        private void CreateDevice(in IntPtr devicePtr)
        {
            throw new NotImplementedException();
        }

        protected override void Release()
        {
            
        }
    }
}
