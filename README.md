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

std::string graphicsHLSL
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
descriptor.Backend = ERHIBackend::DirectX12;
descriptor.EnableDebugLayer = false;
descriptor.EnableValidatior = false;
rhi::RHIInstance* rhiInstance = rhi::CreateInstance(descriptor);
```



### Creating the Device
That select device form instance use index

```c++
#include <rhi/d3d12.h>
...
rhi::RHIDevice* rhiDevice = rhiInstance->GetDevice(0);
```



### Creating the CommandQueue
Create command queue for execution gpu task

```c++
#include <rhi/d3d12.h>
...
uint32 queueCount = rhiDevice->GetMaxQueueCount(ERHIPipeline.Transfer);
//queueCount = rhiDevice->GetMaxQueueCount(ERHIPipeline.Compute);
//queueCount = rhiDevice->GetMaxQueueCount(ERHIPipeline.Graphics);

rhi::RHICommandQueue* rhiTransferQueue = rhiDevice->CreateCommandQueue(ERHIPipeline.Transfer);
rhi::RHICommandQueue* rhiComputeQueue = rhiDevice->CreateCommandQueue(ERHIPipeline.Compute);
rhi::RHICommandQueue* rhiGraphicsQueue = rhiDevice->CreateCommandQueue(ERHIPipeline.Graphics);
```



### Creating the Fence
Create frame fence for render loop sync

```c++
#include <rhi/d3d12.h>
...
rhi::RHIFence* rhiFence = rhiDevice->CreateFence();
```



### Creating the SwapChain
Create swap chain for window display

```c++
#include <rhi/d3d12.h>
...
rhi::RHISwapChainDescriptor swapChainDescriptor;
swapChainDescriptor.FPS = 60;
swapChainDescriptor.Count = 3;
swapChainDescriptor.Extent = screenSize;
swapChainDescriptor.Format = ERHISwapChainFormat::R8G8B8A8_UNorm;
swapChainDescriptor.Surface = hwdnPtr;
swapChainDescriptor.PresentMode = ERHIPresentMode.VSync;
swapChainDescriptor.PresentQueue = rhiGraphicsQueue;
swapChainDescriptor.FrameBufferOnly = true;
rhi::SwapChain* rhiSwapChain = rhiDevice->CreateSwapChain(swapChainDescriptor);
```



### Creating the texture
Create a texture and sampler

```c++
#include <rhi/d3d12.h>
...
rhi::RHISamplerDescriptor samplerInfo;
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
rhi::RHISampler* rhiSampler = rhiDevice->CreateSampler(samplerInfo);

rhi::RHITextureDescriptor textureInfo;
textureInfo.Extent = uint3(screenSize.xy, 1);
textureInfo.MipCount = 1;
textureInfo.SampleCount = ERHISampleCount::None;
textureInfo.Format = ERHIPixelFormat::R8G8B8A8_UNorm;
textureInfo.UsageFlag = ERHITextureUsage::ShaderResource | ERHITextureUsage::UnorderedAccess;
textureInfo.Dimension = ERHITextureDimension::Texture2D;
textureInfo.StorageMode = ERHIStorageMode::GPULocal;
rhi::RHITexture* rhiTexture = rhiDevice->CreateTexture(textureInfo);
```



### Creating the texture view
Create textureView for compute or graphics

```c++
#include <rhi/d3d12.h>
...
rhi::RHITextureViewDescriptor computeOutputViewInfo;
computeOutputViewInfo.MipCount = 1;
computeOutputViewInfo.BaseMipLevel = 0;
computeOutputViewInfo.ArrayCount = 1;
computeOutputViewInfo.BaseArraySlice = 0;
computeOutputViewInfo.ViewType = ERHITextureViewType::UnorderedAccess;
rhi::TextureView* rhiTextureUAV = rhiTexture->CreateTextureView(outputViewInfo);

rhi::RHITextureViewDescriptor graphicsInputViewInfo;
graphicsInputViewInfo.MipCount = 1;
graphicsInputViewInfo.BaseMipLevel = 0;
graphicsInputViewInfo.ArrayCount = 1;
graphicsInputViewInfo.BaseArraySlice = 0;
graphicsInputViewInfo.ViewType = ERHITextureViewType::ShaderResource;
rhi::TextureView* rhiTextureSRV = rhiTexture->CreateTextureView(outputViewInfo);
```



### Creating the Compute bind table
Create a compute bindTable

```c++
#include <rhi/d3d12.h>
...
rhi::RHIBindTableLayoutElement computeBindTableLayoutElements[1];
computeBindTableLayoutElements[0].Slot = 0;
computeBindTableLayoutElements[0].Type = ERHIBindType::StorageTexture2D;
computeBindTableLayoutElements[0].Visible = ERHIFunctionStage::Compute;

rhi::RHIBindTableLayoutDescriptor computeBindTableLayoutInfo;
computeBindTableLayoutInfo.Index = 0;
computeBindTableLayoutInfo.Elements = computeBindTableLayoutElements;
computeBindTableLayoutInfo.NumElements = 1;
rhi::RHIBindTableLayout* rhiComputeBindTableLayout = rhiDevice->CreateBindTableLayout(computeBindTableLayoutInfo);

rhi::RHIBindTableElement computeBindTableElements[1];
computeBindTableElements[0].TextureView = rhiTextureUAV;

rhi::RHIBindTableDescriptor computeBindTableInfo;
computeBindTableInfo.Layout = rhiComputeBindTableLayout;
computeBindTableInfo.Elements = computeBindTableElements;
computeBindTableInfo.NumElements = 1;
rhi::RHIBindTable* rhiComputeBindTable= rhiDevice->CreateBindTable(computeBindTableInfo);
```



### Creating the Compute Pass
Create a compute pipelineState for compute pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIFunctionDescriptor computeFunctionInfo;
computeFunctionInfo.Type = ERHIFunctionType::Compute;
computeFunctionInfo.ByteSize = computeBlob.Size;
computeFunctionInfo.ByteCode = computeBlob.Data;
computeFunctionInfo.EntryName = "CSMain";
rhi::RHIFunction* rhiComputeFunction = rhiDevice->CreateFunction(computeFunctionInfo);

rhi::RHIPipelineLayoutDescriptor computePipelienLayoutInfo;
computePipelienLayoutInfo.bLocalSignature = false;
computePipelienLayoutInfo.bUseVertexLayout = false;
computePipelienLayoutInfo.StaticSamplers = nullptr;
computePipelienLayoutInfo.NumStaticSamplers = 0;
computePipelienLayoutInfo.BindTableLayouts = rhiComputeBindTableLayout;
computePipelienLayoutInfo.NumBindTableLayouts = 1;
rhi::RHIPipelineLayout* rhiComputePipelineLayout = rhiDevice->CreatePipelineLayout(computePipelienLayoutInfo);

rhi::RHIComputePipelineStateDescriptor computePipelineStateInfo;
computePipelineStateInfo.ThreadSize = uint3(8, 8, 1);
computePipelineStateInfo.ComputeFunction = rhiComputeFunction;
computePipelineStateInfo.PipelineLayout = rhiComputePipelineLayout;
rhi::RHIPipelineState* rhiComputePipelineState = rhiDevice->CreateComputePipelineState(computePipelineStateInfo);
```



### Creating the UniformBuffer
Create a uniform buffer for any pipelineState to read constant data

```c++
#include <rhi/d3d12.h>
...
UniformInfo uniformArray[1] = // ......;

rhi::RHIBufferDescriptor uniformBufferInfo;
uniformBufferInfo.ByteSize = uniformArray.Length * ((sizeof(UniformInfo) + 255) & ~255);
uniformBufferInfo.UsageFlag = ERHIBufferUsage::UniformBuffer;
uniformBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
rhi::RHIBuffer* rhiUniformBuffer = rhiDevice->CreateBuffer(uniformBufferInfo);

void* uniformData = rhiUniformBuffer->Map(0, uniformBufferInfo.ByteSize);
MemoryUtility::MemCpy(&uniformArray, uniformData, uniformBufferInfo.ByteSize);
rhiIndexBuffer->Unmap(0, uniformBufferInfo.ByteSize);

rhi::RHIBufferViewDescriptor uniformBufferViewInfo;
uniformBufferViewInfo.Offset = 0;
uniformBufferViewInfo.Type = ERHIBufferViewType.UniformBuffer;
uniformBufferViewInfo.Count = uniformArray.Length;
uniformBufferViewInfo.Stride = (sizeof(UniformInfo) + 255) & ~255;
rhi::RHIBufferView* rhiUniformBufferView = rhiUniformBuffer->CreateBufferView(bufferViewDescriptor);
```



### Creating the Vertex Stream
Create indexBuffer and vertexBuffer

```c++
#include <rhi/d3d12.h>
...
//index buffer
uint16 indices[3] = {0, 1, 2};

rhi::RHIBufferDescriptor indexBufferInfo;
indexBufferInfo.ByteSize = indices.Length * sizeof(uint16);
indexBufferInfo.UsageFlag = ERHIBufferUsage::IndexBuffer;
indexBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
rhi::RHIBuffer* rhiIndexBufferCPU = rhiDevice->CreateBuffer(indexBufferInfo);

void* indexData = rhiIndexBufferCPU->Map(0, indexBufferInfo.ByteSize);
MemoryUtility::MemCpy(&indices, indexData, indexBufferInfo.ByteSize);
rhiIndexBufferCPU->Unmap(0, indexBufferInfo.ByteSize);

vertexBufferInfo.StorageMode = ERHIStorageMode.GPULocal;
rhi::RHIBuffer* rhiIndexBufferGPU = rhiDevice->CreateBuffer(indexBufferInfo);

//vertex buffer
Vertex vertices[3];
vertices[0].color = float4(1, 0, 0, 1);
vertices[0].position = float4(-0.5f, -0.5f, 0, 1);
vertices[1].color = float4(0, 1, 0, 1);
vertices[1].position = float4(0, 0.5f, 0, 1);
vertices[2].color = float4(0, 0, 1, 1);
vertices[2].position = float4(0.5f, -0.5f, 0, 1);

rhi::RHIBufferDescriptor vertexBufferInfo;
vertexBufferInfo.ByteSize = vertices.Length * sizeof(Vertex);
vertexBufferInfo.UsageFlag = ERHIBufferUsage::VertexBuffer;
vertexBufferInfo.StorageMode = ERHIStorageMode::HostUpload;
rhi::RHIBuffer* rhiVertexBufferCPU = rhiDevice->CreateBuffer(vertexBufferInfo);

void* vertexData = rhiVertexBufferCPU->Map(vertexBufferInfo.ByteSize, 0);
MemoryUtility::MemCpy(&vertices, vertexData, vertexBufferInfo.ByteSize);
rhiVertexBufferCPU->Unmap();

vertexBufferInfo.StorageMode = ERHIStorageMode.GPULocal;
rhi::RHIBuffer* rhiVertexBufferGPU = rhiDevice->CreateBuffer(vertexBufferInfo);
```



### Creating the Graphics bind table
Create a bindTable graphics

```c++
#include <rhi/d3d12.h>
...
rhi::RHIBindTableLayoutElement graphicsBindTableLayoutElements[2];
graphicsBindTableLayoutElements[0].Slot = 0;
graphicsBindTableLayoutElements[0].Type = ERHIBindType::Texture2D;
graphicsBindTableLayoutElements[0].Visible = ERHIFunctionStage::Fragment;
graphicsBindTableLayoutElements[1].Slot = 1;
graphicsBindTableLayoutElements[1].Type = ERHIBindType::Sampler;
graphicsBindTableLayoutElements[1].Visible = ERHIFunctionStage::Fragment;

rhi::RHIBindTableLayoutDescriptor graphicsBindTableLayoutInfo;
graphicsBindTableLayoutInfo.Index = 0;
graphicsBindTableLayoutInfo.Elements = &graphicsBindTableLayoutElements;
graphicsBindTableLayoutInfo.NumElements = 2;
rhi::RHIBindTableLayout* rhiGraphicsBindTableLayout = rhiDevice->CreateBindTableLayout(graphicsBindTableLayoutInfo);

rhi::RHIBindTableElement graphicsBindTableElements[2];
graphicsBindTableElements[0].TextureView = rhiTextureSRV;
graphicsBindTableElements[1].Sampler = rhiSampler;

rhi::RHIBindTableDescriptor graphicsBindTableInfo;
graphicsBindTableInfo.Layout = rhiGraphicsBindTableLayout;
graphicsBindTableInfo.Elements = &graphicsBindTableElements;
graphicsBindTableInfo.NumElements = 2;
rhi::RHIBindTable* rhiGraphicsBindTable = rhiDevice->CreateBindTable(graphicsBindTableInfo);
```



### Creating the Graphics Pass
Create a graphics pipelineState for graphics pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIOutputStateDescriptor outputStateInfo;
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

rhi::RHIBlendStateDescriptor blendStateInfo;
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

rhi::RHIRasterizerStateDescriptor rasterizerStateInfo;
rasterizerStateInfo.CullMode = ERHICullMode::Back;
rasterizerStateInfo.FillMode = ERHIFillMode::Solid;
rasterizerStateInfo.DepthBias = 0;
rasterizerStateInfo.DepthBiasClamp = 0;
rasterizerStateInfo.SlopeScaledDepthBias = 0;
rasterizerStateInfo.DepthClipEnable = true;
rasterizerStateInfo.ConservativeRaster = false;
rasterizerStateInfo.AntialiasedLineEnable = false;
rasterizerStateInfo.FrontCounterClockwise = false;

rhi::RHIDepthStencilStateDescriptor depthStencilStateInfo;
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

rhi::RHIRenderStateDescriptor renderStateInfo;
renderStateInfo.SampleMask = 0;
renderStateInfo.BlendState = blendStateInfo;
renderStateInfo.RasterizerState = rasterizerStateInfo;
renderStateInfo.DepthStencilState = depthStencilStateInfo;

rhi::RHIVertexElementDescriptor vertexElementInfos[2];
vertexElementInfos[0].Slot = 1;
vertexElementInfos[0].Offset = 0;
vertexElementInfos[0].Type = ERHISemanticType::Color;
vertexElementInfos[0].Format = ERHISemanticFormat::Float4;
vertexElementInfos[1].Slot = 0;
vertexElementInfos[1].Offset = 16;
vertexElementInfos[1].Type = ERHISemanticType::Position;
vertexElementInfos[1].Format = ERHISemanticFormat::Float4;

rhi::RHIVertexLayoutDescriptor vertexLayoutInfos[1];
vertexLayoutInfos[0].Index = 0;
vertexLayoutInfos[0].Stride = sizeOf(Vertex);
vertexLayoutInfos[0].StepMode = ERHIVertexStepMode::PerVertex;
vertexLayoutInfos[0].VertexElements = &vertexElementInfos;
vertexLayoutInfos[0].VertexElementLength = vertexElementInfos.length;

rhi::RHIPipelineLayoutDescriptor graphicsPipelienLayoutInfo;
graphicsPipelienLayoutInfo.bLocalSignature = false;
graphicsPipelienLayoutInfo.bUseVertexLayout = true;
graphicsPipelienLayoutInfo.StaticSamplers = nullptr;
graphicsPipelienLayoutInfo.NumStaticSamplers = 0;
graphicsPipelienLayoutInfo.BindTableLayouts = rhigraphicsBindTableLayout;
graphicsPipelienLayoutInfo.NumBindTableLayouts = 1;
rhi::RHIPipelineLayout* rhiGraphicsPipelineLayout = rhiDevice->CreatePipelineLayout(graphicsPipelienLayoutInfo);

rhi::RHIFunctionDescriptor vertexFunctionInfo;
vertexFunctionInfo.Type = ERHIFunctionType::Vertex;
vertexFunctionInfo.ByteSize = vertexBlob.Size;
vertexFunctionInfo.ByteCode = vertexBlob.Data;
vertexFunctionInfo.EntryName = "VSMain";
rhi::RHIFunction* rhiVertexFunction = rhiDevice->CreateFunction(vertexFunctionInfo);

rhi::RHIFunctionDescriptor fragmentFunctionInfo;
fragmentFunctionInfo.Type = ERHIFunctionType::Fragment;
fragmentFunctionInfo.ByteSize = fragmentBlob.Size;
fragmentFunctionInfo.ByteCode = fragmentBlob.Data;
fragmentFunctionInfo.EntryName = "FSMain";
rhi::RHIFunction* rhiVertexFunction = rhiDevice->CreateFunction(fragmentFunctionInfo);

rhi::RHIGraphicsPipelineStateDescriptor graphicsPipelineStateInfo;
graphicsPipelineStateInfo.OutputState = outputStateInfo;
graphicsPipelineStateInfo.RenderState = renderStateInfo;
graphicsPipelineStateInfo.VertexLayouts = &vertexLayoutInfos;
graphicsPipelineStateInfo.NumVertexLayouts = vertexLayoutInfos.length;
graphicsPipelineStateInfo.VertexFunction = rhiVertexFunction;
graphicsPipelineStateInfo.FragmentFunction = rhiVertexFunction;
graphicsPipelineStateInfo.PipelineLayout = rhiGraphicsPipelineLayout;
graphicsPipelineStateInfo.PrimitiveTopology = ERHIPrimitiveTopology::TriangleList;
rhi::RHIPipeline* rhiGraphicsPipelineState = rhiDevice->CreateGraphicsPipelineState(graphicsPipelineStateInfo);
```



### Frame Begin Logic
Create commandbuffer and encoder for init

```c++
#include <rhi/d3d12.h>
...
rhi::RHICommandBuffer* rhiCmdBuffer = rhiGraphicsQueue->CreateCommandBuffer(); //if renderer use async upload it's sould be use TransferQueue to record upload command and use Fence to sync CPU event
rhiCmdBuffer.Begin("FrameInit");

rhi::RHIGraphicsPassDescriptor transferPassInfo;
transferPassInfo.Name = "Upload VertexStream";
transferPassInfo.Timestamp = nullptr;

rhi::RHITransferEncoder* rhiTransferEncoder = rhiCmdBuffer.BeginTransferPass(transferPassInfo);
rhiTransferEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics, RHIBarrier::Transition(rhiIndexBufferGPU, ERHITextureState::Undefine, ERHITextureState::CopyDst));
rhiTransferEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiVertexBufferGPU, ERHITextureState::Undefine, ERHITextureState::CopyDst));
rhiTransferEncoder->CopyBufferToBuffer(rhiIndexBufferCPU, 0, rhiIndexBufferGPU, 0, indexBufferInfo.ByteSize);
rhiTransferEncoder->CopyBufferToBuffer(rhiVertexBufferCPU, 0, rhiVertexBufferGPU, 0, vertexBufferInfo.ByteSize);
rhiTransferEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiIndexBufferGPU, ERHITextureState::CopyDst, ERHITextureState::IndexBuffer));
rhiTransferEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiVertexBufferGPU, ERHITextureState::CopyDst, ERHITextureState::VertexBuffer));
rhiTransferEncoder->EndPass();

rhiCmdBuffer.End("FrameInit");
rhiGraphicsQueue->Submit(rhiCmdBuffer, 1, rhiFence, nullptr, 0, nullptr, 0); //cmdBuffers, cmdBufferCount, fence, waitSemaphores, waitSemaphoreCount, signalSemaphores, signalSemaphoreCount
```



### Frame RenderLoop Logic
Create commandbuffer and encoder for rendering

```c++
#include <rhi/d3d12.h>
...
rhi::RHICommandBuffer* rhiCmdBuffer = rhiGraphicsQueue->CreateCommandBuffer();
rhiCmdBuffer.Begin("FrameRendering");

// run compute pass
rhi::RHIGraphicsPassDescriptor computePassInfo;
computePassInfo.Name = "ComputePass";
computePassInfo.Timestamp = nullptr;
computePassInfo.Statistics = nullptr;

rhi::RHIComputeEncoder* rhiComputeEncoder = rhiCmdBuffer->BeginComputePass(computePassInfo);
rhiComputeEncoder->PushDebugGroup("GenereteIndex");
rhiComputeEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiTexture, ERHITextureState::Undefine, ERHITextureState::UnorderedAccess));
rhiComputeEncoder->SetPipelineLayout(rhiComputePipelineLayout);
rhiComputeEncoder->SetPipelineState(rhiComputePipelineState);
rhiComputeEncoder->SetBindTable(rhiComputeBindTable, 0);
rhiComputeEncoder->Dispatch(math::ceil(screenSize.x / 8), math::ceil(screenSize.y / 8), 1);
rhiComputeEncoder->PopDebugGroup();
rhiComputeEncoder->EndPass();

//run graphics pass
rhi::RHIColorAttachmentDescriptor colorAttachmentInfos[1];
colorAttachmentInfos[0].MipLevel = 0;
colorAttachmentInfos[0].ArraySlice = 0;
colorAttachmentInfos[0].ClearValue = float4(0.5f, 0.5f, 1, 1);
colorAttachmentInfos[0].LoadAction = ERHILoadAction::Clear;
colorAttachmentInfos[0].StoreAction = ERHIStoreAction::Store;
colorAttachmentInfos[0].RenderTarget = rhiSwapChain->AcquireBackBufferTexture();
colorAttachmentInfos[0].ResolveTarget = nullptr;

rhi::RHIGraphicsPassDescriptor graphicsPassInfo;
graphicsPassInfo.Name = "GraphicsPass";
graphicsPassInfo.ArrayLength = 1;
graphicsPassInfo.SampleCount = 1;
graphicsPassInfo.MultiViewCount = 0;
graphicsPassInfo.Occlusion = nullptr;
graphicsPassInfo.Timestamp = nullptr;
graphicsPassInfo.Statistics = nullptr;
graphicsPassInfo.ShadingRateTexture = nullptr;
graphicsPassInfo.ColorAttachments = colorAttachmentInfos;
graphicsPassInfo.DepthStencilAttachment = nullptr;

rhi::RHIGraphicsEncoder* rhiGraphicsEncoder = rhiCmdBuffer->BeginGraphicsPass(graphicsPassInfo);
rhiGraphicsEncoder->SetScissor(Rect(0, 0, screenSize.x, screenSize.y));
rhiGraphicsEncoder->SetViewport(Viewport(0, 0, screenSize.x, screenSize.y, 0, 1));
rhiGraphicsEncoder->SetBlendFactor(1);
rhiGraphicsEncoder->PushDebugGroup("DrawTriange");
rhiGraphicsEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiTexture, ERHITextureState::UnorderedAccess, ERHITextureState::ShaderResource));
rhiGraphicsEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiSwapChain->AcquireBackBufferTexture(), ERHITextureState::Present, ERHITextureState::RenderTarget));
rhiGraphicsEncoder->SetPipelineLayout(rhiGraphicsPipelineLayout);
rhiGraphicsEncoder->SetPipelineState(rhiGraphicsPipelineState);
rhiGraphicsEncoder->SetBindTable(rhiGraphicsBindTable, 0);
rhiGraphicsEncoder->SetIndexBuffer(rhiIndexBufferGPU, 0, ERHIIndexFormat::UInt16);
rhiGraphicsEncoder->SetVertexBuffer(rhiVertexBufferGPU, 0, 0);
rhiGraphicsEncoder->SetShadingRate(ERHIShadingRate.Rate2x1, ERHIShadingRateCombiner.Passthrough);
rhiGraphicsEncoder->DrawIndexed(3, 1, 0, 0, 0);
rhiGraphicsEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiTexture, ERHITextureState::ShaderResource, ERHITextureState::Undefine));
rhiGraphicsEncoder->ResourceBarrier(ERHIPipeline::Graphics, ERHIPipeline::Graphics,RHIBarrier::Transition(rhiSwapChain->AcquireBackBufferTexture(), ERHITextureState::RenderTarget, ERHITextureState::Present));
rhiGraphicsEncoder->PopDebugGroup();
rhiGraphicsEncoder->EndPass();

rhiCmdBuffer.End("FrameRendering");
rhiGraphicsQueue->Submit(rhiCmdBuffer, 1, rhiFence, nullptr, 0, nullptr, 0); //cmdBuffers, cmdBufferCount, fence, waitSemaphores, waitSemaphoreCount, signalSemaphores, signalSemaphoreCount

rhiSwapChain->Present();
rhiFence->Wait();
```



### Release Resource
Release every resource on end

```c++
#include <rhi/d3d12.h>
...
rhiIndexBufferCPU->Release();
rhiVertexBufferCPU->Release();
rhiIndexBufferGPU->Release();
rhiVertexBufferGPU->Release();
rhiSampler->Release();
rhiTexture->Release();
rhiTextureSRV->Release();
rhiTextureUAV->Release();
rhiComputeFunction->Release();
rhiComputeBindTable->Release();
rhiComputeBindTableLayout->Release();
rhiComputePipelineState->Release();
rhiComputePipelineLayout->Release();
rhiVertexFunction->Release();
rhiFragmentFunction->Release();
rhiGraphicsBindTable->Release();
rhiGraphicsBindTableLayout->Release();
rhiGraphicsPipelineState->Release();
rhiGraphicsPipelineLayout->Release();
rhiCmdBuffer->Release();
rhiTransferQueue->Release();
rhiComputeQueue->Release();
rhiGraphicsQueue->Release();
rhiFence->Release();
rhiSwapChain->Release();
rhiInstance->Release();
```
