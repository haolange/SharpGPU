# SharpGPU Feature Matrix

This matrix is the public-ready contract surface for SharpGPU feature claims.
The source of truth for runtime evidence is the per-platform JSON written by
`SharpGPU.Conformance.Tests` under `Engine/Artifacts/SharpGPU/`.

Rows marked as `Device feature flag` map to `RHIDeviceFeature`. Rows marked as
`API contract` are public SharpGPU APIs without a dedicated feature flag in v1.

## Verified Platform Artifacts

| Platform | Artifact | Verified evidence |
|---|---|---|
| Windows x64 / RTX 5090 | `Engine/Artifacts/SharpGPU/feature-report-win-x64.json` | DX12 WorkGraph dispatch/readback; DX12 Query, Bindless, StorageQueue, PipelineLibrary, DirectML contract matrix |
| macOS ARM64 / Apple M3 Max | `Engine/Artifacts/SharpGPU/feature-report-macos-arm64.json` | Metal timestamp/occlusion native query contract; Metal ML false/throws contract; per-backend feature document schema |

## Public Contract Rows

| Feature | Surface | Contract kind | Current conformance evidence | Unsupported behavior |
|---|---|---|---|---|
| TimestampQueries | `RHIDeviceFeature.IsTimestampQueriesSupported`, `RHIQuery`, transfer/raster/compute timestamp encoding | Device feature flag | DX12 create + encode + submit + readback in `Dx12_TimestampQuery_ShouldCreateExecuteSubmitAndReadback`; Metal timestamp native counter heap contract is verified by macOS artifact | Feature false backends must throw `NotSupportedException` when creation/encoding is unavailable |
| OcclusionQueries | `RHIDeviceFeature.IsOcclusionQueriesSupported`, `RHIQuery`, raster occlusion encoding | Device feature flag | Metal occlusion native render query contract is verified by macOS artifact; other backends are reported in per-platform feature JSON | Feature false backends must throw `NotSupportedException` when creation/encoding is unavailable |
| PipelineStatisticsQueries | `RHIDeviceFeature.IsPipelineStatsQueriesSupported`, `RHIQuery`, statistics encoding | Device feature flag | Reported in per-platform feature JSON; Metal is false when a statistics counter set is unavailable | Feature false backends must throw `NotSupportedException` when creation/encoding is unavailable |
| MachineLearning | `RHIDeviceFeature.IsMLSupported`, `RHIMLPipeline`, `RHIMLBindingSet`, `RHITensor`, `RHIMLEncoder` | Device feature flag | DX12 DirectML GEMM+ReLU end-to-end contract is native to `SharpGPU.Conformance.Tests`; Vulkan v1 false/throws and Metal ML false/throws are covered | Feature false backends must throw `NotSupportedException`; Vulkan v1 stays false until `VK_ARM_tensors`/`VK_ARM_data_graph` probing exists; Metal stays false until native ML package path exists |
| Raytracing | `RHIDeviceFeature.IsRaytracingSupported`, acceleration structures, raytracing pipeline/pass | Device feature flag | DX12 false adapters now throw `NotSupportedException`; full create + dispatch + readback remains TODO(UNVERIFIED) before any backend is called verified | Feature false backends must throw `NotSupportedException` |
| MeshShading | `RHIDeviceFeature.IsMeshShadingSupported`, mesh raster pipeline, mesh dispatch | Device feature flag | DX12 flag is forced false until the native mesh pipeline path is implemented and covered by conformance | Feature false backends must throw `NotSupportedException` |
| Bindless | `RHIArgumentTableLayoutElement.Count > 1`, `RHIArgumentTable.SetBindElement(..., arrayIndex)` | API contract | DX12 bindless table create + array slot update in `Dx12_BindlessArgumentTable_ShouldCreateAndUpdateArraySlots` | Backend-specific missing bindless support must throw `NotSupportedException` rather than silently no-op |
| StorageQueue | `RHIStorageQueue`, `RHIStorageBufferRequest`, `RHIStorageTextureRequest` | API contract | DX12 buffer request submit + mappable readback in `Dx12_StorageQueueBuffer_ShouldSubmitAndReadBackMappableDestination` | Unsupported CPU fallback paths, such as DX12 texture fallback without DirectStorage, throw `NotSupportedException` |
| PipelineLibrary | `RHIPipelineLibrary` | API contract | DX12 create + serialize in `Dx12_PipelineLibrary_ShouldCreateAndSerialize`; raytracing state object store/load explicitly throws | Unsupported pipeline kinds must throw `NotSupportedException` |
| WorkGraph | `RHIDeviceFeature.IsWorkgraphSupported`, `RHIWorkGraphPipeline`, `RHIWorkGraphEncoder` | Device feature flag | DX12 WorkGraph dispatch/readback conformance and workload encode benchmark; Vulkan/Metal false/throws conformance | Feature false backends must throw `NotSupportedException` |

## Documentation Drift Guard

`SharpGPUFeatureMatrixDocumentationTests` asserts that every public feature row
above remains present in this document. The behavioral truth still comes from
conformance tests and generated per-platform feature JSON; this document must
not claim a backend as verified unless the corresponding conformance path and
artifact exist.
