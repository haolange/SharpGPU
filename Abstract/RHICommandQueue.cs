using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public abstract class RHICommandQueue : Disposal
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
        public abstract void Submits(RHICommandBuffer[] cmdBuffers, RHIFence signalFence, RHISemaphore[] waitSemaphores, RHISemaphore[] signalSemaphores);
    }
}
