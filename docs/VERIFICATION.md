# SharpGPU verification

This file is the authority for the independent SharpGPU repository. All
commands use the .NET 10 SDK and run from the repository root. `Source` and
`Package` are graph-wide modes; do not combine project references from one mode
with packages from the other. `stack.local.props` is an ignored machine
mapping. A handoff records the corresponding the consuming workspace revision manifest revisions.

## Windows x64 source gates

The following is the reproducible source graph command shape. The disposable
product root keeps output and intermediate files separated by project,
configuration, platform and RID.

```powershell
$root = $PWD.Path
$out = Join-Path $root "artifacts/verification-r12"
$props = @(
  "-p:Configuration=Release", "-p:Platform=x64",
  "-p:StackReferenceMode=Source", "-p:StackProductRoot=$out",
  "-p:StackLocalProps=$(Join-Path $root 'stack.local.props')",
  "-p:RestoreUseStaticGraphEvaluation=false", "-p:NuGetAudit=false",
  "-m:1", "-nr:false"
)

dotnet restore src/SharpGPU/SharpGPU.csproj @props
dotnet build src/SharpGPU/SharpGPU.csproj @props
dotnet restore tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props
dotnet build tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj `
  @props --no-build --no-restore `
  --logger "trx;LogFileName=source-release.trx" `
  --results-directory "$out/test-results/source-release"
```

On the current Windows x64 host the Release source build completed with zero
errors and the full conformance run passed **289/289**. It exercised the
DirectX 12 and Vulkan compute/draw paths, native memory and synchronization,
pipeline cache, DirectML, DirectStorage, mesh and optional feature contracts.
The expected RTX 5090 Vulkan local-read capability message is recorded as
`BLOCKED_PLATFORM` by the test and is not converted into a pass claim.

The Debug source build completed with zero errors (224 compiler warnings in the
test project). After the dispatch-table lifetime fix, the full Debug run passed
**290/290**. The Release source run with the same explicit
`INFINITYSTACK_SHARPGPU_ROOT` mapping also passed **290/290**. Both runs create
and destroy independent Vulkan instances with validation disabled/enabled in
alternating order and allocate a graphics command buffer after every creation.
The previously observed `vkAllocateCommandBuffers` / `vkCreatePipelineLayout`
access violations did not recur. Evidence is retained under
`artifacts/verification-r12/test-results/source-debug-full-after-vk-fix.trx` and
`source-release-after-vk-fix-root-mapped.trx`.

The independent Release sample is a real compute and draw workload. From a
different working directory it selected the RTX 5090 for DirectX 12 and
Vulkan, returned `dispatch/readback=41`, drew one triangle with
`pixelInvocations=16`, and disposed compute/draw resources. The captured
output is `artifacts/verification-r12/sample-source-release-r2.log`.

## Package graph and native assets

Pack the library and its five custom Windows Vortice packages from the same
source revision. SharpGPU owns those generated bindings, patches and
provenance. Vulkan remains pinned to the exact `Vortice.Vulkan` 3.2.1 ABI used
by the product; its native dispatch-table lifetime is guarded by the
SharpGPU integration and the private-table layout is checked at startup. Do
not substitute another Vulkan binding revision without rerunning the source
and package lifetime gates.

```powershell
$out = Join-Path $PWD "artifacts/verification-r12"
$props = @(
  "-p:Configuration=Release", "-p:Platform=x64",
  "-p:StackReferenceMode=Source", "-p:StackProductRoot=$out",
  "-p:StackLocalProps=$(Join-Path $PWD 'stack.local.props')",
  "-p:RestoreUseStaticGraphEvaluation=false", "-p:NuGetAudit=false",
  "-m:1", "-nr:false"
)
dotnet pack src/SharpGPU/SharpGPU.csproj @props -o "$out/packages-release-final"
```

The package contains `SharpGPU.FeatureMatrix.md` and
`SharpGPU.native.assets.json` as assembly resources, `native/assets.json`,
licenses and RID-scoped native files under
`runtimes/win-x64/native` and `runtimes/win-arm64/native`. The native manifest
records the source package, SHA-256, RID and IE deployment destination. The
Agility DLLs remain in the application deployment directory expected by D3D12.

Package consumption must use an isolated package root and a Release/x64
assets file. The configuration is part of the command: otherwise a prior
AnyCPU or global-cache assets file can make a package run appear green while
loading a different assembly.

```powershell
$gpu = "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/packages-release-final"
$shader = "D:/Projects/InfinityStack/SharpShader/artifacts/verification-r12/packages-release-final"
$math = "D:/Projects/InfinityStack/SharpMath/artifacts/verification-r12/packages-release-final"
$metal = "D:/Projects/InfinityStack/SharpMetal/artifacts/verification-r12/packages-release-final"
$ie = "D:/Engines/InfinityBrowser/Engine/Intermediate/InfinityStack/20260907/packages-final"
$cache = "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/nuget-cache-package-gpu-final"
$props = @(
  "-p:Configuration=Release", "-p:Platform=x64",
  "-p:StackReferenceMode=Package",
  "-p:StackProductRoot=D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12",
  "-p:StackLocalProps=D:/Projects/InfinityStack/SharpGPU/stack.local.props",
  "-p:RestorePackagesPath=$cache", "-p:NuGetPackageRoot=$cache",
  "-p:RestoreUseStaticGraphEvaluation=false", "-p:NuGetAudit=false",
  "-p:RestoreIgnoreFailedSources=true", "-p:RestoreForceEvaluate=true",
  "-m:1", "-nr:false"
)
dotnet restore tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props `
  --source $gpu --source $shader --source $math --source $metal --source $ie
dotnet build tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props --no-restore
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props `
  --no-build --no-restore --logger "trx;LogFileName=package-full.trx" `
  --results-directory "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/test-results/package-full"
```

The isolated Package graph built from the fixed SharpGPU package passed
**286/286**. This includes the embedded feature matrix/native manifest checks,
RID layout checks, all portable contract tests, the alternating-validation
Vulkan dispatch-table regression, and the Windows DX12/Vulkan workloads. The
matching run is
`artifacts/verification-r12/test-results/package-full-after-vk-fix-isolated.trx`,
with the Release/x64 restore and build output retained beside it. The maintained
binding record is `docs/SharpGPU/VorticeVulkanDispatchTablePatch.md`. The package
run uses a private NuGet cache and local feed so a global cache cannot supply an
older assembly with the same version.

## Platform boundary

Windows x64 is the only matching native host currently qualified. macOS ARM64
must run the Metal binding and native resource tests on an Apple host; Linux
must run the Vulkan loader/device tests on the target distribution; Android
and iOS require their native surface/loaders and mobile packaging projects.
These are `BLOCKED_PLATFORM` / `TODO(UNVERIFIED)` until matching hosts produce
logs. Apple system frameworks are supplied by the OS and are not copied into
the package. No platform gate is weakened to claim portability.

## Review and hygiene

Before accepting a revision, run `git diff --check`, inspect the package
contents and `native/assets.json` hashes, and verify that the dependency graph
contains the five `SharpGPU.Vortice.*` Windows packages and the pinned
`Vortice.Vulkan` 3.2.1 dependency, with no replacement or unpinned custom
binding. A
changed runtime or package revision invalidates the corresponding source,
package and IE consumer evidence and requires a fresh run.

Source handoff records this repository HEAD and every mapped dependency HEAD in
the consuming workspace manifest. IE uses its root stack.lock.json; standalone
consumers own their manifest and do not need an IE checkout. Package consumers
use the project dependency versions and NuGet lock files. There is no product-local
stack.lock.json: the removed copies were not read by any build or setup tool.
