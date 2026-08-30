using System;
using System.Linq;
using Vortice.Vulkan;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal unsafe class VulkanInstance : RHIInstance
    {
        public override int DeviceCount => m_Devices.Count;
        public override ERHIBackend BackendType => ERHIBackend.Vulkan;

        public VkInstance NativeInstance => m_VkInstance;
        internal uint LoaderApiVersion => m_LoaderApiVersion;
        internal uint ApiVersion => m_ApiVersion;
        internal ERHINativeSurfaceKind SurfaceKind => m_SurfaceKind;
        internal VulkanValidationDiagnostics? ValidationDiagnostics =>
            m_ValidationDiagnostics;
        public bool HasDebugUtils => m_HasDebugUtils;
        public bool HasValidationLayerEnabled =>
            m_HasValidationLayerEnabled;

        private VkInstance m_VkInstance;
        private bool m_HasDebugUtils;
        private bool m_HasValidationLayerEnabled;
        private VulkanValidationDiagnostics? m_ValidationDiagnostics;
        private bool m_EnablePortabilityEnumeration;
        private List<VulkanDevice> m_Devices = new List<VulkanDevice>();
        private uint m_LoaderApiVersion;
        private uint m_ApiVersion;
        private List<string> m_ValidationLayers = new List<string>();
        private readonly ERHINativeSurfaceKind m_SurfaceKind;
        private List<string> m_RequiredExtensions = new List<string>();

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
        private delegate VkResult PFN_vkEnumerateInstanceVersion(uint* apiVersion);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate VkResult PFN_vkEnumerateInstanceExtensionProperties(byte* layerName, uint* propertyCount, VkExtensionProperties* properties);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate VkResult PFN_vkEnumerateInstanceLayerProperties(uint* propertyCount, VkLayerProperties* properties);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdBeginDebugUtilsLabel(VkCommandBuffer commandBuffer, VkDebugUtilsLabelEXT_* pLabelInfo);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void PFN_vkCmdEndDebugUtilsLabel(VkCommandBuffer commandBuffer);

        private PFN_vkCmdBeginDebugUtilsLabel? m_CmdBeginDebugUtilsLabel;
        private PFN_vkCmdEndDebugUtilsLabel? m_CmdEndDebugUtilsLabel;
        private static IntPtr s_VulkanGlobalLibrary;
        private static PFN_vkGetInstanceProcAddr? s_GetInstanceProcAddr;
        private static PFN_vkEnumerateInstanceVersion? s_EnumerateInstanceVersion;
        private static PFN_vkEnumerateInstanceExtensionProperties? s_EnumerateInstanceExtensionProperties;
        private static PFN_vkEnumerateInstanceLayerProperties? s_EnumerateInstanceLayerProperties;
        private static bool s_GlobalFunctionsLoaded;
        private static readonly object s_GlobalFunctionsGate = new();

        public VulkanInstance(in RHIInstanceDescriptor descriptor)
        {
            m_SurfaceKind = descriptor.SurfaceKind;
            CheckExtensionSupport(descriptor);
            CheckValidationLayerSupport(descriptor);
            CreateVulkanInstance(descriptor);
            if (m_HasDebugUtils)
            {
                LoadDebugUtilsFunctions();
            }
            EnumeratePhysicalDevices(descriptor);
        }

        internal static bool RequiresSwapchainDeviceExtension(
            ERHINativeSurfaceKind surfaceKind)
        {
            if (!Enum.IsDefined(surfaceKind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(surfaceKind),
                    surfaceKind,
                    "The native surface kind is not defined.");
            }

            return surfaceKind != ERHINativeSurfaceKind.Headless;
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
            PFN_vkEnumerateInstanceExtensionProperties enumerateExtensions = s_EnumerateInstanceExtensionProperties
                ?? throw new InvalidOperationException("Vulkan loader did not expose vkEnumerateInstanceExtensionProperties.");
            uint supportedExtensionCount = 0;
            VulkanUtility.CheckErrors(enumerateExtensions(null, &supportedExtensionCount, null));
            VkExtensionProperties* supportedExtensions = stackalloc VkExtensionProperties[(int)supportedExtensionCount];
            VulkanUtility.CheckErrors(enumerateExtensions(null, &supportedExtensionCount, supportedExtensions));

            HashSet<string> availableExtensions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < supportedExtensionCount; ++i)
            {
                availableExtensions.Add(VulkanUtility.GetString(supportedExtensions[i].extensionName));
            }

            m_RequiredExtensions = new List<string>();
            m_EnablePortabilityEnumeration = false;

            if (descriptor.SurfaceKind != ERHINativeSurfaceKind.Headless)
            {
                RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_surface");
            }

            switch (descriptor.SurfaceKind)
            {
                case ERHINativeSurfaceKind.Headless:
                    break;
                case ERHINativeSurfaceKind.Win32Hwnd:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_win32_surface");
                    break;
                case ERHINativeSurfaceKind.X11Window:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_xlib_surface");
                    break;
                case ERHINativeSurfaceKind.WaylandSurface:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_wayland_surface");
                    break;
                case ERHINativeSurfaceKind.AndroidNativeWindow:
                    RequireExtension(availableExtensions, m_RequiredExtensions, "VK_KHR_android_surface");
                    break;
                case ERHINativeSurfaceKind.AppKitNsWindow:
                case ERHINativeSurfaceKind.UIKitUiWindow:
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

            if (descriptor.EnableValidation)
            {
                if (ContainsExtension(availableExtensions, "VK_EXT_debug_utils"))
                {
                    EnableExtensionIfAvailable(availableExtensions, m_RequiredExtensions, "VK_EXT_debug_utils");
                    m_HasDebugUtils = true;
                }
                else
                {
                    throw new NotSupportedException(
                        "Vulkan validation requires VK_EXT_debug_utils so " +
                        "SharpGPU can collect diagnostics from instance and " +
                        "logical-device creation onward.");
                }
            }
        }

        private void CheckValidationLayerSupport(in RHIInstanceDescriptor descriptor)
        {
            EnsureGlobalVulkanFunctionsLoaded();
            PFN_vkEnumerateInstanceLayerProperties enumerateLayers = s_EnumerateInstanceLayerProperties
                ?? throw new InvalidOperationException("Vulkan loader did not expose vkEnumerateInstanceLayerProperties.");
            uint layerCount = 0;
            VulkanUtility.CheckErrors(enumerateLayers(&layerCount, null));
            VkLayerProperties* availableLayers = stackalloc VkLayerProperties[(int)layerCount];
            VulkanUtility.CheckErrors(enumerateLayers(&layerCount, availableLayers));

            string[] array = new string[layerCount];
            for (int i = 0; i < layerCount; ++i)
            {
                array[i] = VulkanUtility.GetString(availableLayers[i].layerName);
            }

            m_ValidationLayers = new List<string>();
            if (descriptor.EnableValidation)
            {
                if (array.Any(static layer => layer == "VK_LAYER_KHRONOS_validation"))
                {
                    m_ValidationLayers.Add("VK_LAYER_KHRONOS_validation");
                }
                else
                {
                    throw new NotSupportedException(
                        "Vulkan validation was requested, but VK_LAYER_KHRONOS_validation " +
                        "is not available. Install or externally inject the Khronos validation layer.");
                }
            }
        }

        private void CreateVulkanInstance(in RHIInstanceDescriptor descriptor)
        {
            byte* appName = null;
            byte* engineName = null;
            IntPtr* extensionsToBytesArray = stackalloc IntPtr[Math.Max(1, m_RequiredExtensions.Count)];
            IntPtr* layersToBytesArray = stackalloc IntPtr[Math.Max(1, m_ValidationLayers.Count)];
            int extensionPointerCount = 0;
            int layerPointerCount = 0;

            try
            {
                m_LoaderApiVersion = QueryLoaderApiVersion();
                m_ApiVersion = SelectInstanceApiVersion(
                    m_LoaderApiVersion,
                    VulkanUtility.GetCurrentOSPlatfom());
                appName = "InfinityBrowser".ToPointer();
                engineName = "SharpGPU".ToPointer();
                VkApplicationInfo appInfo = new VkApplicationInfo()
                {
                    sType = VkStructureType.ApplicationInfo,
                    pApplicationName = appName,
                    applicationVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                    pEngineName = engineName,
                    engineVersion = new VkVersion(VulkanUtility.Version(1, 0, 0)),
                    apiVersion = new VkVersion(m_ApiVersion),
                };

                VkInstanceCreateInfo createInfo = default;
                createInfo.sType = VkStructureType.InstanceCreateInfo;
                createInfo.pApplicationInfo = &appInfo;
                if (m_EnablePortabilityEnumeration)
                {
                    createInfo.flags |= VkInstanceCreateFlags.EnumeratePortabilityKHR;
                }

                for (int i = 0; i < m_RequiredExtensions.Count; ++i)
                {
                    extensionsToBytesArray[extensionPointerCount] = Marshal.StringToHGlobalAnsi(m_RequiredExtensions[i]);
                    ++extensionPointerCount;
                }

                createInfo.enabledExtensionCount = (uint)extensionPointerCount;
                createInfo.ppEnabledExtensionNames = (byte**)extensionsToBytesArray;

#if DEBUG
                if (m_ValidationLayers.Count > 0)
                {
                    for (int i = 0; i < m_ValidationLayers.Count; ++i)
                    {
                        layersToBytesArray[i] = Marshal.StringToHGlobalAnsi(m_ValidationLayers[i]);
                        ++layerPointerCount;
                    }

                    createInfo.enabledLayerCount = (uint)layerPointerCount;
                    createInfo.ppEnabledLayerNames = (byte**)layersToBytesArray;
                }
                else
                {
                    createInfo.enabledLayerCount = 0;
                }
#else
                createInfo.enabledLayerCount = 0;
#endif

                VulkanValidationDiagnostics? validationDiagnostics = null;
                VulkanDebugUtilsMessengerCreateInfo validationCreateInfo =
                    default;
                if (descriptor.EnableValidation)
                {
                    if (!m_HasDebugUtils)
                    {
                        throw new NotSupportedException(
                            "Vulkan validation diagnostics require " +
                            "VK_EXT_debug_utils.");
                    }

                    validationDiagnostics = new VulkanValidationDiagnostics();
                    validationCreateInfo =
                        validationDiagnostics.CreateInstanceCreateInfo();
                    createInfo.pNext = &validationCreateInfo;
                }

                try
                {
                    fixed (VkInstance* instancePtr = &m_VkInstance)
                    {
                        VulkanUtility.CheckErrors(
                            VulkanNative.vkCreateInstance(
                                &createInfo,
                                null,
                                instancePtr));
                    }

                    m_HasValidationLayerEnabled =
                        createInfo.enabledLayerCount > 0;
                    if (validationDiagnostics != null)
                    {
                        validationDiagnostics.Attach(
                            m_VkInstance,
                            GetInstanceProcedure(
                                m_VkInstance,
                                "vkCreateDebugUtilsMessengerEXT"),
                            GetInstanceProcedure(
                                m_VkInstance,
                                "vkDestroyDebugUtilsMessengerEXT"));
                        m_ValidationDiagnostics = validationDiagnostics;
                    }
                }
                catch
                {
                    validationDiagnostics?.Dispose();
                    if (m_VkInstance.Handle != 0)
                    {
                        VulkanNative.vkDestroyInstance(m_VkInstance, null);
                        m_VkInstance = default;
                    }

                    throw;
                }
            }
            finally
            {
                for (int i = 0; i < extensionPointerCount; ++i)
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

                if (appName != null)
                {
                    Marshal.FreeHGlobal((IntPtr)appName);
                }

                if (engineName != null)
                {
                    Marshal.FreeHGlobal((IntPtr)engineName);
                }
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
            EOSPlatform platform = VulkanUtility.GetCurrentOSPlatfom();
            uint androidMinimum = VulkanUtility.Version(1, 1, 0);
            for (int i = 0; i < deviceCount; ++i)
            {
                VkPhysicalDeviceProperties properties;
                VulkanNative.vkGetPhysicalDeviceProperties(
                    physicalDevices[i],
                    &properties);
                if (platform == EOSPlatform.Android &&
                    properties.apiVersion.Value < androidMinimum)
                {
                    continue;
                }

                m_Devices.Add(new VulkanDevice(this, physicalDevices[i], descriptor.ComputeQueueRequestCount, descriptor.TransferQueueRequestCount, descriptor.GraphicsQueueRequestCount));
            }

            if (m_Devices.Count == 0)
            {
                throw new NotSupportedException(
                    "Android SharpGPU requires a Vulkan 1.1-or-newer physical device.");
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

        private static IntPtr GetInstanceProcedure(
            VkInstance instance,
            string name)
        {
            EnsureGlobalVulkanFunctionsLoaded();
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            byte* nativeName =
                (byte*)Marshal.StringToHGlobalAnsi(name);
            try
            {
                IntPtr procedure = s_GetInstanceProcAddr!(
                    instance,
                    nativeName);
                if (procedure == IntPtr.Zero)
                {
                    throw new NotSupportedException(
                        $"Vulkan instance function '{name}' is unavailable.");
                }

                return procedure;
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)nativeName);
            }
        }

        private static void EnsureGlobalVulkanFunctionsLoaded()
        {
            lock (s_GlobalFunctionsGate)
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

                if (!NativeLibrary.TryGetExport(
                        s_VulkanGlobalLibrary,
                        "vkGetInstanceProcAddr",
                        out IntPtr getInstanceProcAddrPtr))
                {
                    throw new InvalidOperationException(
                        "Failed to load vkGetInstanceProcAddr.");
                }

                if (!NativeLibrary.TryGetExport(s_VulkanGlobalLibrary, "vkEnumerateInstanceExtensionProperties", out IntPtr enumExtensionsPtr))
                {
                    throw new InvalidOperationException("Failed to load vkEnumerateInstanceExtensionProperties.");
                }

                if (!NativeLibrary.TryGetExport(s_VulkanGlobalLibrary, "vkEnumerateInstanceLayerProperties", out IntPtr enumLayersPtr))
                {
                    throw new InvalidOperationException("Failed to load vkEnumerateInstanceLayerProperties.");
                }

                if (NativeLibrary.TryGetExport(
                        s_VulkanGlobalLibrary,
                        "vkEnumerateInstanceVersion",
                        out IntPtr enumerateVersionPtr))
                {
                    s_EnumerateInstanceVersion =
                        Marshal.GetDelegateForFunctionPointer<PFN_vkEnumerateInstanceVersion>(
                            enumerateVersionPtr);
                }

                s_GetInstanceProcAddr =
                    Marshal.GetDelegateForFunctionPointer<
                        PFN_vkGetInstanceProcAddr>(
                        getInstanceProcAddrPtr);
                s_EnumerateInstanceExtensionProperties = Marshal.GetDelegateForFunctionPointer<PFN_vkEnumerateInstanceExtensionProperties>(enumExtensionsPtr);
                s_EnumerateInstanceLayerProperties = Marshal.GetDelegateForFunctionPointer<PFN_vkEnumerateInstanceLayerProperties>(enumLayersPtr);
                s_GlobalFunctionsLoaded = true;
            }
        }

        private static uint QueryLoaderApiVersion()
        {
            EnsureGlobalVulkanFunctionsLoaded();
            if (s_EnumerateInstanceVersion == null)
            {
                return VulkanUtility.Version(1, 0, 0);
            }

            uint loaderVersion = 0;
            VulkanUtility.CheckErrors(s_EnumerateInstanceVersion(&loaderVersion));
            if (loaderVersion < VulkanUtility.Version(1, 0, 0))
            {
                throw new InvalidOperationException(
                    $"The Vulkan loader reported an invalid API version 0x{loaderVersion:X8}.");
            }

            return loaderVersion;
        }

        internal static uint SelectInstanceApiVersion(
            uint loaderApiVersion,
            EOSPlatform platform)
        {
            uint minimum = VulkanUtility.Version(1, 0, 0);
            uint androidMinimum = VulkanUtility.Version(1, 1, 0);
            uint loaderMajor = loaderApiVersion >> 22;
            if (loaderMajor != 1)
            {
                throw new NotSupportedException(
                    $"SharpGPU recognizes Vulkan 1.x instance negotiation, " +
                    $"but the loader reported API version 0x{loaderApiVersion:X8}.");
            }
            if (loaderApiVersion < minimum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(loaderApiVersion),
                    loaderApiVersion,
                    "The Vulkan loader API version must be at least 1.0.");
            }
            if (platform == EOSPlatform.Android &&
                loaderApiVersion < androidMinimum)
            {
                throw new NotSupportedException(
                    "Android SharpGPU requires a Vulkan 1.1-or-newer loader.");
            }

            return loaderApiVersion;
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
            m_ValidationDiagnostics?.Dispose();
            m_ValidationDiagnostics = null;
            VulkanNative.vkDestroyInstance(m_VkInstance, null);
        }
    }

}


namespace SharpGPU
{
    internal enum EVulkanDescriptorIndexingPath : byte
    {
        Unavailable = 0,
        Vulkan12Core = 1,
        ExtDescriptorIndexing = 2,
    }

    internal enum EVulkanFeatureProvenance : byte
    {
        Unavailable = 0,
        Vulkan12Core = 1,
        Vulkan13Core = 2,
        Vulkan14Core = 3,
        KhrExtension = 4,
        ExtExtension = 5,
    }

    internal readonly struct VulkanFeatureChainPlan
    {
        public uint EffectiveApiVersion { get; }
        public bool UseVulkan12Features { get; }
        public bool UseVulkan13Features { get; }
        public bool UseVulkan14Features { get; }
        public bool EnableDynamicRenderingLocalReadExtension { get; }

        public bool UseDynamicRenderingLocalReadExtensionFeatureStruct =>
            DynamicRenderingLocalReadQueryProvenance ==
                EVulkanFeatureProvenance.KhrExtension;
        public EVulkanDescriptorIndexingPath DescriptorIndexingPath { get; }
        public bool UseDynamicRenderingExtension { get; }
        public bool UseSynchronization2Extension { get; }
        public bool SupportsRenderPass2 { get; }
        public bool UseCreateRenderPass2Extension { get; }
        public EVulkanFeatureProvenance DynamicRenderingProvenance { get; }
        public EVulkanFeatureProvenance RenderPass2Provenance { get; }
        public EVulkanFeatureProvenance DynamicRenderingLocalReadQueryProvenance { get; }

        private VulkanFeatureChainPlan(
            uint effectiveApiVersion,
            bool useVulkan12Features,
            bool useVulkan13Features,
            bool useVulkan14Features,
            bool enableDynamicRenderingLocalReadExtension,
            EVulkanDescriptorIndexingPath descriptorIndexingPath,
            bool useDynamicRenderingExtension,
            bool useSynchronization2Extension,
            bool supportsRenderPass2,
            bool useCreateRenderPass2Extension,
            EVulkanFeatureProvenance dynamicRenderingProvenance,
            EVulkanFeatureProvenance renderPass2Provenance,
            EVulkanFeatureProvenance dynamicRenderingLocalReadQueryProvenance)
        {
            EffectiveApiVersion = effectiveApiVersion;
            UseVulkan12Features = useVulkan12Features;
            UseVulkan13Features = useVulkan13Features;
            UseVulkan14Features = useVulkan14Features;
            EnableDynamicRenderingLocalReadExtension =
                enableDynamicRenderingLocalReadExtension;
            DescriptorIndexingPath = descriptorIndexingPath;
            UseDynamicRenderingExtension = useDynamicRenderingExtension;
            UseSynchronization2Extension = useSynchronization2Extension;
            SupportsRenderPass2 = supportsRenderPass2;
            UseCreateRenderPass2Extension = useCreateRenderPass2Extension;
            DynamicRenderingProvenance = dynamicRenderingProvenance;
            RenderPass2Provenance = renderPass2Provenance;
            DynamicRenderingLocalReadQueryProvenance =
                dynamicRenderingLocalReadQueryProvenance;
        }

        public static VulkanFeatureChainPlan Create(
            uint instanceApiVersion,
            uint physicalDeviceApiVersion,
            bool hasDescriptorIndexingExtension,
            bool hasDynamicRenderingExtension,
            bool hasCreateRenderPass2Extension,
            bool hasSynchronization2Extension,
            bool hasDynamicRenderingLocalReadExtension = false)
        {
            ValidateInstanceApiVersion(instanceApiVersion);
            ValidatePhysicalDeviceApiVersion(physicalDeviceApiVersion);

            uint effectiveApiVersion = Math.Min(
                instanceApiVersion,
                physicalDeviceApiVersion);
            bool useVulkan12Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 2, 0);
            bool useVulkan13Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 3, 0);
            bool useVulkan14Features =
                effectiveApiVersion >= VulkanUtility.Version(1, 4, 0);

            EVulkanDescriptorIndexingPath descriptorIndexingPath =
                useVulkan12Features
                    ? EVulkanDescriptorIndexingPath.Vulkan12Core
                    : hasDescriptorIndexingExtension
                        ? EVulkanDescriptorIndexingPath.ExtDescriptorIndexing
                        : EVulkanDescriptorIndexingPath.Unavailable;

            bool useCreateRenderPass2Extension =
                !useVulkan12Features && hasCreateRenderPass2Extension;
            bool useDynamicRenderingExtension =
                !useVulkan13Features && hasDynamicRenderingExtension;

            return new VulkanFeatureChainPlan(
                effectiveApiVersion,
                useVulkan12Features,
                useVulkan13Features,
                useVulkan14Features,
                enableDynamicRenderingLocalReadExtension:
                    hasDynamicRenderingLocalReadExtension,
                descriptorIndexingPath,
                useDynamicRenderingExtension,
                useSynchronization2Extension:
                    !useVulkan13Features && hasSynchronization2Extension,
                supportsRenderPass2:
                    useVulkan12Features ||
                    useCreateRenderPass2Extension,
                useCreateRenderPass2Extension,
                dynamicRenderingProvenance:
                    useVulkan13Features
                        ? EVulkanFeatureProvenance.Vulkan13Core
                        : useDynamicRenderingExtension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable,
                renderPass2Provenance:
                    useVulkan12Features
                        ? EVulkanFeatureProvenance.Vulkan12Core
                        : useCreateRenderPass2Extension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable,
                dynamicRenderingLocalReadQueryProvenance:
                    useVulkan14Features
                        ? EVulkanFeatureProvenance.Vulkan14Core
                        : hasDynamicRenderingLocalReadExtension
                            ? EVulkanFeatureProvenance.KhrExtension
                            : EVulkanFeatureProvenance.Unavailable);
        }

        private static void ValidateInstanceApiVersion(uint version)
        {
            uint minimum = VulkanUtility.Version(1, 0, 0);
            if (version < minimum || GetMajor(version) != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    "The negotiated Vulkan instance API version must be Vulkan 1.x.");
            }
        }

        private static void ValidatePhysicalDeviceApiVersion(uint version)
        {
            if (version < VulkanUtility.Version(1, 0, 0) ||
                GetMajor(version) != 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(version),
                    version,
                    "The Vulkan physical-device API version must be Vulkan 1.x.");
            }
        }

        private static uint GetMajor(uint version)
        {
            return (version >> 22) & 0x7Fu;
        }
    }
}

