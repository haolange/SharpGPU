# SharpGPU QuickStart

This is the smallest opt-in helper use case: create two buffers, open a transfer pass, copy bytes, and let the scoped pass end the encoder.

```csharp
using SharpGPU;
using SharpGPU.Builders;
using SharpGPU.Scopes;

RHIBuffer uploadBuffer = device.CreateBuffer(RHIDescriptorDefaults.Buffer(
    byteSize: 16,
    usage: ERHIBufferUsage.CopySrc,
    storageMode: ERHIStorageMode.HostUpload));

RHIBuffer gpuBuffer = device.CreateBuffer(RHIDescriptorDefaults.Buffer(
    byteSize: 16,
    usage: ERHIBufferUsage.CopyDst | ERHIBufferUsage.ShaderResource));

RHICommandBuffer commandBuffer = graphicsQueue.CreateCommandBuffer();
commandBuffer.Begin();

using (RHITransferPassScope pass = commandBuffer.BeginScopedTransferPass(RHIDescriptorDefaults.TransferPass("upload")))
{
    pass.Encoder.CopyBufferToBuffer(uploadBuffer, 0, gpuBuffer, 0, uploadBuffer.Descriptor.ByteSize);
}

commandBuffer.End();
```

`using SharpGPU;` exposes the low-level HAL. `SharpGPU.Scopes` and `SharpGPU.Builders` are opt-in convenience layers.
