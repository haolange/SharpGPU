using Infinity.Core;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    public abstract class RHICommandPool : Disposal
    {
        public RHIQueue Queue
        {
            get
            {
                return m_Queue;
            }
        }

        protected RHIQueue m_Queue;

        public abstract void Reset();
        public abstract RHICommandBuffer CreateCommandBuffer();
    }
#pragma warning restore CS8618 
}
