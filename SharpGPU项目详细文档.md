# SharpGPU 项目详细文档

## 项目概述

SharpGPU 是一个基于现代 GPU API（Metal/Vulkan/DirectX12）的硬件抽象层（Hardware Abstract Layer, HAL）库，使用 C# 和 .NET 9.0 开发。该项目旨在为不同平台上的图形 API 提供统一的抽象接口，让开发者能够使用一套 API 在多个图形后端之间无缝切换。

### 核心特性
- **多后端支持**: DirectX 12、Vulkan、Metal
- **现代 GPU 功能**: 计算着色器、光线追踪、网格着色器
- **统一抽象**: 提供一致的 API 接口，隐藏底层实现差异
- **高性能**: 直接映射到底层 API，最小化性能开销
- **内存安全**: 基于 C# 的内存管理和类型安全

### 技术架构
项目采用经典的抽象工厂模式，通过抽象基类定义统一接口，具体的图形 API 实现继承这些抽象类。

```
Abstract Layer (抽象层)
    ↓
DirectX12 Implementation | Vulkan Implementation | Metal Implementation
    ↓                         ↓                      ↓
Native API Calls         Native API Calls      Native API Calls
```

## 项目结构详细分析

### 目录结构
```
SharpGPU/
├── Abstract/          # 抽象接口层 (20个文件, ~2,450行代码)
├── Dx12/             # DirectX 12 实现 (20个文件, ~7,000行代码)
├── Vulkan/           # Vulkan 实现 (3个文件, 基础框架)
├── Metal/            # Metal 实现 (3个文件, 基础框架)
├── README.md         # 项目说明文档 (包含详细使用示例)
├── SharpGPU.csproj   # 项目文件 (.NET 9.0)
├── Directory.Build.props  # MSBuild 配置
├── LICENSE           # 开源许可证
└── .gitignore        # Git 忽略规则
```

### 代码统计
- **总文件数**: 46个 C# 文件
- **总代码量**: 约 10,000+ 行代码
- **抽象层**: 2,448 行代码（接口定义）
- **DirectX 12实现**: 6,978 行代码（完整实现）
- **Vulkan/Metal**: 基础框架代码（待完善）

### Abstract 抽象层文件详解

抽象层定义了整个图形 API 的接口契约，包含以下核心模块：

#### 1. 核心基础设施
- **RHIUtility.cs**: 枚举定义和工具函数
  - 定义了 43 个核心枚举类型，覆盖图形 API 的各个方面
  - 包括像素格式（35种）、缓冲区类型、管线状态、同步对象等
  - 支持 HDR 格式、压缩纹理（DXT/BC/ASTC）、多重采样
  - 提供平台无关的常量定义和类型转换函数

- **RHIInstance.cs**: 图形实例管理
  - 负责创建和管理图形设备实例
  - 支持多后端选择（DirectX12/Vulkan/Metal）
  - 设备枚举和初始化

- **RHIDevice.cs**: 图形设备抽象
  - 设备能力查询和管理
  - 资源创建工厂方法
  - 命令队列管理

#### 2. 资源管理模块
- **RHIBuffer.cs**: 缓冲区管理
  - 顶点缓冲区、索引缓冲区、常量缓冲区
  - 内存映射和数据传输
  - 多种存储模式支持

- **RHITexture.cs**: 纹理资源管理
  - 1D/2D/3D 纹理支持
  - 多重采样和多级渐变
  - 渲染目标和深度缓冲区

- **RHISampler.cs**: 采样器状态管理
  - 过滤模式和寻址模式
  - 各向异性过滤支持

- **RHIHeap.cs**: 内存堆管理
  - GPU 内存分配和管理
  - 不同内存类型支持

#### 3. 渲染管线模块
- **RHIPipeline.cs**: 管线状态对象
  - 光栅化管线、计算管线、光线追踪管线
  - 着色器绑定和状态配置
  - 管线缓存和优化

- **RHIFunction.cs**: 着色器函数管理
  - 顶点着色器、片段着色器、计算着色器
  - 任务着色器、网格着色器、光线追踪着色器
  - 函数库和动态链接

- **RHIResourceTable.cs**: 资源绑定表
  - 描述符集合管理
  - 纹理、缓冲区、采样器绑定
  - 动态资源更新

#### 4. 命令记录与执行
- **RHICommandQueue.cs**: 命令队列管理
  - 图形队列、计算队列、传输队列
  - 命令提交和同步
  - 队列优先级管理

- **RHICommandBuffer.cs**: 命令缓冲区
  - 命令记录和重放
  - 多线程命令录制支持
  - 命令缓冲区池化

- **RHICommandEncoder.cs**: 命令编码器
  - 传输编码器、计算编码器、光栅编码器、光线追踪编码器
  - 渲染通道管理
  - 调试组和性能标记

#### 5. 同步与查询
- **RHISynchronous.cs**: 同步对象
  - 栅栏（Fence）和信号量（Semaphore）
  - GPU-CPU 同步
  - 帧同步管理

- **RHIQuery.cs**: GPU 查询对象
  - 遮挡查询、时间戳查询、管线统计
  - 性能分析支持

#### 6. 显示与交换链
- **RHISwapChain.cs**: 交换链管理
  - 前后缓冲区交换
  - 垂直同步控制
  - 全屏/窗口模式切换

#### 7. 高级特性
- **RHIAccelStruct.cs**: 加速结构
  - 光线追踪加速结构（BLAS/TLAS）
  - 几何体实例化
  - 动态场景更新

- **RHIStorageQueue.cs**: 存储队列
  - 异步存储操作
  - 大数据传输优化

### DirectX 12 实现层文件详解

DirectX 12 实现层提供了对 Microsoft DirectX 12 API 的完整封装，是目前最完善的后端实现：

#### 核心实现类
- **Dx12Instance.cs**: DirectX 12 实例实现
  - DXGI 工厂创建和管理
  - 适配器枚举和功能检测
  - 调试层和 GPU 验证启用
  - 支持多 GPU 系统

- **Dx12Device.cs**: DirectX 12 设备实现
  - D3D12 设备创建和初始化
  - 硬件功能支持检测（网格着色器、光线追踪、VRS等）
  - 命令队列创建和管理
  - 描述符堆分配
  - 设备限制查询（包含 Dx12DeviceLimit 类）

- **Dx12Utility.cs**: DirectX 12 工具类
  - 描述符堆管理（Dx12DescriptorHeap 类）
  - 错误处理和 HRESULT 检查
  - 类型转换辅助函数
  - 内存对齐计算

#### 资源实现
- **Dx12Buffer.cs**: DirectX 12 缓冲区实现
- **Dx12Texture.cs**: DirectX 12 纹理实现
- **Dx12BufferView.cs**: 缓冲区视图实现
- **Dx12TextureView.cs**: 纹理视图实现

#### 管线和着色器
- **Dx12Pipeline.cs**: 管线状态对象实现
- **Dx12Function.cs**: 着色器函数实现
- **Dx12ResourceTable.cs**: 根签名和描述符表

#### 命令系统
- **Dx12CommandQueue.cs**: DirectX 12 命令队列
- **Dx12CommandBuffer.cs**: DirectX 12 命令列表包装
- **Dx12CommandEncoder.cs**: 各种编码器实现

### Vulkan 实现层

Vulkan 实现层目前包含基础框架：
- **VulkanInstance.cs**: Vulkan 实例管理
- **VulkanDevice.cs**: Vulkan 逻辑设备管理
- **VulkanUtility.cs**: Vulkan 工具函数

*注：Vulkan 实现尚未完整，仅有基础结构*

### Metal 实现层

Metal 实现层针对 Apple 平台：
- **MetalInstance.cs**: Metal 实例管理
- **MetalDevice.cs**: Metal 设备管理
- **MetalCommandQueue.cs**: Metal 命令队列

*注：Metal 实现也尚未完整*

## 核心概念与 API 设计

### 1. 设备和实例管理

```csharp
// 创建图形实例
var instanceDesc = new RHIInstanceDescriptor
{
    Backend = ERHIBackend.DirectX12,
    EnableDebugLayer = true,
    EnableValidatior = false
};
RHIInstance instance = RHIInstance.Create(instanceDesc);

// 获取设备
RHIDevice device = instance.GetDevice(0);

// 获取命令队列
RHICommandQueue graphicsQueue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0);
```

### 2. 资源创建和管理

```csharp
// 创建纹理
var textureDesc = new RHITextureDescriptor
{
    Extent = new uint3(1920, 1080, 1),
    MipCount = 1,
    Format = ERHIPixelFormat.R8G8B8A8_UNorm,
    UsageFlag = ERHITextureUsage.RenderTarget | ERHITextureUsage.ShaderResource,
    Dimension = ERHITextureDimension.Texture2D,
    StorageMode = ERHIStorageMode.GPULocal
};
RHITexture texture = device.CreateTexture(textureDesc);

// 创建缓冲区
var bufferDesc = new RHIBufferDescriptor
{
    ByteSize = 1024 * 1024,
    UsageFlag = ERHIBufferUsage.VertexBuffer,
    StorageMode = ERHIStorageMode.HostUpload
};
RHIBuffer buffer = device.CreateBuffer(bufferDesc);
```

### 3. 着色器和管线

```csharp
// 创建着色器函数
var functionDesc = new RHIFunctionDescriptor
{
    Type = ERHIFunctionType.Vertex,
    ByteSize = shaderByteCode.Length,
    ByteCode = shaderPtr,
    EntryName = "VSMain"
};
RHIFunction vertexFunction = device.CreateFunction(functionDesc);

// 创建光栅化管线
var pipelineDesc = new RHIRasterPipelineDescriptor
{
    VertexFunction = vertexFunction,
    FragmentFunction = fragmentFunction,
    PipelineLayout = pipelineLayout,
    RenderState = renderState
};
RHIRasterPipeline pipeline = device.CreateRasterPipeline(pipelineDesc);
```

### 4. 命令记录和执行

```csharp
// 创建命令缓冲区
RHICommandBuffer cmdBuffer = graphicsQueue.CreateCommandBuffer();

using (cmdBuffer.BeginScoped("RenderFrame"))
{
    // 资源屏障
    cmdBuffer.ResourceBarrier(RHIBarrier.Transition(texture, 
        ERHITextureState.Present, ERHITextureState.RenderTarget));
    
    // 开始渲染通道
    using (var encoder = cmdBuffer.BeginScopedRasterPass(renderPassDesc))
    {
        encoder.SetPipeline(pipeline);
        encoder.SetResourceTable(resourceTable, 0);
        encoder.SetVertexBuffer(vertexBuffer, 0, 0);
        encoder.Draw(3, 1, 0, 0);
    }
}

// 提交命令
graphicsQueue.Submit(cmdBuffer, fence);
```

### 5. 资源绑定表系统

SharpGPU 使用资源绑定表（Resource Table）来管理着色器资源绑定：

```csharp
// 创建资源表布局
var layoutElements = new RHIResourceTableLayoutElement[]
{
    new RHIResourceTableLayoutElement
    {
        Slot = 0,
        Type = ERHIBindType.Texture2D,
        Stage = ERHIShaderStage.Fragment
    },
    new RHIResourceTableLayoutElement
    {
        Slot = 1,
        Type = ERHIBindType.Sampler,
        Stage = ERHIShaderStage.Fragment
    }
};

var layoutDesc = new RHIResourceTableLayoutDescriptor
{
    Index = 0,
    Elements = layoutElements
};
RHIResourceTableLayout layout = device.CreateResourceTableLayout(layoutDesc);

// 创建资源表
var elements = new RHIResourceTableElement[]
{
    new RHIResourceTableElement { TextureView = textureView },
    new RHIResourceTableElement { Sampler = sampler }
};

var tableDesc = new RHIResourceTableDescriptor
{
    Layout = layout,
    Elements = elements
};
RHIResourceTable resourceTable = device.CreateResourceTable(tableDesc);
```

## 依赖关系分析

### 外部依赖包

项目使用了以下 NuGet 包：

#### Vulkan 相关
- `Evergine.Bindings.Vulkan` (2024.11.1.33)
- `Silk.NET.Vulkan` (2.22.0) 及其扩展包
- `Vortice.Vulkan` (1.9.9)

#### DirectX 相关
- `TerraFX.Interop.D3D12MemoryAllocator` (2.0.1.5)
- `SharpGen.Runtime` (2.2.0-beta)

#### 通用依赖
- `NUnit` (3.13.3) - 单元测试框架
- `System.Text.RegularExpressions` (4.3.1)

#### 内部依赖
- `Infinity.Core` - 核心基础库
- `SharpMetal` - Metal API 绑定

### 平台支持

项目支持以下平台：
- **Windows**: DirectX 12, Vulkan
- **macOS/iOS**: Metal, Vulkan
- **Linux/Android**: Vulkan

架构支持：x64, ARM64

## 特性矩阵

### 图形 API 功能支持

| 功能 | DirectX 12 | Vulkan | Metal |
|------|------------|--------|-------|
| 基础渲染 | ✅ | 🚧 | 🚧 |
| 计算着色器 | ✅ | 🚧 | 🚧 |
| 几何着色器 | ✅ | 🚧 | ❌ |
| 曲面细分 | ✅ | 🚧 | ❌ |
| 网格着色器 | ✅ | 🚧 | ❌ |
| 光线追踪 | ✅ | 🚧 | ❌ |
| 可变速率着色 | ✅ | 🚧 | ❌ |
| 多视图渲染 | ✅ | 🚧 | ✅ |
| 间接绘制 | ✅ | 🚧 | ✅ |
| GPU 查询 | ✅ | 🚧 | ✅ |
| 调试层 | ✅ | 🚧 | ✅ |

### 资源类型支持

| 资源类型 | 支持状态 | 说明 |
|----------|----------|------|
| 1D 纹理 | ✅ | 基础纹理类型 |
| 2D 纹理 | ✅ | 最常用纹理类型 |
| 3D 纹理 | ✅ | 体积纹理 |
| 立方体纹理 | ✅ | 环境映射 |
| 纹理数组 | ✅ | 批量纹理处理 |
| 多重采样纹理 | ✅ | 抗锯齿支持 |
| 压缩纹理 | ✅ | DXT, BC, ASTC 格式 |
| 结构化缓冲区 | ✅ | 高级缓冲区类型 |
| 常量缓冲区 | ✅ | 着色器参数 |
| 间接参数缓冲区 | ✅ | GPU 驱动渲染 |

## 使用最佳实践

### 1. 初始化顺序

```csharp
// 1. 创建实例
RHIInstance instance = RHIInstance.Create(instanceDesc);

// 2. 获取设备
RHIDevice device = instance.GetDevice(0);

// 3. 创建交换链
RHISwapChain swapChain = device.CreateSwapChain(swapChainDesc);

// 4. 创建同步对象
RHIFence fence = device.CreateFence();

// 5. 创建命令队列
RHICommandQueue queue = device.GetCommandQueue(ERHIPipelineType.Graphics, 0);
```

### 2. 资源生命周期管理

```csharp
// 所有 RHI 对象都继承自 Disposal，支持 using 语句
using var texture = device.CreateTexture(textureDesc);
using var pipeline = device.CreateRasterPipeline(pipelineDesc);

// 或者手动释放
texture.Release();
pipeline.Release();
```

### 3. 多线程命令录制

```csharp
// 在不同线程中录制命令
var tasks = new Task[]
{
    Task.Run(() => RecordRenderCommands(queue1)),
    Task.Run(() => RecordComputeCommands(queue2)),
    Task.Run(() => RecordTransferCommands(queue3))
};

Task.WaitAll(tasks);
```

### 4. 性能优化建议

- **管线状态缓存**: 重用管线状态对象
- **资源池化**: 复用命令缓冲区和临时资源
- **批量操作**: 合并小的绘制调用
- **异步上传**: 使用传输队列异步上传资源
- **GPU 同步**: 最小化 CPU-GPU 同步点

## 编译和构建

### 构建要求
- .NET 9.0 SDK（项目目标框架）
- Visual Studio 2022 或 JetBrains Rider
- Windows SDK（DirectX 12 支持）
- 支持的平台：x64、ARM64

### 项目依赖
项目依赖以下外部组件，但当前环境缺少部分依赖：
- `Infinity.Core` - 核心基础库（项目引用）
- `SharpMetal` - Metal API 绑定（项目引用）

### 编译配置
项目支持以下配置：
- Debug/Release 配置
- x64/ARM64 平台目标
- 启用不安全代码块（AllowUnsafeBlocks）

### 输出路径
- 二进制文件: `Binaries/Graphics/SharpGPU/[Platform]/[Configuration]/`
- 中间文件: `Intermediate/Graphics/SharpGPU/`

### 构建说明
当前项目需要完整的开发环境才能成功构建，包括：
1. 完整的 Infinity 框架依赖
2. .NET 9.0 运行时
3. 相应平台的 GPU 驱动程序和 SDK

## 发展路线图

### 现代 C# 特性使用
- **不安全代码**: 使用 `unsafe` 关键字直接操作指针，提高性能
- **值类型优化**: 大量使用 `struct` 减少 GC 压力
- **Memory<T>**: 现代内存管理 API
- **Disposal 模式**: 统一的资源释放管理
- **编译时常量**: 使用 `const` 和 `readonly` 优化性能

### 性能优化设计
- **零拷贝设计**: 直接映射到底层 API 结构
- **栈分配优化**: 优先使用值类型避免堆分配
- **批量操作**: 支持批量资源操作减少 API 调用
- **缓存友好**: 数据结构设计考虑缓存局部性

### 跨平台抽象策略
项目通过以下策略实现跨平台：
1. **统一枚举系统**: 所有平台共享相同的枚举定义
2. **抽象工厂模式**: 运行时选择具体实现
3. **功能特性查询**: 动态检测平台支持的功能
4. **条件编译**: 使用平台特定的条件编译指令

### 短期目标
- [ ] 完善 Vulkan 实现
- [ ] 完善 Metal 实现
- [ ] 添加更多单元测试
- [ ] 性能基准测试

### 中期目标
- [ ] 光线追踪管线完善
- [ ] 网格着色器支持
- [ ] GPU 调试工具集成
- [ ] 内存分配器优化

### 长期目标
- [ ] WebGPU 后端支持
- [ ] 移动平台优化
- [ ] 云渲染支持
- [ ] 机器学习工作负载支持

## 总结

SharpGPU 是一个雄心勃勃的项目，旨在为 .NET 生态系统提供现代化的图形 API 抽象层。虽然目前主要实现了 DirectX 12 后端，但其架构设计为多后端支持奠定了坚实基础。项目采用了现代 C# 特性，提供了类型安全、内存安全的图形编程接口。

该项目的主要优势包括：
1. **统一的 API 设计**: 隐藏了不同图形 API 的复杂性
2. **现代化架构**: 支持最新的 GPU 特性
3. **高性能**: 直接映射到底层 API
4. **跨平台**: 支持 Windows、macOS、Linux
5. **开发者友好**: 提供了丰富的调试和分析工具

对于希望在 .NET 平台上进行高性能图形开发的开发者来说，SharpGPU 提供了一个强大而灵活的基础平台。