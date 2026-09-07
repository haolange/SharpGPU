# Vortice Vulkan dispatch-table lifetime patch

SharpGPU pins `Vortice.Vulkan` **3.2.1** for its Vulkan ABI. That assembly keeps
instance and device dispatch tables in private static
`ConcurrentDictionary` fields. The native `vkDestroyDevice` and
`vkDestroyInstance` paths do not remove the corresponding entries. Reusing a
process for independent Vulkan instances can therefore call through a stale
dispatch table; on the Windows qualification host this appeared as an access
violation in `vkAllocateCommandBuffers` or `vkCreatePipelineLayout`.

`src/SharpGPU/Vulkan/VulkanUtility.cs` owns the lifecycle boundary. After the
native destroy call returns, it removes the destroyed handle from both the
SharpGPU registry and the pinned Vortice table. The integration resolves the
private fields by exact name and exact generic type. If a future Vortice build
changes that layout, type initialization fails with an actionable exception;
it never silently continues with an unverified binding.

The regression is
`VulkanDispatchTableLifetimeQualifiedTests.Vulkan_RecreatingWithDifferentValidationModes_RebuildsDispatchTables`.
It creates and disposes headless instances in the sequence
`validation=false → true → false → true`, allocates a graphics command buffer
for each instance, and runs in both source and package graphs. Current Windows
x64 evidence is:

- Source Debug: 290/290.
- Source Release: 290/290, with `INFINITYSTACK_SHARPGPU_ROOT` explicitly set
  for the isolated output root.
- Isolated Release package: 286/286, using a private NuGet cache and a local
  feed containing the fixed SharpGPU package.

Changing the pinned Vulkan package or its private table layout requires a new
source and package qualification run before the revision can be accepted.