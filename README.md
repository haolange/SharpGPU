# SharpGPU

把现代图形 API 收成一套显式、失败即关闭的硬件抽象。

DirectX 12 × Vulkan × Metal

[English](README.en.md) · [设计与架构](#设计与架构) · [问题反馈](https://github.com/haolange/SharpGPU/issues)

## 这是硬件抽象，不是渲染器

SharpGPU 是 .NET 10 上的 DirectX 12、Vulkan 与 Metal 硬件抽象。公开面是一组显式机制：设备、资源、不可变管线、绑定表、命令编码器、队列和呈现。Pass 拓扑、屏障推导、内存别名和瞬态资源寿命留在调用方，通常是一层 Render Graph。

着色器以编译后的字节码进入。SharpGPU 不带着色器编译器。交换链的获取和呈现只返回 `ERHISwapChainStatus`，重建留给调用方。不支持的工厂抛出 `NotSupportedException`，不换一条执行路径顶上。

本库维护 RHI、三个后端，以及这些后端依赖的 Vortice 绑定。[samples/ComputeAndDraw](samples/ComputeAndDraw) 是无窗口的计算加光栅示例。[docs/SharpGPU/QuickStart.md](docs/SharpGPU/QuickStart.md) 是最短的传输 Pass，并演示可选的 `SharpGPU.Scopes` 与 `SharpGPU.Builders`。底层契约在 `SharpGPU` 命名空间。

## 设计与架构

### 后端

`ERHIBackend` 为 `Metal`、`Vulkan`、`DirectX12` 或 `Pending`。没有 Auto。`RHIInstance.GetBackendByPlatform` 只给建议：Windows 上默认 DirectX 12，`bForceVulkan` 为真时改用 Vulkan；macOS 与 iOS 为 Metal；Linux 与 Android 为 Vulkan。调用方把这个值写入 `RHIInstanceDescriptor`。`Pending` 不能用来创建实例。

`RHIInstance.IsBackendSupported` 只检查操作系统，不探测驱动或原生库。驱动和功能是否真能用，要等设备创建以及读取 `RHIDevice.Capabilities`。未定义 `SHARPGPU_ENABLE_DX12` 的构建没有 DirectX 12 路径。

`RHIInstanceDescriptor` 还带调试层、校验层、原生表面种类，以及请求的图形、计算、传输队列数量。一个 `RHIInstance` 拥有枚举到的全部设备。`GetDevice` 按索引取设备。队列是 `RHIDevice.GetCommandQueue(ERHIPipelineType, index)`。

`ERHIDeviceState` 为 `Unknown`、`Operational`、`Lost`、`Removed`、`Reset`。`Lost` 是超时或设备丢失后的恢复。`Removed` 是物理适配器消失。`Reset` 表示此前的资源已经失效，需要重新创建。原生失败表现为 `RHIException`，其中有 `ERHIErrorCode`、后端、原生代码与消息，以及设备状态。

`RHIAdapterIdentity` 在后端确实提供时保存 LUID 和设备 UUID。`RequireMatch` 用来断言「这块交换链或共享资源必须落在那张适配器上」。

### 资源

GPU 内存是 Buffer 或 Texture。`RHIBufferDescriptor` 包含字节大小、元素格式、用途和 `ERHIStorageMode`。`RHITextureDescriptor` 另有 mip 数量、`uint3` 范围、像素格式、采样数和维度。`ERHIStorageMode` 为 `GPULocal`、`HostUpload`、`GPUUpload`、`Readback` 或 `Memoryless`。

`CreateBuffer` 与 `CreateTexture` 创建 Committed 资源。`CreatePlacedBuffer` 与 `CreatePlacedTexture` 把资源放进由 `RHIHeapDescription` 创建的 `RHIHeap`。`ERHIHeapType` 选择堆的种类（`Default`、`BuffersOnly`、`TexturesOnly`、`RenderTarget`），不表示 CPU/GPU 的传输方向；方向由 `ERHIStorageMode` 表示。稀疏纹理和缓冲、驻留与预算查询是各自的内存能力，先确认能力可用再调用。

视图是调用方已有资源上的一段范围加一种访问方式。Buffer 使用 `CreateBufferView`。Texture 使用描述 mip 与数组范围的视图描述符。Tensor 和函数库有自己的视图类型。这些视图不是后端的 descriptor heap、pool 或 argument buffer pool，后者留在后端内部。

采样器要么是 `RHISampler` 对象，要么是嵌进管线布局的静态采样器。Sampler Feedback 贴图在创建时就和目标纹理配对，编码器之后不能改配对。

### 着色器与管线

`RHIFunctionDescriptor` 指向字节码（`ERHIShaderPayloadKind`：`Dxil`、`SpirV`、`MslSource` 或 `MetalLibrary`）、入口名和 `ERHIFunctionType`。函数库把一份载荷编译一次，再由 `CreateFunction` 选取入口。光线追踪和工作图消费函数库。光栅与计算对函数库的复用是一项能力，部分后端不可用。

管线创建后不可变。

- `RHIRasterPipelineDescriptor` 锁定采样数、颜色与深度格式、渲染状态、可选的片段函数、管线布局和 `RHIPrimitiveAssemblerDescriptor`。装配器要么是顶点函数加顶点布局，要么是网格函数加可选的 task 函数。可选的 `RHIRasterAttachmentShaderAbiClaim` 检查着色器输出和 Pass 附件是同一份契约。
- `RHIComputePipelineDescriptor` 锁定线程组大小、计算函数和布局。组大小写在描述符里，Metal、DirectX 12 和 Vulkan 读同一处。
- 光线追踪、工作图和机器学习各有自己的管线类型。机器学习执行的是二进制管线：`RHIMLBinary` 变成 `RHIMLPipeline`，再是绑定表和 `RHIMLEncoder`。没有公开的运行时算子图 API。

`RHIPipelineCache` 导入和导出一块不透明字节。调用方自己保存。驱动或 GPU 变化会使旧条目不兼容；导入会报告这一点，而不是假装命中。

### 绑定

`RHIPipelineLayoutDescriptor` 给出绑定表布局、Push Constant 大小、可选静态采样器、是否为光线追踪的 local signature，以及是否包含顶点布局。录制时编码器绑定 `RHIBindingTable`。表有 `Count`，并用 `SetBindElement(..., arrayIndex)` 表达有限的 bindless 范围。

DirectX 12 的 descriptor heap、Vulkan 的 descriptor pool 和 Metal 的 view pool 是后端存储。它们不是公开类型，运行时也不把 shader-visible heap 再扩一层。

### 命令录制

`RHICommandQueue.CreateCommandBuffer` 得到命令缓冲。`Begin(string name)` 开始录制，名字是调试标签。`End` 结束录制，之后可以提交。

一个命令缓冲同时只有一个活动编码器。对应的 Begin 选定它，匹配的 `End*Pass` 结束它。六种编码器是：

| Pass | Begin / End |
|---|---|
| 传输 | `BeginTransferPass` / `EndTransferPass` |
| 计算 | `BeginComputePass` / `EndComputePass` |
| 光线追踪 | `BeginRaytracingPass` / `EndRaytracingPass` |
| 光栅 | `BeginRasterPass` / `EndRasterPass` |
| 机器学习 | `BeginMLPass` / `EndMLPass` |
| 工作图 | `BeginWorkGraphPass` / `EndWorkGraphPass` |

光栅录制绘制、间接绘制、视口和绑定。计算录制 Dispatch 与间接 Dispatch。传输录制拷贝和 Blit。每种编码器都录制 `RHIBarrier`：全局、Buffer 或 Texture。阶段与访问掩码成对使用 Before/After。纹理屏障另带 `ERHITextureLayout`（13 个值，含拷贝、Resolve、Present 和 `Common`）。源队列或目标队列非空时，表示跨队列所有权转移。

间接命令缓冲是另一项能力，用来并行录制绘制流，再在编码器里 `Execute`。Vulkan 不实现这份契约。

### 提交与同步

`RHICommandQueue.Submit` 接收 `RHIQueueSubmitDescriptor`：命令缓冲、带阶段掩码的信号量等待、要发出的信号量，以及可选的完成栅栏。空提交抛出 `ArgumentException`（「A queue submission must contain work or a synchronization operation.」）。队列在原生提交失败时回滚已经预约的信号量和栅栏。预约是内部实现。调用方只填描述符，不自己调用 reserve、commit 或 rollback。

`RHIFence` 是 CPU 与 GPU 之间的信号。`Wait` 返回 `ERHIFenceStatus`（`Success`、`NotReady` 或 `Undefined`），不返回布尔值。信号完成后可以 `Reset`。`RHISemaphore` 是 GPU 与 GPU 之间的信号，放在提交描述符里。

时间戳、遮挡和管线统计从 `RHIQuery` 读取，要等 GPU 写完。结果未就绪时 `TryGetTimestamp`、`TryGetOcclusion` 和 `TryGetPipelineStatistics` 返回 false。校准时间戳是 `QueryClockCalibration`，仅当同步能力报告可用时存在。没有用 CPU 时钟冒充的路径。

### 呈现

`RHISwapChainDescriptor` 给出窗口句柄、`ERHINativeSurfaceKind`（`Win32Hwnd`、`AppKitNsWindow`、`X11Window`、`WaylandSurface`、`UIKitUiWindow`、`AndroidNativeWindow` 或 `Headless`）、范围、缓冲数量、格式、呈现模式、帧率、`FrameBufferOnly`、表面代际和呈现队列。

获取和呈现使用各自的描述符，并返回 `ERHISwapChainStatus`：`Success`、`NotReady`、`Timeout`、`Occluded`、`Suboptimal`、`OutOfDate`、`SurfaceLost` 或 `DeviceLost`。`Suboptimal` 仍可呈现。`OutOfDate`、`SurfaceLost` 和 `DeviceLost` 需要调用方重建交换链或设备。HAL 不代做这次重建，也不自行把队列闲置下来。

### 能力

`RHIDevice.Capabilities` 有 14 个域。每个域是一个小对象，带有等级、策略、来源和不可用原因：

`Raster`、`Binding`、`Synchronization`、`Memory`、`Storage`、`PipelineCache`、`Presentation`、`RayTracing`、`Mesh`、`MachineLearning`、`WorkGraph`、`IndirectCommandBuffer`、`Compute`、`FunctionLibrary`。

`ERHICapabilityTier` 从 `Unavailable` 到 `Tier4`。某一级表示什么，由 [能力矩阵](docs/SharpGPU/FeatureMatrix.md) 按域定义，不是一根统一的梯子。`ERHICapabilityStrategy` 说明后端如何实现（`CoreApi`、`NativeExtension`、`NativeSpecialized`、`NativeLibrary`）。`ERHICapabilityProbeKind` 说明这个答案是怎么得到的。

`RHIDeviceLimit` 放每次上传和派发都会用到的对齐与上限。更宽的数值限制走 `RHICapabilityLimits.TryGetValue`。查不到键，表示这个后端不报告该项。

域等级回答不了的精确问题另有查询：`QueryFormatSupport`、`QueryResolveSupport`、`QueryRasterAttachmentSupport`，以及协作矩阵配置查询。光线追踪是几项分面（管线、内联、不透明度微映射、运动），不是一个等级数字。存储队列、可变速率着色、网格着色器、工作图和 Sampler Feedback 同样是可选能力：矩阵写出类型名，没有原生路径就抛异常。

### 资格

能力描述的是刚探测过的那台设备。`Passed`、`Failed`、`Unverified` 和 `BLOCKED_PLATFORM` 描述的是在匹配主机上跑过的场景。这些词只出现在能力矩阵和 [docs/VERIFICATION.md](docs/VERIFICATION.md)。原生探测成功不等于场景资格通过。哪些主机已有报告、哪些仍被挡住，以矩阵为准。

## 开始使用

- 安装 [global.json](global.json) 选定的 .NET SDK。运行时项目目标是 .NET 10；编译器生成器保留各自声明的构建目标。
- 源码开发时，把 [stack.local.props.example](stack.local.props.example) 复制为 `stack.local.props` 并改检出路径。模板假定 InfinityStack 仓库彼此相邻；所选构建用到的依赖在即可。产品测试可能还需要更多同伴仓库。
- 构建、测试、打包和平台命令以 [docs/VERIFICATION.md](docs/VERIFICATION.md) 为准。从 [独立示例](samples/ComputeAndDraw) 开始。
- 以包消费时使用 `StackReferenceMode=Package`，并显式提供含匹配版本的源。Source 与 Package 作用于整张依赖图。包是否可获取取决于已发布的资产；本文不假定 nuget.org 上已有发行。

## 仓库布局

`src/` 是运行时，`samples/` 是可运行负载，`docs/` 是能力矩阵、快速开始和验证，`eng/` 是验证自动化。测试和工具各自有目录。[AGENTS.md](AGENTS.md) 定义贡献规则和产品边界。

构建输出、隔离的包缓存和原始运行证据放在被忽略的 `artifacts/`。提交源码、已审阅的 lock 和可移植配置模板；机器路径留在 `stack.local.props`。历史运行摘要不表示那些一次性输出目录还在。

原生输入和哈希记在 [native/assets.json](native/assets.json)；NuGet 原生输入由构建还原。设备支持必须在运行时检查。

## 许可证

[MPL-2.0](LICENSE)。既有版权声明和第三方声明留在各自文件。[docs/provenance](docs/provenance) 保留提取记录和继承来的声明。
