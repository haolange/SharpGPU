using System;
using System.Diagnostics;
using System.Threading;

namespace SharpGPU.Core
{
    public class Disposal : IDisposable
    {
        public bool IsDisposed => Volatile.Read(ref m_DisposeState) != 0;

        private int m_DisposeState;

#if DEBUG
        ~Disposal()
        {
            if (!IsDisposed)
            {
                Trace.TraceError(
                    "Undisposed SharpGPU object detected: {0}. Native ownership is not released from the finalizer.",
                    GetType().FullName ?? GetType().Name);
            }
        }
#endif

        protected virtual void Release()
        {
        }

        protected virtual void ValidateCanDispose()
        {
        }

        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            ValidateCanDispose();
            if (Interlocked.Exchange(ref m_DisposeState, 1) != 0)
            {
                return;
            }

            try
            {
                Release();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }
}
