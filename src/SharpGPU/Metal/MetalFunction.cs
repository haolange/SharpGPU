using System;
using SharpMetal.Metal;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SharpGPU
{
    internal static class MetalFunctionPayloadDecoder
    {
        [DllImport("/usr/lib/system/libdispatch.dylib")]
        private static extern IntPtr dispatch_data_create(IntPtr buffer, UIntPtr size, IntPtr queue, IntPtr destructor);

        [DllImport("/usr/lib/system/libdispatch.dylib")]
        private static extern void dispatch_release(IntPtr obj);

        internal static MTLLibrary CreateLibraryFromBinary(MTLDevice device, IntPtr byteCode, uint byteSize)
        {
            if (byteCode == IntPtr.Zero || byteSize == 0)
            {
                throw new InvalidOperationException("Metal library binary payload is empty.");
            }

            IntPtr dispatchData = dispatch_data_create(byteCode, new UIntPtr(byteSize), IntPtr.Zero, IntPtr.Zero);
            if (dispatchData == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create dispatch_data_t for Metal library binary payload.");
            }

            try
            {
                NSError error = default;
                MTLLibrary library = device.NewLibrary(dispatchData, ref error);
                if (library.NativePtr == IntPtr.Zero)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to create MTLLibrary from binary payload: {errorText}");
                }

                return library;
            }
            finally
            {
                dispatch_release(dispatchData);
            }
        }

        internal static string DecodeSource(in RHIFunctionDescriptor descriptor)
        {
            if (descriptor.ByteCode == IntPtr.Zero || descriptor.ByteSize == 0)
            {
                return string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MslSource || descriptor.PayloadKind == ERHIShaderPayloadKind.Pending)
            {
                return Marshal.PtrToStringUTF8(descriptor.ByteCode, (int)descriptor.ByteSize) ?? string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MetalLibrary)
            {
                return string.Empty;
            }

            throw new NotSupportedException($"Unsupported shader payload for Metal function: {descriptor.PayloadKind}");
        }

        internal static string DecodeSource(in RHIFunctionLibraryDescriptor descriptor)
        {
            if (descriptor.ByteCode == IntPtr.Zero || descriptor.ByteSize == 0)
            {
                return string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MslSource || descriptor.PayloadKind == ERHIShaderPayloadKind.Pending)
            {
                return Marshal.PtrToStringUTF8(descriptor.ByteCode, (int)descriptor.ByteSize) ?? string.Empty;
            }

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MetalLibrary)
            {
                return string.Empty;
            }

            throw new NotSupportedException($"Unsupported shader payload for Metal function library: {descriptor.PayloadKind}");
        }
    }

    internal static class MetalRayFunctionTableValidator
    {
        internal static void ValidateRecord(
            in string sectionName,
            in int recordIndex,
            in int groupIndex,
            in int sectionGroupCount,
            in int localDataLength,
            in uint localDataStrideInBytes)
        {
            if (groupIndex < 0 || groupIndex >= sectionGroupCount)
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex), $"{sectionName} record[{recordIndex}] group index {groupIndex} is out of range [0, {sectionGroupCount}).");
            }

            if ((uint)localDataLength > localDataStrideInBytes)
            {
                throw new InvalidOperationException($"{sectionName} record[{recordIndex}] local data {localDataLength} bytes exceeds stride {localDataStrideInBytes} bytes.");
            }
        }

        internal static int ResolveVisibleTableIndex(in ERHIRayShaderTableSection section, in int sectionIndex, in int missCount)
        {
            return section switch
            {
                ERHIRayShaderTableSection.Miss => sectionIndex,
                ERHIRayShaderTableSection.Callable => missCount + sectionIndex,
                _ => throw new ArgumentOutOfRangeException(nameof(section), "Visible function table only supports Miss/Callable sections."),
            };
        }

        internal static int ResolveVisibleTableCount(in int missCount, in int callableCount)
        {
            return missCount + callableCount;
        }

        internal static void ValidateHitGroupExports(IReadOnlyList<string> exports, Func<string, bool> pipelineContainsHitGroup)
        {
            if (exports == null)
            {
                throw new ArgumentNullException(nameof(exports));
            }

            if (pipelineContainsHitGroup == null)
            {
                throw new ArgumentNullException(nameof(pipelineContainsHitGroup));
            }

            HashSet<string> uniqueExports = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < exports.Count; ++i)
            {
                string exportName = exports[i];
                if (string.IsNullOrWhiteSpace(exportName))
                {
                    throw new InvalidOperationException($"Hit-group program[{i}] has an empty export name.");
                }

                if (!uniqueExports.Add(exportName))
                {
                    throw new InvalidOperationException($"Duplicate hit-group export '{exportName}' in function table.");
                }

                if (!pipelineContainsHitGroup(exportName))
                {
                    throw new InvalidOperationException($"Hit-group '{exportName}' is not declared by the ray-tracing pipeline hit groups.");
                }
            }
        }

        internal static void ValidateMissExports(IReadOnlyList<string> exports, Func<string, bool> pipelineContainsMissEntry)
        {
            if (exports == null)
            {
                throw new ArgumentNullException(nameof(exports));
            }

            if (pipelineContainsMissEntry == null)
            {
                throw new ArgumentNullException(nameof(pipelineContainsMissEntry));
            }

            for (int i = 0; i < exports.Count; ++i)
            {
                string exportName = exports[i];
                if (string.IsNullOrWhiteSpace(exportName))
                {
                    throw new InvalidOperationException($"Miss program[{i}] has an empty export name.");
                }

                if (!pipelineContainsMissEntry(exportName))
                {
                    throw new InvalidOperationException($"Miss export '{exportName}' is not declared by the ray-tracing pipeline miss entries.");
                }
            }
        }
    }

    internal sealed class MetalFunction : RHIFunction
    {
        public MTLLibrary NativeLibrary
        {
            get
            {
                ThrowIfSourceUnavailable();
                return m_NativeLibrary;
            }
        }
        public MTLFunction NativeFunction
        {
            get
            {
                ThrowIfSourceUnavailable();
                return m_NativeFunction;
            }
        }

        private MTLLibrary m_NativeLibrary;
        private MTLFunction m_NativeFunction;
        private readonly bool m_OwnsLibrary;

        public MetalFunction(MetalDevice device, in RHIFunctionDescriptor descriptor)
        {
            BindDirectBytecodeSource(descriptor);

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MetalLibrary)
            {
                m_NativeLibrary = MetalFunctionPayloadDecoder.CreateLibraryFromBinary(device.NativeDevice, descriptor.ByteCode, descriptor.ByteSize);
            }
            else
            {
                string source = MetalFunctionPayloadDecoder.DecodeSource(descriptor);
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("Metal function payload is empty.");
                }

                NSString sourceString = new NSString(source);
                MTLCompileOptions options = MTLCompileOptions.New();
                options.FastMathEnabled = true;

                NSError error = default;
                m_NativeLibrary = device.NativeDevice.NewLibrary(sourceString, options, ref error);
                if (m_NativeLibrary.NativePtr == IntPtr.Zero)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to compile MSL library: {errorText}");
                }
            }

            m_OwnsLibrary = true;
            m_NativeFunction = CreateStageFunction(m_NativeLibrary, descriptor.EntryName, descriptor.Type);
        }

        internal MetalFunction(MetalFunctionLibrary library, in RHIFunctionViewDescriptor view)
        {
            library.ThrowIfViewSourceUnavailable();
            BindLibraryViewSource(library, view, library.Descriptor.PayloadKind, library.ContentDigest);
            m_NativeLibrary = library.NativeLibrary;
            m_OwnsLibrary = false;
            m_NativeFunction = CreateStageFunction(m_NativeLibrary, view.EntryName, view.Type);
        }

        private static MTLFunction CreateStageFunction(
            MTLLibrary library,
            string entryName,
            ERHIFunctionType type)
        {
            NSString name = new NSString(entryName);
            MTLFunction function = library.NewFunction(name);
            if (function.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load function entry '{entryName}'.");
            }

            if (!MatchesStage(function.FunctionType, type))
            {
                ObjectiveCRuntime.Release(function);
                throw new InvalidOperationException(
                    $"Metal function '{entryName}' type {function.FunctionType} does not match stage {type}.");
            }

            return function;
        }

        private static bool MatchesStage(MTLFunctionType metalType, ERHIFunctionType type)
        {
            return type switch
            {
                ERHIFunctionType.Vertex => metalType == MTLFunctionType.Vertex,
                ERHIFunctionType.Fragment => metalType == MTLFunctionType.Fragment,
                ERHIFunctionType.Compute => metalType == MTLFunctionType.Kernel,
                ERHIFunctionType.Task => metalType == MTLFunctionType.Object,
                ERHIFunctionType.Mesh => metalType == MTLFunctionType.Mesh,
                ERHIFunctionType.RayTracing =>
                    metalType is MTLFunctionType.Visible or MTLFunctionType.Intersection or MTLFunctionType.Kernel,
                _ => false,
            };
        }

        protected override void Release()
        {
            if (m_NativeFunction.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeFunction);
                m_NativeFunction = default;
            }

            if (m_OwnsLibrary && m_NativeLibrary.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeLibrary);
            }

            m_NativeLibrary = default;
        }
    }

    internal sealed class MetalFunctionLibrary : RHIFunctionLibrary
    {
        internal MTLLibrary NativeLibrary => m_NativeLibrary;

        private readonly MetalDevice m_Device;
        private MTLLibrary m_NativeLibrary;

        public MetalFunctionLibrary(MetalDevice device, in RHIFunctionLibraryDescriptor descriptor)
        {
            m_Device = device ?? throw new ArgumentNullException(nameof(device));
            if (!device.Capabilities.FunctionLibrary.SupportsPayloadKind(descriptor.PayloadKind))
            {
                throw new NotSupportedException(
                    $"Metal function libraries require MetalLibrary or MslSource, received {descriptor.PayloadKind}.");
            }

            BindLibraryPayload(descriptor);

            if (descriptor.PayloadKind == ERHIShaderPayloadKind.MetalLibrary)
            {
                m_NativeLibrary = MetalFunctionPayloadDecoder.CreateLibraryFromBinary(device.NativeDevice, descriptor.ByteCode, descriptor.ByteSize);
            }
            else
            {
                string source = MetalFunctionPayloadDecoder.DecodeSource(descriptor);
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("Metal function library payload is empty.");
                }

                NSString sourceString = new NSString(source);
                MTLCompileOptions options = MTLCompileOptions.New();
                options.FastMathEnabled = true;

                NSError error = default;
                m_NativeLibrary = device.NativeDevice.NewLibrary(sourceString, options, ref error);
                if (m_NativeLibrary.NativePtr == IntPtr.Zero)
                {
                    string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                    throw new InvalidOperationException($"Failed to compile MSL function library: {errorText}");
                }
            }
        }

        internal void ThrowIfViewSourceUnavailable()
        {
            ThrowIfDisposed();
        }

        public override RHIFunction CreateFunction(in RHIFunctionViewDescriptor descriptor)
        {
            ThrowIfDisposed();
            m_Device.Capabilities.FunctionLibrary.NativeLibrary.Require(
                "FunctionLibrary.NativeLibrary");
            ERHIFunctionLibraryReusablePipelineClass pipelineClass =
                m_Device.Capabilities.FunctionLibrary.ClassifyFunctionType(descriptor.Type);
            m_Device.Capabilities.FunctionLibrary.RequireReusableClass(
                pipelineClass,
                $"Metal function-library views for {pipelineClass}");
            return new MetalFunction(this, descriptor);
        }

        protected override void Release()
        {
            if (m_NativeLibrary.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeLibrary);
                m_NativeLibrary = default;
            }
        }
    }

    internal struct MetalFunctionTableEntry
    {
        internal int GroupIndex;
        internal byte[] LocalData;

        internal MetalFunctionTableEntry(in int groupIndex, in byte[] localData)
        {
            GroupIndex = groupIndex;
            LocalData = localData;
        }
    }

    internal sealed class MetalFunctionTable : RHIFunctionTable
    {
        internal MTLIntersectionFunctionTable IntersectionFunctionTable => m_IntersectionFunctionTable;
        internal MTLVisibleFunctionTable VisibleFunctionTable => m_VisibleFunctionTable;
        internal bool IsGenerated => m_IsGenerated;
        internal MetalDevice Device { get; }
        internal MetalRaytracingPipeline? GeneratedPipeline => m_GeneratedPipeline;

        private MetalRaytracingPipeline m_GeneratedPipeline;
        private MetalFunctionTableEntry m_RayGenerationRecord;
        private bool m_HasRayGenerationRecord;
        private readonly List<MetalFunctionTableEntry> m_MissRecords;
        private readonly List<MetalFunctionTableEntry> m_HitRecords;
        private readonly List<MetalFunctionTableEntry> m_CallableRecords;
        private MTLIntersectionFunctionTable m_IntersectionFunctionTable;
        private MTLVisibleFunctionTable m_VisibleFunctionTable;
        private bool m_IsGenerated;
        private uint m_LocalDataStrideInBytes;

        public MetalFunctionTable(MetalDevice device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            m_GeneratedPipeline = null;
            m_RayGenerationRecord = default;
            m_HasRayGenerationRecord = false;
            m_MissRecords = new List<MetalFunctionTableEntry>(4);
            m_HitRecords = new List<MetalFunctionTableEntry>(8);
            m_CallableRecords = new List<MetalFunctionTableEntry>(2);
            m_IntersectionFunctionTable = default;
            m_VisibleFunctionTable = default;
            m_IsGenerated = false;
            m_LocalDataStrideInBytes = 0;
        }

        public override void SetRayGenerationRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            m_RayGenerationRecord = CreateEntry(record);
            m_HasRayGenerationRecord = true;
        }

        public override int AddMissRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            m_MissRecords.Add(CreateEntry(record));
            return m_MissRecords.Count - 1;
        }

        public override int AddHitGroupRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            m_HitRecords.Add(CreateEntry(record));
            return m_HitRecords.Count - 1;
        }

        public override int AddCallableRecord(in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            m_CallableRecords.Add(CreateEntry(record));
            return m_CallableRecords.Count - 1;
        }

        public override void SetMissRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_MissRecords.Count, nameof(index));
            m_MissRecords[index] = CreateEntry(record);
        }

        public override void SetHitGroupRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_HitRecords.Count, nameof(index));
            m_HitRecords[index] = CreateEntry(record);
        }

        public override void SetCallableRecord(in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            ValidateRecordIndex(index, m_CallableRecords.Count, nameof(index));
            m_CallableRecords[index] = CreateEntry(record);
        }

        public override void ClearMissRecords()
        {
            ThrowIfDisposed();
            m_MissRecords.Clear();
            m_IsGenerated = false;
        }

        public override void ClearHitGroupRecords()
        {
            ThrowIfDisposed();
            m_HitRecords.Clear();
            m_IsGenerated = false;
        }

        public override void ClearCallableRecords()
        {
            ThrowIfDisposed();
            m_CallableRecords.Clear();
            m_IsGenerated = false;
        }

        public override void UpdateRecord(in ERHIRayShaderTableSection section, in int index, in RHIRayRecordDescriptor record)
        {
            ThrowIfDisposed();
            switch (section)
            {
                case ERHIRayShaderTableSection.RayGeneration:
                    if (index != 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index), "RayGeneration section only supports index 0.");
                    }
                    SetRayGenerationRecord(record);
                    break;
                case ERHIRayShaderTableSection.Miss:
                    SetMissRecord(index, record);
                    break;
                case ERHIRayShaderTableSection.Hit:
                    SetHitGroupRecord(index, record);
                    break;
                case ERHIRayShaderTableSection.Callable:
                    SetCallableRecord(index, record);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(section));
            }

            if (!m_IsGenerated || m_GeneratedPipeline == null)
            {
                return;
            }

            // Metal tables can update entries in place.
            switch (section)
            {
                case ERHIRayShaderTableSection.Miss:
                    ApplyVisibleRecord(section, index, m_MissRecords[index], m_GeneratedPipeline);
                    break;
                case ERHIRayShaderTableSection.Callable:
                    ApplyVisibleRecord(section, index, m_CallableRecords[index], m_GeneratedPipeline);
                    break;
                case ERHIRayShaderTableSection.Hit:
                    ApplyHitRecord(index, m_HitRecords[index], m_GeneratedPipeline);
                    break;
            }
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            ThrowIfDisposed();
            MetalRaytracingPipeline metalPipeline = pipeline as MetalRaytracingPipeline
                ?? throw new ArgumentException("Function table requires a Metal ray tracing pipeline.", nameof(pipeline));

            if (!m_HasRayGenerationRecord)
            {
                throw new InvalidOperationException("Ray generation record is not set.");
            }

            m_LocalDataStrideInBytes = metalPipeline.Descriptor.LocalDataStrideInBytes;
            ValidateAllRecords(metalPipeline);
            ReleaseGeneratedTables();

            if (m_HitRecords.Count > 0)
            {
                MTLIntersectionFunctionTableDescriptor intersectionDescriptor = MTLIntersectionFunctionTableDescriptor.New();
                intersectionDescriptor.FunctionCount = (ulong)m_HitRecords.Count;
                m_IntersectionFunctionTable = metalPipeline.NativePipelineState.NewIntersectionFunctionTable(intersectionDescriptor);
                if (m_IntersectionFunctionTable.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal intersection function table.");
                }

                for (int i = 0; i < m_HitRecords.Count; ++i)
                {
                    ApplyHitRecord(i, m_HitRecords[i], metalPipeline);
                }
            }

            int visibleCount = MetalRayFunctionTableValidator.ResolveVisibleTableCount(m_MissRecords.Count, m_CallableRecords.Count);
            if (visibleCount > 0)
            {
                MTLVisibleFunctionTableDescriptor visibleDescriptor = MTLVisibleFunctionTableDescriptor.New();
                visibleDescriptor.FunctionCount = (ulong)visibleCount;
                m_VisibleFunctionTable = metalPipeline.NativePipelineState.NewVisibleFunctionTable(visibleDescriptor);
                if (m_VisibleFunctionTable.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal visible function table.");
                }

                for (int i = 0; i < m_MissRecords.Count; ++i)
                {
                    ApplyVisibleRecord(ERHIRayShaderTableSection.Miss, i, m_MissRecords[i], metalPipeline);
                }

                for (int i = 0; i < m_CallableRecords.Count; ++i)
                {
                    ApplyVisibleRecord(ERHIRayShaderTableSection.Callable, i, m_CallableRecords[i], metalPipeline);
                }
            }

            m_GeneratedPipeline = metalPipeline;
            m_IsGenerated = true;
        }

        public override void Update()
        {
            ThrowIfDisposed();
            if (m_GeneratedPipeline == null)
            {
                throw new InvalidOperationException("Function table has not been generated yet.");
            }

            Generate(m_GeneratedPipeline);
        }

        private void ValidateAllRecords(MetalRaytracingPipeline pipeline)
        {
            ValidateRecord(ERHIRayShaderTableSection.RayGeneration, 0, m_RayGenerationRecord, pipeline);
            for (int i = 0; i < m_MissRecords.Count; ++i)
            {
                ValidateRecord(ERHIRayShaderTableSection.Miss, i, m_MissRecords[i], pipeline);
            }

            for (int i = 0; i < m_HitRecords.Count; ++i)
            {
                ValidateRecord(ERHIRayShaderTableSection.Hit, i, m_HitRecords[i], pipeline);
            }

            for (int i = 0; i < m_CallableRecords.Count; ++i)
            {
                ValidateRecord(ERHIRayShaderTableSection.Callable, i, m_CallableRecords[i], pipeline);
            }
        }

        private void ValidateRecord(ERHIRayShaderTableSection section, int recordIndex, in MetalFunctionTableEntry entry, MetalRaytracingPipeline pipeline)
        {
            int groupCount = section switch
            {
                ERHIRayShaderTableSection.RayGeneration => 1,
                ERHIRayShaderTableSection.Miss => pipeline.MissGroupCount,
                ERHIRayShaderTableSection.Hit => pipeline.HitGroupCount,
                ERHIRayShaderTableSection.Callable => pipeline.CallableGroupCount,
                _ => 0,
            };

            MetalRayFunctionTableValidator.ValidateRecord(section.ToString(), recordIndex, entry.GroupIndex, groupCount, entry.LocalData.Length, m_LocalDataStrideInBytes);
        }

        private void ApplyHitRecord(in int tableIndex, in MetalFunctionTableEntry record, MetalRaytracingPipeline pipeline)
        {
            if (m_IntersectionFunctionTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            RHIRayHitGroupDescriptor hitGroup = pipeline.GetHitGroupDescriptor(record.GroupIndex);
            switch (hitGroup.Type)
            {
                case ERHIHitGroupType.Triangles:
                    m_IntersectionFunctionTable.SetOpaqueTriangleIntersectionFunction(MTLIntersectionFunctionSignature.Instancing | MTLIntersectionFunctionSignature.TriangleData, (ulong)tableIndex);
                    break;
                case ERHIHitGroupType.Curve:
                    m_IntersectionFunctionTable.SetOpaqueCurveIntersectionFunction(MTLIntersectionFunctionSignature.Instancing | MTLIntersectionFunctionSignature.CurveData, (ulong)tableIndex);
                    break;
                case ERHIHitGroupType.Procedural:
                {
                    string? intersectionName = hitGroup.Intersect?.EntryName;
                    if (string.IsNullOrWhiteSpace(intersectionName))
                    {
                        throw new InvalidOperationException($"Hit group '{hitGroup.Name}' is procedural but has no intersection function.");
                    }

                    MTLFunction function = pipeline.ResolveIntersectionFunction(intersectionName);
                    MTLFunctionHandle handle = pipeline.NativePipelineState.FunctionHandle(function);
                    if (handle.NativePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to create function handle for procedural intersection '{intersectionName}'.");
                    }

                    m_IntersectionFunctionTable.SetFunction(handle, (ulong)tableIndex);
                    break;
                }
                default:
                    throw new NotSupportedException($"Unsupported hit group type '{hitGroup.Type}'.");
            }
        }

        private void ApplyVisibleRecord(in ERHIRayShaderTableSection section, in int sectionIndex, in MetalFunctionTableEntry record, MetalRaytracingPipeline pipeline)
        {
            if (m_VisibleFunctionTable.NativePtr == IntPtr.Zero)
            {
                return;
            }

            string entryName = section switch
            {
                ERHIRayShaderTableSection.Miss => pipeline.GetMissGroupDescriptor(record.GroupIndex).General.EntryName,
                ERHIRayShaderTableSection.Callable => pipeline.GetCallableGroupDescriptor(record.GroupIndex).General.EntryName,
                _ => throw new ArgumentOutOfRangeException(nameof(section)),
            };

            MTLFunction function = pipeline.ResolveVisibleFunction(entryName);
            MTLFunctionHandle handle = pipeline.NativePipelineState.FunctionHandle(function);
            if (handle.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create function handle for visible function '{entryName}'.");
            }

            int visibleIndex = MetalRayFunctionTableValidator.ResolveVisibleTableIndex(section, sectionIndex, m_MissRecords.Count);
            m_VisibleFunctionTable.SetFunction(handle, (ulong)visibleIndex);
        }

        private static MetalFunctionTableEntry CreateEntry(in RHIRayRecordDescriptor record)
        {
            ValidateGroupIndex(record.GroupIndex, nameof(record));
            return new MetalFunctionTableEntry(record.GroupIndex, CloneLocalData(record.LocalData));
        }

        private void ReleaseGeneratedTables()
        {
            if (m_IntersectionFunctionTable.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_IntersectionFunctionTable);
                m_IntersectionFunctionTable = default;
            }

            if (m_VisibleFunctionTable.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_VisibleFunctionTable);
                m_VisibleFunctionTable = default;
            }

            m_IsGenerated = false;
            m_GeneratedPipeline = null;
        }

        private static void ValidateGroupIndex(in int groupIndex, string parameterName)
        {
            if (groupIndex < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "GroupIndex must be non-negative.");
            }
        }

        private static void ValidateRecordIndex(in int index, in int count, string parameterName)
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static byte[] CloneLocalData(in ReadOnlyMemory<byte> localData)
        {
            if (localData.IsEmpty)
            {
                return Array.Empty<byte>();
            }

            byte[] cloned = new byte[localData.Length];
            localData.Span.CopyTo(cloned);
            return cloned;
        }

        protected override void Release()
        {
            ReleaseGeneratedTables();
            m_MissRecords.Clear();
            m_HitRecords.Clear();
            m_CallableRecords.Clear();
            m_HasRayGenerationRecord = false;
            m_LocalDataStrideInBytes = 0;
        }
    }
}
