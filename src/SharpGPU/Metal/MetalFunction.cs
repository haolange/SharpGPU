using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SharpMetal.ObjectiveCCore;

namespace Infinity.Graphics
{
    internal static class MetalFunctionPayloadDecoder
    {
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
                throw new NotSupportedException("Metal library binary payload is not wired yet. Use MslSource payload for now.");
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
                throw new NotSupportedException("Metal library binary payload is not wired yet. Use MslSource payload for now.");
            }

            throw new NotSupportedException($"Unsupported shader payload for Metal function library: {descriptor.PayloadKind}");
        }
    }

    internal sealed class MetalFunction : RHIFunction
    {
        public MTLLibrary NativeLibrary => m_NativeLibrary;
        public MTLFunction NativeFunction => m_NativeFunction;

        private MTLLibrary m_NativeLibrary;
        private MTLFunction m_NativeFunction;

        public MetalFunction(MetalDevice device, in RHIFunctionDescriptor descriptor)
        {
            m_Descriptor = descriptor;
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

            NSString entryName = new NSString(descriptor.EntryName);
            m_NativeFunction = m_NativeLibrary.NewFunction(entryName);
            if (m_NativeFunction.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load function entry '{descriptor.EntryName}'.");
            }
        }

        protected override void Release()
        {
            if (m_NativeFunction.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeFunction);
                m_NativeFunction = default;
            }

            if (m_NativeLibrary.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeLibrary);
                m_NativeLibrary = default;
            }
        }
    }

    internal sealed class MetalFunctionLibrary : RHIFunctionLibrary
    {
        internal MTLLibrary NativeLibrary => m_NativeLibrary;

        private MTLLibrary m_NativeLibrary;

        public MetalFunctionLibrary(MetalDevice device, in RHIFunctionLibraryDescriptor descriptor)
        {
            m_Descriptor = descriptor;
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
        internal string ExportName;
        internal RHIResourceTable[]? ResourceTables;

        internal MetalFunctionTableEntry(string exportName, RHIResourceTable[]? resourceTables)
        {
            ExportName = exportName;
            ResourceTables = resourceTables;
        }
    }

    internal sealed class MetalFunctionTable : RHIFunctionTable
    {
        internal MTLIntersectionFunctionTable IntersectionFunctionTable => m_IntersectionFunctionTable;
        internal MTLVisibleFunctionTable VisibleFunctionTable => m_VisibleFunctionTable;
        internal string RayGenerationExportName => m_RayGenerationExportName;
        internal bool IsGenerated => m_IsGenerated;

        private string m_RayGenerationExportName;
        private RHIResourceTable[]? m_RayGenerationResourceTables;
        private readonly List<MetalFunctionTableEntry> m_MissPrograms;
        private readonly List<MetalFunctionTableEntry> m_HitGroupPrograms;
        private MTLIntersectionFunctionTable m_IntersectionFunctionTable;
        private MTLVisibleFunctionTable m_VisibleFunctionTable;
        private bool m_IsGenerated;

        public MetalFunctionTable()
        {
            m_RayGenerationExportName = string.Empty;
            m_MissPrograms = new List<MetalFunctionTableEntry>(4);
            m_HitGroupPrograms = new List<MetalFunctionTableEntry>(8);
            m_IntersectionFunctionTable = default;
            m_VisibleFunctionTable = default;
            m_IsGenerated = false;
        }

        public override void SetRayGenerationProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            if (string.IsNullOrWhiteSpace(exportName))
            {
                throw new ArgumentException("Ray generation export name is empty.", nameof(exportName));
            }

            m_RayGenerationExportName = exportName;
            m_RayGenerationResourceTables = resourceTables;
            m_IsGenerated = false;
        }

        public override int AddMissProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            if (string.IsNullOrWhiteSpace(exportName))
            {
                throw new ArgumentException("Miss export name is empty.", nameof(exportName));
            }

            m_MissPrograms.Add(new MetalFunctionTableEntry(exportName, resourceTables));
            m_IsGenerated = false;
            return m_MissPrograms.Count - 1;
        }

        public override int AddHitGroupProgram(string exportName, RHIResourceTable[]? resourceTables = null)
        {
            if (string.IsNullOrWhiteSpace(exportName))
            {
                throw new ArgumentException("Hit-group export name is empty.", nameof(exportName));
            }

            m_HitGroupPrograms.Add(new MetalFunctionTableEntry(exportName, resourceTables));
            m_IsGenerated = false;
            return m_HitGroupPrograms.Count - 1;
        }

        public override void SetMissProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            if ((uint)index >= (uint)m_MissPrograms.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            m_MissPrograms[index] = new MetalFunctionTableEntry(exportName, resourceTables);
            m_IsGenerated = false;
        }

        public override void SetHitGroupProgram(in int index, string exportName, RHIResourceTable[]? resourceTables = null)
        {
            if ((uint)index >= (uint)m_HitGroupPrograms.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            m_HitGroupPrograms[index] = new MetalFunctionTableEntry(exportName, resourceTables);
            m_IsGenerated = false;
        }

        public override void ClearMissPrograms()
        {
            m_MissPrograms.Clear();
            m_IsGenerated = false;
        }

        public override void ClearHitGroupPrograms()
        {
            m_HitGroupPrograms.Clear();
            m_IsGenerated = false;
        }

        public override void Generate(RHIRaytracingPipeline pipeline)
        {
            MetalRaytracingPipeline metalPipeline = pipeline as MetalRaytracingPipeline ?? throw new ArgumentException("Function table requires a Metal ray tracing pipeline.", nameof(pipeline));
            if (string.IsNullOrWhiteSpace(m_RayGenerationExportName))
            {
                throw new InvalidOperationException("Ray generation program is not set.");
            }

            if (!string.Equals(m_RayGenerationExportName, metalPipeline.RayGenerationEntryName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Ray generation export '{m_RayGenerationExportName}' does not match pipeline entry '{metalPipeline.RayGenerationEntryName}'.");
            }

            if (m_HitGroupPrograms.Count > metalPipeline.HitGroupCount)
            {
                throw new InvalidOperationException($"Function table hit-group count ({m_HitGroupPrograms.Count}) exceeds pipeline hit-group count ({metalPipeline.HitGroupCount}).");
            }

            if (m_MissPrograms.Count > metalPipeline.MissGroupCount)
            {
                throw new InvalidOperationException($"Function table miss count ({m_MissPrograms.Count}) exceeds pipeline miss-group count ({metalPipeline.MissGroupCount}).");
            }

            ReleaseGeneratedTables();

            if (m_HitGroupPrograms.Count > 0)
            {
                MTLIntersectionFunctionTableDescriptor descriptor = MTLIntersectionFunctionTableDescriptor.New();
                descriptor.FunctionCount = (ulong)m_HitGroupPrograms.Count;
                m_IntersectionFunctionTable = metalPipeline.NativePipelineState.NewIntersectionFunctionTable(descriptor);
                if (m_IntersectionFunctionTable.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal intersection function table.");
                }

                for (int i = 0; i < m_HitGroupPrograms.Count; ++i)
                {
                    RHIRayHitGroupDescriptor hitGroup = metalPipeline.GetHitGroupDescriptor(i);
                    switch (hitGroup.Type)
                    {
                        case ERHIHitGroupType.Triangles:
                            m_IntersectionFunctionTable.SetOpaqueTriangleIntersectionFunction(MTLIntersectionFunctionSignature.Instancing | MTLIntersectionFunctionSignature.TriangleData, (ulong)i);
                            break;
                        case ERHIHitGroupType.Curve:
                            m_IntersectionFunctionTable.SetOpaqueCurveIntersectionFunction(MTLIntersectionFunctionSignature.Instancing | MTLIntersectionFunctionSignature.CurveData, (ulong)i);
                            break;
                        case ERHIHitGroupType.Procedural:
                        {
                            string? intersectionName = hitGroup.Intersect?.EntryName;
                            if (string.IsNullOrWhiteSpace(intersectionName))
                            {
                                throw new InvalidOperationException($"Hit group[{i}] is procedural but has no intersection function.");
                            }

                            MTLFunction function = metalPipeline.ResolveIntersectionFunction(intersectionName);
                            MTLFunctionHandle handle = metalPipeline.NativePipelineState.FunctionHandle(function);
                            if (handle.NativePtr == IntPtr.Zero)
                            {
                                throw new InvalidOperationException($"Failed to create function handle for procedural intersection '{intersectionName}'.");
                            }

                            m_IntersectionFunctionTable.SetFunction(handle, (ulong)i);
                            ObjectiveCRuntime.Release(handle.NativePtr);
                            break;
                        }
                        default:
                            throw new NotSupportedException($"Unsupported hit group type '{hitGroup.Type}'.");
                    }
                }
            }

            if (m_MissPrograms.Count > 0)
            {
                MTLVisibleFunctionTableDescriptor descriptor = MTLVisibleFunctionTableDescriptor.New();
                descriptor.FunctionCount = (ulong)m_MissPrograms.Count;
                m_VisibleFunctionTable = metalPipeline.NativePipelineState.NewVisibleFunctionTable(descriptor);
                if (m_VisibleFunctionTable.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create Metal visible function table.");
                }

                for (int i = 0; i < m_MissPrograms.Count; ++i)
                {
                    MTLFunction function = metalPipeline.ResolveVisibleFunction(m_MissPrograms[i].ExportName);
                    MTLFunctionHandle handle = metalPipeline.NativePipelineState.FunctionHandle(function);
                    if (handle.NativePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException($"Failed to create function handle for visible miss function '{m_MissPrograms[i].ExportName}'.");
                    }

                    m_VisibleFunctionTable.SetFunction(handle, (ulong)i);
                    ObjectiveCRuntime.Release(handle.NativePtr);
                }
            }

            m_IsGenerated = true;
        }

        public override void Update(RHIRaytracingPipeline pipeline)
        {
            Generate(pipeline);
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
        }

        protected override void Release()
        {
            ReleaseGeneratedTables();
            m_MissPrograms.Clear();
            m_HitGroupPrograms.Clear();
            m_RayGenerationExportName = string.Empty;
            m_RayGenerationResourceTables = null;
        }
    }
}
