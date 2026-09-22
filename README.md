# SharpGPU

SharpGPU is a .NET 10 GPU hardware abstraction for DirectX 12, Vulkan and Metal. It supplies explicit mechanisms that map onto those APIs. Pass topology, barrier inference, memory aliasing and transient-resource lifetime stay with the caller. SharpGPU does not compile shaders, and it does not recreate a swap chain when present fails.

The public backend is an explicit `ERHIBackend`. There is no Auto value. `RHIInstance.GetBackendByPlatform` returns a suggestion; the caller writes that value into `RHIInstanceDescriptor`. `ERHIBackend.Pending` cannot create an instance. `RHIInstance.IsBackendSupported` checks the operating system only. DirectX 12 is omitted from a build that does not define `SHARPGPU_ENABLE_DX12`.

A frame goes from Instance to Device to Queue, then to resources and immutable pipelines, then to a command buffer. Six encoders exist: Transfer, Compute, RayTracing, Raster, Machine Learning and Work Graph. The public binding surface is `RHIBindingTable`. `SharpGPU.Scopes` and `SharpGPU.Builders` are optional convenience layers. Start from the [ComputeAndDraw sample](samples/ComputeAndDraw) and the [quick start](docs/SharpGPU/QuickStart.md).

`RHIDeviceCapabilities` publishes 14 domains for the current device only. `Passed` and `BLOCKED_PLATFORM` belong to the [feature matrix](docs/SharpGPU/FeatureMatrix.md) and [verification](docs/VERIFICATION.md). A successful capability probe is not a passed qualification scenario. The matrix records which hosts are still `BLOCKED_PLATFORM`.

Unsupported factories throw `NotSupportedException`. The implementation does not silently downgrade or substitute another execution path. Device state is Operational, Lost, Removed or Reset. Swap-chain acquire and present return `ERHISwapChainStatus` and leave recreate to the caller.

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
