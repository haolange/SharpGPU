using System;
using Infinity.Core;
using System.Diagnostics;
using Infinity.Collections;
using TerraFX.Interop.Windows;
using TerraFX.Interop.DirectX;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static TerraFX.Interop.Windows.Windows;
using Silk.NET.Core.Native;

namespace Infinity.Graphics
{
#pragma warning disable CS8600, CS8602, CA1416
    internal unsafe class Dx12StorageQueue : RHIStorageQueue
    {
        public Dx12Device Dx12Device
        {
            get
            {
                return m_Dx12Device;
            }
        }

        private Dx12Device m_Dx12Device;

        public Dx12StorageQueue(Dx12Device device)
        {
            m_Dx12Device = device;
        }

        public override RHIStorageFileHandle OpenFile(string absPath)
        {
            throw new NotImplementedException();
        }

        public override void CloseFile(in RHIStorageFileHandle fileHandle)
        {
            throw new NotImplementedException();
        }

        public override void QueryFileInfo(in RHIStorageFileHandle fileHandle)
        {
            throw new NotImplementedException();
        }

        public override void RequestBuffer()
        {
            throw new NotImplementedException();
        }

        public override void RequestTexture()
        {
            throw new NotImplementedException();
        }

        public override void Submit(RHIFence signalFence)
        {
            throw new NotImplementedException();
        }

        protected override void Release()
        {

        }
    }
#pragma warning restore CS8600, CS8602, CA1416
}
