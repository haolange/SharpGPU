using System;

namespace SharpGPU.Core
{
    public class Disposal : IDisposable
    {
        public bool IsDisposed => m_IsDisposed;

        private bool m_IsDisposed;

        ~Disposal()
        {
            Shutdown();
        }

        protected virtual void Release()
        {
        }

        private void Shutdown()
        {
            if (!m_IsDisposed)
            {
                Release();
            }

            m_IsDisposed = true;
        }

        public void Dispose()
        {
            Shutdown();
            GC.SuppressFinalize(this);
        }
    }
}
