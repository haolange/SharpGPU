using System;
using Apple.Metal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal unsafe class MtlInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend RHIType => ERHIBackend.Metal;

        private List<MtlDevice> m_Devices;

        public MtlInstance(in RHIInstanceDescriptor descriptor)
        {
            int gpuCount = 0;
            IntPtr gpusPtr = IntPtr.Zero;

            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                MTLDevice defaultDevice = MTLDevice.MTLCreateSystemDefaultDevice();
                gpusPtr = defaultDevice.NativePtr;
                gpuCount = 1;
            }
            else
            {
                NSArray gpus = MTLDevice.MTLCopyAllDevices();
                gpusPtr = gpus.NativePtr;
                gpuCount = (int)gpus.count;
            }

            m_Devices = new List<MtlDevice>(gpuCount);
            for (int i = 0; i < gpuCount; ++i)
            {
                m_Devices.Add(new MtlDevice(this, IntPtr.Add(gpusPtr, i)));
            }
        }

        public override RHIDevice GetDevice(in int index)
        {
            return m_Devices[index];
        }

        protected override void Release()
        {
            for (int i = 0; i < m_Devices.Count; ++i)
            {
                m_Devices[i].Dispose();
            }
        }
    }
}
