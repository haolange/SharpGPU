using Evergine.Bindings.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Infinity.Graphics
{
    internal unsafe class VulkanInstance : RHIInstance
    {
        public override int DeviceCount => 1;
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;

        private VkInstance m_VkInstance;

        string[] validationLayers = new[] { "VK_LAYER_KHRONOS_validation" };

        string[] m_Extensions = new[]
        {
            "VK_KHR_surface",
            "VK_KHR_win32_surface",
            "VK_EXT_debug_utils",
        };

        public VulkanInstance(in RHIInstanceDescriptor descriptor)
        {
            CreateVulkanInstance(descriptor);
        }

        private bool CheckValidationLayerSupport()
        {
            uint layerCount;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, null));
            VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, availableLayers));

            for (int i = 0; i < layerCount; i++)
            {
                Debug.WriteLine($"ValidationLayer: {VulkanUtility.GetString(availableLayers[i].layerName)} version: {availableLayers[i].specVersion} description: {VulkanUtility.GetString(availableLayers[i].description)}");
            }

            for (int i = 0; i < validationLayers.Length; i++)
            {
                bool layerFound = false;
                string validationLayer = validationLayers[i];
                for (int j = 0; j < layerCount; j++)
                {
                    if (validationLayer.Equals(VulkanUtility.GetString(availableLayers[j].layerName)))
                    {
                        layerFound = true;
                        break;
                    }
                }

                if (!layerFound)
                {
                    return false;
                }
            }

            return true;
        }

        private void CreateVulkanInstance(in RHIInstanceDescriptor descriptor)
        {
            VkApplicationInfo appInfo = new VkApplicationInfo()
            {
                sType = VkStructureType.VK_STRUCTURE_TYPE_APPLICATION_INFO,
                pApplicationName = "Hello Triangle".ToPointer(),
                applicationVersion = VulkanUtility.Version(1, 0, 0),
                pEngineName = "No Engine".ToPointer(),
                engineVersion = VulkanUtility.Version(1, 0, 0),
                apiVersion = VulkanUtility.Version(1, 3, 0),
            };

            VkInstanceCreateInfo createInfo = default;
            createInfo.sType = VkStructureType.VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
            createInfo.pApplicationInfo = &appInfo;

            // Extensions
            uint extensionCount;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceExtensionProperties(null, &extensionCount, null));
            VkExtensionProperties* extensions = stackalloc VkExtensionProperties[(int)extensionCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceExtensionProperties(null, &extensionCount, extensions));

            for (int i = 0; i < extensionCount; i++)
            {
                Console.WriteLine($"Extension: {VulkanUtility.GetString(extensions[i].extensionName)} version: {extensions[i].specVersion}");
            }

            IntPtr* extensionsToBytesArray = stackalloc IntPtr[m_Extensions.Length];
            for (int i = 0; i < m_Extensions.Length; i++)
            {
                extensionsToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_Extensions[i]);
            }
            createInfo.enabledExtensionCount = (uint)m_Extensions.Length;
            createInfo.ppEnabledExtensionNames = (byte**)extensionsToBytesArray;

            // Validation layers
#if DEBUG
            if (this.CheckValidationLayerSupport())
            {
                IntPtr* layersToBytesArray = stackalloc IntPtr[validationLayers.Length];
                for (int i = 0; i < validationLayers.Length; i++)
                {
                    layersToBytesArray[i] = Marshal.StringToHGlobalAnsi(validationLayers[i]);
                }

                createInfo.enabledLayerCount = (uint)validationLayers.Length;
                createInfo.ppEnabledLayerNames = (byte**)layersToBytesArray;
            }
            else
            {
                createInfo.enabledLayerCount = 0;
                createInfo.pNext = null;
            }
#else
            createInfo.enabledLayerCount = 0;
            createInfo.pNext = null;
#endif

            fixed (VkInstance* instancePtr = &m_VkInstance)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateInstance(&createInfo, null, instancePtr));
            }
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
