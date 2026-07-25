using System;
using System.Threading;

namespace SharpGPU
{
    internal static class VulkanRasterStrategyDiagnostics
    {
        private static readonly AsyncLocal<EVulkanRasterPassForcedStrategy>
            s_ForcedStrategy = new();

        internal static EVulkanRasterPassForcedStrategy ForcedStrategy =>
            s_ForcedStrategy.Value;

        internal static IDisposable Push(
            EVulkanRasterPassForcedStrategy strategy)
        {
            if (strategy == EVulkanRasterPassForcedStrategy.Auto)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(strategy),
                    "A diagnostic scope must force a concrete Vulkan raster strategy.");
            }

            EVulkanRasterPassForcedStrategy previous =
                s_ForcedStrategy.Value;
            s_ForcedStrategy.Value = strategy;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly EVulkanRasterPassForcedStrategy m_Previous;
            private bool m_Disposed;

            internal Scope(
                EVulkanRasterPassForcedStrategy previous)
            {
                m_Previous = previous;
            }

            public void Dispose()
            {
                if (m_Disposed)
                {
                    return;
                }

                s_ForcedStrategy.Value = m_Previous;
                m_Disposed = true;
            }
        }
    }
}
