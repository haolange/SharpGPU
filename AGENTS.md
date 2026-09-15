# SharpGPU contributor instructions

## Authority

- [DESIGN.md](DESIGN.md) owns product architecture. [docs/VERIFICATION.md](docs/VERIFICATION.md) owns build/test/pack and platform qualification commands.
- Work on the checked-out branch, normally `main`. Do not create branches, PRs, remotes or publish without explicit user authorization.
- Preserve unrelated and uncommitted work. Do not rewrite Git history or discard source provenance during cleanup.

## Product boundary

Keep RHI and backend boundaries explicit. Do not expose backend Heap/Pool/View containers on the public RHI surface. Preserve the maintained Vortice source and patches; do not replace them with an upstream package that lacks the custom behavior.

## Implementation and verification

- Hand-written C# uses block namespaces, Allman braces, four spaces, `m_PascalCase` fields and `s_PascalCase` static fields. Generated/native bindings retain their declared conventions.
- Keep a single implementation path. Do not introduce legacy aliases, forwarding assemblies, compatibility shims or silent dependency fallbacks.
- Source/Package selection is graph-wide. Keep local checkout paths in ignored `stack.local.props`; update the portable template when its contract changes. Do not commit developer drive paths.
- Use current build/test/runtime evidence for behavior changes. Test the relevant error, cancellation and lifetime paths. Mark unavailable matching-platform execution `TODO(UNVERIFIED)` or `BLOCKED_PLATFORM`.
- First-party code and package metadata use Mozilla Public License 2.0 (MPL-2.0); preserve [LICENSE](LICENSE), source attribution and third-party licenses and notices. Update README/design/verification when their contracts change.

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
