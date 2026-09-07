# SharpGPU product design

SharpGPU builds independently of Infinity Engine. Product sources live under src, owned tests under tests, tools under tools and examples under samples. See docs/provenance/extraction.json and commit-map.txt for original source and preserved history.

The runtime baseline is .NET 10; compiler generators retain their appropriate netstandard target. Source and Package dependency modes are explicit and graph-wide. Local source paths belong only in ignored stack.local.props; stack.lock.json records the shareable revision set. Missing dependencies must fail rather than fall back to another version.

Output and intermediate paths are isolated by project, platform, RID, configuration and SDK target framework. Native packages use runtimes/<rid>/native; host integrations select their explicit deployment layout. Products must not infer Infinity Engine location or a developer drive from the current directory.

Public native-backed operations enforce platform and ownership boundaries. No capability downgrade or compatibility implementation is permitted to conceal unsupported execution. Current extraction acceptance is tracked by InfinityBrowser TASK-20260907-INFINITYSTACK-EXTRACTION; this document is not a claim that migration gates have passed.
