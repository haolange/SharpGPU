# macOS Continuation Prompt: Metal 4 ML Native Conformance

Use this prompt on macOS only. Do not broaden the task into general Metal cleanup.

Objective:
Implement and verify the native Metal 4 ML package path for SharpGPU.

Confirmed state:
- Windows cannot mark Metal conformance as verified.
- Metal 4 exposes native entry points for machine learning encoding, tensors, and counter heaps.
- Commit `0cef21f6` verified Metal timestamp/occlusion query contracts on macOS ARM64 and emits `Engine/Artifacts/SharpGPU/feature-report-macos-arm64.json`.
- `IsPipelineStatsQueriesSupported=false` is correct when no Metal statistics counter set is available.
- Metal ML is currently honest false/throws until SharpGPU has a native ML package program path.
- SharpGPU public readiness requires honest capability probing, native path execution, and conformance JSON evidence.

Scope:
1. ML conformance:
   - Probe MTL4 ML/tensor support.
   - Add a minimal MTL4 ML tensor + pipeline + binding + command encoder conformance.
   - Verify output against a CPU reference.
2. Report:
   - Emit/update macOS feature JSON beside the existing SharpGPU conformance artifact.
   - Update `docs/SharpGPU/FeatureMatrix.md` only after conformance passes on macOS.

Out of scope:
- No public API rename or breaking change.
- No rework of the already verified Metal timestamp/occlusion query contract unless it regresses.
- No Vulkan ML work.
- No broad Metal renderer refactor.
- No sample-only proof without readback.

Required verification:
Use only `docs/Canonical/VERIFICATION.md`. Add or update the Metal conformance command there only after the macOS tests pass locally.
