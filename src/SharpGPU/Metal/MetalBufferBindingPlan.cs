using System;
using System.Collections.Generic;

namespace SharpGPU
{
    internal readonly struct MetalVertexBufferBinding
    {
        internal uint LogicalIndex { get; }
        internal ulong PhysicalIndex { get; }
        internal uint Stride { get; }

        internal MetalVertexBufferBinding(in uint logicalIndex, in ulong physicalIndex, in uint stride)
        {
            LogicalIndex = logicalIndex;
            PhysicalIndex = physicalIndex;
            Stride = stride;
        }
    }

    internal sealed class MetalRasterBufferBindingPlan
    {
        private readonly Dictionary<uint, MetalVertexBufferBinding> m_VertexBindings;

        internal IReadOnlyDictionary<uint, MetalVertexBufferBinding> VertexBindings => m_VertexBindings;

        internal MetalRasterBufferBindingPlan(
            Dictionary<uint, MetalVertexBufferBinding> vertexBindings)
        {
            m_VertexBindings = vertexBindings;
        }

        internal MetalVertexBufferBinding GetVertexBinding(in uint logicalIndex)
        {
            if (m_VertexBindings.TryGetValue(logicalIndex, out MetalVertexBufferBinding binding))
            {
                return binding;
            }

            throw new InvalidOperationException(
                $"Metal raster pipeline has no vertex-buffer layout for logical slot {logicalIndex}.");
        }
    }

    internal static class MetalBufferBindingPlanner
    {
        internal const int MaxBufferBindCount = 31;
        internal const int MaxBufferIndex = MaxBufferBindCount - 1;

        internal static void ValidatePipelineBufferBudget(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }
        }

        internal static MetalRasterBufferBindingPlan CompileRaster(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            ReadOnlySpan<RHIVertexLayoutDescriptor> vertexLayouts)
        {
            bool[] occupied = BuildPipelineBufferOccupancy(layouts);
            Dictionary<uint, MetalVertexBufferBinding> bindings = new(vertexLayouts.Length);

            int nextPhysicalIndex = MaxBufferIndex;
            for (int layoutIndex = 0; layoutIndex < vertexLayouts.Length; ++layoutIndex)
            {
                ref readonly RHIVertexLayoutDescriptor layout = ref vertexLayouts[layoutIndex];
                if (bindings.ContainsKey(layout.Index))
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline contains duplicate vertex-buffer logical slot {layout.Index}.");
                }

                while (nextPhysicalIndex >= 0 && occupied[nextPhysicalIndex])
                {
                    --nextPhysicalIndex;
                }

                if (nextPhysicalIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Metal raster pipeline requires {vertexLayouts.Length} vertex buffers, but the shared "
                        + $"Metal 4 buffer namespace has no free slot in [0, {MaxBufferIndex}] after shader resources.");
                }

                occupied[nextPhysicalIndex] = true;
                bindings.Add(
                    layout.Index,
                    new MetalVertexBufferBinding(
                        layout.Index,
                        checked((ulong)nextPhysicalIndex),
                        layout.Stride));
                --nextPhysicalIndex;
            }

            return new MetalRasterBufferBindingPlan(bindings);
        }

        internal static ulong GetDirectArgumentTableBufferBindCount(
            MetalArgumentTableLayout layout,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            ReserveDirectBufferBindings(occupied, layout);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReferenceRootBufferBindCount(
            ReadOnlySpan<MetalArgumentTableLayout> layouts,
            in MetalBindingPipelineType pipelineType)
        {
            bool[] occupied = BuildReferenceBufferOccupancy(layouts);
            if (pipelineType == MetalBindingPipelineType.Raytracing)
            {
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtVisibleFunctionTableSlot,
                    "visible function table");
                Reserve(
                    occupied,
                    MetalBindingHelpers.RtIntersectionFunctionTableSlot,
                    "intersection function table");
            }

            return GetRequiredBindCount(occupied);
        }

        internal static ulong GetReservedBufferOnlyBindCount(in MetalBindingPipelineType pipelineType)
        {
            return pipelineType switch
            {
                MetalBindingPipelineType.Raster or MetalBindingPipelineType.Raytracing =>
                    MaxBufferBindCount,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pipelineType),
                    pipelineType,
                    "Only Metal raster and ray tracing pipelines use reserved-only argument tables.")
            };
        }

        private static bool[] BuildPipelineBufferOccupancy(
            ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            if (MetalBindingHelpers.RequiresReferenceBuffers(layouts))
            {
                return BuildReferenceBufferOccupancy(layouts);
            }

            bool[] occupied = new bool[MaxBufferBindCount];
            if (layouts.Length == 1)
            {
                ReserveDirectBufferBindings(occupied, layouts[0]);
            }

            return occupied;
        }

        private static bool[] BuildReferenceBufferOccupancy(
            ReadOnlySpan<MetalArgumentTableLayout> layouts)
        {
            bool[] occupied = new bool[MaxBufferBindCount];
            for (int index = 0; index < layouts.Length; ++index)
            {
                MetalArgumentTableLayout layout = layouts[index];
                Reserve(
                    occupied,
                    layout.Index,
                    $"argument-table reference buffer {layout.Index}");
            }

            return occupied;
        }

        private static void ReserveDirectBufferBindings(
            bool[] occupied,
            MetalArgumentTableLayout layout)
        {
            ReadOnlySpan<MetalBindInfo> binds = layout.BindInfos;
            for (int index = 0; index < binds.Length; ++index)
            {
                ref readonly MetalBindInfo bind = ref binds[index];
                if (MetalArgumentTableValidation.GetDirectBindingNamespace(bind.Type)
                    != MetalArgumentTableValidation.BindingNamespace.Buffer)
                {
                    continue;
                }

                ulong end = checked((ulong)bind.Slot + bind.Count);
                if (end > MaxBufferBindCount)
                {
                    throw new InvalidOperationException(
                        $"Metal argument table {layout.Index} binding slot={bind.Slot}, type={bind.Type}, count={bind.Count} "
                        + $"exceeds the Metal 4 buffer index range [0, {MaxBufferIndex}].");
                }

                for (ulong physicalIndex = bind.Slot; physicalIndex < end; ++physicalIndex)
                {
                    Reserve(
                        occupied,
                        physicalIndex,
                        $"argument table {layout.Index} {bind.Type} binding");
                }
            }
        }

        private static void Reserve(bool[] occupied, in ulong physicalIndex, string owner)
        {
            if (physicalIndex > MaxBufferIndex)
            {
                throw new InvalidOperationException(
                    $"Metal {owner} uses buffer index {physicalIndex}, outside the Metal 4 range [0, {MaxBufferIndex}].");
            }

            int index = checked((int)physicalIndex);
            if (occupied[index])
            {
                throw new InvalidOperationException(
                    $"Metal {owner} collides at shared Metal 4 buffer index {physicalIndex}.");
            }

            occupied[index] = true;
        }

        private static ulong GetRequiredBindCount(bool[] occupied)
        {
            for (int index = occupied.Length - 1; index >= 0; --index)
            {
                if (occupied[index])
                {
                    return checked((ulong)index + 1UL);
                }
            }

            return 0;
        }
    }
}
