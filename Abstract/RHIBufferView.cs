using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBufferViewDescriptor
    {
        public int Count;
        public int Offset;
        public int Stride;
        public ERHIBufferViewType ViewType;
    }

    public abstract class RHIBufferView : Disposal
    {

    }
}
