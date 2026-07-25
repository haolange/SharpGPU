using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace SharpGPU
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct VulkanDebugUtilsMessengerCreateInfo
    {
        internal uint StructureType;
        internal void* Next;
        internal uint Flags;
        internal uint MessageSeverity;
        internal uint MessageType;
        internal IntPtr UserCallback;
        internal void* UserData;
    }

    /// <summary>
    /// Owns the debug-utils messenger created while a Vulkan instance is still
    /// being constructed. Keeping the creation-info callback in the instance
    /// pNext chain is required to observe validation failures from logical
    /// device creation as well as from later command recording and execution.
    /// </summary>
    internal unsafe sealed class VulkanValidationDiagnostics : IDisposable
    {
        private const uint DebugUtilsMessengerCreateInfoStructureType =
            1000128004;
        private const uint ErrorSeverity = 0x00001000;
        private const uint GeneralMessageType = 0x00000001;
        private const uint ValidationMessageType = 0x00000002;
        private const uint PerformanceMessageType = 0x00000004;

        private readonly ConcurrentQueue<string> m_Errors = new();
        private readonly DebugUtilsMessengerCallback m_Callback;
        private DestroyDebugUtilsMessenger? m_Destroy;
        private VkInstance m_Instance;
        private ulong m_Messenger;
        private bool m_Disposed;

        internal VulkanValidationDiagnostics()
        {
            m_Callback = Collect;
        }

        internal VulkanDebugUtilsMessengerCreateInfo
            CreateInstanceCreateInfo()
        {
            ThrowIfDisposed();
            return new VulkanDebugUtilsMessengerCreateInfo
            {
                StructureType =
                    DebugUtilsMessengerCreateInfoStructureType,
                MessageSeverity = ErrorSeverity,
                MessageType =
                    GeneralMessageType |
                    ValidationMessageType |
                    PerformanceMessageType,
                UserCallback =
                    Marshal.GetFunctionPointerForDelegate(m_Callback),
            };
        }

        internal void Attach(
            VkInstance instance,
            IntPtr createProcedure,
            IntPtr destroyProcedure)
        {
            ThrowIfDisposed();
            if (instance.Handle == 0)
            {
                throw new ArgumentException(
                    "A non-null Vulkan instance is required.",
                    nameof(instance));
            }
            if (createProcedure == IntPtr.Zero ||
                destroyProcedure == IntPtr.Zero)
            {
                throw new NotSupportedException(
                    "VK_EXT_debug_utils does not expose its required " +
                    "messenger entry points.");
            }
            if (m_Messenger != 0)
            {
                throw new InvalidOperationException(
                    "The Vulkan validation messenger is already attached.");
            }

            CreateDebugUtilsMessenger create =
                Marshal.GetDelegateForFunctionPointer<
                    CreateDebugUtilsMessenger>(createProcedure);
            m_Destroy = Marshal.GetDelegateForFunctionPointer<
                DestroyDebugUtilsMessenger>(destroyProcedure);
            VulkanDebugUtilsMessengerCreateInfo createInfo =
                CreateInstanceCreateInfo();
            ulong messenger = 0;
            VkResult result = create(
                instance,
                &createInfo,
                null,
                &messenger);
            if (result != VkResult.Success)
            {
                m_Destroy = null;
                throw new InvalidOperationException(
                    "vkCreateDebugUtilsMessengerEXT failed with " +
                    $"VkResult {result}.");
            }

            m_Instance = instance;
            m_Messenger = messenger;
        }

        /// <summary>
        /// Reads the collected validation result after command, device, and
        /// instance teardown as well as while the messenger is live. Disposal
        /// only releases the native messenger; it must not discard evidence.
        /// </summary>
        internal void ThrowIfErrors(string observedStages)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(observedStages);
            string[] errors = m_Errors.ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    "Vulkan validation reported Error messages during " +
                    observedStages + ":" + Environment.NewLine +
                    string.Join(Environment.NewLine, errors));
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            if (m_Messenger != 0)
            {
                m_Destroy!(m_Instance, m_Messenger, null);
                m_Messenger = 0;
            }
            m_Destroy = null;
            m_Instance = default;
            m_Disposed = true;
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
                VulkanDebugUtilsMessengerCallbackData data =
                    Marshal.PtrToStructure<
                        VulkanDebugUtilsMessengerCallbackData>(
                            callbackData);
                string identifier =
                    Marshal.PtrToStringUTF8(data.MessageIdName) ??
                    "unknown";
                string message =
                    Marshal.PtrToStringUTF8(data.Message) ??
                    "Vulkan validation returned no message text.";
                m_Errors.Enqueue(
                    $"[severity=0x{severity:X}, types=0x{messageTypes:X}, " +
                    $"id={identifier}/{data.MessageIdNumber}] {message}");
            }
            catch (Exception exception)
            {
                m_Errors.Enqueue(
                    "Failed to decode a Vulkan validation Error callback: " +
                    exception.Message);
            }

            return 0;
        }

        private void ThrowIfDisposed() =>
            ObjectDisposedException.ThrowIf(m_Disposed, this);

        [StructLayout(LayoutKind.Sequential)]
        private struct VulkanDebugUtilsMessengerCallbackData
        {
            public uint StructureType;
            public IntPtr Next;
            public uint Flags;
            public IntPtr MessageIdName;
            public int MessageIdNumber;
            public IntPtr Message;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate VkResult CreateDebugUtilsMessenger(
            VkInstance instance,
            VulkanDebugUtilsMessengerCreateInfo* createInfo,
            void* allocator,
            ulong* messenger);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyDebugUtilsMessenger(
            VkInstance instance,
            ulong messenger,
            void* allocator);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint DebugUtilsMessengerCallback(
            uint severity,
            uint messageTypes,
            IntPtr callbackData,
            IntPtr userData);
    }
}

