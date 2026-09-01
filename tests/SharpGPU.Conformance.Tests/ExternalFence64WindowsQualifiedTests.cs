#if SHARPGPU_ENABLE_DX12
using System;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests;

public sealed class ExternalFence64WindowsQualifiedTests
{
    [Trait("Category", "SharpGpuWindowsQualified")]
    [Fact]
    public void Dx12ExternalFence64_ExportReimportSameProcess_ClosesCallerHandle()
    {
        bool initialized = FeatureContractContext.TryCreateDx12(
            out FeatureContractContext? context,
            out string reason);
        Assert.True(
            initialized,
            $"DX12 is required for the Windows-qualified ExternalFence64 gate: {reason}");
        Assert.NotNull(context);

        using (context)
        {
            RHIDevice device = context.Device;
            Assert.NotEqual(
                ERHICapabilityTier.Unavailable,
                device.Capabilities.Synchronization.ExternalFence64.Tier);
            Assert.Equal(ERHICapabilityTier.Unavailable, device.Capabilities.MultiGpu.MultiGpu.Tier);
            Assert.Equal(0u, device.Capabilities.MultiGpu.NodeMask);

            using RHIExternalFence64 created = device.CreateExternalFence64(
                new RHIExternalFence64CreateDescriptor(initialValue: 0));
            Assert.Equal(ERHIExternalFence64Direction.Export, created.Direction);
            created.Signal(7);
            Assert.Equal(7UL, created.GetCompletedValue());

            RHIExternalFence64Export exported = device.ExportExternalFence64(created);
            Assert.NotEqual(IntPtr.Zero, exported.Handle);
            Assert.Equal(ERHIExternalHandleKind.Win32NtShared, exported.HandleKind);
            Assert.Equal(7UL, exported.Value);
            Assert.True(exported.Adapter.HasLuid);

            RHIAdapterIdentity mismatch = new(
                exported.Adapter.Luid ^ unchecked((long)0x00FF00FF00FF00FF),
                Guid.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                device.ImportExternalFence64(
                    new RHIExternalFence64ImportDescriptor(
                        exported.Handle,
                        value: 7,
                        ERHIExternalHandleKind.Win32NtShared,
                        mismatch)));

            using RHIExternalFence64 imported = device.ImportExternalFence64(
                new RHIExternalFence64ImportDescriptor(
                    exported.Handle,
                    value: 7,
                    ERHIExternalHandleKind.Win32NtShared,
                    exported.Adapter));
            Assert.Equal(ERHIExternalFence64Direction.Import, imported.Direction);
            Assert.Equal(7UL, imported.GetCompletedValue());

            created.Signal(11);
            Assert.Equal(11UL, imported.GetCompletedValue());

            RHIWin32NtHandle.Close(exported.Handle);
        }
    }
}
#endif
