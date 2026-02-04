using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal unsafe class MetalInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.Metal;

        private List<MetalDevice> m_Devices;

        public MetalInstance(in RHIInstanceDescriptor descriptor)
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

            m_Devices = new List<MetalDevice>(deviceCount);
            for (int i = 0; i < deviceCount; ++i)
            {
                m_Devices.Add(new MetalDevice(this, IntPtr.Add(devicePtr, i)));
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
