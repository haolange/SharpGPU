using System;
using Infinity.Core;

namespace Infinity.Graphics
{
    public struct RHIInstanceDescriptor
    {
        public ERHIBackend Backend;
        public bool EnableDebugLayer;
        public bool EnableValidatior;
    }

    public abstract class RHIInstance : Disposal
    {
        public abstract int DeviceCount
        {
            get;
        }
        public abstract ERHIBackend RHIType
        {
            get;
        }

        public abstract RHIDevice GetDevice(in int index);

        public static ERHIBackend GetBackendByPlatform(in bool bForceVulkan)
        {
            ERHIBackend rhiType = bForceVulkan ? ERHIBackend.Vulkan : ERHIBackend.DirectX12;

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            {
                rhiType = bForceVulkan ? ERHIBackend.Vulkan : ERHIBackend.Metal;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                rhiType = ERHIBackend.Vulkan;
            }

            return rhiType;
        }

        public static RHIInstance? Create(in RHIInstanceDescriptor descriptor)
        {
            switch (descriptor.Backend)
            {
                case ERHIBackend.Metal:
                    return new MtlInstance(descriptor);

                case ERHIBackend.Vulkan:
                    return new VkInstance(descriptor);

                case ERHIBackend.DirectX12:
                    return new Dx12Instance(descriptor);

                default:
                    return null;
            }
        }
    }
}
