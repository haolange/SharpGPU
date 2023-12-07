using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal unsafe class MtlInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.Metal;

        private List<MtlDevice> m_Devices;

        public MtlInstance(in RHIInstanceDescriptor descriptor)
        {
            int deviceCount = 0;
            IntPtr devicePtr = IntPtr.Zero;

            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                MTLDevice defaultDevice = MTLDevice.CreateSystemDefaultDevice();
                devicePtr = defaultDevice.NativePtr;
                deviceCount = 1;
            }
            else
            {
                NSArray devices = MTLDevice.CopyAllDevices();
                devicePtr = devices.NativePtr;
                deviceCount = (int)devices.Count;
            }

            m_Devices = new List<MtlDevice>(deviceCount);
            for (int i = 0; i < deviceCount; ++i)
            {
                m_Devices.Add(new MtlDevice(this, IntPtr.Add(devicePtr, i)));
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
