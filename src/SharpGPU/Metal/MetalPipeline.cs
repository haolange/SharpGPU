using System;
using SharpMetal.Metal;
using SharpGPU.Mathematics;
using SharpMetal.Foundation;
using SharpMetal.ObjectiveCCore;
using System.Collections.Generic;

namespace Infinity.Graphics
{
    internal sealed class MetalPipelineLayout : RHIPipelineLayout
    {
        public RHIPipelineLayoutDescriptor Descriptor => m_Descriptor;
        internal int ArgumentTableLayoutCount => m_Descriptor.ArgumentTableLayouts?.Length ?? 0;

        private readonly RHIPipelineLayoutDescriptor m_Descriptor;

        public MetalPipelineLayout(in RHIPipelineLayoutDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
        }
    }

    internal sealed class MetalComputePipeline : RHIComputePipeline
    {
        public MTLComputePipelineState NativePipelineState => m_NativePipelineState;
        public SharpGPU.Mathematics.uint3 ThreadgroupSize => m_Descriptor.ThreadSize;

        private MTLComputePipelineState m_NativePipelineState;

        public MetalComputePipeline(MetalDevice device, in RHIComputePipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            MetalFunction computeFunction = (MetalFunction)descriptor.ComputeFunction;
            NSError error = default;
            m_NativePipelineState = device.NativeDevice.NewComputePipelineState(computeFunction.NativeFunction, ref error);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTLComputePipelineState: {errorText}");
            }
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }
        }
    }

    internal sealed class MetalRaytracingPipeline : RHIRaytracingPipeline
    {
        internal MTLComputePipelineState NativePipelineState => m_NativePipelineState;
        internal uint3 ThreadgroupSize => m_ThreadgroupSize;
        internal string RayGenerationEntryName => m_RayGenerationEntryName;
        internal int HitGroupCount => m_HitGroups.Length;
        internal int MissGroupCount => m_MissGroups.Length;
        internal int CallableGroupCount => m_CallableGroups.Length;

        private readonly MetalFunctionLibrary m_FunctionLibrary;
        private readonly uint3 m_ThreadgroupSize;
        private readonly string m_RayGenerationEntryName;
        private readonly RHIRayHitGroupDescriptor[] m_HitGroups;
        private readonly RHIRayGeneralGroupDescriptor[] m_MissGroups;
        private readonly RHIRayGeneralGroupDescriptor[] m_CallableGroups;
        private readonly Dictionary<string, RHIRayHitGroupDescriptor> m_HitGroupsByName;
        private readonly HashSet<string> m_MissEntryNames;
        private readonly HashSet<string> m_CallableEntryNames;
        private readonly Dictionary<string, MTLFunction> m_VisibleFunctionCache;
        private readonly Dictionary<string, MTLFunction> m_IntersectionFunctionCache;
        private readonly MTLFunction m_RayGenerationFunction;
        private MTLComputePipelineState m_NativePipelineState;

        public MetalRaytracingPipeline(MetalDevice device, in RHIRaytracingPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            m_FunctionLibrary = descriptor.FunctionLibrary as MetalFunctionLibrary ?? throw new InvalidOperationException("Metal ray tracing pipeline requires a Metal function library.");
            m_RayGenerationEntryName = descriptor.RayGeneration.General.EntryName;
            if (string.IsNullOrWhiteSpace(m_RayGenerationEntryName))
            {
                throw new InvalidOperationException("Ray generation entry name is empty.");
            }

            m_ThreadgroupSize = new uint3(Math.Max(1u, descriptor.ThreadSize.x), Math.Max(1u, descriptor.ThreadSize.y), Math.Max(1u, descriptor.ThreadSize.z));
            m_HitGroups = descriptor.RayHitGroups.Span.ToArray();
            m_MissGroups = descriptor.RayMissGroups.Span.ToArray();
            m_CallableGroups = descriptor.RayCallableGroups.Span.ToArray();
            m_HitGroupsByName = new Dictionary<string, RHIRayHitGroupDescriptor>(m_HitGroups.Length, StringComparer.Ordinal);
            m_MissEntryNames = new HashSet<string>(StringComparer.Ordinal);
            m_CallableEntryNames = new HashSet<string>(StringComparer.Ordinal);
            m_VisibleFunctionCache = new Dictionary<string, MTLFunction>(StringComparer.Ordinal);
            m_IntersectionFunctionCache = new Dictionary<string, MTLFunction>(StringComparer.Ordinal);

            m_RayGenerationFunction = ResolveKernelFunction(m_RayGenerationEntryName);

            for (int i = 0; i < m_HitGroups.Length; ++i)
            {
                RHIRayHitGroupDescriptor hitGroup = m_HitGroups[i];
                if (string.IsNullOrWhiteSpace(hitGroup.Name))
                {
                    throw new InvalidOperationException($"Ray hit-group[{i}] has an empty name.");
                }

                if (!m_HitGroupsByName.TryAdd(hitGroup.Name, hitGroup))
                {
                    throw new InvalidOperationException($"Duplicate ray hit-group name '{hitGroup.Name}' in pipeline descriptor.");
                }
            }

            for (int i = 0; i < m_MissGroups.Length; ++i)
            {
                string entryName = m_MissGroups[i].General.EntryName;
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException($"Ray miss-group[{i}] has an empty function entry.");
                }

                m_MissEntryNames.Add(entryName);
            }

            for (int i = 0; i < m_CallableGroups.Length; ++i)
            {
                string entryName = m_CallableGroups[i].General.EntryName;
                if (string.IsNullOrWhiteSpace(entryName))
                {
                    throw new InvalidOperationException($"Ray callable-group[{i}] has an empty function entry.");
                }

                m_CallableEntryNames.Add(entryName);
            }

            List<IntPtr> linkedFunctionPointers = new List<IntPtr>(Math.Max(1, m_MissGroups.Length + m_CallableGroups.Length + m_HitGroups.Length));
            for (int i = 0; i < m_MissGroups.Length; ++i)
            {
                MTLFunction missFunction = ResolveVisibleFunction(m_MissGroups[i].General.EntryName);
                linkedFunctionPointers.Add(missFunction.NativePtr);
            }

            for (int i = 0; i < m_CallableGroups.Length; ++i)
            {
                MTLFunction callableFunction = ResolveVisibleFunction(m_CallableGroups[i].General.EntryName);
                linkedFunctionPointers.Add(callableFunction.NativePtr);
            }

            for (int i = 0; i < m_HitGroups.Length; ++i)
            {
                RHIRayHitGroupDescriptor hitGroup = m_HitGroups[i];
                if (hitGroup.Type == ERHIHitGroupType.Procedural)
                {
                    string? entryName = hitGroup.Intersect?.EntryName;
                    if (string.IsNullOrWhiteSpace(entryName))
                    {
                        throw new InvalidOperationException($"Hit group '{hitGroup.Name}' is procedural but has no intersection function.");
                    }

                    MTLFunction intersectionFunction = ResolveIntersectionFunction(entryName);
                    linkedFunctionPointers.Add(intersectionFunction.NativePtr);
                }
            }

            MTLComputePipelineDescriptor nativeDescriptor = MTLComputePipelineDescriptor.New();
            nativeDescriptor.ComputeFunction = m_RayGenerationFunction;
            if (linkedFunctionPointers.Count > 0)
            {
                MTLLinkedFunctions linkedFunctions = MTLLinkedFunctions.New();
                linkedFunctions.Functions = MetalArrayHelper.CreateNSArrayFromPointers(linkedFunctionPointers.ToArray());
                nativeDescriptor.LinkedFunctions = linkedFunctions;
                ObjectiveCRuntime.Release(linkedFunctions.NativePtr);
            }

            NSError error = default;
            m_NativePipelineState = device.NativeDevice.NewComputePipelineState(nativeDescriptor, MTLPipelineOption.None, IntPtr.Zero, ref error);
            ObjectiveCRuntime.Release(nativeDescriptor.NativePtr);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create Metal ray tracing compute pipeline state: {errorText}");
            }
        }

        internal RHIRayHitGroupDescriptor GetHitGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_HitGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_HitGroups[index];
        }

        internal RHIRayHitGroupDescriptor GetHitGroupDescriptor(string hitGroupName)
        {
            if (string.IsNullOrWhiteSpace(hitGroupName))
            {
                throw new ArgumentException("Hit-group name is empty.", nameof(hitGroupName));
            }

            if (!m_HitGroupsByName.TryGetValue(hitGroupName, out RHIRayHitGroupDescriptor hitGroup))
            {
                throw new InvalidOperationException($"Hit-group '{hitGroupName}' does not exist in the ray tracing pipeline.");
            }

            return hitGroup;
        }

        internal bool ContainsHitGroup(string hitGroupName)
        {
            if (string.IsNullOrWhiteSpace(hitGroupName))
            {
                return false;
            }

            return m_HitGroupsByName.ContainsKey(hitGroupName);
        }

        internal bool ContainsMissEntry(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return false;
            }

            return m_MissEntryNames.Contains(entryName);
        }

        internal bool ContainsCallableEntry(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                return false;
            }

            return m_CallableEntryNames.Contains(entryName);
        }

        internal RHIRayGeneralGroupDescriptor GetMissGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_MissGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_MissGroups[index];
        }

        internal RHIRayGeneralGroupDescriptor GetCallableGroupDescriptor(int index)
        {
            if ((uint)index >= (uint)m_CallableGroups.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return m_CallableGroups[index];
        }

        internal MTLFunction ResolveVisibleFunction(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new ArgumentException("Visible function entry name is empty.", nameof(entryName));
            }

            if (m_VisibleFunctionCache.TryGetValue(entryName, out MTLFunction cached))
            {
                return cached;
            }

            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewFunction(new NSString(entryName));
            if (function.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to resolve visible function '{entryName}' from Metal function library.");
            }

            m_VisibleFunctionCache.Add(entryName, function);
            return function;
        }

        internal MTLFunction ResolveIntersectionFunction(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new ArgumentException("Intersection function entry name is empty.", nameof(entryName));
            }

            if (m_IntersectionFunctionCache.TryGetValue(entryName, out MTLFunction cached))
            {
                return cached;
            }

            MTLIntersectionFunctionDescriptor descriptor = MTLIntersectionFunctionDescriptor.New();
            descriptor.Name = new NSString(entryName);

            NSError error = default;
            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewIntersectionFunction(descriptor, ref error);
            ObjectiveCRuntime.Release(descriptor.NativePtr);
            if (function.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to resolve intersection function '{entryName}': {errorText}");
            }

            m_IntersectionFunctionCache.Add(entryName, function);
            return function;
        }

        private MTLFunction ResolveKernelFunction(string entryName)
        {
            MTLFunction function = m_FunctionLibrary.NativeLibrary.NewFunction(new NSString(entryName));
            if (function.NativePtr == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to resolve kernel function '{entryName}' from Metal function library.");
            }

            if (function.FunctionType != MTLFunctionType.Kernel)
            {
                throw new InvalidOperationException($"Ray generation entry '{entryName}' is not a compute kernel function.");
            }

            return function;
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }

            if (m_RayGenerationFunction.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_RayGenerationFunction);
            }

            foreach (KeyValuePair<string, MTLFunction> pair in m_VisibleFunctionCache)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value);
                }
            }

            foreach (KeyValuePair<string, MTLFunction> pair in m_IntersectionFunctionCache)
            {
                if (pair.Value.NativePtr != IntPtr.Zero)
                {
                    ObjectiveCRuntime.Release(pair.Value);
                }
            }

            m_VisibleFunctionCache.Clear();
            m_IntersectionFunctionCache.Clear();
            m_HitGroupsByName.Clear();
            m_MissEntryNames.Clear();
            m_CallableEntryNames.Clear();
        }
    }

    internal sealed class MetalRasterPipeline : RHIRasterPipeline
    {
        public MTLRenderPipelineState NativePipelineState => m_NativePipelineState;
        public MTLDepthStencilState DepthStencilState => m_DepthStencilState;
        public MTLPrimitiveType PrimitiveType => m_PrimitiveType;
        public MTLCullMode CullMode => m_CullMode;
        public MTLTriangleFillMode FillMode => m_FillMode;
        public MTLWinding Winding => m_Winding;

        private MTLRenderPipelineState m_NativePipelineState;
        private MTLDepthStencilState m_DepthStencilState;
        private readonly MTLPrimitiveType m_PrimitiveType;
        private readonly MTLCullMode m_CullMode;
        private readonly MTLTriangleFillMode m_FillMode;
        private readonly MTLWinding m_Winding;

        public MetalRasterPipeline(MetalDevice device, in RHIRasterPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;

            MetalFunction vertexFunction = (MetalFunction)descriptor.PrimitiveAssembler.VertexAssembler!.Value.VertexFunction;
            MetalFunction fragmentFunction = (MetalFunction)descriptor.FragmentFunction;

            MTLRenderPipelineDescriptor nativeDescriptor = MTLRenderPipelineDescriptor.New();
            nativeDescriptor.VertexFunction = vertexFunction.NativeFunction;
            nativeDescriptor.FragmentFunction = fragmentFunction.NativeFunction;
            nativeDescriptor.SampleCount = (ulong)descriptor.SampleCount;
            nativeDescriptor.InputPrimitiveTopology = MetalUtility.ConvertToMetalPrimitiveTopologyClass(descriptor.PrimitiveAssembler.PrimitiveTopology);
            nativeDescriptor.SetAlphaToCoverageEnabled(descriptor.RenderState.BlendState.AlphaToCoverage);
            nativeDescriptor.SetRasterizationEnabled(true);

            ConfigureColorAttachments(nativeDescriptor, descriptor);
            ConfigureDepthStencilAttachment(nativeDescriptor, descriptor.DepthFormat);
            ConfigureVertexLayout(nativeDescriptor, descriptor);

            NSError error = default;
            m_NativePipelineState = device.NativeDevice.NewRenderPipelineState(nativeDescriptor, ref error);
            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"Failed to create MTLRenderPipelineState: {errorText}");
            }

            m_DepthStencilState = CreateDepthStencilState(device, descriptor.RenderState.DepthStencilState);
            m_PrimitiveType = MetalUtility.ConvertToMetalPrimitiveType(descriptor.PrimitiveAssembler.PrimitiveTopology);
            m_CullMode = MetalUtility.ConvertToMetalCullMode(descriptor.RenderState.RasterizerState.CullMode);
            m_FillMode = MetalUtility.ConvertToMetalFillMode(descriptor.RenderState.RasterizerState.FillMode);
            m_Winding = MetalUtility.ConvertToMetalWinding(descriptor.RenderState.RasterizerState.FrontCounterClockwise);
        }

        private static void ConfigureColorAttachments(MTLRenderPipelineDescriptor nativeDescriptor, in RHIRasterPipelineDescriptor descriptor)
        {
            for (int i = 0; i < descriptor.ColorFormats.Length; ++i)
            {
                MTLRenderPipelineColorAttachmentDescriptor nativeColor = nativeDescriptor.ColorAttachments[(uint)i];
                nativeColor.PixelFormat = MetalUtility.ConvertToMetalPixelFormat(descriptor.ColorFormats[i]);

                RHIBlendDescriptor blend = GetBlendDescriptor(descriptor.RenderState.BlendState, i);
                nativeColor.SetBlendingEnabled(blend.BlendEnable);
                nativeColor.SourceRGBBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.SrcBlendColor);
                nativeColor.DestinationRGBBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.DstBlendColor);
                nativeColor.RgbBlendOperation = MetalUtility.ConvertToMetalBlendOperation(blend.BlendOpColor);
                nativeColor.SourceAlphaBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.SrcBlendAlpha);
                nativeColor.DestinationAlphaBlendFactor = MetalUtility.ConvertToMetalBlendFactor(blend.DstBlendAlpha);
                nativeColor.AlphaBlendOperation = MetalUtility.ConvertToMetalBlendOperation(blend.BlendOpAlpha);
                nativeColor.WriteMask = MetalUtility.ConvertToMetalColorWriteMask(blend.ColorWriteChannel);
            }
        }

        private static RHIBlendDescriptor GetBlendDescriptor(in RHIBlendStateDescriptor blendState, in int index)
        {
            switch (index)
            {
                case 0: return blendState.BlendDescriptor0;
                case 1: return blendState.BlendDescriptor1;
                case 2: return blendState.BlendDescriptor2;
                case 3: return blendState.BlendDescriptor3;
                case 4: return blendState.BlendDescriptor4;
                case 5: return blendState.BlendDescriptor5;
                case 6: return blendState.BlendDescriptor6;
                default: return blendState.BlendDescriptor7;
            }
        }

        private static void ConfigureDepthStencilAttachment(MTLRenderPipelineDescriptor nativeDescriptor, in ERHIPixelFormat depthFormat)
        {
            if (depthFormat != ERHIPixelFormat.Unknown)
            {
                MTLPixelFormat nativeFormat = MetalUtility.ConvertToMetalPixelFormat(depthFormat);
                nativeDescriptor.DepthAttachmentPixelFormat = nativeFormat;
                if (depthFormat == ERHIPixelFormat.D24_UNorm_S8_UInt || depthFormat == ERHIPixelFormat.D32_Float_S8_UInt)
                {
                    nativeDescriptor.StencilAttachmentPixelFormat = nativeFormat;
                }
            }
        }

        private static void ConfigureVertexLayout(MTLRenderPipelineDescriptor nativeDescriptor, in RHIRasterPipelineDescriptor descriptor)
        {
            if (!descriptor.PrimitiveAssembler.VertexAssembler.HasValue)
            {
                return;
            }

            MTLVertexDescriptor nativeVertexDescriptor = MTLVertexDescriptor.New();
            Span<RHIVertexLayoutDescriptor> layouts = descriptor.PrimitiveAssembler.VertexAssembler.Value.VertexLayouts.Span;

            uint attributeIndex = 0;
            for (int layoutIndex = 0; layoutIndex < layouts.Length; ++layoutIndex)
            {
                ref RHIVertexLayoutDescriptor layout = ref layouts[layoutIndex];
                MTLVertexBufferLayoutDescriptor nativeLayout = nativeVertexDescriptor.Layouts[(uint)layoutIndex];
                nativeLayout.Stride = layout.Stride;
                nativeLayout.StepFunction = MetalUtility.ConvertToMetalVertexStepFunction(layout.StepMode);
                nativeLayout.StepRate = Math.Max(1u, layout.StepRate);

                Span<RHIVertexElementDescriptor> elements = layout.VertexElements.Span;
                for (int elementIndex = 0; elementIndex < elements.Length; ++elementIndex)
                {
                    ref RHIVertexElementDescriptor element = ref elements[elementIndex];
                    MTLVertexAttributeDescriptor nativeAttribute = nativeVertexDescriptor.Attributes[attributeIndex++];
                    nativeAttribute.Format = MetalUtility.ConvertToMetalVertexFormat(element.Format);
                    nativeAttribute.Offset = element.Offset;
                    nativeAttribute.BufferIndex = (ulong)layoutIndex;
                }
            }

            nativeDescriptor.VertexDescriptor = nativeVertexDescriptor;
        }

        private static MTLDepthStencilState CreateDepthStencilState(MetalDevice device, in RHIDepthStencilStateDescriptor descriptor)
        {
            if (!descriptor.DepthEnable && !descriptor.StencilEnable)
            {
                return default;
            }

            MTLDepthStencilDescriptor nativeDepthStencil = MTLDepthStencilDescriptor.New();
            nativeDepthStencil.DepthCompareFunction = MetalUtility.ConvertToMetalCompareFunction(descriptor.ComparisonMode);
            nativeDepthStencil.SetDepthWriteEnabled(descriptor.DepthEnable && descriptor.DepthWriteMask);

            if (descriptor.StencilEnable)
            {
                MTLStencilDescriptor frontFace = BuildStencilDescriptor(descriptor.FrontFace, descriptor.StencilReadMask, descriptor.StencilWriteMask);
                MTLStencilDescriptor backFace = BuildStencilDescriptor(descriptor.BackFace, descriptor.StencilReadMask, descriptor.StencilWriteMask);
                nativeDepthStencil.FrontFaceStencil = frontFace;
                nativeDepthStencil.BackFaceStencil = backFace;
            }

            return device.NativeDevice.NewDepthStencilState(nativeDepthStencil);
        }

        private static MTLStencilDescriptor BuildStencilDescriptor(in RHIStencilStateDescriptor descriptor, in byte readMask, in byte writeMask)
        {
            MTLStencilDescriptor nativeStencil = MTLStencilDescriptor.New();
            nativeStencil.StencilCompareFunction = MetalUtility.ConvertToMetalCompareFunction(descriptor.ComparisonMode);
            nativeStencil.StencilFailureOperation = ConvertToMetalStencilOperation(descriptor.StencilFailOp);
            nativeStencil.DepthFailureOperation = ConvertToMetalStencilOperation(descriptor.StencilDepthFailOp);
            nativeStencil.DepthStencilPassOperation = ConvertToMetalStencilOperation(descriptor.StencilPassOp);
            nativeStencil.ReadMask = readMask;
            nativeStencil.WriteMask = writeMask;
            return nativeStencil;
        }

        private static MTLStencilOperation ConvertToMetalStencilOperation(in ERHIStencilOp op)
        {
            switch (op)
            {
                case ERHIStencilOp.Zero:
                    return MTLStencilOperation.Zero;
                case ERHIStencilOp.Replace:
                    return MTLStencilOperation.Replace;
                case ERHIStencilOp.IncrementSaturation:
                    return MTLStencilOperation.IncrementClamp;
                case ERHIStencilOp.DecrementSaturation:
                    return MTLStencilOperation.DecrementClamp;
                case ERHIStencilOp.Invert:
                    return MTLStencilOperation.Invert;
                case ERHIStencilOp.Increment:
                    return MTLStencilOperation.IncrementWrap;
                case ERHIStencilOp.Decrement:
                    return MTLStencilOperation.DecrementWrap;
                default:
                    return MTLStencilOperation.Keep;
            }
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }

            if (m_DepthStencilState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_DepthStencilState);
                m_DepthStencilState = default;
            }
        }
    }

    internal sealed class MetalPipelineLibrary : RHIPipelineLibrary
    {
        private readonly MetalDevice m_MetalDevice;
        private MTLBinaryArchive m_NativeBinaryArchive;

        public MetalPipelineLibrary(MetalDevice device, in RHIPipelineLibraryDescriptor descriptor) : base(descriptor)
        {
            m_MetalDevice = device;

            // Create MTLBinaryArchive for pipeline caching
            MTLBinaryArchiveDescriptor archiveDesc = MTLBinaryArchiveDescriptor.New();
            NSError error = default;
            m_NativeBinaryArchive = device.NativeDevice.NewBinaryArchive(archiveDesc, ref error);
            ObjectiveCRuntime.Release(archiveDesc);

            if (m_NativeBinaryArchive.NativePtr == IntPtr.Zero)
            {
                string errorText = error.NativePtr != IntPtr.Zero ? error.LocalizedDescription.ToString() : "unknown error";
                throw new InvalidOperationException($"MetalPipelineLibrary: failed to create MTLBinaryArchive — {errorText}");
            }
        }

        public override void StoreComputePipeline(string name, RHIComputePipeline computePipeline)
        {
            MetalComputePipeline metalPipeline = (MetalComputePipeline)computePipeline;
            MTLComputePipelineDescriptor desc = MTLComputePipelineDescriptor.New();
            desc.Label = new NSString(name);

            NSError error = default;
            m_NativeBinaryArchive.AddComputePipelineFunctions(desc, ref error);
            ObjectiveCRuntime.Release(desc);
        }

        public override void StoreRaytracingPipeline(string name, RHIRaytracingPipeline raytracingPipeline)
        {
            throw new NotSupportedException("MTLBinaryArchive does not support raytracing pipeline serialization.");
        }

        public override void StoreRasterPipeline(string name, RHIRasterPipeline rasterPipeline)
        {
            MetalRasterPipeline metalPipeline = (MetalRasterPipeline)rasterPipeline;
            MTLRenderPipelineDescriptor desc = MTLRenderPipelineDescriptor.New();
            desc.Label = new NSString(name);

            NSError error = default;
            m_NativeBinaryArchive.AddRenderPipelineFunctions(desc, ref error);
            ObjectiveCRuntime.Release(desc);
        }

        public override RHIComputePipeline LoadComputePipeline(RHIComputePipelineDescriptor computePipelineDescriptor)
        {
            // Create compute pipeline with binary archive hint.
            // If cache miss, the pipeline compiles normally and can be stored afterwards.
            return m_MetalDevice.CreateComputePipeline(computePipelineDescriptor);
        }

        public override RHIRaytracingPipeline LoadRaytracingPipeline(RHIRaytracingPipelineDescriptor raytracingPipelineDescriptor)
        {
            throw new NotSupportedException("MTLBinaryArchive does not support raytracing pipeline serialization.");
        }

        public override RHIRasterPipeline LoadRasterPipeline(RHIRasterPipelineDescriptor rasterPipelineDescriptor)
        {
            // Create raster pipeline with binary archive hint.
            // If cache miss, the pipeline compiles normally and can be stored afterwards.
            return m_MetalDevice.CreateRasterPipeline(rasterPipelineDescriptor);
        }

        public override RHIPipelineLibraryResult Serialize()
        {
            // Serialize the binary archive to a temporary URL and read back the bytes
            NSString tempPath = new NSString($"/tmp/metallib_archive_{System.Diagnostics.Process.GetCurrentProcess().Id}.metallib");
            NSURL url = NSURL.FileURLWithPath(tempPath);
            NSError error = default;
            m_NativeBinaryArchive.SerializeToURL(url, ref error);

            if (error.NativePtr != IntPtr.Zero)
            {
                throw new InvalidOperationException($"MetalPipelineLibrary.Serialize: failed — {error.LocalizedDescription}");
            }

            // Read the serialized data
            byte[] data = System.IO.File.ReadAllBytes(tempPath.ToString());
            RHIPipelineLibraryResult result;
            result.ByteSize = (uint)data.Length;
            result.ByteCode = System.Runtime.InteropServices.Marshal.AllocHGlobal(data.Length);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, result.ByteCode, data.Length);
            return result;
        }

        protected override void Release()
        {
            if (m_NativeBinaryArchive.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativeBinaryArchive);
                m_NativeBinaryArchive = default;
            }
        }
    }
    internal sealed class MetalWorkGraphPipeline : RHIWorkGraphPipeline
    {
        internal MetalWorkGraphPipeline(in RHIWorkGraphPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
        }

        protected override void Release()
        {
        }
    }

    internal class MetalMLPipeline : RHIMLPipeline
    {
        internal MTL4MachineLearningPipelineState NativePipelineState => m_NativePipelineState;
        internal MetalMLProgram Program => m_Program;
        internal ulong ReflectionBindingCount => m_ReflectionBindingCount;

        private MTL4MachineLearningPipelineState m_NativePipelineState;
        private readonly MetalMLProgram m_Program;
        private ulong m_ReflectionBindingCount;

        public MetalMLPipeline(MetalDevice device, in RHIMLPipelineDescriptor descriptor)
        {
            m_Descriptor = descriptor;
            m_Program = descriptor.Program as MetalMLProgram
                ?? throw new InvalidOperationException("Metal ML pipeline requires a MetalMLProgram.");
            m_BindingInfos = m_Program.BindingInfos;

            if (!device.SupportsMetal4)
            {
                throw new NotSupportedException(
                    "MetalMLPipeline requires Metal 4 (MTL4MachineLearningPipelineState). " +
                    "The current device does not support Metal 4.");
            }

            MetalFunction mlFunction = m_Program.Function;

            // ── Create MTL4Compiler ──
            NSError compilerError = default;
            MTL4CompilerDescriptor compilerDesc = MTL4CompilerDescriptor.New();
            MTL4Compiler compiler = device.NativeDevice.NewCompiler(compilerDesc, ref compilerError);
            ObjectiveCRuntime.Release(compilerDesc);

            if (compiler.NativePtr == IntPtr.Zero)
            {
                string errorText = compilerError.NativePtr != IntPtr.Zero
                    ? compilerError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4Compiler — {errorText}");
            }

            // ── Build MTL4LibraryFunctionDescriptor from RHIFunction ──
            MTL4LibraryFunctionDescriptor funcDesc = MTL4LibraryFunctionDescriptor.New();
            funcDesc.Library = mlFunction.NativeLibrary;
            funcDesc.Name = new NSString(mlFunction.Descriptor.EntryName);

            // ── Build ML pipeline descriptor ──
            MTL4MachineLearningPipelineDescriptor mlDesc = MTL4MachineLearningPipelineDescriptor.New();
            mlDesc.MachineLearningFunctionDescriptor = funcDesc;
            if (!string.IsNullOrEmpty(descriptor.Name))
            {
                mlDesc.Label = new NSString(descriptor.Name);
            }

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                if (bindingInfo.Kind != ERHIMLTensorBindingKind.Input)
                {
                    continue;
                }

                MTLTensorExtents dimensions = CreateTensorExtents(bindingInfo.Descriptor.Dimensions.Span);
                mlDesc.SetInputDimensions(dimensions, bindingInfo.Index);
                ReleaseNativeObject(dimensions);
            }
            ObjectiveCRuntime.Release(funcDesc);

            // ── Create pipeline state ──
            NSError pipelineError = default;
            m_NativePipelineState = compiler.NewMachineLearningPipelineState(mlDesc, ref pipelineError);
            ObjectiveCRuntime.Release(mlDesc);
            ObjectiveCRuntime.Release(compiler);

            if (m_NativePipelineState.NativePtr == IntPtr.Zero)
            {
                string errorText = pipelineError.NativePtr != IntPtr.Zero
                    ? pipelineError.LocalizedDescription.ToString()
                    : "unknown error";
                throw new InvalidOperationException(
                    $"MetalMLPipeline: failed to create MTL4MachineLearningPipelineState '{descriptor.Name}' — {errorText}");
            }

            MTL4MachineLearningPipelineReflection reflection = m_NativePipelineState.Reflection;
            if (reflection.NativePtr != IntPtr.Zero)
            {
                NSArray reflectionBindings = reflection.Bindings;
                if (reflectionBindings.NativePtr != IntPtr.Zero)
                {
                    m_ReflectionBindingCount = reflectionBindings.Count;
                }
            }

            for (int i = 0; i < m_BindingInfos.Length; ++i)
            {
                ref readonly RHIMLTensorBindingInfo bindingInfo = ref m_BindingInfos[i];
                switch (bindingInfo.Kind)
                {
                    case ERHIMLTensorBindingKind.Input:
                        ++m_InputCount;
                        break;
                    case ERHIMLTensorBindingKind.Output:
                        ++m_OutputCount;
                        break;
                }
            }

            m_TemporaryResourceSize = m_NativePipelineState.IntermediatesHeapSize;
            m_PersistentResourceSize = 0;
        }

        protected override void Release()
        {
            if (m_NativePipelineState.NativePtr != IntPtr.Zero)
            {
                ObjectiveCRuntime.Release(m_NativePipelineState);
                m_NativePipelineState = default;
            }
        }

        private static unsafe MTLTensorExtents CreateTensorExtents(ReadOnlySpan<uint> values)
        {
            if (values.Length > (int)MTLTensorExtents.MaxRank)
            {
                throw new ArgumentOutOfRangeException(nameof(values), $"Metal ML tensors support at most {MTLTensorExtents.MaxRank} dimensions.");
            }

            if (values.Length == 0)
            {
                return MTLTensorExtents.New(0, IntPtr.Zero);
            }

            nint[] nativeValues = new nint[values.Length];
            for (int i = 0; i < values.Length; ++i)
            {
                nativeValues[i] = checked((nint)values[i]);
            }

            fixed (nint* nativeValuesPtr = nativeValues)
            {
                return MTLTensorExtents.New((ulong)nativeValues.Length, (IntPtr)nativeValuesPtr);
            }
        }

        private static void ReleaseNativeObject(IntPtr nativePtr)
        {
            if (nativePtr == IntPtr.Zero)
            {
                return;
            }

            ObjectiveCRuntime.Release(nativePtr);
        }
    }
}
