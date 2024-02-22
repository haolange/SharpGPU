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
        public abstract ERHIBackend BackendType
        {
            get;
        }

        public abstract RHIDevice GetDevice(in int index);

        public static ERHIBackend GetBackendByPlatform(in bool bForceVulkan)
        {
            ERHIBackend backendType = bForceVulkan ? ERHIBackend.Vulkan : ERHIBackend.DirectX12;

            if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
            {
                backendType = bForceVulkan ? ERHIBackend.Vulkan : ERHIBackend.Metal;
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
            {
                backendType = ERHIBackend.Vulkan;
            }

            return backendType;
        }

        public static RHIInstance? Create(in RHIInstanceDescriptor descriptor)
        {
            switch (descriptor.Backend)
            {
                case ERHIBackend.Metal:
                    return new MtlInstance(descriptor);

                case ERHIBackend.Vulkan:
                    return new VulkanInstance(descriptor);

                case ERHIBackend.DirectX12:
                    return new Dx12Instance(descriptor);

                default:
                    return null;
            }
        }
    }
}
