using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIGpuProperty
    {
        public EGpuType Type;
        public uint VendorId;
        public uint DeviceId;
    }

    public abstract class RHIGPU : Disposal
    {
        public abstract RHIGpuProperty GetProperty();
        public abstract RHIDevice CreateDevice(in RHIDeviceDescriptor descriptor);
    }
}
