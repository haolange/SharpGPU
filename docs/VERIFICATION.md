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

## Agility UTF-8 and application deployment qualification

The current native-boundary regression is Dx12AgilityPathMarshallingTests.
The full source suite passed 292/292 in Debug and Release with no skipped tests.
Isolated output must supply the product source roots for architecture tests:

```powershell
$env:INFINITYSTACK_SHARPGPU_ROOT = $PWD.Path
$env:INFINITYSTACK_SHARPSHADER_ROOT = 'PATH_TO_SHARPSHADER_CHECKOUT'
$sourceProps = @(
  '-p:StackReferenceMode=Source', '-p:Platform=x64',
  "-p:StackProductRoot=$(Join-Path $PWD 'artifacts/agility-verification')",
  "-p:StackLocalProps=$(Join-Path $PWD 'stack.local.props')",
  '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
  '-p:RestoreUseStaticGraphEvaluation=false', '-m:1', '-nr:false'
)
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj -c Debug @sourceProps
dotnet test tests/SharpGPU.Conformance.Tests/SharpGPU.Conformance.Tests.csproj -c Release @sourceProps
dotnet pack src/SharpGPU/SharpGPU.csproj -c Release @sourceProps -o artifacts/agility-packages
```

The actual repaired package contains 14 RID-native entries, with zero duplicate
entries and no pack warnings. Source and fresh-cache package combined consumers
also pass real DX12/Vulkan compute and drawing after copying output to a new
Chinese/emoji directory and running from C:/Windows. The missing-application-SDK
negative case fails explicitly. These short API runs do not qualify a 55-second
window/resource scenario. Maintained binding details and exact evidence are in
docs/SharpGPU/VorticeAgilityPathPatch.md. Full package conformance and downstream
IE revalidation remain pending for this revision.

## DirectStorage path and pinned binding generation

After the DirectStorage long-path repair, Source Debug and Release each pass
293/293 with no skipped tests. SharpGPUDirectStorageQualifiedTests covers actual
buffer/texture readback, including a texture source longer than MAX_PATH. The
application SDK deployment test requires both SDK files in Source and Package;
Package also requires the RID-native payload. Current Package Release passes 289/289 with no skipped tests.

Direct3D12 and its DirectX/DXGI dependencies regenerate and compile with the
pinned SDK on the Windows x64 qualification host (zero errors, 172 warnings).
The generation output remains separate from checked-in generated files.

```powershell
$generationRoot = Join-Path $PWD 'artifacts/binding-regeneration'
dotnet build third_party/Vortice.Windows/src/Vortice.Direct3D12/Vortice.Direct3D12.csproj `
  -c Release -p:Platform=x64 -p:GenerateVorticeBindings=true `
  -p:StackReferenceMode=Source "-p:StackProductRoot=$generationRoot" `
  "-p:StackLocalProps=$(Join-Path $PWD 'stack.local.props')" `
  -p:UseSharedCompilation=false -p:NuGetAudit=false -m:1 -nr:false
```

Current full Package Release evidence is
agility-path-fix/package-conformance-storage-r2/test-results/package-storage-release-r2.trx
under the consuming IE task evidence root. Build the complete local feed first,
then restore into an empty cache using that single feed. Do not offer a global
package directory containing older same-version products as an additional feed;
compare restored product nupkg hashes to the intended feed before testing.

DirectML and DirectStorage also regenerate and compile through the same
GenerateVorticeBindings entry, with zero errors (13 and 1 warnings respectively).
The DirectStorage mapping warning for RegisterComponentMaskFlags is retained
in binding-regeneration-Vortice.DirectStorage.log; it is not suppressed.

FeatureReport_ShouldWriteJson writes to
<AppContext.BaseDirectory>/artifacts/SharpGPU/feature-report-<platform>-<arch>.json.
It never searches for InfinityBrowser.sln or writes a consuming repository's
tracked docs. Source and Package report tests both pass 2/2 after this ownership
cleanup; reports are under their respective isolated test output directories.
The explicit INFINITYSTACK_SHARPGPU_ROOT value is used only by the report's
path-privacy assertion, not to choose a write destination.

## Backend test ownership cutover (2026-09-08)

The independent conformance harness owns the migrated backend, barrier,
function-table, API and bytecode-ownership tests listed in
`docs/provenance/backend-test-migration.json`. No friend grant to
`Infinity.Rendering.Tests` remains. The six old engine-path enumeration tests
were not runtime coverage: their unused product helper was removed. Two new
isolated-load-context cases exercise the real native configuration validation,
resolution and first-use freeze behavior.

The source Debug and Release harnesses both pass 356/356, zero skipped, in the
consuming workspace evidence directory
`Engine/Intermediate/InfinityStack/20260907/agility-path-fix/test-results/`
(`full-cutover-Debug.trx`, `full-cutover-Release.trx`). These totals include
platform-guarded contracts; they do not qualify an Apple or Linux runtime on
Windows. Package revalidation and actual IE native-directory selection remain
pending. The FeatureMatrix documentation update after the builds requires a
resource rebuild; it does not alter backend code or the measured test cases.

## Native DLL long-path regression (2026-09-08)

After the native loader fix, complete Source Debug and Release each pass 356/356,
zero skipped (full-native-loader-Debug.trx and full-native-loader-Release.trx in
the consuming workspace agility-path-fix/test-results evidence directory).
The IE source integration also passes 252/252 in both configurations, including
actual process-module assertions for DirectML, PIX (Debug) and DirectStorage.
The original failure is retained as storage-load-error.trx: Windows returned
0x800700CE while loading the configured dstoragecore.dll. Extended-length paths
at the NativeLibrary boundary resolve it without shortening the fixture path.
Native Metal and other unmatched hosts are not qualified by these Windows runs.
Fresh Package Release verification passes 352/352, zero skipped, in
package-native-loader/test-results/package-native-loader.trx. The consumed
archive hash matches the feed archive:
DB3F41FA45258203849BAFA8876D149DE81B7D17012C4B43F7F53C4BD644CEAB.
The package contains 14 native entries and no duplicate ZIP entries. Current IE
Package and generated Host runtime qualification remains owned by the integration
workspace; older integration package evidence predates this loader fix.

## Windows source graph CI

The product-owned `eng/ci.json` lists the explicit build/test projects and
pins dependency commits only. The product's own checkout is not pinned inside
itself. `eng/Verify.ps1` accepts a directory containing named dependency
checkouts, validates their identities/HEADs, writes an isolated source mapping,
and restores with locked mode. It never updates a checkout or tracked lock.

```powershell
./eng/Verify.ps1 -Configuration Debug -Gate Build -DependencyRoot /path/to/checkouts -OutputRoot "$env:TEMP/graph-build-debug"
./eng/Verify.ps1 -Configuration Release -Gate Runtime -DependencyRoot /path/to/checkouts -OutputRoot "$env:TEMP/graph-runtime-release"
```

Repeat each selected gate in both configurations using fresh output directories.
Build compiles the listed tests, tools and samples but reports runtime NOT_RUN.
Runtime additionally runs every listed test project and requires one nonempty
TRX per project, with all tests passed and no skipped results. SharpNeural
explicitly enables its Vulkan target-face gate during Runtime validation.
The script restores modified process environment variables on exit.

The GitHub workflow runs Build on hosted Windows runners. Runtime is an
explicit workflow_dispatch on main using a trusted `infinitystack-gpu` Windows
x64 runner with the documented DX12/Vulkan devices and native prerequisites.
It is not run on pull requests. Apple, Linux and mobile qualification remain
separate matching-platform work. Remote execution is TODO(UNVERIFIED).

The Source entry reports its own source qualification. Package qualification is recorded separately by VerifyPackage.ps1; the trusted runtime workflow now requires both steps. Remote end-to-end workflow qualification is still TODO(UNVERIFIED). Native
assets and pinned dependency revisions must be available in the remote
checkouts, and clean-checkout lock convergence is still required before remote
qualification. No packages or native payloads are uploaded by this workflow.

Current new source-graph Release gate passed: Conformance 356/356 without
skips, plus MLCook, ComputeAndDraw and benchmark compilation. Expanded build
coverage found two stale benchmark API uses, repaired to current public EndPass
and pass-owned timestamp descriptors. The affected scoped transfer and DX12
timestamp benchmark cases ran successfully (2 warmups, 10 iterations), with
actual RTX 5090 for the timestamp case. Evidence is
D:/Projects/InfinityStackVerification/ci-graph-gpu-release-r3.
This small run validates behavior, not a reliable performance comparison.

The new Debug Runtime graph also passed, Conformance 356/356 with zero skips,
including benchmark compilation. Evidence: ci-graph-gpu-debug in the same
verification workspace. Release/Debug suite durations were 59/68 seconds;
these are aggregate test-suite durations, not the bounded 5/20/55-second
window/memory observation protocol. That separate runtime protocol is not
replaced by CI suite success.

## Isolated package runtime CI

`eng/VerifyPackage.ps1` consumes an explicit complete package feed. It copies
this product's real sample into an isolated application, clears inherited
build configuration/feed/fallback configuration, uses a fresh package cache,
restores again in locked mode, and rejects all project dependencies. Every
resolved nupkg must exist in the selected feed and its cached SHA-256 must
match that file. SDK implicit library-packs may still be probed by restore;
the explicit per-package hash requirement prevents qualifying a package absent
from the selected feed. Logs, resolved package hashes and result are retained.

```powershell
./eng/VerifyPackage.ps1 -Configuration Debug -PackageFeed /path/to/complete/feed -OutputRoot "$env:TEMP/package-debug"
./eng/VerifyPackage.ps1 -Configuration Release -PackageFeed /path/to/complete/feed -OutputRoot "$env:TEMP/package-release"
```

The trusted-device workflow runs this after its Source Runtime gate; supply
`package_feed` when requesting runtime qualification. The script performs no
package upload or source checkout. It validates the supplied feed's behavior
and records hashes, but does not assert that those packages were built from
the current source HEAD. Producing and publishing packages from the final
locked source set, remote execution and downloaded-Release consumption remain
separate required gates. A Source failure cannot be overridden by package PASS.

Current Windows Debug/Release package entries passed using the qualified local
feed: evidence ci-package-<product>-<configuration> in
D:/Projects/InfinityStackVerification. SharpGPU checks DX12 and Vulkan compute
readback and triangle draw; Shader checks nonempty DXIL from its compiler;
Neural runs both CPU and required GPU comparisons with release assertions.
Resolved package counts are 31 for GPU, 12 for Shader and 28 for Neural, with
zero source projects. Empty-feed Shader restore was also verified to fail
NU1101 and produce no PASS result. These are runtime sample gates, not a
replacement for the complete product test matrices or platform observation.

## Application notice deployment

After the normal source or package consumer restore/build, publish that same
fixture in both configurations, preserving its graph properties and package cache:

```powershell
dotnet publish $consumerProject -c $configuration --no-restore `
  -o $publishDirectory @consumerGraphProperties
```

Compare ThirdPartyNotices/<product>/... in build and publish output against the
source/package license inputs by exact relative paths and SHA-256. Do not accept
counts alone or package-cache files as application output. Test source and package
modes separately and run the published workload outside its source directory.
The 2026-09-08 Windows notice-deployment-r2 evidence in the extraction ledger
covers both configurations, both modes and build/publish: 25 combined files
(13 SharpGPU, 12 SharpShader), with every path/hash matching; the package GPU
workload also passed from the published directory. Platform claims remain separate.

## Mode-isolated NuGet locks

After deliberately generating and reviewing packages.<StackReferenceMode>.<RID-or-portable>.lock.json, append -p:RestoreLockedMode=true to the normal restore command. Alternate Source/Package restores and compare lock hashes. New RIDs need separate locks and matching-host qualification.

## Recovered debug binding sources

The IE duplicate-tree cleanup recovered three original C# files previously
hidden by the vendored `[Dd]ebug/` ignore rule: DXGI `IDXGIInfoQueue`, and
Direct3D11 `ID3D11InfoQueue`/`Message`. These are namespace folders, not build
outputs. The files retain their original copyright and exact contents; narrow
ignore exceptions keep them visible to Git. DXGI builds through the normal
SharpGPU source graph. Direct3D11 is an upstream binding project outside the
SharpGPU runtime dependency graph. Its standalone build uses the .NET 10 baseline,
explicit centrally managed SharpGen.Runtime, and the checked DirectX/DXGI
consumer mappings when those dependencies use their checked bindings.
The original NU1009 and subsequent missing consumer mapping failure are fixed.

```powershell
dotnet build third_party/Vortice.Windows/src/Vortice.Direct3D11/Vortice.Direct3D11.csproj @props
dotnet restore third_party/Vortice.Windows/src/Vortice.Direct3D11/Vortice.Direct3D11.csproj @props -p:RestoreLockedMode=true
```

2026-09-09 Windows x64: Source/Package, Debug/Release builds pass; both mode
locks restore successfully. A standalone executable using the resulting source
assembly completed 10 hardware D3D11 device/context create, ClearState, Flush,
and dispose cycles. This is a bounded device smoke, not a rendering qualification.
D3D11 still runs SharpGen at build time and requires Windows SDK 10.0.26100.0;
it does not claim the portable checked-generated build surface of the active
DX12 dependencies. Explicit GenerateVorticeBindings regeneration is a separate gate.

The recovered source does not alter previously generated package bytes. Existing
package qualification remains bound to its original packageCommit; publishing
new packages from this source requires a new version and the product package gates.
