# SharpGPU
Hardware Abstract Layer for modern gpus based on Metal/Vulkan/DirectX12 backend.

## Basic Example

### Shader context
That is the example shader source

```c++
std::string computeHLSL
{@"
[[vk::binding(0, 0)]]
RWTexture2D<float4> _ResultTexture[1] : register(u0, space0);

[numthreads(8, 8, 1)]
void CSMain (uint3 id : SV_DispatchThreadID)
{
    float2 UV = (id.xy + 0.5) / float2(1600, 900);
    float IDMod7 = saturate(((id.x & 7) / 7) + ((id.y & 7) / 7));
    _ResultTexture[0][id.xy] = float4(id.x & id.y, IDMod7, UV);
}"};

std::string rasterHLSL
{@"
[[vk::binding(0, 0)]]
Texture2D _DiffuseTexture[1] : register(t0, space0);

[[vk::binding(1, 0)]]
SamplerState _DiffuseSampler[1] : register(s1, space0);

struct Attributes
{
    [[vk::location(0)]]
    float4 color : COLOR1;
    [[vk::location(1)]]
    float4 vertexOS : POSITION0;
};

struct Varyings
{
    [[vk::location(0)]]
    float2 uv0 : TEXCOORD0;
    [[vk::location(1)]]
    float4 color : COLOR1;
};

Varyings VSMain(Attributes input, out float4 vertexCS : SV_POSITION)
{
    vertexCS = input.vertexOS;
    Varyings output = (Varyings)0;
    output.uv0 = input.vertexOS.xy;
    output.color = input.color;
    return output;
}

float4 FSMain(Varyings input) : SV_TARGET
{
    return input.color * _DiffuseTexture[0].Sample(_DiffuseSampler[0], input.uv0);
}"};
```



### Creating the Direct3D_12 Instance //Or Metal/Vulkan/OpenGL/Direct3D 11
The first step after we include the RHI headers and libraries into your project is to create a instance wrapper.

```c++
#include <rhi/d3d12.h>
...
rhi::RHIInstanceDescriptor descriptor;
{
    descriptor.Backend = ERHIBackend::DirectX12;
    descriptor.EnableDebugLayer = false;
    descriptor.EnableValidatior = false;
}
rhi::RHIInstance* instance = rhi::CreateInstance(descriptor);
```



### Get the Device
That select device form instance use index

```c++
#include <rhi/d3d12.h>
...
rhi::RHIDevice* device = instance->GetDevice(0);
```



### Get the CommandQueue
Create command queue for execution gpu task

```c++
#include <rhi/d3d12.h>
...
uint32 computeQueueCount = device->QueryQueueCount(ERHIPipelineType.Compute);
uint32 transferQueueCount = device->QueryQueueCount(ERHIPipelineType.Transfer);
uint32 graphicsQueueCount = device->QueryQueueCount(ERHIPipelineType.Graphics);

rhi::RHICommandQueue* computeQueue = device->GetCommandQueue(ERHIPipelineType.Compute, 0);
rhi::RHICommandQueue* transferQueue = device->GetCommandQueue(ERHIPipelineType.Transfer, 0);
rhi::RHICommandQueue* graphicsQueue = device->GetCommandQueue(ERHIPipelineType.Graphics, 0);
```



### Creating the Fence
Create frame fence for render loop sync

```c++
#include <rhi/d3d12.h>
...
rhi::RHIFence* fence = device->CreateFence();
```



### Creating the SwapChain
Create swap chain for window display

```c++
#include <rhi/d3d12.h>
...
rhi::RHISwapChainDescriptor swapChainDescriptor;
{
    swapChainDescriptor.FPS = 60;
    swapChainDescriptor.Count = 3;
    swapChainDescriptor.Extent = screenSize;
    swapChainDescriptor.Format = ERHISwapChainFormat::R8G8B8A8_UNorm;
    swapChainDescriptor.Surface = hwdnPtr;
    swapChainDescriptor.PresentMode = ERHIPresentMode.VSync;
    swapChainDescriptor.PresentQueue = graphicsQueue;
    swapChainDescriptor.FrameBufferOnly = true;
}
rhi::SwapChain* swapChain = device->CreateSwapChain(swapChainDescriptor);
```



### Creating the texture
Create a texture and sampler

```c++
#include <rhi/d3d12.h>
...
rhi::RHISamplerDescriptor samplerInfo;
{
    samplerInfo.LodMin = -1000;
    samplerInfo.LodMax = 1000;
    samplerInfo.MipLODBias = 0;
    samplerInfo.Anisotropy = 8;
    samplerInfo.MinFilter = ERHIFilterMode::Linear;
    samplerInfo.MagFilter = ERHIFilterMode::Linear;
    samplerInfo.MipFilter = ERHIFilterMode::Linear;
    samplerInfo.AddressModeU = ERHIAddressMode::Repeat;
    samplerInfo.AddressModeV = ERHIAddressMode::Repeat;
    samplerInfo.AddressModeW = ERHIAddressMode::Repeat;
    samplerInfo.ComparisonMode = ERHIComparisonMode::Never;
}
rhi::RHISampler* sampler = device->CreateSampler(samplerInfo);

rhi::RHITextureDescriptor textureInfo;
{
    textureInfo.Extent = uint3(screenSize.xy, 1);
    textureInfo.MipCount = 1;
    textureInfo.SampleCount = ERHISampleCount::None;
    textureInfo.Format = ERHIPixelFormat::R8G8B8A8_UNorm;
    textureInfo.UsageFlag = ERHITextureUsage::ShaderResource | ERHITextureUsage::UnorderedAccess;
    textureInfo.Dimension = ERHITextureDimension::Texture2D;
    textureInfo.StorageMode = ERHIStorageMode::GPULocal;
}
rhi::RHITexture* texture = device->CreateTexture(textureInfo);
```



### Creating the texture view
Create textureView for compute or raster

```c++
#include <rhi/d3d12.h>
...
rhi::RHITextureViewDescriptor computeOutputViewInfo;
{
    computeOutputViewInfo.MipCount = 1;
    computeOutputViewInfo.BaseMipLevel = 0;
    computeOutputViewInfo.ArrayCount = 1;
    computeOutputViewInfo.BaseArraySlice = 0;
    computeOutputViewInfo.ViewType = ERHITextureViewType::UnorderedAccess;
}
rhi::TextureView* textureUAV = texture->CreateTextureView(computeOutputViewInfo);

rhi::RHITextureViewDescriptor rasterInputViewInfo;
{
    rasterInputViewInfo.MipCount = 1;
    rasterInputViewInfo.BaseMipLevel = 0;
    rasterInputViewInfo.ArrayCount = 1;
    rasterInputViewInfo.BaseArraySlice = 0;
    rasterInputViewInfo.ViewType = ERHITextureViewType::ShaderResource;
}
rhi::TextureView* textureSRV = texture->CreateTextureView(rasterInputViewInfo);
```



### Creating the Compute resource table
Create a compute resourceTable

```c++
#include <rhi/d3d12.h>
...
rhi::RHIResourceTableLayoutElement computeResourceTableLayoutElements[1];
{
    computeResourceTableLayoutElements[0].Slot = 0;
    computeResourceTableLayoutElements[0].Type = ERHIBindType::StorageTexture2D;
    computeResourceTableLayoutElements[0].Stage = ERHIShaderStage::Compute;
}

rhi::RHIResourceTableLayoutDescriptor computeResourceTableLayoutInfo;
{
    computeResourceTableLayoutInfo.Index = 0;
    computeResourceTableLayoutInfo.Elements = computeResourceTableLayoutElements;
    computeResourceTableLayoutInfo.NumElements = 1;
}
rhi::RHIResourceTableLayout* computeResourceTableLayout = device->CreateResourceTableLayout(computeResourceTableLayoutInfo);

rhi::RHIResourceTableElement computeResourceTableElements[1];
{
    computeResourceTableElements[0].TextureView = textureUAV;
}

rhi::RHIResourceTableDescriptor computeResourceTableInfo;
{
    computeResourceTableInfo.Layout = computeResourceTableLayout;
    computeResourceTableInfo.Elements = computeResourceTableElements;
    computeResourceTableInfo.NumElements = 1;
}
rhi::RHIResourceTable* computeResourceTable = device->CreateResourceTable(computeResourceTableInfo);
```



### Creating the Compute Pass
Create a compute pipelineState for compute pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIFunctionDescriptor computeFunctionInfo;
{
    computeFunctionInfo.Type = ERHIFunctionType::Compute;
    computeFunctionInfo.ByteSize = computeBlob.Size;
    computeFunctionInfo.ByteCode = computeBlob.Data;
    computeFunctionInfo.EntryName = "CSMain";
}
rhi::RHIFunction* computeFunction = device->CreateFunction(computeFunctionInfo);

rhi::RHIPipelineLayoutDescriptor computePipelienLayoutInfo;
{
    computePipelienLayoutInfo.bLocalSignature = false;
    computePipelienLayoutInfo.bUseVertexLayout = false;
    computePipelienLayoutInfo.StaticSamplers = nullptr;
    computePipelienLayoutInfo.NumStaticSamplers = 0;
    computePipelienLayoutInfo.ResourceTableLayouts = computeResourceTableLayout;
    computePipelienLayoutInfo.NumResourceTableLayouts = 1;
}
rhi::RHIPipelineLayout* computePipelineLayout = device->CreatePipelineLayout(computePipelienLayoutInfo);

rhi::RHIComputePipelineDescriptor computePipelineInfo;
{
    computePipelineInfo.ThreadSize = uint3(8, 8, 1);
    computePipelineInfo.ComputeFunction = computeFunction;
    computePipelineInfo.PipelineLayout = computePipelineLayout;
}
rhi::RHIPipeline* computePipeline = device->CreateComputePipeline(computePipelineInfo);
```



### Creating the UniformBuffer
Create a uniform buffer for any pipelineState to read constant data

```c++
#include <rhi/d3d12.h>
...
UniformInfo uniformArray[1] = // ......;

rhi::RHIBufferDescriptor uniformBufferInfo;
{
    uniformBufferInfo.ByteSize = uniformArray.Length * ((sizeof(UniformInfo) + 255) & ~255);
    uniformBufferInfo.UsageFlag = ERHIBufferUsage::UniformBuffer;
    uniformBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
}
rhi::RHIBuffer* uniformBuffer = device->CreateBuffer(uniformBufferInfo);

void* uniformData = uniformBuffer->Map(0, uniformBufferInfo.ByteSize);
MemoryUtility::MemCpy(&uniformArray, uniformData, uniformBufferInfo.ByteSize);
rhiIndexBuffer->Unmap(0, uniformBufferInfo.ByteSize);

rhi::RHIBufferViewDescriptor uniformBufferViewInfo;
{
    uniformBufferViewInfo.Offset = 0;
    uniformBufferViewInfo.Type = ERHIBufferViewType.UniformBuffer;
    uniformBufferViewInfo.Count = uniformArray.Length;
    uniformBufferViewInfo.Stride = (sizeof(UniformInfo) + 255) & ~255;
}
rhi::RHIBufferView* uniformBufferView = uniformBuffer->CreateBufferView(bufferViewDescriptor);
```



### Creating the Vertex Stream
Create indexBuffer and vertexBuffer

```c++
#include <rhi/d3d12.h>
...
//index buffer
uint16 indices[3] = {0, 1, 2};

rhi::RHIBufferDescriptor indexBufferInfo;
{
    indexBufferInfo.ByteSize = indices.Length * sizeof(uint16);
    indexBufferInfo.UsageFlag = ERHIBufferUsage::IndexBuffer;
    indexBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
}
rhi::RHIBuffer* indexBufferCPU = device->CreateBuffer(indexBufferInfo);

void* indexData = indexBufferCPU->Map(0, indexBufferInfo.ByteSize);
MemoryUtility::MemCpy(&indices, indexData, indexBufferInfo.ByteSize);
indexBufferCPU->Unmap(0, indexBufferInfo.ByteSize);

indexBufferInfo.StorageMode = ERHIStorageMode.GPULocal;
rhi::RHIBuffer* indexBufferGPU = device->CreateBuffer(indexBufferInfo);

//vertex buffer
Vertex vertices[3];
{
    vertices[0].color = float4(1, 0, 0, 1);
    vertices[0].position = float4(-0.5f, -0.5f, 0, 1);
    vertices[1].color = float4(0, 1, 0, 1);
    vertices[1].position = float4(0, 0.5f, 0, 1);
    vertices[2].color = float4(0, 0, 1, 1);
    vertices[2].position = float4(0.5f, -0.5f, 0, 1);
}

rhi::RHIBufferDescriptor vertexBufferInfo;
{
    vertexBufferInfo.ByteSize = vertices.Length * sizeof(Vertex);
    vertexBufferInfo.UsageFlag = ERHIBufferUsage::VertexBuffer;
    vertexBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
}
rhi::RHIBuffer* vertexBufferCPU = device->CreateBuffer(vertexBufferInfo);

void* vertexData = vertexBufferCPU->Map(vertexBufferInfo.ByteSize, 0);
MemoryUtility::MemCpy(&vertices, vertexData, vertexBufferInfo.ByteSize);
vertexBufferCPU->Unmap();

vertexBufferInfo.StorageMode = ERHIStorageMode.GPULocal;
rhi::RHIBuffer* vertexBufferGPU = device->CreateBuffer(vertexBufferInfo);
```



### Creating the Raster resource table
Create a raster resourceTable

```c++
#include <rhi/d3d12.h>
...
rhi::RHIResourceTableLayoutElement rasterResourceTableLayoutElements[2];
{
    rasterResourceTableLayoutElements[0].Slot = 0;
    rasterResourceTableLayoutElements[0].Type = ERHIBindType::Texture2D;
    rasterResourceTableLayoutElements[0].Stage = ERHIShaderStage::Fragment;
    rasterResourceTableLayoutElements[1].Slot = 1;
    rasterResourceTableLayoutElements[1].Type = ERHIBindType::Sampler;
    rasterResourceTableLayoutElements[1].Stage = ERHIShaderStage::Fragment;
}

rhi::RHIResourceTableLayoutDescriptor rasterResourceTableLayoutInfo;
{
    rasterResourceTableLayoutInfo.Index = 0;
    rasterResourceTableLayoutInfo.Elements = &rasterResourceTableLayoutElements;
    rasterResourceTableLayoutInfo.NumElements = 2;
}
rhi::RHIResourceTableLayout* rasterResourceTableLayout = device->CreateResourceTableLayout(rasterResourceTableLayoutInfo);

rhi::RHIResourceTableElement rasterResourceTableElements[2];
{
    rasterResourceTableElements[0].TextureView = textureSRV;
    rasterResourceTableElements[1].Sampler = sampler;
}

rhi::RHIResourceTableDescriptor rasterResourceTableInfo;
{
    rasterResourceTableInfo.Layout = rasterResourceTableLayout;
    rasterResourceTableInfo.Elements = &rasterResourceTableElements;
    rasterResourceTableInfo.NumElements = 2;
}
rhi::RHIResourceTable* rasterResourceTable = device->CreateResourceTable(rasterResourceTableInfo);
```



### Creating the Raster Pass
Create a raster pipelineState for raster pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIOutputStateDescriptor outputStateInfo;
{
    outputStateInfo.SampleCount = ERHISampleCount::None;
    outputStateInfo.DepthFormat = ERHIPixelFormat::D32_Float_S8_UInt;
    outputStateInfo.ColorFormat0 = ERHIPixelFormat::R8G8B8A8_UNorm;
    outputStateInfo.ColorFormat1 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat2 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat3 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat4 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat5 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat6 = outputStateInfo.ColorFormat0;
    outputStateInfo.ColorFormat7 = outputStateInfo.ColorFormat0;
}

rhi::RHIBlendStateDescriptor blendStateInfo;
{
    blendStateInfo.AlphaToCoverage = false;
    blendStateInfo.IndependentBlend = false;
    blendStateInfo.BlendDescriptor0.BlendEnable = false;
    blendStateInfo.BlendDescriptor0.BlendOpColor = ERHIBlendOp::Add;
    blendStateInfo.BlendDescriptor0.BlendOpAlpha = ERHIBlendOp::Add;
    blendStateInfo.BlendDescriptor0.SrcBlendColor = ERHIBlendMode::One;
    blendStateInfo.BlendDescriptor0.SrcBlendAlpha = ERHIBlendMode::One;
    blendStateInfo.BlendDescriptor0.DstBlendColor = ERHIBlendMode::Zero;
    blendStateInfo.BlendDescriptor0.DstBlendAlpha = ERHIBlendMode::Zero;
    blendStateInfo.BlendDescriptor0.ColorWriteChannel = ERHIColorWriteChannel::All;
    blendStateInfo.BlendDescriptor1 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor2 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor3 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor4 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor5 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor6 = blendStateInfo.BlendDescriptor0;
    blendStateInfo.BlendDescriptor7 = blendStateInfo.BlendDescriptor0;
}

rhi::RHIRasterizerStateDescriptor rasterizerStateInfo;
{
    rasterizerStateInfo.CullMode = ERHICullMode::Back;
    rasterizerStateInfo.FillMode = ERHIFillMode::Solid;
    rasterizerStateInfo.DepthBias = 0;
    rasterizerStateInfo.DepthBiasClamp = 0;
    rasterizerStateInfo.SlopeScaledDepthBias = 0;
    rasterizerStateInfo.DepthClipEnable = true;
    rasterizerStateInfo.ConservativeRaster = false;
    rasterizerStateInfo.AntialiasedLineEnable = false;
    rasterizerStateInfo.FrontCounterClockwise = false;
}

rhi::RHIDepthStencilStateDescriptor depthStencilStateInfo;
{
    depthStencilStateInfo.DepthEnable = true;
    depthStencilStateInfo.DepthWriteMask = true;
    depthStencilStateInfo.ComparisonMode = ERHIComparisonMode::LessEqual;
    depthStencilStateInfo.StencilEnable = false;
    depthStencilStateInfo.StencilReadMask = 255;
    depthStencilStateInfo.StencilWriteMask = 255;
    depthStencilStateInfo.BackFace.ComparisonMode = ERHIComparisonMode::Always;
    depthStencilStateInfo.BackFace.StencilPassOp = ERHIStencilOp::Keep;
    depthStencilStateInfo.BackFace.StencilFailOp = ERHIStencilOp::Keep;
    depthStencilStateInfo.BackFace.StencilDepthFailOp = ERHIStencilOp::Keep;
    depthStencilStateInfo.FrontFace.ComparisonMode = ERHIComparisonMode::Always;
    depthStencilStateInfo.FrontFace.StencilPassOp = ERHIStencilOp::Keep;
    depthStencilStateInfo.FrontFace.StencilFailOp = ERHIStencilOp::Keep;
    depthStencilStateInfo.FrontFace.StencilDepthFailOp = ERHIStencilOp::Keep;
}

rhi::RHIRenderStateDescriptor renderStateInfo;
{
    renderStateInfo.SampleMask = 0;
    renderStateInfo.BlendState = blendStateInfo;
    renderStateInfo.RasterizerState = rasterizerStateInfo;
    renderStateInfo.DepthStencilState = depthStencilStateInfo;
}

rhi::RHIVertexElementDescriptor vertexElementInfos[2];
{
    vertexElementInfos[0].Slot = 1;
    vertexElementInfos[0].Offset = 0;
    vertexElementInfos[0].Type = ERHISemanticType::Color;
    vertexElementInfos[0].Format = ERHISemanticFormat::Float4;
    vertexElementInfos[1].Slot = 0;
    vertexElementInfos[1].Offset = 16;
    vertexElementInfos[1].Type = ERHISemanticType::Position;
    vertexElementInfos[1].Format = ERHISemanticFormat::Float4;
}

rhi::RHIVertexLayoutDescriptor vertexLayoutInfos[1];
{
    vertexLayoutInfos[0].Index = 0;
    vertexLayoutInfos[0].Stride = sizeOf(Vertex);
    vertexLayoutInfos[0].StepMode = ERHIVertexStepMode::PerVertex;
    vertexLayoutInfos[0].VertexElements = &vertexElementInfos;
    vertexLayoutInfos[0].VertexElementLength = vertexElementInfos.length;
}

rhi::RHIPipelineLayoutDescriptor rasterPipelienLayoutInfo;
{
    rasterPipelienLayoutInfo.bLocalSignature = false;
    rasterPipelienLayoutInfo.bUseVertexLayout = true;
    rasterPipelienLayoutInfo.StaticSamplers = nullptr;
    rasterPipelienLayoutInfo.NumStaticSamplers = 0;
    rasterPipelienLayoutInfo.ResourceTableLayouts = rhirasterResourceTableLayout;
    rasterPipelienLayoutInfo.NumResourceTableLayouts = 1;
}
rhi::RHIPipelineLayout* rasterPipelineLayout = device->CreatePipelineLayout(rasterPipelienLayoutInfo);

rhi::RHIFunctionDescriptor vertexFunctionInfo;
{
    vertexFunctionInfo.Type = ERHIFunctionType::Vertex;
    vertexFunctionInfo.ByteSize = vertexBlob.Size;
    vertexFunctionInfo.ByteCode = vertexBlob.Data;
    vertexFunctionInfo.EntryName = "VSMain";
}
rhi::RHIFunction* vertexFunction = device->CreateFunction(vertexFunctionInfo);

rhi::RHIFunctionDescriptor fragmentFunctionInfo;
{
    fragmentFunctionInfo.Type = ERHIFunctionType::Fragment;
    fragmentFunctionInfo.ByteSize = fragmentBlob.Size;
    fragmentFunctionInfo.ByteCode = fragmentBlob.Data;
    fragmentFunctionInfo.EntryName = "FSMain";
}
rhi::RHIFunction* vertexFunction = device->CreateFunction(fragmentFunctionInfo);

rhi::RHIRasterPipelineDescriptor rasterPipelineInfo;
{
    rasterPipelineInfo.OutputState = outputStateInfo;
    rasterPipelineInfo.RenderState = renderStateInfo;
    rasterPipelineInfo.VertexLayouts = &vertexLayoutInfos;
    rasterPipelineInfo.NumVertexLayouts = vertexLayoutInfos.length;
    rasterPipelineInfo.VertexFunction = vertexFunction;
    rasterPipelineInfo.FragmentFunction = vertexFunction;
    rasterPipelineInfo.PipelineLayout = rasterPipelineLayout;
    rasterPipelineInfo.PrimitiveTopology = ERHIPrimitiveTopology::TriangleList;
}
rhi::RHIPipeline* rasterPipeline = device->CreateRasterPipeline(rasterPipelineInfo);
```



### Frame Begin Logic
Create commandbuffer and encoder for init

```c++
#include <rhi/d3d12.h>
...
rhi::RHICommandBuffer* cmdBuffer = graphicsQueue->CreateCommandBuffer(); //if renderer use async upload it's sould be use TransferQueue to record upload command and use Fence to sync CPU event

cmdBuffer.Begin("FrameInit");
{
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(indexBufferGPU, ERHITextureState::Undefine, ERHITextureState::CopyDst, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(vertexBufferGPU, ERHITextureState::Undefine, ERHITextureState::CopyDst, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));

    rhi::RHITransferEncoder* transferEncoder = cmdBuffer.BeginTransferPass(RHITransferPassDescriptor("Upload VertexStream"));
    {
        transferEncoder->CopyBufferToBuffer(indexBufferCPU, 0, indexBufferGPU, 0, indexBufferInfo.ByteSize);
        transferEncoder->CopyBufferToBuffer(vertexBufferCPU, 0, vertexBufferGPU, 0, vertexBufferInfo.ByteSize);
    }
    transferEncoder->EndPass();

    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(indexBufferGPU, ERHITextureState::CopyDst, ERHITextureState::IndexBuffer, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(vertexBufferGPU, ERHITextureState::CopyDst, ERHITextureState::VertexBuffer, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));
}
cmdBuffer.End("FrameInit");

graphicsQueue->Submit(cmdBuffer, 1, fence, nullptr, 0, nullptr, 0); //cmdBuffers, cmdBufferCount, fence, waitSemaphores, waitSemaphoreCount, signalSemaphores, signalSemaphoreCount
```



### Frame RenderLoop Logic
Create commandbuffer and encoder for rendering

```c++
#include <rhi/d3d12.h>
...
rhi::RHICommandBuffer* cmdBuffer = graphicsQueue->CreateCommandBuffer();

cmdBuffer.Begin("FrameRendering");
{
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(texture, ERHITextureState::Undefine, ERHITextureState::UnorderedAccess, ERHIPipelineStage.Common, ERHIPipelineStage.Compute, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));

    // run compute pass
    rhi::RHIComputeEncoder* computeEncoder = cmdBuffer->BeginComputePass(RHIComputePassDescriptor("ComputePass"));
    {
        computeEncoder->PushDebugGroup("GenereteIndex");
        computeEncoder->SetPipeline(computePipeline);
        computeEncoder->SetResourceTable(computeResourceTable, 0);
        computeEncoder->Dispatch(math::ceil(screenSize.x / 8), math::ceil(screenSize.y / 8), 1);
        computeEncoder->PopDebugGroup();
    }
    computeEncoder->EndPass();

    //run raster pass
    rhi::RHIColorAttachmentDescriptor colorAttachmentInfos[1];
    {
        colorAttachmentInfos[0].MipLevel = 0;
        colorAttachmentInfos[0].ArraySlice = 0;
        colorAttachmentInfos[0].ClearValue = float4(0.5f, 0.5f, 1, 1);
        colorAttachmentInfos[0].LoadAction = ERHILoadAction::Clear;
        colorAttachmentInfos[0].StoreAction = ERHIStoreAction::Store;
        colorAttachmentInfos[0].RenderTarget = swapChain->AcquireBackBufferTexture();
        colorAttachmentInfos[0].ResolveTarget = nullptr;
        colorAttachmentInfos[0].ResolveMipLevel = 0;
        colorAttachmentInfos[0].ResolveArraySlice = 0;
    }

    RHISubPassDescriptor subPassDescriptors[1];
    {
        subPassDescriptors[0].Flags = ERHISubPassFlags::None;
        subPassDescriptors[0].ColorInputs = RHIAttachmentIndexArray::Emtpy;
        subPassDescriptors[0].ColorOutputs = RHIAttachmentIndexArray(1);
    }

    rhi::RHIRasterPassDescriptor rasterPassInfo;
    {
        rasterPassInfo.Name = "RasterPassInfo";
        rasterPassInfo.ArrayLength = 1;
        rasterPassInfo.SampleCount = 1;
        rasterPassInfo.MultiViewCount = 0;
        rasterPassInfo.Occlusion = nullptr;
        rasterPassInfo.Timestamp = nullptr;
        rasterPassInfo.Statistics = nullptr;
        rasterPassInfo.ShadingRateTexture = nullptr;
        rasterPassInfo.ColorAttachments = colorAttachmentInfos;
        rasterPassInfo.DepthStencilAttachment = nullptr;
        rasterPassInfo.SubPassDescriptors = subPassDescriptors;
    }

    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(texture, ERHITextureState::UnorderedAccess, ERHITextureState::ShaderResource, ERHIPipelineStage.Compute, ERHIPipelineStage.Fragment, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(swapChain->AcquireBackBufferTexture(), ERHITextureState::Present, ERHITextureState::RenderTarget, ERHIPipelineStage.Common, ERHIPipelineStage.Fragment, ERHIPipelineType::Graphics, ERHIPipelineType::Graphics));

    rhi::RHIRasterEncoder* rasterEncoder = cmdBuffer->BeginRasterPass(rasterPassInfo);
    {
        rasterEncoder->SetScissor(Rect(0, 0, screenSize.x, screenSize.y));
        rasterEncoder->SetViewport(Viewport(0, 0, screenSize.x, screenSize.y, 0, 1));
        rasterEncoder->SetBlendFactor(1);
        rasterEncoder->PushDebugGroup("DrawTriange");
        rasterEncoder->SetPipeline(rasterPipeline);
        rasterEncoder->SetResourceTable(rasterResourceTable, 0);
        rasterEncoder->SetIndexBuffer(indexBufferGPU, 0, ERHIIndexFormat::UInt16);
        rasterEncoder->SetVertexBuffer(vertexBufferGPU, 0, 0);
        rasterEncoder->SetShadingRate(ERHIShadingRate.Rate2x1, ERHIShadingRateCombiner.Passthrough);
        rasterEncoder->DrawIndexed(3, 1, 0, 0, 0);
        rasterEncoder->PopDebugGroup();
    }
    rasterEncoder->EndPass();

    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(texture, ERHITextureState.ShaderResource, ERHITextureState.Undefine, ERHIPipelineStage.Fragment, ERHIPipelineStage.Common, ERHIPipelineType.Graphics, ERHIPipelineType.Graphics));
    cmdBuffer->ResourceBarrier(RHIBarrier::Transition(swapChain->AcquireBackBufferTexture(), ERHITextureState.RenderTarget, ERHITextureState.Present, ERHIPipelineStage.Fragment, ERHIPipelineStage.Common, ERHIPipelineType.Graphics, ERHIPipelineType.Graphics));
}
cmdBuffer.End("FrameRendering");

graphicsQueue->Submit(cmdBuffer, 1, fence, nullptr, 0, nullptr, 0); //cmdBuffers, cmdBufferCount, fence, waitSemaphores, waitSemaphoreCount, signalSemaphores, signalSemaphoreCount

swapChain->Present();

fence->Wait();
```



### Release Resource
Release every resource on end

```c++
#include <rhi/d3d12.h>
...
indexBufferCPU->Release();
vertexBufferCPU->Release();
indexBufferGPU->Release();
vertexBufferGPU->Release();
sampler->Release();
texture->Release();
textureSRV->Release();
textureUAV->Release();
computeFunction->Release();
computeResourceTableLayout->Release();
computeResourceTable->Release();
computePipelineLayout->Release();
computePipeline->Release();
vertexFunction->Release();
fragmentFunction->Release();
rasterResourceTableLayout->Release();
rasterResourceTable->Release();
rasterPipelineLayout->Release();
rasterPipeline->Release();
cmdBuffer->Release();
transferQueue->Release();
computeQueue->Release();
graphicsQueue->Release();
fence->Release();
swapChain->Release();
instance->Release();
```
