using System;
using System.Collections.Generic;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal sealed class MetalInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.Metal;

        private readonly List<MetalDevice> m_Devices;

        public MetalInstance(in RHIInstanceDescriptor descriptor)
        {
            m_Devices = new List<MetalDevice>(2);
            NSArray allDevices = MTLDevice.CopyAllDevices();
            ulong count = allDevices.Count;
            if (count == 0)
            {
                MTLDevice defaultDevice = MTLDevice.CreateSystemDefaultDevice();
                if (defaultDevice.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unable to create default Metal device.");
                }

                m_Devices.Add(new MetalDevice(this, defaultDevice, descriptor.ComputeQueueRequestCount, descriptor.TransferQueueRequestCount, descriptor.GraphicsQueueRequestCount));
            }
            else
            {
                for (ulong i = 0; i < count; ++i)
                {
                    IntPtr ptr = allDevices.Object(i);
                    if (ptr == IntPtr.Zero)
                    {
                        continue;
                    }

                    m_Devices.Add(new MetalDevice(this, new MTLDevice(ptr), descriptor.ComputeQueueRequestCount, descriptor.TransferQueueRequestCount, descriptor.GraphicsQueueRequestCount));
                }
            }

            if (allDevices.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(allDevices);
            }

            if (m_Devices.Count == 0)
            {
                throw new InvalidOperationException("No usable Metal device found.");
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
                m_Devices[i]?.Dispose();
            }
        }
    }
}
