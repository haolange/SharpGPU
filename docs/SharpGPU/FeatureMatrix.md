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
| Windows x64 | `docs/Artifacts/SharpGPU/feature-report-win-x64.json` | Current ADR-0064 schema-revision-2 capability report regenerated on 2026-08-30; the report is not a substitute for the separately blocked Windows/Vulkan qualified gates |
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
| PipelineStatisticsQueries | `RHIDeviceCapabilities.Synchronization.PipelineStatisticsQueries`, `RHIQuery` | Typed capability | native statistics query plus resolve/readback | creation/encoding throws `NotSupportedException` when unavailable |
| MachineLearning | `RHIDeviceCapabilities.MachineLearning.Execution`, `RHIMLBinary`, `RHIMLPipeline`, `RHIMLBindingTable`, `RHITensor`, `RHIMLEncoder` | Typed capability (Tier1 = pipeline inference) | Binary-only public contract (ADR-0052): `CreateMLPipeline(RHIMLBinary)` → BindingSet → Encoder; DX12 loads DirectMLProgramV1 (`.dmlbin`); Metal loads MetalPackageV1 (`.mtlmlbin` wrapping CoreML→`metal-package-builder` `.mtlpackage` + reflection); no runtime op-graph public API | all ML factories throw `NotSupportedException` when unavailable; Metal Execution = Tier1 on Metal 4 hosts after W4 multi-dispatch proof (`MetalMLStabilityProbeTests`) |
| IndirectCommandBuffer | `RHIDeviceCapabilities.IndirectCommandBuffer.Execution`, `RHI*IndirectCommandBuffer`, `ExecuteIndirectCommandBuffer` | Typed capability + API contract | DX12 `ExecuteIndirect` + Metal `MTLIndirectCommandBuffer` encode/execute/readback (macOS MetalQualified evidence) | Vulkan and unavailable backends throw `NotSupportedException`; no stub ICB classes |
| Raytracing | `RHIDeviceCapabilities.RayTracing`, acceleration structures, raytracing pipeline/pass | Typed capability | native build/trace/readback before a backend may report an available tier | all ray-tracing factories throw `NotSupportedException` when unavailable |
| MeshShading | `RHIDeviceCapabilities.Mesh.Shader`, mesh raster pipeline and dispatch | Typed capability | native mesh pipeline plus pixel/readback evidence | mesh factories/encoding throw `NotSupportedException` when unavailable; no silent no-op dispatch |
| DescriptorIndexing | `RHIDeviceCapabilities.DescriptorIndexing`, `RHIBindingTableLayoutElement.Count`, `RHIBindingTable.SetBindElement` | Typed capability + API contract | required/optional arrays, namespace mapping, cross-device/disposed validation, pool rollback, dispatch/readback | unsupported native descriptor semantics fail at layout/table creation; no typed dummy descriptors |
| StorageQueue | `RHIDeviceCapabilities.Storage.NativeGpuFileIo`, `RHIStorageQueue` | Typed capability + API contract | DX12 DirectStorage file → GPU-local buffer/texture → fence → readback under `SharpGpuDirectStorageQualified` | non-native backends and missing DirectStorage support throw `NotSupportedException`; no FileStream/map/staging queue fallback |
| PipelineCache | `RHIDeviceCapabilities.PipelineCache.NativeCache`, `RHIPipelineCache` | Typed capability + API contract | cold/warm/restart native hit, typed corrupt/incompatible import, full-key non-collision | unavailable native cache strategy throws `NotSupportedException`; caller owns opaque blobs |
| WorkGraph | `RHIDeviceCapabilities.WorkGraph.Execution`, `RHIWorkGraphPipeline`, `RHIWorkGraphEncoder` | Typed capability | native create/dispatch/readback on each reported strategy | factory/encoding throws `NotSupportedException` when unavailable |
| FramebufferReadWrite | `RHIDeviceCapabilities.Raster.FramebufferReadWrite`, `QueryRasterAttachmentSupport`, `RHIAttachmentShaderAbi` | Typed capability + API contract | same-phase `Inputs ∩ Outputs`, exact format/sample/layer/blend query, Raw ABI revision/hash claim, and backend lowering qualification | unsupported combinations fail before pipeline creation; hardware blend is never silently disabled or rewritten |
| RasterSubPass | `RHIRasterPassDescriptor`, `RHISubPassDescriptor`, `NextSubPass` | Typed capability + API contract | immutable planner tests and backend multi-subpass pixel/readback qualification | inexpressible access/attachment contracts fail before native encoding; no public layout/bindings type |
| Presentation | `RHIDeviceCapabilities.Presentation.SwapChain`/`Hdr`, swapchain acquire/present/resize typed status plus fence/semaphore sync | Typed capability + API contract | matching-window minimize/resize/out-of-date/surface-lost/device-lost scenarios | HAL reports status and never performs hidden recreate or queue idle; acquire signal, present wait, and present completion are available whenever swapchain is |

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
