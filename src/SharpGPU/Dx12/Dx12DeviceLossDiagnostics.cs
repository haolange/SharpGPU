using System;
using System.Text;
using Vortice.Direct3D12;

namespace SharpGPU
{
#pragma warning disable CA1416
    internal static class Dx12DeviceLossDiagnostics
    {
        internal static void Configure(bool forceEnable)
        {
            if (!forceEnable)
            {
                return;
            }

            SharpGen.Runtime.Result result =
                D3D12.D3D12GetDebugInterface(
                    out ID3D12DeviceRemovedExtendedDataSettings1? settings);
            if (result.Failure || settings == null)
            {
                return;
            }

            try
            {
                settings.SetAutoBreadcrumbsEnablement(
                    DredEnablement.ForcedOn);
                settings.SetPageFaultEnablement(
                    DredEnablement.ForcedOn);
                settings.SetBreadcrumbContextEnablement(
                    DredEnablement.ForcedOn);
            }
            finally
            {
                settings.Dispose();
            }
        }

        internal static RHIException Capture(
            Dx12Device device,
            int triggeringCode,
            string operation)
        {
            ArgumentNullException.ThrowIfNull(device);
            SharpGen.Runtime.Result removedReason =
                device.NativeDevice.DeviceRemovedReason;
            int nativeCode = removedReason.Failure
                ? removedReason.Code
                : triggeringCode;
            ERHIDeviceState state = nativeCode switch
            {
                DxgiErrorDeviceRemoved =>
                    ERHIDeviceState.Removed,
                DxgiErrorDeviceReset =>
                    ERHIDeviceState.Reset,
                _ => ERHIDeviceState.Lost
            };

            StringBuilder message = new StringBuilder(512);
            message.Append(operation);
            message.Append(" reported device loss. ");
            message.Append("DeviceRemovedReason=0x");
            message.Append(
                unchecked((uint)nativeCode).ToString("X8"));

            ID3D12DeviceRemovedExtendedData1? dred =
                device.NativeDevice.QueryInterfaceOrNull<
                    ID3D12DeviceRemovedExtendedData1>();
            if (dred != null)
            {
                try
                {
                    AppendBreadcrumbs(dred, message);
                    AppendPageFault(dred, message);
                }
                catch (Exception exception)
                {
                    message.Append("; DRED capture failed: ");
                    message.Append(exception.GetType().Name);
                }
                finally
                {
                    dred.Dispose();
                }
            }

            return new RHIException(
                ERHIErrorCode.DeviceLost,
                ERHIBackend.DirectX12,
                unchecked((uint)nativeCode),
                message.ToString(),
                state);
        }

        private static void AppendBreadcrumbs(
            ID3D12DeviceRemovedExtendedData1 dred,
            StringBuilder message)
        {
            if (dred.GetAutoBreadcrumbsOutput1(
                    out DredAutoBreadcrumbsOutput1? output).Failure ||
                output == null)
            {
                return;
            }

            AutoBreadcrumbNode1? node =
                output.HeadAutoBreadcrumbNode;
            int count = 0;
            while (node != null && count < MaximumReportedNodes)
            {
                int last = node.LastBreadcrumbValue ?? 0;
                if (last > 0)
                {
                    message.Append("; breadcrumb[");
                    message.Append(count);
                    message.Append("] queue='");
                    message.Append(
                        node.CommandQueueDebugName ?? "<unnamed>");
                    message.Append("' list='");
                    message.Append(
                        node.CommandListDebugName ?? "<unnamed>");
                    message.Append("' progress=");
                    message.Append(last);
                    message.Append('/');
                    message.Append(node.BreadcrumbCount);
                }
                node = node.Next;
                count++;
            }
        }

        private static void AppendPageFault(
            ID3D12DeviceRemovedExtendedData1 dred,
            StringBuilder message)
        {
            if (dred.GetPageFaultAllocationOutput1(
                    out DredPageFaultOutput1? output).Failure ||
                output == null ||
                output.PageFaultVA == 0)
            {
                return;
            }

            message.Append("; pageFaultVA=0x");
            message.Append(output.PageFaultVA.ToString("X"));
            DredAllocationNode1? allocation =
                output.HeadExistingAllocationNode ??
                output.HeadRecentFreedAllocationNode;
            if (allocation != null)
            {
                message.Append(" allocation='");
                message.Append(
                    allocation.ObjectName ?? "<unnamed>");
                message.Append("' type=");
                message.Append(allocation.AllocationType);
            }
        }

        private const int MaximumReportedNodes = 16;
        private const int DxgiErrorDeviceRemoved =
            unchecked((int)0x887A0005);
        private const int DxgiErrorDeviceReset =
            unchecked((int)0x887A0007);
    }
#pragma warning restore CA1416
}
