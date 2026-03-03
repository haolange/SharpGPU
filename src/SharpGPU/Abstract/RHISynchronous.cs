using Infinity.Core;

namespace Infinity.Graphics
{
    public enum EFenceStatus : byte
    {
        Success = 0,
        NotReady = 1,
        Undefined
    };

    public abstract class RHIFence : Disposal
    {
        public abstract EFenceStatus Status
        {
            get;
        }

        public abstract void Reset();
        public abstract void Wait();
    }

    public abstract class RHISemaphore : Disposal
    {

    }
}
