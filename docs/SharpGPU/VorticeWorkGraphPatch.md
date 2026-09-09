# Vortice WorkGraph Patch Manifest

This document limits the vendored Vortice maintenance surface for SharpGPU WorkGraph support.

## Scope

Only WorkGraph binding files under
`third_party/Vortice.Windows/src/Vortice.Direct3D12` are in scope.
Do not mix unrelated D3D12 binding cleanup into the upstream patch.

## Files

- `DispatchGraphDescription.cs`
- `ID3D12GraphicsCommandList10.WorkGraph.cs`
- `WorkGraphDescription.StateSubObject.cs`
- WorkGraph-related generated enum/struct mappings in `Mappings.xml`

## Upstream-Ready Diff Rule

Generate the upstream diff from the vendored Vortice directory inside this SharpGPU checkout:

```powershell
git diff -- src/Vortice.Direct3D12/DispatchGraphDescription.cs src/Vortice.Direct3D12/ID3D12GraphicsCommandList10.WorkGraph.cs src/Vortice.Direct3D12/WorkGraphDescription.StateSubObject.cs src/Vortice.Direct3D12/Mappings.xml
```

The patch is upstream-ready only when:

- It compiles inside `third_party/Vortice.Windows`.
- It does not depend on SharpGPU runtime types.
- It contains WorkGraph binding additions only.
- SharpGPU DX12 WorkGraph conformance passes against the vendored build.

## Current Status

TODO(UNVERIFIED): Capture the exact upstream base commit for the vendored Vortice tree before submitting the patch upstream.
