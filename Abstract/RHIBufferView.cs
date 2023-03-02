using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIBufferViewDescriptor
    {
        public int Count;
        public int Offset;
        public int Stride;
        public EBufferViewType ViewType;
    }

    public abstract class RHIBufferView : Disposal
    {

    }
}
