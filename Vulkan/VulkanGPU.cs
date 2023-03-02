using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infinity.Graphics
{
    internal class VkGPU : RHIGPU
    {
        public VkGPU()
        {

        }

        public override RHIGpuProperty GetProperty()
        {
            throw new NotImplementedException();
        }

        public override RHIDevice CreateDevice(in RHIDeviceDescriptor descriptor)
        {
            throw new NotImplementedException();
        }

        protected override void Release()
        {

        }
    }
}
