# SharpGPU verification

This file is the authority for the independent SharpGPU repository. All
commands use the .NET 10 SDK and run from the repository root. `Source` and
`Package` are graph-wide modes; do not combine project references from one mode
with packages from the other. `stack.local.props` is an ignored machine
mapping. A handoff records the corresponding `stack.lock.json` revisions.

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
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj \
  @props --no-build --no-restore \
  --logger "trx;LogFileName=source-release.trx" \
  --results-directory "$out/test-results/source-release"
```

On the current Windows x64 host the Release source build completed with zero
errors and the full conformance run passed **289/289**. It exercised the
DirectX 12 and Vulkan compute/draw paths, native memory and synchronization,
pipeline cache, DirectML, DirectStorage, mesh and optional feature contracts.
The expected RTX 5090 Vulkan local-read capability message is recorded as
`BLOCKED_PLATFORM` by the test and is not converted into a pass claim.

The Debug source build completed with zero errors (224 compiler warnings in the
test project). A full Debug run is **BLOCKED_PLATFORM**: the native Vulkan
test host has nondeterministic access violations after multiple independent
Vulkan instances (`vkAllocateCommandBuffers` / `vkCreatePipelineLayout`).
The focused Vulkan memory test passes 1/1, the generated-binding test passes
alone, and Release passes 289/289. Evidence is retained under
`artifacts/verification-r12/test-debug-full-r7.log`,
`test-debug-full-r8-single-node.log`, and
`test-debug-memory-vulkan-r1.log`; this is a host/driver qualification
boundary, not a skipped assertion or a Debug pass.

The independent Release sample is a real compute and draw workload. From a
different working directory it selected the RTX 5090 for DirectX 12 and
Vulkan, returned `dispatch/readback=41`, drew one triangle with
`pixelInvocations=16`, and disposed compute/draw resources. The captured
output is `artifacts/verification-r12/sample-source-release-r2.log`.

## Package graph and native assets

Pack the library and its five custom Vortice packages from the same source
revision. SharpGPU owns the patched bindings; the upstream Vortice identities
must not enter the graph.

```powershell
$out = Join-Path $PWD "artifacts/verification-r12"
$props = @(
  "-p:Configuration=Release", "-p:Platform=x64",
  "-p:StackReferenceMode=Source", "-p:StackProductRoot=$out",
  "-p:StackLocalProps=$(Join-Path $PWD 'stack.local.props')",
  "-p:RestoreUseStaticGraphEvaluation=false", "-p:NuGetAudit=false",
  "-m:1", "-nr:false"
)
dotnet pack src/SharpGPU/SharpGPU.csproj @props -o "$out/packages-release"
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
$gpu = "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/packages-release-r2"
$shader = "D:/Projects/InfinityStack/SharpShader/artifacts/verification-r12/packages-release-r2"
$math = "D:/Projects/InfinityStack/SharpMath/artifacts/packages"
$metal = "D:/Projects/InfinityStack/SharpMetal/artifacts/packages"
$ie = "D:/Engines/InfinityBrowser/Engine/Intermediate/InfinityStack/20260907/packages-final"
$cache = "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/nuget-cache-package-gpu-isolated"
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
dotnet restore tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props \
  --source $gpu --source $shader --source $math --source $metal --source $ie
dotnet build tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props --no-restore
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj @props \
  --no-build --no-restore --logger "trx;LogFileName=package-full.trx" \
  --results-directory "D:/Projects/InfinityStack/SharpGPU/artifacts/verification-r12/test-results/package-full"
```

The isolated Package graph passed **285/285**. This includes the embedded
feature matrix/native manifest checks, RID layout checks, all portable contract
tests, and the Windows DX12/Vulkan workloads. The matching run is
`artifacts/verification-r12/test-results/package-full-isolated-r5/package-full-isolated-r5.trx`.

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
contains `SharpGPU.Vortice.*` rather than upstream Vortice replacements. A
changed runtime or package revision invalidates the corresponding source,
package and IE consumer evidence and requires a fresh run.
