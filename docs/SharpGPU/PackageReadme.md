# SharpGPU

SharpGPU is a .NET 10 hardware abstraction over DirectX 12, Vulkan and Metal. The public surface is devices, resources, immutable pipelines, `RHIBindingTable`, command encoders, queues and presentation. Pass topology, barrier inference, memory aliasing and transient-resource lifetime stay with the caller. Shaders enter as `Dxil`, `SpirV`, `MslSource` or `MetalLibrary` payloads. SharpGPU does not compile them, and it does not recreate a swap chain when present fails.

`ERHIBackend` is `Metal`, `Vulkan`, `DirectX12` or `Pending`. There is no Auto value. `GetBackendByPlatform` is a suggestion. `IsBackendSupported` checks the operating system only. A build without `SHARPGPU_ENABLE_DX12` has no DirectX 12 path.

Create an `RHIInstance`, take a device, then a graphics, compute or transfer queue. Committed buffers and textures come from `CreateBuffer` and `CreateTexture`. Placed and sparse allocation, residency and budget queries are capabilities. Record into one command buffer with a single active encoder: Transfer, Compute, RayTracing, Raster, Machine Learning or Work Graph. `Submit` takes command buffers, semaphore waits, signal semaphores and an optional completion fence. `RHIFence.Wait` returns `ERHIFenceStatus`.

`RHIDeviceCapabilities` has 14 domains: Raster, Binding, Synchronization, Memory, Storage, PipelineCache, Presentation, RayTracing, Mesh, MachineLearning, WorkGraph, IndirectCommandBuffer, Compute and FunctionLibrary. Tier, strategy and provenance describe that device. `Passed` and `BLOCKED_PLATFORM` are qualification results in the feature matrix, not capability tiers. Unsupported factories throw `NotSupportedException`.

Machine-learning execution is an `RHIMLBinary` pipeline. The repository README and the feature matrix name the remaining opt-in contracts (mesh shaders, work graphs, opacity micromaps, motion, storage queues, variable-rate shading, sampler feedback).

Start with:

- `docs/SharpGPU/QuickStart.md`
- `docs/SharpGPU/FeatureMatrix.md`
- `docs/VERIFICATION.md`

Public contract rows live in `docs/SharpGPU/FeatureMatrix.md`. Qualification numbers live only in `docs/VERIFICATION.md`.
