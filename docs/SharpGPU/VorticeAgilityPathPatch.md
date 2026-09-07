# Vortice Agility UTF-8 path binding

The pinned custom Direct3D12 binding owns the SDK path marshaling for
ID3D12SDKConfiguration1.CreateDeviceFactory. SDK paths use UTF-8; the generated
ANSI conversion corrupted paths containing characters outside the Windows code
page. The maintained Mappings.xml removes this method from generation, while
ID3D12SDKConfiguration1.cs supplies the same COM slot 4 and public overloads.
The manually maintained implementation allocates UTF-8 memory, rejects embedded
null characters and frees the allocation in finally. The checked generated
file omits only the superseded method. Direct3D12 regeneration and compilation pass using the pinned SDK. Ordinary
builds compile the checked generated sources.

Microsoft's Independent Devices specification explicitly permits an absolute
path for CreateDeviceFactory. SetSDKVersion's relative-path restriction is a
separate API contract. Sources:

- https://microsoft.github.io/DirectX-Specs/d3d/IndependentDevices.html
- https://microsoft.github.io/DirectX-Specs/d3d/D3D12Redistributable.html

SharpGPU resolves D3D12 from AppContext.BaseDirectory and requires successful
factory initialization for device creation and debug configuration. Missing or
invalid SDK assets fail with the initialization diagnostic. Initialization
publishes its completed state only after the factory or failure is recorded.
The operating system can select a newer system runtime according to the SDK
contract; module location alone is therefore not a universal version assertion.

Source ProjectReference consumers receive the selected Agility assets as
application-local Content, because NuGet buildTransitive targets do not flow
through project references. The package retains both RID asset sets. Each selected file has one Content item
with an application-local Link and a canonical RID PackagePath. Package consumers retain buildTransitive
application deployment. The asset hashes and upstream versions are unchanged.

Windows x64 evidence is under the consuming IE task's
Engine/Intermediate/InfinityStack/20260907/agility-path-fix directory:

- Native COM-boundary UTF-8 tests pass 2/2, including Chinese and emoji paths.
- Full Source Debug passes 292/292, skipped 0 (full-debug-r2.trx).
- Moved complete output under a Chinese/emoji directory runs from C:/Windows
  and completes real DX12/Vulkan compute and draw work in 5091 ms, exit 0.
- A separate copy missing D3D12/D3D12Core.dll fails explicitly before device
  creation, even though the RID-scoped copy remains present.

The first full Debug invocation omitted the source-root environment variable
and failed 10 source-discovery cases; its evidence is retained. Release also passes 292/292, skipped 0. Repack r4 retains all 14 native files
without duplicate entries or pack warnings. Final source-copy and fresh empty-cache package builds both run successfully
after relocation to Chinese/emoji directories, with C:/Windows as working
directory. Source exits 0 in 8493 ms; Package exits 0 in 8541 ms. Both load
D3D12Core from their own application D3D12 directory. Package evidence is
D:/Projects/InfinityStackVerification/20260907-combined-package-r4/run-moved.
Authoritative commands remain in docs/VERIFICATION.md.

The later full-package run exposed a separate DirectStorage MAX_PATH failure
for a 265-character texture path. Dx12StorageQueue normalizes absolute paths
and adds the Windows extended-length prefix when necessary, preserving an
existing extended prefix and converting UNC syntax correctly. The texture
readback regression now runs normal and deliberately long paths. Source Debug
and Release pass 293/293 after this repair; preceding combined-consumer evidence
above predates this additional edit. Current full Package Release passes 289/289 with no skipped tests, using one isolated feed and a verified restored package hash.

The binding-generation repairs separate Import SDK name/version and omit the
central SharpGen.Runtime declaration only when the SDK supplies its implicit
pinned dependency. Direct3D12 regeneration passes with zero errors; regenerated
FreeUnusedSDKs remains at slot 5 and no duplicate CreateDeviceFactory is emitted.

Windows file-path contract:
https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation
DirectStorage native file API:
https://learn.microsoft.com/en-us/windows/win32/dstorage/dstorage/nf-dstorage-idstoragefactory-openfile
