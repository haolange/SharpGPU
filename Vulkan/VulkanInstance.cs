using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infinity.Graphics
{
    internal class VkInstance : RHIInstance
    {
        public override int GpuCount => 1;
        public override ERHIBackend RHIType => ERHIBackend.Vulkan;

        public VkInstance(in RHIInstanceDescriptor descriptor)
        {

        }

        public override RHIGPU GetGpu(in int index)
        {
            throw new NotImplementedException();
        }

        protected override void Release()
        {

        }
    }
}
