# SharpGPU Feature Matrix

This matrix describes the active SharpGPU public contract. Runtime facts come
from `RHIDevice.Capabilities`; platform qualification outcomes come from the
typed per-platform reports under `docs/Artifacts/SharpGPU/`. Capability and
qualification are deliberately separate: a successful native probe is not a
substitute for a passed runtime scenario.

Rows marked `Typed capability` map to one domain of
`RHIDeviceCapabilities`. Rows marked `API contract` are always present and must
either execute through an accurately reported native strategy or fail with the
documented exception/status model.

## Platform Qualification Artifacts

| Platform | Artifact | Current status |
|---|---|---|
| Windows x64 | `docs/Artifacts/SharpGPU/feature-report-win-x64.json` | G5-T17 implementer re-run HEAD `89cc0096728b0c8fcc0ef1ba975cb832dddf96cc` + uncommitted CS0165 fix (Exit Gate still Luna; do not invent a SHA): schema revision 6; pipeline ABI 9; SER capability absent. Matching-host counts in `VERIFICATION.md` CURRENT: Portable 192/192, WindowsQualified 20/20, DirectStorage 3/3, FeatureReport 2/2, PipelineCache 12/12, SharpShaderAttachment 43/43, RendererFrameResourceQualified 29/29. Independent MotionVulkan 2/2, OMM Vulkan 1/1, Mesh Vulkan 1/1. Full `SharpGpuVulkanQualified` still `BLOCKED_DEVICE` (ROAA; 7 passed / 1 failed / 0 skip). Capability reflection is not a substitute for those qualified gates |
| Android ARM64 | `docs/Artifacts/SharpGPU/feature-report-android-arm64.json` | Historical pre-ADR-0064 API30/ARM64/Adreno650 lifecycle evidence only; current attachment qualification is unverified |
| Linux x64 | none (do not invent placeholders) | `BLOCKED_PLATFORM` / Unverified until matching-host Vulkan qualification passes (`P13-LINUX`) |
| macOS ARM64 | none (current report name intentionally absent) | Historical bytes are preserved as `docs/Artifacts/SharpGPU/historical-pre-adr0064-feature-report-macos-arm64.json`; a matching Apple host must regenerate the current report |
| iOS/iPadOS ARM64 | `docs/Artifacts/SharpGPU/feature-report-ios-arm64.json` | Historical pre-ADR-0064 physical-device lifecycle evidence only; current attachment qualification is unverified |

## Hard Gate Categories (RFC-0021)

Aligned with `docs/Canonical/VERIFICATION.md` and conformance `Trait("Category", ...)`.

| Category | Runs on ordinary hosted CI? | Matching host/device required for PASSED |
|---|---|---|
| `SharpGpuPortable` | Yes (contracts only) | No |
| `SharpGpuWindowsQualified` | No | Windows x64 + real DX12 GPU |
| `SharpGpuAndroidQualified` | No | Android ARM64 device |
| `SharpGpuVulkanQualified` | No | Windows or Linux Vulkan GPU host |
| `SharpGpuMetalQualified` | No | macOS ARM64 or iOS/iPadOS ARM64 |
| `SharpGpuDirectStorageQualified` | No | Windows DirectStorage; else NotApplicable |
| `RendererFrameResourceQualified` | Portable policy subset only | Full frame/presentation scenarios need matching renderer host |
| `SharpShaderAttachment` | Host compile OK | Metal runtime still requires matching Apple host; Windows MSL/metallib is supplementary |

A Successful native capability probe is never a substitute for a Passed Qualified scenario.

## Public Contract Rows

| Feature | Surface | Contract kind | Required conformance evidence | Unsupported behavior |
|---|---|---|---|---|
| TimestampQueries | `RHIDeviceCapabilities.Synchronization.TimestampQueries`, `RHIQuery`, timestamp encoding | Typed capability | create, encode, submit, resolve, and readback on every reported strategy | `CreateQuery`/encoding throws `NotSupportedException` when unavailable |
| OcclusionQueries | `RHIDeviceCapabilities.Synchronization.OcclusionQueries`, `RHIQuery`, raster occlusion encoding | Typed capability | native raster query plus resolve/readback on every reported strategy | creation/encoding throws `NotSupportedException` when unavailable |
| PipelineStatisticsQueries | `RHIDeviceCapabilities.Synchronization.PipelineStatisticsQueries`, `RHIQuery` typed Raster / Compute / RayTracing results | Typed capability | native begin/end/resolve plus typed domain counters; capability reports domain mask and per-domain counter mask | unsupported domain or counter fails at `CreateQuery`; no flat `ulong[]` / stride Statistics path |
| MachineLearning | `RHIDeviceCapabilities.MachineLearning.Execution`, `RHIMLBinary`, `RHIMLPipeline`, `RHIMLBindingTable`, `RHITensor`, `RHIMLEncoder` | Typed capability (Tier1 = pipeline inference) | Binary-only public contract (ADR-0052): `CreateMLPipeline(RHIMLBinary)` → BindingSet → Encoder; DX12 loads DirectMLProgramV1 (`.dmlbin`); Metal loads MetalPackageV1 (`.mtlmlbin` wrapping CoreML→`metal-package-builder` `.mtlpackage` + reflection); no runtime op-graph public API | all ML factories throw `NotSupportedException` when unavailable; Metal Execution = Tier1 on Metal 4 hosts after W4 multi-dispatch proof (`MetalMLStabilityProbeTests`) |
| IndirectCommandBuffer | `RHIDeviceCapabilities.IndirectCommandBuffer.Execution`, `RHI*IndirectCommandBuffer`, `ExecuteIndirectCommandBuffer` | Typed capability + API contract | DX12 `ExecuteIndirect` + Metal `MTLIndirectCommandBuffer` encode/execute/readback (macOS MetalQualified evidence) | Vulkan and unavailable backends throw `NotSupportedException`; no stub ICB classes |
| Raytracing | `RHIDeviceCapabilities.RayTracing`, acceleration structures, raytracing pipeline/pass | Typed capability | native build/trace/readback before a backend may report an available tier | all ray-tracing factories throw `NotSupportedException` when unavailable |
| OpacityMicromap | `RayTracing.OpacityMicromap` / `OpacityMicromapSerialization`, `RHIOpacityMicromap`, BLAS triangle attachment, instance `ForceOmm2State` / `DisableOmms` | Typed capability + AccelStruct object model | G4-T13 PASSED: DX12 Tier1_2 + factory/build; Vulkan only when `VK_EXT_opacity_micromap` is enabled, AS + sync2 (1.3 or `VK_KHR_synchronization2`) are enabled, and function pointers load; WindowsQualified tiny 2-state OMM + one-triangle BLAS; independent Vulkan OMM 1/1 | Metal and missing native path Unavailable; Create/Build `Require` throw; unknown format fail-closed; OMM input / triangle-array / index buffers must include `ERHIBufferUsage.AccelStruct` (maps to `MICROMAP_BUILD_INPUT_READ_ONLY`); `DisableOmms` requires BLAS `AllowDisableOmms` and sets native `ALLOW_DISABLE_OMMS`; serialization is honest Unavailable (no fake blob) |
| Motion | `RayTracing.Motion`, motion triangles (`MotionVertexBuffer` / `MotionVertexOffset` / `MotionVertexStride`) and motion instances (`MotionType` / matrix / SRT), `ERHIAccelStructFlag.Motion` | Typed capability + AccelStruct object model | G4-T15 PASSED at SHA `11d5a8c1ba2bb9d131a96458d22e12ff167e3041`: Vulkan Available only when `VK_NV_ray_tracing_motion_blur` is listed, `rayTracingMotionBlur` is enabled, AS deps + function pointers load, and the native motion AS path is used; DX12 Unavailable (no standard D3D12 motion AS); Metal compile-level Available only when `supportsPrimitiveMotionBlur` plus MTL motion triangle / motion instance descriptors bind | Create/Build `Require` throw when Unavailable; `ERHIAccelStructFlag.Motion` must match motion data (XOR fail-closed); failed Update leaves descriptor + native motion mode unchanged; `MotionVertexStride` 0 means `VertexStride`, a different non-zero stride is fail-closed; unknown `MotionType` fail-closed; no silent static path; pipeline ABI **9** is an incompatible revision for the Motion public descriptor (AS is not a pipeline-cache key); SER / HitObject is G4-T14 `BLOCKED_SDK_BINDING` and is not this row |
| MeshShading | `RHIDeviceCapabilities.Mesh.MeshShader` / `Mesh.TaskShader`, mesh raster pipeline and dispatch | Typed capability | native mesh pipeline plus pixel/readback evidence | mesh factories/encoding throw `NotSupportedException` when unavailable; no silent no-op dispatch |
| DescriptorIndexing | `RHIDeviceCapabilities.DescriptorIndexing`, `RHIBindingTableLayoutElement.Count`, `RHIBindingTable.SetBindElement` | Typed capability + API contract | required/optional arrays, namespace mapping, cross-device/disposed validation, pool rollback, dispatch/readback | unsupported native descriptor semantics fail at layout/table creation; no typed dummy descriptors |
| StorageQueue | `RHIDeviceCapabilities.Storage.NativeGpuFileIo` / `GpuDecompression` / `RequestCancellation` / `IoPriority`, `RHIStorageQueue` | Independent typed capabilities + API contract | DX12 DirectStorage file → GPU-local buffer/texture → fence → readback under `SharpGpuDirectStorageQualified`; GDeflate / `CancelRequestsWithTag` / queue `Priority` only when the native queue expresses them | non-native backends and missing DirectStorage support throw `NotSupportedException`; Vulkan and unproven dimensions stay Unavailable; no FileStream/map/staging queue fallback |
| PipelineCache | `RHIDeviceCapabilities.PipelineCache.NativeCache`, `RHIPipelineCache` | Typed capability + API contract | cold/warm/restart native hit, typed corrupt/incompatible import, full-key non-collision | unavailable native cache strategy throws `NotSupportedException`; caller owns opaque blobs |
| WorkGraph | `RHIDeviceCapabilities.WorkGraph.Execution`, `RHIWorkGraphPipeline`, `RHIWorkGraphEncoder` | Typed capability | native create/dispatch/readback on each reported strategy | factory/encoding throws `NotSupportedException` when unavailable |
| FramebufferReadWrite | `RHIDeviceCapabilities.Raster.FramebufferReadWrite`, `QueryRasterAttachmentSupport`, `RHIAttachmentShaderAbi` | Typed capability + API contract | same-phase `Inputs ∩ Outputs`, exact format/sample/layer/blend query, Raw ABI revision/hash claim, and backend lowering qualification | unsupported combinations fail before pipeline creation; hardware blend is never silently disabled or rewritten |
| RasterSubPass | `RHIRasterPassDescriptor`, `RHISubPassDescriptor`, `NextSubPass` | Typed capability + API contract | immutable planner tests and backend multi-subpass pixel/readback qualification | inexpressible access/attachment contracts fail before native encoding; no public layout/bindings type |
| Presentation | `RHIDeviceCapabilities.Presentation.SwapChain`/`Hdr`, swapchain acquire/present/resize typed status plus fence/semaphore sync | Typed capability + API contract | matching-window minimize/resize/out-of-date/surface-lost/device-lost scenarios | HAL reports status and never performs hidden recreate or queue idle; acquire signal, present wait, and present completion are available whenever swapchain is |
| VariableRateShading | `Raster.VariableRateShadingPerDraw` / `PerPrimitive` / `Attachment` / `Combiners` | Typed capability | DX12 Options6 + `RSSetShadingRate`; Vulkan `VK_KHR_fragment_shading_rate`; Metal Unavailable | setter / attachment bind `Require` then throw; no no-op |
| FormatSupport | `RHIDevice.QueryFormatSupport` | Device exact query | format × usage × dimension × sample × tiling → operation mask including `ResolveSource` / `ResolveDestination` + reason | unknown/Pending format or zero/unknown usage throw; inexpressible combo Unavailable |
| QueryResolveSupport | `RHIDevice.QueryResolveSupport`, `RHIResolveSupportQuery` | Device exact query | MSAA source × single-sample destination × aspect × resolve mode → capability + supported mode mask | non-MSAA source or MSAA destination throw at query construction; unknown pair Unavailable |
| CalibratedTimestamps | `Synchronization.CalibratedTimestamps`, `QueryClockCalibration` | Typed capability + exact query | DX12 `GetClockCalibration`; Vulkan KHR/EXT calibrated timestamps | no Stopwatch/DateTime fake; Require+throw when unavailable |
| SparseMemory | `Memory.SparseTexture2D` / `SparseTexture3D` / `SparseBuffer` / `SparseMsaa` / `SparseMipTail` / `SparseTileGeometry` / `SparseAliasing` | Typed capability | bind/unbind/mip-tail only on implemented facets; no cross-dimension inference | missing facet Unavailable; no sparse-buffer factory on current DX12 |
| SamplerFeedback | `Raster.SamplerFeedback`, `CreateSamplerFeedbackMap` | DX12-only typed facet | create-time pairing; encoder clear/resolve/decode/copy | Vulkan/Metal Unavailable; no pairing on encoder |
| CooperativeMatrix | `Compute.CooperativeMatrix`, `QueryCooperativeMatrixConfigs` | Typed capability | Vulkan `VK_KHR_cooperative_matrix` enumeration | DX12 Unavailable (vendored SDK has no WaveMMA query); not an ML facet |
| FunctionLibrary | `FunctionLibrary` reusable pipeline class mask + library views | Typed capability + API contract | Vulkan/Metal raster/compute views share native container; DX12 raster/compute reuse Unavailable | view after library dispose fail-closed; CURRENT pipeline ABI **9** keys use content digest; ABI 8 blobs are Incompatible |
| AdapterIdentity | `RHIDevice.AdapterIdentity` (Instance-domain LUID / UUID) | Device / Instance fact | DX12 DXGI LUID; Vulkan `deviceUUID` / `deviceLUID` when valid; Metal `registryID` as LUID with `HasDeviceUuid=false` | not a MultiGPU capability; no `NodeMask` placeholder |

## Contract Rules

- `Tier`, `Strategy`, `Limits`, `UnavailableReason`, and `Provenance` describe
  the current device only.
- `Passed`, `Failed`, `Unverified`, and `NotApplicable` belong only to platform
  qualification reports.
- Unknown enums, illegal combinations, and capability/factory disagreement
  fail closed.
- Fixed-function blend is orthogonal to attachment read/write. The exact
  combination either executes unchanged or is rejected.
- A `Qualified` category may not skip, silently return, or use a CPU fallback.

`SharpGPUFeatureMatrixDocumentationTests` guards the public row names. Runtime
truth still requires the corresponding conformance scenario and typed artifact.
