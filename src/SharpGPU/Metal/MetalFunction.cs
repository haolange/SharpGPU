using System;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using SharpMetal.Metal;

namespace Infinity.Graphics
{
    internal sealed class MetalFunction : RHIFunction
    {
        public MTLLibrary NativeLibrary => m_NativeLibrary;
        public MTLFunction NativeFunction => m_NativeFunction;

        private MTLLibrary m_NativeLibrary;
        private MTLFunction m_NativeFunction;

        public MetalFunction(MetalDevice device, in RHIFunctionDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            string source = DecodeSource(descriptor);
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidOperationException("Metal function payload is empty.");
            }

            NSString sourceString = new NSString(source);
            MTLCompileOptions options = MTLCompileOptions.New();
            options.FastMathEnabled = true;

            NSError error = default;
            m_NativeLibrary = device.NativeDevice.NewLibrary(sourceString, options, ref error);
            if (m_NativeLibrary.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to compile MSL library: {errorText}");
            }

            NSString entryName = new NSString(descriptor.EntryName);
            m_NativeFunction = m_NativeLibrary.NewFunction(entryName);
            if (m_NativeFunction.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load function entry '{descriptor.EntryName}'.");
            }
        }

        private static string DecodeSource(in RHIFunctionDescriptor descriptor)
        {
            if (descriptor.ByteCode == IntPtr.Zero || descriptor.ByteSize == 0)
            {
                return string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MslSource || descriptor.PayloadKind == ERHIShaderPayloadKind.Pending)
            {
                return Marshal.PtrToStringUTF8(descriptor.ByteCode, (int)descriptor.ByteSize) ?? string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MetalLibrary)
            {
                throw new NotSupportedException("Metal library binary payload is not wired yet. Use MslSource payload for now.");
            }

            throw new NotSupportedException($"Unsupported shader payload for Metal function: {descriptor.PayloadKind}");
        }

        protected override void Release()
        {
            if (m_NativeFunction.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeFunction);
                m_NativeFunction = default;
            }

            if (m_NativeLibrary.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeLibrary);
                m_NativeLibrary = default;
            }
        }
    }

    internal sealed class MetalFunctionTable : RHIFunctionTable
    {
        public override void SetRayGenerationProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override int AddMissProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override int AddHitGroupProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void SetMissProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void SetHitGroupProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void ClearMissPrograms()
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void ClearHitGroupPrograms()
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        public override void Update(RHIRaytracingPipeline pipeline)
        {
            throw new NotSupportedException("Ray tracing is not implemented in Metal backend yet.");
        }

        protected override void Release()
        {
        }
    }
}
