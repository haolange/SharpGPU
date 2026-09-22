# SharpGPU

SharpGPU is a .NET 10 hardware abstraction over DirectX 12, Vulkan and Metal. The public surface is a set of explicit mechanisms: devices, resources, immutable pipelines, binding tables, command encoders, queues and presentation. Pass topology, barrier inference, memory aliasing and transient-resource lifetime stay with the caller, typically a render graph.

Shaders enter as compiled payloads. SharpGPU does not ship a shader compiler. Swap-chain acquire and present report `ERHISwapChainStatus` and leave recreate to the caller. Unsupported factories throw `NotSupportedException` rather than substituting another execution path.

The maintained product is the RHI, the three backends, and the Vortice bindings those backends rely on. [samples/ComputeAndDraw](samples/ComputeAndDraw) is a headless compute-and-raster workload. [docs/SharpGPU/QuickStart.md](docs/SharpGPU/QuickStart.md) is the smallest transfer-pass example and shows the optional `SharpGPU.Scopes` and `SharpGPU.Builders` helpers. The low-level contract is the `SharpGPU` namespace itself.

## Backends

`ERHIBackend` is `Metal`, `Vulkan`, `DirectX12` or `Pending`. There is no Auto value. `RHIInstance.GetBackendByPlatform` returns a suggestion: DirectX 12 on Windows unless `bForceVulkan` is set, Metal on macOS and iOS, Vulkan on Linux and Android. The caller copies that value into `RHIInstanceDescriptor`. `Pending` cannot create an instance.

`RHIInstance.IsBackendSupported` checks the operating system only. It does not probe drivers or native libraries. Driver and feature availability show up when the device is created and when `RHIDevice.Capabilities` is read. A build that does not define `SHARPGPU_ENABLE_DX12` has no DirectX 12 path.

`RHIInstanceDescriptor` also carries the debug and validation switches, the native surface kind, and the requested graphics, compute and transfer queue counts. One `RHIInstance` owns every enumerated device. `GetDevice` returns a device by index. A queue is `RHIDevice.GetCommandQueue(ERHIPipelineType, index)`.

`ERHIDeviceState` is `Unknown`, `Operational`, `Lost`, `Removed` or `Reset`. `Lost` is a timeout or device-loss recovery. `Removed` is a physical adapter going away. `Reset` means previous resources are dead and must be created again. Native failures surface as `RHIException`, which carries `ERHIErrorCode`, the backend, the native code and message, and the device state.

`RHIAdapterIdentity` holds a LUID and a device UUID when the backend actually has them. `RequireMatch` is the check for “this swap chain or shared resource must land on that adapter”.

## Resources

GPU memory is a buffer or a texture. `RHIBufferDescriptor` carries byte size, element format, usage and `ERHIStorageMode`. `RHITextureDescriptor` adds mip count, `uint3` extent, pixel format, sample count and dimension. `ERHIStorageMode` is `GPULocal`, `HostUpload`, `GPUUpload`, `Readback` or `Memoryless`.

`CreateBuffer` and `CreateTexture` allocate committed resources. `CreatePlacedBuffer` and `CreatePlacedTexture` place a resource in an `RHIHeap` created from `RHIHeapDescription`. `ERHIHeapType` selects the kind of heap (`Default`, `BuffersOnly`, `TexturesOnly`, `RenderTarget`), not the CPU/GPU traffic direction; that direction is `ERHIStorageMode`. Sparse textures and buffers, residency and budget queries are separate memory capabilities. Call them only after the matching capability is available.

A view is a range plus an access mode on a resource the caller already owns. Buffers expose `CreateBufferView`. Textures expose a texture-view descriptor for mip and array ranges. Tensors and function libraries have their own view types. These views are not the backend descriptor heaps, pools or argument-buffer pools; those stay inside the backend.

Samplers are either `RHISampler` objects or static samplers embedded in a pipeline layout. A sampler-feedback map is paired with its texture at creation time. The encoder cannot rebind that pairing later.

## Shaders and pipelines

`RHIFunctionDescriptor` points at bytecode (`ERHIShaderPayloadKind`: `Dxil`, `SpirV`, `MslSource` or `MetalLibrary`), an entry name and an `ERHIFunctionType`. A function library compiles a payload once; `CreateFunction` then selects an entry. Ray tracing and work graphs consume libraries. Raster and compute library reuse is a capability, and it is unavailable on some backends.

Pipelines are immutable after creation.

- `RHIRasterPipelineDescriptor` locks sample count, color and depth formats, render state, an optional fragment function, a pipeline layout and a `RHIPrimitiveAssemblerDescriptor`. The assembler is either a vertex function plus vertex layouts, or a mesh function with an optional task function. An optional `RHIRasterAttachmentShaderAbiClaim` checks that the shader outputs and the pass attachments are the same contract.
- `RHIComputePipelineDescriptor` locks the thread-group size, the compute function and the layout. The descriptor carries the group size so Metal, DirectX 12 and Vulkan share one place to read it.
- Ray tracing, work graphs and machine learning have their own pipeline types. Machine-learning execution is a binary pipeline: an `RHIMLBinary` becomes an `RHIMLPipeline`, then a binding table, then an `RHIMLEncoder`. There is no public runtime op-graph API.

`RHIPipelineCache` imports and exports an opaque blob. The caller stores the bytes. A driver or GPU change makes old entries incompatible; import reports that instead of pretending the cache hit.

## Binding

`RHIPipelineLayoutDescriptor` names binding-table layouts, a push-constant size, optional static samplers, whether the layout is a local ray-tracing signature, and whether it includes a vertex layout. At record time the encoder binds `RHIBindingTable` objects. A table has a `Count` and `SetBindElement(..., arrayIndex)` for a finite bindless range.

DirectX 12 descriptor heaps, Vulkan descriptor pools and the Metal view pool are backend storage. They are not public types, and they are not resized as shader-visible heaps at runtime.

## Command recording

`RHICommandQueue.CreateCommandBuffer` returns the buffer. `Begin(string name)` starts recording; the name is the debug label. `End` closes it for submit.

A buffer has one active encoder. The pass begin call selects it, and the matching `End*Pass` clears it. The six encoders are:

| Pass | Begin / end |
|---|---|
| Transfer | `BeginTransferPass` / `EndTransferPass` |
| Compute | `BeginComputePass` / `EndComputePass` |
| Ray tracing | `BeginRaytracingPass` / `EndRaytracingPass` |
| Raster | `BeginRasterPass` / `EndRasterPass` |
| Machine learning | `BeginMLPass` / `EndMLPass` |
| Work graph | `BeginWorkGraphPass` / `EndWorkGraphPass` |

Raster recording covers draws, indirect draws, viewports and binding. Compute recording covers dispatch and indirect dispatch. Transfer recording covers copies and blits. Every encoder records `RHIBarrier` values: global, buffer or texture. Stage and access masks use Before/After pairs. Texture barriers also carry `ERHITextureLayout` (13 values, including copy, resolve, present and `Common`). A non-null source or destination queue is a cross-queue ownership transfer.

Indirect command buffers are a separate capability used to record draw streams in parallel and `Execute` them inside an encoder. Vulkan does not implement that contract.

## Submit and synchronization

`RHICommandQueue.Submit` takes `RHIQueueSubmitDescriptor`: command buffers, semaphore waits with a stage mask, signal semaphores, and an optional completion fence. An empty submit throws `ArgumentException` ("A queue submission must contain work or a synchronization operation."). The queue reserves semaphore and fence operations and rolls them back if the native submit fails. That reservation is internal. Callers fill the descriptor; they do not call reserve, commit or rollback themselves.

`RHIFence` is the CPU/GPU signal. `Wait` returns `ERHIFenceStatus` (`Success`, `NotReady` or `Undefined`), not a boolean. `Reset` is legal once the signal has completed. `RHISemaphore` is the GPU/GPU signal carried in the submit descriptor.

Timestamp, occlusion and pipeline-statistics results are read from `RHIQuery` after the GPU has written them. `TryGetTimestamp`, `TryGetOcclusion` and `TryGetPipelineStatistics` return false while the result is still pending. Calibrated timestamps are `QueryClockCalibration` on devices whose synchronization capability reports them. There is no CPU-clock substitute.

## Presentation

`RHISwapChainDescriptor` names the window handles, `ERHINativeSurfaceKind` (`Win32Hwnd`, `AppKitNsWindow`, `X11Window`, `WaylandSurface`, `UIKitUiWindow`, `AndroidNativeWindow` or `Headless`), extent, buffer count, format, present mode, frame rate, `FrameBufferOnly`, a surface generation and the present queue.

Acquire and present take their own descriptors and return `ERHISwapChainStatus`: `Success`, `NotReady`, `Timeout`, `Occluded`, `Suboptimal`, `OutOfDate`, `SurfaceLost` or `DeviceLost`. `Suboptimal` still presents. `OutOfDate`, `SurfaceLost` and `DeviceLost` need the caller to rebuild the swap chain or the device. The HAL does not do that rebuild, and it does not idle the queue on its own.

## Capabilities

`RHIDevice.Capabilities` is 14 domains, each a small object with tier, strategy, provenance and an unavailable reason:

`Raster`, `Binding`, `Synchronization`, `Memory`, `Storage`, `PipelineCache`, `Presentation`, `RayTracing`, `Mesh`, `MachineLearning`, `WorkGraph`, `IndirectCommandBuffer`, `Compute`, `FunctionLibrary`.

`ERHICapabilityTier` runs from `Unavailable` through `Tier4`. What a tier means is defined per domain in the [feature matrix](docs/SharpGPU/FeatureMatrix.md), not by a single ladder. `ERHICapabilityStrategy` says how the backend implements it (`CoreApi`, `NativeExtension`, `NativeSpecialized`, `NativeLibrary`). `ERHICapabilityProbeKind` says how that answer was obtained.

`RHIDeviceLimit` holds the alignments and maximums used on every upload and dispatch. The wider numeric limits are `RHICapabilityLimits.TryGetValue`. A missing key means this backend does not report that limit.

Exact questions that a domain tier cannot answer have their own queries: `QueryFormatSupport`, `QueryResolveSupport`, `QueryRasterAttachmentSupport`, and the cooperative-matrix configuration query. Ray tracing is several facets (pipeline, inline, opacity micromap, motion), not one tier number. Storage queues, variable-rate shading, mesh shaders, work graphs and sampler feedback are the same kind of opt-in: the matrix names the type, and a missing native path throws.

## Qualification

A capability describes the device that was probed. `Passed`, `Failed`, `Unverified` and `BLOCKED_PLATFORM` describe a scenario run on a matching host. Those words live in the feature matrix and in [docs/VERIFICATION.md](docs/VERIFICATION.md). A successful native probe is not a passed qualification. The matrix is the list of which hosts have a current report and which are still blocked.

## Getting started

- Install the .NET SDK selected by [global.json](global.json). Runtime projects target .NET 10; compiler generators retain their declared build-time targets.
- For source development, copy [stack.local.props.example](stack.local.props.example) to `stack.local.props` and adjust the checkout paths. The template assumes sibling InfinityStack repositories; only mapped dependencies used by the chosen build need to be present. Product tests may require additional peers.
- Follow [docs/VERIFICATION.md](docs/VERIFICATION.md) for the authoritative build, test, pack and platform-specific commands. Start with the [standalone sample](samples/ComputeAndDraw).
- For package consumption, use `StackReferenceMode=Package` and an explicitly supplied feed containing the matching product versions. Source and package modes apply to the complete graph. Package availability is determined by published assets; this README does not assume a nuget.org release.

## Repository layout

`src/` owns runtime code, `samples/` runnable workloads, `docs/` the feature matrix, quick start and verification, and `eng/` verification automation. Tests and tools live in their own directories where applicable. [AGENTS.md](AGENTS.md) defines contribution rules and product boundaries.

Build outputs, isolated package caches and raw run evidence belong under ignored `artifacts/`. Commit source, reviewed lock files and portable configuration templates; keep machine paths in `stack.local.props`. Historical run summaries do not imply that their disposable output directories still exist.

Native inputs and hashes are recorded in [native/assets.json](native/assets.json); NuGet native inputs are restored by the build. Device support must be checked at runtime.

## License

[MPL-2.0](LICENSE). Existing copyright notices and third-party notices remain with their respective files. Extraction records and inherited notices are retained under [docs/provenance](docs/provenance).
