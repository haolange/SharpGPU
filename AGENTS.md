# SharpGPU contributor instructions

## Authority

- This file owns contribution rules and product boundaries. [docs/VERIFICATION.md](docs/VERIFICATION.md) owns build, test, pack and platform qualification commands.
- Work on the checked-out branch, normally `main`. Do not create branches, PRs, remotes or publish without explicit user authorization.
- Preserve unrelated and uncommitted work. Do not rewrite Git history or discard source provenance during cleanup.

## Product boundary

SharpGPU builds independently of Infinity Engine. Product sources live under `src/`, owned tests under `tests/`, tools under `tools/` and examples under `samples/`. Original source and preserved history stay in [docs/provenance/extraction.json](docs/provenance/extraction.json) and [docs/provenance/commit-map.txt](docs/provenance/commit-map.txt). Missing dependencies fail; they do not fall back to another version. Products do not infer an Infinity Engine location or a developer drive from the current directory, do not duplicate a consuming workspace's revision manifest, and do not record their own commit inside themselves. The Infinity Engine integration records its checkout SHAs in its own root `stack.lock.json`. Current extraction acceptance is tracked by InfinityBrowser TASK-20260907-INFINITYSTACK-EXTRACTION; these instructions are not a claim that migration gates have passed.

Source and Package modes are explicit and graph-wide. Local checkout paths belong only in ignored `stack.local.props`. Package versions are owned by project configuration and mode-specific NuGet lock files named `packages.<StackReferenceMode>.<RID-or-portable>.lock.json` beside each project. The two modes do not share resolution state. NuGet owns target-framework sections within each lock. Configuration and Platform variants do not change package references. Reviewed locks are committed; verification uses `RestoreLockedMode`. Unsuffixed locks are retired. Output and intermediate paths are isolated by project, platform, RID, configuration and SDK target framework. Native packages use `runtimes/<rid>/native`; host integrations select their explicit deployment layout.

The public binding surface is `RHIBindingTable` with `Count` and `SetBindElement(..., arrayIndex)` for finite bindless. RHI does not grow Heap, Pool or View containers. DX12 gives every table group its own CPU mirror plus GPU segment and publishes on `SetBindingTable`; views and interned sampler slots occupy CPU staging only. Vulkan keeps a set per table and pages pools on the device. Metal fills argument tables or reference buffers and locks a private ViewPool at device create. Backends do not resize shader-visible GPU heaps or native pools at runtime. Preserve the maintained Vortice source and patches; do not replace them with an upstream package that lacks the custom behavior.

DX12 requires successful Agility device-factory initialization using the application D3D12 directory and UTF-8 paths. Source references and packages both deploy these assets; missing assets fail explicitly. The maintained binding and evidence are described in [docs/SharpGPU/VorticeAgilityPathPatch.md](docs/SharpGPU/VorticeAgilityPathPatch.md). Public native-backed operations enforce platform and ownership boundaries. No capability downgrade or compatibility implementation may conceal unsupported execution.

Backend implementation tests belong to the independent conformance harness. `Infinity.Rendering.Tests` has no product friend access. Test migration provenance is recorded in [docs/provenance/backend-test-migration.json](docs/provenance/backend-test-migration.json). Native configuration tests exercise actual Configure/Resolve behavior in isolated load contexts; product code does not contain a separate engine-path enumerator solely for tests. Windows native DLL loading uses extended-length local/UNC paths at the native boundary, while reported and configured locations remain canonical ordinary paths.

Product CI owns its project and test inventory and dependency-only pins in `eng/ci.json`. A hosted build and a real-device qualification are distinct results. Source CI cannot stand in for package consumption or another target platform. CI checks out only this product's build and test dependency closure: SharpMath, SharpMetal, and the GPU/Shader peer needed by integration tests. Neural and LLM are not checkout prerequisites. Runtime product dependency direction remains unchanged. The consuming workspace continues to own its stack revision manifest.

Application notice deployment uses `ThirdPartyNotices/<product>/` for both source and package consumers. Source Content metadata and package `buildTransitive` Content items copy the same license inputs during build and publish. Native DLL and Agility locations remain separate. Host staging must preserve these notice files; a license inside the nupkg cache alone does not satisfy this contract. Transitive application-deployment Content is excluded from downstream packing. A consumer must not repack another product's notice assets into framework-specific `contentFiles` that can hide its own portable resources.

## Implementation and verification

- Hand-written C# uses block namespaces, Allman braces, four spaces, `m_PascalCase` fields and `s_PascalCase` static fields. Generated/native bindings retain their declared conventions.
- Keep a single implementation path. Do not introduce legacy aliases, forwarding assemblies, compatibility shims or silent dependency fallbacks.
- Source/Package selection is graph-wide. Keep local checkout paths in ignored `stack.local.props`; update the portable template when its contract changes. Do not commit developer drive paths.
- Use current build/test/runtime evidence for behavior changes. Test the relevant error, cancellation and lifetime paths. Mark unavailable matching-platform execution `TODO(UNVERIFIED)` or `BLOCKED_PLATFORM`.
- First-party code and package metadata use Mozilla Public License 2.0 (MPL-2.0); preserve [LICENSE](LICENSE), source attribution and third-party licenses and notices. Update this file, [README.md](README.md), [README.en.md](README.en.md) and [docs/VERIFICATION.md](docs/VERIFICATION.md) together when their contracts change.

## 工程洁净度：第一性原则

洁净度以功能完整、内容必要、可重建、可追溯和维护成本为判断依据。清理应消除真实冗余；已有结果通过核验就应收口，不为追求形式上的“完美”制造新问题。

1. **一份职责，一处权威。** 源码、配置、命令与文档各有明确归属。迁移切换完成后，同步移除旧位置、重复实现、失效入口和兼容残留；消费者保留集成契约，产品规则归产品自身。
2. **产物必须有生命周期。** 创建构建输出、隔离缓存、测试工程或归档前，确定归属目录与清理时机。优先使用所属工程的忽略目录；确需写到 TEMP 或工程外时记录具体路径，结束时一并收敛，避免每轮留下新的整套副本。
3. **保留重建能力，减少重复占用。** 保留源码、锁定依赖、必要原生输入、可移植配置与重建入口。判断是否可删须查实际消费者、唯一性和可恢复来源；目录名为 Intermediate、Debug 或 backup，文件体积大或被 Git 忽略，都不能单独证明它是垃圾。
4. **文档保存结论与依据。** 长期文档保留当前状态、决策、复现入口和必要证据标识；原始日志、截图、逐帧数据和重复流水账按任务需要限期保留。瘦身时合并重复内容、修复引用，保留失败与未完成边界；删除附件后不得继续承诺其仍在本地。
5. **忽略规则也是工程接口。** 同时验证“生成物不会上传”和“源码、模板、必要资产不会被误屏蔽”。不能用宽泛目录名或扩展名规则掩盖文件归属问题；本机绝对路径放入不提交的本地配置，可共享模板使用可迁移路径。
6. **删除以已核实的范围为单位。** 先核对绝对路径边界、重解析点、Git 状态、使用中的文件及保留项；在已有授权内执行，不覆盖无关修改。唯一未提交成果、必要依赖、最后一份历史或恢复材料须明确处置，不能混入普通缓存清理；确需额外确认时说明具体对象与原因。
7. **历史与发布须内容完整。** 保留许可证、署名、既有 Git 历史及所需 LFS 对象；创建仓库不等于上传完成。发布应核对远端名称、可见性、分支、提交 SHA 和关键文件；不得用不完整推送、空占位文件或改写历史掩盖缺失内容。
8. **验证与改动相称，完成后停止扩散。** 清理核验目标已删除、保留项未损坏及实际占用；文档和配置检查引用、解析与忽略规则，行为变化再执行相应编译和实跑。记录实际失败与明确保留项；除非输入变化或出现新证据，不反复跑同一矩阵，也不为验证清理重新制造整批大产物。
