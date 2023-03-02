using System;
using Apple.Metal;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
    internal unsafe class MtlInstance : RHIInstance
    {
        public override int GpuCount => m_GPUs.Count;
        public override ERHIBackend RHIType => ERHIBackend.Metal;

        private List<MtlGPU> m_GPUs;

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
                gpuCount = (int)gpus.count.ToUInt32();
            }

            m_GPUs = new List<MtlGPU>(gpuCount);
            for (int i = 0; i < gpuCount; ++i)
            {
                m_GPUs.Add(new MtlGPU(this, IntPtr.Add(gpusPtr, i)));
            }
        }

        public override RHIGPU GetGpu(in int index)
        {
            return m_GPUs[index];
        }

        protected override void Release()
        {
            for (int i = 0; i < m_GPUs.Count; ++i)
            {
                m_GPUs[i].Dispose();
            }
        }
    }
}
