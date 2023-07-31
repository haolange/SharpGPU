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
uint32 queueCount = rhiDevice->GetMaxQueueCount(EQueueType.Blit);
//queueCount = rhiDevice->GetMaxQueueCount(EQueueType.Compute);
//queueCount = rhiDevice->GetMaxQueueCount(EQueueType.Graphics);

rhi::RHICommandQueue* rhiBlitQueue = rhiDevice->CreateCommandQueue(EQueueType.Blit);
rhi::RHICommandQueue* rhiComputeQueue = rhiDevice->CreateCommandQueue(EQueueType.Compute);
rhi::RHICommandQueue* rhiGraphicsQueue = rhiDevice->CreateCommandQueue(EQueueType.Graphics);
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
swapChainDescriptor.Format = ESwapChainFormat::R8G8B8A8_UNorm;
swapChainDescriptor.Surface = hwdnPtr;
swapChainDescriptor.PresentMode = EPresentMode.VSync;
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
samplerInfo.MinFilter = EFilterMode::Linear;
samplerInfo.MagFilter = EFilterMode::Linear;
samplerInfo.MipFilter = EFilterMode::Linear;
samplerInfo.AddressModeU = EAddressMode::Repeat;
samplerInfo.AddressModeV = EAddressMode::Repeat;
samplerInfo.AddressModeW = EAddressMode::Repeat;
samplerInfo.ComparisonMode = EComparisonMode::Never;
rhi::RHISampler* rhiSampler = rhiDevice->CreateSampler(samplerInfo);

rhi::RHITextureDescriptor textureInfo;
textureInfo.Extent = uint3(screenSize.xy, 1);
textureInfo.MipCount = 1;
textureInfo.Sample = ESampleCount::None;
textureInfo.Format = EPixelFormat::R8G8B8A8_UNorm;
textureInfo.UsageFlag = ETextureUsage::ShaderResource | ETextureUsage::UnorderedAccess;
textureInfo.Dimension = ETextureDimension::Texture2D;
textureInfo.StorageMode = EStorageMode::GPULocal;
rhi::RHITexture* rhiTexture = rhiDevice->CreateTexture(textureInfo);
```



### Creating the texture view
Create textureView for compute or graphics

```c++
#include <rhi/d3d12.h>
...
rhi::RHITextureViewDescriptor outputViewInfo;
outputViewInfo.MipCount = 1;
outputViewInfo.BaseMipLevel = 0;
outputViewInfo.SliceCount = 1;
outputViewInfo.BaseSliceLevel = 0;
outputViewInfo.ViewType = ETextureViewType::UnorderedAccess;
rhi::TextureView* rhiTextureSRV = rhiTexture->CreateTextureView(outputViewInfo);

rhi::RHITextureViewDescriptor outputViewInfo;
outputViewInfo.MipCount = 1;
outputViewInfo.BaseMipLevel = 0;
outputViewInfo.SliceCount = 1;
outputViewInfo.BaseSliceLevel = 0;
outputViewInfo.ViewType = ETextureViewType::UnorderedAccess;
rhi::TextureView* rhiTextureUAV = rhiTexture->CreateTextureView(outputViewInfo);
```



### Creating the Compute bind table
Create a compute bindTable

```c++
#include <rhi/d3d12.h>
...
rhi::RHIBindTableLayoutElement computeBindTableLayoutElements[1];
computeBindTableLayoutElements[0].Slot = 0;
computeBindTableLayoutElements[0].Type = EBindType::StorageTexture;
computeBindTableLayoutElements[0].Visible = EFunctionStage::Compute;

rhi::RHIBindTableLayoutDescriptor computeBindTableLayoutInfo;
computeBindTableLayoutInfo.Index = 0;
computeBindTableLayoutInfo.Elements = computeBindTableLayoutElements;
computeBindTableLayoutInfo.ElementsLength = 1;
rhi::RHIBindTableLayout* rhiComputeBindTableLayout = rhiDevice->CreateBindTableLayout(computeBindTableLayoutInfo);

rhi::RHIBindTableElement computeBindTableElements[1];
computeBindTableElements[0].TextureView = rhiTextureUAV;

rhi::RHIBindTableDescriptor computeBindTableInfo;
computeBindTableInfo.Layout = rhiComputeBindTableLayout;
computeBindTableInfo.Elements = computeBindTableElements;
computeBindTableInfo.ElementLength = 1;
rhi::RHIBindTable* rhiComputeBindTable= rhiDevice->CreateBindTable(computeBindTableInfo);
```



### Creating the Compute Pass
Create a compute pipeline for compute pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIFunctionDescriptor computeFunctionInfo;
computeFunctionInfo.Type = EFunctionType::Compute;
computeFunctionInfo.ByteSize = computeBlob.Size;
computeFunctionInfo.ByteCode = computeBlob.Data;
computeFunctionInfo.EntryName = "CSMain";
rhi::RHIFunction* rhiComputeFunction = rhiDevice->CreateFunction(computeFunctionInfo);

rhi::RHIPipelineLayoutDescriptor computePipelienLayoutInfo;
computePipelienLayoutInfo.bUseVertexLayout = false;
computePipelienLayoutInfo.bIsLocalSignature = false;
computePipelienLayoutInfo.StaticSamplers = nullptr;
computePipelienLayoutInfo.StaticSamplerLength = 0;
computePipelienLayoutInfo.BindTableLayouts = rhiComputeBindTableLayout;
computePipelienLayoutInfo.BindTableLayoutLength = 1;
rhi::RHIPipelineLayout* rhiComputePipelineLayout = rhiDevice->CreatePipelineLayout(computePipelienLayoutInfo);

rhi::RHIComputePipelineDescriptor computePipelineInfo;
computePipelineInfo.ThreadSize = uint3(8, 8, 1);
computePipelineInfo.ComputeFunction = rhiComputeFunction;
computePipelineInfo.PipelineLayout = rhiComputePipelineLayout;
rhi::RHIPipeline* rhiComputePipeline = rhiDevice->CreateComputePipeline(computePipelineInfo);
```



### Creating the UniformBuffer
Create a uniform buffer for any pipeline to read constant data

```c++
#include <rhi/d3d12.h>
...
UniformInfo uniformArray[1] = // ......;

rhi::RHIBufferDescriptor uniformBufferInfo;
uniformBufferInfo.ByteSize = uniformArray.Length * ((sizeof(UniformInfo) + 255) & ~255);
uniformBufferInfo.UsageFlag = EBufferUsage::UniformBuffer;
uniformBufferInfo.StorageMode = EStorageMode::Dynamic;
rhi::RHIBuffer* rhiUniformBuffer = rhiDevice->CreateBuffer(uniformBufferInfo);

void* uniformData = rhiUniformBuffer->Map(0, uniformBufferInfo.ByteSize);
MemoryUtility::MemCpy(&uniformArray, uniformData, uniformBufferInfo.ByteSize);
rhiIndexBuffer->Unmap(0, uniformBufferInfo.ByteSize);

rhi::RHIBufferViewDescriptor uniformBufferViewInfo;
uniformBufferViewInfo.Offset = 0;
uniformBufferViewInfo.Type = EBufferViewType.UniformBuffer;
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
indexBufferInfo.UsageFlag = EBufferUsage::IndexBuffer;
indexBufferInfo.StorageMode = EStorageMode::HostUpload;
rhi::RHIBuffer* rhiIndexBufferCPU = rhiDevice->CreateBuffer(indexBufferInfo);

void* indexData = rhiIndexBufferCPU->Map(0, indexBufferInfo.ByteSize);
MemoryUtility::MemCpy(&indices, indexData, indexBufferInfo.ByteSize);
rhiIndexBufferCPU->Unmap(0, indexBufferInfo.ByteSize);

vertexBufferInfo.StorageMode = EStorageMode.GPULocal;
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
vertexBufferInfo.UsageFlag = EBufferUsage::VertexBuffer;
vertexBufferInfo.StorageMode = EStorageMode::HostUpload;
rhi::RHIBuffer* rhiVertexBufferCPU = rhiDevice->CreateBuffer(vertexBufferInfo);

void* vertexData = rhiVertexBufferCPU->Map(vertexBufferInfo.ByteSize, 0);
MemoryUtility::MemCpy(&vertices, vertexData, vertexBufferInfo.ByteSize);
rhiVertexBufferCPU->Unmap();

vertexBufferInfo.StorageMode = EStorageMode.GPULocal;
rhi::RHIBuffer* rhiVertexBufferGPU = rhiDevice->CreateBuffer(vertexBufferInfo);
```



### Creating the Graphics bind table
Create a bindTable graphics

```c++
#include <rhi/d3d12.h>
...
rhi::RHIBindTableLayoutElement graphicsBindTableLayoutElements[2];
graphicsBindTableLayoutElements[0].Slot = 0;
graphicsBindTableLayoutElements[0].Type = EBindType::Texture;
graphicsBindTableLayoutElements[0].Visible = EFunctionStage::Fragment;
graphicsBindTableLayoutElements[1].Slot = 1;
graphicsBindTableLayoutElements[1].Type = EBindType::Sampler;
graphicsBindTableLayoutElements[1].Visible = EFunctionStage::Fragment;

rhi::RHIBindTableLayoutDescriptor graphicsBindTableLayoutInfo;
graphicsBindTableLayoutInfo.Index = 0;
graphicsBindTableLayoutInfo.Elements = &graphicsBindTableLayoutElements;
graphicsBindTableLayoutInfo.ElementsLength = 2;
rhi::RHIBindTableLayout* rhiGraphicsBindTableLayout = rhiDevice->CreateBindTableLayout(graphicsBindTableLayoutInfo);

rhi::RHIBindTableElement graphicsBindTableElements[2];
graphicsBindTableElements[0].TextureView = rhiTextureSRV;
graphicsBindTableElements[1].Sampler = rhiSampler;

rhi::RHIBindTableDescriptor graphicsBindTableInfo;
graphicsBindTableInfo.Layout = rhiGraphicsBindTableLayout;
graphicsBindTableInfo.Elements = &graphicsBindTableElements;
graphicsBindTableInfo.ElementLength = 2;
rhi::RHIBindTable* rhiGraphicsBindTable = rhiDevice->CreateBindTable(graphicsBindTableInfo);
```



### Creating the Graphics Pass
Create a graphics pipeline for graphics pass

```c++
#include <rhi/d3d12.h>
...
rhi::RHIOutputStateDescriptor outputStateInfo;
outputStateInfo.SampleCount = ESampleCount::None;
outputStateInfo.DepthFormat = EPixelFormat::D32_Float_S8_UInt;
outputStateInfo.ColorFormat0 = EPixelFormat::R8G8B8A8_UNorm;
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
blendStateInfo.BlendDescriptor0.BlendOpColor = EBlendOp::Add;
blendStateInfo.BlendDescriptor0.BlendOpAlpha = EBlendOp::Add;
blendStateInfo.BlendDescriptor0.SrcBlendColor = EBlendMode::One;
blendStateInfo.BlendDescriptor0.SrcBlendAlpha = EBlendMode::One;
blendStateInfo.BlendDescriptor0.DstBlendColor = EBlendMode::Zero;
blendStateInfo.BlendDescriptor0.DstBlendAlpha = EBlendMode::Zero;
blendStateInfo.BlendDescriptor0.ColorWriteChannel = EColorWriteChannel::All;
blendStateInfo.BlendDescriptor1 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor2 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor3 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor4 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor5 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor6 = blendStateInfo.BlendDescriptor0;
blendStateInfo.BlendDescriptor7 = blendStateInfo.BlendDescriptor0;

rhi::RHIRasterizerStateDescriptor rasterizerStateInfo;
rasterizerStateInfo.CullMode = ECullMode::Back;
rasterizerStateInfo.FillMode = EFillMode::Solid;
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
depthStencilStateInfo.ComparisonMode = EComparisonMode::LessEqual;
depthStencilStateInfo.StencilEnable = false;
depthStencilStateInfo.StencilReadMask = 255;
depthStencilStateInfo.StencilWriteMask = 255;
depthStencilStateInfo.BackFace.ComparisonMode = EComparisonMode::Always;
depthStencilStateInfo.BackFace.StencilPassOp = EStencilOp::Keep;
depthStencilStateInfo.BackFace.StencilFailOp = EStencilOp::Keep;
depthStencilStateInfo.BackFace.StencilDepthFailOp = EStencilOp::Keep;
depthStencilStateInfo.FrontFace.ComparisonMode = EComparisonMode::Always;
depthStencilStateInfo.FrontFace.StencilPassOp = EStencilOp::Keep;
depthStencilStateInfo.FrontFace.StencilFailOp = EStencilOp::Keep;
depthStencilStateInfo.FrontFace.StencilDepthFailOp = EStencilOp::Keep;

rhi::RHIRenderStateDescriptor renderStateInfo;
renderStateInfo.SampleMask = 0;
renderStateInfo.BlendState = blendStateInfo;
renderStateInfo.RasterizerState = rasterizerStateInfo;
renderStateInfo.DepthStencilState = depthStencilStateInfo;

rhi::RHIVertexElementDescriptor vertexElementInfos[2];
vertexElementInfos[0].Slot = 1;
vertexElementInfos[0].Offset = 0;
vertexElementInfos[0].Type = ESemanticType::Color;
vertexElementInfos[0].Format = ESemanticFormat::Float4;
vertexElementInfos[1].Slot = 0;
vertexElementInfos[1].Offset = 16;
vertexElementInfos[1].Type = ESemanticType::Position;
vertexElementInfos[1].Format = ESemanticFormat::Float4;

rhi::RHIVertexLayoutDescriptor vertexLayoutInfos[1];
vertexLayoutInfos[0].Index = 0;
vertexLayoutInfos[0].Stride = sizeOf(Vertex);
vertexLayoutInfos[0].StepMode = EVertexStepMode::PerVertex;
vertexLayoutInfos[0].VertexElements = &vertexElementInfos;
vertexLayoutInfos[0].VertexElementLength = vertexElementInfos.length;

rhi::RHIPipelineLayoutDescriptor graphicsPipelienLayoutInfo;
graphicsPipelienLayoutInfo.bLocalSignature = false;
graphicsPipelienLayoutInfo.bUseVertexLayout = true;
graphicsPipelienLayoutInfo.StaticSamplers = nullptr;
graphicsPipelienLayoutInfo.StaticSamplerLength = 0;
graphicsPipelienLayoutInfo.BindTableLayouts = rhigraphicsBindTableLayout;
graphicsPipelienLayoutInfo.BindTableLayoutLength = 1;
rhi::RHIPipelineLayout* rhiGraphicsPipelineLayout = rhiDevice->CreatePipelineLayout(graphicsPipelienLayoutInfo);

rhi::RHIFunctionDescriptor vertexFunctionInfo;
vertexFunctionInfo.Type = EFunctionType::Vertex;
vertexFunctionInfo.ByteSize = vertexBlob.Size;
vertexFunctionInfo.ByteCode = vertexBlob.Data;
vertexFunctionInfo.EntryName = "VSMain";
rhi::RHIFunction* rhiVertexFunction = rhiDevice->CreateFunction(vertexFunctionInfo);

rhi::RHIFunctionDescriptor fragmentFunctionInfo;
fragmentFunctionInfo.Type = EFunctionType::Fragment;
fragmentFunctionInfo.ByteSize = fragmentBlob.Size;
fragmentFunctionInfo.ByteCode = fragmentBlob.Data;
fragmentFunctionInfo.EntryName = "FSMain";
rhi::RHIFunction* rhiVertexFunction = rhiDevice->CreateFunction(fragmentFunctionInfo);

rhi::RHIGraphicsPipelineDescriptor graphicsPipelineInfo;
graphicsPipelineInfo.OutputState = outputStateInfo;
graphicsPipelineInfo.RenderState = renderStateInfo;
graphicsPipelineInfo.VertexLayouts = &vertexLayoutInfos;
graphicsPipelineInfo.VertexLayoutLength = vertexLayoutInfos.length;
graphicsPipelineInfo.VertexFunction = rhiVertexFunction;
graphicsPipelineInfo.FragmentFunction = rhiVertexFunction;
graphicsPipelineInfo.PipelineLayout = rhiGraphicsPipelineLayout;
graphicsPipelineInfo.PrimitiveTopology = EPrimitiveTopology::TriangleList;
rhi::RHIPipeline* rhiGraphicsPipeline = rhiDevice->CreateGraphicsPipeline(graphicsPipelineInfo);
```



### Frame Begin Logic
Create commandbuffer and encoder for init

```c++
#include <rhi/d3d12.h>
...
rhi::RHICommandBuffer* rhiCmdBuffer = rhiGraphicsQueue->CreateCommandBuffer(); //if renderer use async upload it's sould be use BlitQueue to record upload command and use Fence to sync CPU event
rhiCmdBuffer.Begin("FrameInit");

rhi::RHIBlitEncoder* rhiBlitEncoder = rhiCmdBuffer.BeginBlitEncoding("Upload VertexStream");
rhiBlitEncoder->ResourceBarrier(RHIBarrier::Transition(rhiIndexBufferGPU, EOwnerState::GfxToGfx, ETextureState::Undefine, ETextureState::CopyDst));
rhiBlitEncoder->ResourceBarrier(RHIBarrier::Transition(rhiVertexBufferGPU, EOwnerState::GfxToGfx, ETextureState::Undefine, ETextureState::CopyDst));
rhiBlitEncoder->CopyBufferToBuffer(rhiIndexBufferCPU, 0, rhiIndexBufferGPU, 0, indexBufferInfo.ByteSize);
rhiBlitEncoder->CopyBufferToBuffer(rhiVertexBufferCPU, 0, rhiVertexBufferGPU, 0, vertexBufferInfo.ByteSize);
rhiBlitEncoder->ResourceBarrier(RHIBarrier::Transition(rhiIndexBufferGPU, EOwnerState::GfxToGfx, ETextureState::CopyDst, ETextureState::IndexBuffer));
rhiBlitEncoder->ResourceBarrier(RHIBarrier::Transition(rhiVertexBufferGPU, EOwnerState::GfxToGfx, ETextureState::CopyDst, ETextureState::VertexBuffer));
rhiBlitEncoder->EndEncoding();

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
rhi::RHIComputeEncoder* rhiComputeEncoder = rhiCmdBuffer.BeginComputeEncoding("ComputePass");
rhiComputeEncoder->PushDebugGroup("GenereteIndex");
rhiComputeEncoder->ResourceBarrier(RHIBarrier::Transition(rhiTexture, EOwnerState::GfxToGfx, ETextureState::Undefine, ETextureState::UnorderedAccess));
rhiComputeEncoder->SetPipelineLayout(rhiComputePipelineLayout);
rhiComputeEncoder->SetPipeline(rhiComputePipeline);
rhiComputeEncoder->SetBindTable(rhiComputeBindTable, 0);
rhiComputeEncoder->Dispatch(math::ceil(screenSize.x / 8), math::ceil(screenSize.y / 8), 1);
rhiComputeEncoder->PopDebugGroup();
rhiBlitEncoder->EndEncoding();

//run graphics pass
rhi::RHIColorAttachmentDescriptor colorAttachmentInfos[1];
colorAttachmentInfos[0].LoadOp = ELoadOp::Clear;
colorAttachmentInfos[0].StoreOp = EStoreOp::Store;
colorAttachmentInfos[0].MipLevel = 0;
colorAttachmentInfos[0].SliceLevel = 0;
colorAttachmentInfos[0].ClearValue = float4(0.5f, 0.5f, 1, 1);
colorAttachmentInfos[0].RenderTarget = rhiSwapChain->AcquireBackBufferTexture();
colorAttachmentInfos[0].ResolveTarget = nullptr;

rhi::RHIGraphicsPassDescriptor graphicsPassInfo;
graphicsPassInfo.Name = "GraphicsPass";
graphicsPassInfo.ShadingRate = RHIShadingRateDescriptor(EShadingRate::Rate1x2); //RHIShadingRateDescriptor(RateSurfaceTexture)
graphicsPassInfo.ColorAttachments = colorAttachmentInfos;
graphicsPassInfo.DepthStencilAttachment = nullptr;

rhi::RHIGraphicsEncoder* rhiGraphicsEncoder = rhiCmdBuffer.BeginGraphicsEncoding(graphicsPassInfo);
rhiGraphicsEncoder->SetScissor(Rect(0, 0, screenSize.x, screenSize.y));
rhiGraphicsEncoder->SetViewport(Viewport(0, 0, screenSize.x, screenSize.y, 0, 1));
rhiGraphicsEncoder->SetBlendFactor(1);
rhiGraphicsEncoder->PushDebugGroup("DrawTriange");
rhiGraphicsEncoder->ResourceBarrier(RHIBarrier::Transition(rhiTexture, EOwnerState::GfxToGfx, ETextureState::UnorderedAccess, ETextureState::ShaderResource));
rhiGraphicsEncoder->ResourceBarrier(RHIBarrier::Transition(rhiSwapChain->AcquireBackBufferTexture(), EOwnerState::GfxToGfx, ETextureState::Present, ETextureState::RenderTarget));
rhiGraphicsEncoder->SetPipelineLayout(rhiGraphicsPipelineLayout);
rhiGraphicsEncoder->SetPipeline(rhiGraphicsPipeline);
rhiGraphicsEncoder->SetBindTable(rhiGraphicsBindTable, 0);
rhiGraphicsEncoder->SetIndexBuffer(rhiIndexBufferGPU, 0, EIndexFormat::UInt16);
rhiGraphicsEncoder->SetVertexBuffer(rhiVertexBufferGPU, 0, 0);
rhiGraphicsEncoder->DrawIndexed(3, 1, 0, 0, 0);
rhiGraphicsEncoder->ResourceBarrier(RHIBarrier::Transition(rhiTexture, EOwnerState::GfxToGfx, ETextureState::ShaderResource, ETextureState::Undefine));
rhiGraphicsEncoder->ResourceBarrier(RHIBarrier::Transition(rhiSwapChain->AcquireBackBufferTexture(), EOwnerState::GfxToGfx, ETextureState::RenderTarget, ETextureState::Present));
rhiGraphicsEncoder->PopDebugGroup();
rhiBlitEncoder->EndEncoding();

rhiCmdBuffer.End("FrameRendering");
rhiGraphicsQueue->Submit(rhiCmdBuffer, 1, rhiFence, nullptr, 0, nullptr, 0); //cmdBuffers, cmdBufferCount, fence, waitSemaphores, waitSemaphoreCount, signalSemaphores, signalSemaphoreCount
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
rhiComputePipeline->Release();
rhiComputePipelineLayout->Release();
rhiVertexFunction->Release();
rhiFragmentFunction->Release();
rhiGraphicsBindTable->Release();
rhiGraphicsBindTableLayout->Release();
rhiGraphicsPipeline->Release();
rhiGraphicsPipelineLayout->Release();
rhiCmdBuffer->Release();
rhiBlitQueue->Release();
rhiComputeQueue->Release();
rhiGraphicsQueue->Release();
rhiFence->Release();
rhiSwapChain->Release();
rhiInstance->Release();
```
