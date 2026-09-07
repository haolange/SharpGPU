# SharpGPU

SharpGPU is Infinity's low-level GPU HAL for DirectX 12, Vulkan, and Metal-oriented runtime work.

The public package is intended for source/API/runtime readiness validation. Backend feature claims are governed by runtime capability probing and `SharpGPU.Conformance.Tests`; unsupported features must fail explicitly instead of silently no-oping.

Start with:

- `docs/SharpGPU/QuickStart.md`
- `docs/SharpGPU/FeatureMatrix.md`
- `docs/VERIFICATION.md`

Current public contract rows are in `docs/SharpGPU/FeatureMatrix.md` (21 RHI domains; pipeline ABI 9; feature-report schema 6). Qualification results and platform boundaries live in `docs/VERIFICATION.md`.
