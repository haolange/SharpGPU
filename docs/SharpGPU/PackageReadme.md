# SharpGPU

SharpGPU is Infinity's low-level GPU HAL for DirectX 12, Vulkan, and Metal-oriented runtime work.

The public package is intended for source/API/runtime readiness validation. Backend feature claims are governed by runtime capability probing and `SharpGPU.Conformance.Tests`; unsupported features must fail explicitly instead of silently no-oping.

Start with:
- `docs/SharpGPU/QuickStart.md`
- `docs/SharpGPU/FeatureMatrix.md`
- `docs/Canonical/VERIFICATION.md`

Current focus:
- DX12 WorkGraph dispatch/readback conformance.
- Feature contract matrix for Query, ML, RT, Mesh, Bindless, StorageQueue, PipelineLibrary, and WorkGraph.
- Backend encode and workload benchmarks with regression gates.
