using System;
using Infinity.Core;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal class MetalMLPipeline : RHIMLPipeline
    {
        internal MTL4MachineLearningPipelineState NativePipelineState => m_NativePipelineState;

        private MTL4MachineLearningPipelineState m_NativePipelineState;

        public MetalMLPipeline(MetalDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            if (!device.BindingCapabilities.SupportsMetal4)
            {
                throw new NotSupportedException(
                    "MetalMLPipeline requires Metal 4 (MTL4MachineLearningPipelineState). " +
                    "The current device does not support Metal 4.");
            }

            MetalFunction mlFunction = (MetalFunction)descriptor.Function;

            // ── Create MTL4Compiler ──
            NSError compilerError = default;
            MTL4CompilerDescriptor compilerDesc = MTL4CompilerDescriptor.New();
            MTL4Compiler compiler = device.NativeDevice.NewCompiler(compilerDesc, ref compilerError);
            ObjectiveCRuntime.Release(compilerDesc);

            if (compiler.NativePtr == IntPtr.Zero)
            {
                string errorText = compilerError.NativePtr != IntPtr.Zero
                    ? compilerError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4Compiler — {errorText}");
            }

            // ── Build MTL4LibraryFunctionDescriptor from RHIFunction ──
            MTL4LibraryFunctionDescriptor funcDesc = MTL4LibraryFunctionDescriptor.New();
            funcDesc.Library = mlFunction.NativeLibrary;
            funcDesc.Name = new NSString(mlFunction.Descriptor.EntryName);

            // ── Build ML pipeline descriptor ──
            MTL4MachineLearningPipelineDescriptor mlDesc = MTL4MachineLearningPipelineDescriptor.New();
            mlDesc.MachineLearningFunctionDescriptor = funcDesc;
            if (!string.IsNullOrEmpty(descriptor.Name))
            {
                mlDesc.Label = new NSString(descriptor.Name);
            }
            ObjectiveCRuntime.Release(funcDesc);

            // ── Create pipeline state ──
            NSError pipelineError = default;
            m_NativePipelineState = compiler.NewMachineLearningPipelineState(mlDesc, ref pipelineError);
            ObjectiveCRuntime.Release(mlDesc);
            ObjectiveCRuntime.Release(compiler);

            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = pipelineError.NativePtr != IntPtr.Zero
                    ? pipelineError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4MachineLearningPipelineState '{descriptor.Name}' — {errorText}");
            }

            m_IntermediatesHeapSize = m_NativePipelineState.IntermediatesHeapSize;
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }
        }
    }
}
