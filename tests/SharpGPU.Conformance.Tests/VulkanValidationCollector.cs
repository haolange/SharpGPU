using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU.Conformance.Tests
{
    internal sealed class VulkanValidationCollector : IDisposable
    {
        private const uint DebugUtilsMessengerCreateInfoStructureType =
            1000128004;
        private const uint ErrorSeverity = 0x00001000;
        private const uint GeneralMessageType = 0x00000001;
        private const uint ValidationMessageType = 0x00000002;
        private const uint PerformanceMessageType = 0x00000004;

        private readonly ConcurrentQueue<string> m_Errors = new();
        private readonly DebugUtilsMessengerCallback m_Callback;
        private readonly DestroyDebugUtilsMessenger m_Destroy;
        private readonly IntPtr m_Instance;
        private IntPtr m_Loader;
        private ulong m_Messenger;
        private bool m_IsCompleted;
        private bool m_IsDisposed;

        private VulkanValidationCollector(
            IntPtr loader,
            IntPtr instance,
            DestroyDebugUtilsMessenger destroy)
        {
            m_Loader = loader;
            m_Instance = instance;
            m_Destroy = destroy;
            m_Callback = Collect;
        }

        public static VulkanValidationCollector Attach(IntPtr instance)
        {
            IntPtr loader = LoadVulkanLoader();
            try
            {
                GetInstanceProcAddress getInstanceProcAddress =
                    Marshal.GetDelegateForFunctionPointer<
                        GetInstanceProcAddress>(
                            NativeLibrary.GetExport(
                                loader,
                                "vkGetInstanceProcAddr"));
                CreateDebugUtilsMessenger create =
                    GetInstanceFunction<CreateDebugUtilsMessenger>(
                        getInstanceProcAddress,
                        instance,
                        "vkCreateDebugUtilsMessengerEXT");
                DestroyDebugUtilsMessenger destroy =
                    GetInstanceFunction<DestroyDebugUtilsMessenger>(
                        getInstanceProcAddress,
                        instance,
                        "vkDestroyDebugUtilsMessengerEXT");
                VulkanValidationCollector collector = new(
                    loader,
                    instance,
                    destroy);
                NativeDebugUtilsMessengerCreateInfo createInfo = new()
                {
                    StructureType =
                        DebugUtilsMessengerCreateInfoStructureType,
                    MessageSeverity = ErrorSeverity,
                    MessageType =
                        GeneralMessageType
                        | ValidationMessageType
                        | PerformanceMessageType,
                    UserCallback =
                        Marshal.GetFunctionPointerForDelegate(
                            collector.m_Callback),
                };

                int result = create(
                    instance,
                    ref createInfo,
                    IntPtr.Zero,
                    out collector.m_Messenger);
                if (result != 0)
                {
                    throw new InvalidOperationException(
                        $"vkCreateDebugUtilsMessengerEXT failed with VkResult {result}.");
                }

                return collector;
            }
            catch
            {
                NativeLibrary.Free(loader);
                throw;
            }
        }

        public void CompleteAndAssert(string observedStages)
        {
            ObjectDisposedException.ThrowIf(
                m_IsDisposed,
                this);
            if (m_IsCompleted)
            {
                throw new InvalidOperationException(
                    "Vulkan validation collection is already complete.");
            }

            DestroyMessenger();
            m_IsCompleted = true;

            string[] errors = m_Errors.ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Vulkan validation reported Error messages during {observedStages}:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, errors));
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            try
            {
                DestroyMessenger();
            }
            catch
            {
                // Never replace the primary test failure during fallback cleanup.
            }
            finally
            {
                try
                {
                    if (m_Loader != IntPtr.Zero)
                    {
                        NativeLibrary.Free(m_Loader);
                    }
                }
                catch
                {
                    // Never replace the primary test failure during fallback cleanup.
                }
                finally
                {
                    m_Loader = IntPtr.Zero;
                    m_IsDisposed = true;
                }
            }
        }

        private void DestroyMessenger()
        {
            if (m_Messenger == 0)
            {
                return;
            }

            m_Destroy(
                m_Instance,
                m_Messenger,
                IntPtr.Zero);
            m_Messenger = 0;
        }

        private uint Collect(
            uint severity,
            uint messageTypes,
            IntPtr callbackData,
            IntPtr userData)
        {
            _ = userData;
            try
            {
                NativeDebugUtilsMessengerCallbackData data =
                    Marshal.PtrToStructure<
                        NativeDebugUtilsMessengerCallbackData>(
                            callbackData);
                string idName =
                    Marshal.PtrToStringUTF8(data.MessageIdName)
                    ?? "unknown";
                string message =
                    Marshal.PtrToStringUTF8(data.Message)
                    ?? "Vulkan validation returned no message text.";
                m_Errors.Enqueue(
                    $"[severity=0x{severity:X}, types={FormatMessageTypes(messageTypes)}, id={idName}/{data.MessageIdNumber}] {message}");
            }
            catch (Exception exception)
            {
                m_Errors.Enqueue(
                    "Failed to decode a Vulkan validation Error callback: "
                    + exception.Message);
            }

            return 0;
        }

        private static T GetInstanceFunction<T>(
            GetInstanceProcAddress getInstanceProcAddress,
            IntPtr instance,
            string name)
            where T : Delegate
        {
            IntPtr nativeName =
                Marshal.StringToCoTaskMemUTF8(name);
            try
            {
                IntPtr address =
                    getInstanceProcAddress(
                        instance,
                        nativeName);
                if (address == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"Vulkan instance function '{name}' is unavailable.");
                }

                return Marshal.GetDelegateForFunctionPointer<T>(
                    address);
            }
            finally
            {
                Marshal.FreeCoTaskMem(nativeName);
            }
        }

        private static IntPtr LoadVulkanLoader()
        {
            string[] candidates =
            {
                "vulkan-1",
                "libvulkan.so.1",
                "libvulkan.so",
                "libvulkan.1.dylib",
                "libvulkan.dylib",
                "libMoltenVK.dylib",
                "/usr/local/lib/libvulkan.1.dylib",
                "/usr/local/lib/libvulkan.dylib",
            };
            foreach (string candidate in candidates)
            {
                if (NativeLibrary.TryLoad(
                        candidate,
                        out IntPtr loader))
                {
                    return loader;
                }
            }

            throw new InvalidOperationException(
                "Unable to load the Vulkan loader for the validation collector.");
        }

        private static string FormatMessageTypes(uint messageTypes)
        {
            List<string> names = new(3);
            if ((messageTypes & GeneralMessageType) != 0)
            {
                names.Add("General");
            }

            if ((messageTypes & ValidationMessageType) != 0)
            {
                names.Add("Validation");
            }

            if ((messageTypes & PerformanceMessageType) != 0)
            {
                names.Add("Performance");
            }

            return names.Count == 0
                ? $"0x{messageTypes:X}"
                : string.Join("|", names);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDebugUtilsMessengerCreateInfo
        {
            public uint StructureType;
            public IntPtr Next;
            public uint Flags;
            public uint MessageSeverity;
            public uint MessageType;
            public IntPtr UserCallback;
            public IntPtr UserData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeDebugUtilsMessengerCallbackData
        {
            public uint StructureType;
            public IntPtr Next;
            public uint Flags;
            public IntPtr MessageIdName;
            public int MessageIdNumber;
            public IntPtr Message;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr GetInstanceProcAddress(
            IntPtr instance,
            IntPtr name);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CreateDebugUtilsMessenger(
            IntPtr instance,
            ref NativeDebugUtilsMessengerCreateInfo createInfo,
            IntPtr allocator,
            out ulong messenger);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyDebugUtilsMessenger(
            IntPtr instance,
            ulong messenger,
            IntPtr allocator);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint DebugUtilsMessengerCallback(
            uint severity,
            uint messageTypes,
            IntPtr callbackData,
            IntPtr userData);
    }
}
