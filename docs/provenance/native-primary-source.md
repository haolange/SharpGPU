# Native source provenance correction — 2026-09-08

This record accompanies `native/assets.json`. It establishes archive/member
identity and preserved notices, not unconditional redistribution approval.
Product build and test commands remain in `docs/VERIFICATION.md`.

The DirectStorage, PIX and DirectML payloads were previously recorded against
Vortice repackaged archives. All ten DLLs have now been matched by SHA-256 to
the corresponding official package members. No native DLL was replaced.
Together with the existing official Agility origin, all 14 native payloads
now have primary package URLs, versions, archive hashes and member paths.

| Library | Official package | Archive SHA-256 |
|---|---|---|
| DirectStorage | Microsoft.Direct3D.DirectStorage 1.2.1 | FB1B6C2637722F9BE13978597EDADD22F0FABDCB3D1C6C8FB34E94F0E83B98B3 |
| PIX | WinPixEventRuntime 1.0.240308001 | 726ACC93D6968E2146261A1E415521747D50AD69894C2B42B5D0D4C29FD66EC4 |
| DirectML | Microsoft.AI.DirectML 1.15.2 | 9F07482559087088A4DBA4AE76EEEEE1FAD3F7077A92CCFBDB439C6BC2964C09 |

Five missing companion files were preserved from those official archives:

- DirectStorage: `NOTICES.txt`, `distributable_files.txt`, `LICENSE-CODE.txt`.
- DirectML: `ThirdPartyNotices.txt`, `LICENSE-CODE.txt`.

The DirectStorage file list names dstorage.dll and dstoragecore.dll. Its runtime
license includes application distribution requirements; retaining the file list
does not waive those requirements. DirectML's runtime license also has specific
use/distribution conditions. SDK header source licenses do not replace runtime
binary terms. PIX's existing license and third-party notices remain preserved.
No redistribution TODO was changed into a blanket approval by this correction.

Final Debug and Release locked builds succeeded. The actual rebuilt package
contains all 12 preserved license/notice/list files with identical bytes and all
14 manifest DLLs with matching hashes, and has no duplicate ZIP paths. Package
SHA-256: `0E0D5F7573E3BA4E3C63C94FE7F3DC1F0305B307990D745137C522442EA2C053`.
This is a local verification artifact, not a published release.

The separate Release package consumer uses 31 packages and zero source projects,
with a fresh cache and hashes checked against the explicit feed. Its DX12 and
Vulkan workload results are recorded in the extraction task ledger. This narrow
native-metadata/package correction does not claim full extraction acceptance or
requalify unmatched platforms.

Evidence is retained by the integration workspace under
`Engine/Intermediate/InfinityStack/20260907/native-primary-provenance`: original
official archives, payload comparisons, final build/pack logs and packaged
notice hashes. Existing historical feed results are retained separately.
