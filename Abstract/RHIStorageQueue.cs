using System;
using Infinity.Core;
using Infinity.Mathmatics;

namespace Infinity.Graphics
{
    public struct RHIStorageFileHandle 
    { 

    }

    public abstract class RHIStorageQueue : Disposal
    {
        public abstract RHIStorageFileHandle OpenFile(string absPath);
        public abstract void CloseFile(RHIStorageFileHandle fileHandle);
        public abstract void QueryFileInfo(RHIStorageFileHandle fileHandle);
        public abstract void RequestBuffer();
        public abstract void RequestTexture();
        public abstract void Submit(RHIFence signalFence);
    }
}
