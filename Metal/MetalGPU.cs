using System;

namespace Infinity.Graphics
{
#pragma warning disable CA1416
    internal class MtlGPU : RHIGPU
    {
        public IntPtr GpuPtr
        {
            get
            {
                return m_GpuPtr;
            }
        }
        public MtlInstance MTLInstance
        {
            get
            {
                return m_MTLInstance;
            }
        }

        private IntPtr m_GpuPtr;
        private MtlInstance m_MTLInstance;

        public MtlGPU(MtlInstance instance, IntPtr gpu)
        {
            m_GpuPtr = gpu;
            m_MTLInstance = instance;
        }

        public override RHIGpuProperty GetProperty()
        {
            RHIGpuProperty gpuProperty;
            gpuProperty.DeviceId = 0;
            gpuProperty.VendorId = 0;
            gpuProperty.Type = EGpuType.Hardware;
            return gpuProperty;
        }

        public override RHIDevice CreateDevice(in RHIDeviceDescriptor descriptor)
        {
            return new MtlDevice(this, descriptor);
        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CA1416
}
