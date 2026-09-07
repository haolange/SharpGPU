using System;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class Dx12BytecodeOwnershipTests
    {
#if SHARPGPU_ENABLE_DX12
        [Fact]
        public void Dx12Function_BytecodeOwnership_ShouldDeepCopy()
        {
            byte[] sourceBytes = new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14 };
            IntPtr sourcePtr = Marshal.AllocHGlobal(sourceBytes.Length);
            try
            {
                Marshal.Copy(sourceBytes, 0, sourcePtr, sourceBytes.Length);
                RHIFunctionDescriptor descriptor;
                descriptor.Type = ERHIFunctionType.Compute;
                descriptor.ByteSize = (uint)sourceBytes.Length;
                descriptor.ByteCode = sourcePtr;
                descriptor.EntryName = "CSMain";
                descriptor.PayloadKind = ERHIShaderPayloadKind.Dxil;

                Dx12Function function = new Dx12Function(descriptor);
                try
                {
                    RHIFunctionDescriptor ownedDescriptor = function.Descriptor;
                    Assert.NotEqual(sourcePtr, ownedDescriptor.ByteCode);
                    Assert.Equal((uint)sourceBytes.Length, ownedDescriptor.ByteSize);

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Assert.Equal(sourceBytes[i], (byte)Marshal.ReadByte(ownedDescriptor.ByteCode, i));
                    }

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Marshal.WriteByte(sourcePtr, i, 0x00);
                    }

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Assert.Equal(sourceBytes[i], (byte)Marshal.ReadByte(ownedDescriptor.ByteCode, i));
                    }
                }
                finally
                {
                    function.Dispose();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sourcePtr);
            }
        }

        [Fact]
        public void Dx12FunctionLibrary_BytecodeOwnership_ShouldDeepCopy()
        {
            byte[] sourceBytes = new byte[] { 0x21, 0x22, 0x23, 0x24 };
            IntPtr sourcePtr = Marshal.AllocHGlobal(sourceBytes.Length);
            try
            {
                Marshal.Copy(sourceBytes, 0, sourcePtr, sourceBytes.Length);
                RHIFunctionLibraryDescriptor descriptor;
                descriptor.ByteSize = (uint)sourceBytes.Length;
                descriptor.ByteCode = sourcePtr;
                descriptor.PayloadKind = ERHIShaderPayloadKind.Dxil;

                Dx12FunctionLibrary functionLibrary = new Dx12FunctionLibrary(descriptor);
                try
                {
                    RHIFunctionLibraryDescriptor ownedDescriptor = functionLibrary.Descriptor;
                    Assert.NotEqual(sourcePtr, ownedDescriptor.ByteCode);
                    Assert.Equal((uint)sourceBytes.Length, ownedDescriptor.ByteSize);

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Assert.Equal(sourceBytes[i], (byte)Marshal.ReadByte(ownedDescriptor.ByteCode, i));
                    }

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Marshal.WriteByte(sourcePtr, i, 0x00);
                    }

                    for (int i = 0; i < sourceBytes.Length; i++)
                    {
                        Assert.Equal(sourceBytes[i], (byte)Marshal.ReadByte(ownedDescriptor.ByteCode, i));
                    }
                }
                finally
                {
                    functionLibrary.Dispose();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(sourcePtr);
            }
        }
#endif
    }
}
