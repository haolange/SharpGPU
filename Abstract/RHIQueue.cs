using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHISubmitDescriptor
    {
        public RHISemaphore[] WaitSemaphores;
        public RHISemaphore[] SignalSemaphores;
        public RHICommandBuffer[] CommandBuffers;
    }

    public abstract class RHIQueue : Disposal
    {
        public EQueueType Type
        {
            get
            {
                return m_Type;
            }
        }
        public abstract ulong Frequency
        {
            get;
        }

        protected EQueueType m_Type;
        public abstract RHICommandBuffer CreateCommandBuffer();
        public abstract void Submit(RHICommandBuffer cmdBuffer, RHIFence signalFence, RHISemaphore waitSemaphore, RHISemaphore signalSemaphore);
        public abstract void Submit(in RHISubmitDescriptor submitDescriptor, RHIFence signalFence);
    }
}
