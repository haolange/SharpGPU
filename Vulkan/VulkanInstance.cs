using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infinity.Graphics
{
    internal class VkInstance : RHIInstance
    {
        public override int DeviceCount => 1;
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;

        public VkInstance(in RHIInstanceDescriptor descriptor)
        {

        }

        public override RHIDevice GetDevice(in int index)
        {
            throw new NotImplementedException();
        }

        protected override void Release()
        {

        }
    }
}
