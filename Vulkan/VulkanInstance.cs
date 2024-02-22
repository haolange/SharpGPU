using Evergine.Bindings.Vulkan;
using Silk.NET.Vulkan;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TerraFX.Interop.Windows;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Infinity.Graphics
{
    internal unsafe class VulkanInstance : RHIInstance
    {
        public override int DeviceCount => 1;
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;

        private VkInstance m_VkInstance;

        List<string> m_ValidationLayers;

        List<string> m_RequiredExtensions;

        public VulkanInstance(in RHIInstanceDescriptor descriptor)
        {

            CheckExtensionSupport(descriptor);
            CheckValidationLayerSupport(descriptor);
            CreateVulkanInstance(descriptor);
        }

        private void CheckExtension(string[] availableinstanceExtensions, List<string> extensionsToEnable, string extension)
        {
            if (!availableinstanceExtensions.Any((string e) => e == extension))
            {
                Console.WriteLine("Vulkan", "The requiered instance extensions was not available: " + extension);
            }

            extensionsToEnable.Add(extension);
        }

        private void CheckExtensionSupport(in RHIInstanceDescriptor descriptor)
        {
            uint supportedExtensionCount;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceExtensionProperties(null, &supportedExtensionCount, null));
            VkExtensionProperties* supportedExtensions = stackalloc VkExtensionProperties[(int)supportedExtensionCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceExtensionProperties(null, &supportedExtensionCount, supportedExtensions));

            string[] array = new string[supportedExtensionCount];
            for (int i = 0; i < supportedExtensionCount; ++i)
            {
                array[i] = VulkanUtility.GetString(supportedExtensions[i].extensionName);
            }

            m_RequiredExtensions = new List<string>();
            CheckExtension(array, m_RequiredExtensions, "VK_KHR_surface");
            switch (VulkanUtility.GetCurrentOSPlatfom())
            {
                case EOSPlatform.Windows:
                    CheckExtension(array, m_RequiredExtensions, "VK_KHR_win32_surface");
                    break;
                case EOSPlatform.Linux:
                    CheckExtension(array, m_RequiredExtensions, "VK_KHR_xlib_surface");
                    break;
                case EOSPlatform.Android:
                    CheckExtension(array, m_RequiredExtensions, "VK_KHR_android_surface");
                    break;
                case EOSPlatform.MacOS:
                    CheckExtension(array, m_RequiredExtensions, "VK_MVK_macos_surface");
                    break;
                case EOSPlatform.iOS:
                    CheckExtension(array, m_RequiredExtensions, "VK_MVK_ios_surface");
                    break;
            }

            /*foreach (string item in m_RequiredExtensions)
            {
                CheckExtension(array, m_RequiredExtensions, item);
            }*/

            if (array.Any((string e) => e == "VK_KHR_get_physical_device_properties2"))
            {
                m_RequiredExtensions.Add("VK_KHR_get_physical_device_properties2");
            }

            if (descriptor.EnableValidatior)
            {
                if (array.Any((string e) => e == "VK_EXT_debug_utils"))
                {
                    m_RequiredExtensions.Add("VK_EXT_debug_utils");
                    //DebugUtilsEnabled = true;
                    //DebugMarkerEnabled = true;
                }
                else
                {
                    m_RequiredExtensions.Add("VK_EXT_debug_report");
                }
            }
        }

        private void CheckValidationLayerSupport(in RHIInstanceDescriptor descriptor)
        {
            uint layerCount;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, null));
            VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, availableLayers));

            string[] array = new string[layerCount];
            for (int i = 0; i < layerCount; ++i)
            {
                array[i] = VulkanUtility.GetString(availableLayers[i].layerName);
            }

            if (descriptor.EnableValidatior)
            {
                m_ValidationLayers = new List<string>();
                switch (VulkanUtility.GetCurrentOSPlatfom())
                {
                    case EOSPlatform.Windows:
                        if (array.Any((string l) => l == "VK_LAYER_KHRONOS_validation"))
                        {
                            m_ValidationLayers.Add("VK_LAYER_KHRONOS_validation");
                        }

                        break;
                    case EOSPlatform.Android:
                        if (array.Any((string l) => l == "VK_LAYER_LUNARG_core_validation"))
                        {
                            m_ValidationLayers.Add("VK_LAYER_LUNARG_core_validation");
                        }

                        if (array.Any((string l) => l == "VK_LAYER_LUNARG_swapchain"))
                        {
                            m_ValidationLayers.Add("VK_LAYER_LUNARG_swapchain");
                        }

                        if (array.Any((string l) => l == "VK_LAYER_LUNARG_parameter_validation"))
                        {
                            m_ValidationLayers.Add("VK_LAYER_LUNARG_parameter_validation");
                        }

                        break;
                }
            }
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
            IntPtr* extensionsToBytesArray = stackalloc IntPtr[m_RequiredExtensions.Count];
            for (int i = 0; i < m_RequiredExtensions.Count; ++i)
            {
                extensionsToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_RequiredExtensions[i]);
            }
            createInfo.enabledExtensionCount = (uint)m_RequiredExtensions.Count;
            createInfo.ppEnabledExtensionNames = (byte**)extensionsToBytesArray;

            // Validation layers
#if DEBUG
            IntPtr* layersToBytesArray = stackalloc IntPtr[m_ValidationLayers.Count];
            for (int i = 0; i < m_ValidationLayers.Count; ++i)
            {
                layersToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_ValidationLayers[i]);
            }
            createInfo.enabledLayerCount = (uint)m_ValidationLayers.Count;
            createInfo.ppEnabledLayerNames = (byte**)layersToBytesArray;
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
