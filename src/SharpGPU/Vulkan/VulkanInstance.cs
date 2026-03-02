using System;
using System.Linq;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Infinity.Graphics
{
#pragma warning disable CS8618
    internal unsafe class VulkanInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;

        public VkInstance NativeInstance => m_VkInstance;
        public bool HasDebugUtils => m_HasDebugUtils;

        private VkInstance m_VkInstance;
        private bool m_HasDebugUtils;
        private List<VulkanDevice> m_Devices;
        private List<string> m_ValidationLayers;
        private List<string> m_RequiredExtensions;

        // Debug utils function pointers (loaded at runtime via vkGetInstanceProcAddr)
        [StructLayout(LayoutKind.Sequential)]
        private struct VkDebugUtilsLabelEXT_
        {
            public uint sType; // VK_STRUCTURE_TYPE_DEBUG_UTILS_LABEL_EXT = 1000128002
            public void* pNext;
            public byte* pLabelName;
            public float color0, color1, color2, color3;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr PFN_vkGetInstanceProcAddr(VkInstance instance, byte* pName);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdBeginDebugUtilsLabel(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT_* pLabelInfo);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdEndDebugUtilsLabel(VkCommandBuffer commandBuffer);
        private PFN_vkCmdBeginDebugUtilsLabel m_CmdBeginDebugUtilsLabel;
        private PFN_vkCmdEndDebugUtilsLabel m_CmdEndDebugUtilsLabel;

        public VulkanInstance(in RHIInstanceDescriptor descriptor)
        {
            CheckExtensionSupport(descriptor);
            CheckValidationLayerSupport(descriptor);
            CreateVulkanInstance(descriptor);
            if (m_HasDebugUtils)
            {
                LoadDebugUtilsFunctions();
            }
            EnumeratePhysicalDevices(descriptor);
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
            uint supportedExtensionCount = 0;
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

            if (array.Any((string e) => e == "VK_KHR_get_physical_device_properties2"))
            {
                m_RequiredExtensions.Add("VK_KHR_get_physical_device_properties2");
            }

            if (descriptor.EnableValidatior)
            {
                if (array.Any((string e) => e == "VK_EXT_debug_utils"))
                {
                    m_RequiredExtensions.Add("VK_EXT_debug_utils");
                    m_HasDebugUtils = true;
                }
                else
                {
                    m_RequiredExtensions.Add("VK_EXT_debug_report");
                }
            }
        }

        private void CheckValidationLayerSupport(in RHIInstanceDescriptor descriptor)
        {
            uint layerCount = 0;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, null));
            VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumerateInstanceLayerProperties(&layerCount, availableLayers));

            string[] array = new string[layerCount];
            for (int i = 0; i < layerCount; ++i)
            {
                array[i] = VulkanUtility.GetString(availableLayers[i].layerName);
            }

            m_ValidationLayers = new List<string>();
            if (descriptor.EnableValidatior)
            {
                switch (VulkanUtility.GetCurrentOSPlatfom())
                {
                    case EOSPlatform.Windows:
                    case EOSPlatform.Linux:
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
                sType = VkStructureType.ApplicationInfo,
                pApplicationName = "Hello Triangle".ToPointer(),
                applicationVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                pEngineName = "No Engine".ToPointer(),
                engineVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                apiVersion = new VkVersion(VulkanUtility.Version(1, 3, 0)),
            };

            VkInstanceCreateInfo createInfo = default;
            createInfo.sType = VkStructureType.InstanceCreateInfo;
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
            if (m_ValidationLayers.Count > 0)
            {
                IntPtr* layersToBytesArray = stackalloc IntPtr[m_ValidationLayers.Count];
                for (int i = 0; i < m_ValidationLayers.Count; ++i)
                {
                    layersToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_ValidationLayers[i]);
                }
                createInfo.enabledLayerCount = (uint)m_ValidationLayers.Count;
                createInfo.ppEnabledLayerNames = (byte**)layersToBytesArray;
            }
            else
            {
                createInfo.enabledLayerCount = 0;
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

        private void EnumeratePhysicalDevices(in RHIInstanceDescriptor descriptor)
        {
            uint deviceCount = 0;
            VulkanUtility.CheckErrors(VulkanNative.vkEnumeratePhysicalDevices(m_VkInstance, &deviceCount, null));

            if (deviceCount == 0)
            {
                throw new InvalidOperationException("Failed to find GPUs with Vulkan support.");
            }

            VkPhysicalDevice* physicalDevices = stackalloc VkPhysicalDevice[(int)deviceCount];
            VulkanUtility.CheckErrors(VulkanNative.vkEnumeratePhysicalDevices(m_VkInstance, &deviceCount, physicalDevices));

            m_Devices = new List<VulkanDevice>((int)deviceCount);
            for (int i = 0; i < deviceCount; ++i)
            {
                m_Devices.Add(new VulkanDevice(this, physicalDevices[i], descriptor.ComputeQueueRequestCount, descriptor.TransferQueueRequestCount, descriptor.GraphicsQueueRequestCount));
            }
        }

        private void LoadDebugUtilsFunctions()
        {
            try
            {
                // The Vulkan loader is already loaded by Evergine bindings.
                // NativeLibrary.TryLoad returns the existing handle if already loaded.
                IntPtr vkLib = IntPtr.Zero;
                if (!System.Runtime.InteropServices.NativeLibrary.TryLoad("vulkan-1", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libvulkan.so.1", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libvulkan.so", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libvulkan.dylib", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libMoltenVK.dylib", out vkLib))
                {
                    m_HasDebugUtils = false;
                    return;
                }

                // Get vkGetInstanceProcAddr to load extension functions
                if (!System.Runtime.InteropServices.NativeLibrary.TryGetExport(vkLib, "vkGetInstanceProcAddr", out IntPtr getProcAddrPtr))
                {
                    m_HasDebugUtils = false;
                    return;
                }

                var vkGetInstanceProcAddr = Marshal.GetDelegateForFunctionPointer<PFN_vkGetInstanceProcAddr>(getProcAddrPtr);

                IntPtr beginPtr, endPtr;
                byte* beginName = (byte*)"vkCmdBeginDebugUtilsLabelEXT".ToPointer();
                byte* endName = (byte*)"vkCmdEndDebugUtilsLabelEXT".ToPointer();
                try
                {
                    beginPtr = vkGetInstanceProcAddr(m_VkInstance, beginName);
                    endPtr = vkGetInstanceProcAddr(m_VkInstance, endName);
                }
                finally
                {
                    Marshal.FreeHGlobal((IntPtr)beginName);
                    Marshal.FreeHGlobal((IntPtr)endName);
                }

                if (beginPtr != IntPtr.Zero && endPtr != IntPtr.Zero)
                {
                    m_CmdBeginDebugUtilsLabel = Marshal.GetDelegateForFunctionPointer<PFN_vkCmdBeginDebugUtilsLabel>(beginPtr);
                    m_CmdEndDebugUtilsLabel = Marshal.GetDelegateForFunctionPointer<PFN_vkCmdEndDebugUtilsLabel>(endPtr);
                }
                else
                {
                    m_HasDebugUtils = false;
                }
            }
            catch
            {
                m_HasDebugUtils = false;
            }
        }

        internal void CmdBeginDebugUtilsLabel(VkCommandBuffer commandBuffer, string name)
        {
            if (!m_HasDebugUtils || m_CmdBeginDebugUtilsLabel == null)
                return;

            byte* namePtr = (byte*)Marshal.StringToHGlobalAnsi(name);
            try
            {
                VkDebugUtilsLabelEXT_ labelInfo = default;
                labelInfo.sType = 1000128002; // VK_STRUCTURE_TYPE_DEBUG_UTILS_LABEL_EXT
                labelInfo.pLabelName = namePtr;
                labelInfo.color0 = 1.0f;
                labelInfo.color1 = 1.0f;
                labelInfo.color2 = 1.0f;
                labelInfo.color3 = 1.0f;
                m_CmdBeginDebugUtilsLabel(commandBuffer, &labelInfo);
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)namePtr);
            }
        }

        internal void CmdEndDebugUtilsLabel(VkCommandBuffer commandBuffer)
        {
            if (!m_HasDebugUtils || m_CmdEndDebugUtilsLabel == null)
                return;
            m_CmdEndDebugUtilsLabel(commandBuffer);
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

            VulkanNative.vkDestroyInstance(m_VkInstance, null);
        }
    }
#pragma warning restore CS8618
}


