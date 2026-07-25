using System;
using System.Collections.Generic;
using SharpMetal.ObjectiveCCore;

namespace SharpGPU
{
    /// <summary>
    /// Owns only HAL-private Objective-C objects referenced by one encoded
    /// Metal command-buffer recording. The caller's fence discipline makes
    /// the next command-buffer Begin (or Dispose) the reclaim boundary.
    /// </summary>
    internal sealed class MetalNativeTransientBatch : IDisposable
    {
        private readonly Action<IntPtr> m_Release;
        private readonly List<IntPtr> m_NativeObjects;
        private bool m_IsDisposed;

        internal int Count => m_NativeObjects.Count;

        internal MetalNativeTransientBatch()
            : this(static nativeObject =>
                ObjectiveCRuntime.Release(nativeObject))
        {
        }

        internal MetalNativeTransientBatch(Action<IntPtr> release)
        {
            m_Release = release ??
                throw new ArgumentNullException(nameof(release));
            m_NativeObjects = new List<IntPtr>();
        }

        internal void RetainOwnership(IntPtr nativeObject)
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            if (nativeObject == IntPtr.Zero)
            {
                throw new ArgumentException(
                    "A native transient must have a non-zero pointer.",
                    nameof(nativeObject));
            }
            m_NativeObjects.Add(nativeObject);
        }

        internal int CaptureCheckpoint()
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            return m_NativeObjects.Count;
        }

        internal void RollbackTo(int checkpoint)
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            if ((uint)checkpoint > (uint)m_NativeObjects.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(checkpoint));
            }

            for (int i = m_NativeObjects.Count - 1;
                 i >= checkpoint;
                 --i)
            {
                m_Release(m_NativeObjects[i]);
            }
            m_NativeObjects.RemoveRange(
                checkpoint,
                m_NativeObjects.Count - checkpoint);
        }

        internal void ReleaseForCommandBufferReuse()
        {
            ObjectDisposedException.ThrowIf(m_IsDisposed, this);
            RollbackTo(0);
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }
            RollbackTo(0);
            m_IsDisposed = true;
        }
    }
}