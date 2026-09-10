# SharpGPU contributor instructions

## Authority

- [DESIGN.md](DESIGN.md) owns product architecture. [docs/VERIFICATION.md](docs/VERIFICATION.md) owns build/test/pack and platform qualification commands.
- Work on the checked-out branch, normally `main`. Do not create branches, PRs, remotes or publish without explicit user authorization.
- Preserve unrelated and uncommitted work. Do not rewrite Git history or discard source provenance during cleanup.

## Product boundary

Keep RHI and backend boundaries explicit. Preserve the maintained Vortice source and patches; do not replace them with an upstream package that lacks the custom behavior.

## Implementation and verification

- Hand-written C# uses block namespaces, Allman braces, four spaces, `m_PascalCase` fields and `s_PascalCase` static fields. Generated/native bindings retain their declared conventions.
- Keep a single implementation path. Do not introduce legacy aliases, forwarding assemblies, compatibility shims or silent dependency fallbacks.
- Source/Package selection is graph-wide. Keep local checkout paths in ignored `stack.local.props`; update the portable template when its contract changes. Do not commit developer drive paths.
- Use current build/test/runtime evidence for behavior changes. Test the relevant error, cancellation and lifetime paths. Mark unavailable matching-platform execution `TODO(UNVERIFIED)` or `BLOCKED_PLATFORM`.
- Store generated binaries, isolated caches and raw logs under ignored `artifacts/`; retain concise results and unresolved boundaries in docs. Clean only validated generated outputs, never unique source or required native inputs.
- Preserve [LICENSE](LICENSE), source attribution and third-party notices. Update README/design/verification when their contracts change.
