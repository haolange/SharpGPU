using System;
using System.Linq;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
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
        private bool m_EnablePortabilityEnumeration;
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
        private delegate VkResult PFN_vkEnumerateInstanceExtensionProperties(byte* layerName, uint* propertyCount, VkExtensionProperties* properties);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate VkResult PFN_vkEnumerateInstanceLayerProperties(uint* propertyCount, VkLayerProperties* properties);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdBeginDebugUtilsLabel(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT_* pLabelInfo);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdEndDebugUtilsLabel(VkCommandBuffer commandBuffer);

        private PFN_vkCmdBeginDebugUtilsLabel m_CmdBeginDebugUtilsLabel;
        private PFN_vkCmdEndDebugUtilsLabel m_CmdEndDebugUtilsLabel;
        private static IntPtr s_VulkanGlobalLibrary;
        private static PFN_vkEnumerateInstanceExtensionProperties s_EnumerateInstanceExtensionProperties;
        private static PFN_vkEnumerateInstanceLayerProperties s_EnumerateInstanceLayerProperties;
        private static bool s_GlobalFunctionsLoaded;

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

        private static bool ContainsExtension(HashSet<string> availableExtensions, string extension)
        {
            return availableExtensions.Contains(extension);
        }

        private static void RequireExtension(HashSet<string> availableExtensions, List<string> extensionsToEnable, string extension)
        {
            if (!ContainsExtension(availableExtensions, extension))
            {
                throw new InvalidOperationException($"Required Vulkan instance extension is not available: {extension}");
            }

            if (!extensionsToEnable.Contains(extension))
            {
                extensionsToEnable.Add(extension);
            }
        }

        private static void EnableExtensionIfAvailable(HashSet<string> availableExtensions, List<string> extensionsToEnable, string extension)
        {
            if (ContainsExtension(availableExtensions, extension) && !extensionsToEnable.Contains(extension))
            {
                extensionsToEnable.Add(extension);
            }
        }

        private void CheckExtensionSupport(in RHIInstanceDescriptor descriptor)
        {
            EnsureGlobalVulkanFunctionsLoaded();
            uint supportedExtensionCount = 0;
            VulkanUtility.CheckErrors(s_EnumerateInstanceExtensionProperties(null, &supportedExtensionCount, null));
            VkExtensionProperties* supportedExtensions = stackalloc VkExtensionProperties[(int)supportedExtensionCount];
            VulkanUtility.CheckErrors(s_EnumerateInstanceExtensionProperties(null, &supportedExtensionCount, supportedExtensions));

            HashSet<string> availableExtensions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < supportedExtensionCount; ++i)
            {
                availableExtensions.Add(VulkanUtility.GetString(supportedExtensions[i].extensionName));
            }

            m_RequiredExtensions = new List<string>();
            m_EnablePortabilityEnumeration = false;

            RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_surface");

            switch (descriptor.SurfaceKind)
            {
                case RHINativeSurfaceKind.Win32Hwnd:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_win32_surface");
                    break;
                case RHINativeSurfaceKind.X11Window:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_xlib_surface");
                    break;
                case RHINativeSurfaceKind.WaylandSurface:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_wayland_surface");
                    break;
                case RHINativeSurfaceKind.AndroidNativeWindow:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_android_surface");
                    break;
                case RHINativeSurfaceKind.AppKitNsWindow:
                case RHINativeSurfaceKind.UIKitUiWindow:
                    // TODO(UNVERIFIED): iOS runtime verification pending.
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_EXT_metal_surface");
                    if (ContainsExtension(availableExtensions, "VK_KHR_portability_enumeration"))
                    {
                        EnableExtensionIfAvailable(availableExtensions, m_RequiredExtensions, "VK_KHR_portability_enumeration");
                        m_EnablePortabilityEnumeration = true;
                    }
                    break;
                default:
                    throw new NotSupportedException($"Vulkan surface kind '{descriptor.SurfaceKind}' is not supported.");
            }

            EnableExtensionIfAvailable(availableExtensions, m_RequiredExtensions, "VK_KHR_get_physical_device_properties2");

            if (descriptor.EnableValidatior)
            {
                if (ContainsExtension(availableExtensions, "VK_EXT_debug_utils"))
                {
                    EnableExtensionIfAvailable(availableExtensions, m_RequiredExtensions, "VK_EXT_debug_utils");
                    m_HasDebugUtils = true;
                }
                else
                {
                    EnableExtensionIfAvailable(availableExtensions, m_RequiredExtensions, "VK_EXT_debug_report");
                }
            }
        }

        private void CheckValidationLayerSupport(in RHIInstanceDescriptor descriptor)
        {
            EnsureGlobalVulkanFunctionsLoaded();
            uint layerCount = 0;
            VulkanUtility.CheckErrors(s_EnumerateInstanceLayerProperties(&layerCount, null));
            VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerCount];
            VulkanUtility.CheckErrors(s_EnumerateInstanceLayerProperties(&layerCount, availableLayers));

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
            byte* appName = "Hello Triangle".ToPointer();
            byte* engineName = "No Engine".ToPointer();
            VkApplicationInfo appInfo = new VkApplicationInfo()
            {
                sType = VkStructureType.ApplicationInfo,
                pApplicationName = appName,
                applicationVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                pEngineName = engineName,
                engineVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                apiVersion = new VkVersion(VulkanUtility.Version(1, 3, 0)),
            };

            VkInstanceCreateInfo createInfo = default;
            createInfo.sType = VkStructureType.InstanceCreateInfo;
            createInfo.pApplicationInfo = &appInfo;
            if (m_EnablePortabilityEnumeration)
            {
                createInfo.flags |= VkInstanceCreateFlags.EnumeratePortabilityKHR;
            }

            IntPtr* extensionsToBytesArray = stackalloc IntPtr[m_RequiredExtensions.Count];
            for (int i = 0; i < m_RequiredExtensions.Count; ++i)
            {
                extensionsToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_RequiredExtensions[i]);
            }

            IntPtr* layersToBytesArray = stackalloc IntPtr[Math.Max(1, m_ValidationLayers.Count)];
            int layerPointerCount = m_ValidationLayers.Count;

            createInfo.enabledExtensionCount = (uint)m_RequiredExtensions.Count;
            createInfo.ppEnabledExtensionNames = (byte**)extensionsToBytesArray;

#if DEBUG
            if (m_ValidationLayers.Count > 0)
            {
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

            try
            {
                fixed (VkInstance* instancePtr = &m_VkInstance)
                {
                    VulkanUtility.CheckErrors(VulkanNative.vkCreateInstance(&createInfo, null, instancePtr));
                }
            }
            finally
            {
                for (int i = 0; i < m_RequiredExtensions.Count; ++i)
                {
                    if (extensionsToBytesArray[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(extensionsToBytesArray[i]);
                    }
                }

                for (int i = 0; i < layerPointerCount; ++i)
                {
                    if (layersToBytesArray[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(layersToBytesArray[i]);
                    }
                }

                Marshal.FreeHGlobal((IntPtr)appName);
                Marshal.FreeHGlobal((IntPtr)engineName);
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
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libvulkan.1.dylib", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libvulkan.dylib", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("libMoltenVK.dylib", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("/usr/local/lib/libvulkan.1.dylib", out vkLib) &&
                    !System.Runtime.InteropServices.NativeLibrary.TryLoad("/usr/local/lib/libvulkan.dylib", out vkLib))
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

                IntPtr beginPtr;
                IntPtr endPtr;
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

        private static void EnsureGlobalVulkanFunctionsLoaded()
        {
            if (s_GlobalFunctionsLoaded)
            {
                return;
            }

            if (!NativeLibrary.TryLoad("vulkan-1", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("libvulkan.so.1", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("libvulkan.so", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("libvulkan.1.dylib", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("libvulkan.dylib", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("libMoltenVK.dylib", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("/usr/local/lib/libvulkan.1.dylib", out s_VulkanGlobalLibrary) &&
                !NativeLibrary.TryLoad("/usr/local/lib/libvulkan.dylib", out s_VulkanGlobalLibrary))
            {
                throw new InvalidOperationException("Failed to load Vulkan loader library.");
            }

            if (!NativeLibrary.TryGetExport(s_VulkanGlobalLibrary, "vkEnumerateInstanceExtensionProperties", out IntPtr enumExtensionsPtr))
            {
                throw new InvalidOperationException("Failed to load vkEnumerateInstanceExtensionProperties.");
            }

            if (!NativeLibrary.TryGetExport(s_VulkanGlobalLibrary, "vkEnumerateInstanceLayerProperties", out IntPtr enumLayersPtr))
            {
                throw new InvalidOperationException("Failed to load vkEnumerateInstanceLayerProperties.");
            }

            s_EnumerateInstanceExtensionProperties = Marshal.GetDelegateForFunctionPointer<PFN_vkEnumerateInstanceExtensionProperties>(enumExtensionsPtr);
            s_EnumerateInstanceLayerProperties = Marshal.GetDelegateForFunctionPointer<PFN_vkEnumerateInstanceLayerProperties>(enumLayersPtr);
            s_GlobalFunctionsLoaded = true;
        }

        internal void CmdBeginDebugUtilsLabel(VkCommandBuffer commandBuffer, string name)
        {
            if (!m_HasDebugUtils || m_CmdBeginDebugUtilsLabel == null)
            {
                return;
            }

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
            {
                return;
            }

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

            m_Devices.Clear();
            VulkanNative.vkDestroyInstance(m_VkInstance, null);
        }
    }

#pragma warning restore CS8618
}
