| 字段 | 值 |
|---|---|
| 文档ID | INV-20260830-SHARPGPU-FEATURE-AUDIT |
| 标题 | SharpGPU 2026 三平台 Feature Audit |
| 类型 | Investigation |
| 状态 | Accepted |
| 维护者 | CGBull |
| 创建日期 | 2026-08-30 |
| 最后更新 | 2026-08-30 |
| 相关链接 | `ADR-0054` · `ADR-0064` · `RFC-0021` · `TASK-20260830-sharpgpu-raster-attachment-clean-break` · `CANONICAL-VERIFICATION` |

# SharpGPU 2026 三平台 Feature Audit

审计日期：2026-08-30（Asia/Shanghai）  
仓库快照：`main`，commit `789b898bcc3619d7729a000c869d200aad1b2232`  
实现快照：上述 commit 之上的 ADR-0064 当前工作树  
审计对象：`Engine/Source/Runtime/Graphics/SharpGPU` 的 Abstract、Dx12、Vulkan、Metal 后端，SharpShader attachment 适配层，以及 `SharpGPU.Conformance.Tests`。  
证据规则：本文是 Investigation，不是当前规则或验证命令的权威来源；平台状态最终以 `docs/Canonical/VERIFICATION.md` 为准。仓库源码/测试只证明“当前实现”；Microsoft、Apple、Khronos 官方一手资料只证明“平台 API capability”；历史 JSON/Markdown 只作为历史材料。`Supported` 必须同时满足 probe、native lowering、fail-closed factory/encoder 与适用 qualified evidence，不能由类型、枚举或硬件 tier 单独推出。

## 结论先行

SharpGPU 已经是一个有真实 lowering 的现代 RHI core，而不是三套 API 的薄重命名：encoder 按 transfer/compute/ray-tracing/raster/work-graph 分域，资源、binding table、pipeline、query、fence/semaphore、placed/sparse memory、RT acceleration structure 与 typed capability 的边界清楚；未实现路径大多以 capability unavailable + factory/encoder fail closed 收口。DX12 Work Graph/RT/DirectStorage、Vulkan RT/sync2/dynamic local read/sparse image、Metal MTLIO/Metal4 barrier/ML/RT 都有 native path 或运行时 probe。

但以 2026 年的 DX12、Metal 4 family matrix、Vulkan 1.4 + Roadmap 2026 为基准，SharpGPU 还不能称为“三平台 feature 完整”。当前 P0 真相问题是：DX12 VRS command path 存在但 capability 被硬编码为 true；Vulkan/Metal `SetShadingRate` 仍是 no-op；DX12 mesh command 存在但 public pipeline capability 按设计保持 unavailable；Metal mesh 尚未形成 public supported route；Sampler Feedback Map、video、cooperative matrix、external interop 没有公共域；sparse 3D 缺跨平台 GPU 证据；历史 feature report 早于当前源码。

ADR-0064 已完成第一个 clean break：公共 raster attachment 只保留 Inputs/Outputs；同 phase 交集唯一表示 ordered framebuffer fetch/write；hardware blend 正交并按 format/sample/layer/blend 做 exact query；SharpGPU 拥有可查询、版本化的 Raw shader ABI；ROV/ROAA/ROG 全部私有。这个方向应该延续为“公共语义 + typed capability domain + backend-private lowering”，而不是把所有平台 feature 压成一个 bool，也不是把某个平台对象直接抬进 core。

## 版本与证据边界

| 平台 | 2026 审计目标 | 2026 预览或厂商扩展 | 对 SharpGPU 的含义 |
|---|---|---|---|
| DX12 | Agility 1.619 retail；SM 6.9、DXR 1.2 OMM/SER、Work Graph、Tier 4 tiled、GPU upload heaps | Agility 1.721 preview；partial/generic programs、GUID texture layout、depth UAV；SM 6.10/Work Lists/Raytracing2 尚非稳定公共基线 | 代码当前引用 `Microsoft.Direct3D.D3D12 1.619.3`，必须区分 retail 与 preview，不能把 1.721 预览能力写进 stable RHI |
| Metal | Metal 4 programming model 从 Apple family 7 开始；具体对象/能力仍需 runtime family probe，Apple 6/A14 不是当前 Metal4 backend 基线 | Metal 4 的专用编译器、flexible PSO、pipeline dataset；Apple 9/10 的 address-driven AS、intersection function buffer、sampler reduction 等 | Metal 不是“一个版本一个能力集”；`MTLDevice` family、feature table、对象类型（MTL/MTL4）共同决定可用性 |
| Vulkan | Vulkan 1.4 target API + Roadmap 2026 target profile | `VK_EXT_device_generated_commands`、`VK_AMDX_shader_enqueue` execution graph、cooperative matrix、shader object/pipeline binary 等扩展组合 | Roadmap 2026 是面向新出货中高端设备的目标 milestone，不是所有 Vulkan 设备的普遍稳定基线；core、promoted KHR/EXT、Roadmap required extension 与 vendor extension 必须分层；`VK_KHR_cooperative_matrix` 截至 1.4.351 仍是 extension-only，不是 1.4 core |

官方基线说明：Microsoft 的 Agility release 与 DirectX Specs 区分 retail、preview/proposal 能力；Apple 的 Metal capability/feature-set 表按 Metal/Apple family 与具体对象类型分层；Khronos Roadmap 2026 要求一组 core + extension capability，但“路线图必需”不等于“提升为 core”。详见文末官方引用。

## 把用户点名的 feature 定义清楚

| Feature | 真正的语义 | DX12 | Vulkan | Metal | SharpGPU 应如何表达 |
|---|---|---|---|---|---|
| RT pipeline | ray-generation/miss/hit/callable group、shader table 与 AS traversal | DXR state object + `DispatchRays` | KHR RT pipeline + `vkCmdTraceRaysKHR` | AS + render/compute intersector；没有同形的独立 ray-generation pipeline object | `RayTracing.Pipeline` 独立于 inline query，并查询 tier、shader-table limits、motion/OMM/SER 等可选项 |
| Inline RT / ray query | 在 graphics/compute shader 内维护 traversal query，不启动额外 ray shader pipeline | DXR 1.1 `RayQuery` | `VK_KHR_ray_query` | intersection query/intersector，render 支持受 family 限制 | `RayTracing.Inline` 独立报告 stage、flags、AS/OMM/motion 约束 |
| Work Graph | shader node 产生后继 work，由 runtime 管理 backing memory 与调度 | 原生 Work Graph + `DispatchGraph` | Vulkan core/KHR 无跨厂商等价物；`VK_AMDX_shader_enqueue` 是 AMD-specific execution-graph analogue | 无真正等价物 | 保留 DX12-only tiered domain；AMDX、DGC、ICB 分别建模，绝不冒充 portable Work Graph |
| Device-generated / indirect commands | GPU 写 command stream/token，随后由 API 执行 | `ExecuteIndirect`；与 Work Graph 不是同一层 | `VK_EXT_device_generated_commands` | `MTLIndirectCommandBuffer` | 独立 `DeviceGeneratedCommands` domain，查询 command/token/domain/preprocess；普通 indirect 仍是 common core |
| Render pass / subpass / local read | attachment load/store、phase、tile-local fetch 与 phase dependency | `BeginRenderPass` 固定 attachment；无 Vulkan subpass/local-read 同构 | RenderPass2/subpass；dynamic rendering core 1.3；dynamic local read core 1.4（具体 depth/MSAA 看 properties） | render-pass descriptor/encoder；无 subpass object；programmable blending、color attachment mapping/local-read route 与 imageblock 均按 family/object 查询 | 公共 Inputs/Outputs + phase；native render-pass 形态留在 private planner |
| Ordered attachment read/write | 重叠 fragment 对同像素 attachment 的 fetch/RMW 顺序 | attachment fetch 不存在；用 private ROV/UAV 精确模拟 | input attachment 或 dynamic-rendering-local-read +（需要时）ROAA ordering；depth/MSAA 受 local-read properties 限制 | programmable blending / color attachment mapping / local-read route；framebuffer fetch 是公共语义类比，canonical path 保持普通 color output | 同 phase `Inputs ∩ Outputs` 唯一表示；不公开 ROV/ROAA/ROG/feedback-loop；exact blend 组合不成立就失败 |
| Sampler Feedback Map | 记录 texture sampling 的 mip/region，服务 streaming/texture-space shading | 原生 feedback texture + map/resolve/decode | 没有标准同构 | 没有标准同构；query texture LOD 不是 feedback map | 独立 D3D12-only `SamplerFeedbackMap` optional domain；与 attachment ABI 完全分离 |
| VRS | per-draw、per-primitive 或 image/map 控制 fragment rate，并包含 combiner/sample 规则 | Tier 1/2 + shading-rate image | KHR fragment shading rate三来源 | rasterization-rate map，设备可选择不低于请求的 rate | typed sources/limits/combiner，而非单一 bool 或可能 no-op 的 setter |
| Mesh/task/object shader | task/object 产生 mesh work，mesh stage 产生 primitives | amplification + mesh，`DispatchMesh` | EXT task/mesh | object + mesh，family/indirect route 分层 | `Mesh` capability 必须同时绑定 pipeline factory、dispatch/indirect route 与 qualified evidence |
| GPU file I/O | 文件到 GPU resource 的异步 queue/batch/sync | DirectStorage 是 D3D12 之外的独立 API，可目标 VRAM | 无 DirectStorage-like file queue | `MTLIOCommandQueue/Buffer/FileHandle` | 独立平台 service/domain；不能把 transfer queue 伪装成 file I/O |
| GPU decompression / host copy | GPU memory-to-memory 解压或 host/image optimized copy | DirectStorage GDeflate 等 | `VK_EXT_memory_decompression` 仅 memory-to-memory；host image copy 是另一能力 | MTLIO 的加载/解压能力按对象与格式查询 | 拆 `StorageIO`、`GpuDecompression`、`HostImageCopy`、`ResidencyUpdate` |
| Sparse 2D/3D / residency | 虚拟资源与物理 tile 分离，维度/样本/format 分别保证 residency | reserved/tiled resources；Tier 3 起 volume textures | core sparse binding；buffer/2D/3D feature 分开 | automatic-heap sparse color textures 从 Apple6；placement sparse 在 Apple7 部分设备、Apple8 全部设备；当前 SharpGPU 另要求 Metal4 + placement runtime probe | 分离 buffer/2D/3D/MSAA/residency/tile geometry，禁止一个 bool 代表全部 |
| Bindless / descriptor memory | 大规模动态索引 descriptor 与 GPU-visible binding memory | shader-visible descriptor heaps/root tables | descriptor indexing；descriptor buffer；新 descriptor heap 扩展 | argument buffers；Metal 4 argument tables | 公共 BindingTable + typed limits；descriptor-buffer/heap/argument-table 是 strategy，不是假同构对象 |
| Barriers / synchronization | execution、memory visibility、layout/ownership、host/device event | Enhanced Barriers；classic ResourceBarrier 仍是一等 API 路径 | synchronization2 core 1.3 | memory/command barriers、fence/event/shared event | 公共 state/dependency；后端选择精确 primitive，禁止 silent no-op |
| Video | codec session/queue、bitstream、DPB、encode/decode parameters | D3D12 Video | KHR video queue + codec extensions | VideoToolbox，不属于 Metal | 独立 media service；不要污染 graphics RHI core |
| Cooperative matrix / ML | subgroup/tile matrix primitive或完整 tensor/graph runtime | cooperative vector仍处于 proposal/in-development；DirectML 是独立 runtime | `VK_KHR_cooperative_matrix` 为 ratified extension-only；不是 Vulkan 1.4 core | Metal 4 ML encoder/tensor + MSL cooperative tensor；Core ML 是更高层 | matrix primitive、tensor command 与 ML graph 三个 domain，不能以 `ML=true` 混写 |

## 当前仓库实现矩阵

状态含义：`实装` 表示有公共入口、真实 native lowering、正确 capability gate 和适用 evidence；`部分` 表示缺少其中至少一项；`不可用` 表示 capability/factory 明确 fail closed；`缺失` 表示尚无公共 domain。以下状态针对 ADR-0064 工作树，不能由平台官方 capability 反推。

| 能力 | Abstract | DX12 | Vulkan | Metal | 当前判断 |
|---|---|---|---|---|---|
| RT pipeline / AS | 有 | native pipeline/AS/dispatch | KHR pipeline/AS/trace | AS + RHI pipeline abstraction | 部分实装：基础 path 存在；motion/OMM/SER 与细粒度 limits 不完整 |
| Inline RT | 有独立 capability | DXR 1.1 probe | `VK_KHR_ray_query` probe | 与 RT family probe 耦合 | 部分：Metal 缺独立 inline capability/qualified path |
| Work Graph | 有 domain | native command-list-10/pipeline/backing memory + qualified test | unavailable/fail closed | unavailable/fail closed | DX12 optional 实装；跨平台不可用是正确结果 |
| Device-generated commands | indirect layout | ExecuteIndirect 覆盖 draw/dispatch/mesh/RT | 普通 indirect；无 EXT DGC lowering | 普通 indirect；layout-driven ICB unavailable | 部分：不能把 common indirect 报成 DGC/ICB 完整支持 |
| Raster attachment contract | Inputs/Outputs + exact query + Raw ABI | input-only SRV；overlap private ROV | dynamic local read/RenderPass2 + private ROAA；core 1.4 仍不保证 depth/stencil 或 MSAA，必须查 local-read properties | programmable blend/color-attachment mapping/local-read route，普通 color output | 源码/portable 已实装；本 clean break 仍待各 matching host GPU 复验 |
| Hardware blend composition | 原状态进入 exact query | overlap + blend/A2C/非 All write mask fail closed | native exact combination保持 blend | native exact combination保持 blend | portable contract 与 DX12 qualified gate 已实现，但当前 DX12 rerun 受 RTX 5090 baseline 阻塞；Vulkan/Metal 待 matching-host overlap+blend qualification；所有后端禁止静默关闭或改写 hardware blend |
| Sampler Feedback Map | 无 domain | 未暴露 map/resolve/decode | 无标准同构 | 无标准同构 | 缺失；必须保持独立 optional domain |
| VRS | 有 enum/combiner/API | native command存在，但 capability 硬编码 true | feature probe存在，setter no-op | capability unavailable，setter为空 | P0 correctness：三后端都不能按当前 public contract 记为完整 supported |
| Mesh shader | 有 descriptor/dispatch skeleton | native command存在，public capability刻意 unavailable | EXT probe + task/mesh command path | MTL4 command存在，public capability unavailable | 部分；需 pipeline factory + indirect + GPU evidence 后才能开放 |
| GPU file I/O | `StorageQueue` | DirectStorage native + qualified | unavailable/fail closed | MTLIO native/runtime-probed，当前无 Metal-specific qualified evidence | DX12 实装；Metal 部分；Vulkan 无同构是正确结果，命名仍应细分 |
| Sparse 2D/3D | 有 texture API | reserved textures/tile mapping/residency；native Tier4 当前折叠为 RHI Tier3，Tier4-specific 语义/资格未独立公开 | sparse image 2D/3D feature与mapping | sparse/placement texture mapping | 部分：机制存在，2D/3D/MSAA/residency/Tier4 需要拆分 capability 与 GPU evidence |
| Sparse buffer | capability skeleton | 未形成独立可证明 route | unavailable | unavailable | 不得由 sparse texture 推导 |
| Bindless/indexing | BindingTable + typed capabilities | descriptor table/indexing | descriptor indexing 部分 probe；无 descriptor buffer/heap lowering | MTL4 argument table strategy | 部分；strategy 与 limits 需要继续分层 |
| Barriers/sync | state/barrier/fence/semaphore | Enhanced + classic barrier path | synchronization2 + core barrier path | Metal4 command/memory barrier path | 基础实装；timeline/shared event/external ownership 仍需公共查询 |
| Pipeline cache | 有 factory | native | native | fail closed（当前 blob contract 不同构） | 部分；应引入 typed archive/binary strategy 而非伪 portable blob |
| Video | 无 | 未暴露 D3D12 Video | 未暴露 KHR Video | 未接 VideoToolbox | 缺失但应归 media service，而非 graphics core |
| Cooperative matrix / tensor / ML | ML/tensor domain | DirectML，不是 cooperative matrix | ML unavailable；未暴露 KHR cooperative matrix | Metal4 ML/tensor + qualified | 部分；matrix primitive、tensor command、graph runtime 尚未分域 |
| External interop | 无 | shared heap/fence未公开 | external memory/semaphore未公开 | shared texture/event/IOSurface未公开 | P1 缺口 |
| Exact attachment format support | `QueryRasterAttachmentSupport` | FORMAT_SUPPORT/shape probe | format/image-format properties | runtime texture probe + blendable set | ADR-0064 已实装；通用 buffer/texture format/usage 查询仍缺 |
| Fault/time/counters | 基础 query | PIX/debug hooks，未统一 DRED/calibration | validation/debug collector，未统一 calibrated time | runtime probes，未统一 counter API | P1 diagnostics |

主要源码证据：公共 exact query/Raw ABI 在 [RHIDevice.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Abstract/RHIDevice.cs) 与 [RHIAttachmentShaderAbi.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Abstract/RHIAttachmentShaderAbi.cs)；attachment planner 在 [RHICommandEncoder.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Abstract/RHICommandEncoder.cs)；三后端 probe/lowering 分别在 [Dx12Device.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Dx12/Dx12Device.cs)、[VulkanDevice.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Vulkan/VulkanDevice.cs)、[MetalDevice.cs](D:/Engines/InfinityBrowser/Engine/Source/Runtime/Graphics/SharpGPU/Metal/MetalDevice.cs) 及对应 command encoder；contract evidence 在 [RasterPassPlannerContractTests.cs](D:/Engines/InfinityBrowser/Engine/Source/Developer/Tests/SharpGPU.Conformance.Tests/RasterPassPlannerContractTests.cs) 与 SharpShader attachment tests。平台支持状态最终只由 `docs/Canonical/VERIFICATION.md` 的 gate 决定。

## 明确遗漏，但不应全部塞进 portable core

| 类别 | 建议增加的语义 | 优先级 | 为什么 |
|---|---|---:|---|
| 必须进入 graphics core | 通用 `FormatSupport`（resource kind/usage/format/sample/tiling）、sparse 维度/limits、VRS sources/combiner、timeline/shared-event semantic、external handle descriptors | P0/P1 | 创建前必须可判断合法组合；否则 capability 只是宣传值 |
| 公共 optional domain | Sampler Feedback Map、device-generated commands、pipeline binary/archive、cooperative matrix、RT motion/OMM/SER | P1/P2 | API 可以公共且 portable-queryable，但各平台可明确 unavailable；不得伪造最低公分母 fallback |
| 强烈建议 diagnostics | calibrated timestamps、device fault/DRED-like report、counter set/resolve、debug labels/markers、resource aliasing contract、queue/async limits | P1 | 生产引擎需要跨 CPU/GPU 时间、故障归因与调度证据 |
| 独立 service/framework | GPU file I/O、video codec、ML graph/tensor runtime、MetalFX/upscaler、optical flow、asset conditioning | 另层 | 它们可消费 SharpGPU resource/sync interop，但不应扩张 graphics core 的所有权 |

## 推荐的 capability 形状

不要继续添加一串 `bool`。每个能力至少应带 `Tier`、`Strategy`、`ProbeKind`、`Limits`、`UnavailableReason`，并把“硬件可用”与“SharpGPU 已有 lowering/qualified”分开。建议的最小结构如下：

```text
DeviceCapabilities
  Raster
    FramebufferReadWrite { ExactQuery(format, samples, layered, blend, alphaToCoverage) }
    SamplerFeedbackMap { Resolve, Decode, TextureStreaming, TextureSpaceShading }
    VariableRateShading { PerDraw, PerPrimitive, Attachment, Combiner, SampleRules }
    Subpass { InputAttachment, LocalRead, FramebufferFetch, DynamicRendering }
  RayTracing
    Pipeline { Tier, Motion, OMM, SER, ShaderTableLimits }
    Inline { RayQuery, OMM, QueryStages }
  Mesh { Task, Mesh, IndirectMesh, ICBOrExecuteIndirect }
  WorkCreation
    IndirectCommand { Domains, Tokens, Preprocess, NativeRoute }
    WorkGraph { NodeTypes, BackingMemory, GPUInput }
  Memory
    Sparse { Buffer, Texture2D, Texture3D, MSAA, Residency, TileGeometry }
    External { Memory, Semaphore, Fence, HandleTypes }
  Streaming
    StorageIO { Queue, Batch, FenceSync }
    GpuDecompression { Formats }
    HostImageCopy
  Compilation
    PipelineCache { OpaqueBlob, PortableBlob, BinaryArchive, PipelineBinary }
    ShaderObject / FlexiblePipeline / FunctionLibrary
  Matrix
    Cooperative { Scope, Types, Dimensions, Saturation, Training }
  Diagnostics
    TimestampCalibration, DeviceFault, Counters, DebugMarkers
```

每个 capability 至少要区分 `NativeAvailable`、`SharpGpuLoweringAvailable` 与 `Qualified`，或者由报告把这三层证据组合起来。capability unavailable 时，任何 public factory/encoder 必须抛出一致的 `NotSupportedException`；禁止空实现、CPU fallback 或偷偷改写状态。报告必须记录源码 commit、backend SDK、driver/device、probe kind 与 test evidence。“平台支持”不等于“库已实现”，“portable test passed”也不等于“GPU qualified”。

## 可执行的修复与验收顺序

| 阶段 | 工作项 | 验收 |
|---|---|---|
| P0-A（portable 实现完成；runtime qualification pending） | ADR-0064：Inputs/Outputs、overlap RMW、exact blend query、Raw ABI、三后端私有 lowering、schema clean break | portable/SharpShader gate 已通过；DX12/Vulkan/Metal matching-host 复验仍由当前 Task W8 跟踪；旧字段/reader/cache residue 为零 |
| P0-B | 修正 DX12 VRS Options6 probe；Vulkan/Metal 要么真实 lowering，要么 capability unavailable 并让 setter fail closed；刷新 feature report | capability 与 factory/encoder 行为一致；不存在 no-op；报告带 commit/SDK/device |
| P0 | DX12 mesh native pipeline + indirect qualified；Metal mesh 或明确标为 unavailable；Vulkan mesh 做 real GPU smoke | 每个 backend 对 `MeshShader` 的 false/true 都有对应 test，不以硬件 tier 单独决定 |
| P0 | Sparse 2D 与 3D 分开测试，至少覆盖 2D array、3D、unbound region、tile bind/unbind、barrier/residency | 2D/3D 通过情况分别写入报告；不能用 2D 结果代表 3D |
| P1 | 增加通用 `QueryFormatSupport`、external memory/semaphore、timeline/shared event、calibrated timestamp、device fault hooks | CPU contract tests + backend qualified tests；unsupported 必须 fail closed |
| P1 | 增加独立 `SamplerFeedbackMap` optional domain 和 GPU I/O semantic/service split | D3D12 feedback streaming/resolve smoke；MTLIO 与 Vulkan memory decompression 分开记录，不声明同构 |
| P2 | pipeline binary/shader object/flexible PSO；RT OMM/SER/motion；Vulkan DGC/AMDX；Metal argument table/allocator | 只在对应 SDK/family/extension gate 下启用，stable 报告不混入 preview |

ADR-0064 当前工作树的 portable gate 为 95 passed、0 failed、0 skipped；完整命令、最终门禁与平台 blocker 只记录在 `docs/Canonical/VERIFICATION.md` 和当前 Task 账本。该结果支持“公共 contract/planner/cache/fail-closed 健康”，不支持“三平台 attachment runtime 已重签”或“全部 feature 已完成”。

## 官方参考（截至 2026-08-30）

### Microsoft / D3D12

1. [DXR Functional Spec](https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html)：RT pipeline、tier 与 inline RayQuery。
2. [Work Graphs spec](https://microsoft.github.io/DirectX-Specs/d3d/WorkGraphs.html)：node、backing memory、`DispatchGraph` 与 tier。
3. [D3D12 Render Passes](https://learn.microsoft.com/en-us/windows/win32/direct3d12/direct3d-12-render-passes)：`BeginRenderPass`/`EndRenderPass` 与 attachment load/store。
4. [Sampler Feedback spec](https://microsoft.github.io/DirectX-Specs/d3d/SamplerFeedback.html)：feedback map、streaming 与 texture-space shading。
5. [Variable Rate Shading](https://learn.microsoft.com/en-us/windows/win32/direct3d12/vrs)：Tier 1/2、rate image 与 combiner。
6. [Mesh Shader spec](https://microsoft.github.io/DirectX-Specs/d3d/MeshShader.html)：amplification/mesh 与 `DispatchMesh`。
7. [DirectStorage guidance](https://github.com/microsoft/DirectStorage/blob/main/Docs/DeveloperGuidance.md)：独立 storage queue 与 GPU decompression。
8. [Tiled Resources tiers](https://learn.microsoft.com/en-us/windows/win32/api/d3d12/ne-d3d12-d3d12_tiled_resources_tier)：reserved/tiled resource 与 volume tier。
9. [Rasterizer Ordered Views](https://learn.microsoft.com/en-us/windows/win32/direct3d12/rasterizer-order-views)：HLSL UAV ordered access，不是 attachment fetch API。
10. [Enhanced Barriers](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/enhanced-barriers)：sync/access/layout barrier model。

### Khronos / Vulkan

1. [KHR ray tracing pipeline](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_ray_tracing_pipeline.html) 与 [KHR ray query](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_ray_query.html)：两个独立 feature。
2. [EXT device generated commands](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_device_generated_commands.html)：generated command stream，不是 Work Graph。
3. [KHR dynamic rendering](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_dynamic_rendering.html) 与 [dynamic rendering local read](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_dynamic_rendering_local_read.html)。
4. [EXT rasterization-order attachment access](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_rasterization_order_attachment_access.html)：同 attachment ordered access；与 feedback-loop layout 分离。
5. [KHR fragment shading rate](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_fragment_shading_rate.html) 与 [EXT mesh shader](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_mesh_shader.html)。
6. [EXT memory decompression](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_memory_decompression.html)：memory-to-memory decompression，不是 file I/O。
7. [Sparse resources](https://docs.vulkan.org/spec/latest/chapters/sparsemem.html)：buffer、2D、3D feature 独立。
8. [Descriptor indexing](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_descriptor_indexing.html)、[descriptor buffer](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_descriptor_buffer.html) 与 [descriptor heap](https://docs.vulkan.org/refpages/latest/refpages/source/VK_EXT_descriptor_heap.html)。
9. [Vulkan core revisions](https://docs.vulkan.org/spec/latest/appendices/versions.html)、[KHR cooperative matrix](https://docs.vulkan.org/refpages/latest/refpages/source/VK_KHR_cooperative_matrix.html) 与 [Roadmap 2026](https://docs.vulkan.org/spec/latest/appendices/roadmap.html)：cooperative matrix 是路线图要求的 extension，不是 Vulkan 1.4 core。

### Apple / Metal

1. [Metal capabilities](https://developer.apple.com/metal/capabilities/) 与 [feature sets](https://developer.apple.com/metal/feature-sets/)：按 Metal/Apple family 查询 RT、mesh、VRS、sparse 与 barrier。
2. [Ray tracing with acceleration structures](https://developer.apple.com/documentation/metal/ray-tracing-with-acceleration-structures)：AS 与 intersection query/intersector。
3. [Deferred lighting / programmable blending](https://developer.apple.com/documentation/metal/rendering-a-scene-with-deferred-lighting-in-c%2B%2B)：attachment fetch/tile-local 数据流。
4. [Raster order groups](https://developer.apple.com/documentation/metal/mtldevice/arerasterordergroupssupported)：native ordered fragment memory；不是 SharpGPU 公共 attachment 参数。
5. [Rasterization-rate map](https://developer.apple.com/documentation/metal/rendering-with-a-rasterization-rate-map)：Metal VRS route。
6. [Resource loading](https://developer.apple.com/documentation/metal/resource-loading)：`MTLIOCommandQueue`/buffer/file handle。
7. [Sparse texture memory](https://developer.apple.com/documentation/metal/managing-sparse-texture-memory)：sparse heap/texture mapping。
8. [Metal 4 core API](https://developer.apple.com/documentation/metal/understanding-the-metal-4-core-api)：argument table、command/barrier 模型。
9. [Machine learning passes](https://developer.apple.com/documentation/metal/machine-learning-passes)：ML command encoder/tensor。
10. [Indirect command buffers](https://developer.apple.com/documentation/metal/mtlindirectcommandbuffer)：GPU-generated commands，不是 graph scheduler。
11. [VideoToolbox](https://developer.apple.com/documentation/videotoolbox)：Apple video codec API 位于 Metal 之外。

## 最终判断

SharpGPU 的核心方向是正确的：语义级公共 contract、typed capability、native lowering、无隐式 CPU fallback、域化 encoder/resource/sync，是兼顾简洁与强大的正确组合。ADR-0064 证明 attachment 不需要公开 Vulkan/DX12/Metal 的机制名，也能保持 Raw shader 自由与精确能力查询。

距离“全面公共现代 RHI”的主要差距已经不是 attachment，而是 capability truth 与域完整性：先消灭 VRS no-op/假 probe，完成 mesh 与 sparse 3D 的真实资格，再补通用 format/external/timeline/diagnostics；Sampler Feedback、DGC、cooperative matrix 保持公共 optional domain；GPU file I/O、video、ML graph 保持独立 service/framework。只要继续坚持“查询、lowering、factory、qualified evidence 四者一致”，SharpGPU 可以强大而不臃肿；任何只靠硬件 tier、空 setter 或历史报告得出的 `Supported` 都必须视为 bug。
