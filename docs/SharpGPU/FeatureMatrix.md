# SharpGPU Feature Matrix

This matrix describes the active SharpGPU public contract. Runtime facts come
from `RHIDevice.Capabilities`; platform qualification outcomes come from the
typed per-platform reports under `Engine/Artifacts/SharpGPU/`. Capability and
qualification are deliberately separate: a successful native probe is not a
substitute for a passed runtime scenario.

Rows marked `Typed capability` map to one domain of
`RHIDeviceCapabilities`. Rows marked `API contract` are always present and must
either execute through an accurately reported native strategy or fail with the
documented exception/status model.

## Platform Qualification Artifacts

| Platform | Artifact | Current status |
|---|---|---|
| Windows x64 | `Engine/Artifacts/SharpGPU/feature-report-win-x64.json` | W12 must regenerate the report with the typed schema after all applicable qualified gates pass |
| Android ARM64 | `Engine/Artifacts/SharpGPU/feature-report-android-arm64.json` | Generated only after matching-device build/install/runtime qualification; no placeholder is permitted |
| Linux x64 | none | `BLOCKED_PLATFORM` until matching-host Vulkan qualification passes |
| macOS ARM64 | none | `BLOCKED_PLATFORM` until matching-host Metal qualification passes |
| iOS/iPadOS ARM64 | none | `BLOCKED_PLATFORM` until matching-device Metal qualification passes |

## Public Contract Rows

| Feature | Surface | Contract kind | Required conformance evidence | Unsupported behavior |
|---|---|---|---|---|
| TimestampQueries | `RHIDeviceCapabilities.Synchronization.TimestampQueries`, `RHIQuery`, timestamp encoding | Typed capability | create, encode, submit, resolve, and readback on every reported strategy | `CreateQuery`/encoding throws `NotSupportedException` when unavailable |
| OcclusionQueries | `RHIDeviceCapabilities.Synchronization.OcclusionQueries`, `RHIQuery`, raster occlusion encoding | Typed capability | native raster query plus resolve/readback on every reported strategy | creation/encoding throws `NotSupportedException` when unavailable |
| PipelineStatisticsQueries | `RHIDeviceCapabilities.Synchronization.PipelineStatisticsQueries`, `RHIQuery` | Typed capability | native statistics query plus resolve/readback | creation/encoding throws `NotSupportedException` when unavailable |
| MachineLearning | `RHIDeviceCapabilities.MachineLearning.Execution`, `RHIMLPipeline`, `RHIMLBindingSet`, `RHITensor`, `RHIMLEncoder` | Typed capability | native operator compile/dispatch/readback; DX12 uses the DirectML qualified path | all ML factories throw `NotSupportedException` when unavailable; no CPU emulation |
| Raytracing | `RHIDeviceCapabilities.RayTracing`, acceleration structures, raytracing pipeline/pass | Typed capability | native build/trace/readback before a backend may report an available tier | all ray-tracing factories throw `NotSupportedException` when unavailable |
| MeshShading | `RHIDeviceCapabilities.Mesh.Shader`, mesh raster pipeline and dispatch | Typed capability | native mesh pipeline plus pixel/readback evidence | mesh factories/encoding throw `NotSupportedException` when unavailable |
| DescriptorIndexing | `RHIDeviceCapabilities.DescriptorIndexing`, `RHIArgumentTableLayoutElement.Count`, `RHIArgumentTable.SetBindElement` | Typed capability + API contract | required/optional arrays, namespace mapping, cross-device/disposed validation, pool rollback, dispatch/readback | unsupported native descriptor semantics fail at layout/table creation; no typed dummy descriptors |
| StorageQueue | `RHIDeviceCapabilities.Storage.NativeGpuFileIo`, `RHIStorageQueue` | Typed capability + API contract | DX12 DirectStorage file → GPU-local buffer/texture → fence → readback under `SharpGpuDirectStorageQualified` | non-native backends and missing DirectStorage support throw `NotSupportedException`; no FileStream/map/staging queue fallback |
| PipelineCache | `RHIDeviceCapabilities.PipelineCache.NativeCache`, `RHIPipelineCache` | Typed capability + API contract | cold/warm/restart native hit, typed corrupt/incompatible import, full-key non-collision | unavailable native cache strategy throws `NotSupportedException`; caller owns opaque blobs |
| WorkGraph | `RHIDeviceCapabilities.WorkGraph.Execution`, `RHIWorkGraphPipeline`, `RHIWorkGraphEncoder` | Typed capability | native create/dispatch/readback on each reported strategy | factory/encoding throws `NotSupportedException` when unavailable |
| RasterSubPass | `RHIRasterPassDescriptor`, `RHISubPassDescriptor`, `NextSubPass` | Typed capability + API contract | immutable planner tests and backend multi-subpass pixel/readback qualification | inexpressible access/attachment contracts fail before native encoding; no public layout/bindings type |
| Presentation | `RHIDeviceCapabilities.Presentation`, swapchain acquire/present/resize typed status | Typed capability + API contract | matching-window minimize/resize/out-of-date/surface-lost/device-lost scenarios | HAL reports status and never performs hidden recreate or `WaitIdle` |

## Contract Rules

- `Tier`, `Strategy`, `Limits`, `UnavailableReason`, and `Provenance` describe
  the current device only.
- `Passed`, `Failed`, `Unverified`, and `NotApplicable` belong only to platform
  qualification reports.
- Unknown enums, illegal combinations, and capability/factory disagreement
  fail closed.
- A `Qualified` category may not skip, silently return, or use a CPU fallback.

`SharpGPUFeatureMatrixDocumentationTests` guards the public row names. Runtime
truth still requires the corresponding conformance scenario and typed artifact.
