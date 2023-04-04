using System;
using Apple.Metal;
using System.Diagnostics;
using System.Collections.Generic;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CS8604, CS8618, CA1416
    internal unsafe class MtlDevice : RHIDevice
    {
        public override bool IsMRTSupported => true;
        public override bool IsShadowMapSupported => true;
        public override bool IsRaytracingSupported => true;
        public override bool IsComputeShaderSupported => true;
        public override bool IsFlipProjectionRequired => false;
        public override EClipDepth ClipDepth => EClipDepth.ZeroToOne;
        public override EMatrixMajorness MatrixMajorness => EMatrixMajorness.RowMajor;
        public override EMultiviewStrategy MultiviewStrategy => EMultiviewStrategy.Unsupported;

        public MtlGPU MtlGpu
        {
            get
            {
                return m_MtlGpu;
            }
        }
        public MTLDevice NativeDevice
        {
            get
            {
                return m_NativeDevice;
            }
        }

        private MtlGPU m_MtlGpu;
        private MTLDevice m_NativeDevice;
        private Dictionary<EQueueType, List<MtlQueue>> m_Queues;

        public MtlDevice(MtlGPU gpu, in RHIDeviceDescriptor descriptor)
        {
            m_MtlGpu = gpu;
            CreateDevice();
            CreateQueues(descriptor);
        }

        public override int GetQueueCount(in EQueueType type)
        {
            bool hashValue = m_Queues.TryGetValue(type, out List<MtlQueue> queueArray);
            Debug.Assert(hashValue);
            return queueArray.Count;
        }

        public override RHIQueue GetQueue(in EQueueType type, in int index)
        {
            bool hashValue = m_Queues.TryGetValue(type, out List<MtlQueue> queueArray);
            Debug.Assert(hashValue);
            Debug.Assert(index >= 0 && index < queueArray?.Count);
            return queueArray[index];
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

        public override RHIBindGroupLayout CreateBindGroupLayout(in RHIBindGroupLayoutDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        public override RHIBindGroup CreateBindGroup(in RHIBindGroupDescriptor descriptor)
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

        private void CreateDevice()
        {
            Debug.Assert(m_MtlGpu.GpuPtr.ToPointer() != null);
            m_NativeDevice = new MTLDevice(m_MtlGpu.GpuPtr);
        }

        private void CreateQueues(in RHIDeviceDescriptor descriptor)
        {
            Dictionary<EQueueType, int> queueCountMap = new Dictionary<EQueueType, int>(3);
            for (int i = 0; i < descriptor.QueueInfoCount; ++i)
            {
                RHIQueueDescriptor queueInfo = descriptor.QueueInfos.Span[i];
                if (queueCountMap.TryGetValue(queueInfo.Type, out int value))
                {
                    queueCountMap[queueInfo.Type] = 0;
                }

                queueCountMap.TryAdd(queueInfo.Type, (int)queueInfo.Count);
            }

            m_Queues = new Dictionary<EQueueType, List<MtlQueue>>(3);
            foreach (KeyValuePair<EQueueType, int> iter in queueCountMap)
            {
                List<MtlQueue> tempQueues = new List<MtlQueue>(iter.Value);

                for (int i = 0; i < iter.Value; ++i)
                {
                    MTLCommandQueue commandQueue = m_NativeDevice.newCommandQueue();
                    Debug.Assert(commandQueue.NativePtr.ToPointer() != null);

                    MtlCommandQueueDescriptor queueDescriptor;
                    queueDescriptor.cmdQueue = commandQueue;
                    queueDescriptor.queueType = iter.Key;
                    tempQueues.Add(new MtlQueue(this, queueDescriptor));
                }

                m_Queues.TryAdd(iter.Key, tempQueues);
            }
        }

        protected override void Release()
        {
            ObjectiveCRuntime.release(m_NativeDevice.NativePtr);
        }
    }
#pragma warning restore CS8600, CS8602, CS8604, CS8618, CA1416
}
