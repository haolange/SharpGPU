using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using SharpGPU;
using Xunit;

namespace SharpGPU.Conformance.Tests
{
    public sealed class MetalBindingTablePlanTests
    {
        [Fact]
        public void Layout_ShouldValidateCountsStagesDuplicatesAndDirectNamespaces()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateLayout(0, Element(0, ERHIBindType.Buffer, count: 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateLayout(0, Element(0, ERHIBindType.Buffer, stages: ERHIShaderStageMask.None)));
            Assert.Throws<ArgumentException>(() =>
                CreateLayout(
                    0,
                    Element(0, ERHIBindType.Buffer),
                    Element(0, ERHIBindType.Buffer)));
            Assert.Throws<ArgumentException>(() =>
                CreateLayout(
                    0,
                    Element(0, ERHIBindType.Buffer),
                    Element(0, ERHIBindType.UniformBuffer)));
            Assert.Throws<ArgumentException>(() =>
                CreateLayout(
                    0,
                    Element(0, ERHIBindType.Texture2D),
                    Element(0, ERHIBindType.StorageTexture2D)));

            using MetalBindingTableLayout valid = CreateLayout(
                7,
                Element(0, ERHIBindType.Buffer),
                Element(0, ERHIBindType.Texture2D),
                Element(0, ERHIBindType.Sampler));
            Assert.Equal(3, valid.BindInfos.Length);
            Assert.Throws<InvalidOperationException>(valid.ValidateReferenceBufferRanges);
        }

        [Fact]
        public void ReferenceLayout_ShouldUseRawSparseSlotsAndTableIndices()
        {
            using MetalBindingTableLayout arrayLayout = CreateLayout(
                4,
                Element(5, ERHIBindType.Texture2D, count: 2));
            using MetalBindingTableLayout sparseLayout = CreateLayout(
                9,
                Element(7, ERHIBindType.Sampler));

            Assert.True(arrayLayout.RequiresReferenceBuffer);
            Assert.Equal(7UL, arrayLayout.ReferenceBufferElementCount);
            Assert.Equal(6UL, MetalBindingHelpers.GetPhysicalBindingIndex(arrayLayout.BindInfos[0], 1));
            Assert.Equal(48UL, MetalBindingHelpers.GetReferenceByteOffset(arrayLayout.BindInfos[0], 1));
            Assert.Equal(8UL, sparseLayout.ReferenceBufferElementCount);
            Assert.Equal(
                10UL,
                MetalBufferBindingPlanner.GetReferenceRootBufferBindCount(
                    new[] { arrayLayout, sparseLayout },
                    MetalBindingPipelineType.Compute));
        }

        [Fact]
        public void BufferPlanner_ShouldEnforceUnifiedMetal4RangeAndMapVertexSlots()
        {
            using MetalBindingTableLayout resourceLayout = CreateLayout(
                0,
                Element(30, ERHIBindType.Buffer));
            RHIVertexLayoutDescriptor vertex = new()
            {
                Index = 7,
                Stride = 32,
            };

            MetalRasterBufferBindingPlan rasterPlan =
                MetalBufferBindingPlanner.CompileRaster(
                    new[] { resourceLayout },
                    new[] { vertex });
            MetalVertexBufferBinding binding = rasterPlan.GetVertexBinding(7);
            Assert.Equal(7u, binding.LogicalIndex);
            Assert.Equal(29UL, binding.PhysicalIndex);
            Assert.Equal(32u, binding.Stride);
            Assert.Throws<InvalidOperationException>(() =>
                rasterPlan.GetVertexBinding(8));
            Assert.Throws<InvalidOperationException>(() =>
                MetalBufferBindingPlanner.ValidatePipelineBufferBudget(
                    new[] { resourceLayout },
                    MetalBindingPipelineType.Raytracing));

            using MetalBindingTableLayout rangeConflict = CreateLayout(
                0,
                Element(28, ERHIBindType.Buffer, count: 2));
            Assert.True(MetalBindingHelpers.HasRayFunctionTableSlotConflict(
                rangeConflict.BindInfos));

            using MetalBindingTableLayout overflow = CreateLayout(
                0,
                Element(31, ERHIBindType.Buffer));
            Assert.Throws<InvalidOperationException>(() =>
                MetalBufferBindingPlanner.ValidatePipelineBufferBudget(
                    new[] { overflow },
                    MetalBindingPipelineType.Compute));
        }

        [Fact]
        public void ZeroLayoutRayTracing_ShouldReserveFunctionTableOnlyBindingTableCapacity()
        {
            Assert.Equal(
                31UL,
                MetalBufferBindingPlanner.GetReservedBufferOnlyBindCount(
                    MetalBindingPipelineType.Raytracing));
            Assert.Equal(
                31UL,
                MetalBufferBindingPlanner.GetReferenceRootBufferBindCount(
                    Array.Empty<MetalBindingTableLayout>(),
                    MetalBindingPipelineType.Raytracing));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MetalBufferBindingPlanner.GetReservedBufferOnlyBindCount(
                    MetalBindingPipelineType.Compute));
        }

        [Fact]
        public void PipelineLayout_ShouldSnapshotCallerArrayAndRejectPushConstants()
        {
            using MetalBindingTableLayout first = CreateLayout(
                1,
                Element(0, ERHIBindType.Buffer));
            using MetalBindingTableLayout replacement = CreateLayout(
                2,
                Element(0, ERHIBindType.Texture2D));
            RHIBindingTableLayout[] callerLayouts = { first };
            using MetalPipelineLayout pipeline = new(new RHIPipelineLayoutDescriptor
            {
                BindingTableLayouts = callerLayouts,
            });

            callerLayouts[0] = replacement;

            Assert.Equal(1, pipeline.BindingTableLayoutCount);
            Assert.Same(first, pipeline.BindingTableLayouts[0]);
            Assert.Same(first, pipeline.Descriptor.BindingTableLayouts[0]);
            Assert.Throws<NotSupportedException>(() =>
                new MetalPipelineLayout(new RHIPipelineLayoutDescriptor
                {
                    PushConstantSize = 4,
                    BindingTableLayouts = Array.Empty<RHIBindingTableLayout>(),
                }));
        }

        [Fact]
        public void BindingTable_ShouldFailClosedForUnknownArrayRequiredAndWrongBackendBindings()
        {
            using MetalBindingTableLayout bufferLayout = CreateLayout(
                3,
                Element(0, ERHIBindType.Buffer));
            using MetalBindingTable bufferTable = CreateTable(bufferLayout);

            bufferTable.SetBindElement(default, ERHIBindType.Buffer, 0);
            Assert.Throws<InvalidOperationException>(() =>
                bufferTable.SetBindElement(default, ERHIBindType.Buffer, 0, 0));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() =>
                bufferTable.SetBindElement(default, ERHIBindType.Buffer, 1));

            using TestBufferView wrongBackendView = new();
            Assert.Throws<ArgumentException>(() =>
                bufferTable.SetBindElement(
                    new RHIBindingTableElement { BufferView = wrongBackendView },
                    ERHIBindType.Buffer,
                    0));

            using MetalBindingTableLayout arrayLayout = CreateLayout(
                4,
                Element(2, ERHIBindType.Texture2D, count: 2));
            using MetalBindingTable arrayTable = CreateTable(arrayLayout);
            arrayTable.SetBindElement(default, ERHIBindType.Texture2D, 2);
            arrayTable.SetBindElement(default, ERHIBindType.Texture2D, 2, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                arrayTable.SetBindElement(default, ERHIBindType.Texture2D, 2, 2));

            using MetalBindingTableLayout requiredLayout = CreateLayout(
                5,
                Element(0, ERHIBindType.Sampler),
                Element(0, ERHIBindType.AccelStruct));
            using MetalBindingTable requiredTable = CreateTable(requiredLayout);
            InvalidOperationException requiredError =
                Assert.Throws<InvalidOperationException>(requiredTable.ValidateRequiredBindings);
            Assert.Contains("table 5", requiredError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("slot=0", requiredError.Message, StringComparison.Ordinal);
            Assert.Contains("arrayIndex=0", requiredError.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void BindingRequirement_ShouldControlReadinessAndStructuralIdentity()
        {
            using MetalBindingTableLayout requiredLayout = CreateLayout(
                8,
                Element(0, ERHIBindType.Buffer));
            using MetalBindingTableLayout equivalentRequiredLayout = CreateLayout(
                8,
                Element(0, ERHIBindType.Buffer));
            using MetalBindingTableLayout optionalLayout = CreateLayout(
                8,
                Element(
                    0,
                    ERHIBindType.Buffer,
                    requirement: ERHIBindingRequirement.Optional));
            using MetalBindingTable requiredTable = CreateTable(requiredLayout);
            using MetalBindingTable optionalTable = CreateTable(optionalLayout);

            Assert.Throws<InvalidOperationException>(
                requiredTable.ValidateRequiredBindings);
            optionalTable.ValidateRequiredBindings();
            Assert.True(requiredLayout.StructurallyEquals(
                equivalentRequiredLayout));
            Assert.False(requiredLayout.StructurallyEquals(optionalLayout));
        }

        [Fact]
        public void BindingTable_ShouldStoreOnlyBackendPrivateNativeSnapshots()
        {
            FieldInfo bindingsField = Assert.Single(
                typeof(MetalBindingTable).GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic),
                field => field.Name == "m_Bindings");
            Assert.Equal(typeof(MetalArgumentBindingSnapshot[]), bindingsField.FieldType);
            Assert.False(
                RuntimeHelpers.IsReferenceOrContainsReferences<MetalArgumentBindingSnapshot>());
            Assert.All(
                typeof(MetalArgumentBindingSnapshot).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => Assert.True(
                    field.FieldType.IsValueType,
                    $"Snapshot field {field.Name} must not retain a managed RHI object."));
            Assert.Null(typeof(MetalBindingTable).GetField(
                "m_Elements",
                BindingFlags.Instance | BindingFlags.NonPublic));
        }

        [Fact]
        public void BindingTable_ShouldRejectOperationsAfterItsLayoutIsDisposed()
        {
            MetalBindingTableLayout layout = CreateLayout(
                6,
                Element(0, ERHIBindType.Buffer));
            using MetalBindingTable table = CreateTable(layout);
            layout.Dispose();

            Assert.Throws<ObjectDisposedException>(() => table.GetBindCount());
            Assert.Throws<ObjectDisposedException>(() =>
                table.SetBindElement(default, ERHIBindType.Buffer, 0));
        }

        [Fact]
        public void TextureViewIndexAllocator_ShouldExhaustReuseAndRejectStaleOrDoubleRelease()
        {
            MetalTextureViewIndexAllocator allocator = new(capacity: 2);
            MetalTextureViewIndexLease first = allocator.Allocate();
            MetalTextureViewIndexLease second = allocator.Allocate();

            Assert.Equal(0u, first.Index);
            Assert.Equal(1u, second.Index);
            Assert.Throws<InvalidOperationException>(() => allocator.Allocate());
            Assert.True(allocator.Release(first));
            Assert.False(allocator.Release(first));

            MetalTextureViewIndexLease reused = allocator.Allocate();
            Assert.Equal(first.Index, reused.Index);
            Assert.True(reused.Generation > first.Generation);
            Assert.False(allocator.Release(first));
            Assert.True(allocator.Release(reused));
            Assert.True(allocator.Release(second));
        }

        private static MetalBindingTableLayout CreateLayout(
            uint index,
            params RHIBindingTableLayoutElement[] elements)
        {
            return new MetalBindingTableLayout(new RHIBindingTableLayoutDescriptor
            {
                Index = index,
                Elements = elements,
            });
        }

        private static MetalBindingTable CreateTable(MetalBindingTableLayout layout)
        {
            return new MetalBindingTable(new RHIBindingTableDescriptor
            {
                Layout = layout,
                Elements = Array.Empty<RHIBindingTableElement>(),
            });
        }

        private static RHIBindingTableLayoutElement Element(
            uint slot,
            ERHIBindType type,
            uint count = 1,
            ERHIShaderStageMask stages = ERHIShaderStageMask.Compute,
            ERHIBindingRequirement requirement =
                ERHIBindingRequirement.Required)
        {
            return new RHIBindingTableLayoutElement
            {
                Slot = slot,
                Count = count,
                Type = type,
                Stages = stages,
                Requirement = requirement,
            };
        }

        private sealed class TestBufferView : RHIBufferView
        {
        }
    }
}
