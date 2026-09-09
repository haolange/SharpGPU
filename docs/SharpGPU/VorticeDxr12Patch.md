# Vortice DXR 1.2 Binding Patch Manifest

This document limits the vendored Vortice maintenance surface for SharpGPU DXR 1.2 binding enablement (`ADR-0067`, G4-T12).

## Scope

Only DXR 1.2 / Opacity Micromap binding files under
`third_party/Vortice.Windows` are in scope.
Do not mix unrelated D3D12 binding cleanup into this patch.
Do not implement SharpGPU OMM / SER / Motion object models here (G4-T13..T15).

## Files

- `src/native/include/directx/d3d12.h` — TIER_1_2 + OMM types copied from Agility `Microsoft.Direct3D.D3D12` 1.619.3 `build/native/include/d3d12.h` (values not invented)
- `src/Vortice.Direct3D12/Mappings.xml` — OMM enum-item names (override SharpGen `OM`→OutputMerger expansion), bitfield struct bind/remove
- `src/Vortice.Direct3D12/Constants.cs` — OMM alignment / OC1 subdivision constants
- `src/Vortice.Direct3D12/RaytracingOpacityMicromapDescription.cs` — handwritten 8-byte bitfield layout
- SharpGen-generated enum/struct mappings from the refreshed header (`RaytracingTier.Tier1_2`, OMM enums/structs, geometry / AS / pipeline / ray flag additions)

## Out of scope

- SER / HitObject C# types: absent from retail 1.619.3 headers
- DX12 motion-blur AS API: no standard D3D12 motion AS surface
- Agility NuGet bump: 1.619.3 already contains TIER_1_2 / OMM
- Full wholesale replace of vendored `d3d12.h` with the entire 1.619.3 MIDL header (Device15 / SODB / unrelated Agility 1.619 interfaces)

## Upstream-Ready Diff Rule

Generate the upstream diff from the vendored Vortice directory inside this SharpGPU checkout:

```powershell
git diff -- src/native/include/directx/d3d12.h src/Vortice.Direct3D12/Mappings.xml src/Vortice.Direct3D12/Constants.cs src/Vortice.Direct3D12/RaytracingOpacityMicromapDescription.cs
```

The patch is upstream-ready only when:

- It compiles inside `third_party/Vortice.Windows`.
- It does not depend on SharpGPU runtime types.
- It contains DXR 1.2 / OMM binding additions only.
- Enum and struct numeric values match Agility 1.619.3 headers.

## Current Status

Agility retail package remains `Microsoft.Direct3D.D3D12` 1.619.3 (`ADR-0050` Binaries/ThirdParty layout unchanged).
SER / HitObject stay unimplemented until a header that actually declares those types is adopted.
