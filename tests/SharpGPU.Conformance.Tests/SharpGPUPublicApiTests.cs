using System;
using System.Linq;
using SharpGPU;
using SharpGPU.Builders;
using SharpGPU.Scopes;
#if SHARPGPU_ENABLE_DX12
using Vortice.Direct3D12;
#endif
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class SharpGPUPublicApiTests
    {
        [Fact]
        public void SharpGPUAssembly_PublicTypesDoNotUseOldNamespace()
        {
            string[] leakedTypes = typeof(RHIDevice).Assembly
                .GetExportedTypes()
                .Select(type => type.FullName ?? type.Name)
                .Where(name => name.StartsWith("Infinity.Graphics", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(leakedTypes);
        }

        [Fact]
        public void QuickStart_BufferCopySample_CompilesAndRunsAgainstRhiContracts()
        {
            using FakeBuffer uploadBuffer = new FakeBuffer(RHIDescriptorDefaults.Buffer(
                byteSize: 16,
                usage: ERHIBufferUsage.CopySrc,
                storageMode: ERHIStorageMode.HostUpload));
            using FakeBuffer gpuBuffer = new FakeBuffer(RHIDescriptorDefaults.Buffer(
                byteSize: 16,
                usage: ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource));
            using FakeCommandBuffer commandBuffer = new FakeCommandBuffer();

            commandBuffer.Begin("quickstart upload");
            using (RHITransferPassScope pass = commandBuffer.BeginScopedTransferPass(RHIDescriptorDefaults.TransferPass("quickstart upload")))
            {
                pass.Encoder.CopyBufferToBuffer(uploadBuffer, 0, gpuBuffer, 0, uploadBuffer.Descriptor.ByteSize);
            }
            commandBuffer.End();

            Assert.True(commandBuffer.TransferEncoder.Ended);
            Assert.Equal(16, commandBuffer.TransferEncoder.CopiedBytes);
        }

#if SHARPGPU_ENABLE_DX12
        [Fact]
        public void VendoredVortice_WorkGraphBindingsExposeStateObjectAndDispatchPayload()
        {
            WorkGraphDescription workGraph = new WorkGraphDescription
            {
                ProgramName = "main",
                Flags = WorkGraphFlags.IncludeAllAvailableNodes
            };

            StateSubObject subObject = new StateSubObject(workGraph);
            DispatchGraphDescription dispatch = new DispatchGraphDescription
            {
                Mode = DispatchMode.NodeGpuInput
            };
            dispatch.NodeGPUInput = 0x1000;

            Assert.Equal(StateSubObjectType.WorkGraph, subObject.Type);
            Assert.Equal(DispatchMode.NodeGpuInput, dispatch.Mode);
            Assert.Equal(0x1000UL, dispatch.NodeGPUInput);
        }
#endif

        private sealed class FakeCommandBuffer : RHICommandBuffer
        {
            public FakeTransferEncoder TransferEncoder { get; }

            public FakeCommandBuffer()
            {
                TransferEncoder = new FakeTransferEncoder(this);
            }

            public override void Begin(string name)
            {
                ValidateCanBegin();
                MarkBeginSucceeded();
            }

            public override void End()
            {
                ValidateCanEnd();
                MarkEndSucceeded();
            }

            public override RHITransferEncoder BeginTransferPass(in RHITransferPassDescriptor descriptor)
            {
                ValidateCanBeginEncoder(ERHICommandEncoderKind.Transfer);
                TransferEncoder.BeginPass(descriptor);
                MarkEncoderBeginSucceeded(ERHICommandEncoderKind.Transfer);
                return TransferEncoder;
            }
            public override void EndTransferPass() => TransferEncoder.EndPass();
            public override RHIComputeEncoder BeginComputePass(in RHIComputePassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndComputePass() => throw new NotSupportedException();
            public override RHIRaytracingEncoder BeginRaytracingPass(in RHIRayTracingPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndRaytracingPass() => throw new NotSupportedException();
            public override RHIRasterEncoder BeginRasterPass(in RHIRasterPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndRasterPass() => throw new NotSupportedException();
            public override RHIMLEncoder BeginMLPass(in RHIMLPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndMLPass() => throw new NotSupportedException();
            public override RHIWorkGraphEncoder BeginWorkGraphPass(in RHIWorkGraphPassDescriptor descriptor) => throw new NotSupportedException();
            public override void EndWorkGraphPass() => throw new NotSupportedException();
            public override RHITransferEncoder GetTransferEncoder() => TransferEncoder;
            public override RHIComputeEncoder GetComputeEncoder() => throw new NotSupportedException();
            public override RHIRaytracingEncoder GetRaytracingEncoder() => throw new NotSupportedException();
            public override RHIRasterEncoder GetRasterEncoder() => throw new NotSupportedException();
            public override RHIMLEncoder GetMLEncoder() => throw new NotSupportedException();
            public override RHIWorkGraphEncoder GetWorkGraphEncoder() => throw new NotSupportedException();
            protected override void Release() => TransferEncoder.Dispose();
        }

        private sealed class FakeTransferEncoder : RHITransferEncoder
        {
            public int CopiedBytes { get; private set; }
            public bool Ended { get; private set; }

            public FakeTransferEncoder(RHICommandBuffer commandBuffer)
            {
                m_CommandBuffer = commandBuffer;
            }

            internal override void BeginPass(in RHITransferPassDescriptor descriptor) { }
            public override void Barrier(in RHIBarrier barrier) { }
            public override void Barriers(ReadOnlySpan<RHIBarrier> barriers) { }
            public override void PushDebugGroup(string name) { }
            public override void PopDebugGroup() { }
            public override void WriteTimestamp(in uint index) { }
            public override void ResolveQuery(RHIQuery query, in uint startIndex, in uint queriesCount) { }
            public override void CopyBufferToBuffer(RHIBuffer srcBuffer, in int srcOffset, RHIBuffer dstBuffer, in int dstOffset, in int size) => CopiedBytes += size;
            public override void CopyBufferToTexture(in RHIBufferCopyDescriptor src, in RHITextureCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void CopyTextureToBuffer(in RHITextureCopyDescriptor src, in RHIBufferCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void CopyTextureToTexture(in RHITextureCopyDescriptor src, in RHITextureCopyDescriptor dst, in SharpMath.int3 size) => throw new NotSupportedException();
            public override void EndPass()
            {
                RHICommandBuffer commandBuffer = m_CommandBuffer ??
                    throw new InvalidOperationException("The transfer encoder is not attached to a command buffer.");
                commandBuffer.ValidateEncoderEndFromEncoder(ERHICommandEncoderKind.Transfer);
                commandBuffer.MarkEncoderEndFromEncoder();
                Ended = true;
            }
            protected override void Release() { }
        }

        private sealed class FakeBuffer : RHIBuffer
        {
            public FakeBuffer(in RHIBufferDescriptor descriptor)
            {
                m_Descriptor = descriptor;
            }

            public override IntPtr Map(in uint readBegin, in uint readEnd) => IntPtr.Zero;
            public override void UnMap(in uint writeBegin, in uint writeEnd) { }
            public override RHIBufferView CreateBufferView(in RHIBufferViewDescriptor descriptor) => throw new NotSupportedException();
            protected override void Release() { }
        }
    }
}
