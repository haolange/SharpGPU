using System;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace SharpGPU
{
    #region Fence
internal unsafe class VulkanFence : RHIFence
    {
        public VkFence NativeFence => m_NativeFence;

        public override ERHIFenceStatus Status
        {
            get
            {
                ThrowIfSynchronizationDisposed();
                if (IsSignalKnownComplete)
                {
                    return ERHIFenceStatus.Success;
                }

                if (!IsSignalPending)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VkResult result = VulkanNative.vkGetFenceStatus(m_VulkanDevice.NativeDevice, m_NativeFence);
                if (result == VkResult.Success)
                {
                    MarkSignaled();
                    return ERHIFenceStatus.Success;
                }

                if (result == VkResult.NotReady)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
                return ERHIFenceStatus.Undefined;
            }
        }

        private VulkanDevice m_VulkanDevice;
        private VkFence m_NativeFence;

        public VulkanFence(VulkanDevice device) : base(device)
        {
            m_VulkanDevice = device;

            VkFenceCreateInfo fenceInfo = new VkFenceCreateInfo()
            {
                sType = VkStructureType.FenceCreateInfo,
                flags = 0,
            };

            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateFence(device.NativeDevice, &fenceInfo, null, fencePtr));
            }
        }

        public override void Reset()
        {
            if (IsSignalPending)
            {
                _ = Status;
            }

            if (!BeginReset())
            {
                return;
            }

            try
            {
                fixed (VkFence* fencePtr = &m_NativeFence)
                {
                    VulkanUtility.CheckErrors(VulkanNative.vkResetFences(m_VulkanDevice.NativeDevice, 1, fencePtr));
                }

                CompleteReset();
            }
            catch
            {
                RollbackReset();
                throw;
            }
        }

        public override ERHIFenceStatus Wait(ulong timeoutNanoseconds = ulong.MaxValue)
        {
            EnsureWaitable();
            if (IsSignalKnownComplete)
            {
                return ERHIFenceStatus.Success;
            }

            fixed (VkFence* fencePtr = &m_NativeFence)
            {
                VkResult result = VulkanNative.vkWaitForFences(
                    m_VulkanDevice.NativeDevice,
                    1,
                    fencePtr,
                    true,
                    timeoutNanoseconds);
                if (result == VkResult.Timeout)
                {
                    return ERHIFenceStatus.NotReady;
                }

                VulkanUtility.CheckErrors(result);
            }

            MarkSignaled();
            return ERHIFenceStatus.Success;
        }

        protected override void Release()
        {
            VulkanNative.vkDestroyFence(m_VulkanDevice.NativeDevice, m_NativeFence, null);
        }
    }
    #endregion

    #region Semaphore
internal unsafe class VulkanSemaphore : RHISemaphore
    {
        public VkSemaphore NativeSemaphore => m_NativeSemaphore;

        private VulkanDevice m_VulkanDevice;
        private VkSemaphore m_NativeSemaphore;

        public VulkanSemaphore(VulkanDevice device) : base(device)
        {
            m_VulkanDevice = device;

            VkSemaphoreCreateInfo semaphoreInfo = new VkSemaphoreCreateInfo()
            {
                sType = VkStructureType.SemaphoreCreateInfo,
            };

            fixed (VkSemaphore* semPtr = &m_NativeSemaphore)
            {
                VulkanUtility.CheckErrors(VulkanNative.vkCreateSemaphore(device.NativeDevice, &semaphoreInfo, null, semPtr));
            }
        }

        protected override void Release()
        {
            VulkanNative.vkDestroySemaphore(m_VulkanDevice.NativeDevice, m_NativeSemaphore, null);
        }
    }
    #endregion

    internal unsafe sealed class VulkanExternalFence64 : RHIExternalFence64
    {
        internal RHIAdapterIdentity Adapter => m_Adapter;

        private readonly VulkanDevice m_Device;
        private readonly RHIAdapterIdentity m_Adapter;
        private VkSemaphore m_Semaphore;
        private IntPtr m_OwnedImportHandle;

        private VulkanExternalFence64(
            VulkanDevice device,
            VkSemaphore semaphore,
            ERHIExternalFence64Direction direction,
            in RHIAdapterIdentity adapter,
            IntPtr ownedImportHandle)
            : base(device, direction)
        {
            m_Device = device;
            m_Semaphore = semaphore;
            m_Adapter = adapter;
            m_OwnedImportHandle = ownedImportHandle;
        }

        internal static VulkanExternalFence64 Create(
            VulkanDevice device,
            in RHIExternalFence64CreateDescriptor descriptor)
        {
            VkSemaphore semaphore = CreateTimelineSemaphore(
                device,
                descriptor.InitialValue,
                exportD3D12Fence: true);
            return new VulkanExternalFence64(
                device,
                semaphore,
                ERHIExternalFence64Direction.Export,
                device.AdapterIdentity,
                IntPtr.Zero);
        }

        internal static VulkanExternalFence64 Import(
            VulkanDevice device,
            in RHIExternalFence64ImportDescriptor descriptor)
        {
            if (descriptor.Handle == IntPtr.Zero)
            {
                throw new ArgumentException("Import handle is null.", nameof(descriptor));
            }
            if (descriptor.HandleKind != ERHIExternalHandleKind.Win32NtShared)
            {
                throw new NotSupportedException(
                    $"Vulkan ExternalFence64 only imports Win32 NT shared handles, not {descriptor.HandleKind}.");
            }

            device.AdapterIdentity.RequireMatch(
                descriptor.ExpectedAdapter,
                "Vulkan ExternalFence64 import");
            IntPtr owned = RHIWin32NtHandle.Duplicate(descriptor.Handle);
            VkSemaphore semaphore = default;
            try
            {
                semaphore = CreateTimelineSemaphore(
                    device,
                    initialValue: 0,
                    exportD3D12Fence: false);
                VkImportSemaphoreWin32HandleInfoKHR importInfo = new()
                {
                    sType = VkStructureType.ImportSemaphoreWin32HandleInfoKHR,
                    semaphore = semaphore,
                    handleType = VkExternalSemaphoreHandleTypeFlags.D3D12Fence,
                    handle = owned,
                };
                VulkanUtility.CheckErrors(
                    VulkanExternalFence64Native.ImportSemaphoreWin32Handle(
                        device,
                        &importInfo));
                return new VulkanExternalFence64(
                    device,
                    semaphore,
                    ERHIExternalFence64Direction.Import,
                    device.AdapterIdentity,
                    owned);
            }
            catch
            {
                if (semaphore.Handle != 0)
                {
                    VulkanNative.vkDestroySemaphore(device.NativeDevice, semaphore, null);
                }

                RHIWin32NtHandle.Close(owned);
                throw;
            }
        }

        internal RHIExternalFence64Export Export()
        {
            ThrowIfExternalDisposed();
            VkSemaphoreGetWin32HandleInfoKHR info = new()
            {
                sType = VkStructureType.SemaphoreGetWin32HandleInfoKHR,
                semaphore = m_Semaphore,
                handleType = VkExternalSemaphoreHandleTypeFlags.D3D12Fence,
            };
            IntPtr handle;
            VulkanUtility.CheckErrors(
                VulkanExternalFence64Native.GetSemaphoreWin32Handle(
                    m_Device,
                    &info,
                    &handle));
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    "vkGetSemaphoreWin32HandleKHR returned a null NT handle.");
            }

            return new RHIExternalFence64Export(
                handle,
                GetCompletedValue(),
                ERHIExternalHandleKind.Win32NtShared,
                m_Adapter);
        }

        public override ulong GetCompletedValue()
        {
            ThrowIfExternalDisposed();
            ulong value;
            VulkanUtility.CheckErrors(
                VulkanExternalFence64Native.GetSemaphoreCounterValue(
                    m_Device,
                    m_Semaphore,
                    &value));
            return value;
        }

        public override void Signal(ulong value)
        {
            ThrowIfExternalDisposed();
            VkSemaphoreSignalInfo signalInfo = new()
            {
                sType = VkStructureType.SemaphoreSignalInfo,
                semaphore = m_Semaphore,
                value = value,
            };
            VulkanUtility.CheckErrors(
                VulkanExternalFence64Native.SignalSemaphore(m_Device, &signalInfo));
        }

        protected override void Release()
        {
            if (m_Semaphore.Handle != 0)
            {
                VulkanNative.vkDestroySemaphore(m_Device.NativeDevice, m_Semaphore, null);
                m_Semaphore = default;
            }

            if (m_OwnedImportHandle != IntPtr.Zero)
            {
                RHIWin32NtHandle.Close(m_OwnedImportHandle);
                m_OwnedImportHandle = IntPtr.Zero;
            }
        }

        private static VkSemaphore CreateTimelineSemaphore(
            VulkanDevice device,
            ulong initialValue,
            bool exportD3D12Fence)
        {
            VkSemaphoreTypeCreateInfo typeInfo = new()
            {
                sType = VkStructureType.SemaphoreTypeCreateInfo,
                semaphoreType = VkSemaphoreType.Timeline,
                initialValue = initialValue,
            };
            VkExportSemaphoreCreateInfo exportInfo = new()
            {
                sType = VkStructureType.ExportSemaphoreCreateInfo,
                handleTypes = VkExternalSemaphoreHandleTypeFlags.D3D12Fence,
                pNext = &typeInfo,
            };
            VkSemaphoreCreateInfo createInfo = new()
            {
                sType = VkStructureType.SemaphoreCreateInfo,
                pNext = exportD3D12Fence ? &exportInfo : &typeInfo,
            };
            VkSemaphore semaphore;
            VulkanUtility.CheckErrors(
                VulkanNative.vkCreateSemaphore(
                    device.NativeDevice,
                    &createInfo,
                    null,
                    &semaphore));
            return semaphore;
        }
    }

    internal static unsafe class VulkanExternalFence64Native
    {
        internal static VkResult ImportSemaphoreWin32Handle(
            VulkanDevice device,
            VkImportSemaphoreWin32HandleInfoKHR* importInfo)
        {
            return Load<PFN_vkImportSemaphoreWin32HandleKHR>(
                device,
                "vkImportSemaphoreWin32HandleKHR")(
                device.NativeDevice,
                importInfo);
        }

        internal static VkResult GetSemaphoreWin32Handle(
            VulkanDevice device,
            VkSemaphoreGetWin32HandleInfoKHR* info,
            IntPtr* handle)
        {
            return Load<PFN_vkGetSemaphoreWin32HandleKHR>(
                device,
                "vkGetSemaphoreWin32HandleKHR")(
                device.NativeDevice,
                info,
                handle);
        }

        internal static VkResult GetSemaphoreCounterValue(
            VulkanDevice device,
            VkSemaphore semaphore,
            ulong* value)
        {
            IntPtr procedure = device.VulkanInstance.TryGetInstanceProcedure(
                "vkGetSemaphoreCounterValue");
            if (procedure == IntPtr.Zero)
            {
                procedure = device.VulkanInstance.TryGetInstanceProcedure(
                    "vkGetSemaphoreCounterValueKHR");
            }
            if (procedure == IntPtr.Zero)
            {
                throw new NotSupportedException(
                    "vkGetSemaphoreCounterValue is unavailable.");
            }

            return Marshal.GetDelegateForFunctionPointer<PFN_vkGetSemaphoreCounterValue>(
                procedure)(device.NativeDevice, semaphore, value);
        }

        internal static VkResult SignalSemaphore(
            VulkanDevice device,
            VkSemaphoreSignalInfo* signalInfo)
        {
            IntPtr procedure = device.VulkanInstance.TryGetInstanceProcedure(
                "vkSignalSemaphore");
            if (procedure == IntPtr.Zero)
            {
                procedure = device.VulkanInstance.TryGetInstanceProcedure(
                    "vkSignalSemaphoreKHR");
            }
            if (procedure == IntPtr.Zero)
            {
                throw new NotSupportedException("vkSignalSemaphore is unavailable.");
            }

            return Marshal.GetDelegateForFunctionPointer<PFN_vkSignalSemaphore>(
                procedure)(device.NativeDevice, signalInfo);
        }

        private static T Load<T>(VulkanDevice device, string name)
            where T : Delegate
        {
            IntPtr procedure = device.VulkanInstance.TryGetInstanceProcedure(name);
            if (procedure == IntPtr.Zero)
            {
                throw new NotSupportedException($"Vulkan function '{name}' is unavailable.");
            }

            return Marshal.GetDelegateForFunctionPointer<T>(procedure);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate VkResult PFN_vkImportSemaphoreWin32HandleKHR(
            VkDevice device,
            VkImportSemaphoreWin32HandleInfoKHR* importInfo);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate VkResult PFN_vkGetSemaphoreWin32HandleKHR(
            VkDevice device,
            VkSemaphoreGetWin32HandleInfoKHR* info,
            IntPtr* handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate VkResult PFN_vkGetSemaphoreCounterValue(
            VkDevice device,
            VkSemaphore semaphore,
            ulong* value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate VkResult PFN_vkSignalSemaphore(
            VkDevice device,
            VkSemaphoreSignalInfo* signalInfo);
    }
}
