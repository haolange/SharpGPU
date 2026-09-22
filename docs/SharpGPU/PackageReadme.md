# SharpGPU

SharpGPU is a .NET 10 GPU hardware abstraction for DirectX 12, Vulkan and Metal. It supplies explicit mechanisms that map onto those APIs. Pass topology, barrier inference, memory aliasing and transient-resource lifetime stay with the caller. SharpGPU does not compile shaders, and it does not recreate a swap chain when present fails.

The public binding surface is `RHIBindingTable`. Unsupported factories throw `NotSupportedException` instead of silently substituting another execution path. `RHIDeviceCapabilities` publishes 14 domains for the current device only. `Passed` and `BLOCKED_PLATFORM` are qualification results, not capability tiers.

Start with:

- `docs/SharpGPU/QuickStart.md`
- `docs/SharpGPU/FeatureMatrix.md`
- `docs/VERIFICATION.md`

Public contract rows live in `docs/SharpGPU/FeatureMatrix.md`. Qualification numbers live only in `docs/VERIFICATION.md`.
