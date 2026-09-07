using Xunit;
using System;
using System.Linq;
using SharpGPU;
using System.Reflection;
using System.Collections.Generic;

namespace SharpGPU.Conformance.Tests
{
    public class RayFunctionTableApiTests
    {
        [Fact]
        public void RayRecordBuilder_PrimitivesAlignmentAndFill_AreLittleEndian()
        {
            RHIRayRecordBuilder builder = new RHIRayRecordBuilder(8);
            builder.WriteU32(0x11223344u);
            builder.WriteU64(0x0102030405060708ul);
            builder.WriteF32(1.0f);
            builder.WriteBytes(new byte[] { 0xAA, 0xBB, 0xCC });
            builder.AlignTo(8, 0x7F);

            byte[] bytes = builder.ToArray();
            Assert.Equal(24, bytes.Length);

            Assert.Equal(0x44, bytes[0]);
            Assert.Equal(0x33, bytes[1]);
            Assert.Equal(0x22, bytes[2]);
            Assert.Equal(0x11, bytes[3]);

            Assert.Equal(0x08, bytes[4]);
            Assert.Equal(0x07, bytes[5]);
            Assert.Equal(0x06, bytes[6]);
            Assert.Equal(0x05, bytes[7]);
            Assert.Equal(0x04, bytes[8]);
            Assert.Equal(0x03, bytes[9]);
            Assert.Equal(0x02, bytes[10]);
            Assert.Equal(0x01, bytes[11]);

            Assert.Equal(BitConverter.SingleToUInt32Bits(1.0f), BitConverter.ToUInt32(bytes, 12));
            Assert.Equal(0xAA, bytes[16]);
            Assert.Equal(0xBB, bytes[17]);
            Assert.Equal(0xCC, bytes[18]);
            for (int i = 19; i < 24; ++i)
            {
                Assert.Equal(0x7F, bytes[i]);
            }
        }

        [Fact]
        public void RayRecordBuilder_WriteResourceIndex_WritesStandardToken()
        {
            RHIRayRecordBuilder builder = new RHIRayRecordBuilder();
            builder.WriteResourceIndex(4, 2, 7);
            byte[] bytes = builder.ToArray();

            Assert.Equal(12, bytes.Length);
            Assert.Equal(4u, BitConverter.ToUInt32(bytes, 0));
            Assert.Equal(2u, BitConverter.ToUInt32(bytes, 4));
            Assert.Equal(7u, BitConverter.ToUInt32(bytes, 8));
        }

        [Fact]
        public void FunctionTableValidator_GroupIndexOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MetalRayFunctionTableValidator.ValidateRecord("Miss", 0, 2, 2, 0, 16));
        }

        [Fact]
        public void FunctionTableValidator_LocalDataLargerThanStride_Throws()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MetalRayFunctionTableValidator.ValidateRecord("Callable", 1, 0, 1, 20, 16));
        }

        [Fact]
        public void FunctionTableValidator_VisibleCallablePartition_ResolvesExpectedIndices()
        {
            int missCount = 3;
            int callableCount = 2;

            Assert.Equal(5, MetalRayFunctionTableValidator.ResolveVisibleTableCount(missCount, callableCount));
            Assert.Equal(0, MetalRayFunctionTableValidator.ResolveVisibleTableIndex(ERHIRayShaderTableSection.Miss, 0, missCount));
            Assert.Equal(2, MetalRayFunctionTableValidator.ResolveVisibleTableIndex(ERHIRayShaderTableSection.Miss, 2, missCount));
            Assert.Equal(3, MetalRayFunctionTableValidator.ResolveVisibleTableIndex(ERHIRayShaderTableSection.Callable, 0, missCount));
            Assert.Equal(4, MetalRayFunctionTableValidator.ResolveVisibleTableIndex(ERHIRayShaderTableSection.Callable, 1, missCount));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MetalRayFunctionTableValidator.ResolveVisibleTableIndex(ERHIRayShaderTableSection.Hit, 0, missCount));
        }

        [Fact]
        public void FunctionTableUpdateRecord_OnlyMutatesTargetRecord()
        {
            if (!RHIInstance.IsBackendSupported(ERHIBackend.Metal, out _))
            {
                // MetalFunctionTable requires a live MetalDevice; keep this
                // mutation coverage on matching Apple hosts only.
                return;
            }

            using RHIInstance instance = RHIInstance.Create(new RHIInstanceDescriptor
            {
                Backend = ERHIBackend.Metal,
                EnableDebugLayer = false,
                EnableValidation = false,
                ComputeQueueRequestCount = 0,
                TransferQueueRequestCount = 0,
                GraphicsQueueRequestCount = 1,
            });
            MetalDevice device = Assert.IsType<MetalDevice>(instance.GetDevice(0));
            MetalFunctionTable table = new MetalFunctionTable(device);
            table.SetRayGenerationRecord(new RHIRayRecordDescriptor(0, new byte[] { 0x10 }));
            table.AddMissRecord(new RHIRayRecordDescriptor(0, new byte[] { 0x21 }));
            table.AddMissRecord(new RHIRayRecordDescriptor(1, new byte[] { 0x22 }));
            table.AddHitGroupRecord(new RHIRayRecordDescriptor(0, new byte[] { 0x31 }));
            table.AddCallableRecord(new RHIRayRecordDescriptor(0, new byte[] { 0x41 }));

            MetalFunctionTableEntry rayGenBefore = CloneEntry(GetPrivateField<MetalFunctionTableEntry>(table, "m_RayGenerationRecord"));
            List<MetalFunctionTableEntry> missBefore = CloneEntries(GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_MissRecords"));
            List<MetalFunctionTableEntry> hitBefore = CloneEntries(GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_HitRecords"));
            List<MetalFunctionTableEntry> callableBefore = CloneEntries(GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_CallableRecords"));

            table.UpdateRecord(
                ERHIRayShaderTableSection.Miss,
                1,
                new RHIRayRecordDescriptor(1, new byte[] { 0xAA, 0xBB, 0xCC }));

            MetalFunctionTableEntry rayGenAfter = GetPrivateField<MetalFunctionTableEntry>(table, "m_RayGenerationRecord");
            List<MetalFunctionTableEntry> missAfter = GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_MissRecords");
            List<MetalFunctionTableEntry> hitAfter = GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_HitRecords");
            List<MetalFunctionTableEntry> callableAfter = GetPrivateField<List<MetalFunctionTableEntry>>(table, "m_CallableRecords");

            AssertEntriesEqual(rayGenBefore, rayGenAfter);
            AssertEntriesEqual(missBefore[0], missAfter[0]);
            Assert.Equal(1, missAfter[1].GroupIndex);
            Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, missAfter[1].LocalData);
            AssertEntriesEqual(hitBefore[0], hitAfter[0]);
            AssertEntriesEqual(callableBefore[0], callableAfter[0]);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing private field '{fieldName}'.");
            return (T)field.GetValue(instance)!;
        }

        private static List<MetalFunctionTableEntry> CloneEntries(List<MetalFunctionTableEntry> source)
        {
            return source.Select(CloneEntry).ToList();
        }

        private static MetalFunctionTableEntry CloneEntry(MetalFunctionTableEntry entry)
        {
            byte[] localDataCopy = entry.LocalData.Length > 0 ? (byte[])entry.LocalData.Clone() : Array.Empty<byte>();
            return new MetalFunctionTableEntry(entry.GroupIndex, localDataCopy);
        }

        private static void AssertEntriesEqual(MetalFunctionTableEntry expected, MetalFunctionTableEntry actual)
        {
            Assert.Equal(expected.GroupIndex, actual.GroupIndex);
            Assert.Equal(expected.LocalData, actual.LocalData);
        }
    }
}
