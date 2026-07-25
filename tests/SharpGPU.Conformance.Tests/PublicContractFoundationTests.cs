using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SharpGPU;
using SharpGPU.Core;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class PublicContractFoundationTests
    {
        [Fact]
        public void Disposal_ShouldReleaseExactlyOnceAndRejectUseAfterDispose()
        {
            CountingDisposal disposal = new CountingDisposal();

            Parallel.For(0, 64, _ => disposal.Dispose());

            Assert.True(disposal.IsDisposed);
            Assert.Equal(1, disposal.ReleaseCount);
            Assert.Throws<ObjectDisposedException>(disposal.EnsureUsable);

            disposal.Dispose();
            Assert.Equal(1, disposal.ReleaseCount);
        }

        [Fact]
        public void ShaderStage_ShouldSeparateSingularValuesFromCombinableMasks()
        {
            Assert.Null(typeof(ERHIShaderStage).GetCustomAttribute<FlagsAttribute>());
            Assert.NotNull(typeof(ERHIShaderStageMask).GetCustomAttribute<FlagsAttribute>());
            Assert.Equal(
                ERHIShaderStageMask.Vertex | ERHIShaderStageMask.Fragment | ERHIShaderStageMask.Task | ERHIShaderStageMask.Mesh,
                ERHIShaderStageMask.AllGraphics);
            Assert.Equal(
                ERHIShaderStageMask.AllGraphics
                    | ERHIShaderStageMask.Compute
                    | ERHIShaderStageMask.RayTracing
                    | ERHIShaderStageMask.MachineLearning,
                ERHIShaderStageMask.All);
        }

        [Fact]
        public void InstanceFactory_ShouldBeNonNullableAndFailClosedForUnknownBackend()
        {
            MethodInfo create = typeof(RHIInstance).GetMethod(
                nameof(RHIInstance.Create),
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("RHIInstance.Create was not found.");
            Assert.Equal(typeof(RHIInstance), create.ReturnType);

            RHIInstanceDescriptor descriptor = new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.Pending
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => RHIInstance.Create(descriptor));
        }

        [Fact]
        public void RhiException_ShouldPreserveTypedNativeFailureFacts()
        {
            RHIException exception = new RHIException(
                ERHIErrorCode.DeviceLost,
                ERHIBackend.DirectX12,
                unchecked((long)0x887A0005),
                "The native device was removed.",
                ERHIDeviceState.Removed);

            Assert.Equal(ERHIErrorCode.DeviceLost, exception.ErrorCode);
            Assert.Equal(ERHIBackend.DirectX12, exception.Backend);
            Assert.Equal(unchecked((long)0x887A0005), exception.NativeCode);
            Assert.Equal("The native device was removed.", exception.NativeMessage);
            Assert.Equal(ERHIDeviceState.Removed, exception.DeviceState);
            Assert.Contains("DirectX12 DeviceLost", exception.Message, StringComparison.Ordinal);
        }

        private sealed class CountingDisposal : Disposal
        {
            public int ReleaseCount => Volatile.Read(ref m_ReleaseCount);

            private int m_ReleaseCount;

            public void EnsureUsable()
            {
                ThrowIfDisposed();
            }

            protected override void Release()
            {
                Interlocked.Increment(ref m_ReleaseCount);
            }
        }
    }
}
